using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Building by workers (M5 step 2): a site does nothing till it is built, an Auto worker comes by itself, others are sent, and it stops without hands.</summary>
    public class ConstructionTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private Vector3 at;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            WorldSettings.Set(4242, 8000f);
            at = new Vector3(600f, 0f, 600f);
            at.y = RtsUnit.GetTerrainHeight(at);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            WorldSettings.Set(0, 0f);
        }

        private NpcResourceGatherer Worker(Vector3 pos, string name)
        {
            pos.y = RtsUnit.GetTerrainHeight(pos);
            GameObject w = BuildingNode.CreateWorkerNpc(pos, name);
            spawned.Add(w);
            return w.GetComponent<NpcResourceGatherer>();
        }

        private GameObject Mine()
        {
            GameObject m = MineNode.CreateMinePrefab("SiteMine");
            m.transform.position = at;
            spawned.Add(m);
            return m;
        }

        [Test]
        public void ASitesFooting_GoesDownIntoTheGround_OnASlopeToo()
        {
            Mesh stage = ConstructionSite.StageMesh(new Vector3(13f, 0.6f, 13f), 0);
            Assert.Less(stage.bounds.min.y, -3.5f, "it does not hang in the air on its high corner");
            Assert.Greater(stage.bounds.max.y, 0.2f, "and shows above it");
        }

        [Test]
        public void TheWork_IsLongerTheDearerTheBuilding()
        {
            BuildingDefinition house = BuildingData.GetDefinition(BuildingType.WorkerHouse), hall = BuildingData.GetDefinition(BuildingType.TownHall);
            Assert.Greater(ConstructionSite.SecondsFor(hall), ConstructionSite.SecondsFor(house));
            Assert.GreaterOrEqual(ConstructionSite.SecondsFor(house), GameBalance.BuildBaseSeconds);
        }

        [Category("Slow")]
        [UnityTest, Timeout(120000)]
        public IEnumerator AMine_DoesNothing_TillWorkersBuildIt_ByStages()
        {
            GameObject mineObj = Mine();
            MineNode mine = mineObj.GetComponent<MineNode>();
            ConstructionSite site = ConstructionSite.Begin(mineObj, BuildingType.Mine);
            yield return null;
            Assert.IsFalse(mine.enabled, "a site's mine is off");
            Assert.IsFalse(System.Linq.Enumerable.Contains(MineNode.ActiveMines, mine), "and not among the mines");
            foreach (Renderer r in mineObj.GetComponentsInChildren<Renderer>())
                if (r.name != "ConstructionStage") Assert.IsFalse(r.enabled, "its model hidden: " + r.name);
            float before = site.Progress;
            yield return new WaitForSeconds(0.5f);
            if (site.BuilderCount == 0) Assert.AreEqual(before, site.Progress, 1e-4f, "no hands, no work");

            NpcResourceGatherer auto = Worker(at + new Vector3(12f, 0f, 0f), "NPC_Builder");
            Assert.AreEqual(GatherDirective.Auto, auto.CurrentDirective);
            Time.timeScale = 4f;
            float end = Time.time + 60f;
            while (site != null && Time.time < end)
            {
                if (site.Progress > 0.5f && site.Progress < 1f) Assert.Greater(site.BuilderCount, 0, "they stay at the work"); // (workers left by earlier tests may come too; at 1 it has let them go and is destroyed at the end of this frame)
                yield return null;
            }
            Assert.IsTrue(site == null, "built");
            Assert.IsTrue(mine.enabled, "the mine works now");
            Assert.IsTrue(System.Linq.Enumerable.Contains(MineNode.ActiveMines, mine));
            Assert.IsFalse(auto.AtSite, "and the worker goes back to its job");
        }

        private void ClearWorkers()
        {
            foreach (NpcResourceGatherer w in new List<NpcResourceGatherer>(WorkerRegistry.ActiveWorkers)) if (w != null) Object.Destroy(w.gameObject);
        }

        [Category("Slow")]
        [UnityTest, Timeout(120000)]
        public IEnumerator AWideField_IsBuilt_ByAWorkerComingFromAnySide()
        {
            ClearWorkers();
            yield return null;
            foreach (Vector3 side in new[] { new Vector3(-22f, 0f, -22f), new Vector3(22f, 0f, -22f), new Vector3(-22f, 0f, 22f) })
            {
                GameObject fieldObj = BattleOfAges.Utils.ZoneBuildingBuilder.CreateField();
                fieldObj.transform.position = at;
                spawned.Add(fieldObj);
                ConstructionSite site = ConstructionSite.Begin(fieldObj, BuildingType.Field);
                NpcResourceGatherer worker = Worker(at + side, "NPC_FieldBuilder");
                Time.timeScale = 4f;
                float end = Time.time + 80f;
                while (Time.time < end && site != null && site.Progress <= 0f) yield return null;
                Time.timeScale = 1f;
                Assert.IsTrue(site == null || site.Progress > 0f, $"from {side}: the work goes on; the worker at {worker.transform.position}, field at {at}, state {worker.CurrentState}");
                Object.Destroy(fieldObj);
                Object.Destroy(worker.gameObject);
                yield return null;
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ASiteWithNoOne_BorrowsAWorker_FromItsJob_AndGivesItBack()
        {
            ClearWorkers();
            yield return null;
            NpcResourceGatherer chopper = Worker(at + new Vector3(14f, 0f, 3f), "NPC_OnAJob");
            chopper.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            NpcResourceGatherer far = Worker(at + new Vector3(30f, 0f, 3f), "NPC_FreeFurther");
            far.SetGatherDirective(GatherDirective.Idle);
            yield return null;
            ConstructionSite site = ConstructionSite.Begin(Mine(), BuildingType.Mine);
            yield return new WaitForSeconds(GameBalance.BuildBaseSeconds * 0f + 2.5f);
            Assert.IsTrue(chopper.AtSite, "the one on a job comes before a soldier");
            Assert.IsFalse(far.AtSite, "one is what it takes");
            site.Work(site.WorkSeconds + 1f);
            yield return null;
            Assert.AreEqual(GatherDirective.WoodChopTreesOnly, chopper.CurrentDirective, "and back to its job");
            Assert.IsFalse(chopper.AtSite);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator UnderTheCentralEmpire_ItsPeopleStillBuild_AndGoBackToNoWork()
        {
            ClearWorkers();
            yield return null;
            NpcResourceGatherer person = Worker(at + new Vector3(10f, 0f, 3f), "NPC_Subject");
            person.SetGatherDirective(GatherDirective.Idle);
            int epoch = CentralEmpire.WorldEpoch;
            CentralEmpire.SetActive(true, null);
            try
            {
                ConstructionSite site = ConstructionSite.Begin(Mine(), BuildingType.Mine);
                Time.timeScale = 4f;
                float end = Time.time + 60f;
                while (Time.time < end && site != null && site.Progress <= 0f) yield return null;
                Time.timeScale = 1f;
                Assert.IsTrue(site == null || site.Progress > 0f, $"the site goes up: state {person.CurrentState}, at site {person.AtSite}");
                if (site != null) site.Work(site.WorkSeconds + 1f);
                yield return null;
                Assert.AreEqual(GatherDirective.Idle, person.CurrentDirective, "and it is no worker after");
            }
            finally
            {
                Time.timeScale = 1f;
                CentralEmpire.SetActive(false, null);
                CentralEmpire.WorldEpoch = epoch;
            }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TwoSites_EachGetsOne_BeforeEitherGetsASecond()
        {
            ClearWorkers();
            yield return null;
            GameObject second = MineNode.CreateMinePrefab("SiteMine2");
            second.transform.position = at + new Vector3(40f, 0f, 0f);
            spawned.Add(second);
            ConstructionSite a = ConstructionSite.Begin(Mine(), BuildingType.Mine);
            ConstructionSite b = ConstructionSite.Begin(second, BuildingType.Mine);
            NpcResourceGatherer one = Worker(at + new Vector3(4f, 0f, 8f), "NPC_One");
            NpcResourceGatherer two = Worker(at + new Vector3(6f, 0f, 8f), "NPC_Two");
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(1, a.BuilderCount, "one here");
            Assert.AreEqual(1, b.BuilderCount, "one there, though further");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator AtMost_FourBuilders_AndAWorkerOnAJob_ComesOnlyWhenSent()
        {
            GameObject mineObj = Mine();
            ConstructionSite site = ConstructionSite.Begin(mineObj, BuildingType.Mine);
            NpcResourceGatherer chopper = Worker(at + new Vector3(14f, 0f, 3f), "NPC_Chopper");
            chopper.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            NpcResourceGatherer free = Worker(at + new Vector3(12f, 0f, 3f), "NPC_FreeHand"); // (with no one, a site borrows even one on a job)
            yield return new WaitForSeconds(1.2f);
            Assert.IsFalse(chopper.AtSite, "a worker with its own job stays on it while another has come");
            Assert.IsTrue(chopper.AssignToSite(site), "sent there");
            Assert.AreEqual(GathererState.Building, chopper.CurrentState);
            for (int i = 0; i < ConstructionSite.MaxBuilders - 2; i++)
                Assert.IsTrue(Worker(at + new Vector3(16f, 0f, i * 2f), "NPC_B" + i).AssignToSite(site));
            Assert.IsFalse(site.NeedsHands);
            Assert.IsFalse(Worker(at + new Vector3(18f, 0f, 0f), "NPC_Extra").AssignToSite(site), "the fifth is not needed");
            chopper.LeaveSite(null);
            Assert.AreEqual(ConstructionSite.MaxBuilders - 1, site.BuilderCount);
            Assert.IsTrue(site.NeedsHands, "its place is free again");
        }

        [Test]
        public void TheSite_GoesUpByFiveStages_EachKeepingTheOneBefore()
        {
            Assert.AreEqual(0, ConstructionSite.StageOf(0f));
            Assert.AreEqual(ConstructionSite.Stages - 1, ConstructionSite.StageOf(0.99f));
            Assert.AreEqual(ConstructionSite.Stages - 1, ConstructionSite.StageOf(1f));
            Vector3 house = BuildingData.GetDefinition(BuildingType.WorkerHouse).Size;
            int before = 0;
            float topBefore = float.MinValue;
            for (int s = 0; s < ConstructionSite.Stages; s++)
            {
                Mesh m = ConstructionSite.StageMesh(house, s);
                Assert.Greater(m.vertexCount, before, $"stage {s} adds to the one before");
                Assert.GreaterOrEqual(m.bounds.max.y, topBefore - 1e-3f, $"stage {s} is no lower");
                before = m.vertexCount;
                topBefore = m.bounds.max.y;
            }
            Assert.Greater(topBefore, house.y, "the last stage has the rafters over the walls");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator EachBuilder_TakesTheSideItComesFrom_KeepsIt_AndSwingsOnlyThere()
        {
            GameObject house = BuildingNode.CreateWorkerHousePrefab("SiteHouse");
            house.transform.position = at;
            spawned.Add(house);
            ConstructionSite site = ConstructionSite.Begin(house, BuildingType.WorkerHouse);
            Vector3 half = BuildingData.GetDefinition(BuildingType.WorkerHouse).Size * 0.5f;
            var from = new[] { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };
            var crew = new List<NpcResourceGatherer>();
            ClearWorkers();
            yield return null;
            foreach (Vector3 dir in from)
            {
                NpcResourceGatherer w = Worker(at + dir * 14f, "NPC_Side" + crew.Count);
                w.SetGatherDirective(GatherDirective.Idle);
                crew.Add(w);
            }
            yield return null;
            for (int i = 0; i < crew.Count; i++) Assert.IsTrue(crew[i].AssignToSite(site));
            var spots = new Vector3[crew.Count];
            for (int i = 0; i < crew.Count; i++)
            {
                spots[i] = site.SpotFor(crew[i]);
                Vector3 off = spots[i] - at;
                off.y = 0f;
                Assert.Greater(Vector3.Dot(off.normalized, from[i]), 0.9f, $"builder {i} stands on the side it came from, not across the site");
                Assert.Greater(Mathf.Max(Mathf.Abs(off.x) - half.x, Mathf.Abs(off.z) - half.z), 0.5f, "outside the footprint");
            }
            crew[0].LeaveSite(null);
            for (int i = 1; i < crew.Count; i++) Assert.AreEqual(spots[i], site.SpotFor(crew[i]), "the others keep their places when one leaves");

            // On the way none swings; standing at its place each does
            var swung = new int[crew.Count];
            for (int i = 1; i < crew.Count; i++)
            {
                int k = i;
                crew[i].GetComponent<CombatUnit>().OnAttackStarted += () => swung[k]++;
            }
            for (int i = 1; i < crew.Count; i++) Assert.IsFalse(site.AtItsSpot(crew[i]), "14 m off it is still on its way");
            yield return new WaitForSeconds(1.0f);
            int early = 0;
            for (int i = 1; i < crew.Count; i++) if (!site.AtItsSpot(crew[i])) early += swung[i];
            Assert.AreEqual(0, early, "no hammer while walking");
            float end = Time.time + 20f;
            while (Time.time < end)
            {
                bool all = true;
                for (int i = 1; i < crew.Count; i++) all &= site.AtItsSpot(crew[i]);
                if (all) break;
                yield return null;
            }
            for (int i = 1; i < crew.Count; i++) Assert.IsTrue(site.AtItsSpot(crew[i]), $"builder {i} reaches its place");
            for (int i = 1; i < crew.Count; i++) swung[i] = 0;
            yield return new WaitForSeconds(2f);
            for (int i = 1; i < crew.Count; i++)
            {
                Assert.Greater(swung[i], 0, $"builder {i} works the hammer at its place");
                Vector3 look = at - crew[i].transform.position;
                look.y = 0f;
                Assert.Greater(Vector3.Dot(crew[i].transform.forward, look.normalized), 0.8f, $"builder {i} faces the building");
            }
            Assert.Greater(site.Stage, -1);
        }

        [Category("Slow")]
        [UnityTest, Timeout(120000)]
        public IEnumerator FourBuildersFromOneSide_RaiseATownHall_AndDoNotOrbitIt()
        {
            ClearWorkers();
            yield return null;
            GameObject hall = TownHall.CreatePrefab("SiteTownHall");
            hall.transform.position = at;
            spawned.Add(hall);
            ConstructionSite site = ConstructionSite.Begin(hall, BuildingType.TownHall);

            var crew = new List<NpcResourceGatherer>();
            for (int i = 0; i < ConstructionSite.MaxBuilders; i++)
                crew.Add(Worker(at + new Vector3(12f, 0f, -3f + i * 2f), "NPC_HallBuilder_" + i));
            yield return null;

            var swung = new int[crew.Count];
            for (int i = 0; i < crew.Count; i++)
            {
                int k = i;
                crew[i].GetComponent<CombatUnit>().OnAttackStarted += () => swung[k]++;
            }

            Time.timeScale = 4f;
            try
            {
                float joinEnd = Time.time + 8f;
                while (Time.time < joinEnd && site.BuilderCount < ConstructionSite.MaxBuilders) yield return null;
                Assert.AreEqual(ConstructionSite.MaxBuilders, site.BuilderCount, "all four workers join the town hall site");

                float reachEnd = Time.time + 25f;
                while (Time.time < reachEnd)
                {
                    bool all = true;
                    foreach (NpcResourceGatherer b in crew) all &= site.AtItsSpot(b);
                    if (all) break;
                    yield return null;
                }
                for (int i = 0; i < crew.Count; i++)
                    Assert.IsTrue(site.AtItsSpot(crew[i]), $"builder {i} reached its spot at {site.SpotFor(crew[i])}, now at {crew[i].transform.position}");
                Assert.AreEqual(ConstructionSite.MaxBuilders, site.PresentBuilders, "all four builders stand at the town hall");

                for (int i = 0; i < crew.Count; i++) swung[i] = 0;
                yield return new WaitForSeconds(2f);
                for (int i = 0; i < crew.Count; i++)
                    Assert.Greater(swung[i], 0, $"builder {i} swings the hammer at the town hall");

                float buildEnd = Time.time + 60f;
                while (Time.time < buildEnd && site != null) yield return null;
                Assert.IsTrue(site == null, "the town hall finishes building");
            }
            finally
            {
                Time.timeScale = 1f;
            }
        }
    }
}
