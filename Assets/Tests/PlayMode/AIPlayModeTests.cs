using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Play Mode tests: run the real MonoBehaviour lifecycle (Awake/Start/Update, CharacterController movement)
    /// and check AI behaviour over simulated time.
    /// </summary>
    public class AIPlayModeTests
    {
        private const float SimulationSpeed = 4f;

        private readonly List<GameObject> spawned = new List<GameObject>();
        private float defaultFixedDeltaTime;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = SimulationSpeed;
            defaultFixedDeltaTime = Time.fixedDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Time.fixedDeltaTime = defaultFixedDeltaTime;
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) Object.Destroy(spawned[i]);
            }
            spawned.Clear();
        }

        [Category("Slow")]
        [UnityTest, Order(1)]
        public IEnumerator HostileCamp_MobsPatrolAroundTheirOwnCamp()
        {
            CreateGround();
            Vector3 campCenter = new Vector3(300f, 0f, 300f);
            GameObject camp = Track(HostileCampBuilder.CreateHostileCamp(campCenter, "TestCamp"));

            yield return null; // Start()

            HostileMobAI[] mobs = camp.GetComponentsInChildren<HostileMobAI>();
            Assert.AreEqual(5, mobs.Length, "Camp should spawn a warlord and 4 raiders.");
            foreach (HostileMobAI mob in mobs)
            {
                Assert.Less(FlatDistance(mob.GuardOrigin, campCenter), 6f, $"{mob.name} guard origin must be at its camp, not at the world origin.");
            }

            yield return WaitSimulated(20f);

            foreach (HostileMobAI mob in mobs)
            {
                Assert.AreEqual(AIStateType.Wander, mob.CurrentStateType, $"{mob.name} has nothing to fight and should be patrolling.");
                Assert.Less(FlatDistance(mob.transform.position, campCenter), 25f, $"{mob.name} wandered away from its camp.");
            }
        }

        /// <summary>
        /// Spawners add the CharacterController at the world origin and move the object afterwards.
        /// With Physics.autoSyncTransforms off, the first CharacterController.Move() before any physics step
        /// used the stale origin position and teleported the unit back to (0,0,0) - i.e. into the player's base.
        /// A huge fixed timestep guarantees no physics step runs before the first AI Update.
        /// </summary>
        [UnityTest, Order(0)]
        public IEnumerator SpawnedMobsAndAnimals_KeepSpawnPosition_WhenNoPhysicsStepRunsFirst()
        {
            CreateGround();
            Time.fixedDeltaTime = 5f;

            Vector3 campCenter = new Vector3(200f, 0f, 200f);
            GameObject camp = Track(HostileCampBuilder.CreateHostileCamp(campCenter, "TestCamp"));
            Vector3 boarSpawn = new Vector3(-150f, 0f, -150f);
            GameObject boar = Track(AnimalModelBuilder.CreateAnimalPrefab(AnimalType.Boar, "TestBoar"));
            boar.transform.position = boarSpawn;

            for (int i = 0; i < 5; i++) yield return null;

            foreach (HostileMobAI mob in camp.GetComponentsInChildren<HostileMobAI>())
            {
                // The bug teleports units ~280 m to the world origin; 40 m leaves room for patrol walking during long first
                // frames (time scale 4, first-time mesh sculpting) without hiding a teleport.
                Assert.Less(FlatDistance(mob.transform.position, campCenter), 40f, $"{mob.name} was teleported to {mob.transform.position}.");
            }
            Assert.Less(FlatDistance(boar.transform.position, boarSpawn), 30f, $"Boar was teleported to {boar.transform.position}.");
        }

        [UnityTest, Order(2)]
        public IEnumerator HostileCamp_LeaderIsWarlordWithWarlordStats()
        {
            CreateGround();
            GameObject camp = Track(HostileCampBuilder.CreateHostileCamp(new Vector3(-250f, 0f, 200f), "TestCamp"));

            yield return null;

            HostileSettlementLeader leader = camp.GetComponentInChildren<HostileSettlementLeader>();
            Assert.IsNotNull(leader);
            HostileMobAI leaderAI = leader.GetComponent<HostileMobAI>();
            Assert.AreEqual(HostileMobType.WarlordGuard, leaderAI.MobType);
            Assert.AreEqual(UnitRanks.Health(UnitRank.General), leaderAI.GetComponent<Equipment>().BaseHealth, "The warlord is a general (before its armour).");
            Assert.AreEqual(4, leader.SquadMembers.Count);
        }

        [UnityTest]
        public IEnumerator KilledAnimal_FallsOntoItsSide_AwayFromTheKiller_OnTheGround()
        {
            CreateGround();
            foreach (AnimalType type in new[] { AnimalType.Deer, AnimalType.Boar, AnimalType.Rabbit })
            {
                GameObject a = Track(AnimalModelBuilder.CreateAnimalPrefab(type, "Dying" + type));
                Vector3 spot = new Vector3(-300f, 0f, -300f + (int)type * 10f);
                spot.y = BattleOfAges.RTS.RtsUnit.GetTerrainHeight(spot);
                a.transform.SetPositionAndRotation(spot, Quaternion.identity);
                GameObject hunter = Track(new GameObject("Hunter", typeof(HealthComponent), typeof(CombatUnit)));
                hunter.transform.position = a.transform.position + Vector3.right * 6f; // on its right: it falls to its left
                yield return null;
                PeacefulAnimalAI ai = a.GetComponent<PeacefulAnimalAI>();
                ai.enabled = false;
                Transform model = a.transform.Find("VisualModel");
                float ground = BattleOfAges.RTS.RtsUnit.GetTerrainHeight(a.transform.position);
                float standingTop = Top(a) - ground;

                HealthComponent hp = a.GetComponent<HealthComponent>();
                hp.LastAttacker = hunter.GetComponent<CombatUnit>();
                hp.TakeDamage(hp.MaxHealth * 10);
                Assert.IsTrue(ai.HasFallen, $"{type}: falls");
                yield return new WaitForSeconds(PeacefulAnimalAI.FallSeconds + PeacefulAnimalAI.SettleSeconds + 0.2f);

                Assert.Less(Mathf.Abs(Vector3.Dot(model.up, Vector3.up)), 0.1f, $"{type}: lies on its side");
                Vector3 toHunter = hunter.transform.position - a.transform.position;
                toHunter.y = 0f;
                Assert.Less(Vector3.Dot(model.up, toHunter.normalized), 0f, $"{type}: fell away from the hunter (its back away, the legs toward them)");
                float bottom = float.MaxValue;
                foreach (Renderer r in a.GetComponentsInChildren<Renderer>()) if (r.enabled) bottom = Mathf.Min(bottom, r.bounds.min.y);
                bottom -= ground; // above the ground under it (the world of an earlier test may be loaded)
                Assert.Greater(bottom, -0.35f, $"{type}: not sunk into the ground"); // (a turned leg's box reaches a little lower than the leg)
                Assert.Less(bottom, 0.25f, $"{type}: not hovering");
                Assert.Less(Top(a) - ground, standingTop * 0.85f, $"{type}: lower lying than standing");
            }
        }

        private static float Top(GameObject g)
        {
            float top = float.MinValue;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>()) if (r.enabled) top = Mathf.Max(top, r.bounds.max.y);
            return top;
        }

        [Category("Slow")]
        [UnityTest, Order(3)]
        public IEnumerator WildBoar_HasBoarStats_IsAnimated_AndGrazesNearSpawn()
        {
            CreateGround();
            GameObject boar = Track(AnimalModelBuilder.CreateAnimalPrefab(AnimalType.Boar, "TestBoar"));
            Vector3 spawnPos = new Vector3(-200f, 0f, 150f);
            boar.transform.position = spawnPos;

            yield return null;

            PeacefulAnimalAI ai = boar.GetComponent<PeacefulAnimalAI>();
            Assert.AreEqual(AnimalType.Boar, ai.Type);
            Assert.AreEqual(120, ai.Health.MaxHealth, "Boar must not fall back to deer stats.");
            Assert.Less(FlatDistance(ai.SpawnOrigin, spawnPos), 0.01f);

            // Procedural animation must actually drive the visual model while the boar moves/grazes.
            Transform visual = boar.transform.Find("VisualModel");
            Transform head = visual.Find("NeckPivot");                 // jointed rig: the neck carries the head
            Transform leg = visual.Find("LegPivot_0");
            Quaternion initialHeadRot = head.localRotation, initialLegRot = leg.localRotation;
            bool animated = false;
            float end = Time.time + 10f;
            while (Time.time < end && !animated)
            {
                animated = visual.localRotation != Quaternion.identity
                           || visual.localPosition.sqrMagnitude > 1e-6f
                           || head.localRotation != initialHeadRot
                           || leg.localRotation != initialLegRot;
                yield return null;
            }
            Assert.IsTrue(animated, "Animal procedural animation never ran.");

            yield return WaitSimulated(20f);

            Assert.Less(FlatDistance(boar.transform.position, spawnPos), ai.WanderRadius + 5f, "Animal wandered away from its spawn point.");
        }

        /// <summary>
        /// The assault is launched towards five directions, so the squad has to get past different camp
        /// obstacles (campfire, tents, barricades) on the way out — AI movement has no pathfinding.
        /// </summary>
        [Category("Slow")]
        [UnityTest, Order(4)]
        public IEnumerator SettlementLeader_AssaultOrder_SquadReachesPlayerFromAnyDirection()
        {
            CreateGround();
            Vector3[] directions =
            {
                Vector3.back, Vector3.forward, Vector3.right, Vector3.left, new Vector3(1f, 0f, 1f).normalized
            };

            for (int d = 0; d < directions.Length; d++)
            {
                Vector3 campCenter = new Vector3(-600f + d * 300f, 0f, -300f);
                GameObject camp = HostileCampBuilder.CreateHostileCamp(campCenter, $"TestCamp_{d}");

                GameObject player = new GameObject("Player");
                player.AddComponent<CharacterController>();
                player.AddComponent<HealthComponent>().InitializeHealth(100000);
                // Beyond aggro (18m) and leash (28.8m), but within the leader's assault radius (80m).
                player.transform.position = campCenter + directions[d] * 55f;

                yield return null;

                HostileSettlementLeader leader = camp.GetComponentInChildren<HostileSettlementLeader>();
                HostileMobAI[] mobs = camp.GetComponentsInChildren<HostileMobAI>();
                leader.ConsiderAssault();
                Assert.AreEqual(SquadCommand.AssaultPlayerBase, leader.CurrentCommand);

                // Keep a coarse path per mob so a failure shows where it got stuck.
                var paths = new Dictionary<HostileMobAI, System.Text.StringBuilder>();
                foreach (HostileMobAI mob in mobs) paths[mob] = new System.Text.StringBuilder();
                for (int s = 0; s < 5; s++)
                {
                    foreach (HostileMobAI mob in mobs) paths[mob].Append((mob.transform.position - campCenter).ToString("F1")).Append(' ');
                    yield return WaitSimulated(5f);
                }

                foreach (HostileMobAI mob in mobs)
                {
                    Assert.Less(FlatDistance(mob.transform.position, player.transform.position), 15f,
                        $"Direction {directions[d]}: {mob.name} did not reach the player (state: {mob.CurrentStateType}); path relative to camp: {paths[mob]}");
                }

                Object.Destroy(camp);
                Object.Destroy(player);
                yield return null;
            }
        }

        /// <summary>
        /// Smoke test of the real game scene: world generation, camps, wildlife and AI run for
        /// 30 simulated seconds. Any Debug.LogError or unhandled exception fails the test.
        /// Camps and wildlife only spawn in detailed chunks away from the base zone, so the player
        /// is teleported to chunk corners (far from any camp centre) until a camp is generated.
        /// </summary>
        [UnityTest, Order(100)]
        public IEnumerator SampleScene_RunsWithoutErrors_AndSpawnedAIIsConsistent()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;

            GameObject player = GameObject.Find("Player");
            Assert.IsNotNull(player, "SampleScene must contain the Player.");
            HealthComponent playerHp = player.GetComponent<HealthComponent>();
            if (playerHp != null) playerHp.InitializeHealth(100000); // keep the run deterministic if raiders attack

            Vector2[] probes =
            {
                new Vector2(416f, 416f), new Vector2(-416f, 416f), new Vector2(416f, -416f),
                new Vector2(-416f, -416f), new Vector2(800f, 32f), new Vector2(32f, -800f)
            };

            HostileSettlementLeader[] leaders = new HostileSettlementLeader[0];
            foreach (Vector2 probe in probes)
            {
                TeleportPlayer(player, probe);
                yield return WaitSimulated(1f);
                leaders = Object.FindObjectsByType<HostileSettlementLeader>(FindObjectsSortMode.None);
                if (leaders.Length > 0) break;
            }

            HostileMobAI[] mobs = Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None);
            PeacefulAnimalAI[] animals = Object.FindObjectsByType<PeacefulAnimalAI>(FindObjectsSortMode.None);
            Debug.Log($"[PlayModeSmoke] player={player.transform.position} camps={leaders.Length} mobs={mobs.Length} animals={animals.Length}");

            Assert.Greater(leaders.Length, 0, "No hostile camp was generated around any probe position.");
            Assert.Greater(animals.Length, 0, "No wildlife was generated around the player.");

            foreach (HostileSettlementLeader leader in leaders)
            {
                HostileMobAI leaderAI = leader.GetComponent<HostileMobAI>();
                Assert.AreEqual(HostileMobType.WarlordGuard, leaderAI.MobType, $"{leader.name} is not a warlord.");
                Assert.AreEqual(UnitRanks.Health(UnitRank.General), leaderAI.GetComponent<Equipment>().BaseHealth, "The warlord is a general (before its armour).");
            }

            foreach (PeacefulAnimalAI animal in animals)
            {
                Assert.IsTrue(animal.name.StartsWith("Wild_" + animal.Type), $"{animal.name} spawned with AI type {animal.Type}.");
                int expectedHp = animal.Type == AnimalType.Boar ? 120 : animal.Type == AnimalType.Rabbit ? 30 : 80;
                Assert.AreEqual(expectedHp, animal.Health.MaxHealth, $"{animal.name} has wrong HP.");
                Assert.Greater(animal.SpawnOrigin.sqrMagnitude, 1f, $"{animal.name} spawn origin is the world origin.");
            }

            var startPositions = new Dictionary<HostileMobAI, Vector3>();
            foreach (HostileMobAI mob in mobs)
            {
                Assert.Greater(mob.GuardOrigin.sqrMagnitude, 1f, $"{mob.name} guard origin is the world origin.");
                startPositions[mob] = mob.transform.position;
            }

            yield return WaitSimulated(30f);

            foreach (HostileMobAI mob in mobs)
            {
                if (mob == null || mob.CurrentStateType != AIStateType.Wander) continue; // despawned or fighting
                Assert.Less(FlatDistance(mob.transform.position, startPositions[mob]), 30f, $"{mob.name} left its camp while patrolling.");
            }

            foreach (PeacefulAnimalAI animal in animals)
            {
                if (animal == null || animal.CurrentStateType == AIStateType.Flee) continue; // despawned or fleeing
                Assert.Less(FlatDistance(animal.transform.position, animal.SpawnOrigin), animal.WanderRadius + 6f, $"{animal.name} wandered away from its spawn point.");
            }
        }

        private static void TeleportPlayer(GameObject player, Vector2 xz)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            float y = BattleOfAges.World.InfiniteWorldManager.GetSampledTerrainHeight(xz.x, xz.y) + 1.0f;
            player.transform.position = new Vector3(xz.x, y, xz.y);
            if (cc != null) cc.enabled = true;
        }

        private GameObject Track(GameObject go)
        {
            spawned.Add(go);
            return go;
        }

        private void CreateGround()
        {
            // RtsUnit.GetTerrainHeight raycasts against an object named "Ground" when no world manager exists.
            GameObject ground = Track(new GameObject("Ground"));
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.5f, 0f);
            box.size = new Vector3(2000f, 1f, 2000f);
        }

        private static IEnumerator WaitSimulated(float gameSeconds)
        {
            float end = Time.time + gameSeconds;
            while (Time.time < end) yield return null;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
