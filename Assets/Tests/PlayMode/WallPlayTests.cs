using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Wall segments joining into a line (§10.5): snapping to free ends, shared piers, the gate opening for own.</summary>
    public class WallPlayTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(-5000f, 0f, 5000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            foreach (WallPier p in new List<WallPier>(WallPier.Active)) if (p != null) Object.Destroy(p.gameObject);
        }

        private WallSegment Segment(BuildingType type, Vector3 at, float yaw)
        {
            GameObject go = BuildingNode.CreateWallPrefab(type, "TestWall_" + spawned.Count);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            spawned.Add(go);
            return go.GetComponent<WallSegment>();
        }

        private static int PiersNear(Vector3 at, float radius)
        {
            int n = 0;
            foreach (WallPier p in WallPier.Active) if (p != null && (p.transform.position - at).magnitude < radius) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator NextSegment_SnapsOn_KeepingTheOutside()
        {
            Segment(BuildingType.CastleWall, Far, 0f);
            yield return null;

            // Straight on past the +x end
            Assert.IsTrue(WallSegment.Snap(Far + new Vector3(4.6f, 0f, 0.2f), 4f, out Vector3 c, out float yaw, out _));
            Assert.Less((c - (Far + new Vector3(4f, 0f, 0f))).magnitude, 0.01f, "the next segment starts at the end");
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, 0f), 0.5f, "facing the same way");

            // A right turn inward (outside = +z): it runs along -z, its outside now +x
            Assert.IsTrue(WallSegment.Snap(Far + new Vector3(2.2f, 0f, -2.5f), 4f, out c, out yaw, out _));
            Assert.Less((c - (Far + new Vector3(2f, 0f, -2f))).magnitude, 0.01f);
            Vector3 outside = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Assert.Greater(outside.x, 0.99f, "outside stays outside");

            // Far from any end: no snap
            Assert.IsFalse(WallSegment.Snap(Far + new Vector3(0f, 0f, 9f), 4f, out _, out _, out _));
        }

        [UnityTest]
        public IEnumerator JoinedSegments_SharePiers_AndAJointIsNotFree()
        {
            WallSegment a = Segment(BuildingType.CastleWall, Far, 0f);
            Segment(BuildingType.WallGate, Far + new Vector3(5f, 0f, 0f), 0f); // 6 m: from x 2 to 8
            yield return null;
            yield return null;
            Assert.AreEqual(3, PiersNear(Far, 20f), "two segments in a line: three piers");
            Assert.IsFalse(WallSegment.Snap(Far + new Vector3(2.3f, 0f, 1f), 4f, out _, out _, out _), "the joint is taken (and the free ends are far)");

            Object.Destroy(a.gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(2, PiersNear(Far, 20f), "the free end's pier went with its segment, the shared one stays");
        }

        [UnityTest]
        public IEnumerator Gate_OpensForThePlayer_AndCloses()
        {
            WallSegment gate = Segment(BuildingType.WallGate, Far, 0f);
            WallGateDoors doors = gate.GetComponent<WallGateDoors>();
            var player = new GameObject("Player");
            spawned.Add(player);
            player.transform.position = Far + new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0f, doors.Angle, 0.01f, "shut with nobody near");

            player.transform.position = Far + new Vector3(0f, 0f, GameBalance.GateOpenRadius * 0.5f);
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(WallGateDoors.OpenAngle, doors.Angle, 0.5f, "open for the player");

            player.transform.position = Far + new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(0f, doors.Angle, 0.5f, "shut again");
        }
    }
}
