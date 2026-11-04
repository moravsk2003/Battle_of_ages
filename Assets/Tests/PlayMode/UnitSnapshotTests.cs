using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Net;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>M8 step 3: units through a real socket: the player sees them where they are, as hurt as they are, gone when they fall, at the size the wire can take.</summary>
    public class UnitSnapshotTests
    {
        private const ushort Port = 27778;
        private static readonly Vector3 Far = new Vector3(5100f, 0f, -5100f);
        private NetLink host, client;
        private RemoteUnits remote;

        [TearDown]
        public void Teardown()
        {
            client?.Dispose();
            host?.Dispose();
            client = host = null;
            if (remote != null) Object.Destroy(remote.gameObject);
            NpcKingdom.RemoveAll();
        }

        [Test]
        public void ARow_ComesBackAsItWent()
        {
            var row = new UnitRow { Id = 4097, X = 12345, Z = 54321, Yaw = 200, Health = 150, Rank = 2, Team = 7 };
            var bytes = new byte[UnitRow.Bytes];
            row.Write(bytes, 0);
            UnitRow back = UnitRow.Read(bytes, 0);
            Assert.AreEqual(row, back);
            Assert.AreEqual(100f, UnitSnapshotWriter.Unquantize(UnitSnapshotWriter.Quantize(100f)), 0.3f, "a quarter of a metre");
            Assert.AreEqual(-7000f, UnitSnapshotWriter.Unquantize(UnitSnapshotWriter.Quantize(-7000f)), 0.3f);
        }

        private IEnumerator Connect()
        {
            host = NetLink.Host(Port);
            client = NetLink.Join("127.0.0.1", Port);
            bool joined = false;
            host.Connected += _ => joined = true;
            remote = new GameObject("RemoteUnitsTest").AddComponent<RemoteUnits>();
            remote.WithModels = false;
            client.Received += (_, channel, data, length) => { if (channel == NetChannel.Fast) remote.Apply(data, length); };
            float end = Time.realtimeSinceStartup + 5f;
            while (!joined && Time.realtimeSinceStartup < end) { host.Poll(); client.Poll(); yield return null; }
            Assert.IsTrue(joined);
        }

        private IEnumerator Run(UnitSnapshotWriter writer, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                host.Poll();
                writer.Tick(host, Time.realtimeSinceStartup);
                client.Poll();
                yield return null;
            }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ThePlayer_SeesTheUnits_WhereTheyAre_AndLosesThemWhenTheyFall()
        {
            yield return Connect();
            NpcKingdom k = NpcKingdom.Found(Far, "Видимі", 0);
            k.enabled = false;
            HostileMobAI one = k.AddSubject(UnitRank.Knight, k.Home + new Vector3(6f, 0f, 0f));
            HostileMobAI two = k.AddSubject(UnitRank.Warrior, k.Home + new Vector3(-6f, 0f, 3f));
            yield return null;
            var writer = new UnitSnapshotWriter();
            yield return Run(writer, 1f);

            int idOne = one.GridId, idTwo = two.GridId;
            Assert.IsTrue(remote.TryGet(0, (ushort)idOne, out Vector3 seen, out int share), "it has come");
            Vector3 off = seen - one.transform.position;
            off.y = 0f;
            Assert.Less(off.magnitude, 1f, "where it is");
            Assert.AreEqual(200, share, "whole");

            one.Health.TakeDamage(one.Health.MaxHealth / 2, ignoreArmor: true);
            yield return Run(writer, 0.6f);
            Assert.IsTrue(remote.TryGet(0, (ushort)idOne, out _, out share));
            Assert.AreEqual(100f, share, 5f, "hurt as it is hurt");

            two.Health.TakeDamage(int.MaxValue / 2, ignoreArmor: true);
            yield return Run(writer, 1f);
            Assert.IsFalse(remote.TryGet(0, (ushort)idTwo, out _, out _), "fallen, it is gone");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator UnitsAtRest_CostLittle_AndAMovingCrowd_FitsThePackets()
        {
            yield return Connect();
            var crowd = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < 300; i++)
            {
                // At rest: no AI, only a place in the grid
                var go = new GameObject("Crowd_" + i, typeof(HealthComponent), typeof(CombatUnit));
                go.transform.position = Far + new Vector3(i % 20 * 2f, 0f, 300f + i / 20 * 2f);
                HealthComponent hp = go.GetComponent<HealthComponent>();
                hp.InitializeHealth(100);
                UnitSpatialGrid.Register(go.GetComponent<CombatUnit>(), hp, UnitSpatialGrid.TeamKingdomFirst, 0.4f);
                crowd.Add(go);
            }
            yield return null;
            var writer = new UnitSnapshotWriter();
            yield return Run(writer, 1f);
            Assert.GreaterOrEqual(remote.Count, 300, "a crowd bigger than one packet has come whole");
            Assert.Greater(300, UnitSnapshotWriter.RowsPerPacket);

            int before = writer.RowsSent;
            yield return Run(writer, 1f);
            int quiet = writer.RowsSent - before;
            foreach (GameObject go in crowd) { go.GetComponent<HealthComponent>().TakeDamage(int.MaxValue / 2, ignoreArmor: true); Object.Destroy(go); }
            Assert.Less(quiet, 60, "at rest it says next to nothing");
        }
    }
}
