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
        Fast
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
        private const int ReliablePiece = 16 * 1024, ReliableWindow = 64;

        public event Action<int> Connected;
        public event Action<int> Disconnected;
        /// <summary>(peer, channel, bytes): the array is only valid during the call.</summary>
        public event Action<int, NetChannel, byte[], int> Received;

        public bool IsHost { get; private set; }
        public int PeerCount => peers.Count;
        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        private NetworkDriver driver;
        private NetworkPipeline reliable, fast;
        private readonly Dictionary<int, NetworkConnection> peers = new Dictionary<int, NetworkConnection>();
        private readonly List<int> gone = new List<int>();
        private readonly Queue<(int peer, byte[] data)> reliableBacklog = new Queue<(int, byte[])>();
        private int nextPeer = 1;
        private byte[] buffer = new byte[4096];
        private readonly Dictionary<int, System.IO.MemoryStream> assembling = new Dictionary<int, System.IO.MemoryStream>();
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
            FlushBacklog();
        }

        private void Drain(int peer, NetworkConnection connection)
        {
            NetworkEvent.Type type;
            while ((type = driver.PopEventForConnection(connection, out DataStreamReader stream, out NetworkPipeline pipeline)) != NetworkEvent.Type.Empty)
            {
                switch (type)
                {
                    case NetworkEvent.Type.Connect:
                        Connected?.Invoke(peer);
                        break;
                    case NetworkEvent.Type.Disconnect:
                        gone.Add(peer);
                        return;
                    case NetworkEvent.Type.Data:
                        int length = stream.Length;
                        if (buffer.Length < length) buffer = new byte[length * 2];
                        var bytes = new NativeArray<byte>(length, Allocator.Temp);
                        stream.ReadBytes(bytes);
                        NativeArray<byte>.Copy(bytes, buffer, length);
                        bytes.Dispose();
                        BytesReceived += length;
                        if (pipeline == fast) Received?.Invoke(peer, NetChannel.Fast, buffer, length);
                        else Piece(peer, length);
                        break;
                }
            }
        }

        /// <summary>One piece of a reliable message is in the buffer: the whole message goes out when its last piece has come.</summary>
        private void Piece(int peer, int length)
        {
            if (!assembling.TryGetValue(peer, out System.IO.MemoryStream whole)) assembling[peer] = whole = new System.IO.MemoryStream();
            bool more = buffer[0] != 0;
            whole.Write(buffer, 1, length - 1);
            if (more) return;
            Received?.Invoke(peer, NetChannel.Reliable, whole.GetBuffer(), (int)whole.Length);
            whole.SetLength(0);
        }

        /// <summary>Sends a whole message: a reliable one of any size arrives, waiting its turn if the window is full; a fast one is dropped if it cannot go now.</summary>
        public bool Send(int peer, NetChannel channel, byte[] data, int length)
        {
            if (disposed || !peers.ContainsKey(peer)) return false;
            if (channel == NetChannel.Fast) return length <= MaxUnreliableBytes && TrySend(peer, channel, data, length);
            for (int at = 0; at == 0 || at < length; at += ReliablePiece)
            {
                int n = Math.Min(ReliablePiece, length - at);
                var piece = new byte[n + 1];
                piece[0] = at + n < length ? (byte)1 : (byte)0;
                Array.Copy(data, at, piece, 1, n);
                reliableBacklog.Enqueue((peer, piece));
            }
            FlushBacklog();
            return true;
        }

        public void SendAll(NetChannel channel, byte[] data, int length)
        {
            foreach (int peer in peers.Keys) Send(peer, channel, data, length);
        }

        private bool TrySend(int peer, NetChannel channel, byte[] data, int length)
        {
            if (!peers.TryGetValue(peer, out NetworkConnection connection)) return true; // gone: nothing to wait for
            if (driver.BeginSend(channel == NetChannel.Fast ? fast : reliable, connection, out DataStreamWriter writer, length) != 0) return false;
            var bytes = new NativeArray<byte>(length, Allocator.Temp);
            NativeArray<byte>.Copy(data, bytes, length);
            bool wrote = writer.WriteBytes(bytes);
            bytes.Dispose();
            if (!wrote) { driver.AbortSend(writer); return false; }
            if (driver.EndSend(writer) < 0) return false;
            BytesSent += length;
            return true;
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
            driver.Disconnect(connection);
            peers.Remove(peer);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (NetworkConnection connection in peers.Values) if (driver.IsCreated) driver.Disconnect(connection);
            peers.Clear();
            if (driver.IsCreated)
            {
                driver.ScheduleUpdate().Complete(); // the goodbyes go out
                driver.Dispose();
            }
        }
    }
}
