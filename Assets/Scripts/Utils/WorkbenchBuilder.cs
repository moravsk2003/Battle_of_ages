using System;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// The smith's workbench (front = +Z, ground at y = 0, ~2.8 x 1.3 m): a heavy oak bench of three thick planks on
    /// chamfered legs with stretchers and a lower shelf (bar stock, a crate), a back board on two posts with a peg rail of
    /// hanging tools (hammer, tongs, saw, files), a leg vise with its screw and tommy bar on the front right; beside it an
    /// anvil (waist, face, horn and heel) on an elm stump and a quench bucket. One mesh built once and shared, and a light
    /// one for afar, on the shared prop material (the old bench was eight plain boxes).
    /// </summary>
    public static class WorkbenchBuilder
    {
        private const float TopY = 0.95f, TopT = 0.09f, HalfW = 1.4f, HalfD = 0.62f;

        private static readonly Color Oak = new Color(0.47f, 0.31f, 0.17f);
        private static readonly Color OakDark = new Color(0.30f, 0.20f, 0.11f);
        private static readonly Color Bark = new Color(0.27f, 0.20f, 0.14f);
        private static readonly Color EndGrain = new Color(0.74f, 0.60f, 0.41f);
        private static readonly Color Iron = new Color(0.22f, 0.22f, 0.23f);
        private static readonly Color IronBright = new Color(0.46f, 0.46f, 0.47f);
        private static readonly Color Rust = new Color(0.42f, 0.25f, 0.14f);
        private static readonly Color Copper = new Color(0.72f, 0.42f, 0.24f);
        private static readonly Color Water = new Color(0.12f, 0.17f, 0.19f);

        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.22f);
        private static readonly Vector3 BarkS = new Vector3(4f, 0f, 0.1f);
        private static readonly Vector3 IronS = new Vector3(3f, 0.8f, 0.42f);
        private static readonly Vector3 WornS = new Vector3(3f, 0.85f, 0.45f);
        /// <summary>A flat blade: dull (a smooth one mirrors the sun over its whole face, a blinding glare with bloom).</summary>
        private static readonly Vector3 BladeS = new Vector3(3f, 0.7f, 0.28f);
        private static readonly Vector3 DarkS = new Vector3(1f, 0f, 0.05f);
        private static readonly Vector3 WetS = new Vector3(1f, 0f, 0.85f);

        private static Mesh mesh, farMesh;
        private static MeshKit kit;
        private static System.Random rng;

        public static Mesh Mesh => mesh != null ? mesh : (mesh = Build());
        public static Mesh FarMesh => farMesh != null ? farMesh : (farMesh = BuildFar());

        /// <summary>Adds the bench model under the building root (full near, light past ~25 m).</summary>
        public static GameObject Attach(Transform root) => MeshKit.AttachWithLod(root, "WorkbenchModel", Mesh, FarMesh, 0.08f);

        private static float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        private static void B(Vector3 c, Vector3 size, Color color, Vector3 surface, float chamfer = 0.012f, Quaternion? rot = null, float jitter = 0f, Func<Vector3, Color> shade = null)
        {
            kit.Box(Matrix4x4.TRS(c, rot ?? Quaternion.identity, Vector3.one), size, chamfer, surface, shade ?? (p => color), jitter, rng.Next(1000));
        }

        /// <summary>Wood with grain streaks along x and a tint per piece.</summary>
        private static Func<Vector3, Color> Grain(Color baseColor, Vector3 seed)
        {
            float tint = Rand(0.86f, 1.08f);
            return p => baseColor * tint * (0.9f + 0.16f * SdfCreature.Noise(new Vector3(p.x * 1.5f, p.y * 22f, p.z * 22f) + seed));
        }

        /// <summary>A round part along the frame's Y axis: profile (radius, height).</summary>
        private static void Round(Vector3 at, Quaternion rot, Vector2[] profile, int segments, Color color, Vector3 surface, Func<int, float, Color> shade = null)
        {
            kit.Lathe(Matrix4x4.TRS(at, rot, Vector3.one), profile, segments, surface, shade ?? ((i, a) => color));
        }

        private static Vector2[] Cylinder(float r, float h) => new[] { new Vector2(0f, 0f), new Vector2(r, 0f), new Vector2(r, h), new Vector2(0f, h) };

        private static Mesh Build()
        {
            kit = new MeshKit();
            rng = new System.Random(777);
            Bench();
            BackBoard();
            Vise();
            Anvil(new Vector3(-HalfW - 0.75f, 0f, 0.15f));
            Bucket(new Vector3(-HalfW - 0.55f, 0f, -0.55f));
            Mesh m = kit.ToMesh("Workbench");
            kit = null;
            return m;
        }

        // ------------------------------------------------------------------ the bench

        private static void Bench()
        {
            // Three thick planks with a hairline between them, the front one worn
            float pd = HalfD * 2f / 3f;
            for (int i = 0; i < 3; i++)
            {
                float z = -HalfD + pd * (i + 0.5f);
                B(new Vector3(Rand(-0.02f, 0.02f), TopY + TopT * 0.5f, z), new Vector3(HalfW * 2f + Rand(0f, 0.06f), TopT, pd - 0.012f), Oak, WoodS, 0.014f, null, 0f, Grain(Oak, new Vector3(i * 3.1f, 0f, 0f)));
            }
            // Four chamfered legs, slightly inset, with the end grain of the top over them
            foreach (float x in new[] { -HalfW + 0.16f, HalfW - 0.16f })
                foreach (float z in new[] { -HalfD + 0.12f, HalfD - 0.12f })
                    B(new Vector3(x, TopY * 0.5f, z), new Vector3(0.13f, TopY, 0.13f), OakDark, WoodS, 0.02f, null, 0f, Grain(OakDark, new Vector3(x, 0f, z)));
            // Apron under the top (front and back) and stretchers low down, a shelf of planks on them
            foreach (float z in new[] { -HalfD + 0.12f, HalfD - 0.12f })
            {
                B(new Vector3(0f, TopY - 0.07f, z), new Vector3(HalfW * 2f - 0.42f, 0.12f, 0.05f), OakDark, WoodS, 0.01f);
                B(new Vector3(0f, 0.22f, z), new Vector3(HalfW * 2f - 0.32f, 0.08f, 0.07f), OakDark, WoodS, 0.01f);
            }
            foreach (float x in new[] { -HalfW + 0.16f, HalfW - 0.16f })
                B(new Vector3(x, 0.22f, 0f), new Vector3(0.07f, 0.08f, HalfD * 2f - 0.3f), OakDark, WoodS, 0.01f);
            for (int i = 0; i < 4; i++)
                B(new Vector3(0f, 0.28f, -HalfD + 0.22f + i * 0.27f), new Vector3(HalfW * 2f - 0.36f, 0.035f, 0.25f), Oak * 0.9f, WoodS, 0.006f, null, 0f, Grain(Oak * 0.9f, new Vector3(0f, i, 0f)));

            // On the shelf: bar stock and a small crate of nails
            for (int i = 0; i < 5; i++)
                B(new Vector3(0.35f + Rand(-0.02f, 0.02f), 0.33f + (i / 3) * 0.04f, -0.1f + (i % 3) * 0.06f), new Vector3(0.9f, 0.035f, 0.045f), Rust, IronS, 0.004f, Quaternion.Euler(0f, Rand(-3f, 3f), 0f));
            B(new Vector3(-0.75f, 0.42f, 0.05f), new Vector3(0.42f, 0.24f, 0.34f), Oak * 0.8f, WoodS, 0.01f, Quaternion.Euler(0f, 8f, 0f));

            // On the top: a hammer and a few ingots, a whetstone
            Hammer(new Vector3(0.25f, TopY + TopT, 0.22f), Quaternion.Euler(0f, 25f, 90f), 0.42f);
            for (int i = 0; i < 3; i++)
                B(new Vector3(-0.55f + i * 0.13f, TopY + TopT + 0.025f, -0.18f), new Vector3(0.11f, 0.05f, 0.26f), i == 2 ? Copper : IronBright, WornS, 0.012f, Quaternion.Euler(0f, Rand(-6f, 6f), 0f));
            B(new Vector3(-0.1f, TopY + TopT + 0.02f, 0.3f), new Vector3(0.22f, 0.04f, 0.06f), new Color(0.48f, 0.46f, 0.42f), new Vector3(8f, 0f, 0.3f), 0.01f, Quaternion.Euler(0f, -12f, 0f));
        }

        // ------------------------------------------------------------------ back board and tools

        private static void BackBoard()
        {
            float z = -HalfD + 0.04f;
            foreach (float x in new[] { -HalfW + 0.1f, HalfW - 0.1f })
                B(new Vector3(x, (TopY + 1.95f) * 0.5f + 0.1f, z), new Vector3(0.1f, 1.95f - TopY + 0.2f, 0.09f), OakDark, WoodS, 0.015f, null, 0f, Grain(OakDark, new Vector3(x, 1f, 0f)));
            // Two boards, and a rail with pegs
            B(new Vector3(0f, 1.36f, z - 0.01f), new Vector3(HalfW * 2f - 0.1f, 0.34f, 0.035f), Oak * 0.92f, WoodS, 0.008f, null, 0f, Grain(Oak * 0.92f, new Vector3(5f, 0f, 0f)));
            B(new Vector3(0f, 1.72f, z - 0.01f), new Vector3(HalfW * 2f - 0.1f, 0.34f, 0.035f), Oak * 0.86f, WoodS, 0.008f, null, 0f, Grain(Oak * 0.86f, new Vector3(6f, 0f, 0f)));
            B(new Vector3(0f, 1.93f, z + 0.01f), new Vector3(HalfW * 2f + 0.12f, 0.07f, 0.07f), OakDark, WoodS, 0.01f);
            for (int i = 0; i < 7; i++)
            {
                float x = -1.05f + i * 0.35f;
                Round(new Vector3(x, 1.62f, z + 0.02f), Quaternion.Euler(90f, 0f, 0f), Cylinder(0.015f, 0.08f), 6, OakDark, WoodS);
            }
            float face = z + 0.06f;
            // Hanging: tongs, a hammer, a saw, two files, a rasp
            Tongs(new Vector3(-1.05f, 1.6f, face));
            Hammer(new Vector3(-0.7f, 1.62f - 0.38f, face + 0.02f), Quaternion.Euler(0f, 0f, 180f), 0.38f); // head down, the handle up to the peg
            Saw(new Vector3(-0.15f, 1.6f, face));
            for (int i = 0; i < 2; i++)
            {
                B(new Vector3(0.55f + i * 0.35f, 1.42f, face), new Vector3(0.025f, 0.3f, 0.008f), Iron, IronS, 0.003f);
                B(new Vector3(0.55f + i * 0.35f, 1.6f, face), new Vector3(0.035f, 0.1f, 0.03f), Oak, WoodS, 0.008f);
            }
            Hammer(new Vector3(1.25f, 1.62f - 0.32f, face + 0.02f), Quaternion.Euler(0f, 0f, 180f), 0.32f);
        }

        /// <summary>A hammer lying or hanging: the handle along the frame's -Y from the head at "at".</summary>
        private static void Hammer(Vector3 at, Quaternion rot, float length)
        {
            Vector3 down = rot * Vector3.down;
            Quaternion handleRot = Quaternion.FromToRotation(Vector3.up, -down);
            Round(at + down * length, handleRot, new[] { new Vector2(0f, 0f), new Vector2(0.016f, 0f), new Vector2(0.014f, length * 0.6f), new Vector2(0.018f, length), new Vector2(0f, length) }, 7, Oak, WoodS);
            B(at, new Vector3(0.05f, 0.06f, 0.15f), Iron, IronS, 0.01f, rot * Quaternion.Euler(90f, 0f, 0f));
        }

        private static void Tongs(Vector3 at)
        {
            foreach (float s in new[] { -1f, 1f })
                B(at + new Vector3(s * 0.025f, -0.2f, 0f), new Vector3(0.016f, 0.42f, 0.012f), Iron, IronS, 0.004f, Quaternion.Euler(0f, 0f, s * 4f));
            B(at + new Vector3(0f, -0.04f, 0.004f), new Vector3(0.05f, 0.03f, 0.02f), Iron, IronS, 0.006f);
            B(at + new Vector3(0f, -0.44f, 0f), new Vector3(0.09f, 0.04f, 0.014f), Iron, IronS, 0.004f);
        }

        private static void Saw(Vector3 at)
        {
            B(at + new Vector3(0.12f, -0.12f, 0f), new Vector3(0.46f, 0.11f, 0.004f), IronBright * 0.85f, BladeS, 0.002f, Quaternion.Euler(0f, 0f, -4f),
              0f, p => p.y < -0.04f ? IronBright * 0.75f : IronBright); // the teeth darker
            B(at + new Vector3(-0.15f, -0.08f, 0f), new Vector3(0.12f, 0.14f, 0.03f), Oak, WoodS, 0.02f);
        }

        // ------------------------------------------------------------------ vise, anvil, bucket

        private static void Vise()
        {
            float x = HalfW - 0.32f, z = HalfD + 0.06f;
            // Back jaw on a post down to the floor (a leg vise), front jaw, the screw and the tommy bar
            B(new Vector3(x, TopY * 0.55f, z - 0.02f), new Vector3(0.12f, TopY * 1.1f, 0.06f), OakDark, WoodS, 0.012f);
            B(new Vector3(x, TopY + 0.12f, z + 0.04f), new Vector3(0.2f, 0.14f, 0.05f), Iron, IronS, 0.01f);
            B(new Vector3(x, TopY + 0.12f, z - 0.03f), new Vector3(0.2f, 0.14f, 0.05f), Iron, IronS, 0.01f);
            Round(new Vector3(x, TopY - 0.06f, z), Quaternion.Euler(90f, 0f, 0f), Cylinder(0.022f, 0.24f), 8, IronBright, WornS);
            Round(new Vector3(x - 0.17f, TopY - 0.06f, z + 0.24f), Quaternion.Euler(0f, 0f, 90f), Cylinder(0.012f, 0.34f), 6, Iron, IronS);
            Round(new Vector3(x - 0.17f, TopY - 0.06f, z + 0.24f), Quaternion.Euler(0f, 0f, 90f), new[] { new Vector2(0f, -0.02f), new Vector2(0.022f, -0.02f), new Vector2(0.022f, 0f), new Vector2(0f, 0f) }, 6, Iron, IronS);
        }

        private static void Anvil(Vector3 at)
        {
            // Elm stump with bark, a lighter cut top with rings
            Round(at, Quaternion.identity, new[] { new Vector2(0f, 0f), new Vector2(0.34f, -0.03f), new Vector2(0.31f, 0.1f), new Vector2(0.29f, 0.52f), new Vector2(0.29f, 0.56f), new Vector2(0f, 0.57f) }, 14, Bark, BarkS,
                  (i, a) => i >= 4 ? EndGrain * (0.85f + 0.15f * Mathf.Sin(a * 3f)) : Bark * (0.85f + 0.2f * SdfCreature.Noise(new Vector3(a * 3f, i, 0f))));
            Vector3 top = at + Vector3.up * 0.57f;
            Quaternion yaw = Quaternion.Euler(0f, 90f, 0f); // the anvil lies along z, the horn to the back
            // Feet, waist, body and the face plate
            B(top + Vector3.up * 0.04f, new Vector3(0.3f, 0.08f, 0.22f), Iron, IronS, 0.02f, yaw);
            B(top + Vector3.up * 0.13f, new Vector3(0.16f, 0.12f, 0.12f), Iron, IronS, 0.02f, yaw);
            B(top + Vector3.up * 0.24f, new Vector3(0.36f, 0.1f, 0.14f), Iron, IronS, 0.02f, yaw);
            B(top + Vector3.up * 0.3f, new Vector3(0.36f, 0.025f, 0.14f), IronBright, WornS, 0.01f, yaw);
            // Horn: a cone off the front, heel off the back
            Round(top + Vector3.up * 0.27f + yaw * new Vector3(0.18f, 0f, 0f), yaw * Quaternion.Euler(0f, 0f, -90f),
                  new[] { new Vector2(0f, 0f), new Vector2(0.06f, 0f), new Vector2(0.035f, 0.14f), new Vector2(0.008f, 0.24f), new Vector2(0f, 0.25f) }, 10, Iron, IronS);
            B(top + Vector3.up * 0.27f - yaw * new Vector3(0.24f, 0f, 0f), new Vector3(0.12f, 0.06f, 0.1f), Iron, IronS, 0.01f, yaw);
        }

        private static void Bucket(Vector3 at)
        {
            Round(at, Quaternion.identity, new[] { new Vector2(0f, 0.02f), new Vector2(0.2f, 0f), new Vector2(0.23f, 0.36f), new Vector2(0.21f, 0.36f), new Vector2(0.19f, 0.3f), new Vector2(0f, 0.3f) }, 12, Oak, WoodS,
                  (i, a) => i == 5 || i == 4 ? Water : Oak * (Mathf.Repeat(a * 12f / (Mathf.PI * 2f), 1f) < 0.08f ? 0.6f : 0.95f));
            foreach (float y in new[] { 0.06f, 0.29f })
                Round(at + Vector3.up * y, Quaternion.identity, new[] { new Vector2(0.205f + y * 0.08f, 0f), new Vector2(0.215f + y * 0.08f, 0f), new Vector2(0.215f + y * 0.08f, 0.03f), new Vector2(0.205f + y * 0.08f, 0.03f) }, 12, Iron, IronS);
        }

        // ------------------------------------------------------------------ far

        private static Mesh BuildFar()
        {
            kit = new MeshKit();
            rng = new System.Random(777);
            B(new Vector3(0f, TopY + TopT * 0.5f, 0f), new Vector3(HalfW * 2f, TopT, HalfD * 2f), Oak, WoodS, 0f);
            foreach (float x in new[] { -HalfW + 0.16f, HalfW - 0.16f })
                foreach (float z in new[] { -HalfD + 0.12f, HalfD - 0.12f })
                    B(new Vector3(x, TopY * 0.5f, z), new Vector3(0.13f, TopY, 0.13f), OakDark, WoodS, 0f);
            B(new Vector3(0f, 0.28f, 0f), new Vector3(HalfW * 2f - 0.36f, 0.04f, HalfD * 2f - 0.2f), Oak * 0.9f, WoodS, 0f);
            B(new Vector3(0f, 1.5f, -HalfD + 0.03f), new Vector3(HalfW * 2f, 0.9f, 0.05f), Oak * 0.88f, WoodS, 0f);
            Vector3 a = new Vector3(-HalfW - 0.75f, 0f, 0.15f);
            Round(a, Quaternion.identity, Cylinder(0.3f, 0.57f), 8, Bark, BarkS);
            B(a + Vector3.up * 0.78f, new Vector3(0.14f, 0.36f, 0.5f), Iron, IronS, 0f);
            Round(new Vector3(-HalfW - 0.55f, 0f, -0.55f), Quaternion.identity, Cylinder(0.21f, 0.36f), 8, Oak, WoodS);
            Mesh m = kit.ToMesh("WorkbenchFar");
            kit = null;
            return m;
        }
    }
}
