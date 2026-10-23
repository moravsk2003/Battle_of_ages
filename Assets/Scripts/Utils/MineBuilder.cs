using System;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// The mine (front = +Z, ground at y = 0, ~4 x 4 m): a craggy rock hill with moss on top and loose boulders round it,
    /// the adit cut into its front - a timber set of round logs (posts, cap, knee braces) and a second set deeper in,
    /// so the dark goes back - a rail track with sleepers out to where the ore is taken (MineNode's output, 2.8 m in
    /// front), a wooden cart of ore on iron wheels, an ore heap with a pick leaning on it and a lantern post. One mesh built
    /// once and shared, a light one for afar, on the shared prop material (the old mine was spheres and boxes).
    /// </summary>
    public static class MineBuilder
    {
        private static readonly Color Rock = new Color(0.44f, 0.42f, 0.40f);
        private static readonly Color RockDark = new Color(0.30f, 0.29f, 0.28f);
        private static readonly Color Moss = new Color(0.36f, 0.42f, 0.22f);
        private static readonly Color Log = new Color(0.40f, 0.29f, 0.18f);
        private static readonly Color Bark = new Color(0.27f, 0.20f, 0.14f);
        private static readonly Color EndGrain = new Color(0.72f, 0.58f, 0.40f);
        private static readonly Color Plank = new Color(0.46f, 0.33f, 0.20f);
        private static readonly Color Iron = new Color(0.22f, 0.22f, 0.23f);
        private static readonly Color Rail = new Color(0.40f, 0.38f, 0.36f);
        private static readonly Color Ore = new Color(0.62f, 0.36f, 0.16f);
        private static readonly Color OreDark = new Color(0.34f, 0.24f, 0.18f);
        private static readonly Color Void = new Color(0.03f, 0.03f, 0.03f);
        private static readonly Color Lamp = new Color(1.0f, 0.78f, 0.35f);

        private static readonly Vector3 RockS = new Vector3(8f, 0f, 0.15f);
        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.2f);
        private static readonly Vector3 BarkS = new Vector3(4f, 0f, 0.1f);
        private static readonly Vector3 IronS = new Vector3(3f, 0.8f, 0.4f);
        private static readonly Vector3 OreS = new Vector3(8f, 0.35f, 0.45f);
        private static readonly Vector3 DarkS = new Vector3(1f, 0f, 0.02f);
        private static readonly Vector3 GlowS = new Vector3(1f, 0f, 0.9f);

        private const float AditZ = 1.05f, AditHalfW = 0.75f, AditH = 1.95f;

        private static Mesh mesh, farMesh;
        private static MeshKit kit;
        private static System.Random rng;

        public static Mesh Mesh => mesh != null ? mesh : (mesh = Build());
        public static Mesh FarMesh => farMesh != null ? farMesh : (farMesh = BuildFar());

        /// <summary>Adds the mine model under the building root (full near, light past ~35 m).</summary>
        public static GameObject Attach(Transform root) => MeshKit.AttachWithLod(root, "MineModel", Mesh, FarMesh, 0.1f);

        private static float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        private static void B(Vector3 c, Vector3 size, Color color, Vector3 surface, float chamfer = 0.012f, Quaternion? rot = null, float jitter = 0f, Func<Vector3, Color> shade = null)
        {
            kit.Box(Matrix4x4.TRS(c, rot ?? Quaternion.identity, Vector3.one), size, chamfer, surface, shade ?? (p => color), jitter, rng.Next(1000));
        }

        private static void Round(Vector3 at, Quaternion rot, Vector2[] profile, int segments, Vector3 surface, Func<int, float, Color> shade, Func<int, float, float> radius = null)
        {
            kit.Lathe(Matrix4x4.TRS(at, rot, Vector3.one), profile, segments, surface, shade, radius);
        }

        /// <summary>A round log from a to b with bark and lighter cut ends.</summary>
        private static void LogBetween(Vector3 a, Vector3 b, float r)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            var prof = new[] { new Vector2(0f, 0f), new Vector2(r * 0.92f, 0f), new Vector2(r, 0.02f), new Vector2(r * Rand(0.92f, 1.02f), len * 0.5f), new Vector2(r * 0.96f, len - 0.02f), new Vector2(r * 0.88f, len), new Vector2(0f, len) };
            float seed = Rand(0f, 10f);
            Round(a, Quaternion.FromToRotation(Vector3.up, d / len), prof, 9, BarkS,
                  (i, ang) => i == 0 || i == 6 ? EndGrain : Bark * (0.82f + 0.25f * SdfCreature.Noise(new Vector3(ang * 2f, i * 0.7f, seed))));
        }

        /// <summary>A rough stone: a jittered chamfered box, darker at the bottom.</summary>
        private static void Stone(Vector3 c, Vector3 size, float yaw, Color tint)
        {
            B(c, size, tint, RockS, Mathf.Min(size.x, size.y, size.z) * 0.22f, Quaternion.Euler(Rand(-12f, 12f), yaw, Rand(-12f, 12f)), Mathf.Min(size.x, size.y, size.z) * 0.32f,
              p => Color.Lerp(tint * 0.7f, tint, Mathf.Clamp01(p.y / size.y + 0.6f)) * (0.88f + 0.2f * SdfCreature.Noise(p * 3f + c)));
        }

        private static Mesh Build()
        {
            kit = new MeshKit();
            rng = new System.Random(5151);
            Hill(22);
            Adit();
            Track();
            Cart(new Vector3(-0.15f, 0f, 2.05f));
            Heap(new Vector3(1.55f, 0f, 1.95f));
            LanternPost(new Vector3(-1.3f, 0f, 1.6f));
            Mesh m = kit.ToMesh("Mine");
            kit = null;
            return m;
        }

        // ------------------------------------------------------------------ the hill

        private static void Hill(int segments)
        {
            // A craggy dome, flattened front to back, its face cut where the adit goes in; moss where it is level
            var prof = new[] { new Vector2(2.1f, -0.25f), new Vector2(2.05f, 0.35f), new Vector2(1.85f, 1.1f), new Vector2(1.45f, 1.8f), new Vector2(0.9f, 2.4f), new Vector2(0.35f, 2.75f), new Vector2(0f, 2.85f) };
            Matrix4x4 frame = Matrix4x4.TRS(new Vector3(0f, 0f, -0.55f), Quaternion.identity, new Vector3(1f, 1f, 0.82f));
            kit.Lathe(frame, prof, segments, RockS,
                (i, a) =>
                {
                    float n = SdfCreature.Noise(new Vector3(Mathf.Cos(a) * 3f, i * 0.9f, Mathf.Sin(a) * 3f));
                    Color c = Color.Lerp(RockDark, Rock, 0.4f + 0.6f * n);
                    return i >= 4 ? Color.Lerp(c, Moss, 0.35f + 0.4f * n) : c;
                },
                (i, a) =>
                {
                    float crag = 0.74f + 0.36f * SdfCreature.Noise(new Vector3(Mathf.Cos(a) * 1.6f, i * 0.8f, Mathf.Sin(a) * 1.6f))
                                       + 0.16f * SdfCreature.Noise(new Vector3(Mathf.Cos(a) * 5f, i * 2.1f, Mathf.Sin(a) * 5f));
                    float front = Mathf.Sin(a); // +z side: pulled back flat for the adit's face
                    return front > 0.6f && i <= 4 ? crag * Mathf.Lerp(1f, 0.78f, (front - 0.6f) / 0.4f) : crag;
                });
            // Boulders fallen round its foot, and a few on the slopes
            for (int k = 0; k < 9; k++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                if (Mathf.Sin(a) > 0.55f) a += Mathf.PI * 0.45f; // keep the front clear for the track
                float r = Rand(1.85f, 2.35f);
                Vector3 c = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.85f - 0.5f);
                Vector3 s = new Vector3(Rand(0.4f, 0.9f), Rand(0.3f, 0.6f), Rand(0.4f, 0.8f));
                c.y = s.y * 0.35f;
                Stone(c, s, Rand(0f, 360f), Rock * Rand(0.85f, 1.1f));
            }
            // Outcrops sitting on the slopes, half out of the hill, so it reads as rock and not as a dome
            for (int k = 0; k < 14; k++)
            {
                float a = Rand(0f, Mathf.PI * 2f);
                if (Mathf.Sin(a) > 0.5f) a = Mathf.PI * 1.5f + Rand(-1.2f, 1.2f); // the front is the adit's face
                float t = Rand(0.1f, 0.85f), y = Mathf.Lerp(0.2f, 2.5f, t);
                float r = Mathf.Lerp(2.05f, 0.75f, t * t) * 0.92f;
                Vector3 c = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r * 0.82f - 0.55f);
                float s = Rand(0.55f, 1.0f) * (1f - t * 0.4f);
                Stone(c, new Vector3(s, s * Rand(0.5f, 0.8f), s * Rand(0.7f, 1f)), a * Mathf.Rad2Deg + Rand(-30f, 30f), (k % 3 == 0 ? RockDark : Rock) * Rand(0.88f, 1.1f));
            }
        }

        // ------------------------------------------------------------------ the adit

        private static void Adit()
        {
            // The dark: a deep box going back into the hill, a lighter rock lip round the mouth
            B(new Vector3(0f, AditH * 0.5f, AditZ - 0.9f), new Vector3(AditHalfW * 2f - 0.05f, AditH, 1.9f), Void, DarkS, 0f);
            for (int i = 0; i < 3; i++) // the brow over the mouth: three rough slabs
                Stone(new Vector3(-0.75f + i * 0.75f + Rand(-0.08f, 0.08f), AditH + 0.3f + Rand(-0.05f, 0.1f), AditZ - 0.3f), new Vector3(Rand(0.75f, 0.95f), Rand(0.45f, 0.6f), Rand(0.7f, 0.9f)), Rand(-20f, 20f), Rock * Rand(0.88f, 1.05f));
            foreach (float s in new[] { -1f, 1f })
                Stone(new Vector3(s * (AditHalfW + 0.55f), 0.7f, AditZ - 0.2f), new Vector3(0.7f, 1.5f, 0.75f), s * 8f, Rock);

            // The timber set at the mouth: two posts leaning in a little, the cap log, knee braces; a second set deeper in
            foreach ((float z, float scale) in new[] { (AditZ + 0.05f, 1f), (AditZ - 0.75f, 0.92f) })
            {
                float hw = AditHalfW * scale, h = AditH * scale;
                Color tint = scale < 1f ? new Color(0.55f, 0.55f, 0.55f) : Color.white; // the deep one in the shade
                Vector3 l0 = new Vector3(-hw - 0.02f, 0f, z), l1 = new Vector3(-hw + 0.08f, h, z);
                Vector3 r0 = new Vector3(hw + 0.02f, 0f, z), r1 = new Vector3(hw - 0.08f, h, z);
                LogBetween(l0, l1, 0.11f);
                LogBetween(r0, r1, 0.11f);
                LogBetween(new Vector3(-hw - 0.3f, h + 0.08f, z), new Vector3(hw + 0.3f, h + 0.06f, z), 0.12f);
                if (scale >= 1f)
                {
                    LogBetween(new Vector3(-hw + 0.06f, h - 0.4f, z + 0.04f), new Vector3(-hw + 0.42f, h, z + 0.04f), 0.05f);
                    LogBetween(new Vector3(hw - 0.06f, h - 0.4f, z + 0.04f), new Vector3(hw - 0.42f, h, z + 0.04f), 0.05f);
                    // Lagging boards over the cap
                    for (int i = 0; i < 5; i++)
                        B(new Vector3(-0.6f + i * 0.3f, h + 0.22f, z - 0.35f), new Vector3(0.26f, 0.04f, 0.9f), Plank * Rand(0.8f, 1.0f), WoodS, 0.006f, Quaternion.Euler(Rand(-4f, 4f), 0f, Rand(-3f, 3f)));
                }
            }
        }

        // ------------------------------------------------------------------ track, cart, heap, lantern

        private static void Track()
        {
            float z0 = AditZ - 1.0f, z1 = 3.3f;
            for (float z = z0 + 0.15f; z < z1; z += 0.42f)
                B(new Vector3(Rand(-0.03f, 0.03f), 0.035f, z), new Vector3(0.95f, 0.07f, 0.16f), Plank * Rand(0.7f, 0.95f), WoodS, 0.01f, Quaternion.Euler(0f, Rand(-4f, 4f), 0f));
            foreach (float x in new[] { -0.3f, 0.3f })
                B(new Vector3(x, 0.1f, (z0 + z1) * 0.5f), new Vector3(0.05f, 0.06f, z1 - z0), Rail, IronS, 0.008f);
        }

        private static void Cart(Vector3 at)
        {
            // A tapered tub of planks with iron bands and corners
            float w = 0.66f, l = 0.95f, h = 0.5f, y0 = 0.32f;
            B(at + new Vector3(0f, y0 + 0.03f, 0f), new Vector3(w * 0.85f, 0.06f, l * 0.85f), Plank, WoodS, 0.01f);
            foreach (float s in new[] { -1f, 1f })
            {
                B(at + new Vector3(s * w * 0.46f, y0 + h * 0.5f, 0f), new Vector3(0.05f, h, l), Plank, WoodS, 0.008f, Quaternion.Euler(0f, 0f, -s * 8f), 0f, p => Plank * (0.85f + 0.15f * Mathf.Repeat(p.y * 6f, 1f)));
                B(at + new Vector3(0f, y0 + h * 0.5f, s * l * 0.48f), new Vector3(w, h, 0.05f), Plank * 0.9f, WoodS, 0.008f, Quaternion.Euler(s * 8f, 0f, 0f));
            }
            foreach (float y in new[] { y0 + 0.08f, y0 + h - 0.06f })
                foreach (float s in new[] { -1f, 1f })
                    B(at + new Vector3(s * (w * 0.47f + (y - y0) * 0.14f), y, 0f), new Vector3(0.015f, 0.05f, l + 0.02f), Iron, IronS, 0.004f, Quaternion.Euler(0f, 0f, -s * 8f));
            // Wheels on axles, sitting on the rails
            foreach (float z in new[] { -0.28f, 0.28f })
            {
                B(at + new Vector3(0f, 0.2f, z), new Vector3(0.68f, 0.04f, 0.04f), Iron, IronS, 0.006f);
                foreach (float x in new[] { -0.3f, 0.3f })
                    Round(at + new Vector3(x + Mathf.Sign(x) * 0.02f, 0.2f, z), Quaternion.Euler(0f, 0f, 90f),
                          new[] { new Vector2(0f, -0.025f), new Vector2(0.15f, -0.025f), new Vector2(0.15f, 0.025f), new Vector2(0f, 0.025f) }, 12, IronS, (i, a) => Iron);
            }
            // Heaped with ore
            for (int k = 0; k < 12; k++)
            {
                Vector3 c = at + new Vector3(Rand(-0.22f, 0.22f), y0 + h - 0.02f + Rand(0f, 0.12f), Rand(-0.33f, 0.33f));
                float s = Rand(0.1f, 0.18f);
                Stone(c, new Vector3(s, s * 0.8f, s), Rand(0f, 360f), (k % 3 == 0 ? OreDark : Ore) * Rand(0.85f, 1.15f));
            }
        }

        private static void Heap(Vector3 at)
        {
            for (int k = 0; k < 16; k++)
            {
                float a = Rand(0f, Mathf.PI * 2f), r = Rand(0f, 0.45f);
                float s = Rand(0.14f, 0.26f) * (1f - r * 0.6f);
                Vector3 c = at + new Vector3(Mathf.Cos(a) * r, s * 0.3f + (0.45f - r) * 0.5f, Mathf.Sin(a) * r);
                Stone(c, new Vector3(s, s * 0.75f, s), Rand(0f, 360f), (k % 4 == 0 ? OreDark : Ore) * Rand(0.85f, 1.15f));
            }
            // A pick leaning on it: handle and a curved iron head
            Vector3 top = at + new Vector3(-0.2f, 0.85f, 0.3f), foot = at + new Vector3(-0.55f, 0f, 0.55f);
            Vector3 d = (top - foot).normalized;
            Round(foot, Quaternion.FromToRotation(Vector3.up, d), new[] { new Vector2(0f, 0f), new Vector2(0.018f, 0f), new Vector2(0.02f, 0.95f), new Vector2(0f, 0.95f) }, 7, WoodS, (i, a) => Plank);
            Quaternion headRot = Quaternion.LookRotation(Vector3.Cross(d, Vector3.right), d);
            B(foot + d * 0.93f, new Vector3(0.05f, 0.05f, 0.5f), Iron, IronS, 0.01f, headRot);
            B(foot + d * 0.93f + headRot * new Vector3(0f, -0.03f, 0.27f), new Vector3(0.035f, 0.035f, 0.1f), Iron, IronS, 0.006f, headRot * Quaternion.Euler(30f, 0f, 0f));
            B(foot + d * 0.93f + headRot * new Vector3(0f, -0.03f, -0.27f), new Vector3(0.035f, 0.035f, 0.1f), Iron, IronS, 0.006f, headRot * Quaternion.Euler(-30f, 0f, 0f));
        }

        private static void LanternPost(Vector3 at)
        {
            LogBetween(at, at + Vector3.up * 1.7f, 0.06f);
            B(at + new Vector3(0f, 1.62f, 0.16f), new Vector3(0.04f, 0.04f, 0.3f), Iron, IronS, 0.006f);
            Vector3 lamp = at + new Vector3(0f, 1.42f, 0.3f);
            B(lamp + Vector3.up * 0.13f, new Vector3(0.16f, 0.03f, 0.16f), Iron, IronS, 0.006f);
            B(lamp, new Vector3(0.12f, 0.2f, 0.12f), Lamp, GlowS, 0.01f);
            foreach (float x in new[] { -0.065f, 0.065f })
                foreach (float z in new[] { -0.065f, 0.065f })
                    B(lamp + new Vector3(x, 0f, z), new Vector3(0.015f, 0.22f, 0.015f), Iron, IronS, 0.003f);
            B(lamp - Vector3.up * 0.12f, new Vector3(0.15f, 0.03f, 0.15f), Iron, IronS, 0.006f);
        }

        // ------------------------------------------------------------------ far

        private static Mesh BuildFar()
        {
            kit = new MeshKit();
            rng = new System.Random(5151);
            var prof = new[] { new Vector2(2.1f, -0.25f), new Vector2(1.9f, 1.0f), new Vector2(1.2f, 2.1f), new Vector2(0.4f, 2.75f), new Vector2(0f, 2.85f) };
            kit.Lathe(Matrix4x4.TRS(new Vector3(0f, 0f, -0.55f), Quaternion.identity, new Vector3(1f, 1f, 0.82f)), prof, 8, RockS,
                      (i, a) => i >= 3 ? Color.Lerp(Rock, Moss, 0.5f) : Rock * 0.85f,
                      (i, a) => Mathf.Sin(a) > 0.6f && i <= 2 ? 0.8f : 1f);
            B(new Vector3(0f, AditH * 0.5f, AditZ - 0.4f), new Vector3(AditHalfW * 2f, AditH, 0.9f), Void, DarkS, 0f);
            foreach (float x in new[] { -AditHalfW, AditHalfW })
                B(new Vector3(x, AditH * 0.5f, AditZ + 0.05f), new Vector3(0.2f, AditH, 0.2f), Bark, BarkS, 0f);
            B(new Vector3(0f, AditH + 0.08f, AditZ + 0.05f), new Vector3(AditHalfW * 2f + 0.6f, 0.22f, 0.24f), Bark, BarkS, 0f);
            B(new Vector3(-0.15f, 0.55f, 2.05f), new Vector3(0.66f, 0.5f, 0.95f), Plank, WoodS, 0f);
            B(new Vector3(1.55f, 0.2f, 1.95f), new Vector3(0.8f, 0.4f, 0.8f), Ore, OreS, 0f);
            Mesh m = kit.ToMesh("MineFar");
            kit = null;
            return m;
        }
    }
}
