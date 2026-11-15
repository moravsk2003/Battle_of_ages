using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The tester's editor (KNOWN_ISSUES №21): units of a rank on either side, sides swapped, trees planted.</summary>
    public class DevToolsTests
    {
        private static readonly Vector3 Far = new Vector3(7000f, 0f, 7000f);
        private GameObject ground;

        [SetUp]
        public void SetUp()
        {
            ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(400f, 1f, 400f);
        }

        [TearDown]
        public void TearDown()
        {
            NpcKingdom.RemoveAll();
            foreach (RtsUnit u in Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None)) Object.Destroy(u.gameObject);
            foreach (HostileMobAI m in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            GameObject tree = GameObject.Find("EditorTree");
            if (tree != null) Object.Destroy(tree);
            Object.Destroy(ground);
        }

        [UnityTest]
        public IEnumerator UnitsOfARank_OnEitherSide_AndTheyChangeSides()
        {
            GameObject knight = DevTools.SpawnOwn(UnitRank.Knight, Far);
            yield return null;
            Assert.IsNotNull(knight.GetComponent<RtsUnit>(), "one of the player's");
            Assert.AreEqual(UnitRank.Knight, knight.GetComponent<UnitProgress>().Rank);

            HostileMobAI enemy = DevTools.SpawnEnemy(UnitRank.Warrior, Far + new Vector3(10f, 0f, 0f));
            Assert.IsNotNull(enemy.Kingdom, "a kingdom's (the editor's own when none is near)");
            Assert.AreEqual(UnitRank.Warrior, enemy.GetComponent<UnitProgress>().Rank);
            NpcKingdom kingdom = enemy.Kingdom;

            Assert.AreEqual(1, DevTools.GiveToEnemy(new[] { knight.GetComponent<RtsUnit>() }));
            yield return null;
            Assert.IsTrue(knight == null, "gone from the player");
            Assert.AreEqual(2, kingdom.Population, "the kingdom has it");
            bool knightThere = false;
            foreach (HostileMobAI m in kingdom.Subjects) if (m != null && m.GetComponent<UnitProgress>().Rank == UnitRank.Knight) knightThere = true;
            Assert.IsTrue(knightThere, "as the knight it was");

            Assert.IsTrue(DevTools.TakeNearestEnemy(Far, 40f));
            yield return null;
            Assert.AreEqual(1, Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None).Length, "one came over to the player");
            Assert.IsFalse(DevTools.TakeNearestEnemy(Far + new Vector3(150f, 0f, 0f), 40f), "none that near");
        }

        [Test]
        public void ATree_IsPlanted_ToBeFelled()
        {
            GameObject tree = DevTools.PlantTree(Far);
            Assert.IsNotNull(tree.GetComponent<Resources.ResourceNodeTree>());
            Assert.AreEqual(Far, tree.transform.position);
        }
    }
}
