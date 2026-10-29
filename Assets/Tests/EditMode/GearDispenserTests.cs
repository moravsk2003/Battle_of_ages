using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Crafting;

namespace BattleOfAges.Tests
{
    /// <summary>Handing gear out of the armory: a miner's own pickaxe, one item to each chosen unit, the best from the store.</summary>
    [TestFixture]
    public class GearDispenserTests
    {
        private readonly List<GameObject> made = new List<GameObject>();

        private Equipment Unit(params Item[] worn)
        {
            var go = new GameObject("GearUnit");
            made.Add(go);
            Equipment e = go.AddComponent<Equipment>();
            e.SetBaseStats(20, 2, 1f, 3f);
            foreach (Item i in worn) e.Equip(i);
            return e;
        }

        [SetUp]
        public void SetUp() => Armory.Instance.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made) if (go != null) Object.DestroyImmediate(go);
            made.Clear();
            foreach (Armory a in Object.FindObjectsByType<Armory>(FindObjectsSortMode.None)) Object.DestroyImmediate(a.gameObject);
        }

        [Test]
        public void AWorker_TakesTheBestPickaxe_ButNeverDropsItsSword()
        {
            Armory.Instance.Add(new Item(ItemType.Pickaxe, 1));
            Armory.Instance.Add(new Item(ItemType.Pickaxe, 3));
            Equipment villager = Unit(new Item(ItemType.Axe, 1));
            Assert.IsTrue(GearDispenser.GiveTool(villager, ItemType.Pickaxe));
            Assert.AreEqual(new Item(ItemType.Pickaxe, 3), villager[EquipSlot.Weapon], "the best one");
            Assert.AreEqual(1, Armory.Instance.CountOf(new Item(ItemType.Axe, 1)), "its axe went to the store");
            Assert.IsFalse(GearDispenser.GiveTool(villager, ItemType.Pickaxe), "the store's pickaxe I is no better");

            Equipment warrior = Unit(new Item(ItemType.Sword, 2));
            Assert.IsFalse(GearDispenser.GiveTool(warrior, ItemType.Pickaxe), "a sword is not swapped for a tool");
            Assert.AreEqual(ItemType.Sword, warrior[EquipSlot.Weapon].Type);

            Equipment empty = Unit();
            Assert.IsTrue(GearDispenser.GiveTool(empty, ItemType.Pickaxe), "empty hands take one");
        }

        [Test]
        public void OneClick_GivesAPieceToEachChosen_WhileTheStoreHasIt()
        {
            var helmet = new Item(ItemType.Helmet, 2);
            Armory.Instance.Add(helmet); Armory.Instance.Add(helmet);
            var units = new List<Equipment> { Unit(helmet), Unit(), Unit(), Unit() };
            Assert.AreEqual(2, GearDispenser.GiveEach(units, helmet), "the one wearing it is skipped, two in store");
            Assert.AreEqual(helmet, units[1][EquipSlot.Helmet]);
            Assert.AreEqual(helmet, units[2][EquipSlot.Helmet]);
            Assert.IsTrue(units[3][EquipSlot.Helmet].IsEmpty);
            Assert.AreEqual(0, Armory.Instance.CountOf(helmet));
        }

        [Test]
        public void TheBestFromTheStore_UpgradesArmour_AndTheSameKindOfWeapon_Only()
        {
            Armory.Instance.Add(new Item(ItemType.Helmet, 1));
            Armory.Instance.Add(new Item(ItemType.Helmet, 3));
            Armory.Instance.Add(new Item(ItemType.Boots, 1));
            Armory.Instance.Add(new Item(ItemType.Bow, 4));
            Armory.Instance.Add(new Item(ItemType.Sword, 3));
            Equipment swordsman = Unit(new Item(ItemType.Sword, 1), new Item(ItemType.Boots, 2));
            Equipment miner = Unit(new Item(ItemType.Pickaxe, 1));
            int n = GearDispenser.UpgradeAll(new List<Equipment> { swordsman, miner });
            Assert.AreEqual(new Item(ItemType.Sword, 3), swordsman[EquipSlot.Weapon], "a better sword, not the bow");
            Assert.AreEqual(new Item(ItemType.Helmet, 3), swordsman[EquipSlot.Helmet]);
            Assert.AreEqual(new Item(ItemType.Boots, 2), swordsman[EquipSlot.Boots], "worse boots are left in the store");
            Assert.AreEqual(ItemType.Pickaxe, miner[EquipSlot.Weapon].Type, "the miner keeps its pickaxe");
            Assert.AreEqual(new Item(ItemType.Helmet, 1), miner[EquipSlot.Helmet], "and gets the helmet left");
            Assert.AreEqual(new Item(ItemType.Boots, 1), miner[EquipSlot.Boots], "and the boots too worn for the swordsman");
            Assert.AreEqual(4, n);
        }
    }
}
