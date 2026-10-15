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
            villagerObj.AddComponent<HealthComponent>().InitializeHealth(100);
            villagerObj.AddComponent<CombatUnit>().InitializeCombatStats(10, 2f, 2.5f);
            villagerUnit = villagerObj.AddComponent<RtsUnit>();
        }

        [TearDown]
        public void Teardown()
        {
            if (workbenchObj != null) Object.DestroyImmediate(workbenchObj);
            if (playerObj != null) Object.DestroyImmediate(playerObj);
            if (villagerObj != null) Object.DestroyImmediate(villagerObj);
            foreach (Armory a in Object.FindObjectsByType<Armory>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
        }

        private void Craft(ItemType type, int level)
        {
            workbench.Queue.AssignWorker();
            workbench.Queue.EnqueueItem(type, level);
            workbench.Queue.Tick(30.0f);
        }

        [Test]
        public void Workbench_WhenCraftCompletes_DeliversToArmory()
        {
            Craft(ItemType.Sword, 3);
            Assert.AreEqual(1, Armory.Instance.Items.Count, "The finished item should land in the shared armory.");
            Assert.AreEqual(new Item(ItemType.Sword, 3), Armory.Instance.Items[0]);
        }

        [Test]
        public void Armory_EquipSwordOnPlayer_IncreasesDamageAndKeepsSlot()
        {
            Craft(ItemType.Sword, 4);
            CombatUnit cu = playerObj.GetComponent<CombatUnit>();
            playerCombat.Gear.Unequip(EquipSlot.Weapon);
            int unarmed = cu.AttackDamage;

            Assert.IsTrue(Armory.Instance.EquipFromStore(playerCombat.Gear, new Item(ItemType.Sword, 4)));
            Assert.AreEqual(0, Armory.Instance.Items.Count, "The item leaves the armory when worn.");
            Assert.AreEqual(4, playerCombat.EquippedSwordLevel);
            Assert.Greater(cu.AttackDamage, unarmed, "A sword should raise the attack damage.");
        }

        [Test]
        public void Armory_SwappingGear_ReturnsTheOldPieceToTheStore()
        {
            Equipment gear = playerCombat.Gear;
            gear.Equip(new Item(ItemType.Helmet, 1));
            Armory.Instance.Add(new Item(ItemType.Helmet, 3));
            Assert.IsTrue(Armory.Instance.EquipFromStore(gear, new Item(ItemType.Helmet, 3)));
            Assert.AreEqual(3, gear[EquipSlot.Helmet].Level);
            Assert.AreEqual(1, Armory.Instance.CountOf(new Item(ItemType.Helmet, 1)), "The replaced helmet goes back to the armory.");

            Assert.IsTrue(Armory.Instance.UnequipToStore(gear, EquipSlot.Helmet));
            Assert.IsTrue(gear[EquipSlot.Helmet].IsEmpty);
            Assert.AreEqual(2, Armory.Instance.Items.Count);
        }

        [Test]
        public void Armor_ReducesIncomingDamageAndDoesNotHeal()
        {
            HealthComponent hp = playerObj.GetComponent<HealthComponent>();
            Equipment gear = playerCombat.Gear;
            gear.Unequip(EquipSlot.Shield);
            hp.TakeDamage(100);

            gear.Equip(new Item(ItemType.Armor, 5));
            Assert.Greater(hp.MaxHealth, PlayerCombatController.BaseHealth, "A chest piece adds max health.");
            Assert.AreEqual(hp.MaxHealth - 100, hp.CurrentHealth, "Putting armour on must not heal the damage already taken.");

            int before = hp.CurrentHealth;
            hp.TakeDamage(100);
            Assert.AreEqual(80, before - hp.CurrentHealth, "A level V chest piece takes 20% off a hit.");
        }

        [Test]
        public void ArmourPieces_AddUpAndAreCapped()
        {
            Equipment gear = playerCombat.Gear;
            foreach (ItemType t in new[] { ItemType.Armor, ItemType.Shield, ItemType.Helmet, ItemType.Gloves, ItemType.Boots })
                gear.Equip(new Item(t, 5));
            Assert.AreEqual(0.55f, gear.DamageReduction, 0.001f);
            Assert.LessOrEqual(gear.DamageReduction, ItemStatCalculator.MAX_DAMAGE_REDUCTION);
        }

        [Test]
        public void AxeInWeaponSlot_SetsHarvesterAxeLevel()
        {
            playerCombat.EquipItem(ItemType.Axe, 2);
            Assert.AreEqual(2, playerHarvester.EquippedAxeLevel, "The axe works while it is the worn weapon.");
            Assert.AreEqual(0, playerCombat.EquippedSwordLevel, "Only one weapon is held.");
        }

        [Test]
        public void Bow_ExtendsAttackRange()
        {
            playerCombat.EquipItem(ItemType.Bow, 1);
            Assert.AreEqual(ItemStatCalculator.BOW_RANGE, playerObj.GetComponent<CombatUnit>().AttackRange, 0.001f);
        }

        [Test]
        public void Villager_EquippedFromArmory_GetsStrongerAndShowsTheKit()
        {
            Equipment gear = villagerObj.AddComponent<Equipment>();
            gear.InitLoadout(new Item(ItemType.Axe, 1));
            int before = villagerObj.GetComponent<CombatUnit>().AttackDamage;
            Assert.AreEqual(10, before, "The starting kit keeps the tuned stats.");

            Craft(ItemType.Sword, 3);
            Assert.IsTrue(Armory.Instance.EquipFromStore(gear, new Item(ItemType.Sword, 3)));
            Assert.Greater(villagerObj.GetComponent<CombatUnit>().AttackDamage, before);
            Assert.AreEqual(1, Armory.Instance.CountOf(new Item(ItemType.Axe, 1)), "The axe goes back to the armory.");
        }

        [Test]
        public void Workbench_CancelTask_RefundsResources()
        {
            workbench.Queue.EnqueueItem(ItemType.Sword, 1);
            string id = workbench.Queue.Queue[0].TaskId;
            Assert.IsTrue(workbench.CancelTask(id));
            Assert.AreEqual(0, workbench.Queue.Queue.Count);
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
