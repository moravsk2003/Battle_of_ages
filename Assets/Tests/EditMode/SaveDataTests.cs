using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class SaveDataTests
    {
        [TearDown]
        public void Teardown() => WorldPersistence.Clear();

        [Test]
        public void SaveData_RoundTripsThroughJson()
        {
            var d = new SaveData { Wood = 37, PlaySeconds = 125f };
            d.Ore.Add(new TierCount(0, 12));
            d.Ore.Add(new TierCount(3, 1));
            d.Armory.Add(new Item(ItemType.Sword, 0));
            d.Armory.Add(new Item(ItemType.Helmet, 4));
            d.Player.Gear[0] = new Item(ItemType.Sword, 2);
            d.Buildings.Add(new BuildingSave { Type = 2, Position = new Vector3(1, 2, 3) });
            d.Units.Add(new UnitSave { Name = "NPC_Villager_3", Villager = true, InsideMine = 0 });
            d.World.Trees.Add(new TreeSave { Key = "4,-2:7", State = (int)TreeState.WoodPile, Wood = 6 });

            SaveData r = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(d));
            Assert.AreEqual(37, r.Wood);
            Assert.AreEqual(13, r.OreTotal);
            Assert.AreEqual(new Item(ItemType.Sword, 0), r.Armory[0], "A level 0 sword survives (not an empty slot).");
            Assert.AreEqual(new Item(ItemType.Sword, 2), r.Player.Gear[0]);
            Assert.IsTrue(r.Player.Gear[1].IsEmpty);
            Assert.AreEqual(new Vector3(1, 2, 3), r.Buildings[0].Position);
            Assert.AreEqual(0, r.Units[0].InsideMine);
            Assert.AreEqual("4,-2:7", r.World.Trees[0].Key);
        }

        [Test]
        public void WorldPersistence_KeepsAFelledTreeAcrossReload()
        {
            var go = new GameObject("Tree");
            var tree = go.AddComponent<ResourceNodeTree>();
            WorldPersistence.RegisterTree(tree, "1,1:0");
            tree.ApplySavedState(TreeState.WoodPile, 4);
            WorldPersistence.RecordTree(tree);           // chunk unloads
            WorldSave saved = WorldPersistence.Capture();
            Object.DestroyImmediate(go);

            WorldPersistence.Load(saved);
            var again = new GameObject("Tree").AddComponent<ResourceNodeTree>();
            WorldPersistence.RegisterTree(again, "1,1:0"); // chunk builds it again
            Assert.AreEqual(TreeState.WoodPile, again.CurrentState);
            Assert.AreEqual(4, again.WoodRemaining);
            Object.DestroyImmediate(again.gameObject);
        }
    }
}
