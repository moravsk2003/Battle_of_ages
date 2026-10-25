using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// The walkway on the walls (§10.5): posts for defenders behind the breastwork, and the way to one - up the stairs
    /// of a segment with stairs and along the walkway across the joints to the segment of the post. Segments joined end
    /// to end make one walkway; a part of it with no stairs cannot be reached.
    /// </summary>
    public static class WallWalk
    {
        public struct Post
        {
            public WallSegment Wall;
            public int Slot;
            public bool Valid => Wall != null;
            public Post(WallSegment wall, int slot) { Wall = wall; Slot = slot; }
        }

        /// <summary>Posts this far apart along a segment; where on the walkway they are and the line walked along it.</summary>
        public const float PostSpacing = 1.2f;
        public const float PostZ = WallBuilder.Thickness * 0.5f - WallBuilder.ParapetT - 0.35f, WalkZ = -0.25f;

        private static readonly Dictionary<WallSegment, RtsUnit[]> taken = new Dictionary<WallSegment, RtsUnit[]>();

        public static int SlotCount(WallSegment s) => Mathf.Max(1, Mathf.FloorToInt((s.Length - 0.8f) / PostSpacing));

        private static float SlotX(WallSegment s, int i) => (i - (SlotCount(s) - 1) * 0.5f) * PostSpacing;

        public static Vector3 PostPoint(Post p) => p.Wall.transform.TransformPoint(new Vector3(SlotX(p.Wall, p.Slot), WallBuilder.WalkY, PostZ));

        private static Vector3 WalkPoint(WallSegment s, float x) => s.transform.TransformPoint(new Vector3(x, WallBuilder.WalkY, WalkZ));

        // ------------------------------------------------------------------ who stands where

        public static RtsUnit Holder(Post p) =>
            p.Valid && taken.TryGetValue(p.Wall, out RtsUnit[] slots) && p.Slot < slots.Length && slots[p.Slot] != null && slots[p.Slot].IsAlive ? slots[p.Slot] : null;

        public static void Reserve(Post p, RtsUnit who)
        {
            Release(who);
            if (!taken.TryGetValue(p.Wall, out RtsUnit[] slots) || slots.Length != SlotCount(p.Wall)) taken[p.Wall] = slots = new RtsUnit[SlotCount(p.Wall)];
            slots[p.Slot] = who;
        }

        public static void Release(RtsUnit who)
        {
            foreach (RtsUnit[] slots in taken.Values)
                for (int i = 0; i < slots.Length; i++) if (slots[i] == who) slots[i] = null;
        }

        // ------------------------------------------------------------------ the walkway's graph

        /// <summary>The segments joined to s end to end (their walkways meet).</summary>
        private static IEnumerable<WallSegment> Neighbours(WallSegment s)
        {
            foreach (WallSegment o in WallSegment.Active)
            {
                if (o == null || o == s) continue;
                if (Joint(s, o, out _, out _)) yield return o;
            }
        }

        /// <summary>Whether a and b share an end, and which end of each (-1 the A end, +1 the B end).</summary>
        private static bool Joint(WallSegment a, WallSegment b, out float endOfA, out float endOfB)
        {
            endOfA = endOfB = 0f;
            for (int i = 0; i < 2; i++)
                for (int j = 0; j < 2; j++)
                {
                    Vector3 pa = i == 0 ? a.EndA : a.EndB, pb = j == 0 ? b.EndA : b.EndB;
                    pa.y = pb.y = 0f;
                    if ((pa - pb).magnitude < WallSegment.JoinDistance) { endOfA = i == 0 ? -1f : 1f; endOfB = j == 0 ? -1f : 1f; return true; }
                }
            return false;
        }

        /// <summary>The chain of segments from the nearest one with stairs to target (breadth first), or null.</summary>
        private static List<WallSegment> ChainFromStairs(WallSegment target)
        {
            var from = new Dictionary<WallSegment, WallSegment> { { target, null } };
            var queue = new Queue<WallSegment>();
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                WallSegment s = queue.Dequeue();
                if (s.Kind == WallBuilder.Kind.Stairs)
                {
                    var chain = new List<WallSegment>();
                    for (WallSegment c = s; c != null; c = from[c]) chain.Add(c); // stairs first, target last
                    return chain;
                }
                foreach (WallSegment n in Neighbours(s))
                    if (!from.ContainsKey(n)) { from[n] = s; queue.Enqueue(n); }
            }
            return null;
        }

        /// <summary>
        /// The way from the ground to the post: the foot of the stairs (on the ground), up the flight, the landing,
        /// along the walkway across each joint, the post. False when no stairs lead up to it.
        /// </summary>
        public static bool PathUp(Post p, List<Vector3> path)
        {
            path.Clear();
            if (!p.Valid) return false;
            List<WallSegment> chain = ChainFromStairs(p.Wall);
            if (chain == null) return false;
            WallSegment stairs = chain[0];
            Transform t = stairs.transform;
            float run = (WallBuilder.StairsLowX - WallBuilder.StairsTopX) / WallBuilder.StairSteps;
            float zc = (WallBuilder.StairsInnerZ + WallBuilder.StairsOuterZ) * 0.5f, landX = -WallBuilder.Length * 0.5f + 0.45f;
            Vector3 foot = t.TransformPoint(new Vector3(WallBuilder.StairsLowX + 0.7f, 0f, zc));
            foot.y = RtsUnit.GetTerrainHeight(foot);
            path.Add(foot);
            path.Add(t.TransformPoint(new Vector3(WallBuilder.StairsLowX - run * 0.5f, WallBuilder.WalkY / WallBuilder.StairSteps, zc)));
            path.Add(t.TransformPoint(new Vector3(WallBuilder.StairsTopX + run * 0.5f, WallBuilder.WalkY, zc)));
            path.Add(t.TransformPoint(new Vector3(landX, WallBuilder.WalkY, zc)));
            path.Add(WalkPoint(stairs, landX));
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                Joint(chain[i], chain[i + 1], out float endA, out float endB);
                // the joint on the walking line, between the two segments' ends (their walkways may differ a little in height)
                Vector3 a = WalkPoint(chain[i], endA * (chain[i].Length * 0.5f - 0.3f)), b = WalkPoint(chain[i + 1], endB * (chain[i + 1].Length * 0.5f - 0.3f));
                path.Add(a);
                path.Add(b);
            }
            path.Add(WalkPoint(p.Wall, SlotX(p.Wall, p.Slot)));
            path.Add(PostPoint(p));
            return true;
        }

        /// <summary>The free post nearest to near that stairs lead up to (none if there is none).</summary>
        public static Post FreePost(Vector3 near, RtsUnit except = null)
        {
            Post best = default;
            float bestD = float.MaxValue;
            var reachable = new HashSet<WallSegment>();
            var queue = new Queue<WallSegment>();
            foreach (WallSegment s in WallSegment.Active)
                if (s != null && s.Kind == WallBuilder.Kind.Stairs && reachable.Add(s)) queue.Enqueue(s);
            while (queue.Count > 0)
                foreach (WallSegment n in Neighbours(queue.Dequeue()))
                    if (reachable.Add(n)) queue.Enqueue(n);
            foreach (WallSegment s in reachable)
                for (int i = 0; i < SlotCount(s); i++)
                {
                    var p = new Post(s, i);
                    RtsUnit holder = Holder(p);
                    if (holder != null && holder != except) continue;
                    float d = (PostPoint(p) - near).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = p; }
                }
            return best;
        }
    }
}
