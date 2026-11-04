using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A worker's axe fells faster (as a pickaxe digs faster): an armload of wood in 2.5 s by hand, 2 s with an axe I.</summary>
    public class AxeChopTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(7000f, 0f, 7000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        private ResourceNodeTree Tree(Vector3 at)
        {
            var go = new GameObject("TestTree");
            at.y = RtsUnit.GetTerrainHeight(at); // a world left by an earlier test has its own ground here
            go.transform.position = at;
            go.AddComponent<CapsuleCollider>();
            spawned.Add(go);
            return go.AddComponent<ResourceNodeTree>();
        }

        private IEnumerator Chop(NpcResourceGatherer worker, ResourceNodeTree tree, float[] seconds)
        {
            worker.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            worker.CommandHarvestTree(tree);
            float end = Time.time + 20f;
            while (worker.CurrentState != GathererState.Harvesting && Time.time < end) yield return null;
            Assert.AreEqual(GathererState.Harvesting, worker.CurrentState,
                $"never got to fell: {worker.CurrentDirective}, at {worker.transform.position} (tree {tree.transform.position}), alarm {SettlementAlarm.Active}, working {worker.IsWorking}");
            float start = Time.time;
            while (worker.CurrentState == GathererState.Harvesting && Time.time < end) yield return null;
            seconds[0] = Time.time - start;
        }

        [Category("Slow")]
        [UnityTest]
        public IEnumerator AnAxeI_FellsAnArmload_AQuarterFaster()
        {
            Armory.Instance.Clear();
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(200f, 1f, 200f);
            spawned.Add(ground);
            if (ResourceManager.Instance == null) spawned.Add(new GameObject("Resources", typeof(ResourceManager)));
            GameObject w = BuildingNode.CreateWorkerNpc(Far, "NPC_Chopper");
            spawned.Add(w);
            yield return null;
            NpcResourceGatherer worker = w.GetComponent<NpcResourceGatherer>();
            Equipment gear = w.GetComponent<Equipment>();
            Assert.AreEqual(1, gear.LevelOf(ItemType.Axe), "a villager starts with an axe I");
            Assert.AreEqual(1.25f, worker.AxeEfficiency(), 1e-4f);

            var t = new float[1];
            yield return Chop(worker, Tree(Far + new Vector3(1.2f, 0f, 0f)), t);
            float withAxe = t[0];
            gear.Unequip(EquipSlot.Weapon);
            yield return Chop(worker, Tree(Far + new Vector3(-1.2f, 0f, 0f)), t);
            float byHand = t[0];
            TestContext.WriteLine($"an armload: with an axe I {withAxe:0.00} s, by hand {byHand:0.00} s");
            Assert.AreEqual(2.0f, withAxe, 0.2f);
            Assert.AreEqual(2.5f, byHand, 0.2f);

            Armory.Instance.Add(new Item(ItemType.Axe, 3));
            yield return Chop(worker, Tree(Far + new Vector3(0f, 0f, 1.2f)), t);
            Assert.AreEqual(new Item(ItemType.Axe, 3), gear[EquipSlot.Weapon], "took the best axe from the store going to fell");
            Assert.AreEqual(2.5f / 1.75f, t[0], 0.2f);
        }
    }
}
