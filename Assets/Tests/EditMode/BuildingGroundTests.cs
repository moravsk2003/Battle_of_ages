using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Building;

namespace BattleOfAges.Tests
{
    /// <summary>Buildings go only on ground level enough for them (a player report: a house half sunk into a hillside).</summary>
    public class BuildingGroundTests
    {
        /// <summary>A plane rising along +z at the given angle through the origin.</summary>
        private static System.Func<Vector3, float> Slope(float degrees) => p => p.z * Mathf.Tan(degrees * Mathf.Deg2Rad);

        private static Vector3 HouseSize => BuildingData.GetDefinition(BuildingType.WorkerHouse).Size;

        [Test]
        public void HouseOnAHillside_IsRefused()
        {
            Assert.IsFalse(BuildingPlacementSystem.GroundLevel(Vector3.zero, HouseSize, Slope(35f), out _), "35 degrees is too steep");
        }

        [Test]
        public void HouseOnGentleGround_IsAllowed_AboveTheMiddle()
        {
            Assert.IsTrue(BuildingPlacementSystem.GroundLevel(Vector3.zero, HouseSize, Slope(8f), out float h), "8 degrees is fine");
            float top = HouseSize.z * 0.5f * Mathf.Tan(8f * Mathf.Deg2Rad);
            Assert.Greater(h, 0f, "a little above the middle of the slope");
            Assert.Less(h, top, "but not above its top");
        }

        [Test]
        public void WorldSettlements_GoToTheNearestLevelGround()
        {
            // A 25-degree hillside with one level terrace, 40 m east of the wanted spot
            System.Func<Vector3, float> ground = p => (p - new Vector3(40f, 0f, 0f)).magnitude < 12f ? 5f : p.z * Mathf.Tan(25f * Mathf.Deg2Rad) + 5f;
            Assert.IsTrue(BuildSite.FindLevel(Vector3.zero, 7f, 150f, 1.2f, ground, out Vector3 site), "a level spot is found");
            Assert.Less((site - new Vector3(40f, 5f, 0f)).magnitude, 12f, "on the terrace");
            Assert.AreEqual(5f, site.y, 1e-3f);
            Assert.IsFalse(BuildSite.FindLevel(Vector3.zero, 14f, 24f, 1.6f, Slope(25f), out _), "a camp on a bare hillside: none");
        }

        [Test]
        public void FlatGround_IsAllowed_AtItsHeight()
        {
            Assert.IsTrue(BuildingPlacementSystem.GroundLevel(Vector3.zero, HouseSize, p => 3f, out float h));
            Assert.AreEqual(3f, h, 1e-4f);
        }
    }
}
