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

        private void Ground()
        {
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(200f, 1f, 200f);
            spawned.Add(ground);
        }

        /// <summary>p on the ground (the world of an earlier test may still be loaded: its terrain, not the test box).</summary>
        private static Vector3 OnGround(Vector3 p) { p.y = RTS.RtsUnit.GetTerrainHeight(p); return p; }

        /// <summary>A wall from x -2 to 2 and the gate from 2 to 8 (outside +z), on the ground.</summary>
        private IEnumerator WallWithGate()
        {
            Ground();
            Segment(BuildingType.CastleWall, OnGround(Far), 0f);
            Segment(BuildingType.WallGate, OnGround(Far + new Vector3(5f, 0f, 0f)), 0f);
            yield return null;
            yield return null;
        }

        /// <summary>Walks a stand-in from start straight at the target (or along the route) with the ground motor.</summary>
        private static Vector3 Walk(Vector3 start, Vector3 target, bool own, bool route, float seconds = 12f)
        {
            var t = new GameObject("Walker").transform;
            t.position = start;
            const float dt = 0.05f;
            for (float time = 0f; time < seconds; time += dt)
            {
                Vector3 aim = route ? WallBarriers.Route(t.position, target) : target;
                Vector3 d = aim - t.position;
                d.y = 0f;
                if (d.magnitude < 0.2f && aim == target) break;
                RTS.GroundMotor.Move(t, -1, d.normalized * 3f, 0.4f, dt, own);
            }
            Vector3 end = t.position;
            Object.Destroy(t.gameObject);
            return end;
        }

        [UnityTest]
        public IEnumerator Walls_StopEveryone_TheGateOnlyEnemies()
        {
            yield return WallWithGate();
            float face = Far.z + Utils.WallBuilder.Thickness * 0.5f + 0.4f - 0.05f;

            Vector3 enemyAtWall = Walk(Far + new Vector3(0f, 0f, 5f), Far + new Vector3(0f, 0f, -5f), false, false);
            Assert.Greater(enemyAtWall.z, face, "an enemy stops at the wall");
            Vector3 ownAtWall = Walk(Far + new Vector3(0f, 0f, 5f), Far + new Vector3(0f, 0f, -5f), true, false);
            Assert.Greater(ownAtWall.z, face, "so does an own unit going straight at it");

            Vector3 enemyAtGate = Walk(Far + new Vector3(5f, 0f, 5f), Far + new Vector3(5f, 0f, -5f), false, false);
            Assert.Greater(enemyAtGate.z, face, "the gate is shut to enemies");
            Vector3 ownAtGate = Walk(Far + new Vector3(5f, 0f, 5f), Far + new Vector3(5f, 0f, -5f), true, false);
            Assert.Less(ownAtGate.z, Far.z - 4f, "own go through the gate");
        }

        [UnityTest]
        public IEnumerator Villager_SentBehindTheWall_WalksThroughTheGate()
        {
            yield return WallWithGate();
            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(-1f, 0f, 6f)), "WallTestVillager");
            spawned.Add(v);
            yield return null;
            v.GetComponent<RTS.NpcResourceGatherer>().SetGatherDirective(RTS.GatherDirective.Idle);
            Vector3 target = OnGround(Far + new Vector3(0f, 0f, -5f));
            v.GetComponent<RTS.RtsUnit>().IssueMoveCommand(target);
            var track = new System.Text.StringBuilder();
            float mark = 0f;
            for (float t = 0f; t < 20f && Vector3.Distance(v.transform.position, target) > 1.2f; t += Time.deltaTime)
            {
                // never through the wall itself: past its middle only within the gate's opening
                Vector3 p = v.transform.position - Far;
                if (p.z < 0f && p.z > -0.9f) Assert.Greater(p.x, 3.9f - 0.01f, $"walked through the wall at {p}");
                if (t >= mark) { mark += 1f; track.Append($" {p.x:0.0},{p.z:0.0}"); }
                yield return null;
            }
            Assert.Less(Vector3.Distance(v.transform.position, target), 1.5f, $"arrived behind the wall (at {v.transform.position - Far}; track{track})");
        }

        [UnityTest]
        public IEnumerator OwnUnits_GoRoundThroughTheGate()
        {
            yield return WallWithGate();
            Vector3 target = Far + new Vector3(-1f, 0f, -5f);
            Assert.IsTrue(WallBarriers.Blocked(Far + new Vector3(0f, 0f, 5f), target, true));
            Vector3 end = Walk(Far + new Vector3(0f, 0f, 5f), target, true, true, 20f);
            Assert.Less(Vector3.Distance(new Vector3(end.x, 0f, end.z), new Vector3(target.x, 0f, target.z)), 0.5f, $"behind the wall by way of the gate: ended at {end - Far}, heading {WallBarriers.Route(end, target) - Far}, blocked {WallBarriers.Blocked(end, target, true)}");
        }
    }
}
