using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// First-person arms (viewmodel camera space: +Z forward, +X right), one mesh and one material per arm with the
    /// surface (colour, CharacterSurface pattern, metallic, smoothness) per vertex. Each arm hangs from a shoulder pivot
    /// just outside the view ("RightArmPivot" / "LeftArmPivot") that FpsViewmodelAnimation rotates for the swing.
    /// Gloved fists are sculpted (SdfCreature) around the grips; the hard parts are lathes and lofts with crisp edges:
    /// quilted gambeson sleeves, ridged steel vambraces with strapped buckles, flared leather cuffs, an arming sword
    /// (bevelled blade with a fuller, curved crossguard with finials, cord-wrapped grip, wheel pommel) and a round
    /// plank shield seen from behind (battens, iron rim with rivets, centre grip under the boss).
    /// "Sword" / "Shield" are empty markers at the grips (framing for the capture tools).
    /// </summary>
    public static class ViewmodelArmsBuilder
    {
        // Placement (pivot space) kept from the original primitive arms, so the on-screen layout and the swing match.
        private static readonly Vector3 RightPivot = new Vector3(0.30f, -0.44f, 0.16f);
        private static readonly Vector3 LeftPivot = new Vector3(-0.36f, -0.50f, 0.14f);
        private static readonly Vector3 SwordPos = new Vector3(0.02f, 0.13f, 0.47f);
        private static readonly Quaternion SwordRot = Quaternion.Euler(-28f, -12f, -8f);
        private static readonly Vector3 ShieldPos = new Vector3(-0.12f, 0.13f, 0.46f);
        private static readonly Quaternion ShieldRot = Quaternion.Euler(90f, -22f, 0f);

        private static readonly Color Gambeson = new Color(0.12f, 0.26f, 0.62f);
        private static readonly Color Steel = new Color(0.78f, 0.80f, 0.84f);
        private static readonly Color Iron = new Color(0.34f, 0.34f, 0.36f);
        private static readonly Color Leather = new Color(0.32f, 0.19f, 0.10f);
        private static readonly Color DarkLeather = new Color(0.17f, 0.10f, 0.06f);
        private static readonly Color Thread = new Color(0.72f, 0.64f, 0.48f);
        private static readonly Color Brass = new Color(0.86f, 0.66f, 0.28f);
        private static readonly Color Wood = new Color(0.46f, 0.31f, 0.18f);
        private static readonly Color ShieldPaint = new Color(0.13f, 0.27f, 0.60f);
        private static readonly Color ShieldBand = new Color(0.90f, 0.84f, 0.68f);

        // (pattern, metallic, smoothness): 1 cloth, 2 leather, 3 metal, 4 hair (wood grain)
        private static readonly Vector3 ClothS = new Vector3(1f, 0f, 0.15f);
        private static readonly Vector3 LeatherS = new Vector3(2f, 0f, 0.38f);
        private static readonly Vector3 SteelS = new Vector3(3f, 0.9f, 0.74f);
        private static readonly Vector3 IronS = new Vector3(3f, 0.8f, 0.45f);
        private static readonly Vector3 BrassS = new Vector3(3f, 0.95f, 0.62f);
        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.22f);
        private static readonly Vector3 PaintS = new Vector3(2f, 0f, 0.30f);

        private const float ShieldRadius = 0.225f;

        /// <summary>Builds the arms; meshCache (key "Right" / "Left") can return previously baked meshes instead.</summary>
        public static GameObject Create(string name = "ViewmodelArms", Func<string, Mesh> meshCache = null)
        {
            GameObject root = new GameObject(name);
            Material material = SharedMaterial();

            Transform right = Pivot(root.transform, "RightArmPivot", RightPivot);
            Marker(right, "Sword", SwordPos, SwordRot);
            Attach(right, "RightArm", meshCache?.Invoke("Right") ?? BuildRight(), material);

            Transform left = Pivot(root.transform, "LeftArmPivot", LeftPivot);
            Marker(left, "Shield", ShieldPos, ShieldRot);
            Attach(left, "LeftArm", meshCache?.Invoke("Left") ?? BuildLeft(), material);
            return root;
        }

        private static Material sharedMaterial;

        public static Material SharedMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Shader.Find("Custom/CharacterSurface");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            sharedMaterial = new Material(shader) { name = "MAT_ViewmodelArms" };
            sharedMaterial.SetColor("_BaseColor", Color.white);
            sharedMaterial.SetFloat("_VertexSurface", 1f);
            sharedMaterial.SetFloat("_UseVertexColor", 1f);
            sharedMaterial.SetFloat("_ObjectSpace", 1f);
            return sharedMaterial;
        }

        // ------------------------------------------------------------------ right: arm, fist, sword

        public static Mesh BuildRight()
        {
            var kit = new MeshKit();
            Vector3 grip = SwordRot * Vector3.up;
            Vector3 f = Vector3.ProjectOnPlane(SwordPos.normalized, grip).normalized;
            Vector3 d = Vector3.Cross(grip, f); // back of the right hand faces out (+X)
            Vector3 wrist = Fist(kit, SwordPos, f, d, true, 0.0165f);
            Arm(kit, wrist, true);
            Sword(kit, Matrix4x4.TRS(SwordPos, SwordRot, Vector3.one));
            return kit.ToMesh("ViewmodelArm_Right");
        }

        private static void Sword(MeshKit kit, Matrix4x4 m)
        {
            // ---- blade: bevelled edges, flats and a fuller fading out before the point (rows along +Y)
            var ys = new List<float> { 0.094f, 0.102f };
            for (float y = 0.14f; y < 0.70f; y += 0.04f) ys.Add(y);
            ys.AddRange(new[] { 0.70f, 0.74f, 0.77f, 0.80f, 0.82f, 0.836f, 0.849f, 0.858f, 0.866f });
            const int SectionPoints = 22;
            var blade = new Vector3[ys.Count, SectionPoints];
            var bladeColor = new Color[ys.Count, SectionPoints];
            Color edge = new Color(0.95f, 0.96f, 0.97f), bevel = new Color(0.84f, 0.86f, 0.89f), flat = Steel, fuller = new Color(0.60f, 0.62f, 0.66f);
            for (int i = 0; i < ys.Count; i++)
            {
                float y = ys[i];
                float u = Mathf.Clamp01((y - 0.70f) / 0.166f);
                float w = y <= 0.70f ? Mathf.Lerp(0.0265f, 0.021f, (y - 0.094f) / 0.606f) : 0.021f * Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.75f);
                float t = Mathf.Lerp(0.0056f, 0.0042f, Mathf.Clamp01((y - 0.094f) / 0.606f)) * Mathf.Lerp(1f, 0.25f, u);
                w = Mathf.Max(w, 0.0003f); t = Mathf.Max(t, 0.0003f);
                float full = Mathf.InverseLerp(0.094f, 0.13f, y) * Mathf.InverseLerp(0.64f, 0.56f, y);
                float bev = Mathf.Min(0.0085f, w * 0.42f), b = w - bev, fw = Mathf.Min(0.0062f, b * 0.8f), fd = 0.0019f * full;
                // Around the section: edge, bevel line, flat, fuller edge, fuller centre ... (pairs = crisp creases)
                var pts = new[]
                {
                    new Vector2(w, 0f), new Vector2(w, 0f), new Vector2(b, t), new Vector2(b, t), new Vector2(fw, t), new Vector2(fw, t), new Vector2(0f, t - fd),
                    new Vector2(-fw, t), new Vector2(-fw, t), new Vector2(-b, t), new Vector2(-b, t), new Vector2(-w, 0f), new Vector2(-w, 0f),
                    new Vector2(-b, -t), new Vector2(-b, -t), new Vector2(-fw, -t), new Vector2(-fw, -t), new Vector2(0f, -t + fd),
                    new Vector2(fw, -t), new Vector2(fw, -t), new Vector2(b, -t), new Vector2(b, -t)
                };
                var cols = new[] { edge, edge, bevel, flat, flat, fuller, fuller * 0.9f, fuller, flat, flat, bevel, edge, edge, bevel, flat, flat, fuller, fuller * 0.9f, fuller, flat, flat, bevel };
                for (int j = 0; j < SectionPoints; j++)
                {
                    blade[i, j] = m.MultiplyPoint3x4(new Vector3(pts[j].x, y, pts[j].y));
                    bladeColor[i, j] = Color.Lerp(cols[j], flat, 1f - full) * (j == 6 || j == 17 ? Mathf.Lerp(1f, 0.9f, full) : 1f);
                }
            }
            kit.Grid(blade, true, SteelS, (i, j) => bladeColor[i, j]);

            // ---- crossguard: rounded bar curving towards the blade, swelling at the centre, ball finials
            const int GuardRows = 49, Ring = 20;
            var guard = new Vector3[GuardRows, Ring];
            for (int i = 0; i < GuardRows; i++)
            {
                float s = -1f + 2f * i / (GuardRows - 1);
                float x = 0.112f * Mathf.Sin(s * Mathf.PI * 0.5f), ax = Mathf.Abs(x);
                float centre = Mathf.Exp(-(x / 0.02f) * (x / 0.02f));
                float core = Mathf.Clamp01((0.106f - ax) / 0.012f);
                float bulb = 0.0105f * Mathf.Sqrt(Mathf.Max(0f, 1f - Sq((ax - 0.101f) / 0.011f)));
                float hy = Mathf.Max((0.0068f + 0.0035f * centre) * core, bulb), hz = Mathf.Max((0.0075f + 0.0045f * centre) * core, bulb);
                Vector3 c = new Vector3(x, 0.085f + 0.011f * Sq(x / 0.105f), 0f);
                for (int j = 0; j < Ring; j++)
                {
                    float a = j * Mathf.PI * 2f / Ring;
                    guard[i, j] = m.MultiplyPoint3x4(c + new Vector3(0f, hy * SignedPow(Mathf.Cos(a), 0.55f), hz * SignedPow(Mathf.Sin(a), 0.55f)));
                }
            }
            kit.Grid(guard, true, BrassS, (i, j) => Brass * (0.92f + 0.12f * Mathf.Max(0f, Mathf.Sin(j * Mathf.PI * 2f / Ring))));

            // ---- grip: slightly swelling core under a spiral cord wrap, a brass ferrule under the guard
            var gripProfile = new List<Vector2> { V2(0f, -0.072f) }; // closed ends: seen through the ferrules
            for (int i = 0; i <= 70; i++)
            {
                float y = Mathf.Lerp(-0.072f, 0.074f, i / 70f);
                gripProfile.Add(new Vector2((0.0158f + 0.0014f * Mathf.Sin(Mathf.PI * (y + 0.072f) / 0.146f)) * Mathf.Lerp(0.62f, 1f, Mathf.InverseLerp(-0.072f, -0.058f, y)), y)); // seated into the pommel
            }
            gripProfile.Add(V2(0f, 0.074f));
            float Cord(float y, float a) => Mathf.Pow(0.5f + 0.5f * Mathf.Cos(a + y * Mathf.PI * 2f / 0.011f), 4f);
            kit.Lathe(m, gripProfile.ToArray(), 24, LeatherS, (i, a) => DarkLeather * (0.7f + 0.45f * Cord(gripProfile[i].y, a)),
                      (i, a) => 1f + 0.07f * Cord(gripProfile[i].y, a));
            foreach (float y0 in new[] { 0.068f })
                kit.Lathe(m, new[] { V2(0.0145f, y0), V2(0.0172f, y0), V2(0.0189f, y0 + 0.0015f), V2(0.0189f, y0 + 0.0095f), V2(0.0172f, y0 + 0.011f), V2(0.0145f, y0 + 0.011f) }, 32, BrassS, (i, a) => Brass);

            // ---- wheel pommel (axis across the blade flat) with a raised centre boss, and the peened tang end
            Matrix4x4 wheel = m * Matrix4x4.TRS(new Vector3(0f, -0.095f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            var wheelProfile = new[]
            {
                V2(0f, 0.0105f), V2(0.0085f, 0.0105f), V2(0.0105f, 0.0085f), V2(0.0105f, 0.0085f), V2(0.023f, 0.0068f), V2(0.0295f, 0.0042f), V2(0.0318f, 0.0012f),
                V2(0.0318f, -0.0012f), V2(0.0295f, -0.0042f), V2(0.023f, -0.0068f), V2(0.0105f, -0.0085f), V2(0.0105f, -0.0085f), V2(0.0085f, -0.0105f), V2(0f, -0.0105f)
            };
            kit.Lathe(wheel, wheelProfile, 48, BrassS, (i, a) => Brass * (wheelProfile[i].x < 0.0106f ? 1.08f : 0.95f));
            kit.Lathe(m, new[] { V2(0f, -0.133f), V2(0.0045f, -0.1315f), V2(0.0062f, -0.1275f), V2(0.0058f, -0.123f) }, 20, BrassS, (i, a) => Brass * 0.9f);
        }

        // ------------------------------------------------------------------ left: arm, fist, round shield

        public static Mesh BuildLeft()
        {
            var kit = new MeshKit();
            Matrix4x4 s = Matrix4x4.TRS(ShieldPos, ShieldRot, Vector3.one);
            float handleDepth = 0.016f;
            Vector3 gripCentre = s.MultiplyPoint3x4(new Vector3(0f, Back(0f) - handleDepth, 0f));
            Vector3 grip = ShieldRot * Vector3.right;
            if (grip.x < 0f) grip = -grip; // thumb towards the body
            Vector3 f = Vector3.ProjectOnPlane(gripCentre.normalized, grip).normalized;
            Vector3 d = Vector3.Cross(f, grip); // back of the left hand faces up
            Vector3 wrist = Fist(kit, gripCentre, f, d, false, 0.0105f);
            Arm(kit, wrist, false);
            Shield(kit, s, handleDepth);
            return kit.ToMesh("ViewmodelArm_Left");
        }

        private static float Front(float r) => 0.0075f + 0.005f * (1f - Sq(r / ShieldRadius));
        private static float Back(float r) => Front(r) - 0.013f;

        private static void Shield(MeshKit kit, Matrix4x4 s, float handleDepth)
        {
            float R = ShieldRadius;
            Vector3 up = s.MultiplyVector(Vector3.up), side = s.MultiplyVector(Vector3.right);
            Matrix4x4 sInv = s.inverse;
            float[] tone = { 1.0f, 0.86f, 1.06f, 0.92f, 1.1f, 0.95f };
            const int Rows = 20, Cols = 7;
            const float Hole = 0.06f; // the fist reaches into the boss through a hole in the planks

            // ---- six planks (two halves around the hole): front painted, back bare wood, side walls at the seams
            for (int k = 0; k < 6; k++)
            {
                float x0 = -R + k * 0.075f + 0.0006f, x1 = x0 + 0.075f - 0.0012f, tk = tone[k];
                foreach (float half in new[] { -1f, 1f })
                {
                    var back = new Vector3[Rows, Cols];
                    var front = new Vector3[Rows, Cols];
                    var wallA = new Vector3[Rows, 2];
                    var wallB = new Vector3[Rows, 2];
                    for (int i = 0; i < Rows; i++)
                    {
                        float u = i / (Rows - 1f);
                        for (int j = 0; j < Cols; j++)
                        {
                            float x = Mathf.Lerp(x0, x1, j / (Cols - 1f));
                            float zOut = Mathf.Sqrt(Mathf.Max(0f, R * R - x * x)) * 0.995f, zIn = Mathf.Abs(x) < Hole ? Mathf.Sqrt(Hole * Hole - x * x) : 0f;
                            float z = half * Mathf.Lerp(zIn, zOut, u), r = Mathf.Sqrt(x * x + z * z);
                            back[i, j] = s.MultiplyPoint3x4(new Vector3(x, Back(r), z));
                            front[i, j] = s.MultiplyPoint3x4(new Vector3(x, Front(r), z));
                        }
                        wallA[i, 0] = back[i, 0]; wallA[i, 1] = front[i, 0];
                        wallB[i, 0] = back[i, Cols - 1]; wallB[i, 1] = front[i, Cols - 1];
                    }
                    kit.Grid(back, false, WoodS, (i, j) => Wood * tk * (0.88f + 0.22f * SdfCreature.Noise(back[i, j] * 45f + Vector3.one * k)), -up);
                    kit.Grid(front, false, PaintS, (i, j) => FrontPaint(sInv.MultiplyPoint3x4(front[i, j])), up);
                    kit.Grid(wallA, false, WoodS, (i, j) => Wood * tk * 0.7f, -side);
                    kit.Grid(wallB, false, WoodS, (i, j) => Wood * tk * 0.7f, side);
                }
            }
            // Wall of the hole, and the inside of the boss seen through it
            kit.Lathe(s, new[] { V2(Hole, Back(Hole)), V2(Hole, Front(Hole) + 0.003f) }, 40, WoodS, (i, a) => Wood * 0.55f, null, true);
            var cavity = new List<Vector2> { V2(0.059f, Front(0.06f) + 0.002f) };
            for (int k = 1; k <= 9; k++)
            {
                float a = k * Mathf.PI * 0.5f / 9f;
                cavity.Add(V2(k == 9 ? 0f : 0.055f * Mathf.Cos(a), Front(0f) + 0.006f + 0.033f * Mathf.Sin(a)));
            }
            kit.Lathe(s, cavity.ToArray(), 40, IronS, (i, a) => Iron * 0.6f, null, true);

            // ---- two battens across the back holding the planks
            foreach (float zc in new[] { -0.115f, 0.115f })
            {
                float xm = Mathf.Sqrt(R * R - zc * zc) - 0.016f;
                var batten = new Vector3[18, 16];
                for (int i = 0; i < 18; i++)
                {
                    float x = Mathf.Lerp(-xm, xm, i / 17f), end = Mathf.Clamp01((xm - Mathf.Abs(x)) / 0.008f + 0.15f);
                    float yc = Back(Mathf.Sqrt(x * x + zc * zc)) - 0.0035f;
                    for (int j = 0; j < 16; j++)
                    {
                        float a = j * Mathf.PI * 2f / 16f;
                        batten[i, j] = s.MultiplyPoint3x4(new Vector3(x, yc + 0.0055f * SignedPow(Mathf.Cos(a), 0.5f), zc + 0.012f * end * SignedPow(Mathf.Sin(a), 0.5f)));
                    }
                }
                kit.Grid(batten, true, WoodS, (i, j) => Wood * 0.78f);
            }

            // ---- centre handle under the boss: wood, leather-wrapped where the fist holds it, iron bands at the ends
            var handle = new Vector3[34, 16];
            var handleX = new float[34];
            for (int i = 0; i < 34; i++)
            {
                float x = Mathf.Lerp(-0.085f, 0.085f, i / 33f), bend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.085f, 0.055f, Mathf.Abs(x)));
                handleX[i] = x;
                float yc = Back(Mathf.Abs(x)) - handleDepth * bend + 0.002f * (1f - bend);
                for (int j = 0; j < 16; j++)
                {
                    float a = j * Mathf.PI * 2f / 16f;
                    handle[i, j] = s.MultiplyPoint3x4(new Vector3(x, yc + 0.0085f * SignedPow(Mathf.Cos(a), 0.6f), 0.011f * SignedPow(Mathf.Sin(a), 0.6f)));
                }
            }
            kit.Grid(handle, true, WoodS, (i, j) =>
            {
                float ax = Mathf.Abs(handleX[i]);
                return ax < 0.045f ? DarkLeather : (ax > 0.062f && ax < 0.074f ? Iron : Wood * 0.8f);
            });

            // ---- iron rim wrapped over the edge, clamped with rivets on the back
            var rim = new[]
            {
                V2(R - 0.011f, 0.0105f), V2(R - 0.004f, 0.0125f), V2(R + 0.0035f, 0.0095f), V2(R + 0.0065f, 0.002f),
                V2(R + 0.0065f, -0.002f), V2(R + 0.0035f, -0.0095f), V2(R - 0.004f, -0.0125f), V2(R - 0.012f, -0.0105f)
            };
            kit.Lathe(s, rim, 96, IronS, (i, a) => Iron * (i == 3 || i == 4 ? 1.25f : 1f));
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI * 2f / 16f + 0.1f;
                Vector3 p = new Vector3(Mathf.Cos(a) * (R - 0.0075f), -0.0122f, Mathf.Sin(a) * (R - 0.0075f));
                kit.Dome(s.MultiplyPoint3x4(p), -up, 0.0034f, Iron * 1.15f, IronS);
            }

            // ---- steel boss on the front with a flange and rivets
            var boss = new List<Vector2> { V2(0.078f, Front(0.078f) - 0.001f), V2(0.077f, Front(0.077f) + 0.003f), V2(0.068f, Front(0.068f) + 0.0045f), V2(0.068f, Front(0.068f) + 0.0045f) };
            for (int k = 0; k <= 9; k++)
            {
                float a = k * Mathf.PI * 0.5f / 9f;
                boss.Add(V2(0.058f * Mathf.Cos(a), Front(0f) + 0.006f + 0.036f * Mathf.Sin(a)));
            }
            boss[boss.Count - 1] = V2(0f, boss[boss.Count - 1].y);
            kit.Lathe(s, boss.ToArray(), 64, SteelS, (i, a) => Steel * (i < 4 ? 0.85f : 1f));
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                kit.Dome(s.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * 0.072f, Front(0.072f) + 0.0042f, Mathf.Sin(a) * 0.072f)), up, 0.0035f, Steel * 0.9f, SteelS);
            }
        }

        /// <summary>Front of the shield: blue field with a cream ring towards the rim.</summary>
        private static Color FrontPaint(Vector3 q)
        {
            float r = new Vector2(q.x, q.z).magnitude;
            float band = Mathf.InverseLerp(0.158f, 0.164f, r) * Mathf.InverseLerp(0.190f, 0.184f, r);
            return Color.Lerp(ShieldPaint, ShieldBand, band) * (0.92f + 0.12f * SdfCreature.Noise(q * 30f));
        }

        // ------------------------------------------------------------------ shared: arm and fist

        /// <summary>
        /// Sleeve, vambrace with straps and buckle, and glove cuff from the shoulder pivot (origin) to the wrist.
        /// </summary>
        private static void Arm(MeshKit kit, Vector3 wrist, bool right)
        {
            float L = wrist.magnitude;
            Vector3 axis = wrist / L;
            Vector3 top = Vector3.ProjectOnPlane(Vector3.up, axis).normalized;
            Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, Quaternion.LookRotation(top, axis), Vector3.one);
            // Lathe angle of the side the camera sees (inner top of the arm)
            Vector3 toCam = Vector3.ProjectOnPlane(-(right ? RightPivot : LeftPivot) - wrist * 0.7f, axis).normalized;
            float seen = Mathf.Atan2(Vector3.Dot(toCam, m.MultiplyVector(Vector3.forward)), Vector3.Dot(toCam, m.MultiplyVector(Vector3.right)));

            // ---- quilted gambeson sleeve, bunched where it enters the vambrace
            float s1 = 0.48f * L;
            var sleeve = new List<Vector2> { V2(0f, -0.03f) };
            for (int i = 0; i <= 26; i++) sleeve.Add(V2(0.058f, Mathf.Lerp(-0.02f, s1, i / 26f)));
            float Quilt(float a) => Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 5f)), 0.5f);
            kit.Lathe(m, sleeve.ToArray(), 60, ClothS, (i, a) => Gambeson * Mathf.Lerp(0.7f, 1.02f, Quilt(a)),
                      (i, a) =>
                      {
                          float y = sleeve[i].y, bunch = Mathf.InverseLerp(s1 - 0.10f, s1, y);
                          return (0.95f + 0.06f * Quilt(a)) * (1f + 0.035f * Mathf.Sin(y * 95f + a) * bunch + 0.02f * bunch);
                      });

            // ---- steel vambrace: rolled beads at both ends, ridge along the top
            float v0 = 0.42f * L, v1 = 0.86f * L;
            var vam = new List<Vector2> { V2(0.050f, v0), V2(0.0565f, v0 + 0.003f), V2(0.0585f, v0 + 0.008f), V2(0.0565f, v0 + 0.014f), V2(0.0545f, v0 + 0.02f) };
            int bodyStart = vam.Count;
            for (int i = 1; i <= 16; i++) vam.Add(V2(Mathf.Lerp(0.0545f, 0.0455f, i / 16f), Mathf.Lerp(v0 + 0.02f, v1 - 0.016f, i / 16f)));
            int bodyEnd = vam.Count;
            vam.AddRange(new[] { V2(0.0475f, v1 - 0.011f), V2(0.049f, v1 - 0.006f), V2(0.0475f, v1 - 0.001f), V2(0.043f, v1) });
            float Ridge(float a) => Mathf.Exp(-Sq(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f) / 13f));
            kit.Lathe(m, vam.ToArray(), 56, SteelS, (i, a) => Steel * (i >= bodyStart && i < bodyEnd ? 1f + 0.08f * Ridge(a) : 0.9f),
                      (i, a) => i >= bodyStart - 1 && i <= bodyEnd ? 1f + 0.07f * Ridge(a) : 1f);
            float VamRadius(float y) => Mathf.Lerp(0.0545f, 0.0455f, Mathf.InverseLerp(v0 + 0.02f, v1 - 0.016f, y));

            // Strap with a brass buckle on the visible side, rivets on the beads
            foreach (float tc in new[] { 0.55f, 0.74f })
            {
                float y = tc * L, r = VamRadius(y);
                var strap = new[] { V2(r - 0.001f, y - 0.0115f), V2(r + 0.0032f, y - 0.0105f), V2(r + 0.0038f, y), V2(r + 0.0032f, y + 0.0105f), V2(r - 0.001f, y + 0.0115f) };
                kit.Lathe(m, strap, 56, LeatherS, (i, a) => DarkLeather * (i == 2 ? 1.15f : 1f), (i, a) => 1f + 0.07f * Ridge(a));
                float ba = seen + 0.5f;
                Vector3 radial = m.MultiplyVector(new Vector3(Mathf.Cos(ba), 0f, Mathf.Sin(ba)));
                Vector3 at = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(ba) * (r + 0.004f), y, Mathf.Sin(ba) * (r + 0.004f)));
                Buckle(kit, at, radial, m.MultiplyVector(Vector3.up));
            }
            for (int k = -2; k <= 2; k++)
            {
                float a = seen + k * 0.45f;
                foreach (float y in new[] { v0 + 0.008f, v1 - 0.006f })
                {
                    float r = y < L * 0.6f ? 0.0585f : 0.049f;
                    Vector3 n = m.MultiplyVector(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                    kit.Dome(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)), n, 0.0028f, Steel * 0.95f, SteelS);
                }
            }

            // ---- flared leather glove cuff over the end of the vambrace, stitched edge, narrowing into the wrist
            float c0 = 0.80f * L;
            var cuff = new List<Vector2> { V2(0.0505f, c0), V2(0.0545f, c0 + 0.003f), V2(0.0555f, c0 + 0.008f), V2(0.0535f, c0 + 0.014f) };
            for (int i = 1; i <= 10; i++)
            {
                float u = i / 10f;
                cuff.Add(V2(Mathf.Lerp(0.0535f, 0.031f, u * u * (3f - 2f * u)), Mathf.Lerp(c0 + 0.014f, L + 0.006f, u)));
            }
            kit.Lathe(m, cuff.ToArray(), 56, LeatherS, (i, a) =>
            {
                if (i == 3 && Mathf.Repeat(a * 72f / (Mathf.PI * 2f), 1f) < 0.4f) return Color.Lerp(Leather, Thread, 0.6f); // stitches along the edge
                return Leather * (i <= 2 ? 0.8f : 1f);
            });
        }

        /// <summary>Brass buckle frame lying on a strap (centre, strap normal, strap direction along the arm).</summary>
        private static void Buckle(MeshKit kit, Vector3 centre, Vector3 normal, Vector3 along)
        {
            Matrix4x4 m = Matrix4x4.TRS(centre, Quaternion.LookRotation(along, normal), Vector3.one);
            var ring = new Vector3[17, 8];
            for (int i = 0; i <= 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Vector2 c = new Vector2(0.0085f * SignedPow(Mathf.Cos(a), 0.4f), 0.0095f * SignedPow(Mathf.Sin(a), 0.4f));
                Vector2 o = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (int j = 0; j < 8; j++)
                {
                    float b = j * Mathf.PI * 2f / 8f;
                    Vector2 xy = c + o * (0.0017f * Mathf.Cos(b));
                    ring[i, j] = m.MultiplyPoint3x4(new Vector3(xy.x, 0.0017f * Mathf.Sin(b), xy.y));
                }
            }
            kit.Grid(ring, true, BrassS, (i, j) => Brass);
        }

        /// <summary>
        /// Gloved fist sculpted around a grip (axis g = f x d for the right hand, d x f for the left): fingers curl
        /// over the top and front of the grip and back underneath, the thumb wraps over the index finger.
        /// f = forearm direction (across the grip), d = back of the hand.
        /// Returns the wrist end (pivot space) where the arm attaches.
        /// </summary>
        private static Vector3 Fist(MeshKit kit, Vector3 centre, Vector3 f, Vector3 d, bool right, float gripRadius)
        {
            var tmp = new GameObject("FistSculpt").transform;
            Transform frame = new GameObject("FistFrame").transform;
            frame.SetParent(tmp, false);
            frame.localPosition = centre;
            frame.localRotation = Quaternion.LookRotation(f, d);
            float gs = right ? -1f : 1f; // grip axis along frame x * gs (index finger on the + side)
            Vector3 P(float g, float dd, float ff) => new Vector3(gs * g, dd, ff);

            var sdf = new SdfCreature(tmp)
            {
                AmbientOcclusion = 0.6f, OcclusionScale = 0.22f,
                Paint = (p, n, c, paintable) => c * Mathf.Lerp(0.9f, 1.08f, SdfCreature.Noise(p * 70f)),
                SurfaceBrush = LeatherS
            };
            Color glove = Leather;

            // Palm and the heels of the hand
            sdf.Ellipsoid(frame, P(0f, 0.010f, -0.045f), new Vector3(0.040f, 0.021f, 0.040f), Vector3.zero, glove, 0.012f);
            sdf.Ellipsoid(frame, P(0.024f, -0.010f, -0.044f), new Vector3(0.020f, 0.018f, 0.030f), Vector3.zero, glove, 0.010f);  // thenar
            sdf.Ellipsoid(frame, P(-0.030f, -0.006f, -0.050f), new Vector3(0.014f, 0.016f, 0.030f), Vector3.zero, glove, 0.010f); // hypothenar
            sdf.Cone(frame, P(0f, 0.006f, -0.072f), P(0f, 0.004f, -0.122f), 0.027f, 0.030f, glove, 0.012f);                        // wrist

            // Four fingers curled around the grip: proximal over the top, middle down the front, distal back underneath
            float[] along = { 0.026f, 0.0085f, -0.009f, -0.026f };
            float[] radius = { 0.0108f, 0.0108f, 0.0102f, 0.0092f };
            for (int k = 0; k < 4; k++)
            {
                float rf = radius[k], rr = gripRadius + rf * 0.95f, g = along[k];
                Vector3 At(float deg, float ring) => P(g, ring * Mathf.Sin(deg * Mathf.Deg2Rad), ring * Mathf.Cos(deg * Mathf.Deg2Rad));
                Vector3 mcp = At(108f, rr * 1.18f), pip = At(18f, rr), dip = At(-62f, rr), tip = At(k == 3 ? -118f : -128f, rr * 0.98f);
                sdf.Ellipsoid(frame, mcp, new Vector3(rf * 1.15f, rf * 1.1f, rf * 1.2f), Vector3.zero, glove, 0.006f); // knuckle
                sdf.Cone(frame, mcp, pip, rf * 1.06f, rf, glove, 0.004f);
                sdf.Cone(frame, pip, dip, rf, rf * 0.93f, glove, 0.0035f);
                sdf.Cone(frame, dip, tip, rf * 0.93f, rf * 0.86f, glove, 0.003f);
            }

            // Thumb: from the thenar around under the grip, its tip pressing on the index finger
            Vector3 cmc = P(0.032f, -0.006f, -0.058f), tmcp = P(0.040f, -0.028f, -0.034f), ip = P(0.032f, -0.040f, -0.008f), ttip = P(0.021f, -0.040f, 0.013f);
            sdf.Cone(frame, cmc, tmcp, 0.016f, 0.0135f, glove, 0.008f);
            sdf.Cone(frame, tmcp, ip, 0.0135f, 0.0118f, glove, 0.004f);
            sdf.Cone(frame, ip, ttip, 0.0118f, 0.0105f, glove, 0.003f);

            sdf.BuildStatic(0.003f, kit.V, kit.N, kit.C, kit.S, kit.T);
            Vector3 wrist = frame.TransformPoint(P(0f, 0.004f, -0.122f));
            if (Application.isPlaying) UnityEngine.Object.Destroy(tmp.gameObject); else UnityEngine.Object.DestroyImmediate(tmp.gameObject);
            return wrist;
        }

        // ------------------------------------------------------------------ helpers

        private static Transform Pivot(Transform parent, string name, Vector3 position)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            return t;
        }

        private static void Marker(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = rotation;
        }

        private static void Attach(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        private static Vector2 V2(float x, float y) => new Vector2(x, y);
        private static float Sq(float x) => x * x;
        private static float SignedPow(float v, float p) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), p);
    }
}
