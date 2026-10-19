using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    /// <summary>Rank promotions from battle experience (docs/GAME_DESIGN.md §11.1).</summary>
    [TestFixture]
    public class PromotionTests
    {
        private GameObject unit;

        [SetUp]
        public void SetUp()
        {
            unit = new GameObject("Recruit", typeof(HealthComponent), typeof(CombatUnit));
            unit.AddComponent<Equipment>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(unit);

        [Test]
        public void Person_BecomesWarriorAt30_WarriorKnightAt100_KnightGeneralAt500()
        {
            UnitProgress p = UnitProgress.Setup(unit, UnitRank.Person);
            p.AddBattleXp(29);
            Assert.AreEqual(UnitRank.Person, p.Rank);
            p.OnHit();
            Assert.AreEqual(UnitRank.Warrior, p.Rank, "30 XP: a fighting person becomes a warrior.");
            Assert.AreEqual(0, p.BattleXp, "Experience counts within the rank.");

            for (int i = 0; i < 9; i++) p.OnKill();
            Assert.AreEqual(UnitRank.Warrior, p.Rank);
            p.OnKill();
            Assert.AreEqual(UnitRank.Knight, p.Rank, "100 XP as a warrior: a knight.");

            p.AddBattleXp(499);
            Assert.AreEqual(UnitRank.Knight, p.Rank);
            p.OnHit();
            Assert.AreEqual(UnitRank.General, p.Rank, "500 XP as a knight: a general.");
            p.AddBattleXp(10000);
            Assert.AreEqual(UnitRank.General, p.Rank, "Generals are the top rank.");
        }

        [Test]
        public void Promotion_TakesTheNewRanksStats_AndKeepsTheShareOfHp()
        {
            UnitProgress p = UnitProgress.Setup(unit, UnitRank.Warrior);
            HealthComponent hp = unit.GetComponent<HealthComponent>();
            hp.SetCurrentHealth(20); // half of 40
            p.Promote();
            Assert.AreEqual(UnitRank.Knight, p.Rank);
            Assert.AreEqual(100, hp.MaxHealth);
            Assert.AreEqual(50, hp.CurrentHealth, "Half the HP before, half after.");
            Assert.AreEqual(10f, unit.GetComponent<CombatUnit>().Strength, 1e-4f);
        }

        [Test]
        public void Survival_IsAwardedOncePerFight()
        {
            UnitProgress p = UnitProgress.Setup(unit, UnitRank.Warrior);
            p.OnFightSurvived();
            p.OnFightSurvived();
            Assert.AreEqual(GameBalance.XpPerSurvival, p.BattleXp, "Killing several targets in one fight is one survival.");
        }

        [Test]
        public void Heroes_NeverChangeRank()
        {
            UnitProgress p = UnitProgress.Setup(unit, UnitRank.Person, hero: true);
            p.AddBattleXp(1000);
            p.Promote();
            Assert.AreEqual(UnitRank.Person, p.Rank, "A hero's rank changes only by death.");
            Assert.AreEqual(40, unit.GetComponent<HealthComponent>().MaxHealth, "Hero person: x2 of 20 HP.");
        }
    }
}
