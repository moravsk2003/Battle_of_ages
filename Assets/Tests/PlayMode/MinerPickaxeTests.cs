using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Miners take their pickaxes from the store themselves: going in, and when one is made while they dig.</summary>
    public class MinerPickaxeTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(-6000f, 0f, -6000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            Armory.Instance.Clear();
        }

        [UnityTest]
        public IEnumerator AMiner_TakesThePickaxe_GoingIn_AndOneMadeWhileItDigs()
        {
            Armory.Instance.Clear();
            GameObject mineObj = MineNode.CreateMinePrefab("TestMine");
            mineObj.transform.position = Far;
            spawned.Add(mineObj);
            MineNode mine = mineObj.GetComponent<MineNode>();
            GameObject a = BuildingNode.CreateWorkerNpc(Far + Vector3.right * 6f, "NPC_MinerA");
            GameObject b = BuildingNode.CreateWorkerNpc(Far + Vector3.left * 6f, "NPC_MinerB");
            spawned.Add(a); spawned.Add(b);
            yield return null;
            Equipment gearA = a.GetComponent<Equipment>(), gearB = b.GetComponent<Equipment>();
            Assert.IsNotNull(gearA);

            Armory.Instance.Add(new Item(ItemType.Pickaxe, 2));
            a.GetComponent<NpcResourceGatherer>().EnterMineNow(mine);
            Assert.AreEqual(new Item(ItemType.Pickaxe, 2), gearA[EquipSlot.Weapon], "took it going in");
            b.GetComponent<NpcResourceGatherer>().EnterMineNow(mine);
            Assert.AreNotEqual(ItemType.Pickaxe, gearB[EquipSlot.Weapon].Type, "none left for the second");

            float before = mine.OrePerSecond;
            Armory.Instance.Add(new Item(ItemType.Pickaxe, 1)); // the workbench made one
            Assert.AreEqual(new Item(ItemType.Pickaxe, 1), gearB[EquipSlot.Weapon], "the one digging without took it");
            Assert.Greater(mine.OrePerSecond, before, "and digs faster");
        }
    }
}
