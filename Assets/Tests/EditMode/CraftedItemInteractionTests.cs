using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Crafting;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.Combat;
using BattleOfAges.Building;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class CraftedItemInteractionTests
    {
        private GameObject workbenchObj;
        private WorkbenchNode workbench;
        private GameObject playerObj;
        private PlayerCombatController playerCombat;
        private PlayerResourceHarvester playerHarvester;
        private GameObject villagerObj;
        private RtsUnit villagerUnit;

        [SetUp]
        public void Setup()
        {
            workbenchObj = new GameObject("TestWorkbench");
            workbench = workbenchObj.AddComponent<WorkbenchNode>();

            playerObj = new GameObject("TestPlayer");
            playerObj.AddComponent<CharacterController>();
            playerObj.AddComponent<HealthComponent>();
            playerObj.AddComponent<CombatUnit>();
            playerCombat = playerObj.AddComponent<PlayerCombatController>();
            playerHarvester = playerObj.AddComponent<PlayerResourceHarvester>();

            villagerObj = new GameObject("TestVillager_1");
            villagerObj.AddComponent<CharacterController>();
            villagerObj.AddComponent<HealthComponent>();
            villagerObj.AddComponent<CombatUnit>();
            villagerUnit = villagerObj.AddComponent<RtsUnit>();
        }

        [TearDown]
        public void Teardown()
        {
            if (workbenchObj != null) Object.DestroyImmediate(workbenchObj);
            if (playerObj != null) Object.DestroyImmediate(playerObj);
            if (villagerObj != null) Object.DestroyImmediate(villagerObj);
        }

        [Test]
        public void Workbench_WhenCraftCompletes_AddsToCompletedOutput()
        {
            workbench.Queue.AssignWorker();
            workbench.Queue.EnqueueItem(ItemType.Sword, 3);

            // Fast forward time by 10 seconds to complete craft
            workbench.Queue.Tick(10.0f);

            Assert.AreEqual(1, workbench.CompletedOutput.Count, "CompletedOutput should contain 1 item after craft completes.");
            Assert.AreEqual(ItemType.Sword, workbench.CompletedOutput[0].Type);
            Assert.AreEqual(3, workbench.CompletedOutput[0].Level);
        }

        [Test]
        public void Workbench_ClaimItemToPlayer_EquipsSwordAndIncreasesDamage()
        {
            workbench.Queue.AssignWorker();
            workbench.Queue.EnqueueItem(ItemType.Sword, 4);
            workbench.Queue.Tick(10.0f);

            CombatUnit cu = playerObj.GetComponent<CombatUnit>();
            int baseDamage = cu.AttackDamage;

            bool claimed = workbench.ClaimItemToPlayer(0);
            Assert.IsTrue(claimed, "Item should be claimed successfully.");
            Assert.AreEqual(0, workbench.CompletedOutput.Count, "CompletedOutput should be empty after claiming.");
            Assert.AreEqual(4, playerCombat.EquippedSwordLevel, "Player should have Level 4 sword equipped.");
            Assert.Greater(cu.AttackDamage, baseDamage, "Player attack damage should increase after equipping sword.");
        }

        [Test]
        public void Workbench_ClaimItemToPlayer_EquipsArmorAndIncreasesHealth()
        {
            workbench.Queue.AssignWorker();
            workbench.Queue.EnqueueItem(ItemType.Armor, 5);
            workbench.Queue.Tick(10.0f);

            HealthComponent hp = playerObj.GetComponent<HealthComponent>();
            int baseHp = hp.MaxHealth;

            bool claimed = workbench.ClaimItemToPlayer(0);
            Assert.IsTrue(claimed);
            Assert.AreEqual(5, playerCombat.EquippedArmorLevel);
            Assert.Greater(hp.MaxHealth, baseHp, "Player max health should increase after equipping armor.");
        }

        [Test]
        public void Workbench_ClaimArmor_ReducesIncomingDamageAndDoesNotHeal()
        {
            HealthComponent hp = playerObj.GetComponent<HealthComponent>();
            hp.InitializeHealth(500);
            hp.TakeDamage(100);

            workbench.Queue.AssignWorker();
            workbench.Queue.EnqueueItem(ItemType.Armor, 3);
            workbench.Queue.Tick(10.0f);
            workbench.ClaimItemToPlayer(0);

            Assert.AreEqual(hp.MaxHealth - 100, hp.CurrentHealth, "Putting armor on must not heal the damage already taken.");
            int before = hp.CurrentHealth;
            hp.TakeDamage(100);
            Assert.AreEqual(70, before - hp.CurrentHealth, "Level 3 armor should take 30% off a hit.");
        }

        [Test]
        public void Workbench_ClaimItemToPlayer_EquipsAxeAndIncreasesToolLevel()
        {
            workbench.Queue.AssignWorker();
            workbench.Queue.EnqueueItem(ItemType.Axe, 2);
            workbench.Queue.Tick(10.0f);

            bool claimed = workbench.ClaimItemToPlayer(0);
            Assert.IsTrue(claimed);
            Assert.AreEqual(2, playerHarvester.EquippedAxeLevel, "Player harvester should have Level 2 axe equipped.");
        }

        [Test]
        public void Workbench_EquipNearestVillager_PromotesVillagerToSoldier()
        {
            workbench.Queue.AssignWorker();
            workbench.Queue.EnqueueItem(ItemType.Sword, 3);
            workbench.Queue.Tick(10.0f);

            bool equipped = workbench.EquipNearestVillager(0);
            Assert.IsTrue(equipped, "Nearest villager should be equipped.");
            Assert.AreEqual(0, workbench.CompletedOutput.Count);

            CombatUnit villagerCombat = villagerObj.GetComponent<CombatUnit>();
            Assert.GreaterOrEqual(villagerCombat.AttackDamage, 25, "Equipped villager should have elevated combat damage.");
            StringAssert.Contains("Soldier", villagerObj.name, "Villager name should reflect Soldier promotion.");
        }

        [Test]
        public void CastleWall_HasClimbAssistTrigger()
        {
            GameObject wall = BuildingNode.CreateCastleWallPrefab("TestWall");
            CastleWallClimbTrigger trigger = wall.GetComponentInChildren<CastleWallClimbTrigger>();
            Assert.IsNotNull(trigger, "CastleWall prefab should contain CastleWallClimbTrigger component.");

            Object.DestroyImmediate(wall);
        }
    }
}
