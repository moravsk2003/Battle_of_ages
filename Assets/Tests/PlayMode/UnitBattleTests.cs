using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Player soldiers with the large-battle movement (kinematic + crowd separation): orders, self-defence, props, death.
    /// </summary>
    public class UnitBattleTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void SetUp() => Time.timeScale = 4f;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (GameObject g in spawned) if (g != null) Object.Destroy(g);
            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator Soldier_StopsAtTree_InsteadOfWalkingThroughIt()
        {
            CreateGround();
            GameObject tree = Track(SteppeTreeBuilder.CreateSteppeTree("TestTree"));
            tree.transform.position = OnGround(new Vector3(10f, 0f, 0f));
            RtsUnit knight = CreateKnight(new Vector3(4f, 0f, 0f));
            yield return null;

            knight.IssueMoveCommand(tree.transform.position);
            yield return Wait(4f);

            float d = Flat(knight.transform.position, tree.transform.position);
            Assert.Greater(d, 0.7f, $"Soldier walked into the trunk (centre distance {d:0.00} m).");
            Assert.Less(d, 3f, "Soldier should still get right up to the tree.");
        }

        [UnityTest]
        public IEnumerator Knight_CanWork_ButStandsByAsASoldierUntilToldTo()
        {
            CreateGround();
            int before = WorkerRegistry.WorkerCount, workingBefore = WorkerRegistry.WorkingCount;
            RtsUnit knight = CreateKnight(Vector3.zero);
            yield return null;

            var job = knight.GetComponent<NpcResourceGatherer>();
            Assert.IsNotNull(job, "Every unit can work (one kind of unit).");
            Assert.IsFalse(job.IsWorking, "Without an order a knight stands by as a soldier.");
            Assert.AreEqual(before + 1, WorkerRegistry.WorkerCount, "The town hall cap counts soldiers too.");
            Assert.AreEqual(workingBefore, WorkerRegistry.WorkingCount);
            Assert.AreEqual(UnitRanks.Health(UnitRank.Knight), knight.GetComponent<Crafting.Equipment>().BaseHealth);

            WorkerRegistry.ApplyJobDistribution(0, 0, 0, 0, 0);
            Assert.IsFalse(job.IsWorking, "The job window only shares out units that already work.");

            job.CommandDeliverToChest();
            Assert.IsTrue(job.IsWorking, "A direct work order puts the knight to work.");
        }

        [UnityTest]
        public IEnumerator Villager_IsAPersonThatCanBeOrderedToFight()
        {
            CreateGround();
            GameObject villager = Track(Building.BuildingNode.CreateWorkerNpc(OnGround(Vector3.zero), "TestVillager"));
            HealthComponent raider = CreateRaider(new Vector3(6f, 0f, 0f), thinking: false);
            yield return null;

            Assert.AreEqual(UnitRanks.Health(UnitRank.Person), villager.GetComponent<Crafting.Equipment>().BaseHealth);
            Assert.AreEqual(UnitRanks.Strength(UnitRank.Person), villager.GetComponent<CombatUnit>().Strength, 1e-4f);

            villager.GetComponent<RtsUnit>().IssueAttackCommand(raider);
            yield return Wait(8f);
            Assert.Less(raider.CurrentHealth, raider.MaxHealth, "A worker ordered to attack fights instead of working.");
        }

        [UnityTest]
        public IEnumerator Work_TurnsPeopleIntoWarriors_NotIdleSoldiers()
        {
            CreateGround();
            GameObject villager = Track(Building.BuildingNode.CreateWorkerNpc(OnGround(Vector3.zero), "TestVillager"));
            RtsUnit knight = CreateKnight(new Vector3(5f, 0f, 0f));
            yield return null;

            Building.TownHall.PromoteWorkers(1f); // every working person this hour
            Assert.AreEqual(UnitRank.Warrior, villager.GetComponent<UnitProgress>().Rank, "Work hardens a person into a warrior.");
            Assert.AreEqual(UnitRank.Knight, knight.GetComponent<UnitProgress>().Rank, "Only working people are promoted by work.");
            Assert.AreEqual(UnitRanks.Health(UnitRank.Warrior), villager.GetComponent<Crafting.Equipment>().BaseHealth);
        }

        [UnityTest]
        public IEnumerator HerosKill_GivesXpByTheVictimsRank()
        {
            GameObject heroObj = Track(new GameObject("TestHero", typeof(HealthComponent), typeof(CombatUnit)));
            heroObj.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(heroObj, UnitRank.General, hero: true);
            HeroProgress hero = heroObj.AddComponent<HeroProgress>();
            GameObject victim = Track(new GameObject("TestRaider", typeof(HealthComponent), typeof(CombatUnit)));
            victim.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(victim, UnitRank.Warrior);
            victim.transform.position = new Vector3(1f, 0f, 0f);
            yield return null;

            victim.GetComponent<HealthComponent>().SetCurrentHealth(1);
            Assert.IsTrue(heroObj.GetComponent<CombatUnit>().TryAttack(victim.GetComponent<HealthComponent>()));
            Assert.AreEqual(4, hero.Xp, "Killing a warrior is worth 4 XP.");
        }

        [UnityTest]
        public IEnumerator AttackCommand_IsKept_AndDamagesTheTarget()
        {
            CreateGround();
            RtsUnit knight = CreateKnight(Vector3.zero);
            HealthComponent raider = CreateRaider(new Vector3(20f, 0f, 0f), thinking: false); // beyond self-defence range
            yield return null;

            knight.IssueAttackCommand(raider);
            yield return Wait(8f);

            Assert.Less(raider.CurrentHealth, raider.MaxHealth, "An ordered attack must reach and hurt the target.");
        }

        [UnityTest]
        public IEnumerator IdleSoldier_EngagesRaiderThatComesNear()
        {
            CreateGround();
            CreateKnight(Vector3.zero);
            HealthComponent raider = CreateRaider(new Vector3(8f, 0f, 0f), thinking: true);
            yield return Wait(6f);

            Assert.Less(raider.CurrentHealth, raider.MaxHealth, "Idle soldiers should fight raiders within reach on their own.");
        }

        [UnityTest]
        public IEnumerator FallenSoldier_StopsBlocking_AndIsRemovedLater()
        {
            CreateGround();
            RtsUnit knight = CreateKnight(Vector3.zero);
            yield return null;

            knight.GetComponent<HealthComponent>().TakeDamage(100000);
            yield return null;
            Assert.IsFalse(knight.GetComponent<CharacterController>().enabled, "Corpses must not block the living.");
            Assert.IsFalse(knight.enabled, "A fallen unit no longer moves or fights.");

            yield return Wait(UnitCorpse.SinkAfter + UnitCorpse.SinkDuration + 1f);
            Assert.IsTrue(knight == null, "The body is removed after it has sunk into the ground.");
        }

        // ------------------------------------------------------------------ helpers

        private RtsUnit CreateKnight(Vector3 position)
        {
            GameObject go = Track(new GameObject("TestKnight"));
            go.transform.position = OnGround(position);
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 2f; cc.center = new Vector3(0f, 1f, 0f); cc.radius = 0.45f;
            go.AddComponent<HealthComponent>().InitializeHealth(150);
            go.AddComponent<CombatUnit>().InitializeCombatStats(18, 1.6f, 2.5f);
            RtsUnit unit = go.AddComponent<RtsUnit>();
            GameObject model = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.KnightGuard, "VisualModel");
            model.transform.SetParent(go.transform, false);
            return unit;
        }

        private HealthComponent CreateRaider(Vector3 position, bool thinking)
        {
            GameObject mob = Track(HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "TestRaider"));
            mob.transform.position = OnGround(position);
            HostileMobAI ai = mob.GetComponent<HostileMobAI>();
            ai.SetGuardOrigin(mob.transform.position);
            if (!thinking) ai.enabled = false; // a target dummy that stays put
            return mob.GetComponent<HealthComponent>();
        }

        /// <summary>On the real ground surface: a previous test may have left a scene with the world terrain loaded.</summary>
        private static Vector3 OnGround(Vector3 p) { p.y = RtsUnit.GetTerrainHeight(p); return p; }

        private GameObject Track(GameObject go) { spawned.Add(go); return go; }

        private void CreateGround()
        {
            GameObject ground = Track(new GameObject("Ground"));
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.5f, 0f);
            box.size = new Vector3(400f, 1f, 400f);
        }

        private static IEnumerator Wait(float gameSeconds)
        {
            float end = Time.time + gameSeconds;
            while (Time.time < end) yield return null;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }
    }
}
