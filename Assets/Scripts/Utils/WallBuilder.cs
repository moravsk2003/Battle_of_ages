using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// The settlement's stone wall (outside = +Z, ground at y = 0, the segment along X): a curtain of coursed ashlar on a
    /// battered plinth over a rubble foundation (deep enough for a slope), a flagged walkway on top at WalkY behind a
    /// breastwork with merlons, an octagonal buttress at each end (where the next segment joins: neighbours share it, at
    /// any angle). Kinds: a plain segment, one with a stone flight of stairs inside up to the walkway, and a gate - an
    /// arched opening with voussoirs and two plank leaves on iron straps (separate meshes on hinges, they swing). One mesh
    /// per kind built once and shared, and a light one for afar, on the shared prop material.
    /// </summary>
    public static class WallBuilder
    {
        public enum Kind { Wall, Stairs, Gate }

        /// <summary>Segment length (the pitch the next one snaps at), thickness, walkway height.</summary>
        public const float Length = 4f, GateLength = 6f, Thickness = 1.8f, WalkY = 3.6f;
        public const float ParapetT = 0.42f, BreastH = 0.55f, MerlonH = 0.8f, FootY = -1.3f;
        /// <summary>Gate opening: half width, springing line (the arch's centre height), arch radius = half width.</summary>
        public const float GateHalfW = 1.1f, GateSpring = 1.9f;
        /// <summary>Stairs inside: from the bottom step at StairsLowX up to the landing at StairsTopX, between these z.</summary>
        public const float StairsLowX = 2.2f, StairsTopX = -1.4f, StairsInnerZ = -1.05f, StairsOuterZ = -2.05f;
        public const int StairSteps = 12;
        private const float PierR = 1.12f;

        public static float LengthOf(Kind k) => k == Kind.Gate ? GateLength : Length;

        private static readonly Color Stone = new Color(0.55f, 0.52f, 0.48f);
        private static readonly Color StoneDark = new Color(0.40f, 0.38f, 0.35f);
        private static readonly Color StoneLight = new Color(0.66f, 0.63f, 0.58f);
        private static readonly Color Core = new Color(0.33f, 0.31f, 0.29f);
        private static readonly Color Moss = new Color(0.34f, 0.38f, 0.22f);
        private static readonly Color Oak = new Color(0.40f, 0.27f, 0.15f);
        private static readonly Color OakDark = new Color(0.27f, 0.18f, 0.10f);
        private static readonly Color Iron = new Color(0.20f, 0.20f, 0.21f);

        private static readonly Vector3 StoneS = new Vector3(8f, 0f, 0.14f);
        private static readonly Vector3 WornS = new Vector3(8f, 0f, 0.24f);
        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.2f);
        private static readonly Vector3 IronS = new Vector3(3f, 0.8f, 0.4f);

        private static readonly Dictionary<Kind, Mesh> meshes = new Dictionary<Kind, Mesh>(), farMeshes = new Dictionary<Kind, Mesh>();
        private static Mesh leafMesh;
        private static MeshKit kit;
        private static System.Random rng;

        public static Mesh MeshOf(Kind k) => meshes.TryGetValue(k, out Mesh m) && m != null ? m : (meshes[k] = Build(k));
        public static Mesh FarMeshOf(Kind k) => farMeshes.TryGetValue(k, out Mesh m) && m != null ? m : (farMeshes[k] = BuildFar(k));
        /// <summary>A gate leaf in its hinge's frame (the left one; the right one is it mirrored by scale x = -1).</summary>
        public static Mesh LeafMesh => leafMesh != null ? leafMesh : (leafMesh = BuildLeaf());

        /// <summary>Adds the model under the building root (full near, light past ~30 m).</summary>
        public static GameObject Attach(Transform root, Kind k) => MeshKit.AttachWithLod(root, "WallModel", MeshOf(k), FarMeshOf(k), 0.1f);

        private static float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        private static void B(Vector3 c, Vector3 size, Color color, Vector3 surface, float chamfer = 0.03f, Quaternion? rot = null, float jitter = 0f)
        {
            kit.Box(Matrix4x4.TRS(c, rot ?? Quaternion.identity, Vector3.one), size, chamfer, surface, p => color, jitter, rng.Next(1000));
        }

        /// <summary>A dressed stone: tinted per block, mossy and darker low down.</summary>
        private static Color Block(float y, Color baseColor)
        {
            Color c = baseColor * Rand(0.84f, 1.1f);
            c.r *= Rand(0.97f, 1.03f);
            if (y < 0.7f && rng.NextDouble() < 0.35) c = Color.Lerp(c, Moss, Rand(0.2f, 0.45f));
            return c;
        }

        /// <summary>
        /// Coursed ashlar over the rectangle x0..x1, y0..y1 of the core's face at z (facing sign): rows of blocks 0.16 deep
        /// in front of it with staggered joints (the core's darker colour between them reads as mortar).
        /// </summary>
        private static void Ashlar(float z, float sign, float x0, float x1, float y0, float y1, float rowH = 0.45f)
        {
            float y = y0;
            int row = 0;
            while (y < y1 - 0.05f)
            {
                float h = Mathf.Min(rowH * Rand(0.85f, 1.12f), y1 - y);
                if (y1 - (y + h) < 0.15f) h = y1 - y;
                float x = x0 - (row % 2 == 0 ? 0f : Rand(0.25f, 0.45f));
                while (x < x1 - 0.02f)
                {
                    float w = Rand(0.55f, 1.15f);
                    float a = Mathf.Max(x, x0), b = Mathf.Min(x + w, x1);
                    if (x1 - b < 0.2f) b = x1;
                    if (b - a > 0.05f)
                        B(new Vector3((a + b) * 0.5f, y + h * 0.5f, z + sign * 0.08f), new Vector3(b - a - 0.035f, h - 0.035f, 0.16f), Block(y, Stone), StoneS, 0.035f, null, 0.012f);
                    x = b;
                }
                y += h;
                row++;
            }
        }

        private static Mesh Build(Kind k)
        {
            kit = new MeshKit();
            rng = new System.Random(4100 + (int)k);
            float half = LengthOf(k) * 0.5f;
            if (k == Kind.Gate) GateBody(half);
            else Curtain(-half, half);
            Walkway(-half, half, k == Kind.Gate);
            if (k == Kind.Stairs) Stairs();
            if (k == Kind.Gate) Sconce(new Vector3(-GateHalfW - 0.55f, 2.4f, -FaceZ - 0.04f));
            Mesh m = kit.ToMesh("Wall_" + k);
            kit = null;
            return m;
        }

        // ------------------------------------------------------------------ curtain, walkway, breastwork

        /// <summary>The faces of the ashlar (the core is the blocks' depth behind them).</summary>
        private const float FaceZ = Thickness * 0.5f, CoreZ = FaceZ - 0.16f;

        /// <summary>The solid wall between x0 and x1: foundation, plinth, core and both ashlar faces.</summary>
        private static void Curtain(float x0, float x1)
        {
            float len = x1 - x0, mid = (x0 + x1) * 0.5f;
            // Rubble foundation and the battered plinth (stands proud on both faces)
            B(new Vector3(mid, (FootY - 0.2f) * 0.5f, 0f), new Vector3(len, -0.2f - FootY, Thickness + 0.5f), StoneDark, StoneS, 0.05f, null, 0.05f);
            Plinth(FaceZ + 0.18f, 1f, x0, x1);
            Plinth(-FaceZ - 0.18f, -1f, x0, x1);
            // Core (the mortar between the blocks)
            // (from the foundation up: between its top and the blocks one could see right through the wall)
            B(new Vector3(mid, (WalkY - 0.08f + FootY) * 0.5f, 0f), new Vector3(len, WalkY - 0.08f - FootY, CoreZ * 2f), Core, StoneS, 0f);
            Ashlar(CoreZ, 1f, x0, x1, 0.35f, WalkY + BreastH);
            Ashlar(-CoreZ, -1f, x0, x1, 0.35f, WalkY - 0.15f);
        }

        /// <summary>The battered plinth: big rough stones, the top bevelled back to the face.</summary>
        private static void Plinth(float z, float sign, float x0, float x1)
        {
            float x = x0;
            while (x < x1 - 0.02f)
            {
                float b = Mathf.Min(x + Rand(0.9f, 1.4f), x1);
                if (x1 - b < 0.3f) b = x1;
                B(new Vector3((x + b) * 0.5f, 0.05f, z - sign * 0.1f), new Vector3(b - x - 0.04f, 0.6f, 0.4f), Block(0f, StoneDark), StoneS, 0.08f, null, 0.03f);
                x = b;
            }
            B(new Vector3((x0 + x1) * 0.5f, 0.37f, z - sign * 0.2f), new Vector3(x1 - x0, 0.1f, 0.3f), StoneDark * 1.05f, StoneS, 0.04f, Quaternion.Euler(sign * 30f, 0f, 0f));
        }

        /// <summary>Flagstones on top, the breastwork with merlons on the outside, a coping course on the inner edge.</summary>
        private static void Walkway(float x0, float x1, bool gate)
        {
            float innerZ = -FaceZ, outerZ = FaceZ - ParapetT;
            float x = x0;
            int row = 0;
            while (x < x1 - 0.02f)
            {
                float b = Mathf.Min(x + Rand(0.6f, 1.0f), x1);
                if (x1 - b < 0.25f) b = x1;
                // two flags across, the joint between them staggered
                float split = innerZ + (outerZ - innerZ) * (row % 2 == 0 ? 0.45f : 0.6f);
                B(new Vector3((x + b) * 0.5f, WalkY - 0.04f, (innerZ + split) * 0.5f), new Vector3(b - x - 0.02f, 0.08f, split - innerZ - 0.02f), Block(1f, StoneLight * 0.92f), WornS, 0.015f);
                B(new Vector3((x + b) * 0.5f, WalkY - 0.04f, (split + outerZ) * 0.5f), new Vector3(b - x - 0.02f, 0.08f, outerZ - split - 0.02f), Block(1f, StoneLight * 0.92f), WornS, 0.015f);
                x = b;
                row++;
            }
            // Breastwork: core, the walk-side face, the coping on top
            float bz = FaceZ - ParapetT * 0.5f;
            B(new Vector3((x0 + x1) * 0.5f, WalkY + BreastH * 0.5f, (outerZ + 0.16f + CoreZ) * 0.5f), new Vector3(x1 - x0, BreastH, CoreZ - outerZ - 0.16f), Core, StoneS, 0f);
            Ashlar(outerZ + 0.16f, -1f, x0, x1, WalkY, WalkY + BreastH, 0.28f);
            B(new Vector3((x0 + x1) * 0.5f, WalkY + BreastH + 0.04f, bz), new Vector3(x1 - x0, 0.08f, ParapetT + 0.06f), StoneLight, WornS, 0.02f);
            // Merlons, two blocks each and a cap, clear of the joints (the pier there carries one)
            float inner0 = x0 + PierMerlonW * 0.5f, inner1 = x1 - PierMerlonW * 0.5f, span = inner1 - inner0;
            int n = Mathf.Max(1, Mathf.RoundToInt((span - 0.55f) / (0.7f + 0.55f)));
            float gap = (span - n * 0.7f) / (n + 1);
            for (int i = 0; i < n; i++) Merlon(inner0 + gap + 0.35f + i * (0.7f + gap), 0.7f, WalkY + BreastH + 0.08f);
            // Coping on the inner edge (none above the gate's arch: it carries a timber beam instead)
            B(new Vector3((x0 + x1) * 0.5f, WalkY - 0.16f, innerZ - 0.04f), new Vector3(x1 - x0, 0.14f, 0.16f), StoneDark, StoneS, 0.03f);
            if (gate) B(new Vector3(0f, WalkY - 0.45f, innerZ - 0.1f), new Vector3(GateHalfW * 2f + 0.8f, 0.22f, 0.2f), OakDark, WoodS, 0.02f);
        }

        private static void Merlon(float x, float w, float y0)
        {
            float z = FaceZ - ParapetT * 0.5f;
            float h1 = MerlonH * Rand(0.45f, 0.55f);
            B(new Vector3(x, y0 + h1 * 0.5f, z), new Vector3(w - 0.03f, h1 - 0.03f, ParapetT), Block(2f, Stone), StoneS, 0.035f, null, 0.012f);
            B(new Vector3(x, y0 + (h1 + MerlonH) * 0.5f, z), new Vector3(w - 0.03f, MerlonH - h1 - 0.03f, ParapetT), Block(2f, Stone), StoneS, 0.035f, null, 0.012f);
            B(new Vector3(x, y0 + MerlonH + 0.04f, z), new Vector3(w + 0.06f, 0.08f, ParapetT + 0.06f), StoneLight, WornS, 0.02f);
        }

        // ------------------------------------------------------------------ piers, stairs

        /// <summary>Width of the merlon on a pier (the segments' own merlons keep clear of it).</summary>
        private const float PierMerlonW = 0.8f;
        /// <summary>A pier's top stands this much above the walkway (a flush one would flicker against the flags).</summary>
        public const float PierTopRise = 0.03f;
        /// <summary>Flat-to-flat half width of a pier (the octagon's circumradius is PierR).</summary>
        public static float PierApothem => PierR * Mathf.Cos(Mathf.PI / 8f);

        private static Mesh pierMesh, pierFarMesh;
        public static Mesh PierMesh => pierMesh != null ? pierMesh : (pierMesh = BuildPier(false));
        public static Mesh PierFarMesh => pierFarMesh != null ? pierFarMesh : (pierFarMesh = BuildPier(true));

        /// <summary>The pier model under its root (a joint of the wall: the segments meeting there share it).</summary>
        public static GameObject AttachPier(Transform root) => MeshKit.AttachWithLod(root, "PierModel", PierMesh, PierFarMesh, 0.06f);

        /// <summary>One octagonal band of a pier: flat sides from (r0, y0) to (r1, y1).</summary>
        private static void OctBand(float r0, float y0, float r1, float y1, Color color)
        {
            for (int j = 0; j < 8; j++)
            {
                float a0 = (j + 0.5f) * Mathf.PI / 4f, a1 = (j + 1.5f) * Mathf.PI / 4f; // flats facing the axes
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Color c = color * Rand(0.9f, 1.06f);
                kit.Polygon(new[] { d0 * r0 + Vector3.up * y0, d1 * r0 + Vector3.up * y0, d1 * r1 + Vector3.up * y1, d0 * r1 + Vector3.up * y1 },
                            new Vector3(0f, (y0 + y1) * 0.5f, 0f), StoneS, q => c);
            }
        }

        private static void OctCap(float r, float y, Color color)
        {
            var pts = new Vector3[8];
            for (int j = 0; j < 8; j++) { float a = (j + 0.5f) * Mathf.PI / 4f; pts[j] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }
            kit.Polygon(pts, new Vector3(0f, y - 1f, 0f), WornS, q => color);
        }

        /// <summary>
        /// The octagonal buttress at a joint (flats facing the faces): a spreading foot, coursed stones (a band per course
        /// with crisp flats), a corbelled top a little above the walkway, and on the outer side a stretch of breastwork with
        /// a merlon closing the gap between the segments' own.
        /// </summary>
        private static Mesh BuildPier(bool far)
        {
            kit = new MeshKit();
            rng = new System.Random(far ? 4301 : 4300);
            float r = PierR, top = WalkY + PierTopRise;
            OctBand(r + 0.25f, FootY, r + 0.25f, 0.3f, StoneDark);
            OctBand(r + 0.25f, 0.3f, r, 0.5f, StoneDark);
            if (far) OctBand(r, 0.5f, r, top - 0.2f, Stone);
            else
                for (float y = 0.5f; y < top - 0.25f; y += 0.45f)
                {
                    float y1 = Mathf.Min(y + 0.45f, top - 0.2f);
                    OctBand(r, y, r, y1 - 0.02f, Block(y, Stone));
                    OctBand(r - 0.025f, y1 - 0.02f, r - 0.025f, y1, Core); // the joint
                }
            OctBand(r, top - 0.2f, r + 0.1f, top - 0.12f, StoneLight * 0.9f);
            OctBand(r + 0.1f, top - 0.12f, r + 0.1f, top, StoneLight * 0.9f);
            OctCap(r + 0.1f, top, StoneLight * 0.88f);
            if (far) OctCap(r + 0.25f, FootY, StoneDark);
            // Breastwork and merlon on the outer side (the first segment's outside: the pier turns with it)
            float bz = FaceZ - ParapetT * 0.5f + 0.06f, by = top + BreastH * 0.5f;
            B(new Vector3(0f, by, bz), new Vector3(PierMerlonW + 0.5f, BreastH, ParapetT + 0.12f), Block(2f, Stone), StoneS, far ? 0f : 0.035f);
            B(new Vector3(0f, top + BreastH + 0.04f, bz), new Vector3(PierMerlonW + 0.56f, 0.08f, ParapetT + 0.18f), StoneLight, WornS, far ? 0f : 0.02f);
            if (far) B(new Vector3(0f, top + BreastH + 0.08f + MerlonH * 0.5f, bz), new Vector3(PierMerlonW, MerlonH, ParapetT + 0.12f), Stone, StoneS, 0f);
            else Merlon(0f, PierMerlonW, top + BreastH + 0.08f);
            Mesh m = kit.ToMesh(far ? "WallPierFar" : "WallPier");
            kit = null;
            return m;
        }

        /// <summary>A stone flight against the inner face up to a landing at walkway height, solid under each step.</summary>
        private static void Stairs()
        {
            float run = (StairsLowX - StairsTopX) / StairSteps, rise = WalkY / StairSteps;
            float zc = (StairsInnerZ + StairsOuterZ) * 0.5f, w = StairsInnerZ - StairsOuterZ;
            for (int s = 0; s < StairSteps - 1; s++)
            {
                float top = rise * (s + 1), xHi = StairsLowX - run * s, xLo = xHi - run;
                B(new Vector3((xLo + xHi) * 0.5f, (top - 0.6f) * 0.5f, zc), new Vector3(run + 0.01f, top + 0.6f, w), Block(top, Stone), StoneS, 0.025f, null, 0.01f);
                B(new Vector3((xLo + xHi) * 0.5f, top - 0.04f, zc - 0.02f), new Vector3(run + 0.04f, 0.08f, w + 0.04f), Block(1f, StoneLight * 0.9f), WornS, 0.012f);
            }
            // Landing from the top step to the segment's end, joining the walkway
            float lx = (StairsTopX + run - Length * 0.5f) * 0.5f;
            B(new Vector3(lx, (WalkY - 0.6f) * 0.5f, (StairsOuterZ - FaceZ) * 0.5f), new Vector3(StairsTopX + run + Length * 0.5f, WalkY + 0.6f, -FaceZ - StairsOuterZ + 0.06f), Block(2f, Stone), StoneS, 0f);
            Ashlar(StairsOuterZ + 0.16f, -1f, -Length * 0.5f + 0.9f, StairsTopX + run, 0.35f, WalkY - 0.1f);
            B(new Vector3(lx, WalkY - 0.04f, (StairsOuterZ - FaceZ) * 0.5f), new Vector3(StairsTopX + run + Length * 0.5f, 0.08f, -FaceZ - StairsOuterZ + 0.08f), StoneLight * 0.9f, WornS, 0.015f);
        }

        // ------------------------------------------------------------------ gate

        private static void GateBody(float half)
        {
            float r = GateHalfW, crown = GateSpring + r;
            Curtain(-half, -GateHalfW);
            Curtain(GateHalfW, half);
            // Over the opening: the spandrels on both faces (from the arch out to the square), the soffit, the core
            foreach (float sign in new[] { 1f, -1f })
            {
                float z = sign * (FaceZ - 0.02f);
                const int n = 16;
                var p = new Vector3[n + 1, 2];
                for (int i = 0; i <= n; i++)
                {
                    float a = Mathf.PI * i / n;
                    Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    // where the ray from the arch's centre leaves the square |x| <= r, y <= WalkY (+ breastwork outside)
                    float topY = (sign > 0f ? WalkY + BreastH : WalkY - 0.06f) - GateSpring;
                    float t = Mathf.Min(Mathf.Abs(dir.x) > 1e-4f ? r / Mathf.Abs(dir.x) : 99f, dir.y > 1e-4f ? topY / dir.y : 99f);
                    p[i, 0] = new Vector3(dir.x * r, GateSpring + dir.y * r, z);
                    p[i, 1] = new Vector3(dir.x * t, GateSpring + dir.y * t, z);
                }
                kit.Grid(p, false, StoneS, (i, j) => Stone * 0.9f, new Vector3(0f, 0f, sign));
                // Voussoirs round the arch, proud of the face; the keystone bigger
                const int vn = 11;
                for (int v = 0; v < vn; v++)
                {
                    float a = Mathf.PI * (v + 0.5f) / vn;
                    bool key = v == vn / 2;
                    float depth = key ? 0.55f : 0.42f, arc = Mathf.PI * r / vn - 0.03f;
                    Vector3 c = new Vector3(Mathf.Cos(a) * (r + depth * 0.5f), GateSpring + Mathf.Sin(a) * (r + depth * 0.5f), z + sign * 0.03f);
                    B(c, new Vector3(key ? arc * 1.3f : arc, depth, 0.14f), Block(2f, StoneLight), StoneS, 0.03f, Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg - 90f), 0.01f);
                }
                // Imposts at the springing
                foreach (float sx in new[] { -1f, 1f })
                    B(new Vector3(sx * (r + 0.12f), GateSpring - 0.06f, z + sign * 0.02f), new Vector3(0.5f, 0.14f, 0.2f), StoneLight, StoneS, 0.03f);
                // Ashlar above the arch's crown
            }
            // Soffit (the arch's underside) and the core filling the spandrel between the faces
            const int sn = 16;
            var s = new Vector3[sn + 1, 2];
            for (int i = 0; i <= sn; i++)
            {
                float a = Mathf.PI * i / sn;
                Vector3 q = new Vector3(Mathf.Cos(a) * r, GateSpring + Mathf.Sin(a) * r, 0f);
                s[i, 0] = q + Vector3.forward * FaceZ;
                s[i, 1] = q - Vector3.forward * FaceZ;
            }
            kit.Grid(s, false, StoneS, (i, j) => Stone * 0.75f, Vector3.down, false);
            for (int i = 0; i < 6; i++)
            {
                float y0 = GateSpring + r * i / 6f, y1 = GateSpring + r * (i + 1) / 6f, w = Mathf.Sqrt(Mathf.Max(0f, r * r - (y1 - GateSpring) * (y1 - GateSpring)));
                foreach (float sx in new[] { -1f, 1f })
                    B(new Vector3(sx * (w + r) * 0.5f, (y0 + y1) * 0.5f, 0f), new Vector3(r - w, y1 - y0, CoreZ * 2f), Core, StoneS, 0f);
            }
            B(new Vector3(0f, (crown + WalkY - 0.06f) * 0.5f, 0f), new Vector3(r * 2f, WalkY - 0.06f - crown, CoreZ * 2f), Core, StoneS, 0f);
            // Threshold
            B(new Vector3(0f, -0.02f, 0f), new Vector3(r * 2f, 0.12f, Thickness - 0.1f), StoneDark, WornS, 0.03f, null, 0.02f);
            // Iron pintles the leaves hang on
            foreach (float sx in new[] { -1f, 1f })
                foreach (float y in new[] { 0.45f, 1.85f })
                    B(new Vector3(sx * (r - 0.02f), y, GateLeafZ), new Vector3(0.06f, 0.1f, 0.06f), Iron, IronS, 0.01f);
        }

        /// <summary>The leaves hang in the opening at this z (a little inside of the middle).</summary>
        public const float GateLeafZ = -0.2f;

        /// <summary>A left leaf, its hinge at x = 0, closing toward +x: planks cut to the arch, iron straps and rivets on both faces.</summary>
        private static Mesh BuildLeaf()
        {
            kit = new MeshKit();
            rng = new System.Random(4199);
            float r = GateHalfW;
            const int planks = 5;
            float pw = r / planks;
            for (int i = 0; i < planks; i++)
            {
                float x = pw * (i + 0.5f), xc = -r + x, top = GateSpring + Mathf.Sqrt(Mathf.Max(0f, r * r - xc * xc)) - 0.04f;
                Color c = Oak * Rand(0.85f, 1.08f);
                B(new Vector3(x, (top + 0.02f) * 0.5f, 0f), new Vector3(pw - 0.012f, top - 0.02f, 0.1f), c, WoodS, 0.012f);
            }
            foreach (float y in new[] { 0.45f, 1.2f, 1.85f })
                foreach (float side in new[] { -1f, 1f })
                {
                    float len = y > GateSpring - 0.2f ? r - 0.25f : r - 0.06f;
                    B(new Vector3(len * 0.5f + 0.02f, y, side * 0.065f), new Vector3(len, 0.09f, 0.025f), Iron, IronS, 0.008f);
                    for (int k = 0; k < 4; k++)
                        kit.Dome(new Vector3(0.12f + k * (len - 0.2f) / 3f, y, side * 0.078f), new Vector3(0f, 0f, side), 0.022f, Iron * 1.3f, IronS);
                }
            // The hinge knuckles
            foreach (float y in new[] { 0.45f, 1.85f })
                kit.Lathe(Matrix4x4.TRS(new Vector3(0f, y - 0.08f, 0f), Quaternion.identity, Vector3.one), new[] { new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.05f, 0.16f), new Vector2(0f, 0.16f) }, 8, IronS, (i, a) => Iron);
            Mesh m = kit.ToMesh("WallGateLeaf");
            kit = null;
            return m;
        }

        private static void Sconce(Vector3 at)
        {
            B(at + new Vector3(0f, -0.1f, -0.06f), new Vector3(0.08f, 0.3f, 0.06f), Iron, IronS, 0.01f);
            kit.Lathe(Matrix4x4.TRS(at + new Vector3(0f, 0.02f, -0.16f), Quaternion.identity, Vector3.one),
                      new[] { new Vector2(0f, 0f), new Vector2(0.04f, 0f), new Vector2(0.08f, 0.14f), new Vector2(0.07f, 0.16f), new Vector2(0f, 0.12f) }, 8, IronS, (i, a) => Iron);
            kit.Lathe(Matrix4x4.TRS(at + new Vector3(0f, -0.3f, -0.16f), Quaternion.identity, Vector3.one),
                      new[] { new Vector2(0f, 0f), new Vector2(0.03f, 0f), new Vector2(0.035f, 0.34f), new Vector2(0f, 0.34f) }, 6, WoodS, (i, a) => OakDark);
        }

        // ------------------------------------------------------------------ siege ladder

        /// <summary>Length of a siege ladder (its foot at the origin, the rails up local Y).</summary>
        public const float LadderLength = 4.7f;
        private static Mesh ladderMesh;
        public static Mesh LadderMesh => ladderMesh != null ? ladderMesh : (ladderMesh = BuildLadder());

        /// <summary>A rough siege ladder: two split-pole rails with the bark on, lashed rungs, the tops cut to rest on the wall.</summary>
        private static Mesh BuildLadder()
        {
            kit = new MeshKit();
            rng = new System.Random(4400);
            Color pole = new Color(0.45f, 0.34f, 0.22f), rung = new Color(0.52f, 0.40f, 0.26f), rope = new Color(0.62f, 0.54f, 0.40f);
            foreach (float x in new[] { -0.26f, 0.26f })
                kit.Lathe(Matrix4x4.TRS(new Vector3(x, 0f, 0f), Quaternion.identity, Vector3.one),
                          new[] { new Vector2(0f, 0f), new Vector2(0.05f, 0f), new Vector2(0.045f, LadderLength * 0.5f), new Vector2(0.038f, LadderLength), new Vector2(0f, LadderLength) },
                          7, WoodS, (i, a) => pole * (0.85f + 0.15f * Mathf.Sin(a * 3f + x * 10f)));
            for (float y = 0.35f; y < LadderLength - 0.2f; y += 0.36f)
            {
                kit.Lathe(Matrix4x4.TRS(new Vector3(-0.3f, y, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one),
                          new[] { new Vector2(0f, 0f), new Vector2(0.026f, 0f), new Vector2(0.026f, 0.6f), new Vector2(0f, 0.6f) }, 6, WoodS, (i, a) => rung * Rand(0.85f, 1.1f));
                foreach (float x in new[] { -0.26f, 0.26f })
                    B(new Vector3(x, y, 0f), new Vector3(0.12f, 0.05f, 0.12f), rope, new Vector3(1f, 0f, 0.1f), 0.015f);
            }
            Mesh m = kit.ToMesh("SiegeLadder");
            kit = null;
            return m;
        }

        // ------------------------------------------------------------------ far

        private static Mesh BuildFar(Kind k)
        {
            kit = new MeshKit();
            rng = new System.Random(4100 + (int)k);
            float half = LengthOf(k) * 0.5f;
            void Solid(float x0, float x1)
            {
                B(new Vector3((x0 + x1) * 0.5f, (WalkY - 0.5f) * 0.5f, 0f), new Vector3(x1 - x0, WalkY + 0.5f, Thickness), Stone * 0.92f, StoneS, 0f);
                B(new Vector3((x0 + x1) * 0.5f, 0.05f, 0f), new Vector3(x1 - x0, 0.6f, Thickness + 0.36f), StoneDark, StoneS, 0f);
            }
            if (k == Kind.Gate)
            {
                Solid(-half, -GateHalfW);
                Solid(GateHalfW, half);
                B(new Vector3(0f, (GateSpring + GateHalfW * 0.6f + WalkY) * 0.5f, 0f), new Vector3(GateHalfW * 2f, WalkY - GateSpring - GateHalfW * 0.6f, Thickness), Stone * 0.92f, StoneS, 0f);
                B(new Vector3(0f, (GateSpring + GateHalfW * 0.6f) * 0.5f, GateLeafZ), new Vector3(GateHalfW * 2f, GateSpring + GateHalfW * 0.6f, 0.1f), Oak, WoodS, 0f);
            }
            else Solid(-half, half);
            B(new Vector3(0f, WalkY + BreastH * 0.5f, FaceZ - ParapetT * 0.5f), new Vector3(half * 2f, BreastH, ParapetT), Stone * 0.92f, StoneS, 0f);
            float span = half * 2f - PierMerlonW;
            int n = Mathf.Max(1, Mathf.RoundToInt((span - 0.55f) / (0.7f + 0.55f)));
            float gap = (span - n * 0.7f) / (n + 1);
            for (int i = 0; i < n; i++)
                B(new Vector3(-half + PierMerlonW * 0.5f + gap + 0.35f + i * (0.7f + gap), WalkY + BreastH + MerlonH * 0.5f, FaceZ - ParapetT * 0.5f), new Vector3(0.7f, MerlonH, ParapetT), Stone * 0.92f, StoneS, 0f);
            if (k == Kind.Stairs)
            {
                float zc = (StairsInnerZ + StairsOuterZ) * 0.5f, w = StairsInnerZ - StairsOuterZ;
                for (int s = 0; s < 4; s++)
                {
                    float x1 = StairsLowX - (StairsLowX - StairsTopX) * s / 4f, x0 = StairsLowX - (StairsLowX - StairsTopX) * (s + 1) / 4f;
                    B(new Vector3((x0 + x1) * 0.5f, WalkY * (s + 1) / 8f - 0.25f, zc), new Vector3(x1 - x0, WalkY * (s + 1) / 4f + 0.5f, w), Stone * 0.92f, StoneS, 0f);
                }
                B(new Vector3((StairsTopX - Length * 0.5f) * 0.5f, (WalkY - 0.5f) * 0.5f, zc), new Vector3(StairsTopX + Length * 0.5f, WalkY + 0.5f, w), Stone * 0.92f, StoneS, 0f);
            }
            Mesh m = kit.ToMesh("WallFar_" + k);
            kit = null;
            return m;
        }
    }
}
