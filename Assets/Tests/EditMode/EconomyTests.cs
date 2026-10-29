using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    /// <summary>
    /// The economy after a tester's town of 4708 ore and 67 wood: workers on their own split by need, and the workbench
    /// melts ore up a tier, so the rare ore is a known amount of digging.
    /// </summary>
    [TestFixture]
    public class EconomyTests
    {
        [Test]
        public void AutoWorkers_SplitByNeed_MostFetchWoodWhileItIsShort()
        {
            Assert.IsFalse(NpcResourceGatherer.AutoShouldHaul(1, 0, true), "a lone worker goes for wood while wood is short");
            Assert.IsTrue(NpcResourceGatherer.AutoShouldHaul(8, 1, true), "a quarter of eight haul: two");
            Assert.IsFalse(NpcResourceGatherer.AutoShouldHaul(8, 2, true));
            Assert.IsTrue(NpcResourceGatherer.AutoShouldHaul(10, 5, false), "with wood enough six of ten haul");
            Assert.IsFalse(NpcResourceGatherer.AutoShouldHaul(10, 6, false));
        }

        [Test]
        public void ASmeltTask_TakesItsTime_AndSaysWhatItMakes()
        {
            var queue = new CraftingQueue();
            queue.SetWorkerCount(1);
            CraftingTask done = null;
            queue.OnTaskCompleted += t => done = t;
            CraftingTask task = queue.EnqueueSmelt(2, 3);
            Assert.IsTrue(task.IsSmelt);
            Assert.AreEqual(3 * GameBalance.SmeltSecondsPerPiece, task.TargetDurationSeconds, 1e-4f);
            StringAssert.Contains("руда II ×" + 3 * GameBalance.SmeltRatio, task.ToString());
            StringAssert.Contains("III ×3", task.ToString());
            queue.Tick(task.TargetDurationSeconds * 0.5f);
            Assert.IsNull(done);
            queue.Tick(task.TargetDurationSeconds);
            Assert.AreSame(task, done);
        }

        /// <summary>
        /// Mines ore in batches (the mine's own roll) and melts the stock up after each: how much digging the town hall IV
        /// (15 ore III+) and V (20 ore IV+) take. Without smelting ~7 500 and ~100 000 pieces: 3 hours of a full mine for V.
        /// </summary>
        [Test]
        public void Smelting_MakesTheRareOre_AKnownAmountOfDigging()
        {
            int hallIV = 0, hallV = 0;
            const int runs = 5;
            for (int seed = 1; seed <= runs; seed++)
            {
                hallIV += MinedUntil(3, GameBalance.TownHallOre[4], seed);
                hallV += MinedUntil(4, GameBalance.TownHallOre[5], seed);
            }
            hallIV /= runs;
            hallV /= runs;
            TestContext.WriteLine($"mined ore with smelting {GameBalance.SmeltRatio}:1 — hall IV {hallIV}, hall V {hallV} (10 miners: {hallIV / 600f:0.#} / {hallV / 600f:0.#} min)");
            Assert.That(hallIV, Is.InRange(1500, 3500), "hall IV: a few minutes of a full mine (7 500 without smelting)");
            Assert.That(hallV, Is.InRange(15000, 35000), "hall V: well under an hour of a full mine (100 000 without)");
        }

        private static int MinedUntil(int tier, int pieces, int seed)
        {
            Random.InitState(seed);
            var stock = new int[OreTierHelper.MAX_TIERS];
            int mined = 0;
            while (mined < 1000000)
            {
                for (int i = 0; i < 100; i++) stock[OreTierHelper.DetermineMineOreTier()]++;
                mined += 100;
                // melt everything below the tier upwards (a copy: the stock keeps growing)
                var melt = (int[])stock.Clone();
                for (int t = 0; t < tier; t++) { melt[t + 1] += melt[t] / GameBalance.SmeltRatio; melt[t] %= GameBalance.SmeltRatio; }
                int have = 0;
                for (int t = tier; t < melt.Length; t++) have += melt[t];
                if (have >= pieces) return mined;
            }
            return mined;
        }
    }
}
