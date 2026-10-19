using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    /// <summary>The four hero classes (docs/GAME_DESIGN.md §8.1) on a hero general (500 HP, strength 50).</summary>
    [TestFixture]
    public class HeroClassTests
    {
        private GameObject heroObj;
        private HeroProgress hero;
        private Equipment gear;
        private HealthComponent hp;
        private CombatUnit combat;

        [SetUp]
        public void SetUp()
        {
            heroObj = new GameObject("Hero", typeof(HealthComponent), typeof(CombatUnit));
            gear = heroObj.AddComponent<Equipment>();
            gear.SetBaseStats(500, 50, 2f, 3f);
            UnitProgress.Setup(heroObj, UnitRank.General, hero: true);
            hero = heroObj.AddComponent<HeroProgress>();
            hp = heroObj.GetComponent<HealthComponent>();
            combat = heroObj.GetComponent<CombatUnit>();
            hp.InitializeHealth(hp.MaxHealth); // edit mode: no Awake, start at full health
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(heroObj);

        [Test]
        public void ClassIsPickedOnce()
        {
            Assert.IsTrue(hero.ChooseClass(HeroClass.Tank));
            Assert.IsFalse(hero.ChooseClass(HeroClass.Mage), "The class is chosen once.");
            Assert.AreEqual(HeroClass.Tank, hero.Class);
        }

        [Test]
        public void Tank_Has20PercentMoreHp_AndIgnoresEveryTenthHit()
        {
            hero.ChooseClass(HeroClass.Tank);
            Assert.AreEqual(600, hp.MaxHealth);
            for (int i = 0; i < 9; i++) hp.TakeDamage(10);
            Assert.AreEqual(510, hp.CurrentHealth);
            hp.TakeDamage(10);
            Assert.AreEqual(510, hp.CurrentHealth, "The 10th hit does nothing.");
            hp.TakeDamage(10);
            Assert.AreEqual(500, hp.CurrentHealth);
        }

        [Test]
        public void Archer_DoublesStrengthWithABow_ShootsAsFarAsItRunsIn3s()
        {
            hero.ChooseClass(HeroClass.Archer);
            Assert.AreEqual(400, hp.MaxHealth, "-20% HP.");

            gear.Equip(new Item(ItemType.Sword, 0));
            float swordCooldown = combat.AttackCooldown;
            Assert.AreEqual(50f, combat.Strength, 1e-3f, "A sword: plain strength.");

            gear.Equip(new Item(ItemType.Bow, 1));
            Assert.AreEqual(100f, combat.Strength, 1e-3f, "A bow doubles the archer's strength.");
            float speed = 0.25f * 5f * Mathf.Sqrt(100f / 2f) + 1.5f; // 10.3 m/s
            Assert.AreEqual(speed, combat.MoveSpeed, 1e-3f, "... and through sqrt(strength) the archer runs faster.");
            Assert.AreEqual(speed * 3f, combat.AttackRange, 1e-3f, "Range = the distance run in 3 s.");
            Assert.AreEqual(swordCooldown / 2f * 1.7f / 0.85f, combat.AttackCooldown, 1e-3f,
                "The bow is twice as slow as a sword; the archer's sword is twice as slow again.");
        }

        [Test]
        public void Mage_FiresBolts_WithARangeOfItsRun_NeverUnder12m()
        {
            hero.ChooseClass(HeroClass.Mage);
            Assert.IsTrue(combat.FiresBolts);
            Assert.AreEqual((0.25f * 5f * Mathf.Sqrt(40f / 2f) + 1.5f) * 3f, combat.AttackRange, 1e-3f, "~21 m: the mage runs like strength 40.");
            Assert.AreEqual(0.25f * 5f * Mathf.Sqrt(40f / 2f) + 1.5f, combat.MoveSpeed, 1e-3f, "A hero general mage runs 7.1 m/s.");

            Assert.AreEqual(12f, HeroClassRules.Range(2.5f), 1e-4f, "A hero person runs 7.5 m in 3 s: the minimum 12 m.");
        }

        [Test]
        public void Dd_AttacksTwiceAsFast()
        {
            float before = combat.AttackCooldown;
            hero.ChooseClass(HeroClass.Dd);
            Assert.AreEqual(before / 2f, combat.AttackCooldown, 1e-4f);
        }

        [Test]
        public void AfterAMeleeHit_TheAttackerIsSlowed()
        {
            var victim = new GameObject("Victim", typeof(HealthComponent));
            try
            {
                victim.GetComponent<HealthComponent>().InitializeHealth(100000);
                float speed = combat.MoveSpeed;
                Assert.IsTrue(combat.TryAttack(victim.GetComponent<HealthComponent>()));
                Assert.AreEqual(speed * GameBalance.MeleeSlowFactor, combat.MoveSpeed, 1e-3f, "A melee hit slows the attacker (no hit-and-run).");
            }
            finally { Object.DestroyImmediate(victim); }
        }

        [Test]
        public void AfterARangedShot_TheShooterIsRooted()
        {
            var victim = new GameObject("Victim", typeof(HealthComponent));
            try
            {
                victim.GetComponent<HealthComponent>().InitializeHealth(100000);
                hero.ChooseClass(HeroClass.Mage);
                Assert.IsTrue(combat.IsRanged);
                Assert.IsTrue(combat.TryAttack(victim.GetComponent<HealthComponent>()));
                Assert.AreEqual(0f, combat.MoveSpeed, 1e-5f, "A ranged shot roots the shooter.");
            }
            finally { Object.DestroyImmediate(victim); }
        }

        [Test]
        public void MageStrengthPoints_GoIntoSpells_NotIntoRunning()
        {
            hero.ChooseClass(HeroClass.Mage);
            hero.AddXp(100);
            float speed = combat.MoveSpeed;
            int damage = combat.AttackDamage;
            Assert.IsTrue(hero.SpendOnStrength());
            Assert.AreEqual(speed, combat.MoveSpeed, 1e-4f, "The mage's run speed stays.");
            Assert.Greater(combat.AttackDamage, damage, "Its spells hit harder.");
        }

        [Test]
        public void ArmourIsIgnored_ByAttacksThroughIt()
        {
            hp.DamageReduction = 0.5f;
            hp.TakeDamage(100);
            Assert.AreEqual(450, hp.CurrentHealth, "Armour halves a normal hit.");
            hp.TakeDamage(100, ignoreArmor: true);
            Assert.AreEqual(350, hp.CurrentHealth, "A mage's bolt goes through.");
        }
    }
}
