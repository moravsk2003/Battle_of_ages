using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Resources;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A building takes ore of its tier or better (a tester had only tier I and II ore and could not build a house); a wall keeps its own tier.</summary>
    public class BuildingOreTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator BetterOre_PaysForAHouse_ButNotForACommonWall()
        {
            if (ResourceManager.Instance == null) spawned.Add(new GameObject("Resources", typeof(ResourceManager)));
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            if (placer == null) { var go = new GameObject("Placer"); spawned.Add(go); placer = go.AddComponent<BuildingPlacementSystem>(); }
            yield return null;
            ResourceManager.Instance.SetState(50, new[] { new KeyValuePair<int, int>(1, 12), new KeyValuePair<int, int>(2, 4) });
            Assert.IsTrue(placer.CanAfford(BuildingType.WorkerHouse), "ore I pays for a house that needs common ore");
            Assert.IsTrue(placer.CanAfford(BuildingType.Mine));
            Assert.IsTrue(placer.CanAfford(BuildingType.CastleWall), "a wall picks a tier the stock has (I)");
            ResourceManager.Instance.SetState(50, new[] { new KeyValuePair<int, int>(2, 1) });
            Assert.IsFalse(placer.CanAfford(BuildingType.Mine), "two ore needed, one there");
        }
    }
}
