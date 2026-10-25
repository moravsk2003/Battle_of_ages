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
        public void ArmourSet_StopsFourFifths_ButNotMagic()
        {
            bag.Add(ArtifactType.Armor90);
            hp.TakeDamage(100);
            Assert.AreEqual(hp.MaxHealth - 20, hp.CurrentHealth, "-80%");
            hp.TakeDamage(100, ignoreArmor: true);
            Assert.AreEqual(hp.MaxHealth - 120, hp.CurrentHealth, "The mage goes through.");
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
        public void FullHeal_GivesAMomentOfInvulnerability()
        {
            bag.Add(ArtifactType.FullHeal);
            hp.SetCurrentHealth(10);
            Assert.IsTrue(bag.Use(ArtifactType.FullHeal));
            Assert.IsTrue(hp.IsInvulnerable);
            Assert.AreEqual(Time.time + GameBalance.FullHealInvulnerableSeconds, hp.InvulnerableUntil, 1e-3f, "for 1 s");
            hp.TakeDamage(400, ignoreArmor: true);
            Assert.AreEqual(hp.MaxHealth, hp.CurrentHealth, "nothing hurts meanwhile, magic neither");
            hp.InvulnerableUntil = Time.time - 0.01f; // the second is over
            hp.TakeDamage(40, ignoreArmor: true);
            Assert.AreEqual(hp.MaxHealth - 40, hp.CurrentHealth, "then it does");
        }

        [Test]
        public void ArtifactSwordAndArmour_CannotBeTakenOffOrReplaced()
        {
            gear.Equip(new Item(ItemType.Sword, 3));
            gear.Equip(new Item(ItemType.Armor, 2));
            Assert.IsFalse(gear.IsLocked(EquipSlot.Weapon), "an ordinary sword comes off");
            bag.Add(ArtifactType.SwordX5);
            bag.Add(ArtifactType.Armor90);
            int damage = combat.AttackDamage;

            Assert.IsTrue(gear.IsLocked(EquipSlot.Weapon));
            Assert.IsTrue(gear.IsLocked(EquipSlot.Chest));
            Assert.IsTrue(gear.Unequip(EquipSlot.Weapon).IsEmpty, "the sword slot gives nothing up");
            Assert.IsTrue(gear.Unequip(EquipSlot.Chest).IsEmpty, "nor the armour slot");
            Item other = new Item(ItemType.Sword, 5);
            Assert.AreEqual(other, gear.Equip(other), "another sword is handed back");
            Assert.AreEqual(new Item(ItemType.Sword, 3), gear[EquipSlot.Weapon]);
            Assert.AreEqual(damage, combat.AttackDamage, "the artifact's damage stays");
            foreach (EquipSlot s in new[] { EquipSlot.Helmet, EquipSlot.Gloves, EquipSlot.Boots }) Assert.IsTrue(gear.IsLocked(s), $"{s}: part of the armour set");
            Assert.IsFalse(gear.IsLocked(EquipSlot.Shield), "the shield is free");
            gear.Equip(new Item(ItemType.Shield, 1));
            Assert.AreEqual(new Item(ItemType.Shield, 1), gear[EquipSlot.Shield], "a shield goes on");
        }

        [Test]
        public void ArmourSet_ReplacesThePieces_AShieldAddsUpToTheCap()
        {
            gear.Equip(new Item(ItemType.Helmet, 5));
            gear.Equip(new Item(ItemType.Armor, 5));
            bag.Add(ArtifactType.Armor90);
            Assert.AreEqual(GameBalance.ArmorArtifactReduction, gear.DamageReduction, 1e-4f, "the set instead of the helmet and chest, not on top");
            gear.Equip(new Item(ItemType.Shield, 2));
            Assert.AreEqual(GameBalance.ArmorArtifactReduction + ItemStatCalculator.DamageReduction(new Item(ItemType.Shield, 2)), gear.DamageReduction, 1e-4f, "a shield adds");
            gear.Equip(new Item(ItemType.Shield, 5));
            Assert.AreEqual(GameBalance.MaxDamageReduction, gear.DamageReduction, 1e-4f, "up to the cap");
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
