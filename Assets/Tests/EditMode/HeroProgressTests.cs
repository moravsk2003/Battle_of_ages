using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    /// <summary>Hero levels from kills, attribute and skill points (docs/GAME_DESIGN.md §8).</summary>
    [TestFixture]
    public class HeroProgressTests
    {
        private GameObject heroObj;
        private HeroProgress hero;

        [SetUp]
        public void SetUp()
        {
            heroObj = new GameObject("Hero", typeof(HealthComponent), typeof(CombatUnit));
            heroObj.AddComponent<Equipment>();
            UnitProgress.Setup(heroObj, UnitRank.Warrior, hero: true);
            hero = heroObj.AddComponent<HeroProgress>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(heroObj);

        [Test]
        public void LevelUp_Costs100TimesTheCurrentLevel_AndGivesOnePointOfEach()
        {
            Assert.AreEqual(1, hero.Level, "A hero starts at level 1.");
            hero.AddXp(99);
            Assert.AreEqual(1, hero.Level);
            hero.AddXp(1);
            Assert.AreEqual(2, hero.Level, "Level 1 -> 2 costs 100.");
            Assert.AreEqual(200, hero.XpToNextLevel, "Level 2 -> 3 costs 200.");
            hero.AddXp(200 + 300 + 50);
            Assert.AreEqual(4, hero.Level);
            Assert.AreEqual(50, hero.Xp, "The rest carries over.");
            Assert.AreEqual(3, hero.AttributePoints);
            Assert.AreEqual(3, hero.SkillPoints);
        }

        [Test]
        public void AttributePoint_IsFiveStrengthOrFiftyHp()
        {
            hero.AddXp(100 + 200);
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent hp = heroObj.GetComponent<HealthComponent>();
            Assert.AreEqual(8f, combat.Strength, 1e-4f, "Hero warrior: x2 of strength 4.");
            Assert.AreEqual(80, hp.MaxHealth);

            Assert.IsTrue(hero.SpendOnStrength());
            Assert.AreEqual(13f, combat.Strength, 1e-4f, "+5 strength.");
            Assert.IsTrue(hero.SpendOnHealth());
            Assert.AreEqual(130, hp.MaxHealth, "+50 HP.");
            Assert.IsFalse(hero.SpendOnHealth(), "No points left.");
        }

        [Test]
        public void KillXp_ByVictimRank_HeroesByTheirLevel()
        {
            var victim = new GameObject("Victim", typeof(HealthComponent), typeof(CombatUnit));
            victim.AddComponent<Equipment>();
            try
            {
                UnitProgress.Setup(victim, UnitRank.Person);
                Assert.AreEqual(2, HeroProgress.KillXp(victim));
                UnitProgress.Setup(victim, UnitRank.Knight);
                Assert.AreEqual(10, HeroProgress.KillXp(victim));

                UnitProgress.Setup(victim, UnitRank.General, hero: true);
                HeroProgress enemyHero = victim.AddComponent<HeroProgress>();
                enemyHero.AddXp(100 + 200 + 300 + 400 + 500 + 600 + 700 + 800 + 900);
                Assert.AreEqual(10, enemyHero.Level);
                Assert.AreEqual(50 * 11, HeroProgress.KillXp(victim), "A level 10 hero general: 50 x (1 + 10).");
            }
            finally { Object.DestroyImmediate(victim); }
        }
    }
}
