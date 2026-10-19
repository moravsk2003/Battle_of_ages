using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    /// <summary>Unit ranks, the speed formula and the town hall table of docs/GAME_DESIGN.md (§7.1, §10.4, §11.1).</summary>
    [TestFixture]
    public class UnitRankTests
    {
        [TestCase(UnitRank.Person, 20, 2, 2)]
        [TestCase(UnitRank.Warrior, 40, 4, 4)]
        [TestCase(UnitRank.Knight, 100, 10, 10)]
        [TestCase(UnitRank.General, 250, 25, 50)]
        public void Ranks_HaveTheirBaseStatsAndKillXp(UnitRank rank, int hp, int strength, int xp)
        {
            Assert.AreEqual(hp, UnitRanks.Health(rank));
            Assert.AreEqual(strength, UnitRanks.Strength(rank));
            Assert.AreEqual(xp, UnitRanks.KillXp(rank));
            Assert.AreEqual(hp * 2, UnitRanks.Health(rank, hero: true), "A hero has twice the HP of its rank.");
            Assert.AreEqual(strength * 2, UnitRanks.Strength(rank, hero: true), "A hero has twice the strength of its rank.");
        }

        [TestCase(UnitRank.Person, false, 2.47f)]
        [TestCase(UnitRank.Warrior, false, 3.0f)]
        [TestCase(UnitRank.Knight, false, 4.74f)]
        [TestCase(UnitRank.General, false, 7.5f)]
        [TestCase(UnitRank.Person, true, 2.5f)]
        [TestCase(UnitRank.General, true, 8.84f)]
        public void Speed_IsGlobalTimesModifierTimesSqrtStrength(UnitRank rank, bool hero, float expected)
        {
            float speed = CharacterSpeed.Of(UnitRanks.SpeedModifier(rank, hero), UnitRanks.Strength(rank, hero));
            Assert.AreEqual(expected, speed, 0.01f);
        }

        [Test]
        public void Locomotion_PlaysAtTheGroundSpeed()
        {
            Assert.AreEqual(1f, Utils.CharacterAnimationDriver.AnimatorLocoRate(3f), 1e-5f, "Up to the run clip the blend tree is enough.");
            Assert.AreEqual(7.5f / 4.2f, Utils.CharacterAnimationDriver.AnimatorLocoRate(7.5f), 1e-4f, "A general runs 1.8x the clip.");
            Assert.AreEqual(2f, Utils.CharacterAnimationDriver.ClipRate(2.8f, 1.4f), 1e-5f);
            Assert.AreEqual(0.5f, Utils.CharacterAnimationDriver.ClipRate(0.1f, 1.4f), 1e-5f, "Never below half speed.");
        }

        [Test]
        public void Apply_SetsBaseStatsUnderTheWornKit()
        {
            var unit = new GameObject("RankedUnit", typeof(HealthComponent), typeof(CombatUnit));
            try
            {
                Equipment gear = unit.AddComponent<Equipment>();
                gear.LoadSlots(new[] { new Item(ItemType.Sword, 5) });
                UnitRanks.Apply(unit, UnitRank.Knight);

                CombatUnit combat = unit.GetComponent<CombatUnit>();
                Assert.AreEqual(100, unit.GetComponent<HealthComponent>().MaxHealth, "No chest piece: the knight's 100 HP.");
                Assert.AreEqual(10f, combat.Strength, 1e-4f);
                Assert.AreEqual(20, combat.AttackDamage, "A level 5 sword doubles the strength.");
                Assert.AreEqual(GameBalance.SpeedModifierSoldier, combat.SpeedModifier);
            }
            finally { Object.DestroyImmediate(unit); }
        }

        [Test]
        public void Mobs_AreWarriorsAndTheWarlordAGeneral()
        {
            Assert.AreEqual(UnitRank.Warrior, HostileMobAI.RankOf(HostileMobType.Bandit));
            Assert.AreEqual(UnitRank.Warrior, HostileMobAI.RankOf(HostileMobType.Goblin));
            Assert.AreEqual(UnitRank.General, HostileMobAI.RankOf(HostileMobType.WarlordGuard));
        }

        [Test]
        public void TownHall_FollowsTheDesignTable()
        {
            int[] caps = { 6, 15, 25, 40, 60, 85 };
            for (int l = 0; l <= TownHall.MaxLevel; l++) Assert.AreEqual(caps[l], TownHall.CapForLevel(l), $"Cap at level {l}.");

            int[] wood = { 0, 0, 30, 60, 100, 160 };
            for (int to = 2; to <= TownHall.MaxLevel; to++)
            {
                TownHall.UpgradeCost(to, out int w, out _, out _);
                Assert.AreEqual(wood[to], w, $"Wood to reach level {to}.");
            }
            Assert.AreEqual(10, BuildingData.GetDefinition(BuildingType.TownHall).WoodCost, "Building the hall costs 10 wood.");
        }
    }
}
