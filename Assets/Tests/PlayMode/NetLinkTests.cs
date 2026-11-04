using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Net;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>M8 step 2: a host and a player on this machine, over a real socket: joining, both channels, big messages, leaving, a command across.</summary>
    public class NetLinkTests
    {
        private const ushort Port = 27777;
        private NetLink host, client;

        [TearDown]
        public void Teardown()
        {
            client?.Dispose();
            host?.Dispose();
            client = host = null;
        }

        private IEnumerator Until(System.Func<bool> done, float seconds = 5f)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < end)
            {
                host?.Poll();
                client?.Poll();
                yield return null;
            }
        }

        private IEnumerator Connect()
        {
            host = NetLink.Host(Port);
            Assert.IsNotNull(host, "the port is free");
            client = NetLink.Join("127.0.0.1", Port);
            Assert.IsNotNull(client);
            bool hostSaw = false, clientSaw = false;
            host.Connected += _ => hostSaw = true;
            client.Connected += _ => clientSaw = true;
            yield return Until(() => hostSaw && clientSaw);
            Assert.IsTrue(hostSaw, "the host saw it join");
            Assert.IsTrue(clientSaw, "the player knows it is in");
        }

        [Test]
        public void ABadAddress_IsSaid_NotThrown()
        {
            Assert.IsNull(NetLink.Join("not an address"));
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator BothChannels_Carry_BothWays()
        {
            yield return Connect();
            var atHost = new List<string>();
            var atClient = new List<string>();
            host.Received += (_, ch, b, n) => atHost.Add(ch + ":" + Encoding.UTF8.GetString(b, 0, n));
            client.Received += (_, ch, b, n) => atClient.Add(ch + ":" + Encoding.UTF8.GetString(b, 0, n));

            byte[] order = Encoding.UTF8.GetBytes("наказ");
            client.Send(0, NetChannel.Reliable, order, order.Length);
            byte[] snapshot = Encoding.UTF8.GetBytes("знімок");
            host.SendAll(NetChannel.Fast, snapshot, snapshot.Length);
            yield return Until(() => atHost.Count > 0 && atClient.Count > 0);
            CollectionAssert.Contains(atHost, "Reliable:наказ");
            CollectionAssert.Contains(atClient, "Fast:знімок");
            Assert.Greater(client.BytesSent, 0);
            Assert.Greater(client.BytesReceived, 0);
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator AHundredKilobytes_AndManyInARow_ArriveWholeInOrder()
        {
            yield return Connect();
            var big = new byte[100 * 1024];
            for (int i = 0; i < big.Length; i++) big[i] = (byte)(i * 31);
            int bigSeen = 0;
            var numbers = new List<int>();
            client.Received += (_, ch, b, n) =>
            {
                if (n == big.Length)
                {
                    for (int i = 0; i < n; i++) if (b[i] != big[i]) { bigSeen = -1; return; }
                    bigSeen = 1;
                }
                else numbers.Add(System.BitConverter.ToInt32(b, 0));
            };
            host.SendAll(NetChannel.Reliable, big, big.Length);
            // More than a reliable window at once: the rest wait their turn
            for (int i = 0; i < 300; i++)
            {
                byte[] one = System.BitConverter.GetBytes(i);
                host.SendAll(NetChannel.Reliable, one, one.Length);
            }
            yield return Until(() => bigSeen != 0 && numbers.Count == 300, 15f);
            Assert.AreEqual(1, bigSeen, "the big one, every byte");
            Assert.AreEqual(300, numbers.Count, "and all the small ones");
            for (int i = 0; i < 300; i++) Assert.AreEqual(i, numbers[i], "in order");
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator APlayerLeaving_IsNoticed()
        {
            yield return Connect();
            Assert.AreEqual(1, host.PeerCount);
            bool left = false;
            host.Disconnected += _ => left = true;
            client.Dispose();
            client = null;
            yield return Until(() => left);
            Assert.IsTrue(left, "the host knows");
            Assert.AreEqual(0, host.PeerCount);
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator ACommand_GoesAcross_AndRunsAtTheHost()
        {
            yield return Connect();
            GameCommand arrived = null;
            host.Received += (_, ch, b, n) => arrived = CommandCodec.Decode(Encoding.UTF8.GetString(b, 0, n));
            var sent = new PaintZoneCommand { From = Vector3.one, To = Vector3.up, Radius = 8f, Zone = World.ZoneType.Mining, Issuer = 2 };
            byte[] text = Encoding.UTF8.GetBytes(CommandCodec.Encode(sent));
            client.Send(0, NetChannel.Reliable, text, text.Length);
            yield return Until(() => arrived != null);
            Assert.IsInstanceOf<PaintZoneCommand>(arrived);
            Assert.AreEqual(2, arrived.Issuer);
            Assert.AreEqual(World.ZoneType.Mining, ((PaintZoneCommand)arrived).Zone);
        }
    }
}
