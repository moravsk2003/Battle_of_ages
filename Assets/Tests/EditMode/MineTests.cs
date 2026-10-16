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
        public void Produce_RollsTiersUpToTwo_AndStopsAtCapacity()
        {
            mine.Produce(2000);
            Assert.AreEqual(mine.MaxAccumulatedOre, mine.AccumulatedOreCount, "The buffer holds at most its capacity.");
            Assert.AreEqual(mine.AccumulatedOreCount, mine.OreOfTier(0) + mine.OreOfTier(1) + mine.OreOfTier(2));
            Assert.Greater(mine.OreOfTier(0), mine.OreOfTier(1), "Tier 0 is the common ore.");
            Assert.Greater(mine.OreOfTier(1), 0, "About one piece in ten is tier I (500 pieces).");
        }

        [Test]
        public void TakeOre_TakesTheBestTierFirst()
        {
            mine.Produce(400);
            int best = mine.OreOfTier(2) > 0 ? 2 : mine.OreOfTier(1) > 0 ? 1 : 0;
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
