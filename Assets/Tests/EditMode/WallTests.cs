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

        /// <summary>A tester saw the grass through a pier: rays from outside, from above and below, all along its height, must meet it.</summary>
        [Test]
        public void APier_HasNoCracksToSeeThrough()
        {
            var go = new GameObject("PierCracks");
            try
            {
                MeshCollider solid = go.AddComponent<MeshCollider>();
                solid.sharedMesh = Utils.WallBuilder.PierMesh;
                Physics.SyncTransforms();
                float r = Utils.WallBuilder.PierApothem;
                string open = "";
                for (int side = 0; side < 8; side++)
                {
                    float a = side * Mathf.PI / 4f;
                    Vector3 flat = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    foreach (float tilt in new[] { 0.7f, -0.7f, 0.2f, -0.2f })
                        for (float y = 0.6f; y < 3.3f; y += 0.003f)
                        {
                            Vector3 onFace = flat * r + Vector3.up * y;
                            var ray = new Ray(onFace + (flat + Vector3.up * tilt).normalized * 2f, -(flat + Vector3.up * tilt).normalized);
                            if (!solid.Raycast(ray, out _, 2.3f)) { open = $"side {side}, height {y:0.000}, tilt {tilt}"; break; }
                        }
                }
                Assert.IsEmpty(open, "a ray went in between the courses");
            }
            finally { Object.DestroyImmediate(go); }
        }

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
        public void Barrier_IsARectangleWithSquareEnds()
        {
            // A wall from (0,0) to (4,0), 0.9 either side of its middle line
            var wall = new WallBarriers.Barrier(Vector2.zero, new Vector2(4f, 0f), 0.9f, 10f);
            Assert.AreEqual(-0.9f, WallBarriers.Distance(wall, new Vector2(2f, 0f), out _), 1e-4f, "the middle: deepest inside");
            Assert.AreEqual(0.6f, WallBarriers.Distance(wall, new Vector2(2f, 1.5f), out Vector2 n), 1e-4f, "beside it");
            Assert.AreEqual(1f, n.y, 1e-4f, "the normal points out of the face");
            Assert.AreEqual(0.5f, WallBarriers.Distance(wall, new Vector2(4.5f, 0.8f), out n), 1e-4f, "past the end, within its thickness: square, not rounded");
            Assert.AreEqual(1f, n.x, 1e-4f);
            Assert.AreEqual(Mathf.Sqrt(0.5f * 0.5f + 0.6f * 0.6f), WallBarriers.Distance(wall, new Vector2(4.5f, 1.5f), out _), 1e-4f, "off the corner");
            var pier = new WallBarriers.Barrier(new Vector2(4f, 0f), new Vector2(4f, 0f), 1f, 10f);
            Assert.AreEqual(1f, WallBarriers.Distance(pier, new Vector2(6f, 0f), out _), 1e-4f, "a pier is a disc");
        }

        [Test]
        public void SegmentDistance_CrossingIsZero_ParallelIsTheGap()
        {
            Assert.AreEqual(0f, WallBarriers.SegmentDistance(new Vector2(0f, -1f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f)), 1e-5f);
            Assert.AreEqual(2f, WallBarriers.SegmentDistance(new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(0f, 2f), new Vector2(4f, 2f)), 1e-5f);
            Assert.AreEqual(1f, WallBarriers.SegmentDistance(new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f), new Vector2(3f, 0f)), 1e-5f, "in line, apart");
        }

        [Test]
        public void FromTheWalls_TheWallsLevelAddsDamage()
        {
            int before = Fortifications.Level;
            PendingHits.FlushAll();
            GameObject Striker(string name)
            {
                var go = new GameObject(name, typeof(HealthComponent), typeof(CombatUnit));
                go.AddComponent<Crafting.Equipment>();
                UnitProgress.Setup(go, UnitRank.Knight);
                go.GetComponent<HealthComponent>().InitializeHealth(100);
                return go;
            }
            GameObject ground = Striker("OnGround"), wall = Striker("OnWall");
            var victimObj = new GameObject("Victim", typeof(HealthComponent));
            victimObj.transform.position = new Vector3(1.5f, 0f, 0f);
            HealthComponent victim = victimObj.GetComponent<HealthComponent>();
            try
            {
                Fortifications.Level = 3;
                victim.InitializeHealth(100000);
                Assert.IsTrue(ground.GetComponent<CombatUnit>().TryAttack(victim));
                PendingHits.FlushAll();
                int plain = 100000 - victim.CurrentHealth;

                victim.InitializeHealth(100000);
                CombatUnit fromWall = wall.GetComponent<CombatUnit>();
                fromWall.OnWall = true;
                Assert.IsTrue(fromWall.TryAttack(victim));
                PendingHits.FlushAll();
                int walled = 100000 - victim.CurrentHealth;
                Assert.AreEqual(Mathf.RoundToInt(plain * (1f + GameBalance.WallLevelDamageBonus[3])), walled, 1, "walls III: +30%");
            }
            finally
            {
                Fortifications.Level = before;
                PendingHits.FlushAll();
                Object.DestroyImmediate(ground);
                Object.DestroyImmediate(wall);
                Object.DestroyImmediate(victimObj);
            }
        }

        [Test]
        public void Fortifications_CannotBeRaised_WithoutWalls_OrPastV()
        {
            int before = Fortifications.Level;
            try
            {
                Fortifications.Level = 1;
                Assert.IsFalse(Fortifications.CanUpgrade(out string why), "no wall yet");
                Assert.IsNotEmpty(why);
                Fortifications.Level = Fortifications.MaxLevel;
                Assert.IsFalse(Fortifications.CanUpgrade(out why), "the top level");
                Assert.AreEqual(GameBalance.WallLevelDamageBonus[Fortifications.MaxLevel], Fortifications.DamageBonus);
            }
            finally { Fortifications.Level = before; }
        }

        [Test]
        public void WallTypes_AreWalls_OthersAreNot()
        {
            Assert.IsTrue(WallSegment.IsWall(BuildingType.CastleWall));
            Assert.IsTrue(WallSegment.IsWall(BuildingType.WallStairs));
            Assert.IsTrue(WallSegment.IsWall(BuildingType.WallGate));
            Assert.IsFalse(WallSegment.IsWall(BuildingType.WorkerHouse));
            Assert.IsFalse(WallSegment.IsWall(BuildingType.TownHall));
            Assert.AreEqual(Utils.WallBuilder.Kind.Stairs, WallSegment.KindOf(BuildingType.WallStairs));
            Assert.AreEqual(Utils.WallBuilder.Kind.Gate, WallSegment.KindOf(BuildingType.WallGate));
            Assert.Greater(BuildingPlacementSystem.MaxRiseFor(BuildingType.CastleWall), BuildingPlacementSystem.MaxRiseFor(BuildingType.WorkerHouse), "walls stand on steeper ground");
            foreach (BuildingType t in new[] { BuildingType.CastleWall, BuildingType.WallStairs, BuildingType.WallGate })
            {
                BuildingDefinition d = BuildingData.GetDefinition(t);
                Assert.AreEqual(t, d.Type, $"{t} is in the build menu");
                Assert.Greater(d.OreCost, 0);
                Assert.Greater(d.MaxHealth, 0);
            }
            Assert.AreEqual(Utils.WallBuilder.GateLength, BuildingData.GetDefinition(BuildingType.WallGate).Size.x, "the gate snaps at its own length");
        }

        [Test]
        public void Stairs_ClimbAssist_PointsUpTheFlight()
        {
            GameObject stairs = BuildingNode.CreateWallPrefab(BuildingType.WallStairs, "TestStairs");
            try
            {
                CastleWallClimbTrigger assist = stairs.GetComponentInChildren<CastleWallClimbTrigger>();
                Assert.IsNotNull(assist);
                Vector3 up = assist.transform.forward;
                Assert.Greater(up.y, 0.5f, "uphill");
                Assert.Less(up.x, -0.5f, "toward the landing (-x)");
                Assert.IsNotNull(stairs.transform.Find("StairsRampCollider"));
                Assert.IsNotNull(stairs.transform.Find("Landing"));
            }
            finally { Object.DestroyImmediate(stairs); }
        }

        [Test]
        public void Gate_LeavesHangOnTheJambs()
        {
            GameObject gate = BuildingNode.CreateWallPrefab(BuildingType.WallGate, "TestGate");
            try
            {
                Transform left = gate.transform.Find("LeafLeft"), right = gate.transform.Find("LeafRight");
                Assert.IsNotNull(left);
                Assert.IsNotNull(right);
                Assert.AreEqual(-Utils.WallBuilder.GateHalfW, left.localPosition.x, 1e-4f);
                Assert.AreEqual(Utils.WallBuilder.GateHalfW, right.localPosition.x, 1e-4f);
                Assert.IsNotNull(left.GetComponent<BoxCollider>(), "a shut leaf stops the player");
                Assert.IsNotNull(gate.GetComponentInChildren<Light>(), "a torch by the gate");
                Assert.IsNull(gate.transform.Find("Body"), "no solid body across the opening");
            }
            finally { Object.DestroyImmediate(gate); }
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
