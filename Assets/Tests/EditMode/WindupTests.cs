using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    /// <summary>Attacks land after their wind-up; a started swing lands even if its striker falls (§11.1).</summary>
    [TestFixture]
    public class WindupTests
    {
        private GameObject strikerObj, victimObj;
        private CombatUnit striker;
        private HealthComponent victim;

        [SetUp]
        public void SetUp()
        {
            PendingHits.FlushAll();
            strikerObj = new GameObject("Striker", typeof(HealthComponent), typeof(CombatUnit));
            strikerObj.AddComponent<Equipment>();
            UnitProgress.Setup(strikerObj, UnitRank.Knight);
            strikerObj.GetComponent<HealthComponent>().InitializeHealth(100);
            striker = strikerObj.GetComponent<CombatUnit>();
            victimObj = new GameObject("Victim", typeof(HealthComponent));
            victimObj.transform.position = new Vector3(1.5f, 0f, 0f);
            victim = victimObj.GetComponent<HealthComponent>();
            victim.InitializeHealth(1000);
        }

        [TearDown]
        public void TearDown()
        {
            PendingHits.FlushAll();
            Object.DestroyImmediate(strikerObj);
            Object.DestroyImmediate(victimObj);
        }

        [Test]
        public void Hit_LandsAfterTheWindup_NotAtOnce()
        {
            Assert.IsTrue(striker.TryAttack(victim));
            Assert.AreEqual(1000, victim.CurrentHealth, "Nothing at the start of the swing.");
            PendingHits.Tick(Time.time + GameBalance.MeleeWindup - 0.05f);
            Assert.AreEqual(1000, victim.CurrentHealth, "Still swinging.");
            PendingHits.Tick(Time.time + GameBalance.MeleeWindup + 0.01f);
            Assert.AreEqual(1000 - striker.AttackDamage, victim.CurrentHealth, "The hit lands at the end of the wind-up.");
        }

        [Test]
        public void StartedSwing_LandsEvenIfTheStrikerFalls()
        {
            Assert.IsTrue(striker.TryAttack(victim));
            strikerObj.GetComponent<HealthComponent>().TakeDamage(100000);
            striker.enabled = false; // as a corpse is
            PendingHits.FlushAll();
            Assert.AreEqual(1000 - striker.AttackDamage, victim.CurrentHealth, "A trade: the swing was already on its way.");
        }

        [Test]
        public void TargetThatGotAway_IsNotHit()
        {
            Assert.IsTrue(striker.TryAttack(victim));
            victimObj.transform.position = new Vector3(12f, 0f, 0f);
            PendingHits.FlushAll();
            Assert.AreEqual(1000, victim.CurrentHealth);
        }

        [Test]
        public void Windups_SwordBowCast()
        {
            Assert.AreEqual(GameBalance.MeleeWindup, striker.Windup);
            striker.FiresBolts = true;
            Assert.AreEqual(GameBalance.MageWindup, striker.Windup);
            striker.FiresBolts = false;
            striker.InitializeCombatStats(10, 2f, 12f); // a bow's reach
            Assert.AreEqual(GameBalance.BowWindup, striker.Windup);
        }
    }
}
