using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class SmartAITests
    {
        private GameObject mobObj;
        private HostileMobAI mobAI;
        private GameObject playerObj;
        private HealthComponent playerHp;

        [SetUp]
        public void Setup()
        {
            mobObj = new GameObject("TestSmartMob");
            mobObj.AddComponent<CharacterController>();
            mobObj.AddComponent<HealthComponent>();
            mobObj.AddComponent<CombatUnit>();
            mobAI = mobObj.AddComponent<HostileMobAI>();
            mobAI.InitializeMob(HostileMobType.Bandit);

            playerObj = new GameObject("Player");
            playerObj.AddComponent<CharacterController>();
            playerHp = playerObj.AddComponent<HealthComponent>();
            playerHp.InitializeHealth(500);
        }

        [TearDown]
        public void Teardown()
        {
            if (mobObj != null) Object.DestroyImmediate(mobObj);
            if (playerObj != null) Object.DestroyImmediate(playerObj);
        }

        [Test]
        public void HostileMob_GetFlankingDestination_CalculatesPerimeterPosition()
        {
            Vector3 targetCenter = new Vector3(10f, 0f, 10f);
            float desiredRadius = 2.5f;

            Vector3 flankPos = mobAI.GetFlankingDestination(targetCenter, desiredRadius);
            float distToTarget = Vector3.Distance(new Vector3(flankPos.x, 0, flankPos.z), targetCenter);

            Assert.AreEqual(desiredRadius, distToTarget, 0.05f, "Flanking destination should be positioned on the circle around target.");
        }

        [Test]
        public void HostileMob_WhenHealthFallsBelow25Percent_Retreats()
        {
            HealthComponent hp = mobObj.GetComponent<HealthComponent>();
            mobAI.SetTarget(playerHp);
            mobAI.ChangeState(AIStateType.Attack);

            Assert.AreEqual(AIStateType.Attack, mobAI.CurrentStateType);

            // Deal heavy damage bringing HP below 20%
            hp.TakeDamage(Mathf.RoundToInt(hp.MaxHealth * 0.85f));

            // Tick state
            mobAI.TickAI(0.1f);

            Assert.AreEqual(AIStateType.Retreat, mobAI.CurrentStateType, "Hostile mob must retreat when health drops below retreat threshold.");
        }

        [Test]
        public void PeacefulAnimal_AlertNearbyHerd_PropagatesFleeState()
        {
            GameObject deer1Obj = new GameObject("Deer_1");
            deer1Obj.AddComponent<CharacterController>();
            deer1Obj.AddComponent<HealthComponent>();
            PeacefulAnimalAI deer1 = deer1Obj.AddComponent<PeacefulAnimalAI>();

            GameObject deer2Obj = new GameObject("Deer_2");
            deer2Obj.transform.position = deer1Obj.transform.position + new Vector3(5f, 0f, 0f);
            deer2Obj.AddComponent<CharacterController>();
            deer2Obj.AddComponent<HealthComponent>();
            PeacefulAnimalAI deer2 = deer2Obj.AddComponent<PeacefulAnimalAI>();

            deer1.ChangeState(AIStateType.Idle);
            deer2.ChangeState(AIStateType.Idle);

            Assert.AreEqual(AIStateType.Idle, deer2.CurrentStateType);

            // Deer 1 spots player and alerts herd
            deer1.SetDetectedThreat(playerObj.transform);
            deer1.AlertNearbyHerd(playerObj.transform);

            Assert.AreEqual(AIStateType.Flee, deer2.CurrentStateType, "Neighboring herd members must switch to Flee state upon herd alert.");

            Object.DestroyImmediate(deer1Obj);
            Object.DestroyImmediate(deer2Obj);
        }

        [Test]
        public void Villager_WhenDamaged_FleesTowardsBase()
        {
            GameObject villager = new GameObject("TestVillager");
            villager.AddComponent<CharacterController>();
            HealthComponent hp = villager.AddComponent<HealthComponent>();
            hp.InitializeHealth(100);
            RtsUnit rts = villager.AddComponent<RtsUnit>();
            NpcResourceGatherer gatherer = villager.AddComponent<NpcResourceGatherer>();

            // Villager takes damage
            hp.TakeDamage(25);

            Assert.IsTrue(hp.IsAlive);
            Assert.AreEqual(75, hp.CurrentHealth);

            Object.DestroyImmediate(villager);
        }
    }
}
