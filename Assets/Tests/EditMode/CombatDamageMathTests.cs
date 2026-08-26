using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Building;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class CombatDamageMathTests
    {
        [Test]
        [TestCase(100, 1, 90)] // Lvl 1: 10% reduction -> 90 dmg
        [TestCase(100, 2, 80)] // Lvl 2: 20% reduction -> 80 dmg
        [TestCase(100, 3, 70)] // Lvl 3: 30% reduction -> 70 dmg
        [TestCase(100, 4, 60)] // Lvl 4: 40% reduction -> 60 dmg
        [TestCase(100, 5, 50)] // Lvl 5: 50% reduction -> 50 dmg
        public void ArmorDamageMitigation_ReducesDamageBy10PercentPerArmorLevel(int rawDamage, int armorLevel, int expectedDamage)
        {
            int effectiveDamage = ItemStatCalculator.CalculateEffectiveDamageAgainstArmorLevel(rawDamage, armorLevel);
            Assert.AreEqual(expectedDamage, effectiveDamage, $"Armor level {armorLevel} should mitigate {armorLevel * 10}% of raw damage.");
        }

        [Test]
        public void DamageReduction_ZeroArmorReceivesFullDamage()
        {
            int rawDamage = 60;
            int effectiveDamage = ItemStatCalculator.CalculateEffectiveDamageAgainstArmorLevel(rawDamage, 0);
            Assert.AreEqual(60, effectiveDamage, "Level 0 armor receives full unmitigated damage.");
        }

        [Test]
        public void DamageReduction_MinimumOneDamageFloor()
        {
            int rawDamage = 1;
            int effectiveDamage = ItemStatCalculator.CalculateEffectiveDamageAgainstArmorLevel(rawDamage, 5);
            Assert.AreEqual(1, effectiveDamage, "Damage calculation must maintain a minimum floor of 1 damage.");
        }

        [Test]
        public void HighGround_AttackingFromAboveDeals10PercentBonusDamage()
        {
            int baseDamage = 50;
            float attackerY = 3.5f; // On top of castle wall
            float targetY = 0.5f;   // On ground level below

            int highGroundDamage = ItemStatCalculator.CalculateDamageWithHighGround(baseDamage, attackerY, targetY);
            // 50 * 1.10 = 55 damage (+10%)
            Assert.AreEqual(55, highGroundDamage, "Attacking from high ground (> 1.2m elevation) must grant +10% bonus damage.");

            // Same level (no bonus)
            int flatDamage = ItemStatCalculator.CalculateDamageWithHighGround(baseDamage, 1.0f, 1.0f);
            Assert.AreEqual(50, flatDamage, "Attacking on the same elevation level should not grant high ground bonus.");
        }

        [Test]
        public void HighGround_ComboWithSwordLevel5()
        {
            // Sword Lvl 5 base damage: 50
            int swordLvl5Dmg = Mathf.RoundToInt(ItemStatCalculator.CalculateSwordDamage(5));
            Assert.AreEqual(50, swordLvl5Dmg);

            // Attacking from wall top (3.5m) against ground (0.5m) -> 50 * 1.10 = 55
            int comboDamage = ItemStatCalculator.CalculateDamageWithHighGround(swordLvl5Dmg, 3.5f, 0.5f);
            Assert.AreEqual(55, comboDamage, "Sword Lvl 5 + High ground should deal 55 total damage.");
        }

        [Test]
        public void HealthComponent_TakeDamageReducesHealthAndTriggersDeath()
        {
            GameObject testObj = new GameObject("TestHealthEntity");
            HealthComponent hp = testObj.AddComponent<HealthComponent>();
            hp.InitializeHealth(100);

            bool deathFired = false;
            hp.OnDeath += () => deathFired = true;

            hp.TakeDamage(40);
            Assert.AreEqual(60, hp.CurrentHealth);
            Assert.IsTrue(hp.IsAlive);
            Assert.IsFalse(deathFired);

            hp.TakeDamage(70);
            Assert.AreEqual(0, hp.CurrentHealth);
            Assert.IsFalse(hp.IsAlive);
            Assert.IsTrue(deathFired);

            Object.DestroyImmediate(testObj);
        }

        [Test]
        public void HealthComponent_HealRestoresHealthUpToMax()
        {
            GameObject testObj = new GameObject("TestHealthEntity");
            HealthComponent hp = testObj.AddComponent<HealthComponent>();
            hp.InitializeHealth(100);

            hp.TakeDamage(50);
            Assert.AreEqual(50, hp.CurrentHealth);

            hp.Heal(30);
            Assert.AreEqual(80, hp.CurrentHealth);

            hp.Heal(50);
            Assert.AreEqual(100, hp.CurrentHealth, "Heal must not exceed MaxHealth.");

            Object.DestroyImmediate(testObj);
        }

        [Test]
        public void CastleWall_DestroysOnZeroHP()
        {
            GameObject wall = BuildingNode.CreateCastleWallPrefab("TestCastleWall");
            BuildingNode bNode = wall.GetComponent<BuildingNode>();
            HealthComponent hp = wall.GetComponent<HealthComponent>();

            Assert.IsNotNull(bNode, "Castle wall must have BuildingNode component.");
            Assert.IsNotNull(hp, "Castle wall must have HealthComponent component.");
            Assert.AreEqual(600, hp.MaxHealth, "Castle wall must have 600 base HP.");

            // Lethal damage
            hp.TakeDamage(600);
            Assert.AreEqual(0, hp.CurrentHealth);
            Assert.IsFalse(hp.IsAlive);

            Object.DestroyImmediate(wall);
        }
    }
}
