using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// The people on the villager skeleton, sculpted like the knight (SdfCreature, skinned to the rig bones in the rest
    /// pose; the imported low-poly body goes): a head with brow, cheekbones, nose, lips, ears, eyes and brows, hair or a
    /// hood, and for some a beard; a linen tunic to the knee with sleeves, a leather belt, hose with leg wraps, leather
    /// turnshoes, and hands closed in fists (round a tool's grip). The warrior wears a quilted gambeson instead, the bandit
    /// a hood. One renderer, one material, surface per vertex, so worn gear is merged into it (CharacterGear,
    /// GearMeshBuilder fits armour to this body) and a distant mesh is swapped in by SkinnedMeshLod.
    /// </summary>
    public static class PeasantModelBuilder
    {
        private const float NearCell = 0.011f, FarCell = 0.035f, NearScreenHeight = 0.25f, CullScreenHeight = 0.006f;

        public sealed class Style
        {
            public string Key;
            public Color Tunic, Hose, Wraps, Belt, Shoes, Skin, Hair;
            public bool Beard, Gambeson, Hood;
            /// <summary>How thick the beard is: 1 full, less a stubble.</summary>
            public float BeardShade = 0.9f;
            /// <summary>The tunic's hem (root height): above the knee for a peasant, lower for the gambeson.</summary>
            public float Hem = 0.5f;
        }

        private static readonly Color Leather = new Color(0.33f, 0.21f, 0.12f);
        private static readonly Color DarkLeather = new Color(0.18f, 0.12f, 0.08f);

        public static readonly Style Peasant = new Style
        {
            Key = "Peasant", Tunic = new Color(0.40f, 0.50f, 0.28f), Hose = new Color(0.36f, 0.27f, 0.18f), Wraps = new Color(0.62f, 0.56f, 0.44f),
            Belt = Leather, Shoes = DarkLeather, Skin = new Color(0.90f, 0.70f, 0.55f), Hair = new Color(0.40f, 0.27f, 0.15f), Beard = true
        };

        public static readonly Style PeasantShaven = new Style
        {
            Key = "PeasantShaven", Tunic = new Color(0.55f, 0.46f, 0.32f), Hose = new Color(0.30f, 0.30f, 0.26f), Wraps = new Color(0.55f, 0.50f, 0.40f),
            Belt = Leather, Shoes = DarkLeather, Skin = new Color(0.92f, 0.73f, 0.58f), Hair = new Color(0.50f, 0.35f, 0.17f)
        };

        public static readonly Style Warrior = new Style
        {
            Key = "Warrior", Tunic = new Color(0.64f, 0.56f, 0.40f), Hose = new Color(0.26f, 0.20f, 0.14f), Wraps = new Color(0.45f, 0.38f, 0.28f),
            Belt = DarkLeather, Shoes = DarkLeather, Skin = new Color(0.88f, 0.68f, 0.53f), Hair = new Color(0.30f, 0.20f, 0.12f),
            Beard = true, Gambeson = true, Hem = 0.44f
        };

        public static readonly Style Bandit = new Style
        {
            Key = "Bandit", Tunic = new Color(0.45f, 0.14f, 0.11f), Hose = new Color(0.17f, 0.15f, 0.13f), Wraps = new Color(0.30f, 0.26f, 0.22f),
            Belt = DarkLeather, Shoes = DarkLeather, Skin = new Color(0.80f, 0.60f, 0.46f), Hair = new Color(0.30f, 0.21f, 0.14f),
            Beard = true, BeardShade = 0.55f, Hood = true
        };

        /// <summary>A villager made while this is set gets this look rather than a random one (the bake tool).</summary>
        public static Style Forced;

        /// <summary>The sculpted look for a role on the villager skeleton, or null (the goblin keeps the imported body).</summary>
        public static Style For(HumanoidModelBuilder.HumanType type)
        {
            switch (type)
            {
                case HumanoidModelBuilder.HumanType.Villager: return Forced ?? (Random.value < 0.55f ? Peasant : PeasantShaven);
                case HumanoidModelBuilder.HumanType.Soldier: return Warrior;
                case HumanoidModelBuilder.HumanType.Bandit: return Bandit;
                default: return null;
            }
        }

        // Surface = (CharacterSurface pattern, metallic, smoothness)
        private static readonly Vector3 SkinS = new Vector3(0f, 0f, 0.22f);
        private static readonly Vector3 ClothS = new Vector3(1f, 0f, 0.14f);
        private static readonly Vector3 LeatherS = new Vector3(2f, 0f, 0.32f);

        // What the paint does where (the shapes' paintable weight)
        private const float TunicMark = 1f, HoseMark = 0.6f, HairMark = 0.3f, PlainMark = 0f, EarMark = 0.15f;
        private static float eyeLine, faceFront; // the head being painted (hairline)

        private static Style style = Peasant;

        private sealed class Rig
        {
            public Transform Root, Hips, Abdomen, Torso, Neck, Head;
            public Transform[] Shoulder = new Transform[2], UpperArm = new Transform[2], LowerArm = new Transform[2], Palm = new Transform[2];
            public Transform[] UpperLeg = new Transform[2], LowerLeg = new Transform[2], Foot = new Transform[2];
            public Vector3[] ShoulderAt = new Vector3[2], Elbow = new Vector3[2], Wrist = new Vector3[2], Knuckles = new Vector3[2];
            public Vector3[] Hip = new Vector3[2], Knee = new Vector3[2], Ankle = new Vector3[2];
            public Vector3[] Grip = new Vector3[2];
            public Vector3 EyeL, EyeR;
            public Vector3 P(Transform t) => Root.InverseTransformPoint(t.position);
        }

        /// <summary>
        /// Replaces the imported body of a freshly built (rest pose) villager rig with the sculpt; false if bones are
        /// missing. The skin (one renderer under the Animator) and its near and distant meshes come back for the gear.
        /// </summary>
        public static bool Build(GameObject root, Transform model, Style look, out SkinnedMeshRenderer skin, out Mesh near, out Mesh far)
        {
            skin = null; near = far = null;
            style = look ?? Peasant;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var sdf = new SdfCreature(root.transform)
            {
                RootSpaceCoordinates = true, VertexSurfaces = true,
                AmbientOcclusion = 0.12f, OcclusionScale = 0.4f, SkinSigma = 0.03f, Paint = Paint
            };
            SkinnedMeshRenderer[] skins = sdf.ApplyCachedWithLod(style.Key, NearScreenHeight, CullScreenHeight, oneRenderer: true);
            if (skins == null)
            {
                Rig rig = FindRig(root.transform, model);
                if (rig == null) return false;
                Head(sdf, rig);
                Body(sdf, rig);
                Arms(sdf, rig);
                Legs(sdf, rig);
                skins = sdf.ApplyWithLod(style.Key, NearCell, FarCell, NearScreenHeight, CullScreenHeight, oneRenderer: true);
                Debug.Log($"[Peasant {style.Key}] declared and built in {timer.ElapsedMilliseconds} ms");
            }

            foreach (SkinnedMeshRenderer old in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(old.gameObject); else Object.DestroyImmediate(old.gameObject);
            }
            skin = skins[0];
            Bounds b = skin.localBounds;
            b.Expand(0.6f); // tools and weapons swing beyond the body
            skin.localBounds = b;
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            skin.updateWhenOffscreen = false;
            skin.transform.SetParent(model, true);
            SkinnedMeshLod lod = root.GetComponent<SkinnedMeshLod>();
            near = lod != null ? lod.Near : skin.sharedMesh;
            far = lod != null ? lod.Far : null;
            if (lod != null) lod.Apply();
            return true;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        private static Rig FindRig(Transform root, Transform model)
        {
            Transform F(string n) => FindDeep(model, n);
            var r = new Rig { Root = root, Hips = F("Hips"), Abdomen = F("Abdomen"), Torso = F("Torso"), Neck = F("Neck"), Head = F("Head") };
            if (r.Hips == null || r.Abdomen == null || r.Torso == null || r.Neck == null || r.Head == null) return null;
            string[] sides = { "L", "R" };
            for (int s = 0; s < 2; s++)
            {
                string x = "." + sides[s];
                r.Shoulder[s] = F("Shoulder" + x);
                r.UpperArm[s] = F("UpperArm" + x); r.LowerArm[s] = F("LowerArm" + x); r.Palm[s] = F("Palm" + x);
                r.UpperLeg[s] = F("UpperLeg" + x); r.LowerLeg[s] = F("LowerLeg" + x); r.Foot[s] = F("Foot" + x);
                Transform knuckles = F("MiddleHand" + x);
                if (r.UpperArm[s] == null || r.LowerArm[s] == null || r.Palm[s] == null || knuckles == null ||
                    r.UpperLeg[s] == null || r.LowerLeg[s] == null || r.Foot[s] == null) return null;
                r.ShoulderAt[s] = r.P(r.UpperArm[s]); r.Elbow[s] = r.P(r.LowerArm[s]); r.Wrist[s] = r.P(r.Palm[s]); r.Knuckles[s] = r.P(knuckles);
                r.Hip[s] = r.P(r.UpperLeg[s]); r.Knee[s] = r.P(r.LowerLeg[s]); r.Ankle[s] = r.P(r.Foot[s]);
                r.Grip[s] = Vector3.Lerp(r.Wrist[s], r.Knuckles[s], GearMeshBuilder.GripAlongHand);
            }
            CharacterModelLibrary lib = CharacterModelLibrary.Instance;
            bool eyes = lib != null && lib.VillagerEyeLeft != Vector3.zero && lib.VillagerEyeRight != Vector3.zero;
            r.EyeL = eyes ? lib.VillagerEyeLeft : new Vector3(-0.04f, 1.677f, 0.143f);
            r.EyeR = eyes ? lib.VillagerEyeRight : new Vector3(0.043f, 1.677f, 0.144f);
            return r;
        }

        // ------------------------------------------------------------------ head

        private static void Head(SdfCreature sdf, Rig r)
        {
            Vector3 eyeMid = (r.EyeL + r.EyeR) * 0.5f;
            float eyeY = eyeMid.y, front = eyeMid.z; // the face's front at the eyes
            eyeLine = eyeY; faceFront = front;
            // The eyes closer together and back in the face, under the brow (the imported face was flatter and wider)
            r.EyeL = new Vector3(-0.033f, eyeY, front - 0.005f);
            r.EyeR = new Vector3(0.033f, eyeY, front - 0.005f);
            Color skin = style.Skin, hair = style.Hair, lip = Color.Lerp(style.Skin, new Color(0.62f, 0.30f, 0.26f), 0.4f);

            sdf.SurfaceBrush = SkinS;
            // Neck, skull, jaw and chin: broad soft forms, nothing smaller than the sculpt's cells (small things are crisp kits below)
            sdf.Cone(r.Neck, new Vector3(0f, 1.45f, 0.005f), new Vector3(0f, eyeY - 0.07f, front - 0.11f), 0.067f, 0.058f, skin, 0.02f, null, PlainMark);
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY + 0.004f, front - 0.103f), new Vector3(0.078f, 0.104f, 0.098f), Vector3.zero, skin, 0.02f, PlainMark);
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.06f, front - 0.06f), new Vector3(0.064f, 0.05f, 0.066f), Vector3.zero, skin, 0.03f, PlainMark);
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.094f, front - 0.03f), new Vector3(0.03f, 0.022f, 0.026f), Vector3.zero, skin, 0.022f, PlainMark); // chin
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY + 0.022f, front - 0.024f), new Vector3(0.064f, 0.016f, 0.022f), Vector3.zero, skin, 0.016f, PlainMark); // brow ridge
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.012f, front - 0.042f), new Vector3(0.06f, 0.06f, 0.034f), Vector3.zero, skin, 0.02f, PlainMark); // the face's front, flatter than the skull
            // Nose: bridge, tip, wings
            sdf.Cone(r.Head, new Vector3(0f, eyeY + 0.002f, front - 0.005f), new Vector3(0f, eyeY - 0.036f, front + 0.013f), 0.0085f, 0.011f, skin, 0.012f, new Vector3(1f, 1f, 0.9f), PlainMark);
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.038f, front + 0.011f), new Vector3(0.012f, 0.011f, 0.011f), Vector3.zero, skin, 0.008f, PlainMark);
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.043f, front + 0.0f), new Vector3(0.019f, 0.009f, 0.01f), Vector3.zero, skin, 0.01f, PlainMark);
            sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.081f, front - 0.004f), new Vector3(0.018f, 0.0075f, 0.01f), Vector3.zero, lip, 0.008f, PlainMark); // lower lip
            for (int s = 0; s < 2; s++)
            {
                Vector3 eye = s == 0 ? r.EyeL : r.EyeR;
                float side = s == 0 ? -1f : 1f;
                sdf.CarveEllipsoid(r.Head, eye + new Vector3(0f, 0.001f, 0.003f), new Vector3(0.017f, 0.012f, 0.012f), Vector3.zero, 0.006f); // socket
                // Ears
                sdf.Ellipsoid(r.Head, new Vector3(side * 0.079f, eyeY - 0.012f, front - 0.118f), new Vector3(0.012f, 0.03f, 0.02f), new Vector3(0f, side * -15f, 0f), skin, 0.008f, EarMark);
                sdf.CarveEllipsoid(r.Head, new Vector3(side * 0.089f, eyeY - 0.01f, front - 0.116f), new Vector3(0.004f, 0.015f, 0.009f), Vector3.zero, 0.004f);
            }
            sdf.RigidMesh(r.Head, Face(r, eyeY, front, true), Face(r, eyeY, front, false));

            // (the hair keeps the skin's surface: surfaces blend between shapes, and between skin 0 and hair 4 the rounding
            // painted bands of cloth, leather and metal across the face; the hair's colour is painted by where it grows)
            sdf.SurfaceBrush = SkinS;
            if (style.Hood)
            {
                // A cloth hood framing the face, its cowl down over the shoulders
                Color hood = new Color(0.50f, 0.44f, 0.34f);
                sdf.SurfaceBrush = ClothS;
                sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY + 0.012f, front - 0.125f), new Vector3(0.097f, 0.118f, 0.118f), Vector3.zero, hood, 0.015f, PlainMark);
                sdf.Cone(r.Neck, new Vector3(0f, eyeY - 0.07f, front - 0.15f), new Vector3(0f, 1.43f, -0.01f), 0.085f, 0.15f, hood, 0.03f, new Vector3(1f, 1f, 0.8f), PlainMark);
                sdf.CarveEllipsoid(r.Head, new Vector3(0f, eyeY - 0.03f, front + 0.02f), new Vector3(0.074f, 0.105f, 0.065f), Vector3.zero, 0.012f);
                sdf.Cone(r.Head, new Vector3(0f, eyeY + 0.07f, front - 0.19f), new Vector3(0f, eyeY - 0.02f, front - 0.33f), 0.045f, 0.012f, hood, 0.03f, null, PlainMark); // the hood's tail
            }
            else
            {
                // Hair: a cap over the skull, longer at the back, the forehead and the face left bare
                sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY + 0.026f, front - 0.118f), new Vector3(0.093f, 0.097f, 0.118f), new Vector3(-12f, 0f, 0f), hair, 0.012f, HairMark);
                sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.02f, front - 0.155f), new Vector3(0.084f, 0.075f, 0.08f), Vector3.zero, hair, 0.02f, HairMark);
            }
            if (style.Beard)
            {
                // A short full beard round the jaw and chin, and a moustache
                // (hugging the jaw, a centimetre thick: not a muzzle)
                sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.074f, front - 0.06f), new Vector3(0.069f, 0.05f, 0.07f), Vector3.zero, skin, 0.012f, PlainMark);
                sdf.Ellipsoid(r.Head, new Vector3(0f, eyeY - 0.1f, front - 0.03f), new Vector3(0.035f, 0.03f, 0.032f), Vector3.zero, skin, 0.012f, PlainMark);
                sdf.Cone(r.Head, new Vector3(-0.026f, eyeY - 0.067f, front + 0.005f), new Vector3(0.026f, eyeY - 0.067f, front + 0.005f), 0.0055f, 0.0055f, skin, 0.005f, null, PlainMark);
                sdf.CarveEllipsoid(r.Head, new Vector3(0f, eyeY - 0.08f, front + 0.008f), new Vector3(0.02f, 0.012f, 0.012f), Vector3.zero, 0.004f);
            }
            sdf.SurfaceBrush = SkinS;
        }

        /// <summary>
        /// The face's small parts, crisp (they are smaller than the sculpt's cells): eyeballs with iris and pupil, upper and lower
        /// lids meeting at the corners (an almond, the iris touching both: a calm look, not a stare), brows on the brow ridge,
        /// and the line of the mouth along the jaw's curve.
        /// </summary>
        private static MeshKit Face(Rig r, float eyeY, float front, bool near)
        {
            var kit = new MeshKit();
            Color skin = style.Skin, brow = Color.Lerp(style.Hair, style.Skin, 0.15f);
            const float R = 0.0105f;
            int seg = near ? 20 : 6;
            for (int s = 0; s < 2; s++)
            {
                Vector3 eye = s == 0 ? r.EyeL : r.EyeR;
                float side = s == 0 ? -1f : 1f;
                Vector3 c = eye + new Vector3(0f, 0f, -R);
                // Eyeball round the forward axis, iris and pupil painted on it (rows doubled at their edges: crisp)
                float[] rows = near ? new[] { 0f, 9f, 14f, 14f, 25f, 31f, 31f, 50f, 75f, 100f, 140f, 180f } : new[] { 0f, 31f, 31f, 90f, 180f };
                var prof = new Vector2[rows.Length];
                for (int i = 0; i < prof.Length; i++) prof[i] = new Vector2(R * Mathf.Sin(rows[i] * Mathf.Deg2Rad), R * Mathf.Cos(rows[i] * Mathf.Deg2Rad));
                Color Ball(int i)
                {
                    bool second = i > 0 && rows[i] == rows[i - 1];
                    float a = rows[i];
                    if (a < 14f || (a == 14f && !second)) return near ? new Color(0.06f, 0.05f, 0.05f) : new Color(0.16f, 0.11f, 0.08f);
                    if (a < 31f || (a == 31f && !second)) return new Color(0.27f, 0.19f, 0.12f);
                    return new Color(0.86f, 0.83f, 0.78f);
                }
                kit.Lathe(Matrix4x4.TRS(c, Quaternion.FromToRotation(Vector3.up, Vector3.forward), Vector3.one), prof, seg, new Vector3(5f, 0f, 0.6f), (i, a) => Ball(i));

                // Lids: shells over the ball, their edges an almond (high at the middle, meeting at the corners)
                Lid(kit, c, R * 1.13f, true, 23f, skin, near);
                Lid(kit, c, R * 1.1f, false, 24f, skin, near);

                // Brow on the ridge, its outer end back with the head's curve and a little lower
                Matrix4x4 m = Matrix4x4.TRS(eye + new Vector3(side * 0.003f, 0.019f, -0.002f), Quaternion.Euler(0f, side * 18f, side * 3f), Vector3.one);
                kit.Box(m, new Vector3(0.029f, 0.0058f, 0.005f), 0.0022f, new Vector3(0f, 0f, 0.15f), _ => brow);
            }
            if (!near) return kit;
            // The mouth: a thin dark line round the jaw (whose front is at front + 0.006 there), the corners a touch lower
            int n = 11;
            var line = new Vector3[n, 6];
            for (int i = 0; i < n; i++)
            {
                float x = Mathf.Lerp(-0.021f, 0.021f, i / (float)(n - 1));
                float z = front - 0.06f + 0.066f * Mathf.Sqrt(Mathf.Max(0f, 1f - Sq(x / 0.064f))) - 0.0008f;
                Vector3 at = new Vector3(x, eyeY - 0.0735f - 0.0025f * Sq(x / 0.021f), z);
                float t = 1f - Sq(x / 0.021f) * 0.6f;
                for (int j = 0; j < 6; j++)
                {
                    float a = j * Mathf.PI * 2f / 6f;
                    line[i, j] = at + new Vector3(0f, Mathf.Cos(a) * 0.0014f * t, Mathf.Sin(a) * 0.0018f);
                }
            }
            kit.Grid(line, true, new Vector3(0f, 0f, 0.2f), (i, j) => new Color(0.30f, 0.15f, 0.12f));
            return kit;
        }

        /// <summary>An eyelid: a shell round the eyeball from its top (or bottom) down to an edge that is open most at the front.</summary>
        private static void Lid(MeshKit kit, Vector3 c, float radius, bool upper, float open, Color skin, bool near)
        {
            int rows = near ? 7 : 3, cols = near ? 24 : 8;
            var p = new Vector3[rows + 1, cols + 1];
            Vector3 axis = upper ? Vector3.up : Vector3.down;
            for (int j = 0; j <= cols; j++)
            {
                float phi = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, j / (float)cols); // round the front, corner to corner
                float ahead = Mathf.Max(0f, Mathf.Cos(phi));                                               // 1 at the front, 0 at the corners
                float edge = (90f - open * Mathf.Pow(ahead, 0.8f)) * Mathf.Deg2Rad;         // from the axis down to the lid's edge
                for (int i = 0; i <= rows; i++)
                {
                    float th = edge * i / rows;
                    float rr = i == rows ? radius * 0.97f : radius; // the edge turns in a little: the lid has a rim
                    Vector3 dir = axis * Mathf.Cos(th) + (Vector3.forward * Mathf.Cos(phi) + Vector3.right * Mathf.Sin(phi)) * Mathf.Sin(th);
                    p[i, j] = c + dir * rr;
                }
            }
            kit.Grid(p, false, SkinS, (i, j) => i == rows ? skin * 0.8f : skin, axis + Vector3.forward);
        }

        // ------------------------------------------------------------------ tunic and belt

        private static void Body(SdfCreature sdf, Rig r)
        {
            Color tunic = style.Tunic;
            float z = 0.03f, pad = style.Gambeson ? 0.014f : 0f;
            sdf.SurfaceBrush = ClothS;
            sdf.Ellipsoid(r.Torso, new Vector3(0f, 1.31f, z + 0.005f), new Vector3(0.178f + pad, 0.165f, 0.108f + pad), Vector3.zero, tunic, 0.04f, TunicMark);
            sdf.Ellipsoid(r.Torso, new Vector3(0f, 1.425f, z - 0.01f), new Vector3(0.21f + pad, 0.06f, 0.095f + pad), Vector3.zero, tunic, 0.04f, TunicMark); // shoulders
            sdf.Ellipsoid(r.Abdomen, new Vector3(0f, 1.1f, z - 0.004f), new Vector3(0.165f + pad, 0.15f, 0.096f + pad), Vector3.zero, tunic, 0.04f, TunicMark);
            sdf.Ellipsoid(r.Hips, new Vector3(0f, 0.9f, z), new Vector3(0.165f + pad, 0.11f, 0.102f + pad), Vector3.zero, tunic, 0.035f, TunicMark);
            // A straight trunk from the chest to the belt: no waist (a man's shape, and armour fitted to it is no corset)
            sdf.Cone(r.Abdomen, new Vector3(0f, 1.3f, z), new Vector3(0f, 0.93f, z), 0.172f + pad, 0.166f + pad, tunic, 0.04f, new Vector3(1f, 1f, 0.62f), TunicMark);
            for (int s = 0; s < 2; s++)
                if (r.Shoulder[s] != null) // trapezius under the tunic: the shoulder bones get their share of the skin
                    sdf.Ellipsoid(r.Shoulder[s], new Vector3((s == 0 ? -1f : 1f) * 0.1f, 1.445f, z - 0.015f), new Vector3(0.075f, 0.035f, 0.07f), Vector3.zero, tunic, 0.03f, TunicMark);
            if (style.Gambeson) // a standing quilted collar
                sdf.Cone(r.Neck, new Vector3(0f, 1.44f, z - 0.02f), new Vector3(0f, 1.52f, z - 0.015f), 0.085f, 0.07f, tunic, 0.02f, new Vector3(1f, 1f, 0.9f), TunicMark);
            // Skirt to the hem, split over the legs (it follows each thigh)
            sdf.ClipBelow = style.Hem;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -1f : 1f;
                sdf.Cone(r.UpperLeg[s], new Vector3(x * 0.07f, 0.86f, z), new Vector3(x * 0.105f, style.Hem - 0.03f, z + 0.025f), 0.125f + pad, 0.142f + pad, tunic, 0.03f, new Vector3(1f, 1f, 0.92f), TunicMark);
            }
            // and a panel between them front and back (no gap between the legs at the hem)
            sdf.Ellipsoid(r.Hips, new Vector3(0f, style.Hem + 0.12f, z + 0.005f), new Vector3(0.115f + pad, 0.2f, 0.1f + pad), Vector3.zero, tunic, 0.03f, TunicMark);
            sdf.ClipBelow = null;
            // Belt, a pouch at the hip
            sdf.SurfaceBrush = LeatherS;
            sdf.Ellipsoid(r.Hips, new Vector3(0f, 0.93f, z + 0.001f), new Vector3(0.169f + pad, 0.017f, 0.106f + pad), Vector3.zero, style.Belt, 0.004f, PlainMark);
            sdf.Ellipsoid(r.Hips, new Vector3(0.13f + pad, 0.87f, z + 0.06f), new Vector3(0.035f, 0.045f, 0.022f), new Vector3(0f, 35f, 0f), style.Belt, 0.008f, PlainMark);
            sdf.SurfaceBrush = ClothS;
        }

        // ------------------------------------------------------------------ arms and fists

        private static void Arms(SdfCreature sdf, Rig r)
        {
            Color tunic = style.Tunic, skin = style.Skin;
            float pad = style.Gambeson ? 0.01f : 0f;
            sdf.ShapeGroup = 1; // apart from the body they hang against
            for (int s = 0; s < 2; s++)
            {
                Vector3 sh = r.ShoulderAt[s], el = r.Elbow[s], wr = r.Wrist[s];
                sdf.SurfaceBrush = ClothS;
                sdf.Ellipsoid(r.UpperArm[s], sh + Vector3.down * 0.015f, new Vector3(0.064f, 0.06f, 0.062f) + Vector3.one * pad, Vector3.zero, tunic, 0.02f, TunicMark);
                sdf.Cone(r.UpperArm[s], sh + Vector3.down * 0.02f, el, 0.058f + pad, 0.046f + pad, tunic, 0.02f, null, TunicMark);
                if (style.Gambeson)
                {
                    sdf.Ellipsoid(r.LowerArm[s], el, Vector3.one * (0.048f + pad), Vector3.zero, tunic, 0.015f, TunicMark);
                    sdf.Cone(r.LowerArm[s], el, Vector3.Lerp(el, wr, 0.92f), 0.046f + pad, 0.038f + pad, tunic, 0.015f, null, TunicMark);
                    sdf.SurfaceBrush = SkinS;
                    sdf.Cone(r.LowerArm[s], Vector3.Lerp(el, wr, 0.8f), wr, 0.03f, 0.028f, skin, 0.01f, null, PlainMark);
                }
                else
                {
                    // Sleeves rolled up over the elbow, bare forearms
                    sdf.Ellipsoid(r.LowerArm[s], Vector3.Lerp(el, wr, 0.08f), new Vector3(0.052f, 0.03f, 0.052f), Vector3.zero, tunic * 0.92f, 0.01f, TunicMark);
                    sdf.SurfaceBrush = SkinS;
                    sdf.Cone(r.LowerArm[s], el, wr, 0.04f, 0.029f, skin, 0.015f, new Vector3(1f, 1f, 0.85f), PlainMark);
                }
                Fist(sdf, r, s);
            }
            sdf.ShapeGroup = 0;
        }

        /// <summary>A closed hand on the palm bone, the fingers curled round a grip through the fist (the line a held tool takes).</summary>
        private static void Fist(SdfCreature sdf, Rig r, int s)
        {
            Color skin = style.Skin;
            Vector3 w = r.Wrist[s], k = r.Knuckles[s], g = r.Grip[s];
            Vector3 down = (k - w).normalized;
            Vector3 outSide = new Vector3(s == 0 ? -1f : 1f, 0f, 0f);
            sdf.SurfaceBrush = SkinS;
            sdf.Cone(r.Palm[s], w, g, 0.027f, 0.031f, skin, 0.01f, new Vector3(0.7f, 1f, 1f), PlainMark); // back of the hand and palm
            sdf.Cone(r.Palm[s], g + Vector3.forward * 0.036f + down * 0.012f + outSide * 0.004f, g - Vector3.forward * 0.03f + down * 0.012f + outSide * 0.004f, 0.026f, 0.023f, skin, 0.009f, null, PlainMark); // curled fingers
            sdf.Ellipsoid(r.Palm[s], g + outSide * 0.014f + down * 0.004f, new Vector3(0.016f, 0.026f, 0.04f), Vector3.zero, skin, 0.007f, PlainMark); // knuckles
            sdf.Cone(r.Palm[s], w + down * 0.025f - outSide * 0.016f + Vector3.forward * 0.024f, g - outSide * 0.024f + Vector3.forward * 0.03f, 0.016f, 0.013f, skin, 0.008f, null, PlainMark); // thumb over the fingers
        }

        // ------------------------------------------------------------------ hose, wraps and shoes

        private static void Legs(SdfCreature sdf, Rig r)
        {
            sdf.ShapeGroup = 2;
            for (int s = 0; s < 2; s++)
            {
                Vector3 hip = r.Hip[s], knee = r.Knee[s], a = r.Ankle[s];
                Vector3 ankleTop = new Vector3(a.x, 0.095f, a.z + 0.035f);
                sdf.SurfaceBrush = ClothS;
                sdf.Cone(r.UpperLeg[s], hip, knee, 0.072f, 0.054f, style.Hose, 0.02f, null, HoseMark); // slim: under the skirt, must not poke through it
                sdf.Ellipsoid(r.LowerLeg[s], knee, Vector3.one * 0.054f, Vector3.zero, style.Hose, 0.015f, HoseMark);
                sdf.Cone(r.LowerLeg[s], knee, Vector3.Lerp(knee, ankleTop, 0.3f), 0.053f, 0.052f, style.Hose, 0.015f, null, HoseMark); // calf
                sdf.Cone(r.LowerLeg[s], Vector3.Lerp(knee, ankleTop, 0.3f), ankleTop, 0.052f, 0.038f, style.Hose, 0.015f, null, HoseMark);

                // Leather turnshoe: foot, rounded toe, heel, a flat sole
                sdf.SurfaceBrush = LeatherS;
                sdf.ClipBelow = 0f;
                float x = s == 0 ? -1f : 1f;
                sdf.Ellipsoid(r.Foot[s], new Vector3(a.x, 0.048f, a.z + 0.04f), new Vector3(0.046f, 0.048f, 0.095f), Vector3.zero, style.Shoes, 0.02f, PlainMark);
                sdf.Cone(r.Foot[s], new Vector3(a.x, 0.04f, a.z + 0.06f), new Vector3(a.x + x * 0.006f, 0.03f, a.z + 0.155f), 0.038f, 0.028f, style.Shoes, 0.02f, new Vector3(1.2f, 1f, 1f), PlainMark);
                sdf.Ellipsoid(r.Foot[s], new Vector3(a.x, 0.047f, a.z - 0.035f), new Vector3(0.04f, 0.047f, 0.042f), Vector3.zero, style.Shoes, 0.015f, PlainMark);
                sdf.Cone(r.Foot[s], new Vector3(a.x, 0.06f, a.z + 0.0f), ankleTop + Vector3.up * 0.02f, 0.042f, 0.042f, style.Shoes, 0.015f, null, PlainMark); // the shoe's collar round the ankle
                sdf.ClipBelow = null;
            }
            sdf.SurfaceBrush = ClothS;
            sdf.ShapeGroup = 0;
        }

        // ------------------------------------------------------------------ paint

        private static float Sq(float v) => v * v;

        private static Color Paint(Vector3 p, Vector3 n, Color c, float paintable)
        {
            float grime = 0.94f + 0.1f * SdfCreature.Noise(p * 15f);
            if (paintable < 0.45f && !(paintable > 0.1f && paintable < 0.2f) && p.y > eyeLine - 0.14f && p.y < eyeLine + 0.15f)
            {
                // Hair and beard are painted by where they grow (soft edges), not by the shapes' colours (blocky patches)
                float y = p.y - eyeLine, dz = p.z - (faceFront - 0.105f), ax = Mathf.Abs(p.x);
                Color strands = style.Hair * (0.85f + 0.3f * SdfCreature.Noise(new Vector3(p.x * 300f, p.y * 40f, p.z * 300f)));
                if (!style.Hood)
                {
                    // The hairline: over the forehead, above the ears, low on the nape
                    float line = Mathf.Lerp(-0.075f, 0.036f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.05f, 0.085f, dz)));
                    line += 0.003f * SdfCreature.Noise(p * 40f);
                    float hairy = Mathf.Clamp01((y - line) / 0.01f + 0.5f);
                    float groove = 0.5f + 0.5f * Mathf.Sin(Mathf.Atan2(p.x, dz + 0.02f) * 14f + SdfCreature.Noise(p * 25f) * 1.5f);
                    if (hairy > 0f) c = Color.Lerp(c, strands * (0.82f + 0.22f * groove), hairy);
                }
                if (style.Beard && dz > -0.035f && y < 0.005f)
                {
                    // Sideburns down into a full short beard: below a line from the ear's front across the cheek to under the
                    // nose; the lips bare, a moustache over them
                    float top = Mathf.Lerp(0f, -0.048f, Mathf.InverseLerp(0f, 0.08f, dz));
                    if (ax < 0.03f && dz > 0.07f) top = -0.056f; // the moustache under the nose
                    float beard = Mathf.Clamp01((top - y) / 0.01f + 0.5f) * Mathf.Clamp01((dz + 0.035f) / 0.012f) * Mathf.Clamp01((y + 0.132f) / 0.012f) * style.BeardShade;
                    if (beard > 0f) c = Color.Lerp(c, strands, beard);
                }
                return c * grime;
            }
            if (paintable > 0.8f) // the tunic
            {
                if (style.Gambeson)
                {
                    // Quilting: stitched seams down and round the padding
                    float seam = Mathf.Min(Mathf.Abs(Mathf.Repeat(Mathf.Atan2(p.x, p.z - 0.03f) * 6f / Mathf.PI, 1f) - 0.5f), Mathf.Abs(Mathf.Repeat(p.y * 14f, 1f) - 0.5f));
                    c *= Mathf.Lerp(0.86f, 1f, Mathf.Clamp01((0.5f - seam) * 10f - 3.6f));
                }
                else
                {
                    // The neckline: a slit down the front with its lacing
                    float slit = Mathf.Clamp01((0.012f - Mathf.Abs(p.x)) / 0.003f) * Mathf.Clamp01((p.y - 1.36f) / 0.01f) * (p.z > 0.05f ? 1f : 0f);
                    c = Color.Lerp(c, c * 0.45f, slit);
                    if (p.y > 1.36f && p.z > 0.05f && Mathf.Abs(p.x) < 0.02f && Mathf.Repeat(p.y * 70f, 1f) < 0.2f) c = style.Belt * 1.2f;
                }
                // Hems: a darker band at the skirt's edge and round the neck; folds down the skirt
                c *= Mathf.Lerp(0.78f, 1f, Mathf.Clamp01((p.y - style.Hem - 0.02f) / 0.004f));
                if (p.y < 0.86f)
                {
                    float around = Mathf.Atan2(p.z - 0.03f, p.x);
                    c *= Mathf.Lerp(1f, 0.84f + 0.16f * (0.5f + 0.5f * Mathf.Sin(around * 9f + SdfCreature.Noise(p * 8f) * 2f)), Mathf.InverseLerp(0.86f, style.Hem, p.y));
                }
                if (n.y < -0.6f) c *= 0.4f;
                return c * grime;
            }
            if (paintable > 0.4f) // hose: the wraps wound round the shins
            {
                if (p.y < 0.4f && p.y > 0.1f)
                {
                    float band = Mathf.Repeat(p.y * 24f + Mathf.Atan2(p.z - 0.05f, p.x - Mathf.Sign(p.x) * 0.15f) * 0.5f, 1f);
                    c = Color.Lerp(style.Wraps, style.Wraps * 0.7f, Mathf.Clamp01((band - 0.82f) / 0.05f));
                }
                return c * grime;
            }
            return c * grime;
        }
    }
}
