using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// The walls as obstacles for the kinematic units (GroundMotor; the player's CharacterController has the colliders):
    /// each segment a thick line on the ground plane, a pier a disc, the stairs their own strip; the gate's opening is
    /// a leaf that only the player's side passes (§10.5: own units go through the gates only, enemies not at all).
    /// Own units headed across a wall take the shortest way round (Route): through the gates and past the free ends of
    /// the walls, over a small graph of waypoints rebuilt when the walls change.
    /// </summary>
    public static class WallBarriers
    {
        public struct Barrier
        {
            public Vector2 A, B;
            /// <summary>Half the thickness (the disc's radius for a pier).</summary>
            public float Half;
            /// <summary>The gate's opening: open to the player's side.</summary>
            public bool Leaf;
            /// <summary>The walkway's height: a unit up there (stage 4) is not stopped by its own wall.</summary>
            public float TopY;
            public Vector2 Min, Max;
            /// <summary>The segment it belongs to (none for a pier).</summary>
            public WallSegment Owner;

            public Barrier(Vector2 a, Vector2 b, float half, float topY, bool leaf = false)
            {
                A = a; B = b; Half = half; TopY = topY; Leaf = leaf; Owner = null;
                Min = Vector2.Min(a, b) - Vector2.one * half;
                Max = Vector2.Max(a, b) + Vector2.one * half;
            }
        }

        private static readonly List<Barrier> barriers = new List<Barrier>();
        private static int builtFrame = -1, builtCount = -1, builtPiers = -1;

        /// <summary>A gate's centre and its outside (local +Z) on the ground plane.</summary>
        private static readonly List<(Vector2 center, Vector2 outside, Vector2 along)> gates = new List<(Vector2, Vector2, Vector2)>();

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        public static IReadOnlyList<Barrier> All { get { Refresh(); return barriers; } }

        /// <summary>Rebuilt at most once a frame, when a segment or pier came, went or moved.</summary>
        private static void Refresh()
        {
            if (builtFrame == Time.frameCount) return;
            builtFrame = Time.frameCount;
            var segments = WallSegment.Active;
            bool changed = segments.Count != builtCount || WallPier.Active.Count != builtPiers;
            foreach (WallSegment s in segments)
                if (s != null && s.transform.hasChanged) { changed = true; s.transform.hasChanged = false; }
            if (!changed) return;
            version++;
            builtCount = segments.Count;
            builtPiers = WallPier.Active.Count;
            barriers.Clear();
            gates.Clear();
            foreach (WallSegment s in segments)
                if (s != null) Add(s.transform, s.Kind, s);
            foreach (WallPier p in WallPier.Active)
                if (p != null)
                {
                    Vector2 c = Flat(p.transform.position);
                    barriers.Add(new Barrier(c, c, WallBuilder.PierApothem, p.transform.position.y + WallBuilder.WalkY));
                }
        }

        /// <summary>The barriers of a segment of the kind at t (also used for a wall not registered yet).</summary>
        private static void Add(Transform t, WallBuilder.Kind kind, WallSegment owner)
        {
            int first = barriers.Count;
            float half = WallBuilder.LengthOf(kind) * 0.5f, top = t.position.y + WallBuilder.WalkY, th = WallBuilder.Thickness * 0.5f;
            Vector2 P(float x, float z = 0f) => Flat(t.TransformPoint(new Vector3(x, 0f, z)));
            if (kind == WallBuilder.Kind.Gate)
            {
                float w = WallBuilder.GateHalfW;
                barriers.Add(new Barrier(P(-half), P(-w), th, top));
                barriers.Add(new Barrier(P(w), P(half), th, top));
                barriers.Add(new Barrier(P(-w), P(w), th, top, true));
                gates.Add((P(0f), (P(0f, 1f) - P(0f)).normalized, (P(1f) - P(0f)).normalized));
            }
            else
            {
                barriers.Add(new Barrier(P(-half), P(half), th, top));
                if (kind == WallBuilder.Kind.Stairs)
                {
                    float zc = (WallBuilder.StairsInnerZ + WallBuilder.StairsOuterZ) * 0.5f, sw = (WallBuilder.StairsInnerZ - WallBuilder.StairsOuterZ) * 0.5f;
                    barriers.Add(new Barrier(P(-half, zc), P(WallBuilder.StairsLowX, zc), sw, top));
                }
            }
            for (int i = first; i < barriers.Count; i++) { Barrier b = barriers[i]; b.Owner = owner; barriers[i] = b; }
        }

        /// <summary>
        /// The wall segment the straight way from-to runs into first (for enemies: the gates shut), the point on its
        /// middle line where it does (kept off the segment's ends, where the piers stand) and the side from comes from.
        /// </summary>
        public static bool FirstCrossing(Vector3 from, Vector3 to, out WallSegment segment, out Vector3 point, out Vector3 side)
        {
            Refresh();
            segment = null; point = to; side = Vector3.zero;
            Vector2 a = Flat(from), b = Flat(to), r = b - a;
            float best = float.MaxValue;
            Barrier hit = default;
            for (int i = 0; i < barriers.Count; i++)
            {
                Barrier w = barriers[i];
                if (w.Owner == null || !Applies(w, from.y, false)) continue;
                Barrier g = Inflated(w, UnitRadius);
                if (!Crosses(g, a, b)) continue;
                // How far along the way: where it comes closest to the middle line
                Vector2 c = Closest(Vector2.Lerp(a, b, 0.5f), w.A, w.B);
                float along = Vector2.Dot(c - a, r.normalized);
                if (along < best) { best = along; hit = w; segment = w.Owner; }
            }
            if (segment == null) return false;
            Vector2 axis = hit.B - hit.A;
            float len = axis.magnitude;
            Vector2 dir = len > 1e-4f ? axis / len : Vector2.right, n = new Vector2(-dir.y, dir.x);
            // Where the way meets the middle line, else the point of it nearest to from
            float t = len;
            float den = r.x * n.x + r.y * n.y;
            Vector2 onLine = Mathf.Abs(den) > 1e-5f ? a + r * (Vector2.Dot(hit.A - a, n) / den) : Closest(a, hit.A, hit.B);
            float u = Mathf.Clamp(Vector2.Dot(onLine - hit.A, dir), Mathf.Min(1.2f, t * 0.5f), Mathf.Max(t - 1.2f, t * 0.5f));
            Vector2 p = hit.A + dir * u;
            float s = Vector2.Dot(a - p, n) >= 0f ? 1f : -1f;
            point = new Vector3(p.x, segment.transform.position.y, p.y);
            side = new Vector3(n.x * s, 0f, n.y * s);
            return true;
        }

        // ------------------------------------------------------------------ geometry

        private static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return a;
            return a + ab * Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
        }

        /// <summary>Distance between the segments p0-p1 and a-b on the plane.</summary>
        public static float SegmentDistance(Vector2 p0, Vector2 p1, Vector2 a, Vector2 b)
        {
            if (SegmentsCross(p0, p1, a, b)) return 0f;
            float d = (Closest(p0, a, b) - p0).magnitude;
            d = Mathf.Min(d, (Closest(p1, a, b) - p1).magnitude);
            d = Mathf.Min(d, (Closest(a, p0, p1) - a).magnitude);
            return Mathf.Min(d, (Closest(b, p0, p1) - b).magnitude);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool SegmentsCross(Vector2 p0, Vector2 p1, Vector2 a, Vector2 b)
        {
            Vector2 r = p1 - p0, s = b - a;
            float den = Cross(r, s);
            if (Mathf.Abs(den) < 1e-8f) return false;
            float t = Cross(a - p0, s) / den, u = Cross(a - p0, r) / den;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private static bool Applies(in Barrier b, float y, bool own) => !(own && b.Leaf) && y < b.TopY - 1f;

        // ------------------------------------------------------------------ movement

        /// <summary>
        /// The step from p to p + step for a unit of the radius, against the walls: a step deeper into one slides along
        /// it, one that cannot slide stays (a step out of a wall it stands in is allowed). own: the player's side.
        /// Returns the step taken.
        /// </summary>
        public static Vector3 Constrain(Vector3 p, Vector3 step, float radius, bool own)
        {
            Refresh();
            if (barriers.Count == 0) return step;
            for (int pass = 0; pass < 2; pass++)
            {
                if (!Hit(p, step, radius, own, out Vector2 normal)) return step;
                float into = step.x * normal.x + step.z * normal.y;
                if (into >= 0f) return step;
                step -= new Vector3(normal.x, 0f, normal.y) * into; // slide along the wall
            }
            return Hit(p, step, radius, own, out _) ? Vector3.zero : step;
        }

        /// <summary>
        /// Signed distance from p to the barrier (negative inside): a pier a disc, a wall a rectangle with square ends (a
        /// rounded end would close the gate's opening), and the outward normal at the nearest side.
        /// </summary>
        public static float Distance(in Barrier b, Vector2 p, out Vector2 normal)
        {
            Vector2 ab = b.B - b.A;
            float len = ab.magnitude;
            if (len < 1e-4f)
            {
                Vector2 d = p - b.A;
                float m = d.magnitude;
                normal = m > 1e-6f ? d / m : Vector2.up;
                return m - b.Half;
            }
            Vector2 dir = ab / len, perp = new Vector2(-dir.y, dir.x);
            float u = Vector2.Dot(p - b.A, dir), v = Vector2.Dot(p - b.A, perp);
            float dx = Mathf.Max(-u, u - len), dy = Mathf.Abs(v) - b.Half; // > 0: outside along that axis
            Vector2 nx = dir * (u < len * 0.5f ? -1f : 1f), ny = perp * (v >= 0f ? 1f : -1f);
            if (dx > 0f && dy > 0f)
            {
                normal = (nx * dx + ny * dy).normalized;
                return Mathf.Sqrt(dx * dx + dy * dy);
            }
            normal = dx > dy ? nx : ny;
            return Mathf.Max(dx, dy);
        }

        /// <summary>Whether p + step ends inside a wall (further in than p), and the wall's outward normal there.</summary>
        private static bool Hit(Vector3 p, Vector3 step, float radius, bool own, out Vector2 normal)
        {
            normal = Vector2.zero;
            Vector2 from = Flat(p), to = Flat(p + step);
            float best = float.MaxValue;
            bool hit = false;
            for (int i = 0; i < barriers.Count; i++)
            {
                Barrier b = barriers[i];
                if (to.x < b.Min.x - radius || to.x > b.Max.x + radius || to.y < b.Min.y - radius || to.y > b.Max.y + radius) continue;
                if (!Applies(b, p.y, own)) continue;
                float d = Distance(b, to, out Vector2 n) - radius;
                if (d >= 0f) continue;
                float dFrom = Distance(b, from, out _) - radius;
                if (d >= dFrom && dFrom < 0f) continue; // already in it: moving out (or along) is fine
                if (d < best)
                {
                    best = d;
                    hit = true;
                    normal = n;
                }
            }
            return hit;
        }

        /// <summary>Whether a unit of the radius going straight from-to would run into a wall (the gates are open for own).</summary>
        public static bool Blocked(Vector3 from, Vector3 to, bool own, float radius = UnitRadius)
        {
            Refresh();
            Vector2 a = Flat(from), b = Flat(to);
            Vector2 min = Vector2.Min(a, b), max = Vector2.Max(a, b);
            for (int i = 0; i < barriers.Count; i++)
            {
                Barrier w = barriers[i];
                if (max.x < w.Min.x - radius || min.x > w.Max.x + radius || max.y < w.Min.y - radius || min.y > w.Max.y + radius) continue;
                if (!Applies(w, from.y, own)) continue;
                if (Crosses(Inflated(w, radius), a, b)) return true;
            }
            return false;
        }

        /// <summary>The room a way is checked with: a little under a unit's radius, so one walking along a wall's face is not "blocked".</summary>
        public const float UnitRadius = 0.35f;

        /// <summary>The barrier grown by r on every side (the room a unit of radius r needs).</summary>
        private static Barrier Inflated(in Barrier w, float r)
        {
            Vector2 ab = w.B - w.A;
            if (ab.sqrMagnitude < 1e-8f) return new Barrier(w.A, w.B, w.Half + r, w.TopY, w.Leaf) { Owner = w.Owner };
            Vector2 d = ab.normalized * r;
            return new Barrier(w.A - d, w.B + d, w.Half + r, w.TopY, w.Leaf) { Owner = w.Owner };
        }

        /// <summary>Whether the segment from a to b enters the barrier (a standing in it already does not count).</summary>
        private static bool Crosses(in Barrier w, Vector2 a, Vector2 b)
        {
            Vector2 ab = w.B - w.A;
            if (ab.sqrMagnitude < 1e-8f) return SegmentDistance(a, b, w.A, w.A) < w.Half;
            if (Distance(w, b, out _) < 0f) return true;
            Vector2 side = new Vector2(-ab.y, ab.x).normalized * w.Half;
            Vector2 c0 = w.A + side, c1 = w.B + side, c2 = w.B - side, c3 = w.A - side;
            return SegmentsCross(a, b, c0, c1) || SegmentsCross(a, b, c1, c2) || SegmentsCross(a, b, c2, c3) || SegmentsCross(a, b, c3, c0);
        }

        /// <summary>How far before and after a gate a unit lines up to go through.</summary>
        public const float GateApproach = 2.6f;

        // ------------------------------------------------------------------ routing (own units)

        private static int version, graphVersion = -1;
        /// <summary>Goes up whenever a wall or a pier came, went or moved.</summary>
        public static int Version { get { Refresh(); return version; } }
        /// <summary>Waypoints: in front of and behind every gate (on its axis), and round the free ends of the walls.</summary>
        private static readonly List<Vector2> nodes = new List<Vector2>();
        /// <summary>The gate a waypoint lines up with (-1: a wall's end).</summary>
        private static readonly List<int> nodeGate = new List<int>();
        /// <summary>Which waypoints see each other (a straight way between them misses every wall).</summary>
        private static bool[,] sees = new bool[0, 0];
        private static float[] cost = new float[0];
        private static int[] previous = new int[0];
        private static bool[] done = new bool[0];
        private static bool[] fromSees = new bool[0];

        /// <summary>How far off a gate's axis a unit may be and still count as lining up in it; room kept round a wall's end.</summary>
        private const float GateCorridor = 1.5f, EndRoom = UnitRadius + 0.45f;

        /// <summary>
        /// Where an own unit at from should head for to: to itself if no wall is in the way, else the next waypoint of the
        /// shortest way round (through the gates, in front of each and out behind it along its axis, and past the free
        /// ends of the walls). With no way round, to (the unit walks up to the wall and stays there).
        /// </summary>
        public static Vector3 Route(Vector3 from, Vector3 to) => Route(from, to, out _);

        /// <summary>Route; inGateway: the unit is lining up in a gate's corridor (the way is clear: no probes against the jambs).</summary>
        public static Vector3 Route(Vector3 from, Vector3 to, out bool inGateway)
        {
            Refresh();
            inGateway = false;
            if (barriers.Count == 0 || !Blocked(from, to, true)) return to;
            BuildGraph();
            int n = nodes.Count;
            if (n == 0) return to;
            Vector2 f = Flat(from), t = Flat(to);

            // Dijkstra from the unit over the waypoints (a few dozen); the target is reached from any waypoint that sees it
            for (int i = 0; i < n; i++)
            {
                done[i] = false;
                previous[i] = -1;
                // A waypoint the unit stands on is passed: it heads on to the next one (or the target)
                fromSees[i] = (nodes[i] - f).sqrMagnitude > 0.36f && !Blocked(from, To3(nodes[i], from.y), true);
                cost[i] = fromSees[i] ? (nodes[i] - f).magnitude : float.MaxValue;
            }
            int last = -1;
            float best = float.MaxValue;
            for (int step = 0; step < n; step++)
            {
                int u = -1;
                for (int i = 0; i < n; i++) if (!done[i] && cost[i] < float.MaxValue && (u < 0 || cost[i] < cost[u])) u = i;
                if (u < 0 || cost[u] >= best) break;
                done[u] = true;
                float toTarget = cost[u] + (t - nodes[u]).magnitude;
                if (toTarget < best && !Blocked(To3(nodes[u], from.y), to, true)) { best = toTarget; last = u; }
                for (int w = 0; w < n; w++)
                {
                    if (done[w] || !sees[u, w]) continue;
                    float c = cost[u] + (nodes[w] - nodes[u]).magnitude;
                    if (c < cost[w]) { cost[w] = c; previous[w] = u; }
                }
            }
            if (last < 0) return to;
            int first = last;
            while (previous[first] >= 0) first = previous[first];

            int gate = nodeGate[first];
            if (gate >= 0)
            {
                // Lining up in the gate's corridor (to the point behind it, or out of it): no whiskers against the jambs
                (Vector2 g, Vector2 axis, Vector2 along) = gates[gate];
                Vector2 rel = f - g;
                inGateway = Mathf.Abs(Vector2.Dot(rel, along)) < GateCorridor && Mathf.Abs(Vector2.Dot(rel, axis)) < GateApproach + 0.5f;
            }
            return To3(nodes[first], from.y);
        }

        private static Vector3 To3(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        /// <summary>The waypoints and which see which: once per change of the walls.</summary>
        private static void BuildGraph()
        {
            if (graphVersion == version) return;
            graphVersion = version;
            nodes.Clear();
            nodeGate.Clear();
            for (int i = 0; i < gates.Count; i++)
            {
                (Vector2 g, Vector2 axis, _) = gates[i];
                foreach (float side in new[] { 1f, -1f })
                    // As far as GateApproach out, closer if another wall stands right behind the gate
                    foreach (float d in new[] { GateApproach, 1.9f, 1.3f })
                    {
                        Vector2 p = g + axis * side * d;
                        if (!Free(p)) continue;
                        nodes.Add(p);
                        nodeGate.Add(i);
                        break;
                    }
            }
            // Round the ends of every wall (a joint's corners lie in the next wall and drop out): the corners of the
            // segment's outline grown by the room a unit needs, and round each pier
            foreach (Barrier b in barriers)
            {
                if (b.Leaf) continue;
                Vector2 ab = b.B - b.A;
                float r = b.Half + EndRoom;
                if (ab.sqrMagnitude < 1e-6f)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float a = (45f + 90f * k) * Mathf.Deg2Rad;
                        TryEnd(b.A + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * 1.42f);
                    }
                    continue;
                }
                Vector2 dir = ab.normalized, perp = new Vector2(-dir.y, dir.x);
                foreach (float s in new[] { 1f, -1f })
                {
                    TryEnd(b.A - dir * EndRoom + perp * s * r);
                    TryEnd(b.B + dir * EndRoom + perp * s * r);
                }
            }
            int n = nodes.Count;
            sees = new bool[n, n];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    sees[i, j] = sees[j, i] = !Blocked(To3(nodes[i], -1000f), To3(nodes[j], -1000f), true);
            cost = new float[n];
            previous = new int[n];
            done = new bool[n];
            fromSees = new bool[n];
        }

        private static void TryEnd(Vector2 p)
        {
            if (!Free(p)) return;
            for (int i = 0; i < nodes.Count; i++) if ((nodes[i] - p).sqrMagnitude < 0.25f) return; // one per corner
            nodes.Add(p);
            nodeGate.Add(-1);
        }

        /// <summary>Whether a unit fits at p (not in any wall, the gates' openings open).</summary>
        private static bool Free(Vector2 p)
        {
            foreach (Barrier b in barriers)
                if (!b.Leaf && Distance(b, p, out _) < UnitRadius + 0.05f) return false;
            return true;
        }
    }
}
