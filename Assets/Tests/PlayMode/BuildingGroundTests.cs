using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Buildings go only on ground level enough for them (a player report: a house half sunk into a hillside).</summary>
    public class BuildingGroundTests
    {
        private static readonly Vector3 Far = new Vector3(-5000f, 0f, -5000f);

        private static IEnumerator WithGround(float tiltDegrees, System.Action check)
        {
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            ground.transform.SetPositionAndRotation(Far, Quaternion.Euler(tiltDegrees, 0f, 0f));
            box.center = new Vector3(0f, -0.5f, 0f);
            box.size = new Vector3(60f, 1f, 60f);
            Physics.SyncTransforms();
            yield return null;
            try { check(); }
            finally { Object.Destroy(ground); }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HouseOnAHillside_IsRefused()
        {
            Vector3 size = BuildingData.GetDefinition(BuildingType.WorkerHouse).Size;
            yield return WithGround(35f, () => Assert.IsFalse(BuildingPlacementSystem.GroundLevel(Far, size, out _), "35 degrees is too steep"));
        }

        [UnityTest]
        public IEnumerator HouseOnGentleGround_IsAllowed_AboveTheMiddle()
        {
            Vector3 size = BuildingData.GetDefinition(BuildingType.WorkerHouse).Size;
            yield return WithGround(8f, () =>
            {
                Assert.IsTrue(BuildingPlacementSystem.GroundLevel(Far, size, out float h), "8 degrees is fine");
                Assert.Greater(h, Far.y - 0.05f, "a little above the middle of the slope");
                Assert.Less(h, Far.y + size.z * 0.5f * Mathf.Tan(8f * Mathf.Deg2Rad) + 0.05f, "but not above its top");
            });
        }
    }
}
