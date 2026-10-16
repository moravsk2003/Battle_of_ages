using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.Player;
using BattleOfAges.Resources;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Save, change the game, load: the saved state comes back (in a temporary save folder).</summary>
    public class SaveLoadTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_save_test_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            SaveSystem.FolderOverride = null;
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_RestoresResourcesArmoryHeroBuildingsAndTrees()
        {
            yield return SceneManager.LoadSceneAsync(SaveSystem.GameScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);

            ResourceManager res = ResourceManager.Instance;
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Assert.IsNotNull(res);
            Assert.IsNotNull(hero);

            // A known state
            res.SetState(123, new[] { new System.Collections.Generic.KeyValuePair<int, int>(0, 40), new System.Collections.Generic.KeyValuePair<int, int>(2, 3) });
            Armory.Instance.Clear();
            Armory.Instance.Add(new Item(ItemType.Helmet, 3));
            hero.Gear.Equip(new Item(ItemType.Sword, 4));
            Vector3 heroPos = hero.transform.position + new Vector3(3f, 0f, 2f);
            var cc = hero.GetComponent<CharacterController>();
            cc.enabled = false; hero.transform.position = heroPos; cc.enabled = true;
            GameObject mine = BuildingPlacementSystem.CreateBuilding(BuildingType.Mine);
            mine.transform.position = heroPos + new Vector3(8f, 0f, 8f);
            mine.GetComponent<MineNode>().Produce(20);
            int buildings = 0;
            foreach (BuildingNode b in BuildingNode.Active) if (b != null && !b.name.StartsWith("BuildGhost")) buildings++;
            ResourceNodeTree tree = Object.FindFirstObjectByType<ResourceNodeTree>();
            string treeKey = tree != null ? tree.PersistentKey : null;
            if (tree != null) tree.ApplySavedState(TreeState.WoodPile, 5);
            yield return null;

            Assert.IsTrue(SaveSystem.Save(1), "Saving should succeed.");
            Assert.IsTrue(SaveSystem.Meta(1).Exists);

            // Change everything, then load
            res.SetState(0, null);
            Armory.Instance.Clear();
            hero.Gear.Unequip(EquipSlot.Weapon);
            Assert.IsTrue(SaveSystem.Load(1));
            yield return new WaitForSeconds(2.0f);

            res = ResourceManager.Instance;
            hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Assert.AreEqual(123, res.WoodCount);
            Assert.AreEqual(3, res.GetOreTierCount(2));
            Assert.AreEqual(1, Armory.Instance.CountOf(new Item(ItemType.Helmet, 3)));
            Assert.AreEqual(4, hero.EquippedSwordLevel);
            Assert.Less(Vector3.Distance(heroPos, hero.transform.position), 1.0f, "The hero is back where they were saved.");
            int after = 0;
            MineNode loadedMine = null;
            foreach (BuildingNode b in BuildingNode.Active)
                if (b != null && !b.name.StartsWith("BuildGhost")) { after++; if (b.GetComponent<MineNode>() != null) loadedMine = b.GetComponent<MineNode>(); }
            Assert.AreEqual(buildings, after, "The same buildings come back.");
            Assert.IsNotNull(loadedMine);
            Assert.AreEqual(20, loadedMine.AccumulatedOreCount, "The mine keeps its waiting ore.");
            if (!string.IsNullOrEmpty(treeKey))
            {
                ResourceNodeTree same = null;
                foreach (ResourceNodeTree t in Object.FindObjectsByType<ResourceNodeTree>(FindObjectsSortMode.None)) if (t.PersistentKey == treeKey) same = t;
                Assert.IsNotNull(same, "The tree is found again by its key.");
                Assert.AreEqual(TreeState.WoodPile, same.CurrentState, "A felled tree stays felled.");
            }
        }
    }
}
