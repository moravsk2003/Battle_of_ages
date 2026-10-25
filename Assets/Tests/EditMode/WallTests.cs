using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;

namespace BattleOfAges.Tests
{
    /// <summary>Walls (docs/GAME_DESIGN.md §10.5): strength from the ore, deeper foundations, the walls' level.</summary>
    public class WallTests
    {
        private static System.Func<Vector3, float> SlopeAlongZ(float degrees) => p => p.z * Mathf.Tan(degrees * Mathf.Deg2Rad);

        [Test]
        public void BetterOre_MakesAStrongerWall()
        {
            int baseHp = BuildingData.GetDefinition(BuildingType.CastleWall).MaxHealth;
            Assert.AreEqual(baseHp, WallSegment.MaxHealthFor(BuildingType.CastleWall, 0));
            Assert.AreEqual(Mathf.RoundToInt(baseHp * (1f + 2f * GameBalance.WallHealthPerOreTier)), WallSegment.MaxHealthFor(BuildingType.CastleWall, 2));
            Assert.Greater(WallSegment.MaxHealthFor(BuildingType.WallGate, 1), WallSegment.MaxHealthFor(BuildingType.WallGate, 0));
        }

        [Test]
        public void Wall_StandsOnASlope_TooSteepForAHouse()
        {
            System.Func<Vector3, float> ground = SlopeAlongZ(20f);
            Assert.IsFalse(BuildingPlacementSystem.FootprintLevel(BuildingType.WorkerHouse, Vector3.zero, Quaternion.identity, ground, out _), "a house: too steep");
            Assert.IsTrue(BuildingPlacementSystem.FootprintLevel(BuildingType.CastleWall, Vector3.zero, Quaternion.identity, ground, out _), "a wall across the slope: fine");
        }

        [Test]
        public void TurnedFootprint_IsCheckedTurned()
        {
            System.Func<Vector3, float> ground = SlopeAlongZ(25f);
            Assert.IsTrue(BuildingPlacementSystem.FootprintLevel(BuildingType.CastleWall, Vector3.zero, Quaternion.identity, ground, out _), "along the contour");
            Assert.IsFalse(BuildingPlacementSystem.FootprintLevel(BuildingType.CastleWall, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), ground, out _), "straight up the slope");
        }

        [Test]
        public void Stairs_FootprintIsInside()
        {
            // The stairs stand inside (-z): a drop just behind the wall matters, one in front of it does not
            System.Func<Vector3, float> ditchInside = p => p.z < -1.6f ? -2f : 0f, ditchOutside = p => p.z > 1.6f ? -2f : 0f;
            Assert.IsFalse(BuildingPlacementSystem.FootprintLevel(BuildingType.WallStairs, Vector3.zero, Quaternion.identity, ditchInside, out _));
            Assert.IsTrue(BuildingPlacementSystem.FootprintLevel(BuildingType.WallStairs, Vector3.zero, Quaternion.identity, ditchOutside, out _));
        }

        [Test]
        public void WallCheckBox_LeavesTheJointsFree()
        {
            BuildingPlacementSystem.CheckBox(BuildingData.GetDefinition(BuildingType.CastleWall), out _, out Vector3 size);
            Assert.Less(size.x * 0.5f, WallSegmentHalf - Utils.WallBuilder.PierApothem, "clear of the pier at each end");
            Assert.Greater(size.x, 1f);
        }

        private static float WallSegmentHalf => Utils.WallBuilder.Length * 0.5f;

        [Test]
        public void Fortifications_LevelRaisesTheBonus_AndCostsRarerOre()
        {
            int before = Fortifications.Level;
            try
            {
                Fortifications.Level = 1;
                float b1 = Fortifications.DamageBonus;
                Fortifications.Level = 3;
                Assert.Greater(Fortifications.DamageBonus, b1);
                Fortifications.UpgradeCost(4, out int wood, out int ore, out int tier);
                Assert.AreEqual(3, tier, "level IV needs tier III ore");
                Assert.Greater(wood, 0);
                Assert.Greater(ore, 0);
                Fortifications.Level = 99;
                Assert.AreEqual(Fortifications.MaxLevel, Fortifications.Level);
            }
            finally { Fortifications.Level = before; }
        }

        [Test]
        public void OnlyTheStrong_HarmWalls()
        {
            GameObject wall = BuildingNode.CreateWallPrefab(BuildingType.CastleWall, "TestWall");
            var striker = new GameObject("Striker");
            try
            {
                HealthComponent hp = wall.GetComponent<HealthComponent>();
                CombatUnit unit = striker.AddComponent<CombatUnit>();
                unit.Strength = GameBalance.RankStrength[1]; // a warrior
                unit.DeliverHit(hp, 50, false);
                Assert.AreEqual(hp.MaxHealth, hp.CurrentHealth, "a warrior's blow does nothing to a wall");
                unit.Strength = GameBalance.RankStrength[2]; // a knight
                unit.DeliverHit(hp, 50, false);
                Assert.Less(hp.CurrentHealth, hp.MaxHealth, "a knight's does");
            }
            finally
            {
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(striker);
            }
        }

        [Test]
        public void OnTheWalls_ABowReachesFurther_ASwordDoesNot()
        {
            var archer = new GameObject("Archer");
            var swordsman = new GameObject("Swordsman");
            try
            {
                CombatUnit bow = archer.AddComponent<CombatUnit>();
                bow.InitializeCombatStats(4, 2f, Crafting.ItemStatCalculator.BOW_RANGE);
                CombatUnit sword = swordsman.AddComponent<CombatUnit>();
                sword.InitializeCombatStats(4, 2f, 2.5f);
                float bowRange = bow.AttackRange, swordRange = sword.AttackRange;
                bow.OnWall = sword.OnWall = true;
                Assert.AreEqual(bowRange * (1f + GameBalance.WallRangeBonus), bow.AttackRange, 1e-4f);
                Assert.AreEqual(swordRange, sword.AttackRange, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(archer);
                Object.DestroyImmediate(swordsman);
            }
        }

        [Test]
        public void WallPrefabs_HaveTheirParts()
        {
            GameObject gate = BuildingNode.CreateWallPrefab(BuildingType.WallGate, "TestGate");
            GameObject wall = BuildingNode.CreateWallPrefab(BuildingType.CastleWall, "TestWall");
            try
            {
                Assert.IsNotNull(gate.GetComponent<WallGateDoors>(), "the gate has leaves");
                Assert.AreEqual(Utils.WallBuilder.Kind.Gate, gate.GetComponent<WallSegment>().Kind);
                Assert.AreEqual(BuildingType.WallGate, gate.GetComponent<BuildingNode>().Type);
                Assert.IsNull(wall.GetComponentInChildren<CastleWallClimbTrigger>(), "a plain segment has no stairs");
                Assert.IsNotNull(wall.GetComponentInChildren<LODGroup>(), "near and far model");
            }
            finally
            {
                Object.DestroyImmediate(gate);
                Object.DestroyImmediate(wall);
            }
        }
    }
}
