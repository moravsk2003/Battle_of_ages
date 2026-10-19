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
            Assert.AreEqual(1000 - Mathf.RoundToInt(full * 0.6f), near.CurrentHealth, 1, "Close by: 60% of the attack.");
            Assert.AreEqual(1000 - Mathf.RoundToInt(full * 0.6f * 0.5f), edge.CurrentHealth, 2, "At the edge: half of that.");
            Assert.AreEqual(1000, aside.CurrentHealth);
            Assert.AreEqual(1000, behind.CurrentHealth);
        }

        [Test]
        public void ArcherPiercingArrow_OnQ_200PercentThen100And50Behind()
        {
            hero.ChooseClass(HeroClass.Archer);
            Assert.IsFalse(skills.LearnPierce(), "Long shot first.");
            Assert.IsTrue(skills.LearnLongShot());
            Assert.IsTrue(skills.LearnPierce());
            Assert.IsTrue(skills.HasActiveIn(0), "The archer's piercing shot is on Q.");

            heroObj.GetComponent<Equipment>().Equip(new Item(ItemType.Bow, 1));
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent front = Enemy(new Vector3(0f, 0f, 8f));
            HealthComponent second = Enemy(new Vector3(0.5f, 0f, 12f)); // on the line, behind
            HealthComponent third = Enemy(new Vector3(-0.5f, 0f, 16f)); // further on the line
            HealthComponent aside = Enemy(new Vector3(6f, 0f, 12f));    // behind, but off the line
            int dmg = combat.AttackDamage;

            Assert.IsTrue(skills.UseActive(0, Vector3.forward));
            Assert.IsFalse(skills.UseActive(0, Vector3.forward), "Then 100 s to recharge.");
            combat.InitializeCombatStats(dmg, 0f, combat.AttackRange); // no attack cooldown for the test
            Assert.IsTrue(combat.TryAttack(front));
            PendingHits.FlushAll();
            Assert.AreEqual(1000 - dmg * 2, front.CurrentHealth, "200% to the target.");
            Assert.AreEqual(1000 - dmg, second.CurrentHealth, "100% to the one behind.");
            Assert.AreEqual(1000 - Mathf.RoundToInt(dmg * 0.5f), third.CurrentHealth, "50% to the next.");
            Assert.AreEqual(1000, aside.CurrentHealth);

            Assert.IsTrue(combat.TryAttack(front));
            PendingHits.FlushAll();
            Assert.AreEqual(1000 - dmg * 3, front.CurrentHealth, "Then a normal shot again.");
            Assert.AreEqual(1000 - dmg, second.CurrentHealth, "... that does not pierce.");
        }

        [Test]
        public void DdCriticalHit_NeedsDash_OnR_TheNextHitIsDouble()
        {
            hero.ChooseClass(HeroClass.Dd);
            Assert.IsFalse(skills.LearnCriticalHit(), "Dash first.");
            Assert.IsTrue(skills.LearnDash());
            Assert.IsTrue(skills.LearnCriticalHit());
            Assert.IsTrue(skills.HasActiveIn(1), "Critical hit is the DD's second skill (R).");
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent target = Enemy(new Vector3(0f, 0f, 1f));
            int dmg = combat.AttackDamage;

            Assert.IsTrue(skills.UseActive(1, Vector3.forward));
            Assert.IsFalse(skills.UseActive(1, Vector3.forward), "Then 40 s to recharge.");
            for (int i = 1; i <= 2; i++)
            {
                combat.InitializeCombatStats(dmg, 0f, combat.AttackRange); // no attack cooldown for the test
                int before = target.CurrentHealth;
                Assert.IsTrue(combat.TryAttack(target));
                PendingHits.FlushAll();
                Assert.AreEqual(i == 1 ? dmg * 2 : dmg, before - target.CurrentHealth, i == 1 ? "The next hit is double." : "Only that one.");
            }
        }

        [Test]
        public void MageFanOnQ_MeteorOnR_CastLikeAnAttack()
        {
            hero.ChooseClass(HeroClass.Mage);
            Assert.IsFalse(skills.LearnMeteor(), "The fan first.");
            Assert.IsTrue(skills.LearnFireFan());
            Assert.IsTrue(skills.LearnMeteor());
            Assert.IsTrue(skills.HasActiveIn(0) && skills.HasActiveIn(1), "Fan on Q, meteor on R.");

            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent target = Enemy(new Vector3(0f, 0f, 5f));
            float speed = combat.MoveSpeed;
            Assert.IsTrue(skills.UseActive(0, Vector3.forward));
            Assert.Less(target.CurrentHealth, 1000, "The fan hits (edit mode: at once; in play after the wind-up).");
            Assert.AreEqual(0f, combat.MoveSpeed, 1e-5f, "The mage stands while casting and a moment after.");
            Assert.Greater(speed, 0f);
            Assert.IsTrue(skills.UseActive(1, Vector3.forward), "The meteor has a target in range.");
            foreach (MeteorStrike m in Object.FindObjectsByType<MeteorStrike>(FindObjectsSortMode.None)) extra.Add(m.gameObject);
        }

        [Test]
        public void Meteor_HitsEveryoneAroundTheSpot_FiveTimesTheDamage()
        {
            hero.ChooseClass(HeroClass.Mage);
            CombatUnit combat = heroObj.GetComponent<CombatUnit>();
            HealthComponent a = Enemy(new Vector3(10f, 0f, 0f)), b = Enemy(new Vector3(12f, 0f, 2f)), far = Enemy(new Vector3(20f, 0f, 0f));
            MeteorStrike m = MeteorStrike.Drop(combat, new Vector3(11f, 0f, 1f));
            extra.Add(m.gameObject);
            Assert.AreEqual(2, m.Impact());
            Assert.AreEqual(1000 - Mathf.RoundToInt(combat.AttackDamage * 5f), a.CurrentHealth, "5x the mage's damage.");
            Assert.AreEqual(1000, far.CurrentHealth, "Outside the 2.5 m.");
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
