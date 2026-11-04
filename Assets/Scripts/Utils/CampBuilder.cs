using System;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// The raider camp's pieces as MeshKit meshes on the shared prop material, each built once and shared (the old camp
    /// was flat red plates and bare cylinders): an A-frame canvas tent (front = +Z) with sagging panels, a closed
    /// back, the door flaps tied open, poles, guy ropes to pegs and a bedroll inside; a cheval-de-frise barricade (a log
    /// with sharpened stakes through it, crossed); a campfire (a ring of stones, charred logs, glowing embers); a war
    /// standard (pole, crossbar, a ragged swallow-tailed banner, an iron spike); and camp clutter (crates, a barrel, a
    /// spear rack).
    /// </summary>
    public static class CampBuilder
    {
        private static readonly Color Cloth = new Color(0.56f, 0.20f, 0.15f);
        private static readonly Color Mud = new Color(0.33f, 0.26f, 0.19f);
        private static readonly Color Wood = new Color(0.42f, 0.30f, 0.18f);
        private static readonly Color WoodDark = new Color(0.28f, 0.20f, 0.13f);
        private static readonly Color Bark = new Color(0.27f, 0.20f, 0.14f);
        private static readonly Color Point = new Color(0.78f, 0.66f, 0.46f);
        private static readonly Color Rope = new Color(0.62f, 0.54f, 0.40f);
        private static readonly Color Stone = new Color(0.45f, 0.43f, 0.41f);
        private static readonly Color Char = new Color(0.07f, 0.06f, 0.05f);
        private static readonly Color Ember = new Color(1.0f, 0.42f, 0.10f);
        private static readonly Color Iron = new Color(0.22f, 0.22f, 0.23f);
        private static readonly Color Fur = new Color(0.36f, 0.30f, 0.24f);

        private static readonly Vector3 ClothS = new Vector3(1f, 0f, 0.15f);
        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.2f);
        private static readonly Vector3 BarkS = new Vector3(4f, 0f, 0.1f);
        private static readonly Vector3 StoneS = new Vector3(8f, 0f, 0.18f);
        private static readonly Vector3 IronS = new Vector3(3f, 0.8f, 0.4f);
        private static readonly Vector3 GlowS = new Vector3(1f, 0f, 0.9f);
        private static readonly Vector3 DarkS = new Vector3(1f, 0f, 0.05f);

        private const float TentW = 1.55f, TentL = 1.6f, TentH = 2.05f;

        private static Mesh tent, tentFar, barricade, standard, clutter;
        private static MeshKit kit;
        private static System.Random rng;

        public static Mesh Tent => tent != null ? tent : (tent = Make("RaiderTent", 11, BuildTent));
        public static Mesh TentFar => tentFar != null ? tentFar : (tentFar = Make("RaiderTentFar", 11, BuildTentFar));
        public static Mesh Barricade => barricade != null ? barricade : (barricade = Make("SpikeBarricade", 12, BuildBarricade));
        public static Mesh Standard => standard != null ? standard : (standard = Make("WarStandard", 14, BuildStandard));
        public static Mesh Clutter => clutter != null ? clutter : (clutter = Make("CampClutter", 15, BuildClutter));

        private static Mesh Make(string name, int seed, Action build)
        {
            kit = new MeshKit();
            rng = new System.Random(seed);
            build();
            Mesh m = kit.ToMesh(name);
            kit = null;
            return m;
        }

        private static float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();

        private static void B(Vector3 c, Vector3 size, Color color, Vector3 surface, float chamfer = 0.01f, Quaternion? rot = null, float jitter = 0f, Func<Vector3, Color> shade = null)
        {
            kit.Box(Matrix4x4.TRS(c, rot ?? Quaternion.identity, Vector3.one), size, chamfer, surface, shade ?? (p => color), jitter, rng.Next(1000));
        }

        /// <summary>A round pole or log from a to b; sharpened = a pale point at b.</summary>
        private static void Pole(Vector3 a, Vector3 b, float r, Color color, bool sharpened = false, bool bark = false)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            Vector2[] prof = sharpened
                ? new[] { new Vector2(0f, 0f), new Vector2(r, 0f), new Vector2(r, len * 0.8f), new Vector2(r * 0.15f, len), new Vector2(0f, len) }
                : new[] { new Vector2(0f, 0f), new Vector2(r, 0f), new Vector2(r * Rand(0.9f, 1f), len), new Vector2(0f, len) };
            kit.Lathe(Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, d / len), Vector3.one), prof, bark ? 8 : 6, bark ? BarkS : WoodS,
                      (i, ang) => sharpened && i >= 2 ? Point : color * (0.86f + 0.2f * SdfCreature.Noise(new Vector3(ang, i, len))));
        }

        /// <summary>A rope as a thin box from a to b.</summary>
        private static void Line(Vector3 a, Vector3 b, float w = 0.014f)
        {
            Vector3 d = b - a;
            B((a + b) * 0.5f, new Vector3(w, w, d.magnitude), Rope, ClothS, 0f, Quaternion.LookRotation(d));
        }

        // ------------------------------------------------------------------ tent

        private static void BuildTent()
        {
            const int rows = 6, cols = 9;
            float eaveY = 0.1f;
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 n = new Vector3(s * TentH, TentW, 0f).normalized; // out of this panel
                var outer = new Vector3[rows, cols];
                var inner = new Vector3[rows, cols];
                for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    float u = i / (float)(rows - 1), v = j / (float)(cols - 1);
                    Vector3 p = new Vector3(s * TentW * u, Mathf.Lerp(TentH, eaveY, u), Mathf.Lerp(-TentL, TentL, v));
                    float sag = Mathf.Sin(u * Mathf.PI) * (0.55f + 0.45f * Mathf.Sin(v * Mathf.PI)) * 0.15f; // the canvas hangs between the ridge and the pegs
                    if (i == rows - 1) p += new Vector3(s * 0.05f, 0f, 0f) * Mathf.Sin(v * Mathf.PI * 3f); // a scalloped hem
                    outer[i, j] = p - n * sag;
                    inner[i, j] = outer[i, j] - n * 0.03f;
                }
                Func<int, int, Color> shade = (i, j) =>
                {
                    float u = i / (float)(rows - 1);
                    Color c = Cloth * (0.86f + 0.16f * SdfCreature.Noise(new Vector3(j * 0.6f, i * 0.8f, s)));
                    return Color.Lerp(c, Mud, Mathf.SmoothStep(0.6f, 1f, u) * 0.6f); // dirtier towards the ground
                };
                kit.Grid(outer, false, ClothS, shade, n);
                kit.Grid(inner, false, ClothS, (i, j) => Cloth * 0.55f, n, inward: true);
            }
            // The back, closed (seen from both sides); the front flaps tied back to the sides
            Vector3 bl = new Vector3(-TentW, eaveY, -TentL), br = new Vector3(TentW, eaveY, -TentL), bt = new Vector3(0f, TentH, -TentL);
            kit.Polygon(new[] { bl, br, bt }, new Vector3(0f, 1f, 0f), ClothS, p => Cloth * 0.8f);
            kit.Polygon(new[] { bl, br, bt }, new Vector3(0f, 1f, -3f), ClothS, p => Cloth * 0.95f);
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 top = new Vector3(0f, TentH - 0.05f, TentL), side = new Vector3(s * TentW, eaveY, TentL), tied = new Vector3(s * (TentW * 0.85f), 0.9f, TentL + 0.32f);
                kit.Polygon(new[] { top, side, tied }, new Vector3(0f, 1f, TentL - 1f), ClothS, p => Cloth * 0.9f);
                kit.Polygon(new[] { top, side, tied }, new Vector3(s * 3f, 1f, TentL + 2f), ClothS, p => Cloth * 0.7f);
                Line(tied, tied + new Vector3(-s * 0.05f, -0.25f, -0.08f), 0.02f);
            }
            // Poles: the ridge and an upright at each end; guy ropes from the ends to pegs
            Pole(new Vector3(0f, TentH + 0.02f, -TentL - 0.12f), new Vector3(0f, TentH + 0.02f, TentL + 0.12f), 0.045f, WoodDark);
            foreach (float z in new[] { -TentL - 0.05f, TentL + 0.05f })
            {
                Pole(new Vector3(0f, 0f, z), new Vector3(0f, TentH + 0.18f, z), 0.045f, WoodDark);
                float zs = Mathf.Sign(z);
                Vector3 peg = new Vector3(0f, 0f, z + zs * 1.1f);
                Line(new Vector3(0f, TentH + 0.12f, z), peg + Vector3.up * 0.12f);
                Pole(peg + new Vector3(0f, -0.05f, -zs * 0.05f), peg + new Vector3(0f, 0.2f, zs * 0.05f), 0.025f, Wood);
            }
            // Pegs along the hems
            foreach (float s in new[] { -1f, 1f })
                for (int k = 0; k < 4; k++)
                {
                    Vector3 peg = new Vector3(s * (TentW + 0.22f), 0f, Mathf.Lerp(-TentL + 0.3f, TentL - 0.3f, k / 3f));
                    Line(new Vector3(s * TentW, 0.12f, peg.z), peg + Vector3.up * 0.1f, 0.01f);
                    Pole(peg + Vector3.down * 0.05f, peg + Vector3.up * 0.16f, 0.02f, Wood);
                }
            // Inside: a bedroll and a fur on the ground
            Pole(new Vector3(-0.55f, 0.1f, 0.2f), new Vector3(0.55f, 0.1f, 0.2f), 0.1f, new Color(0.35f, 0.32f, 0.22f));
            B(new Vector3(0.1f, 0.03f, -0.6f), new Vector3(1.3f, 0.05f, 0.9f), Fur, ClothS, 0.02f, Quaternion.Euler(0f, 12f, 0f), 0.04f);
        }

        private static void BuildTentFar()
        {
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 n = new Vector3(s * TentH, TentW, 0f).normalized;
                Vector3 a = new Vector3(0f, TentH, -TentL), b = new Vector3(0f, TentH, TentL), c = new Vector3(s * TentW, 0.1f, TentL), d = new Vector3(s * TentW, 0.1f, -TentL);
                kit.Polygon(new[] { a, b, c, d }, a - n, ClothS, p => Color.Lerp(Cloth, Mud, 0.3f * (1f - p.y / TentH)));
            }
            Vector3 bl = new Vector3(-TentW, 0.1f, -TentL), br = new Vector3(TentW, 0.1f, -TentL), bt = new Vector3(0f, TentH, -TentL);
            kit.Polygon(new[] { bl, br, bt }, new Vector3(0f, 1f, 0f), ClothS, p => Cloth * 0.8f);
            Vector3 fl = new Vector3(-TentW, 0.1f, TentL), fr = new Vector3(TentW, 0.1f, TentL), ft = new Vector3(0f, TentH, TentL);
            kit.Polygon(new[] { fl, fr, ft }, new Vector3(0f, 1f, 0f), ClothS, p => p.y < 1.4f && Mathf.Abs(p.x) < 0.5f ? Char : Cloth * 0.9f);
        }

        // ------------------------------------------------------------------ barricade, fire, standard, clutter

        private static void BuildBarricade()
        {
            // A rough log on the ground with three pairs of sharpened stakes through it, crossed, lashed with rope
            Pole(new Vector3(-1.3f, 0.32f, 0f), new Vector3(1.3f, 0.36f, 0f), 0.13f, Bark, bark: true);
            for (int i = 0; i < 4; i++)
            {
                float x = -1.05f + i * 0.7f;
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 dir = new Vector3(Rand(-0.08f, 0.08f), 0.72f, s * 0.7f).normalized;
                    Vector3 mid = new Vector3(x + s * 0.08f, 0.34f, 0f);
                    Pole(mid - dir * 0.75f, mid + dir * 1.05f, 0.05f, Wood, sharpened: true);
                }
                B(new Vector3(x, 0.34f, 0f), new Vector3(0.22f, 0.29f, 0.29f), Rope, ClothS, 0.04f);
            }
        }

        private static void BuildStandard()
        {
            Pole(new Vector3(0f, -0.2f, 0f), new Vector3(0f, 4.0f, 0f), 0.06f, WoodDark);
            Pole(new Vector3(-0.15f, 3.65f, 0f), new Vector3(1.35f, 3.65f, 0f), 0.035f, WoodDark);
            kit.Lathe(Matrix4x4.Translate(new Vector3(0f, 4.0f, 0f)), new[] { new Vector2(0f, 0f), new Vector2(0.07f, 0f), new Vector2(0.03f, 0.12f), new Vector2(0f, 0.38f) }, 6, IronS, (i, a) => Iron);
            // A ragged swallow-tailed banner hanging from the crossbar, a little wavy
            const int rows = 7, cols = 5;
            var p = new Vector3[rows, cols];
            for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                float u = i / (float)(rows - 1), v = j / (float)(cols - 1);
                float x = 0.05f + v * 1.2f;
                float drop = 1.6f - (v > 0.35f && v < 0.65f ? 0.45f : 0f) * u; // the notch of the swallow tail
                p[i, j] = new Vector3(x, 3.6f - u * drop, Mathf.Sin(v * 5f + u * 2f) * 0.05f * u);
            }
            Func<int, int, Color> c = (i, j) => (i == 2 || i == 3) ? new Color(0.12f, 0.10f, 0.09f) : Cloth * (0.9f + 0.1f * SdfCreature.Noise(new Vector3(i, j, 1f)));
            kit.Grid(p, false, ClothS, c, Vector3.forward);
            kit.Grid(p, false, ClothS, c, Vector3.forward, inward: true);
            // Rope lashings and a skull-like iron boss on the pole
            B(new Vector3(0f, 3.65f, 0f), new Vector3(0.16f, 0.12f, 0.16f), Rope, ClothS, 0.03f);
            B(new Vector3(0f, 2.4f, 0.07f), new Vector3(0.2f, 0.24f, 0.08f), Iron, IronS, 0.04f);
        }

        private static void BuildClutter()
        {
            // Two crates, a barrel, a rack of spears
            B(new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.5f, 0.5f), Wood, WoodS, 0.02f, Quaternion.Euler(0f, 8f, 0f), 0f, p => Mathf.Abs(p.x) > 0.26f || Mathf.Abs(p.y) > 0.21f ? WoodDark : Wood);
            B(new Vector3(0.12f, 0.68f, 0.02f), new Vector3(0.45f, 0.36f, 0.4f), Wood * 0.9f, WoodS, 0.02f, Quaternion.Euler(0f, -14f, 0f));
            kit.Lathe(Matrix4x4.Translate(new Vector3(0.85f, 0f, 0.1f)),
                      new[] { new Vector2(0f, 0f), new Vector2(0.24f, 0f), new Vector2(0.29f, 0.35f), new Vector2(0.24f, 0.7f), new Vector2(0f, 0.7f) }, 12, WoodS,
                      (i, a) => i == 1 || i == 3 ? Iron : Wood * (Mathf.Repeat(a * 12f / (Mathf.PI * 2f), 1f) < 0.1f ? 0.65f : 0.95f));
            Pole(new Vector3(-0.8f, 0f, -0.2f), new Vector3(-0.8f, 1.0f, -0.2f), 0.04f, WoodDark);
            Pole(new Vector3(-1.6f, 0f, -0.2f), new Vector3(-1.6f, 1.0f, -0.2f), 0.04f, WoodDark);
            Pole(new Vector3(-1.7f, 0.9f, -0.2f), new Vector3(-0.7f, 0.9f, -0.2f), 0.03f, WoodDark);
            for (int k = 0; k < 4; k++)
            {
                Vector3 foot = new Vector3(-1.5f + k * 0.2f, 0f, 0.05f);
                Pole(foot, foot + new Vector3(Rand(-0.05f, 0.05f), 2.0f, -0.32f), 0.02f, Wood);
                Vector3 tip = foot + new Vector3(0f, 2.0f, -0.32f);
                kit.Lathe(Matrix4x4.TRS(tip, Quaternion.FromToRotation(Vector3.up, new Vector3(0f, 2.0f, -0.32f).normalized), Vector3.one),
                          new[] { new Vector2(0f, 0f), new Vector2(0.035f, 0.02f), new Vector2(0f, 0.22f) }, 4, IronS, (i, a) => Iron);
            }
        }

        /// <summary>A piece under the camp root: one renderer on the prop material (the tent with a far model).</summary>
        public static GameObject Attach(Transform parent, string name, Mesh mesh, Mesh far = null, float nearScreenHeight = 0.06f)
        {
            if (far != null) return MeshKit.AttachWithLod(parent, name, mesh, far, nearScreenHeight);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MeshKit.PropMaterial();
            return go;
        }
    }
}
