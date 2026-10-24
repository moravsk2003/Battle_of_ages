using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// The walls as obstacles for the kinematic units (GroundMotor; the player's CharacterController has the colliders):
    /// each segment a thick line on the ground plane, a pier a disc, the stairs their own strip; the gate's opening is
    /// a leaf that only the player's side passes (§10.5: own units go through the gates only, enemies not at all).
    /// Own units headed across a wall are routed through the gate that makes the shortest way (Route).
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

        /// <summary>
        /// Where an own unit at from should head for to: to itself if no wall is in the way, else through the gate with
        /// the shortest way round (first to the point in front of it, then along its axis through the opening and out the
        /// other side). With no gate, to (the unit walks up to the wall and stays there).
        /// </summary>
        public static Vector3 Route(Vector3 from, Vector3 to) => Route(from, to, out _);

        /// <summary>Route; inGateway: the unit is in the corridor of the gate, heading along its axis (the way is clear).</summary>
        public static Vector3 Route(Vector3 from, Vector3 to, out bool inGateway)
        {
            Refresh();
            inGateway = false;
            if (gates.Count == 0 || !Blocked(from, to, true)) return to;
            Vector2 f = Flat(from), t = Flat(to);
            int best = -1;
            float bestCost = float.MaxValue;
            for (int i = 0; i < gates.Count; i++)
            {
                float cost = (gates[i].center - f).magnitude + (t - gates[i].center).magnitude;
                if (cost < bestCost) { bestCost = cost; best = i; }
            }
            (Vector2 g, Vector2 n, Vector2 along) = gates[best];
            // Sides by the target's (not the unit's: halfway through the unit must not turn back)
            Vector2 rel = f - g;
            float targetSide = Vector2.Dot(t - g, n) >= 0f ? 1f : -1f;
            float onTargetSide = Vector2.Dot(rel, n) * targetSide, lateral = Mathf.Abs(Vector2.Dot(rel, along));
            Vector2 before = g - n * targetSide * GateApproach;
            // In the corridor in front of the opening (or in it, or past it): follow its axis a little ahead (a point
            // that moves on with the unit, so it lines up as it goes); otherwise first to the point before it
            bool corridor = onTargetSide > 0f || (lateral < GateCorridor && onTargetSide >= -GateApproach - 0.5f);
            inGateway = corridor;
            Vector2 aim = corridor ? g + n * targetSide * Mathf.Min(onTargetSide + GateLead, GateApproach + GateLead) : before;
            return new Vector3(aim.x, from.y, aim.y);
        }

        /// <summary>How far off the gate's axis a unit may be and still head along it; how far ahead along it it aims.</summary>
        private const float GateCorridor = 1.5f, GateLead = 1.5f;
    }
}
