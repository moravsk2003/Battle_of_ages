using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>What a kingdom has round its hall (KingdomTown), its training and the ring in the units' way.</summary>
    public class KingdomTownTests
    {
        private static readonly Vector3 Site = new Vector3(4000f, 0f, -3000f);

        [TearDown]
        public void Teardown()
        {
            Time.timeScale = 1f;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
        }

        private static NpcKingdom Kingdom(int hall, int people)
        {
            NpcKingdom k = NpcKingdom.Found(Site, "Тестове місто", 0, -1, HeroClass.Tank);
            k.Economy.HallLevel = hall;
            for (int i = k.Population; i < people; i++) k.AddSubject(UnitRank.Person, Site + Vector3.right * 6f);
            return k;
        }

        [Test]
        public void HowMuchStands_FollowsPeopleAndHall()
        {
            Assert.AreEqual(0, KingdomTown.Wanted(KingdomTown.Kind.House, 0, 5), "no hall, no town");
            Assert.AreEqual(1, KingdomTown.Wanted(KingdomTown.Kind.House, 1, 2));
            Assert.AreEqual(10, KingdomTown.Wanted(KingdomTown.Kind.House, 2, 30));
            Assert.AreEqual(0, KingdomTown.Wanted(KingdomTown.Kind.Mine, 1, 30), "mines from hall II");
            Assert.AreEqual(1, KingdomTown.Wanted(KingdomTown.Kind.Mine, 2, 30));
            Assert.AreEqual(0, KingdomTown.Wanted(KingdomTown.Kind.Wall, 2, 30), "walls from hall III");
            Assert.Greater(KingdomTown.Wanted(KingdomTown.Kind.Wall, 3, 30), 0);
        }

        [UnityTest]
        public IEnumerator TheSameName_RaisesTheSameTown_AndTheRingCloses()
        {
            NpcKingdom k = Kingdom(3, 30);
            k.Town.RaiseAllNow();
            yield return null;
            Assert.AreEqual(10, k.Town.Standing(KingdomTown.Kind.House));
            Assert.AreEqual(3, k.Town.Standing(KingdomTown.Kind.Field));
            Assert.AreEqual(2, k.Town.Standing(KingdomTown.Kind.Mine));

            var places = k.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("TownHouse")).Select(t => t.position).ToArray();
            var walls = k.GetComponentsInChildren<Transform>().Where(t => t.name == "TownWall").ToArray();
            Assert.Greater(walls.Length, 60, "most of the ring stands");

            // Each segment reaches the next: its length along the ring's tangent, nothing to see between them
            foreach (Transform w in walls)
            {
                Vector3 offCentre = w.position - k.Home;
                offCentre.y = 0f;
                Assert.AreEqual(0f, Vector3.Dot(w.right, offCentre.normalized), 0.01f, "along the ring, not across it");
                Assert.AreEqual(KingdomTown.WallRingRadius, offCentre.magnitude, 0.2f);
            }

            NpcKingdom.RemoveAll();
            yield return null;
            NpcKingdom again = Kingdom(3, 30);
            again.Town.RaiseAllNow();
            var placesAgain = again.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("TownHouse")).Select(t => t.position).ToArray();
            CollectionAssert.AreEqual(places, placesAgain, "where it stands comes from the name");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AUnit_GoesThroughTheWay_NotThroughTheWall()
        {
            NpcKingdom k = Kingdom(3, 2);
            k.Town.RaiseAllNow();
            yield return null;
            Vector3 home = k.Home;
            float r = KingdomTown.WallRingRadius;
            // Inside, by a stretch of wall half-way between two ways, heading straight out across it
            Vector3 Point(float degrees, float radius)
            {
                float a = degrees * Mathf.Deg2Rad;
                Vector3 p = home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
                return p;
            }
            GameObject unit = Building.BuildingNode.CreateWorkerNpc(Point(45f, r - 6f), "NPC_TownWalker");
            NpcResourceGatherer job = unit.GetComponent<NpcResourceGatherer>();
            job.SetGatherDirective(GatherDirective.Idle);
            yield return null;
            RtsUnit walker = unit.GetComponent<RtsUnit>();
            Vector3 goal = Point(45f, r + 12f);
            walker.IssueMoveCommand(goal);

            Time.timeScale = 4f;
            float maxDeepInWall = float.MaxValue, end = Time.time + 150f;
            while (Time.time < end && walker.HasMoveTarget)
            {
                Vector3 off = unit.transform.position - home;
                off.y = 0f;
                float angle = Mathf.Atan2(off.z, off.x) * Mathf.Rad2Deg;
                bool atAWay = Mathf.Abs(Mathf.DeltaAngle(angle, Mathf.Round(angle / 90f) * 90f)) < 9f;
                if (!atAWay) maxDeepInWall = Mathf.Min(maxDeepInWall, Mathf.Abs(off.magnitude - r));
                yield return null;
            }
            Vector3 left = unit.transform.position - goal;
            left.y = 0f;
            Object.Destroy(unit);
            Assert.Less(left.magnitude, 3f, "it got to the far side");
            Assert.Greater(maxDeepInWall, WallBuilder.Thickness * 0.5f - 0.1f, "and never stood in the wall");
        }

        [UnityTest]
        public IEnumerator TheMinimapPoints_ToTheNearestEnemyKingdom_NotToAFreeBand()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            NpcKingdom near = NpcKingdom.Found(new Vector3(1500f, 0f, 0f), "Ближній", 0);
            NpcKingdom.Found(new Vector3(0f, 0f, 3000f), "Дальній", 0);
            NpcKingdom.Found(new Vector3(300f, 0f, 0f), "Ватага", 0, -1, HeroClass.Tank, -1, free: true);
            yield return null;
            Assert.IsTrue(KingdomRealm.NearestEnemy(Vector3.zero, out string name, out Vector3 at));
            Assert.AreEqual("Ближній", name);
            Assert.AreEqual(near.Home.x, at.x, 0.01f);
            WorldSettings.Set(0, 0f);
        }

        [Test]
        public void Training_RaisesWarriorsOfAFarKingdom_ByItsHall()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            int Knights(int hall)
            {
                KingdomState s = KingdomRealm.NewState(new Vector3(3000f, 0f, -2000f), "Навчання", false, 5, new System.Random(3));
                s.Hall = hall;
                s.Subjects.Clear();
                for (int i = 0; i < 400; i++) s.Subjects.Add(new UnitState { Rank = (int)UnitRank.Warrior });
                s.Wood = 0;
                KingdomRealm.Advance(s, 24f);
                return s.Subjects.Count(u => u.Rank >= (int)UnitRank.Knight);
            }
            int low = Knights(1), high = Knights(5);
            WorldSettings.Set(0, 0f);
            TestContext.WriteLine($"knights of 400 in a day: hall I {low}, hall V {high}");
            Assert.Greater(low, 0, "a hall I trains some");
            Assert.Greater(high, low * 2, "a hall V trains more");
            Assert.Less(high, 200, "not everyone at once");
        }
    }
}
