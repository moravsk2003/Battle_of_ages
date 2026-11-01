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
        public void TheWork_IsLongerTheDearerTheBuilding()
        {
            BuildingDefinition house = BuildingData.GetDefinition(BuildingType.WorkerHouse), hall = BuildingData.GetDefinition(BuildingType.TownHall);
            Assert.Greater(ConstructionSite.SecondsFor(hall), ConstructionSite.SecondsFor(house));
            Assert.GreaterOrEqual(ConstructionSite.SecondsFor(house), GameBalance.BuildBaseSeconds);
        }

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

        [UnityTest, Timeout(60000)]
        public IEnumerator AtMost_FourBuilders_AndAWorkerOnAJob_ComesOnlyWhenSent()
        {
            GameObject mineObj = Mine();
            ConstructionSite site = ConstructionSite.Begin(mineObj, BuildingType.Mine);
            NpcResourceGatherer chopper = Worker(at + new Vector3(14f, 0f, 3f), "NPC_Chopper");
            chopper.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            yield return new WaitForSeconds(1.2f);
            Assert.IsFalse(chopper.AtSite, "a worker with its own job stays on it");
            Assert.IsTrue(chopper.AssignToSite(site), "sent there");
            Assert.AreEqual(GathererState.Building, chopper.CurrentState);
            for (int i = 0; i < ConstructionSite.MaxBuilders - 1; i++)
                Assert.IsTrue(Worker(at + new Vector3(16f, 0f, i * 2f), "NPC_B" + i).AssignToSite(site));
            Assert.IsFalse(site.NeedsHands);
            Assert.IsFalse(Worker(at + new Vector3(18f, 0f, 0f), "NPC_Extra").AssignToSite(site), "the fifth is not needed");
            chopper.LeaveSite(null);
            Assert.AreEqual(ConstructionSite.MaxBuilders - 1, site.BuilderCount);
            Assert.IsTrue(site.NeedsHands, "its place is free again");
        }
    }
}
