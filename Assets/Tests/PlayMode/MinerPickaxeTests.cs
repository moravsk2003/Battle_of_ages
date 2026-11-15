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

        [UnityTest, Timeout(60000)]
        public IEnumerator TheTool_GoesWithTheJob_AndTheMine_TakesTwo()
        {
            GameObject mineObj = MineNode.CreateMinePrefab("TestMine2");
            mineObj.transform.position = Far + new Vector3(30f, 0f, 0f);
            spawned.Add(mineObj);
            MineNode mine = mineObj.GetComponent<MineNode>();
            GameObject field = Utils.ZoneBuildingBuilder.CreateField("TestToolField");
            field.transform.position = Far + new Vector3(30f, 0f, 20f);
            spawned.Add(field);
            var crew = new NpcResourceGatherer[3];
            for (int i = 0; i < crew.Length; i++)
            {
                GameObject w = BuildingNode.CreateWorkerNpc(Far + new Vector3(24f + i * 2f, 0f, 6f), "NPC_ToolCrew" + i);
                spawned.Add(w);
                crew[i] = w.GetComponent<NpcResourceGatherer>();
            }
            yield return null;
            Assert.AreEqual(2, MineNode.MAX_WORKERS, "two at a time in a mine");
            Armory.Instance.Add(new Item(ItemType.Pickaxe, 3));
            crew[0].EnterMineNow(mine);
            crew[1].EnterMineNow(mine);
            crew[2].EnterMineNow(mine);
            Assert.AreEqual(2, mine.AssignedWorkerCount, "the third does not get in");
            Assert.IsTrue(mine.IsFullWorkers);
            Equipment first = crew[0].GetComponent<Equipment>(), second = crew[1].GetComponent<Equipment>();
            Assert.AreEqual(new Item(ItemType.Pickaxe, 3), first[EquipSlot.Weapon], "the first digger took the only pickaxe");
            Assert.AreNotEqual(ItemType.Pickaxe, second[EquipSlot.Weapon].Type);

            // The first goes to the field: its pickaxe goes back to the store, and on to the one digging without
            crew[0].LeaveMine();
            Assert.IsTrue(crew[0].AssignToPlace(field.GetComponent<WorkPlace>(), true));
            Assert.IsNull(crew[0].ToolWanted, "a field takes no tool");
            yield return new WaitForSeconds(NpcResourceGatherer.ToolEvery + 1.5f);
            Assert.AreNotEqual(ItemType.Pickaxe, first[EquipSlot.Weapon].Type, "the farmer put the pickaxe back");
            Assert.AreEqual(new Item(ItemType.Pickaxe, 3), second[EquipSlot.Weapon], "and the digger has it now");
        }
    }
}
