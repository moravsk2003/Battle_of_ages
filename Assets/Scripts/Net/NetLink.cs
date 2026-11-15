using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

namespace BattleOfAges.Net
{
    public enum NetChannel : byte
    {
        /// <summary>Arrives, in order, whatever its size: commands, joining, the world's changes.</summary>
        Reliable,
        /// <summary>May be lost, the newest wins: snapshots of units. At most MaxUnreliableBytes a message.</summary>
        Fast,
        /// <summary>
        /// Arrives, in order, on a reliable line of its own: big things that may take seconds (a guest's save). Fed to the wire
        /// at BulkBytesPerSecond in small pieces, so it never holds Reliable up, not even while its lost packets are resent.
        /// </summary>
        Bulk
    }

    /// <summary>
    /// The wire (M8 step 2, docs/GAME_DESIGN.md §14.2): a host listening on a port, players connecting by IP, over Unity Transport.
    /// Messages are whole byte arrays on one of two channels. It knows nothing of the game; Poll on the main thread, each frame.
    /// </summary>
    public sealed class NetLink : IDisposable
    {
        public const ushort DefaultPort = 7777;
        public const int MaxUnreliableBytes = 1200;
        /// <summary>
        /// A reliable message goes in pieces this size, each with one byte saying whether more follow: the transport's window is
        /// 64 fragments, and a single message larger than that would never fit it and would stop the queue for good.
        /// </summary>
        private const int ReliablePiece = 16 * 1024, ReliableWindow = 64, BulkPiece = 4 * 1024;

        /// <summary>
        /// How fast Bulk goes out: a tenth of a weak upload (1 Mbit/s), gameplay keeps the rest (§14.4; NetSession.GuestSaveInterval
        /// spaces the guest's saves by the same figure).
        /// </summary>
        public const int BulkBytesPerSecond = 12800;

        public event Action<int> Connected;
        public event Action<int> Disconnected;
        /// <summary>(peer, channel, bytes): the array is only valid during the call.</summary>
        public event Action<int, NetChannel, byte[], int> Received;

        public bool IsHost { get; private set; }
        public int PeerCount => peers.Count;
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        private NetworkDriver driver;
        private NetworkPipeline reliable, fast, bulk;
        private readonly Dictionary<int, NetworkConnection> peers = new Dictionary<int, NetworkConnection>();
        private readonly List<int> gone = new List<int>();
        /// <summary>Connections let go: what they still send is read and dropped till they are gone (left in the queue, the
        /// transport logged "Resetting event queue with pending events" when they went).</summary>
        private readonly List<NetworkConnection> leaving = new List<NetworkConnection>();
        private readonly Queue<(int peer, byte[] data)> reliableBacklog = new Queue<(int, byte[])>();
        private readonly Queue<(int peer, byte[] data)> bulkBacklog = new Queue<(int, byte[])>();
        private double bulkAllowance = BulkPiece + 1;
        private long bulkTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        private int nextPeer = 1;
        private readonly Dictionary<(int, NetChannel), System.IO.MemoryStream> assembling = new Dictionary<(int, NetChannel), System.IO.MemoryStream>();
        private bool disposed;

        private NetLink() { }

        private void Create()
        {
            var settings = new NetworkSettings();
            settings.WithFragmentationStageParameters(payloadCapacity: ReliablePiece + 16);
            settings.WithReliableStageParameters(windowSize: ReliableWindow);
            driver = NetworkDriver.Create(settings);
            reliable = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
            fast = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
            bulk = driver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage)); // its own sequence and resends
        }

        /// <summary>Starts listening; null when the port cannot be taken.</summary>
        public static NetLink Host(ushort port = DefaultPort)
        {
            var link = new NetLink { IsHost = true };
            link.Create();
            NetworkEndpoint at = NetworkEndpoint.AnyIpv4.WithPort(port);
            if (link.driver.Bind(at) != 0 || link.driver.Listen() != 0) { link.Dispose(); return null; }
            return link;
        }

        /// <summary>Starts connecting to a host; null when the address cannot be read. Connected comes (peer 0) when it answers.</summary>
        public static NetLink Join(string address, ushort port = DefaultPort)
        {
            if (!NetworkEndpoint.TryParse(address, port, out NetworkEndpoint to)) return null;
            var link = new NetLink();
            link.Create();
            link.peers[0] = link.driver.Connect(to);
            return link;
        }

        /// <summary>Moves the wire on and hands out what has come.</summary>
        public void Poll()
        {
            if (disposed) return;
            driver.ScheduleUpdate().Complete();
            if (IsHost)
            {
                NetworkConnection incoming;
                while ((incoming = driver.Accept()) != default)
                {
                    int id = nextPeer++;
                    peers[id] = incoming;
                    Connected?.Invoke(id);
                }
            }
            gone.Clear();
            foreach (KeyValuePair<int, NetworkConnection> p in peers) Drain(p.Key, p.Value);
            foreach (int id in gone) if (peers.Remove(id)) Disconnected?.Invoke(id);
            for (int i = leaving.Count - 1; i >= 0; i--)
            {
                Discard(leaving[i]);
                if (driver.GetConnectionState(leaving[i]) == NetworkConnection.State.Disconnected) leaving.RemoveAt(i);
            }
            FlushBacklog();
            FlushBulk();
        }

        /// <summary>Bulk pieces waiting to go (tests).</summary>
        public int BulkWaiting => bulkBacklog.Count;

        private readonly List<(NetworkEvent.Type type, NetworkPipeline pipeline, byte[] data)> drained = new List<(NetworkEvent.Type, NetworkPipeline, byte[])>();

        /// <summary>
        /// Reads what came for one peer. A player gone and another come in the same update share the transport's number for a
        /// connection: the one gone can no longer read its queue, so its last data and its goodbye turn up in the newcomer's.
        /// A goodbye on a connection still up is such a stranger's: it and all before it are dropped (taken for the newcomer's,
        /// it lost the newcomer, kept the one gone as a ghost, and the newcomer's data stayed unread: "Resetting event queue").
        /// A connection down is gone once its queue is read (its last data, a guest's save going away, is still delivered).
        /// </summary>
        private void Drain(int peer, NetworkConnection connection)
        {
            drained.Clear();
            int from = 0;
            NetworkEvent.Type type;
            while ((type = driver.PopEventForConnection(connection, out DataStreamReader stream, out NetworkPipeline pipeline)) != NetworkEvent.Type.Empty)
            {
                if (type == NetworkEvent.Type.Disconnect && driver.GetConnectionState(connection) != NetworkConnection.State.Disconnected)
                {
                    from = drained.Count + 1; // a stranger's goodbye: it and what came before it are not this peer's
                    drained.Add((type, pipeline, null));
                    continue;
                }
                byte[] data = null;
                if (type == NetworkEvent.Type.Data)
                {
                    data = new byte[stream.Length];
                    var bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
                    stream.ReadBytes(bytes);
                    NativeArray<byte>.Copy(bytes, data, data.Length);
                    bytes.Dispose();
                }
                drained.Add((type, pipeline, data));
            }
            for (int i = from; i < drained.Count; i++)
            {
                (NetworkEvent.Type kind, NetworkPipeline line, byte[] data) = drained[i];
                if (kind == NetworkEvent.Type.Connect) Connected?.Invoke(peer);
                else if (kind == NetworkEvent.Type.Disconnect) { gone.Add(peer); return; }
                else if (kind == NetworkEvent.Type.Data)
                {
                    BytesReceived += data.Length;
                    if (line == fast) Received?.Invoke(peer, NetChannel.Fast, data, data.Length);
                    else Piece(peer, line == bulk ? NetChannel.Bulk : NetChannel.Reliable, data);
                }
            }
            if (driver.GetConnectionState(connection) == NetworkConnection.State.Disconnected) gone.Add(peer); // its goodbye went to the one after it
        }

        /// <summary>One piece of a reliable message: the whole message goes out when its last piece has come.</summary>
        private void Piece(int peer, NetChannel channel, byte[] piece)
        {
            if (!assembling.TryGetValue((peer, channel), out System.IO.MemoryStream whole)) assembling[(peer, channel)] = whole = new System.IO.MemoryStream();
            bool more = piece[0] != 0;
            whole.Write(piece, 1, piece.Length - 1);
            if (more) return;
            Received?.Invoke(peer, channel, whole.GetBuffer(), (int)whole.Length);
            whole.SetLength(0);
        }

        /// <summary>Sends a whole message: a reliable one of any size arrives, waiting its turn if the window is full; a fast one is dropped if it cannot go now.</summary>
        public bool Send(int peer, NetChannel channel, byte[] data, int length)
        {
            if (disposed || !peers.ContainsKey(peer)) return false;
            if (channel == NetChannel.Fast) return length <= MaxUnreliableBytes && TrySend(peer, channel, data, length);
            int size = channel == NetChannel.Bulk ? BulkPiece : ReliablePiece;
            Queue<(int, byte[])> queue = channel == NetChannel.Bulk ? bulkBacklog : reliableBacklog;
            for (int at = 0; at == 0 || at < length; at += size)
            {
                int n = Math.Min(size, length - at);
                var piece = new byte[n + 1];
                piece[0] = at + n < length ? (byte)1 : (byte)0;
                Array.Copy(data, at, piece, 1, n);
                queue.Enqueue((peer, piece));
            }
            if (channel == NetChannel.Bulk) FlushBulk(); else FlushBacklog();
            return true;
        }

        /// <summary>To everyone but one (a host passing on what a player sent).</summary>
        public void SendOthers(int except, NetChannel channel, byte[] data, int length)
        {
            foreach (int peer in peers.Keys) if (peer != except) Send(peer, channel, data, length);
        }

        public void SendAll(NetChannel channel, byte[] data, int length)
        {
            foreach (int peer in peers.Keys) Send(peer, channel, data, length);
        }

        private bool TrySend(int peer, NetChannel channel, byte[] data, int length)
        {
            if (!peers.TryGetValue(peer, out NetworkConnection connection)) return true; // gone: nothing to wait for
            NetworkPipeline line = channel == NetChannel.Fast ? fast : channel == NetChannel.Bulk ? bulk : reliable;
            if (driver.BeginSend(line, connection, out DataStreamWriter writer, length) != 0) return false;
            var bytes = new NativeArray<byte>(length, Allocator.Temp);
            NativeArray<byte>.Copy(data, bytes, length);
            bool wrote = writer.WriteBytes(bytes);
            bytes.Dispose();
            if (!wrote) { driver.AbortSend(writer); return false; }
            if (driver.EndSend(writer) < 0) return false;
            BytesSent += length;
            return true;
        }

        /// <summary>Bulk pieces as the pace allows (no burst over a piece after a quiet spell).</summary>
        private void FlushBulk()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            bulkAllowance = Math.Min(bulkAllowance + (now - bulkTicks) / (double)System.Diagnostics.Stopwatch.Frequency * BulkBytesPerSecond, BulkPiece + 1);
            bulkTicks = now;
            while (bulkBacklog.Count > 0)
            {
                (int peer, byte[] data) = bulkBacklog.Peek();
                if (bulkAllowance < data.Length || !TrySend(peer, NetChannel.Bulk, data, data.Length)) return;
                bulkAllowance -= data.Length;
                bulkBacklog.Dequeue();
            }
        }

        private void FlushBacklog()
        {
            while (reliableBacklog.Count > 0)
            {
                (int peer, byte[] data) = reliableBacklog.Peek();
                if (!TrySend(peer, NetChannel.Reliable, data, data.Length)) return;
                reliableBacklog.Dequeue();
            }
        }

        /// <summary>Lets one peer go (a host), or the host (a player).</summary>
        public void Drop(int peer)
        {
            if (disposed || !peers.TryGetValue(peer, out NetworkConnection connection)) return;
            Discard(connection);
            driver.Disconnect(connection);
            peers.Remove(peer);
            leaving.Add(connection);
        }

        private void Discard(NetworkConnection connection)
        {
            while (driver.PopEventForConnection(connection, out DataStreamReader _, out NetworkPipeline _) != NetworkEvent.Type.Empty) { }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (NetworkConnection connection in peers.Values) if (driver.IsCreated) { Discard(connection); driver.Disconnect(connection); }
            peers.Clear();
            if (driver.IsCreated)
            {
                driver.ScheduleUpdate().Complete(); // the goodbyes go out
                driver.Dispose();
            }
        }
    }
}
