using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Work at the workbench: a called worker walks to its place and stays there crafting (before, its own job took it away).</summary>
    public class BenchWorkTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(-5000f, 0f, 5000f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(400f, 1f, 400f);
            spawned.Add(ground);
            if (ResourceManager.Instance == null) spawned.Add(new GameObject("Resources", typeof(ResourceManager)));
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        private NpcResourceGatherer Worker(Vector3 at, string name)
        {
            GameObject w = BuildingNode.CreateWorkerNpc(at, name);
            spawned.Add(w);
            return w.GetComponent<NpcResourceGatherer>();
        }

        private IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end && !done()) yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ACalledWorker_StaysAtTheBench_Smelts_AndAnotherOrderTakesItOff()
        {
            // Earlier tests' free workers would bring their own ore to the one stock this test counts
            foreach (NpcResourceGatherer left in new List<NpcResourceGatherer>(WorkerRegistry.ActiveWorkers)) if (left != null) Object.Destroy(left.gameObject);
            foreach (MineNode left in new List<MineNode>(MineNode.ActiveMines)) if (left != null) Object.Destroy(left.gameObject);
            yield return null;
            GameObject benchObj = WorkbenchNode.CreateWorkbenchPrefab("TestBench");
            benchObj.transform.position = Far;
            spawned.Add(benchObj);
            WorkbenchNode bench = benchObj.GetComponent<WorkbenchNode>();
            NpcResourceGatherer worker = Worker(Far + new Vector3(9f, 0f, 7f), "NPC_Smith_Test");
            NpcResourceGatherer other = Worker(Far + new Vector3(-9f, 0f, 7f), "NPC_Other_Test");
            RtsUnit unit = worker.GetComponent<RtsUnit>();
            ResourceManager rm = ResourceManager.Instance;
            rm.SetState(0, new[] { new KeyValuePair<int, int>(0, 25) });
            yield return null;

            Assert.IsTrue(bench.AssignWorker(unit));
            Assert.IsTrue(worker.AtBench);
            Assert.AreEqual(0, bench.AssignedWorkerCount, "still on the way: not crafting yet");
            Assert.IsTrue(bench.TryEnqueueSmelt(0, 2));
            Assert.AreEqual(5, rm.GetOreTierCount(0), "the ore is taken when queued");
            Assert.IsFalse(bench.TryEnqueueSmelt(0, 1), "five is not enough for one more");

            Time.timeScale = 3f;
            yield return Until(() => bench.AssignedWorkerCount == 1, 40f);
            Assert.AreEqual(1, bench.AssignedWorkerCount, "at its place it crafts");
            Assert.AreEqual(GathererState.WorkingAtBench, worker.CurrentState);

            yield return Until(() => rm.GetOreTierCount(1) >= 2, 20f);
            Assert.AreEqual(2, rm.GetOreTierCount(1), "20 ore 0 melted into 2 of tier I");

            WorkerRegistry.ApplyJobDistribution(5, 0, 0, 0, 0); // everyone onto branches
            yield return new WaitForSeconds(3f);
            Assert.IsTrue(worker.AtBench, "the job window leaves a smith at the bench");
            Assert.AreEqual(1, bench.AssignedWorkerCount, "and it stays at its place");
            Assert.AreEqual(GatherDirective.WoodGatherBranchesOnly, other.CurrentDirective);

            worker.SetGatherDirective(GatherDirective.Auto); // another order
            Assert.IsFalse(worker.AtBench);
            Assert.AreEqual(0, bench.CalledWorkerCount, "another order calls it off the bench");
            yield return null;
            Assert.AreEqual(0, bench.AssignedWorkerCount);
        }
    }
}
