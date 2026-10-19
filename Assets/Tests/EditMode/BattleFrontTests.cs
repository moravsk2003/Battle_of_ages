using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    /// <summary>The pull of a battle: ±5% for a side's units moving to or from its enemies, only while they fight (§11.1).</summary>
    [TestFixture]
    public class BattleFrontTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private readonly List<int> ids = new List<int>();

        [TearDown]
        public void TearDown()
        {
            foreach (int id in ids) UnitSpatialGrid.Unregister(id);
            ids.Clear();
            foreach (GameObject g in objects) Object.DestroyImmediate(g);
            objects.Clear();
            BattleFront.Recompute();
        }

        private void Agent(Vector3 position, int team)
        {
            var go = new GameObject("Agent", typeof(HealthComponent));
            go.transform.position = position;
            HealthComponent hp = go.GetComponent<HealthComponent>();
            hp.InitializeHealth(100);
            objects.Add(go);
            ids.Add(UnitSpatialGrid.Register(hp, hp, team, 0.4f));
        }

        [Test]
        public void InBattle_TowardsTheEnemyIsFaster_AwayIsSlower()
        {
            Agent(new Vector3(0f, 0f, 0f), UnitSpatialGrid.TeamPlayer);
            Agent(new Vector3(0f, 0f, 1f), UnitSpatialGrid.TeamPlayer);
            Agent(new Vector3(0f, 0f, 15f), UnitSpatialGrid.TeamHostile); // 15 m north: within the battle radius
            BattleFront.Recompute();

            Assert.IsTrue(BattleFront.InBattle(UnitSpatialGrid.TeamPlayer));
            Assert.AreEqual(1.05f, BattleFront.Factor(UnitSpatialGrid.TeamPlayer, Vector3.forward), 1e-4f, "Towards the enemy: +5%.");
            Assert.AreEqual(0.95f, BattleFront.Factor(UnitSpatialGrid.TeamPlayer, Vector3.back), 1e-4f, "Away: -5%.");
            Assert.AreEqual(1f, BattleFront.Factor(UnitSpatialGrid.TeamPlayer, Vector3.right), 1e-4f, "Sideways: no change.");
            Assert.AreEqual(1.05f, BattleFront.Factor(UnitSpatialGrid.TeamHostile, Vector3.back), 1e-4f, "The other side is pulled the other way.");
        }

        [Test]
        public void NoBattle_NoPull()
        {
            Agent(new Vector3(0f, 0f, 0f), UnitSpatialGrid.TeamPlayer);
            Agent(new Vector3(0f, 0f, 200f), UnitSpatialGrid.TeamHostile); // far beyond the battle radius
            BattleFront.Recompute();

            Assert.IsFalse(BattleFront.InBattle(UnitSpatialGrid.TeamPlayer));
            Assert.AreEqual(1f, BattleFront.Factor(UnitSpatialGrid.TeamPlayer, Vector3.back), 1e-5f);
        }
    }
}
