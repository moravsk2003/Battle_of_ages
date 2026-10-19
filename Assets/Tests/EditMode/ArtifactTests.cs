using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    /// <summary>The first artifacts and how they stack (docs/GAME_DESIGN.md §9.1.1).</summary>
    [TestFixture]
    public class ArtifactTests
    {
        private GameObject heroObj;
        private HeroArtifacts bag;
        private HealthComponent hp;
        private CombatUnit combat;
        private Equipment gear;

        [SetUp]
        public void SetUp()
        {
            heroObj = new GameObject("Hero", typeof(HealthComponent), typeof(CombatUnit));
            gear = heroObj.AddComponent<Equipment>();
            UnitProgress.Setup(heroObj, UnitRank.General, hero: true);
            heroObj.AddComponent<HeroProgress>();
            bag = heroObj.AddComponent<HeroArtifacts>();
            hp = heroObj.GetComponent<HealthComponent>();
            hp.InitializeHealth(hp.MaxHealth);
            combat = heroObj.GetComponent<CombatUnit>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(heroObj);

        [Test]
        public void StartingArtifact_IsChosenOnce()
        {
            Assert.IsTrue(bag.ChooseStart(ArtifactType.Wisdom));
            Assert.IsFalse(bag.ChooseStart(ArtifactType.SwordX5));
            Assert.AreEqual(1, bag.Held.Count);
        }

        [Test]
        public void SwordX5_TakesTheWeaponSlot()
        {
            gear.Equip(new Item(ItemType.Sword, 5)); // x2
            Assert.AreEqual(100, combat.AttackDamage);
            bag.Add(ArtifactType.SwordX5);
            Assert.AreEqual(250, combat.AttackDamage, "Strength 50 x 5: the artifact replaces the worn sword's x2.");
        }

        [Test]
        public void Armor90_StopsNineTenths_ButNotMagic()
        {
            bag.Add(ArtifactType.Armor90);
            hp.TakeDamage(100);
            Assert.AreEqual(hp.MaxHealth - 10, hp.CurrentHealth);
            hp.TakeDamage(100, ignoreArmor: true);
            Assert.AreEqual(hp.MaxHealth - 110, hp.CurrentHealth, "The mage goes through.");
        }

        [Test]
        public void Vampiric_HealsTenPercentOfTheDamageDealt_AndStacksWithTheSword()
        {
            bag.Add(ArtifactType.SwordX5);
            bag.Add(ArtifactType.Vampiric);
            hp.SetCurrentHealth(100);
            var victim = new GameObject("Victim", typeof(HealthComponent));
            try
            {
                victim.GetComponent<HealthComponent>().InitializeHealth(1000);
                combat.DeliverHit(victim.GetComponent<HealthComponent>(), combat.AttackDamage, ignoreArmor: false);
                Assert.AreEqual(100 + 25, hp.CurrentHealth, "10% of 250 = 25 HP a hit.");
            }
            finally { Object.DestroyImmediate(victim); }
        }

        [Test]
        public void Wisdom_GivesTwentyPercentMoreXp()
        {
            bag.Add(ArtifactType.Wisdom);
            heroObj.GetComponent<HeroProgress>().AddXp(50);
            Assert.AreEqual(60, heroObj.GetComponent<HeroProgress>().Xp);
        }

        [Test]
        public void FullHeal_IsUsedOnce()
        {
            bag.Add(ArtifactType.FullHeal);
            hp.SetCurrentHealth(10);
            Assert.IsTrue(bag.Use(ArtifactType.FullHeal));
            Assert.AreEqual(hp.MaxHealth, hp.CurrentHealth);
            Assert.IsFalse(bag.Has(ArtifactType.FullHeal), "Gone after use.");
            Assert.IsFalse(bag.Use(ArtifactType.FullHeal));
        }

        [Test]
        public void TwoShards_MakeOneOfTheirArtifacts()
        {
            bag.AddShard(ArtifactType.Wisdom);
            Assert.IsFalse(bag.CombineShards(), "Two shards are needed.");
            bag.AddShard(ArtifactType.Vampiric);
            Assert.IsTrue(bag.CombineShards());
            Assert.AreEqual(0, bag.Shards.Count);
            Assert.AreEqual(1, bag.Held.Count);
            Assert.That(bag.Held[0] == ArtifactType.Wisdom || bag.Held[0] == ArtifactType.Vampiric);
        }
    }
}
