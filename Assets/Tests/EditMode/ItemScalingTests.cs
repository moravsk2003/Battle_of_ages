using NUnit.Framework;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class ItemScalingTests
    {
        [Test]
        [TestCase(1, 30.0f)]  // 25 * (1 + 0.20 * 1) = 30.0
        [TestCase(2, 35.0f)]  // 25 * (1 + 0.20 * 2) = 35.0
        [TestCase(3, 40.0f)]  // 25 * (1 + 0.20 * 3) = 40.0
        [TestCase(4, 45.0f)]  // 25 * (1 + 0.20 * 4) = 45.0
        [TestCase(5, 50.0f)]  // 25 * (1 + 0.20 * 5) = 50.0
        public void SwordDamage_ScalesBy20PercentTimesLevel(int level, float expectedDamage)
        {
            float damage = ItemStatCalculator.CalculateSwordDamage(level);
            Assert.AreEqual(expectedDamage, damage, 0.001f, $"Sword at level {level} should have {expectedDamage} damage (+20% * Lvl).");
        }

        [Test]
        [TestCase(1, 0.10f)] // 10% reduction
        [TestCase(2, 0.20f)] // 20% reduction
        [TestCase(3, 0.30f)] // 30% reduction
        [TestCase(4, 0.40f)] // 40% reduction
        [TestCase(5, 0.50f)] // 50% reduction
        public void ArmorReduction_ScalesBy10PercentTimesLevel(int level, float expectedReduction)
        {
            float reduction = ItemStatCalculator.CalculateArmorReductionPercent(level);
            Assert.AreEqual(expectedReduction, reduction, 0.001f, $"Armor at level {level} should provide {expectedReduction * 100}% damage reduction (10% * Lvl).");
        }

        [Test]
        [TestCase(1, 24.0f)] // 20 * (1 + 0.20 * 1) = 24.0
        [TestCase(2, 28.0f)] // 20 * (1 + 0.20 * 2) = 28.0
        [TestCase(3, 32.0f)] // 20 * (1 + 0.20 * 3) = 32.0
        [TestCase(4, 36.0f)] // 20 * (1 + 0.20 * 4) = 36.0
        [TestCase(5, 40.0f)] // 20 * (1 + 0.20 * 5) = 40.0
        public void BowDamage_ScalesBy20PercentTimesLevel(int level, float expectedDamage)
        {
            float damage = ItemStatCalculator.CalculateBowDamage(level);
            Assert.AreEqual(expectedDamage, damage, 0.001f, $"Bow at level {level} should have {expectedDamage} damage.");
        }

        [Test]
        [TestCase(1, 1.25f)] // 1.0 * (1 + 0.25 * 1) = 1.25
        [TestCase(2, 1.50f)] // 1.0 * (1 + 0.25 * 2) = 1.50
        [TestCase(3, 1.75f)] // 1.0 * (1 + 0.25 * 3) = 1.75
        [TestCase(4, 2.00f)] // 1.0 * (1 + 0.25 * 4) = 2.00
        [TestCase(5, 2.25f)] // 1.0 * (1 + 0.25 * 5) = 2.25
        public void ToolEfficiency_ScalesBy25PercentTimesLevel(int level, float expectedEfficiency)
        {
            float efficiency = ItemStatCalculator.CalculateToolEfficiency(level);
            Assert.AreEqual(expectedEfficiency, efficiency, 0.001f, $"Tool at level {level} should have {expectedEfficiency} efficiency.");
        }

        [Test]
        public void LevelClamping_ClampsOutOfBoundsCorrectly()
        {
            // Level below 1 clamps to 1 (30.0 damage)
            Assert.AreEqual(30.0f, ItemStatCalculator.CalculateSwordDamage(0), 0.001f);
            Assert.AreEqual(30.0f, ItemStatCalculator.CalculateSwordDamage(-5), 0.001f);

            // Level above 5 clamps to 5 (50.0 damage)
            Assert.AreEqual(50.0f, ItemStatCalculator.CalculateSwordDamage(6), 0.001f);
            Assert.AreEqual(50.0f, ItemStatCalculator.CalculateSwordDamage(100), 0.001f);
        }

        [Test]
        public void ResourceCosts_ScaleMonotonicallyWithLevel()
        {
            for (int lvl = 1; lvl < 5; lvl++)
            {
                int woodCurrent = ItemStatCalculator.CalculateWoodCost(ItemType.Sword, lvl);
                int woodNext = ItemStatCalculator.CalculateWoodCost(ItemType.Sword, lvl + 1);
                Assert.Greater(woodNext, woodCurrent, $"Level {lvl + 1} wood cost should exceed level {lvl}.");

                int oreCurrent = ItemStatCalculator.CalculateOreCost(ItemType.Sword, lvl);
                int oreNext = ItemStatCalculator.CalculateOreCost(ItemType.Sword, lvl + 1);
                Assert.Greater(oreNext, oreCurrent, $"Level {lvl + 1} ore cost should exceed level {lvl}.");
            }
        }

        [Test]
        public void RequiredOreTiers_ScaleWithLevel()
        {
            Assert.AreEqual(0, ItemStatCalculator.CalculateRequiredOreTier(1), "Level 1 requires Tier 0 ore.");
            Assert.AreEqual(5, ItemStatCalculator.CalculateRequiredOreTier(2), "Level 2 requires Tier 5 ore.");
            Assert.AreEqual(10, ItemStatCalculator.CalculateRequiredOreTier(3), "Level 3 requires Tier 10 ore.");
            Assert.AreEqual(15, ItemStatCalculator.CalculateRequiredOreTier(4), "Level 4 requires Tier 15 ore.");
            Assert.AreEqual(20, ItemStatCalculator.CalculateRequiredOreTier(5), "Level 5 requires Tier 20 ore.");
        }
    }
}
