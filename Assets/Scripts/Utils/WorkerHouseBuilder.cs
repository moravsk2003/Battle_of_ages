using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Worker's cottage (front = +Z, ground at y = 0, walls 3 x 3 m): a timber frame of dark oak (sills, corner and door
    /// posts, rails, braces, top plates, gable king posts) with whitewashed infill on a plinth of rough stones; a steep
    /// thatched roof with stepped courses, a ragged eave and a ridge roll; a plank door with strap hinges and a ring,
    /// windows with mullions (open shutters on the sides), a stone chimney, a woodpile, a barrel and a door step.
    /// One mesh (built once, shared by every house) and the shared prop material.
    /// </summary>
    public static class WorkerHouseBuilder
    {
        private const float W = 1.5f, D = 1.5f;          // half width (x) and half depth (z) of the walls
        private const float SillTop = 0.38f, PlateTop = 2.5f, WallFace = 1.5f, BeamFace = 1.53f;
        private const float Ridge = PlateTop + D;       // 45 degree roof: underside ridge height
        private const float Eave = 0.45f, Verge = 0.35f, Thatch = 0.3f;

        private static readonly Color Plaster = new Color(0.87f, 0.79f, 0.63f);
        private static readonly Color Dirt = new Color(0.60f, 0.54f, 0.44f);
        private static readonly Color Oak = new Color(0.30f, 0.20f, 0.12f);
        private static readonly Color Plank = new Color(0.45f, 0.31f, 0.18f);
        private static readonly Color Iron = new Color(0.20f, 0.20f, 0.21f);
        private static readonly Color Stone = new Color(0.55f, 0.52f, 0.47f);
        private static readonly Color Straw = new Color(0.72f, 0.60f, 0.38f);
        private static readonly Color StrawOld = new Color(0.52f, 0.47f, 0.38f);
        private static readonly Color StrawUnder = new Color(0.40f, 0.31f, 0.18f);
        private static readonly Color Void = new Color(0.04f, 0.035f, 0.03f);
        private static readonly Color Bark = new Color(0.28f, 0.21f, 0.15f);
        private static readonly Color EndGrain = new Color(0.80f, 0.66f, 0.45f);

        // Surface = (CharacterSurface pattern, metallic, smoothness)
        private static readonly Vector3 PlasterS = new Vector3(8f, 0f, 0.08f);
        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.22f);
        private static readonly Vector3 StoneS = new Vector3(8f, 0f, 0.2f);
        private static readonly Vector3 StrawS = new Vector3(4f, 0f, 0.12f);
        private static readonly Vector3 IronS = new Vector3(3f, 0.75f, 0.4f);
        private static readonly Vector3 DarkS = new Vector3(1f, 0f, 0.05f);

        private static Mesh mesh;

        /// <summary>The cottage mesh (house space), built on first use.</summary>
        public static Mesh Mesh => mesh != null ? mesh : (mesh = Build());

        /// <summary>Adds the cottage model under the building root.</summary>
        public static GameObject Attach(Transform root)
        {
            var go = new GameObject("HouseModel");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MeshKit.PropMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        private static MeshKit kit;
        private static System.Random rng;

        private static Mesh Build()
        {
            kit = new MeshKit();
            rng = new System.Random(4242);
            Plinth();
            Walls();
            Roof();
            Chimney();
            Door();
            Props();
            Mesh m = kit.ToMesh("WorkerHouse");
            kit = null;
            return m;
        }

        // ------------------------------------------------------------------ helpers

        private static float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        private static void B(Vector3 centre, Vector3 size, Color color, Vector3 surface, float chamfer = 0.012f, Quaternion? rot = null, float jitter = 0f, Func<Vector3, Color> shade = null)
        {
            int seed = rng.Next(1000);
            kit.Box(Matrix4x4.TRS(centre, rot ?? Quaternion.identity, Vector3.one), size, chamfer, surface, shade ?? (p => color), jitter, seed);
        }

        /// <summary>Beam between two points (in a plane facing the given normal), width across, depth along normal.</summary>
        private static void Beam(Vector3 a, Vector3 b, float width, float depth, Vector3 normal, Color color)
        {
            Vector3 along = b - a;
            Quaternion rot = Quaternion.LookRotation(normal, along); // z = normal (depth), y = along
            B((a + b) * 0.5f, new Vector3(width, along.magnitude, depth), color, WoodS, 0.012f, rot, 0f,
              p => color * (0.92f + 0.14f * SdfCreature.Noise(p * 9f + a)));
        }

        /// <summary>Point on a wall: side 0 front (+z), 1 right (+x), 2 back (-z), 3 left (-x); u along the wall as seen from outside, height y, offset out of the wall plane.</summary>
        private static Vector3 WallPoint(int side, float u, float y, float outward)
        {
            switch (side)
            {
                case 0: return new Vector3(u, y, outward);
                case 1: return new Vector3(outward, y, -u);
                case 2: return new Vector3(-u, y, -outward);
                default: return new Vector3(-outward, y, u);
            }
        }

        private static Vector3 WallNormal(int side) => side == 0 ? Vector3.forward : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.left;

        // ------------------------------------------------------------------ parts

        /// <summary>Two courses of rough stones round the base, sunk into the ground (sloping sites).</summary>
        private static void Plinth()
        {
            float outer = 1.62f, depth = 0.28f;
            for (int side = 0; side < 4; side++)
            {
                float half = side % 2 == 0 ? outer : outer - depth; // front and back stones cover the corners
                float[] y0 = { -0.22f, 0.07f }, hgt = { 0.29f, 0.19f };
                for (int course = 0; course < 2; course++)
                {
                    float u = -half + (course == 1 ? Rand(0.1f, 0.22f) : 0f);
                    if (course == 1)
                    {
                        float first = u + half;
                        if (first > 0.05f) PlinthStone(side, -half, -half + first, y0[course], hgt[course], outer, depth);
                    }
                    while (u < half - 0.05f)
                    {
                        float len = Mathf.Min(Rand(0.3f, 0.52f), half - u);
                        if (half - (u + len) < 0.14f) len = half - u;
                        PlinthStone(side, u, u + len, y0[course], hgt[course], outer, depth);
                        u += len;
                    }
                }
            }
        }

        private static void PlinthStone(int side, float u0, float u1, float y0, float h, float outer, float depth)
        {
            Vector3 n = WallNormal(side);
            Vector3 c = WallPoint(side, (u0 + u1) * 0.5f, y0 + h * 0.5f, outer - depth * 0.5f);
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            Color tint = Stone * Rand(0.82f, 1.12f);
            tint.g *= Rand(0.96f, 1.03f);
            B(c, new Vector3(u1 - u0 - 0.012f, h - 0.012f, depth), tint, StoneS, 0.04f, rot, 0.022f,
              p => tint * (0.9f + 0.18f * SdfCreature.Noise(p * 11f + c)));
        }

        private static void Walls()
        {
            for (int side = 0; side < 4; side++)
            {
                Vector3 n = WallNormal(side);
                Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
                float plasterTint = Rand(0.96f, 1.03f);

                // Whitewashed infill, dirtier towards the ground
                Vector3 pc = WallPoint(side, 0f, (SillTop + PlateTop - 0.14f) * 0.5f, WallFace - 0.05f);
                B(pc, new Vector3(2 * W, PlateTop - 0.14f - SillTop, 0.1f), Plaster, PlasterS, 0f, rot, 0f,
                  p => Color.Lerp(Dirt, Plaster * plasterTint, Mathf.SmoothStep(0f, 1f, (p.y + 0.99f) / 0.9f)));

                // Sill, top plate, corner post (the one at the right end of this wall)
                Beam(WallPoint(side, -W - 0.09f, SillTop - 0.065f, BeamFace - 0.09f), WallPoint(side, W + 0.09f, SillTop - 0.065f, BeamFace - 0.09f), 0.13f, 0.18f, Vector3.up, Oak);
                Beam(WallPoint(side, -W - 0.09f, PlateTop - 0.07f, BeamFace - 0.09f), WallPoint(side, W + 0.09f, PlateTop - 0.07f, BeamFace - 0.09f), 0.14f, 0.18f, Vector3.up, Oak);
                Beam(WallPoint(side, W - 0.06f, SillTop, BeamFace - 0.09f), WallPoint(side, W - 0.06f, PlateTop - 0.14f, BeamFace - 0.09f), 0.18f, 0.18f, n, Oak);

                float f = BeamFace - 0.03f; // frame members proud of the plaster
                if (side == 0) FrontFrame(n, f);
                else if (side == 2) BackFrame(n, f);
                else SideFrame(side, n, f);
            }

            // Gables (left and right): plaster triangle, king post and collar
            foreach (int side in new[] { 1, 3 })
            {
                Vector3 n = WallNormal(side);
                var tri = new[] { WallPoint(side, -W, PlateTop, WallFace - 0.02f), WallPoint(side, W, PlateTop, WallFace - 0.02f), WallPoint(side, 0f, Ridge + 0.02f, WallFace - 0.02f) };
                kit.Polygon(tri, Vector3.zero + Vector3.up * 3f, PlasterS, p => Plaster * 0.97f);
                float f = BeamFace - 0.03f;
                Beam(WallPoint(side, 0f, PlateTop, f), WallPoint(side, 0f, Ridge - 0.08f, f), 0.14f, 0.07f, n, Oak);
                Beam(WallPoint(side, -0.78f, PlateTop + 0.72f, f), WallPoint(side, 0.78f, PlateTop + 0.72f, f), 0.12f, 0.07f, n, Oak);
                Beam(WallPoint(side, -1.38f, PlateTop + 0.05f, f), WallPoint(side, -0.1f, PlateTop + 0.68f, f), 0.1f, 0.06f, n, Oak);
                Beam(WallPoint(side, 1.38f, PlateTop + 0.05f, f), WallPoint(side, 0.1f, PlateTop + 0.68f, f), 0.1f, 0.06f, n, Oak);
            }
        }

        private static void FrontFrame(Vector3 n, float f)
        {
            float top = PlateTop - 0.14f;
            // Door posts and lintel
            foreach (float x in new[] { -0.5f, 0.5f }) Beam(WallPoint(0, x, SillTop, f), WallPoint(0, x, top, f), 0.14f, 0.07f, n, Oak);
            Beam(WallPoint(0, -0.57f, 2.2f, f), WallPoint(0, 0.57f, 2.2f, f), 0.12f, 0.07f, n, Oak);
            // Left: window between rails; right: rail and a chevron of braces
            Beam(WallPoint(0, -1.36f, 1.1f, f), WallPoint(0, -0.57f, 1.1f, f), 0.11f, 0.07f, n, Oak);
            Beam(WallPoint(0, -1.36f, 1.74f, f), WallPoint(0, -0.57f, 1.74f, f), 0.11f, 0.07f, n, Oak);
            Window(0, -0.97f, 1.155f, 1.685f, 0.36f, false);
            Beam(WallPoint(0, -1.3f, SillTop + 0.04f, f), WallPoint(0, -0.62f, 1.05f, f), 0.1f, 0.065f, n, Oak);
            Beam(WallPoint(0, 0.57f, 1.38f, f), WallPoint(0, 1.36f, 1.38f, f), 0.11f, 0.07f, n, Oak);
            Beam(WallPoint(0, 0.62f, SillTop + 0.04f, f), WallPoint(0, 1.3f, 1.32f, f), 0.1f, 0.065f, n, Oak);
            Beam(WallPoint(0, 1.3f, 1.44f, f), WallPoint(0, 0.62f, top - 0.04f, f), 0.1f, 0.065f, n, Oak);
        }

        private static void SideFrame(int side, Vector3 n, float f)
        {
            float top = PlateTop - 0.14f;
            Beam(WallPoint(side, -1.36f, 1.05f, f), WallPoint(side, 1.36f, 1.05f, f), 0.11f, 0.07f, n, Oak);
            Beam(WallPoint(side, -1.36f, 1.74f, f), WallPoint(side, 1.36f, 1.74f, f), 0.11f, 0.07f, n, Oak);
            foreach (float u in new[] { -0.36f, 0.36f }) Beam(WallPoint(side, u, SillTop, f), WallPoint(side, u, top, f), 0.12f, 0.07f, n, Oak);
            Window(side, 0f, 1.105f, 1.685f, 0.5f, true);
            Beam(WallPoint(side, -1.3f, SillTop + 0.04f, f), WallPoint(side, -0.42f, 1.0f, f), 0.1f, 0.065f, n, Oak);
            Beam(WallPoint(side, 1.3f, SillTop + 0.04f, f), WallPoint(side, 0.42f, 1.0f, f), 0.1f, 0.065f, n, Oak);
            Beam(WallPoint(side, -1.3f, top - 0.04f, f), WallPoint(side, -0.42f, 1.8f, f), 0.1f, 0.065f, n, Oak);
            Beam(WallPoint(side, 1.3f, top - 0.04f, f), WallPoint(side, 0.42f, 1.8f, f), 0.1f, 0.065f, n, Oak);
        }

        private static void BackFrame(Vector3 n, float f)
        {
            float top = PlateTop - 0.14f;
            Beam(WallPoint(2, -1.36f, 1.35f, f), WallPoint(2, 1.36f, 1.35f, f), 0.11f, 0.07f, n, Oak);
            foreach (float u in new[] { -0.45f, 0.45f }) Beam(WallPoint(2, u, SillTop, f), WallPoint(2, u, top, f), 0.12f, 0.07f, n, Oak);
            foreach (float s in new[] { -1f, 1f })
            {
                Beam(WallPoint(2, s * 1.3f, SillTop + 0.04f, f), WallPoint(2, s * 0.5f, 1.3f, f), 0.1f, 0.065f, n, Oak);
                Beam(WallPoint(2, s * 1.3f, top - 0.04f, f), WallPoint(2, s * 0.5f, 1.41f, f), 0.1f, 0.065f, n, Oak);
            }
        }

        /// <summary>Window opening: dark interior, frame, cross mullion; side windows get shutters swung open.</summary>
        private static void Window(int side, float u, float y0, float y1, float width, bool shutters)
        {
            Vector3 n = WallNormal(side);
            Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
            float hw = width * 0.5f, ym = (y0 + y1) * 0.5f;
            B(WallPoint(side, u, ym, WallFace + 0.004f), new Vector3(width, y1 - y0, 0.01f), Void, DarkS, 0f, rot);
            float f = BeamFace - 0.02f;
            Color frame = Oak * 1.15f;
            Beam(WallPoint(side, u - hw - 0.03f, y0 - 0.03f, f), WallPoint(side, u + hw + 0.03f, y0 - 0.03f, f), 0.07f, 0.08f, n, frame);
            Beam(WallPoint(side, u - hw - 0.03f, y1 + 0.03f, f), WallPoint(side, u + hw + 0.03f, y1 + 0.03f, f), 0.07f, 0.08f, n, frame);
            Beam(WallPoint(side, u - hw - 0.03f, y0 - 0.06f, f), WallPoint(side, u - hw - 0.03f, y1 + 0.06f, f), 0.06f, 0.08f, n, frame);
            Beam(WallPoint(side, u + hw + 0.03f, y0 - 0.06f, f), WallPoint(side, u + hw + 0.03f, y1 + 0.06f, f), 0.06f, 0.08f, n, frame);
            Beam(WallPoint(side, u, y0, f - 0.01f), WallPoint(side, u, y1, f - 0.01f), 0.035f, 0.04f, n, frame);
            Beam(WallPoint(side, u - hw, ym + 0.04f, f - 0.01f), WallPoint(side, u + hw, ym + 0.04f, f - 0.01f), 0.035f, 0.04f, n, frame);
            // Sill board sticking out
            B(WallPoint(side, u, y0 - 0.075f, BeamFace + 0.02f), new Vector3(width + 0.16f, 0.04f, 0.12f), frame, WoodS, 0.008f, rot);
            if (!shutters) return;
            foreach (float s in new[] { -1f, 1f })
            {
                // Hinged at the frame edge and swung open: lies 25 degrees off the wall
                Vector3 hinge = WallPoint(side, u + s * (hw + 0.07f), ym, BeamFace + 0.02f);
                Vector3 wallDir = WallPoint(side, u + s * 1f, 0f, 0f) - WallPoint(side, u, 0f, 0f);
                Vector3 dir = (wallDir.normalized * Mathf.Cos(25f * Mathf.Deg2Rad) + n * Mathf.Sin(25f * Mathf.Deg2Rad)).normalized;
                Quaternion srot = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up); // local x along the shutter
                for (int k = 0; k < 3; k++)
                {
                    float off = hw * (k + 0.5f) / 3f;
                    B(hinge + dir * off, new Vector3(hw / 3f - 0.008f, y1 - y0 + 0.08f, 0.03f), Plank * Rand(0.85f, 1.05f), WoodS, 0.006f, srot);
                }
                foreach (float yy in new[] { y0 + 0.08f, y1 - 0.08f })
                    B(hinge + dir * hw * 0.5f + Vector3.up * (yy - ym) + Vector3.Cross(dir, Vector3.up).normalized * (Vector3.Dot(Vector3.Cross(dir, Vector3.up), n) > 0f ? 0.025f : -0.025f), new Vector3(hw, 0.06f, 0.025f), Plank * 0.8f, WoodS, 0.005f, srot);
            }
        }

        /// <summary>Plank door with battens, a brace, strap hinges and a ring pull; a stone step in front.</summary>
        private static void Door()
        {
            float z = BeamFace - 0.04f, y0 = SillTop + 0.02f, y1 = 2.14f, x0 = -0.43f, x1 = 0.43f;
            int planks = 5;
            float pw = (x1 - x0) / planks;
            for (int i = 0; i < planks; i++)
            {
                Color c = Plank * Rand(0.82f, 1.06f);
                B(new Vector3(x0 + pw * (i + 0.5f), (y0 + y1) * 0.5f, z), new Vector3(pw - 0.008f, y1 - y0, 0.045f), c, WoodS, 0.006f, null, 0f,
                  p => c * (0.9f + 0.15f * SdfCreature.Noise(p * 14f + Vector3.one * i)));
            }
            Color batten = Plank * 0.78f;
            float bz = z + 0.035f;
            B(new Vector3(0f, 0.72f, bz), new Vector3(0.8f, 0.1f, 0.03f), batten, WoodS, 0.006f);
            B(new Vector3(0f, 1.82f, bz), new Vector3(0.8f, 0.1f, 0.03f), batten, WoodS, 0.006f);
            Vector3 a = new Vector3(-0.32f, 0.78f, bz), b = new Vector3(0.32f, 1.76f, bz);
            B((a + b) * 0.5f, new Vector3(0.09f, Vector3.Distance(a, b), 0.026f), batten, WoodS, 0.006f, Quaternion.LookRotation(Vector3.forward, b - a));
            // Strap hinges on the left, nail heads
            foreach (float y in new[] { 0.72f, 1.82f })
            {
                B(new Vector3(-0.15f, y, bz + 0.02f), new Vector3(0.56f, 0.045f, 0.01f), Iron, IronS, 0.003f);
                for (int k = 0; k < 4; k++) kit.Dome(new Vector3(-0.38f + k * 0.15f, y, bz + 0.026f), Vector3.forward, 0.009f, Iron * 1.3f, IronS);
            }
            // Ring pull on a round plate
            kit.Lathe(Matrix4x4.TRS(new Vector3(0.28f, 1.2f, z + 0.025f), Quaternion.Euler(90f, 0f, 0f), Vector3.one),
                      new[] { new Vector2(0f, 0.006f), new Vector2(0.032f, 0.004f), new Vector2(0.035f, 0f) }, 14, IronS, (i, ang) => Iron);
            var ring = new Vector3[17, 6];
            for (int i = 0; i <= 16; i++)
            {
                float t = i * Mathf.PI * 2f / 16f;
                for (int j = 0; j < 6; j++)
                {
                    float s = j * Mathf.PI * 2f / 6f;
                    float r = 0.045f + 0.008f * Mathf.Cos(s);
                    ring[i, j] = new Vector3(0.28f + Mathf.Sin(t) * r, 1.2f - 0.045f - Mathf.Cos(t) * r * 0.9f, z + 0.045f + 0.008f * Mathf.Sin(s) + 0.012f * (1f - Mathf.Cos(t)) * 0.5f);
                }
            }
            kit.Grid(ring, true, IronS, (i, j) => Iron * 1.2f);
            // Step
            B(new Vector3(0.02f, 0.06f, 1.86f), new Vector3(1.05f, 0.2f, 0.46f), Stone * 0.95f, StoneS, 0.04f, Quaternion.Euler(0f, 2f, 0f), 0.02f,
              p => Stone * (0.88f + 0.2f * SdfCreature.Noise(p * 8f)));
        }

        // ------------------------------------------------------------------ roof

        private static void Roof()
        {
            foreach (float s in new[] { 1f, -1f }) ThatchSlope(s);
            RidgeRoll();
        }

        /// <summary>
        /// One roof slope as a closed thick slab (rows along X): underside from the ridge to the eave, a rounded,
        /// ragged eave lip, and the top in stepped courses back to the ridge; flat ends at the verges.
        /// </summary>
        private static void ThatchSlope(float s)
        {
            float c45 = Mathf.Sqrt(0.5f);
            Vector2 down = new Vector2(s * c45, -c45), outN = new Vector2(s * c45, c45); // (z, y)
            Vector2 ridge = new Vector2(0f, Ridge);
            float U = (D + Eave) / c45, L = W + Verge, course = 0.42f;

            List<(Vector2 p, int part)> Profile(float x)
            {
                var pts = new List<(Vector2, int)>(); // part: 0 underside, 1 lip, 2 top
                float rag = (SdfCreature.Noise(new Vector3(x * 9f, 1.3f, s)) - 0.5f) * 0.09f + (SdfCreature.Noise(new Vector3(x * 31f, 4.1f, s)) - 0.5f) * 0.04f;
                float u1 = U + rag;
                for (int k = 0; k <= 5; k++) pts.Add((ridge + down * Mathf.Lerp(-0.05f, u1, k / 5f), 0));
                float r = Thatch * 0.5f;
                Vector2 lipC = ridge + down * u1 + outN * r;
                for (int k = 1; k < 6; k++)
                {
                    float a = -90f + 180f * k / 6f;
                    float rad = a * Mathf.Deg2Rad;
                    pts.Add((lipC + (down * Mathf.Cos(rad) + outN * Mathf.Sin(rad)) * r * (1f + 0.25f * Mathf.Cos(rad)), 1));
                }
                // Top: courses stepping up the slope (each butt end proud of the course above)
                float u = u1;
                float t = Thatch;
                pts.Add((ridge + down * u + outN * t, 2));
                while (u - course > 0.1f)
                {
                    float next = u - course;
                    pts.Add((ridge + down * (next + 0.03f) + outN * (t - 0.035f), 2));
                    pts.Add((ridge + down * next + outN * t, 2));
                    u = next;
                }
                pts.Add((ridge + down * -0.05f + outN * t, 2));
                return pts;
            }

            int rows = 56;
            List<(Vector2 p, int part)> sample = Profile(0f);
            int cols = sample.Count;
            var grid = new Vector3[rows, cols];
            var col = new Color[rows, cols];
            for (int i = 0; i < rows; i++)
            {
                float x = Mathf.Lerp(-L, L, i / (rows - 1f));
                List<(Vector2 p, int part)> prof = Profile(x);
                for (int j = 0; j < cols; j++)
                {
                    Vector2 q = prof[Mathf.Min(j, prof.Count - 1)].p;
                    int part = prof[Mathf.Min(j, prof.Count - 1)].part;
                    grid[i, j] = new Vector3(x, q.y, q.x);
                    float streak = SdfCreature.Noise(new Vector3(x * 14f, q.y * 1.5f, s * 3f));
                    float weather = Mathf.InverseLerp(PlateTop, Ridge + 0.3f, q.y);
                    col[i, j] = part == 0 ? StrawUnder * (0.85f + 0.2f * streak)
                              : part == 1 ? Color.Lerp(Straw * 1.08f, StrawUnder, 0.25f) * (0.85f + 0.25f * streak)
                              : Color.Lerp(Straw, StrawOld, weather * 0.7f) * (0.78f + 0.32f * streak);
                }
            }
            kit.Grid(grid, true, StrawS, (i, j) => col[i, j]);
            Vector3 centre = new Vector3(0f, Ridge - 0.5f, s * 0.9f);
            foreach (int i in new[] { 0, rows - 1 })
            {
                var cap = new Vector3[cols];
                for (int j = 0; j < cols; j++) cap[j] = grid[i, j];
                kit.Polygon(cap, new Vector3(0f, centre.y, centre.z), StrawS, p => StrawUnder * 1.05f);
            }
        }

        private static void RidgeRoll()
        {
            float L = W + Verge + 0.08f, y = Ridge + Thatch / Mathf.Sqrt(0.5f) - 0.07f;
            int rows = 44, ring = 14;
            var p = new Vector3[rows, ring];
            for (int i = 0; i < rows; i++)
            {
                float x = Mathf.Lerp(-L, L, i / (rows - 1f));
                float end = Mathf.Clamp01((L - Mathf.Abs(x)) / 0.14f);
                float sc = Mathf.Sqrt(end) * 0.95f + (end > 0f ? 0.05f : 0f);
                for (int j = 0; j < ring; j++)
                {
                    float a = j * Mathf.PI * 2f / ring;
                    p[i, j] = new Vector3(x, y + Mathf.Sin(a) * 0.15f * sc, Mathf.Cos(a) * 0.24f * sc);
                }
            }
            kit.Grid(p, true, StrawS, (i, j) =>
            {
                float x = p[i, j].x;
                bool ligger = Mathf.Repeat(x * 2.4f + (j < ring / 2 ? 0.25f : 0f), 1f) < 0.12f; // hazel liggers pinning the ridge
                return (ligger ? StrawUnder * 0.8f : StrawOld * 1.05f) * (0.88f + 0.2f * SdfCreature.Noise(p[i, j] * 9f));
            });
        }

        // ------------------------------------------------------------------ chimney and props

        private static void Chimney()
        {
            float x = 0.78f, z = -(D + 0.27f), w = 0.62f, d = 0.48f, top = Ridge + 0.62f;
            float y = -0.15f;
            int course = 0;
            while (y < top)
            {
                float h = Mathf.Min(0.2f, top - y);
                float split = course % 2 == 0 ? 0.42f : 0.58f;
                float xa = x - w * 0.5f, xb = xa + w * split;
                foreach ((float a, float b) in new[] { (xa, xb), (xb, x + w * 0.5f) })
                {
                    Color tint = Stone * Rand(0.72f, 1.02f);
                    Vector3 c = new Vector3((a + b) * 0.5f, y + h * 0.5f, z);
                    B(c, new Vector3(b - a - 0.012f, h - 0.012f, d), tint, StoneS, 0.03f, null, 0.015f, p => tint * (0.88f + 0.2f * SdfCreature.Noise(p * 12f + c)));
                }
                y += h;
                course++;
            }
            // Cap slab and the dark flue
            B(new Vector3(x, top + 0.04f, z), new Vector3(w + 0.1f, 0.08f, d + 0.1f), Stone * 0.8f, StoneS, 0.02f, null, 0.008f);
            B(new Vector3(x, top + 0.081f, z), new Vector3(w * 0.5f, 0.004f, d * 0.45f), Void, DarkS, 0f);
        }

        private static void Props()
        {
            // Woodpile against the left wall, log ends out
            int[] perRow = { 5, 4, 3, 2 };
            float zc = -0.95f;
            for (int row = 0; row < perRow.Length; row++)
                for (int k = 0; k < perRow[row]; k++)
                {
                    float r = Rand(0.065f, 0.085f);
                    float zz = zc + (k - (perRow[row] - 1) * 0.5f) * 0.165f + Rand(-0.01f, 0.01f);
                    float yy = 0.08f + row * 0.142f;
                    Log(new Vector3(-W - 0.34f + Rand(-0.03f, 0.03f), yy, zz), r, 0.52f);
                }
            Barrel(new Vector3(1.05f, 0f, D + 0.42f));
        }

        private static void Log(Vector3 centre, float r, float length)
        {
            var prof = new[]
            {
                new Vector2(0f, -length * 0.5f), new Vector2(r * 0.45f, -length * 0.5f), new Vector2(r * 0.8f, -length * 0.5f), new Vector2(r * 0.92f, -length * 0.5f),
                new Vector2(r, -length * 0.5f + 0.005f), new Vector2(r * 1.03f, 0f), new Vector2(r, length * 0.5f - 0.005f),
                new Vector2(r * 0.92f, length * 0.5f), new Vector2(r * 0.8f, length * 0.5f), new Vector2(r * 0.45f, length * 0.5f), new Vector2(0f, length * 0.5f)
            };
            Color[] cols = { EndGrain * 0.85f, EndGrain, EndGrain * 0.82f, EndGrain * 0.95f, Bark, Bark * 1.1f, Bark, EndGrain * 0.95f, EndGrain * 0.82f, EndGrain, EndGrain * 0.85f };
            float tint = Rand(0.85f, 1.1f);
            kit.Lathe(Matrix4x4.TRS(centre, Quaternion.Euler(0f, 0f, 90f), Vector3.one), prof, 12, WoodS, (i, a) => cols[i] * tint * (i >= 4 && i <= 6 ? 0.85f + 0.3f * Mathf.Abs(Mathf.Sin(a * 3f)) : 1f));
        }

        private static void Barrel(Vector3 at)
        {
            const float H = 0.78f;
            var prof = new List<Vector2> { new Vector2(0f, 0.01f) };
            int n = 14;
            for (int i = 0; i <= n; i++)
            {
                float y = Mathf.Lerp(0.01f, H, i / (float)n);
                prof.Add(new Vector2(0.24f + 0.045f * Mathf.Sin(Mathf.PI * y / H), y));
            }
            prof.Add(new Vector2(0.22f, H));
            prof.Add(new Vector2(0.22f, H - 0.03f));
            prof.Add(new Vector2(0f, H - 0.03f));
            float[] hoops = { 0.1f, 0.22f, H - 0.22f, H - 0.1f };
            kit.Lathe(Matrix4x4.TRS(at, Quaternion.Euler(0f, 18f, 0f), Vector3.one), prof.ToArray(), 24, WoodS, (i, a) =>
            {
                float y = prof[i].y;
                if (i >= prof.Count - 3) return Plank * 0.55f;
                foreach (float hy in hoops) if (Mathf.Abs(y - hy) < 0.03f) return Iron * 1.4f;
                return Plank * (Mathf.Repeat(a * 24f / (Mathf.PI * 2f), 2f) < 1f ? 1f : 0.86f);
            });
        }
    }
}
