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
            foreach (int id in gridIds) UnitSpatialGrid.Unregister(id);
            gridIds.Clear();
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
            yield return Wait(0.5f); // the swing lands after its wind-up
            Assert.AreEqual(4, hero.Xp, "Killing a warrior is worth 4 XP.");
        }

        [UnityTest]
        public IEnumerator WalkingUpToAWarrior_ItStrikesBack_EvenThoughOneHitKillsIt()
        {
            GameObject heroObj = Track(new GameObject("TestHero", typeof(HealthComponent), typeof(CombatUnit)));
            heroObj.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(heroObj, UnitRank.General, hero: true);
            HealthComponent heroHp = heroObj.GetComponent<HealthComponent>();
            heroHp.InitializeHealth(heroHp.MaxHealth);
            GameObject warriorObj = Track(new GameObject("TestWarrior", typeof(HealthComponent), typeof(CombatUnit)));
            warriorObj.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(warriorObj, UnitRank.Warrior);
            HealthComponent warriorHp = warriorObj.GetComponent<HealthComponent>();
            warriorHp.InitializeHealth(warriorHp.MaxHealth);
            warriorObj.transform.position = new Vector3(2f, 0f, 0f);
            yield return null;

            CombatUnit hero = heroObj.GetComponent<CombatUnit>(), warrior = warriorObj.GetComponent<CombatUnit>();
            Assert.Greater(hero.AttackDamage, warriorHp.MaxHealth, "The hero kills the warrior with one hit.");
            Assert.IsTrue(hero.TryAttack(warriorHp), "The hero swings first...");
            yield return null;
            Assert.IsTrue(warrior.TryAttack(heroHp), "... the warrior reacts a frame later, during the wind-up.");
            yield return Wait(1f);
            Assert.IsFalse(warriorHp.IsAlive);
            Assert.AreEqual(heroHp.MaxHealth - warrior.AttackDamage, heroHp.CurrentHealth, "Its swing still landed: a trade.");
        }

        [UnityTest]
        public IEnumerator MageBolts_FlyOnToTheNextEnemy_WhenTheTargetFalls()
        {
            GameObject heroObj = Track(new GameObject("TestMage", typeof(HealthComponent), typeof(CombatUnit)));
            heroObj.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(heroObj, UnitRank.General, hero: true);
            heroObj.AddComponent<HeroProgress>().ChooseClass(HeroClass.Mage);
            HealthComponent weak = Enemy(new Vector3(10f, 0f, 0f), 1);
            HealthComponent next = Enemy(new Vector3(12f, 0f, 2f), 10000);
            yield return null;

            int each = Mathf.RoundToInt(heroObj.GetComponent<CombatUnit>().AttackDamage * GameBalance.MageBoltShare);
            Assert.IsTrue(heroObj.GetComponent<CombatUnit>().TryAttack(weak));
            yield return Wait(2f);
            Assert.IsFalse(weak.IsAlive, "The first bolt kills the weak target...");
            Assert.AreEqual(10000 - (GameBalance.MageBolts - 1) * each, next.CurrentHealth, "... the other four fly on to the next enemy.");
        }

        private readonly List<int> gridIds = new List<int>();

        private HealthComponent Enemy(Vector3 position, int hp)
        {
            GameObject go = Track(new GameObject("TestEnemy", typeof(HealthComponent)));
            go.transform.position = position;
            HealthComponent h = go.GetComponent<HealthComponent>();
            h.InitializeHealth(hp);
            gridIds.Add(UnitSpatialGrid.Register(h, h, UnitSpatialGrid.TeamHostile, 0.4f));
            return h;
        }

        [UnityTest]
        public IEnumerator MageVolley_HomesInAndGoesThroughArmour()
        {
            GameObject heroObj = Track(new GameObject("TestMage", typeof(HealthComponent), typeof(CombatUnit)));
            heroObj.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(heroObj, UnitRank.General, hero: true);
            HeroProgress mage = heroObj.AddComponent<HeroProgress>();
            mage.ChooseClass(HeroClass.Mage);
            GameObject victim = Track(new GameObject("TestRaider", typeof(HealthComponent), typeof(CombatUnit)));
            victim.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(victim, UnitRank.General);
            victim.transform.position = new Vector3(10f, 0f, 0f);
            yield return null;

            HealthComponent target = victim.GetComponent<HealthComponent>();
            target.DamageReduction = 0.5f;
            int damage = heroObj.GetComponent<CombatUnit>().AttackDamage;
            Assert.IsTrue(heroObj.GetComponent<CombatUnit>().TryAttack(target), "10 m is within the mage's range.");
            Assert.AreEqual(target.MaxHealth, target.CurrentHealth, "Bolts take time to fly.");
            yield return Wait(1.5f);
            Assert.AreEqual(target.MaxHealth - 5 * Mathf.RoundToInt(damage * 0.2f), target.CurrentHealth,
                "Five bolts of 20% each, armour ignored.");
        }

        [UnityTest]
        public IEnumerator HeroDeath_HalfDesert_ThenTheStrongestBecomesTheHero()
        {
            yield return ClearWorld();
            CreateGround();
            HeroSuccession hero = CreateHero();
            RtsUnit knight = CreateKnight(new Vector3(4f, 0f, 0f));
            for (int i = 0; i < 3; i++) Track(Building.BuildingNode.CreateWorkerNpc(OnGround(new Vector3(-4f - i * 2f, 0f, 3f)), "TestVillager"));
            yield return null;
            Assert.AreEqual(4, HeroSuccession.OwnUnits().Count);

            hero.GetComponent<HealthComponent>().TakeDamage(100000);
            Assert.IsTrue(hero.InInterregnum, "The hero's death starts the interregnum.");
            Assert.AreEqual(GameBalance.InterregnumSeconds, hero.InterregnumLeft, 0.1f);
            yield return null;
            Assert.AreEqual(2, HeroSuccession.OwnUnits().Count, "Half of the units deserted.");
            int deserters = 0;
            foreach (HostileMobAI mob in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None))
                if (mob.name == "Deserter") { deserters++; Track(mob.gameObject); }
            Assert.AreEqual(2, deserters, "Deserters do not die: they become hostile bands.");

            bool knightLeft = knight != null && knight.gameObject.activeInHierarchy;
            hero.EndInterregnum();
            yield return null;
            UnitRank expected = knightLeft ? UnitRank.Knight : UnitRank.Person;
            Assert.AreEqual(expected, hero.GetComponent<UnitProgress>().Rank, "The strongest unit left takes over.");
            Assert.AreEqual(UnitRanks.Health(expected, hero: true), hero.GetComponent<Crafting.Equipment>().BaseHealth, "x2 of its rank.");
            Assert.AreEqual(1, hero.GetComponent<HeroProgress>().Level);
            Assert.IsTrue(hero.GetComponent<HealthComponent>().IsAlive);
            Assert.AreEqual(1, HeroSuccession.OwnUnits().Count, "Its old body is gone.");
        }

        [UnityTest]
        public IEnumerator HeroDeath_WithNoUnitsLeft_IsTheEndOfTheGame()
        {
            yield return ClearWorld();
            CreateGround();
            HeroSuccession hero = CreateHero();
            bool over = false;
            System.Action<HeroSuccession> onOver = s => over = true;
            HeroSuccession.GameOver += onOver;
            try
            {
                yield return null;
                hero.GetComponent<HealthComponent>().TakeDamage(100000);
                hero.EndInterregnum();
                Assert.IsTrue(over);
                Assert.IsTrue(hero.IsGameOver);
            }
            finally { HeroSuccession.GameOver -= onOver; }
        }

        /// <summary>Earlier tests may leave a loaded scene (units, a town hall, camps): these tests count units, so clear them.</summary>
        private static IEnumerator ClearWorld()
        {
            foreach (RtsUnit u in Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None)) Object.Destroy(u.gameObject);
            foreach (HostileMobAI m in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (Building.TownHall h in Object.FindObjectsByType<Building.TownHall>(FindObjectsSortMode.None)) Object.Destroy(h.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator FallenHerosArtifacts_WaitForTheNewHero_TheEmpireTurnsIntoAShard()
        {
            yield return ClearWorld();
            CreateGround();
            HeroSuccession hero = CreateHero();
            HeroArtifacts bag = hero.gameObject.AddComponent<HeroArtifacts>();
            CreateKnight(new Vector3(4f, 0f, 0f));
            CreateKnight(new Vector3(6f, 0f, 0f));
            yield return null;
            bag.Add(ArtifactType.SwordX5);
            bag.Add(ArtifactType.CentralEmpire);
            Assert.IsTrue(CentralEmpire.Active);
            int epoch = CentralEmpire.WorldEpoch;

            hero.GetComponent<HealthComponent>().TakeDamage(100000);
            yield return null;
            Assert.IsFalse(CentralEmpire.Active, "The bearer's death destroys the empire...");
            Assert.AreEqual(epoch + 1, CentralEmpire.WorldEpoch, "... and opens a new world epoch.");
            Assert.AreEqual(0, bag.Held.Count, "Everything fell to the ground.");
            ArtifactPickup sword = null, empire = null;
            foreach (ArtifactPickup p in ArtifactPickup.Active) { Track(p.gameObject); if (p.Type == ArtifactType.SwordX5) sword = p; else empire = p; }
            Assert.IsNotNull(sword); Assert.IsNotNull(empire);
            Assert.IsTrue(empire.IsShard, "The empire's artifact is a shard at once.");
            Assert.IsFalse(sword.IsShard);
            Assert.IsFalse(sword.MayTake(hero), "Nobody takes it during the interregnum.");

            hero.EndInterregnum();
            Assert.IsTrue(sword.MayTake(hero), "The new hero may take it...");
            Assert.IsFalse(sword.MayTake(null), "... nobody else.");
            sword.TakeBy(hero);
            Assert.IsTrue(bag.Has(ArtifactType.SwordX5));
            yield return ClearWorld(); // the deserter band must not wander into later tests
        }

        private HeroSuccession CreateHero()
        {
            GameObject go = Track(new GameObject("TestHero", typeof(CharacterController), typeof(HealthComponent), typeof(CombatUnit)));
            go.transform.position = OnGround(new Vector3(0f, 0f, -6f));
            go.AddComponent<Crafting.Equipment>();
            UnitProgress.Setup(go, UnitRank.General, hero: true);
            go.GetComponent<HealthComponent>().InitializeHealth(go.GetComponent<HealthComponent>().MaxHealth);
            go.AddComponent<HeroProgress>();
            go.AddComponent<HeroSkills>();
            return go.AddComponent<HeroSuccession>();
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
