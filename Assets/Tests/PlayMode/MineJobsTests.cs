using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The mine and the job window (a player report: miners pulled out, haulers stood about as if gone).</summary>
    public class MineJobsTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(5000f, 0f, -5000f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(400f, 1f, 400f);
            spawned.Add(ground);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        private NpcResourceGatherer Worker(Vector3 at, string name)
        {
            GameObject w = BuildingNode.CreateWorkerNpc(at, name);
            spawned.Add(w);
            return w.GetComponent<NpcResourceGatherer>();
        }

        private MineNode Mine(Vector3 at)
        {
            GameObject m = MineNode.CreateMinePrefab("TestMine");
            m.transform.position = at;
            spawned.Add(m);
            return m.GetComponent<MineNode>();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator JobWindow_LeavesMinersInTheMine()
        {
            MineNode mine = Mine(Far);
            NpcResourceGatherer miner = Worker(Far + new Vector3(4f, 0f, 4f), "NPC_Miner_Test");
            NpcResourceGatherer other = Worker(Far + new Vector3(6f, 0f, 4f), "NPC_Other_Test");
            yield return null;
            miner.EnterMineNow(mine);
            Assert.IsTrue(miner.IsInsideMine);

            WorkerRegistry.ApplyJobDistribution(5, 0, 0, 0, 0); // everyone onto branches
            yield return null;
            Assert.IsTrue(miner.IsInsideMine, "the job window does not pull a miner out of the mine");
            Assert.AreEqual(1, mine.AssignedWorkerCount);
            Assert.AreEqual(GatherDirective.WoodGatherBranchesOnly, other.CurrentDirective, "the others get the jobs");
            Assert.GreaterOrEqual(WorkerRegistry.MinerCount, 1);
        }

        [Category("Slow")]
        [UnityTest, Timeout(120000)]
        public IEnumerator HaulerOnALongRoad_BringsTheOreIn()
        {
            GameObject chest = Resources.StorageChestNode.CreateStorageChestPrefab("TestChest");
            chest.transform.position = Far;
            spawned.Add(chest);
            if (Resources.ResourceManager.Instance == null) spawned.Add(new GameObject("Resources", typeof(Resources.ResourceManager)));
            MineNode mine = Mine(Far + new Vector3(90f, 0f, 0f)); // further than ten seconds' walk
            mine.Produce(50);
            NpcResourceGatherer hauler = Worker(mine.GetOutputDepositPosition() + new Vector3(2f, 0f, 2f), "NPC_Hauler_Test");
            yield return null;
            hauler.SetGatherDirective(GatherDirective.HaulOreFromMineToChest);
            int before = Resources.ResourceManager.Instance.OreCount;
            Time.timeScale = 4f;
            try
            {
                float end = Time.time + 80f;
                while (Time.time < end && Resources.ResourceManager.Instance.OreCount == before) yield return null;
            }
            finally { Time.timeScale = 1f; }
            Assert.Greater(Resources.ResourceManager.Instance.OreCount, before, "the load reached the chest");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator HaulerWithNoOre_WaitsByTheMine()
        {
            MineNode mine = Mine(Far);
            NpcResourceGatherer hauler = Worker(Far + new Vector3(25f, 0f, 25f), "NPC_Hauler_Test");
            yield return null;
            hauler.SetGatherDirective(GatherDirective.HaulOreFromMineToChest);
            Time.timeScale = 3f;
            try
            {
                float end = Time.time + 30f;
                while (Time.time < end && (hauler.transform.position - mine.GetOutputDepositPosition()).sqrMagnitude > 5f * 5f) yield return null;
            }
            finally { Time.timeScale = 1f; }
            Assert.Less(Vector3.Distance(hauler.transform.position, mine.GetOutputDepositPosition()), 5f, "the hauler waits at the mine's door");
        }
    }
}
