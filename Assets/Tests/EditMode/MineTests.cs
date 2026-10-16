using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class MineTests
    {
        private GameObject mineObj;
        private MineNode mine;

        [SetUp]
        public void Setup()
        {
            mineObj = new GameObject("TestMine");
            mine = mineObj.AddComponent<MineNode>();
        }

        [TearDown]
        public void Teardown()
        {
            Object.DestroyImmediate(mineObj);
        }

        [Test]
        public void Produce_StopsAtCapacity_AndKeepsTierCounts()
        {
            mine.Produce(2000);
            Assert.AreEqual(mine.MaxAccumulatedOre, mine.AccumulatedOreCount, "The buffer holds at most its capacity.");
            int sum = 0;
            for (int t = 0; t <= MineNode.MAX_MINED_TIER; t++) sum += mine.OreOfTier(t);
            Assert.AreEqual(mine.AccumulatedOreCount, sum);
        }

        [Test]
        public void MineOreTier_IsEightyPercentTierZero_EighteenPercentTierOne()
        {
            Random.InitState(1234);
            const int N = 100000;
            var counts = new int[BattleOfAges.Resources.OreTierHelper.MAX_TIERS];
            for (int i = 0; i < N; i++) counts[BattleOfAges.Resources.OreTierHelper.DetermineMineOreTier()]++;
            Assert.AreEqual(0.80f, counts[0] / (float)N, 0.01f);
            Assert.AreEqual(0.18f, counts[1] / (float)N, 0.01f);
            Assert.AreEqual(0.018f, counts[2] / (float)N, 0.003f);
            Assert.Greater(counts[3], 0, "Higher tiers keep going (0.18% tier 3).");
        }

        [Test]
        public void TakeOre_TakesTheBestTierFirst()
        {
            mine.Produce(400);
            int best = mine.HighestTierInStock;
            int taken = mine.TakeOre(3, out int tier);
            Assert.AreEqual(best, tier);
            Assert.Greater(taken, 0);
        }

        [Test]
        public void UnassignWorker_WhoIsNotInside_KeepsTheCount()
        {
            var a = new GameObject("WorkerA", typeof(CharacterController)).AddComponent<NpcResourceGatherer>();
            var b = new GameObject("WorkerB", typeof(CharacterController)).AddComponent<NpcResourceGatherer>();
            Assert.IsTrue(mine.AssignWorker(a));
            Assert.AreEqual(1, mine.AssignedWorkerCount);
            Assert.IsFalse(mine.UnassignWorker(b), "A worker still walking to the mine is not counted.");
            Assert.AreEqual(1, mine.AssignedWorkerCount);
            Assert.IsTrue(mine.UnassignWorker(a));
            Assert.AreEqual(0, mine.AssignedWorkerCount);
            Object.DestroyImmediate(a.gameObject);
            Object.DestroyImmediate(b.gameObject);
        }
    }
}
