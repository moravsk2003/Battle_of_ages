using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    /// <summary>Class skills learnt with skill points (docs/GAME_DESIGN.md §8.1).</summary>
    [TestFixture]
    public class HeroSkillsTests
    {
        private GameObject heroObj;
        private HeroProgress hero;
        private HeroSkills skills;
        private readonly List<GameObject> extra = new List<GameObject>();
        private readonly List<int> gridIds = new List<int>();

        [SetUp]
        public void SetUp()
        {
            heroObj = new GameObject("Hero", typeof(HealthComponent), typeof(CombatUnit));
            heroObj.AddComponent<Equipment>();
            UnitProgress.Setup(heroObj, UnitRank.General, hero: true);
            hero = heroObj.AddComponent<HeroProgress>();
            skills = heroObj.AddComponent<HeroSkills>();
            heroObj.GetComponent<HealthComponent>().InitializeHealth(500);
            hero.AddXp(100 * (1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9)); // level 10: 9 skill points
        }

        [TearDown]
        public void TearDown()
        {
            foreach (int id in gridIds) UnitSpatialGrid.Unregister(id);
            gridIds.Clear();
            foreach (GameObject g in extra) Object.DestroyImmediate(g);
            extra.Clear();
            Object.DestroyImmediate(heroObj);
        }

        [Test]
        public void TankRegeneration_StartsWithBothSteps_ThenOneStepPerPoint()
        {
            Assert.IsFalse(skills.LearnRegenStart(), "Not a tank yet.");
            hero.ChooseClass(HeroClass.Tank);
            Assert.IsFalse(skills.LearnRegenMax(), "The start comes first.");
            Assert.IsTrue(skills.LearnRegenStart());
            Assert.AreEqual(0.01f, skills.RegenMaxShare, 1e-5f);
            Assert.AreEqual(0.02f, skills.RegenCurrentShare, 1e-5f);
            skills.LearnRegenMax(); skills.LearnRegenMax(); skills.LearnRegenCurrent();
            Assert.AreEqual(0.03f, skills.RegenMaxShare, 1e-5f);
            Assert.AreEqual(0.04f, skills.RegenCurrentShare, 1e-5f);
            HealthComponent hp = heroObj.GetComponent<HealthComponent>();
            Assert.AreEqual(0.03f, hp.RegenMaxPerHour, 1e-5f, "The health component heals with it.");
            Assert.AreEqual(9 - 4, hero.SkillPoints);
        }

        [Test]
        public void TankInstantHeal_Is5PercentPlus4PerLevel_UpTo25()
        {
            hero.ChooseClass(HeroClass.Tank);
            Assert.IsFalse(skills.LearnHeal(), "The heal is the second skill: Regeneration first.");
            Assert.IsTrue(skills.LearnRegenStart());
            Assert.IsFalse(skills.LearnHeal(), "... and a point in one of its branches.");
            Assert.IsTrue(skills.LearnRegenCurrent());
            float[] expected = { 0.05f, 0.09f, 0.13f, 0.17f, 0.21f, 0.25f };
            foreach (float share in expected)
            {
                Assert.IsTrue(skills.LearnHeal());
                Assert.AreEqual(share, skills.HealShare, 1e-5f);
            }
            Assert.IsFalse(skills.LearnHeal(), "25% is the top.");

            HealthComponent hp = heroObj.GetComponent<HealthComponent>();
            hp.SetCurrentHealth(100);
            Assert.IsTrue(skills.UseActive(Vector3.forward));
            Assert.AreEqual(100 + Mathf.RoundToInt(hp.MaxHealth * 0.25f), hp.CurrentHealth);
            Assert.IsFalse(skills.UseActive(Vector3.forward), "Then it recharges for 12 minutes.");
        }

        [Test]
        public void ArcherLongShot_CostsTwoPoints_AndAdds5PercentRange()
        {
            hero.ChooseClass(HeroClass.Archer);
            heroObj.GetComponent<Equipment>().Equip(new Item(ItemType.Bow, 1));
            float before = heroObj.GetComponent<CombatUnit>().AttackRange;
            Assert.IsTrue(skills.LearnLongShot());
            Assert.AreEqual(9 - 2, hero.SkillPoints);
            Assert.AreEqual(before * 1.05f, heroObj.GetComponent<CombatUnit>().AttackRange, 1e-3f);
            Assert.IsFalse(skills.LearnLongShot(), "Learnt once.");
        }

        [Test]
        public void DdDash_IsTimesTwoThenPlusOnePerPoint_UpToFive()
        {
            hero.ChooseClass(HeroClass.Dd);
            for (int i = 0; i < 4; i++) Assert.IsTrue(skills.LearnDash());
            Assert.AreEqual(5f, skills.DashFactor, 1e-5f);
            Assert.IsFalse(skills.LearnDash());
        }

        [Test]
        public void MageFireFan_HitsTheCone_LessAtTheEdge()
        {
            hero.ChooseClass(HeroClass.Mage);
            Assert.IsTrue(skills.LearnFireFan());
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            float range = combat.AttackRange;

            HealthComponent near = Enemy(new Vector3(0f, 0f, 1f));
            HealthComponent edge = Enemy(new Vector3(0f, 0f, range * 0.99f));
            HealthComponent aside = Enemy(new Vector3(5f, 0f, 1f)); // ~79° off: outside the 60° fan
            HealthComponent behind = Enemy(new Vector3(0f, 0f, -3f));

            Assert.AreEqual(2, skills.CastFireFan(Vector3.forward));
            int full = combat.AttackDamage;
            Assert.AreEqual(1000 - full, near.CurrentHealth, 1, "Close by: about the full damage.");
            Assert.AreEqual(1000 - Mathf.RoundToInt(full * 0.5f), edge.CurrentHealth, 2, "At the edge: half.");
            Assert.AreEqual(1000, aside.CurrentHealth);
            Assert.AreEqual(1000, behind.CurrentHealth);
        }

        [Test]
        public void ArcherPiercingArrow_NeedsLongShot_And10To50PercentOfArmour()
        {
            hero.ChooseClass(HeroClass.Archer);
            Assert.IsFalse(skills.LearnPierce(), "Long shot first.");
            Assert.IsTrue(skills.LearnLongShot());
            Assert.IsTrue(skills.LearnPierce());
            Assert.AreEqual(0.10f, skills.PierceShare, 1e-5f);
            for (int i = 0; i < 6; i++) skills.LearnPierce();
            Assert.AreEqual(0.40f, skills.PierceShare, 1e-5f, "+5% a point.");

            heroObj.GetComponent<Equipment>().Equip(new Item(ItemType.Bow, 1));
            HealthComponent target = Enemy(new Vector3(0f, 0f, 5f));
            target.DamageReduction = 0.5f;
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            combat.DeliverHit(target, 100, ignoreArmor: false);
            Assert.AreEqual(1000 - 70, target.CurrentHealth, "50% armour, 40% of it pierced: the arrow loses 30%.");
        }

        [Test]
        public void DdCriticalHit_NeedsDash_EveryFifthHitIsDouble()
        {
            hero.ChooseClass(HeroClass.Dd);
            Assert.IsFalse(skills.LearnCriticalHit(), "Dash first.");
            Assert.IsTrue(skills.LearnDash());
            Assert.IsTrue(skills.LearnCriticalHit());
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent target = Enemy(new Vector3(0f, 0f, 1f));
            int dmg = combat.AttackDamage;
            for (int i = 1; i <= 5; i++)
            {
                combat.InitializeCombatStats(dmg, 0f, combat.AttackRange); // no cooldown for the test
                int before = target.CurrentHealth;
                Assert.IsTrue(combat.TryAttack(target));
                PendingHits.FlushAll();
                Assert.AreEqual(i == 5 ? dmg * 2 : dmg, before - target.CurrentHealth, $"Hit {i}.");
            }
        }

        [Test]
        public void MageFan_CastsItselfWithTheAttack_MeteorNeedsTheFan()
        {
            hero.ChooseClass(HeroClass.Mage);
            Assert.IsFalse(skills.LearnMeteor(), "The fan first.");
            Assert.IsTrue(skills.LearnFireFan());
            Assert.IsTrue(skills.LearnMeteor());
            Assert.IsTrue(skills.HasActive, "The meteor is the mage's Q.");

            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent near = Enemy(new Vector3(0f, 0f, 5f));
            HealthComponent side = Enemy(new Vector3(2f, 0f, 8f)); // in the fan, not the target
            Assert.IsTrue(combat.TryAttack(near));
            Assert.Less(side.CurrentHealth, 1000, "The fan went off with the attack.");
        }

        [Test]
        public void Meteor_HitsEveryoneAroundTheSpot_ThreeTimesTheDamage()
        {
            hero.ChooseClass(HeroClass.Mage);
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent a = Enemy(new Vector3(10f, 0f, 0f)), b = Enemy(new Vector3(12f, 0f, 2f)), far = Enemy(new Vector3(20f, 0f, 0f));
            MeteorStrike m = MeteorStrike.Drop(combat, new Vector3(11f, 0f, 1f));
            extra.Add(m.gameObject);
            Assert.AreEqual(2, m.Impact());
            Assert.AreEqual(1000 - Mathf.RoundToInt(combat.AttackDamage * 3f), a.CurrentHealth);
            Assert.AreEqual(1000, far.CurrentHealth, "Outside the 4 m.");
        }

        private HealthComponent Enemy(Vector3 position)
        {
            var go = new GameObject("Enemy", typeof(HealthComponent));
            go.transform.position = position;
            HealthComponent hp = go.GetComponent<HealthComponent>();
            hp.InitializeHealth(1000);
            extra.Add(go);
            gridIds.Add(UnitSpatialGrid.Register(hp, hp, UnitSpatialGrid.TeamHostile, 0.4f));
            return hp;
        }
    }
}
