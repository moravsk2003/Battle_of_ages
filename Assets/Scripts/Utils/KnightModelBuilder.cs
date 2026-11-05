using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Knight guard on the rigged knight skeleton (animations, foot IK and sword clips unchanged), one skinned mesh and
    /// one material with the surface per vertex. The soft body is sculpted (SdfCreature, skinned to the rig bones in
    /// the rest pose): mail hauberk, coif and chausses, a red surcoat with a gold cross falling to a split skirt, a
    /// leather belt, steel fists and sabatons. The plate is hard-surface (MeshKit, rigid on its bone): a sugarloaf
    /// great helm with eye slits, brass cross and rivets, laminated pauldrons, couters, vambraces with gauntlet cuffs,
    /// poleyns and greaves, an arming sword and a painted heater shield. A distant mesh (coarser sculpt, fewer
    /// segments) swapped into the one renderer by SkinnedMeshLod keeps big battles at the old cost per knight.
    /// </summary>
    public static class KnightModelBuilder
    {
        private const float NearCell = 0.013f, FarCell = 0.04f, NearScreenHeight = 0.25f, CullScreenHeight = 0.006f;

        private static readonly Color Surcoat = new Color(0.55f, 0.09f, 0.08f);
        private static readonly Color Gold = new Color(0.86f, 0.67f, 0.25f);
        private static readonly Color Mail = new Color(0.50f, 0.51f, 0.54f);
        private static readonly Color Steel = new Color(0.76f, 0.78f, 0.82f);
        private static readonly Color Leather = new Color(0.30f, 0.18f, 0.10f);
        private static readonly Color DarkLeather = new Color(0.16f, 0.10f, 0.06f);
        private static readonly Color Brass = new Color(0.84f, 0.64f, 0.27f);
        private static readonly Color Wood = new Color(0.45f, 0.31f, 0.18f);
        private static readonly Color Iron = new Color(0.33f, 0.33f, 0.35f);
        private static readonly Color ShieldField = new Color(0.60f, 0.10f, 0.09f);
        private static readonly Color Slit = new Color(0.03f, 0.03f, 0.035f);

        /// <summary>What sets one armoured rank apart: the surcoat and shield colours, the trim, and the general's cape, crown and plume.</summary>
        public sealed class Style
        {
            public string Key;
            public Color Surcoat, Trim, Field;
            public Color Cape, Plume; // clear: none
            public bool Crown;
        }

        public static readonly Style KnightStyle = new Style { Key = "Knight", Surcoat = Surcoat, Trim = Gold, Field = ShieldField };

        /// <summary>The general (§11.1): the knight's plate under a royal purple surcoat (his rank's colour on the map), a crimson cape, a gold crown round the helm and a crest of plumes, white at the root and red at the tips.</summary>
        public static readonly Style GeneralStyle = new Style
        {
            Key = "General", Surcoat = new Color(0.30f, 0.09f, 0.40f), Trim = Gold, Field = new Color(0.33f, 0.10f, 0.45f),
            Cape = new Color(0.46f, 0.06f, 0.07f), Plume = new Color(0.93f, 0.92f, 0.88f), Crown = true
        };

        private static Style style = KnightStyle; // the one being built (Paint reads it)

        // Surface = (CharacterSurface pattern, metallic, smoothness)
        private static readonly Vector3 ClothS = new Vector3(1f, 0f, 0.18f);
        private static readonly Vector3 MailS = new Vector3(7f, 0.8f, 0.45f);
        private static readonly Vector3 SteelS = new Vector3(3f, 0.9f, 0.7f);
        private static readonly Vector3 LeatherS = new Vector3(2f, 0f, 0.35f);
        private static readonly Vector3 BrassS = new Vector3(3f, 0.95f, 0.6f);
        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.22f);
        private static readonly Vector3 PaintS = new Vector3(4f, 0f, 0.3f); // paint over wood grain
        private static readonly Vector3 DarkS = new Vector3(1f, 0f, 0.05f);

        /// <summary>Rig bones and their rest positions in character (visual root) space.</summary>
        private sealed class Rig
        {
            public Transform Root, Hips, Abdomen, Torso, Neck, Head;
            public Transform[] UpperArm = new Transform[2], LowerArm = new Transform[2], Palm = new Transform[2];
            public Transform[] UpperLeg = new Transform[2], LowerLeg = new Transform[2], Foot = new Transform[2];
            public Vector3[] Shoulder = new Vector3[2], Elbow = new Vector3[2], Wrist = new Vector3[2], Knuckles = new Vector3[2];
            public Vector3[] Hip = new Vector3[2], Knee = new Vector3[2], Ankle = new Vector3[2];
            public Matrix4x4 SwordFrame, ShieldFrame, BowFrame; // character space

            public Vector3 P(Transform t) => Root.InverseTransformPoint(t.position);
        }

        /// <summary>
        /// Replaces the imported body of a freshly built (rest pose) knight rig; false if bones are missing. With a bow
        /// the sculpt holds it in the left hand and has no shield (a bow takes both hands): an armoured archer.
        /// </summary>
        public static bool Build(GameObject root, Transform model, Style look = null, bool bow = false)
        {
            style = look ?? KnightStyle;
            string Key = style.Key + (bow ? "Bow" : "");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var sdf = new SdfCreature(root.transform)
            {
                RootSpaceCoordinates = true, VertexSurfaces = true,
                AmbientOcclusion = 0.45f, OcclusionScale = 0.6f, SkinSigma = 0.035f, Paint = Paint
            };
            // Later knights reuse the meshes: declaring (the hard-surface kits especially) is skipped.
            SkinnedMeshRenderer[] skins = sdf.ApplyCachedWithLod(Key, NearScreenHeight, CullScreenHeight, oneRenderer: true);
            if (skins == null)
            {
                Rig rig = FindRig(root.transform, model);
                if (rig == null) return false;
                Body(sdf, rig);
                Hands(sdf, rig);
                Plate(sdf, rig, bow);
                skins = sdf.ApplyWithLod(Key, NearCell, FarCell, NearScreenHeight, CullScreenHeight, oneRenderer: true);
                Debug.Log($"[{Key}] declared and built in {timer.ElapsedMilliseconds} ms");
            }

            // The imported body goes; the new skins live under the Animator so its culling sees them.
            foreach (SkinnedMeshRenderer old in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                old.gameObject.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(old.gameObject); else UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            foreach (SkinnedMeshRenderer skin in skins)
            {
                Bounds b = skin.localBounds;
                b.Expand(0.6f); // sword swings
                skin.localBounds = b;
                skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                skin.updateWhenOffscreen = false;
                skin.transform.SetParent(model, true);
            }
            SkinnedMeshLod lod = root.GetComponent<SkinnedMeshLod>();
            if (lod != null) lod.Apply();
            return true;
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
                r.UpperArm[s] = F("UpperArm" + x); r.LowerArm[s] = F("LowerArm" + x); r.Palm[s] = F("Palm" + x);
                r.UpperLeg[s] = F("UpperLeg" + x); r.LowerLeg[s] = F("LowerLeg" + x); r.Foot[s] = F("Foot" + x);
                Transform knuckles = F("MiddleHand" + x);
                if (r.UpperArm[s] == null || r.LowerArm[s] == null || r.Palm[s] == null || knuckles == null ||
                    r.UpperLeg[s] == null || r.LowerLeg[s] == null || r.Foot[s] == null) return null;
                r.Shoulder[s] = r.P(r.UpperArm[s]); r.Elbow[s] = r.P(r.LowerArm[s]); r.Wrist[s] = r.P(r.Palm[s]); r.Knuckles[s] = r.P(knuckles);
                r.Hip[s] = r.P(r.UpperLeg[s]); r.Knee[s] = r.P(r.LowerLeg[s]); r.Ankle[s] = r.P(r.Foot[s]);
            }

            // The same attachment frames the old primitive gear used (sword clips expect the blade along palm -X).
            Matrix4x4 Frame(Transform bone, Vector3 euler, Vector3? position)
            {
                var t = new GameObject("Frame").transform;
                t.SetParent(bone, false);
                t.localRotation = Quaternion.Euler(euler);
                if (position.HasValue) t.position = position.Value;
                Matrix4x4 m = root.worldToLocalMatrix * Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
                if (Application.isPlaying) UnityEngine.Object.Destroy(t.gameObject); else UnityEngine.Object.DestroyImmediate(t.gameObject);
                return m;
            }
            // Sword: grip in the middle of the fist, not at the wrist joint
            Vector3 fistR = Vector3.Lerp(r.Wrist[1], r.Knuckles[1], 0.75f);
            r.SwordFrame = Frame(r.Palm[1], RiggedCharacterBuilder.HandWeaponEuler, root.TransformPoint(fistR));
            // Shield: face normal as the old gear (out from the forearm), the point towards the hand
            Vector3 up = Frame(r.LowerArm[0], RiggedCharacterBuilder.ShieldEuler, null).MultiplyVector(Vector3.up).normalized;
            Vector3 down = Vector3.ProjectOnPlane(r.Wrist[0] - r.Elbow[0], up).normalized;
            // Off the forearm and shifted away from the body, so the shield clears the surcoat when the arm hangs
            Vector3 away = Vector3.ProjectOnPlane(Vector3.left, up).normalized;
            // Bow: the grip through the left fist along the palm matrix's X (turned to point forward), the limbs across it
            // and the palm's Z, as GearMeshBuilder builds a worn bow and the Shoot clip aims it
            Vector3 bx = root.InverseTransformDirection(((Vector3)r.Palm[0].localToWorldMatrix.GetColumn(0)).normalized);
            Vector3 bz = root.InverseTransformDirection(((Vector3)r.Palm[0].localToWorldMatrix.GetColumn(2)).normalized);
            Vector3 grip = Vector3.Dot(-bx, Vector3.forward) >= 0f ? -bx : bx;
            r.BowFrame = Matrix4x4.TRS(Vector3.Lerp(r.Wrist[0], r.Knuckles[0], 0.75f), Quaternion.LookRotation(Vector3.ProjectOnPlane(bz, grip).normalized, grip), Vector3.one);
            r.ShieldFrame = Matrix4x4.TRS(Vector3.Lerp(r.Elbow[0], r.Wrist[0], 0.45f) + up * 0.075f + away * 0.06f, Quaternion.LookRotation(-down, up), Vector3.one);
            return r;
        }

        // ------------------------------------------------------------------ sculpted body

        private static void Body(SdfCreature sdf, Rig r)
        {
            float back = -0.025f; // body centre line (rest pose leans the spine slightly back)

            // ---- surcoat over the torso: chest, waist, hips; split skirt cut flat at the hem (paintable = cross)
            sdf.SurfaceBrush = ClothS;
            sdf.Ellipsoid(r.Torso, new Vector3(0f, 1.065f, back + 0.005f), new Vector3(0.195f, 0.185f, 0.134f), Vector3.zero, style.Surcoat, 0.04f);
            sdf.Ellipsoid(r.Abdomen, new Vector3(0f, 0.875f, back + 0.003f), new Vector3(0.182f, 0.16f, 0.124f), Vector3.zero, style.Surcoat, 0.04f);
            sdf.Ellipsoid(r.Hips, new Vector3(0f, 0.70f, back), new Vector3(0.212f, 0.12f, 0.134f), Vector3.zero, style.Surcoat, 0.035f);
            sdf.ClipBelow = 0.335f;
            for (int s = 0; s < 2; s++)
            {
                float x = s == 0 ? -1f : 1f;
                sdf.Cone(r.UpperLeg[s], new Vector3(x * 0.085f, 0.64f, back), new Vector3(x * 0.125f, 0.30f, back + 0.005f), 0.152f, 0.18f, style.Surcoat, 0.03f, new Vector3(1f, 1f, 0.8f));
            }
            sdf.ClipBelow = null;

            // ---- leather belt
            sdf.SurfaceBrush = LeatherS;
            sdf.Ellipsoid(r.Hips, new Vector3(0f, 0.757f, back + 0.002f), new Vector3(0.199f, 0.019f, 0.135f), Vector3.zero, Leather, 0.005f, 0f);

            // ---- mail: shoulder cape and coif rising into the helm, sleeves, chausses
            sdf.SurfaceBrush = MailS;
            // Shoulder cape (tippet) over the top of the surcoat, and the aventail as a bell from it up into the helm rim:
            // the helm sits on a sloping mail collar instead of a neck
            sdf.Ellipsoid(r.Torso, new Vector3(0f, 1.228f, back - 0.008f), new Vector3(0.24f, 0.088f, 0.13f), Vector3.zero, Mail, 0.045f, 0f);
            sdf.Cone(r.Neck, new Vector3(0f, 1.25f, back - 0.005f), new Vector3(0f, 1.33f, -0.003f), 0.13f, 0.108f, Mail, 0.04f, new Vector3(1f, 1f, 0.88f), 0f);
            for (int s = 0; s < 2; s++)
            {
                sdf.ShapeGroup = 1; // arms and legs are polygonized apart from the surcoat they hang against
                sdf.Cone(r.UpperArm[s], r.Shoulder[s] + Vector3.down * 0.01f, r.Elbow[s], 0.062f, 0.051f, Mail, 0.02f, null, 0f);
                sdf.Ellipsoid(r.LowerArm[s], r.Elbow[s], Vector3.one * 0.05f, Vector3.zero, Mail, 0.015f, 0f);
                sdf.Cone(r.LowerArm[s], r.Elbow[s], r.Wrist[s], 0.048f, 0.039f, Mail, 0.015f, null, 0f);
                sdf.ShapeGroup = 2;
                sdf.Cone(r.UpperLeg[s], r.Hip[s], r.Knee[s], 0.06f, 0.06f, Mail, 0.02f, null, 0f); // slim: hidden under the skirt, must not poke through it
                sdf.Ellipsoid(r.LowerLeg[s], r.Knee[s], Vector3.one * 0.062f, Vector3.zero, Mail, 0.015f, 0f);
                Vector3 ankleTop = new Vector3(r.Ankle[s].x, 0.075f, r.Ankle[s].z + 0.012f);
                sdf.Cone(r.LowerLeg[s], r.Knee[s], ankleTop, 0.058f, 0.044f, Mail, 0.015f, null, 0f);

                // ---- steel sabaton: foot, toe and heel, flat sole, lames painted across
                sdf.SurfaceBrush = SteelS;
                sdf.ClipBelow = 0f;
                Vector3 a = r.Ankle[s];
                sdf.Ellipsoid(r.Foot[s], new Vector3(a.x, 0.05f, a.z + 0.035f), new Vector3(0.05f, 0.05f, 0.1f), Vector3.zero, Steel, 0.02f, 0f);
                sdf.Cone(r.Foot[s], new Vector3(a.x, 0.04f, a.z + 0.06f), new Vector3(a.x + (s == 0 ? -0.006f : 0.006f), 0.028f, a.z + 0.165f), 0.04f, 0.026f, Steel, 0.02f, new Vector3(1.2f, 1f, 1f), 0f);
                sdf.Ellipsoid(r.Foot[s], new Vector3(a.x, 0.045f, a.z - 0.04f), new Vector3(0.042f, 0.045f, 0.045f), Vector3.zero, Leather, 0.015f, 0f);
                sdf.ClipBelow = null;
                sdf.SurfaceBrush = MailS;
                sdf.ShapeGroup = 0;
            }
        }

        /// <summary>Steel-plated fists, rigid on the palm bones (the right one closed round the sword grip).</summary>
        private static void Hands(SdfCreature sdf, Rig r)
        {
            sdf.SurfaceBrush = SteelS;
            sdf.ShapeGroup = 1;
            for (int s = 0; s < 2; s++)
            {
                Vector3 w = r.Wrist[s], k = r.Knuckles[s];
                Vector3 down = (k - w).normalized;
                Vector3 fist = Vector3.Lerp(w, k, 0.75f);
                Vector3 outSide = new Vector3(s == 0 ? -1f : 1f, 0f, 0f);
                // Back of the hand, palm and the curled fingers as a barrel round the grip (along Z in the rest pose)
                sdf.Cone(r.Palm[s], w, fist, 0.036f, 0.04f, Steel, 0.012f, new Vector3(0.75f, 1f, 1f), 0f);
                sdf.Cone(r.Palm[s], fist + Vector3.forward * 0.042f + down * 0.012f, fist - Vector3.forward * 0.042f + down * 0.012f, 0.034f, 0.031f, Steel, 0.01f, null, 0f);
                sdf.Ellipsoid(r.Palm[s], fist + outSide * 0.018f + Vector3.forward * 0.004f, new Vector3(0.02f, 0.03f, 0.045f), Vector3.zero, Steel, 0.008f, 0f); // knuckle plate
                // Thumb over the fingers on the inner side, leather palm showing under it
                sdf.Cone(r.Palm[s], w + down * 0.03f - outSide * 0.02f + Vector3.forward * 0.03f, fist - outSide * 0.03f + Vector3.forward * 0.035f, 0.02f, 0.017f, Leather, 0.01f, null, 0f);
            }
            sdf.ShapeGroup = 0;
        }

        private static Color Paint(Vector3 p, Vector3 n, Color c, float paintable)
        {
            float grime = 0.93f + 0.12f * SdfCreature.Noise(p * 17f);
            if (paintable > 0.5f)
            {
                // Gold cross front and back, gold hem band, folds down the skirt, dark lining underneath
                float Band(float v, float half) => Mathf.Clamp01((half - Mathf.Abs(v)) / 0.004f + 0.5f);
                float faces = Mathf.Max(Mathf.InverseLerp(0.0f, 0.05f, p.z), Mathf.InverseLerp(-0.07f, -0.12f, p.z));
                float upright = Band(p.x, 0.04f) * Mathf.Clamp01((1.19f - p.y) / 0.004f) * Mathf.Clamp01((p.y - 0.40f) / 0.004f);
                float arm = Band(p.y - 1.0f, 0.04f) * Band(p.x, 0.15f);
                c = Color.Lerp(c, style.Trim, Mathf.Max(upright, arm) * faces);
                c = Color.Lerp(c, style.Trim, Mathf.Clamp01((0.365f - p.y) / 0.004f) * Mathf.Clamp01((p.y - 0.343f) / 0.004f + 1f) * (n.y > -0.5f ? 1f : 0f));
                if (p.y < 0.64f)
                {
                    float around = Mathf.Atan2(p.z + 0.02f, p.x);
                    c *= Mathf.Lerp(1f, 0.82f + 0.18f * (0.5f + 0.5f * Mathf.Sin(around * 11f + SdfCreature.Noise(p * 9f) * 2f)), Mathf.InverseLerp(0.64f, 0.4f, p.y));
                }
                if (n.y < -0.6f) c *= 0.35f;
                return c * grime;
            }
            // Mail collar darkening up under the helm rim (the helm is hard-surface: the sculpt's occlusion misses it)
            if (p.y > 1.2f && Sq(p.x / 0.125f) + Sq((p.z + 0.004f) / 0.136f) < 1f) // only inside the helm rim, not the mantle front
                c *= Mathf.Lerp(1f, 0.45f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.23f, 1.3f, p.y)));
            // Sabaton lames: dark seams across the top of the foot
            if (p.y < 0.1f && n.y > 0.2f && Mathf.Repeat(p.z * 40f, 1f) < 0.12f && c.b > 0.6f) c *= 0.55f;
            return c * grime;
        }

        // ------------------------------------------------------------------ hard-surface plate and gear

        private static void Plate(SdfCreature sdf, Rig r, bool bow)
        {
            sdf.RigidMesh(r.Head, Helm(r, true), Helm(r, false));
            for (int s = 0; s < 2; s++)
            {
                sdf.RigidMesh(r.UpperArm[s], Pauldron(r, s, true), Pauldron(r, s, false));
                sdf.RigidMesh(r.LowerArm[s], Forearm(r, s, true), Forearm(r, s, false));
                sdf.RigidMesh(r.LowerLeg[s], Leg(r, s, true), Leg(r, s, false));
            }
            if (bow) sdf.RigidMesh(r.Palm[0], Bow(r.BowFrame, true), Bow(r.BowFrame, false));
            else
            {
                sdf.RigidMesh(r.Palm[1], Sword(r.SwordFrame, true), Sword(r.SwordFrame, false));
                sdf.RigidMesh(r.LowerArm[0], Shield(r.ShieldFrame, true), Shield(r.ShieldFrame, false));
            }
            sdf.RigidMesh(r.Hips, Buckle(new Vector3(0f, 0.757f, 0.113f)), null);
            if (style.Cape.a > 0f) sdf.RigidMesh(r.Torso, Cape(true), Cape(false));
        }

        /// <summary>
        /// A cape from under the mail tippet down the back to mid-thigh, flaring and falling in folds, clasped with gold at
        /// the shoulders; rigid on the torso (short enough for the legs not to swing through it). Lined darker inside.
        /// </summary>
        private static MeshKit Cape(bool near)
        {
            var kit = new MeshKit();
            int rows = near ? 14 : 4, cols = near ? 24 : 6;
            const float Top = 1.27f, Bottom = 0.56f, T = 0.008f;
            var outer = new Vector3[rows + 1, cols + 1];
            var inner = new Vector3[rows + 1, cols + 1];
            for (int i = 0; i <= rows; i++)
            {
                float t = i / (float)rows, y = Mathf.Lerp(Top, Bottom, t);
                float half = Mathf.Lerp(0.2f, 0.31f, t), depth = Mathf.Lerp(0.07f, 0.13f, t), centre = Mathf.Lerp(-0.105f, -0.16f, t);
                for (int j = 0; j <= cols; j++)
                {
                    float a = Mathf.Lerp(-1.25f, 1.25f, j / (float)cols);
                    float fold = 1f + (near ? 0.05f * t * Mathf.Sin(a * 7f + 0.6f) : 0f);
                    Vector3 p = new Vector3(Mathf.Sin(a) * half * fold, y, centre - Mathf.Cos(a) * depth * fold);
                    Vector3 n = new Vector3(Mathf.Sin(a) * depth, 0f, -Mathf.Cos(a) * half).normalized;
                    outer[i, j] = p + n * T;
                    inner[i, j] = p - n * T;
                }
            }
            Color cape = style.Cape, lining = style.Cape * 0.45f;
            kit.Grid(outer, false, ClothS, (i, j) => cape * (0.92f + 0.12f * SdfCreature.Noise(outer[i, j] * 14f)) * (i == rows ? 0.8f : 1f), Vector3.back);
            kit.Grid(inner, false, ClothS, (i, j) => lining, Vector3.forward);
            if (near)
                foreach (float x in new[] { -1f, 1f })
                    kit.Dome(new Vector3(x * 0.17f, Top + 0.01f, -0.05f), new Vector3(x * 0.3f, 0.4f, 1f).normalized, 0.022f, style.Trim, BrassS);
            return kit;
        }

        /// <summary>
        /// Sugarloaf great helm: oval drum with a keel down the face, tapering to a rounded point; eye slits either side
        /// of a brass upright, brass brow band, breaths on the right cheek, rivets round the rolled lower edge.
        /// </summary>
        private static MeshKit Helm(Rig r, bool near)
        {
            var kit = new MeshKit();
            Vector3 o = new Vector3(0f, 1.3f, 0.004f);
            const float H = 0.37f, Rx = 0.121f, Rz = 0.132f, Drum = 0.655f;
            float slitLo = 0.198f, slitHi = 0.212f, slitIn = 0.12f, slitOut = 0.95f;

            float Scale(float h)
            {
                float t = h / H;
                if (t <= Drum) return 1f + 0.025f * Mathf.Sin(Mathf.PI * t / Drum) + 0.04f * Sq(Mathf.Clamp01(1f - h / 0.07f)); // swell over the cheeks, flared lower edge
                float u = (t - Drum) / (1f - Drum);
                return Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.72f);
            }
            float Keel(float a, float h) => 1f + 0.055f * Mathf.Exp(-Sq(a / 0.22f)) * Mathf.Clamp01((H * 0.93f - h) / 0.03f);

            // Rows: denser at the slits (duplicated edges = crisp walls)
            var hs = new List<float>();
            int rowsLow = near ? 8 : 3, rowsHigh = near ? 8 : 2, rowsTop = near ? 12 : 4;
            for (int i = 0; i < rowsLow; i++) hs.Add(slitLo * i / rowsLow);
            hs.Add(slitLo); hs.Add(slitLo); hs.Add(slitHi); hs.Add(slitHi);
            for (int i = 1; i <= rowsHigh; i++) hs.Add(Mathf.Lerp(slitHi, H * Drum, i / (float)rowsHigh));
            for (int i = 1; i <= rowsTop; i++) hs.Add(H * Mathf.Lerp(Drum, 1f, 1f - Sq(1f - i / (float)rowsTop)));
            // Columns: angle 0 = front; slit edges duplicated
            var angles = new List<float>();
            int cols = near ? 72 : 14;
            for (int j = 0; j < cols; j++) angles.Add(-Mathf.PI + 2f * Mathf.PI * j / cols);
            if (near) foreach (float e in new[] { -slitOut, -slitIn, slitIn, slitOut }) { angles.Add(e); angles.Add(e + 1e-5f); }
            angles.Sort();

            // Edge copies (e, e + 1e-5): the copy towards the slit belongs to it
            bool Inside(float a) => a > 0f ? a > slitIn + 5e-6f && a < slitOut + 5e-6f : a > -slitOut + 5e-6f && a < -slitIn + 5e-6f;
            var pts = new Vector3[hs.Count, angles.Count];
            var col = new Color[hs.Count, angles.Count];
            int lo0 = rowsLow + 1, hi0 = rowsLow + 2; // rows inside the slit band (second copy of the lower edge, first of the upper)
            for (int i = 0; i < hs.Count; i++)
            for (int j = 0; j < angles.Count; j++)
            {
                float a = angles[j], h = hs[i];
                bool slit = near ? (i == lo0 || i == hi0) && Inside(a) : h > slitLo - 0.004f && h < slitHi + 0.004f && Mathf.Abs(a) > slitIn && Mathf.Abs(a) < slitOut;
                float rr = Scale(h) * (near && slit ? 0.9f : 1f); // the slit is a recess about 1 cm deep
                pts[i, j] = o + new Vector3(Mathf.Sin(a) * Rx * rr, h, Mathf.Cos(a) * Rz * rr * Keel(a, h));
                col[i, j] = slit ? Slit : Steel * (0.94f + 0.08f * Mathf.Cos(a));
            }
            kit.Grid(pts, true, SteelS, (i, j) => col[i, j]);
            // Dark cap inside the open bottom
            kit.Lathe(Matrix4x4.TRS(o + Vector3.up * 0.012f, Quaternion.identity, new Vector3(Rx * 0.97f, 1f, Rz * 0.97f)), new[] { V2(0f, 0f), V2(1f, 0f) }, near ? 24 : 8, DarkS, (i, a) => Slit);

            if (!near)
            {
                if (style.Plume.a > 0f) Plume(kit, o + Vector3.up * (H - 0.01f), false);
                return kit;
            }

            Vector3 Surface(float a, float h, float lift)
            {
                float sc = Scale(h) + lift / Rx;
                return o + new Vector3(Mathf.Sin(a) * Rx * sc, h, Mathf.Cos(a) * Rz * sc * Keel(a, h));
            }
            // Brass upright down the keel (between the slits) and the brow band above them, rolled lower edge
            Strip(kit, h => Surface(-0.075f, h, 0.003f), h => Surface(0.075f, h, 0.003f), 0.02f, slitHi + 0.045f, 14, BrassS, Brass, Vector3.forward);
            Strip(kit, a => Surface(a, slitHi + 0.006f, 0.003f), a => Surface(a, slitHi + 0.034f, style.Crown ? 0.006f : 0.003f), -Mathf.PI, Mathf.PI, 72, BrassS, style.Crown ? style.Trim : Brass, null);
            if (style.Crown)
                for (int k = 0; k < 10; k++)
                {
                    // The crown's points stand on the brow band
                    float a = -Mathf.PI + 2f * Mathf.PI * k / 10f;
                    Vector3 p = Surface(a, slitHi + 0.05f, 0.007f), outward = (p - new Vector3(o.x, p.y, o.z)).normalized;
                    kit.Box(Matrix4x4.TRS(p, Quaternion.LookRotation(outward, Vector3.up), Vector3.one), new Vector3(0.026f, 0.036f, 0.007f), 0.003f, BrassS, _ => style.Trim);
                    kit.Dome(p + Vector3.up * 0.02f + outward * 0.004f, outward, 0.006f, k % 2 == 0 ? new Color(0.7f, 0.08f, 0.1f) : new Color(0.1f, 0.25f, 0.6f), BrassS);
                }
            var lip = new Vector3[5, 64];
            for (int j = 0; j < 64; j++)
            {
                float a = -Mathf.PI + 2f * Mathf.PI * j / 64f;
                for (int i = 0; i < 5; i++)
                {
                    float b = Mathf.PI * i / 4f;
                    lip[i, j] = Surface(a, 0.004f - 0.007f * Mathf.Cos(b), 0.003f * Mathf.Sin(b) + 0.001f);
                }
            }
            kit.Grid(lip, true, SteelS, (i, j) => Steel * 0.9f);
            for (int k = 0; k < 18; k++)
            {
                float a = -Mathf.PI + 2f * Mathf.PI * (k + 0.5f) / 18f;
                Vector3 p = Surface(a, 0.022f, 0.001f);
                kit.Dome(p, (p - new Vector3(o.x, p.y, o.z)).normalized, 0.0048f, Steel * 0.95f, SteelS);
            }
            // Breaths: two crosses of small holes on the right cheek
            foreach (float a0 in new[] { 0.55f, 0.9f })
                for (int k = -2; k <= 2; k++)
                {
                    Vector2 d = k == 0 ? Vector2.zero : (Mathf.Abs(k) == 1 ? new Vector2(k * 0.065f, 0f) : new Vector2(0f, (k > 0 ? 1 : -1) * 0.018f));
                    Vector3 p = Surface(a0 + d.x, 0.13f + d.y, -0.0005f);
                    kit.Dome(p, (p - new Vector3(o.x, p.y, o.z)).normalized, 0.0042f, Slit, DarkS);
                }
            if (style.Plume.a > 0f) Plume(kit, o + Vector3.up * (H - 0.01f), true);
            // Brass finial on the point
            kit.Lathe(Matrix4x4.TRS(o + Vector3.up * (H - 0.006f), Quaternion.identity, Vector3.one),
                      new[] { V2(0f, 0f), V2(0.012f, 0.002f), V2(0.01f, 0.012f), V2(0.006f, 0.02f), V2(0f, 0.023f) }, 16, BrassS, (i, a) => Brass);
            return kit;
        }

        /// <summary>A plume of feathers rising from the helm's point and sweeping back (the general seen from afar).</summary>
        private static void Plume(MeshKit kit, Vector3 basePoint, bool near)
        {
            int n = near ? 15 : 5;
            Color tip = new Color(0.62f, 0.06f, 0.08f);
            for (int k = 0; k < n; k++)
            {
                float t = n == 1 ? 0f : k / (float)(n - 1);
                float back = Mathf.Lerp(5f, 100f, t), side = (k % 3 - 1) * (near ? 9f : 0f);
                Vector3 dir = Quaternion.Euler(-back, side, 0f) * Vector3.up; // up, then over towards the back
                dir = new Vector3(dir.x, dir.y, -Mathf.Abs(dir.z));
                float len = Mathf.Lerp(0.26f, 0.36f, Mathf.Sin(t * Mathf.PI));
                Matrix4x4 m = Matrix4x4.TRS(basePoint + dir * (len * 0.5f), Quaternion.LookRotation(dir, Vector3.Cross(dir, Vector3.right)), Vector3.one);
                // Broad feathers, white with red tips: a crest seen from the map
                kit.Box(m, new Vector3(0.012f, near ? 0.075f : 0.1f, len), 0.004f, ClothS, p => Color.Lerp(style.Plume, tip, Mathf.Clamp01((p - basePoint).magnitude / len * 1.6f - 0.9f)));
            }
        }

        /// <summary>Laminated shoulder cap over the top and outside of the shoulder (three lames and a rolled rim).</summary>
        private static MeshKit Pauldron(Rig r, int s, bool near)
        {
            var kit = new MeshKit();
            float x = s == 0 ? -1f : 1f;
            Vector3 axis = new Vector3(x * 0.62f, 1f, -0.05f).normalized;
            Vector3 c = r.Shoulder[s] + new Vector3(x * -0.008f, 0.012f, 0f);
            Matrix4x4 m = Matrix4x4.TRS(c, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one);
            const float R = 0.105f, Hh = 0.085f;
            // Dome from the crown down; each lower lame steps out over the one above (duplicated rows = crisp step)
            float Rad(float t, int lame) => R * Mathf.Sin(t * Mathf.PI * 0.5f) + 0.006f * t + 0.005f * lame;
            float Hgt(float t) => Hh * (1f - t) - 0.012f * t;
            float[] steps = near ? new[] { 0.45f, 0.72f } : new float[0];
            int n = near ? 14 : 4, lameIndex = 0;
            var prof = new List<Vector2> { V2(0f, Hh) };
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                while (lameIndex < steps.Length && t >= steps[lameIndex])
                {
                    float ts = steps[lameIndex];
                    prof.Add(V2(Rad(ts, lameIndex), Hgt(ts)));
                    lameIndex++;
                    prof.Add(V2(Rad(ts, lameIndex), Hgt(ts)));
                }
                prof.Add(V2(Rad(t, lameIndex), Hgt(t)));
            }
            float rimR = prof[prof.Count - 1].x, rimH = Hgt(1f);
            if (near) { prof.Add(V2(rimR + 0.004f, rimH - 0.004f)); prof.Add(V2(rimR - 0.002f, rimH - 0.008f)); }
            prof.Add(V2(rimR - 0.008f, rimH - 0.002f));
            prof.Add(V2(0f, Hh - 0.012f)); // inner surface back to the crown: a closed shell
            kit.Lathe(m, prof.ToArray(), near ? 36 : 10, SteelS, (i, a) => Steel * (i == prof.Count - 1 ? 0.5f : 1f));
            if (near)
                for (int k = 0; k < 6; k++)
                {
                    float a = (k / 6f) * Mathf.PI * 2f;
                    Vector3 p = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * (rimR - 0.003f), rimH + 0.008f, Mathf.Sin(a) * (rimR - 0.003f)));
                    kit.Dome(p, ((p - m.MultiplyPoint3x4(new Vector3(0f, rimH + 0.008f, 0f))).normalized + axis * 0.3f).normalized, 0.0045f, Brass, BrassS);
                }
            return kit;
        }

        /// <summary>Couter over the elbow, vambrace down the forearm with rolled ends, flared gauntlet cuff at the wrist.</summary>
        private static MeshKit Forearm(Rig r, int s, bool near)
        {
            var kit = new MeshKit();
            float x = s == 0 ? -1f : 1f;
            Vector3 e = r.Elbow[s], w = r.Wrist[s], dir = (w - e).normalized;
            int seg = near ? 28 : 8;
            Matrix4x4 m = Matrix4x4.TRS(e, Quaternion.FromToRotation(Vector3.up, dir), Vector3.one);
            float L = Vector3.Distance(e, w);

            // Vambrace
            var vam = new List<Vector2>();
            float v0 = 0.055f, v1 = L - 0.035f;
            if (near) vam.AddRange(new[] { V2(0.049f, v0 - 0.004f), V2(0.0545f, v0), V2(0.056f, v0 + 0.006f), V2(0.0535f, v0 + 0.012f) });
            else vam.Add(V2(0.054f, v0));
            int rows = near ? 8 : 2;
            for (int i = 1; i <= rows; i++) vam.Add(V2(Mathf.Lerp(0.0535f, 0.046f, i / (float)rows), Mathf.Lerp(v0 + 0.012f, v1, i / (float)rows)));
            vam.Add(V2(0.041f, v1 + 0.003f));
            kit.Lathe(m, vam.ToArray(), seg, SteelS, (i, a) => Steel * (0.95f + 0.07f * Mathf.Cos(a)));

            // Gauntlet cuff flaring over the vambrace end
            Matrix4x4 wm = Matrix4x4.TRS(w, Quaternion.FromToRotation(Vector3.up, -dir), Vector3.one);
            var cuff = near
                ? new[] { V2(0.039f, -0.006f), V2(0.043f, 0f), V2(0.05f, 0.03f), V2(0.058f, 0.06f), V2(0.061f, 0.066f), V2(0.057f, 0.07f), V2(0.05f, 0.066f) }
                : new[] { V2(0.042f, 0f), V2(0.058f, 0.065f), V2(0.05f, 0.066f) };
            kit.Lathe(wm, cuff, seg, SteelS, (i, a) => Steel * (i >= cuff.Length - 2 ? 0.75f : 1f));

            // Couter: cup on the back of the elbow with a rim
            Vector3 cupAxis = new Vector3(x * 0.35f, 0.1f, -1f).normalized;
            Matrix4x4 cm = Matrix4x4.TRS(e + cupAxis * 0.012f, Quaternion.FromToRotation(Vector3.up, cupAxis), Vector3.one);
            var cup = new List<Vector2> { V2(0f, 0.05f) };
            int cs = near ? 7 : 3;
            for (int i = 1; i <= cs; i++)
            {
                float t = i / (float)cs * Mathf.PI * 0.5f;
                cup.Add(V2(0.055f * Mathf.Sin(t), 0.05f * Mathf.Cos(t)));
            }
            if (near) { cup.Add(V2(0.062f, -0.004f)); cup.Add(V2(0.06f, -0.009f)); }
            cup.Add(V2(0.048f, -0.006f));
            cup.Add(V2(0f, 0.035f));
            kit.Lathe(cm, cup.ToArray(), near ? 24 : 8, SteelS, (i, a) => Steel);
            if (near) kit.Dome(cm.MultiplyPoint3x4(new Vector3(0f, 0.05f, 0f)), cupAxis, 0.007f, Brass, BrassS);
            return kit;
        }

        /// <summary>Poleyn over the knee (cup with a side wing), greave down the shin with a front ridge.</summary>
        private static MeshKit Leg(Rig r, int s, bool near)
        {
            var kit = new MeshKit();
            float x = s == 0 ? -1f : 1f;
            Vector3 k = r.Knee[s], ankle = new Vector3(r.Ankle[s].x, 0.075f, r.Ankle[s].z + 0.012f);
            Vector3 dir = (ankle - k).normalized;
            int seg = near ? 28 : 8;

            // Greave: slightly oval (deeper front to back), swelling at the calf, ridge down the shin
            Matrix4x4 m = Matrix4x4.TRS(k, Quaternion.LookRotation(Vector3.forward, -dir), Vector3.one);
            float L = Vector3.Distance(k, ankle);
            var g = new List<Vector2>();
            int rows = near ? 10 : 3;
            float g0 = 0.05f, g1 = L + 0.012f;
            for (int i = 0; i <= rows; i++)
            {
                float t = i / (float)rows;
                g.Add(V2(Mathf.Lerp(0.062f, 0.05f, t) + 0.008f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.6f)), -(g0 + (g1 - g0) * t)));
            }
            if (near) { g.Insert(0, V2(0.058f, -g0 + 0.004f)); g.Add(V2(0.047f, -g1 - 0.004f)); }
            float Ridge(float a) => 1f + 0.12f * Mathf.Exp(-Sq(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f) / 16f));
            kit.Lathe(m, g.ToArray(), seg, SteelS, (i, a) => Steel * (0.94f + 0.08f * Mathf.Sin(a)),
                      (i, a) => (1f + 0.12f * Mathf.Sin(a) * Mathf.Sin(a)) * (near ? Ridge(a) : 1f));

            // Poleyn
            Vector3 capAxis = new Vector3(x * 0.25f, 0.15f, 1f).normalized;
            Matrix4x4 cm = Matrix4x4.TRS(k + capAxis * 0.02f, Quaternion.FromToRotation(Vector3.up, capAxis), Vector3.one);
            var cup = new List<Vector2> { V2(0f, 0.055f) };
            int cs = near ? 7 : 3;
            for (int i = 1; i <= cs; i++)
            {
                float t = i / (float)cs * Mathf.PI * 0.5f;
                cup.Add(V2(0.064f * Mathf.Sin(t), 0.055f * Mathf.Cos(t)));
            }
            if (near) { cup.Add(V2(0.071f, -0.004f)); cup.Add(V2(0.069f, -0.009f)); }
            cup.Add(V2(0.056f, -0.006f));
            cup.Add(V2(0f, 0.04f));
            kit.Lathe(cm, cup.ToArray(), near ? 28 : 8, SteelS, (i, a) => Steel, (i, a) => 1f + 0.18f * Mathf.Cos(a) * Mathf.Cos(a)); // wider across the knee
            if (near)
            {
                kit.Dome(cm.MultiplyPoint3x4(new Vector3(0f, 0.055f, 0f)), capAxis, 0.008f, Brass, BrassS);
                // Side wing (fan) on the outside of the knee
                Vector3 wingC = k + new Vector3(x * 0.07f, 0f, 0.0f);
                Matrix4x4 wm = Matrix4x4.TRS(wingC, Quaternion.FromToRotation(Vector3.up, new Vector3(x, 0f, 0.25f).normalized), new Vector3(1f, 1f, 1.25f));
                kit.Lathe(wm, new[] { V2(0f, 0.012f), V2(0.03f, 0.008f), V2(0.038f, 0.002f), V2(0.036f, -0.003f), V2(0f, -0.002f) }, 20, SteelS, (i, a) => Steel * 0.97f);
            }
            return kit;
        }

        /// <summary>
        /// A war bow through the fist (grip at the origin): limbs along the frame's ±X curving forward (+Y) to horn
        /// tips, a leather grip, the string straight behind; gilded tips on a general's.
        /// </summary>
        private static MeshKit Bow(Matrix4x4 m, bool near)
        {
            var kit = new MeshKit();
            int n = near ? 17 : 5, ring = near ? 6 : 4;
            const float Half = 0.68f, Belly = 0.1f;
            var limb = new Vector3[n, ring];
            for (int i = 0; i < n; i++)
            {
                float s = -1f + 2f * i / (n - 1);
                float y = (1f - s * s) * Belly - 0.04f * Mathf.Pow(Mathf.Abs(s), 6f); // recurved tips
                float half = Mathf.Lerp(0.019f, 0.007f, Mathf.Abs(s));
                for (int j = 0; j < ring; j++)
                {
                    float a = j * Mathf.PI * 2f / ring;
                    limb[i, j] = m.MultiplyPoint3x4(new Vector3(s * Half, y + Mathf.Sin(a) * half * 0.7f, Mathf.Cos(a) * half));
                }
            }
            kit.Grid(limb, true, WoodS, (i, j) => Mathf.Abs(i - (n - 1) / 2) <= (near ? 1 : 0) ? DarkLeather
                                                  : (i == 0 || i == n - 1) ? (style.Crown ? style.Trim : Iron) : Wood);
            // String, from tip to tip behind the grip
            Vector3 top = m.MultiplyPoint3x4(new Vector3(Half, -0.04f, 0f)), bottom = m.MultiplyPoint3x4(new Vector3(-Half, -0.04f, 0f));
            Vector3 along = (top - bottom).normalized, u = Vector3.Cross(along, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(along, Vector3.up).normalized : Vector3.right, w = Vector3.Cross(along, u);
            var str = new Vector3[2, 4];
            for (int j = 0; j < 4; j++)
            {
                Vector3 off = (u * Mathf.Cos(j * Mathf.PI * 0.5f) + w * Mathf.Sin(j * Mathf.PI * 0.5f)) * 0.0028f;
                str[0, j] = bottom + off; str[1, j] = top + off;
            }
            kit.Grid(str, true, ClothS, (i, j) => new Color(0.86f, 0.82f, 0.70f));
            return kit;
        }

        /// <summary>Arming sword along the frame's +Y (grip centre at the origin, blade width along X).</summary>
        private static MeshKit Sword(Matrix4x4 m, bool near)
        {
            var kit = new MeshKit();
            // Blade: diamond-ish section with bevelled edges and a fuller, tapering to the point
            var ys = new List<float>();
            if (near) { for (float y = 0.115f; y < 0.74f; y += 0.06f) ys.Add(y); ys.AddRange(new[] { 0.74f, 0.80f, 0.85f, 0.885f, 0.905f }); }
            else ys.AddRange(new[] { 0.115f, 0.72f, 0.905f });
            var blade = new Vector3[ys.Count, near ? 12 : 4];
            for (int i = 0; i < ys.Count; i++)
            {
                float y = ys[i], u = Mathf.Clamp01((y - 0.72f) / 0.185f);
                float w = Mathf.Lerp(0.026f, 0.02f, Mathf.Clamp01((y - 0.115f) / 0.6f)) * Mathf.Pow(Mathf.Max(0f, 1f - u * u), 0.7f);
                float t = 0.0055f * Mathf.Lerp(1f, 0.3f, u);
                w = Mathf.Max(w, 0.0004f); t = Mathf.Max(t, 0.0004f);
                Vector2[] sec = near
                    ? new[] { V2(w, 0f), V2(w, 0f), V2(w * 0.55f, t), V2(w * 0.55f, t), V2(0f, t * 0.6f), V2(-w * 0.55f, t), V2(-w * 0.55f, t), V2(-w, 0f), V2(-w, 0f), V2(-w * 0.55f, -t), V2(-w * 0.55f, -t), V2(0f, -t * 0.6f) }
                    : new[] { V2(w, 0f), V2(0f, t), V2(-w, 0f), V2(0f, -t) };
                for (int j = 0; j < sec.Length; j++) blade[i, j] = m.MultiplyPoint3x4(new Vector3(sec[j].x, y, sec[j].y));
            }
            kit.Grid(blade, true, SteelS, (i, j) => near && (j == 0 || j == 1 || j == 7 || j == 8) ? Steel * 1.15f : Steel);

            // Crossguard: bar with slightly drooping ends
            int gr = near ? 13 : 3, ring = near ? 8 : 4;
            var guard = new Vector3[gr, ring];
            for (int i = 0; i < gr; i++)
            {
                float sx = -1f + 2f * i / (gr - 1), x = 0.115f * sx;
                float h = Mathf.Lerp(0.011f, 0.008f, Mathf.Abs(sx)), d = Mathf.Lerp(0.012f, 0.009f, Mathf.Abs(sx));
                for (int j = 0; j < ring; j++)
                {
                    float a = j * Mathf.PI * 2f / ring + Mathf.PI / ring;
                    guard[i, j] = m.MultiplyPoint3x4(new Vector3(x, 0.105f + 0.012f * sx * sx + h * Mathf.Cos(a), d * Mathf.Sin(a)));
                }
            }
            kit.Grid(guard, true, BrassS, (i, j) => Brass);
            // Grip with leather wrap and closed ends
            var grip = new List<Vector2> { V2(0f, -0.088f) };
            int gs = near ? 8 : 1;
            for (int i = 0; i <= gs; i++) grip.Add(V2(0.015f + (near ? 0.0018f * (i % 2) : 0f), Mathf.Lerp(-0.088f, 0.094f, i / (float)gs)));
            grip.Add(V2(0f, 0.094f));
            kit.Lathe(m, grip.ToArray(), near ? 12 : 5, LeatherS, (i, a) => DarkLeather * (i % 2 == 0 ? 1f : 1.25f));
            // Wheel pommel
            Matrix4x4 wheel = m * Matrix4x4.TRS(new Vector3(0f, -0.11f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            var wp = near
                ? new[] { V2(0f, 0.012f), V2(0.012f, 0.012f), V2(0.026f, 0.008f), V2(0.031f, 0f), V2(0.026f, -0.008f), V2(0.012f, -0.012f), V2(0f, -0.012f) }
                : new[] { V2(0f, 0.01f), V2(0.03f, 0f), V2(0f, -0.01f) };
            kit.Lathe(wheel, wp, near ? 20 : 6, BrassS, (i, a) => Brass);
            return kit;
        }

        /// <summary>
        /// Heater shield in the frame (face normal +Y): planks with a curved painted face (red field, gold cross),
        /// iron edging, plain wood back; the point hangs towards the hand.
        /// </summary>
        private static MeshKit Shield(Matrix4x4 f, bool near)
        {
            var kit = new MeshKit();
            // In-plane axes: v down the shield towards the hand (projected forearm direction), u across
            const float W = 0.215f, Hgt = 0.56f, Top = 0.21f, T = 0.016f, Curve = 0.032f, Rim = 0.014f, CrossHalf = 0.035f;
            float Half(float v) // half width at distance v below the top edge
            {
                float sv = v / Hgt;
                if (sv <= 0.42f) return W;
                float q = (sv - 0.42f) / 0.58f;
                return Mathf.Max(0.002f, W * Mathf.Sqrt(Mathf.Max(0f, 1f - q * q * q * 0.9f - q * 0.1f)) * (1f - q * 0.05f));
            }
            Vector3 P(float u, float v, float lift)
            {
                float zc = Curve * (1f - Sq(u / W));
                return f.MultiplyPoint3x4(new Vector3(u, zc + lift, Top - v));
            }
            // Rows from top to point; columns across with rim and cross edges duplicated (crisp paint)
            int rows = near ? 26 : 6;
            var vs = new List<float>();
            for (int i = 0; i <= rows; i++) vs.Add(Hgt * i / rows);
            foreach (float e in near ? new[] { Rim, Hgt * 0.36f - CrossHalf, Hgt * 0.36f + CrossHalf } : new[] { Hgt * 0.36f - CrossHalf, Hgt * 0.36f + CrossHalf }) { vs.Add(e); vs.Add(e + 1e-5f); }
            vs.Sort();
            var us = new List<float>();
            int ncol = near ? 14 : 4;
            for (int j = 0; j <= ncol; j++) us.Add(-1f + 2f * j / ncol);
            foreach (float e in near ? new[] { -1f + Rim / W, -CrossHalf / W, CrossHalf / W, 1f - Rim / W } : new[] { -CrossHalf / W, CrossHalf / W }) { us.Add(e); us.Add(e + 1e-5f); }
            us.Sort();

            Color FrontPaint(float u, float v, float half)
            {
                // Far mesh: the cross only (no rim band)
                float uu = u * half;
                bool rim = near && (v < Rim + 5e-6f || half - Mathf.Abs(uu) < Rim + 5e-6f || v > Hgt - 0.02f);
                if (rim) return Iron * (0.95f + 0.1f * SdfCreature.Noise(new Vector3(uu, v, 0f) * 60f));
                bool cross = Mathf.Abs(uu) < CrossHalf + 5e-6f || Mathf.Abs(v - Hgt * 0.36f) < CrossHalf + 5e-6f;
                Color c = cross ? style.Trim : style.Field;
                return c * (0.9f + 0.14f * SdfCreature.Noise(new Vector3(uu, v, 0f) * 25f));
            }

            var front = new Vector3[vs.Count, us.Count];
            var back = new Vector3[vs.Count, us.Count];
            var fc = new Color[vs.Count, us.Count];
            for (int i = 0; i < vs.Count; i++)
            {
                float v = vs[i], half = Half(v);
                for (int j = 0; j < us.Count; j++)
                {
                    float u = us[j] * half;
                    front[i, j] = P(u, v, T * 0.5f);
                    back[i, j] = P(u, v, -T * 0.5f);
                    // Colour of a duplicated edge copy: sample a hair inside its cell
                    float cu = us[j] + (j + 1 < us.Count && us[j + 1] - us[j] < 2e-5f ? -2e-5f : (j > 0 && us[j] - us[j - 1] < 2e-5f ? 2e-5f : 0f));
                    float cv = v + (i + 1 < vs.Count && vs[i + 1] - v < 2e-5f ? -2e-5f : (i > 0 && v - vs[i - 1] < 2e-5f ? 2e-5f : 0f));
                    fc[i, j] = FrontPaint(cu, cv, Half(cv));
                }
            }
            Vector3 normal = f.MultiplyVector(Vector3.up);
            kit.Grid(front, false, PaintS, (i, j) => fc[i, j], normal);
            kit.Grid(back, false, WoodS, (i, j) => Wood * (0.85f + 0.2f * SdfCreature.Noise(back[i, j] * 40f)), -normal);
            // Edge wall all round (top edge, both sides down to the point)
            var outline = new List<Vector2>();
            for (int i = 0; i < vs.Count; i++) outline.Add(V2(-Half(vs[i]), vs[i]));
            for (int i = vs.Count - 1; i >= 0; i--) outline.Add(V2(Half(vs[i]), vs[i]));
            outline.Add(outline[0]);
            var wall = new Vector3[outline.Count, 2];
            for (int i = 0; i < outline.Count; i++)
            {
                wall[i, 0] = P(outline[i].x, outline[i].y, -T * 0.5f);
                wall[i, 1] = P(outline[i].x, outline[i].y, T * 0.5f + (near ? 0.002f : 0f));
            }
            kit.Grid(wall, false, SteelS, (i, j) => Iron);
            if (near)
            {
                // Boss rivets of the enarmes showing on the face
                foreach (Vector2 q in new[] { V2(-0.1f, 0.3f), V2(0.1f, 0.3f), V2(-0.1f, 0.12f), V2(0.1f, 0.12f) })
                    kit.Dome(P(q.x, q.y, T * 0.5f), normal, 0.006f, Iron * 1.2f, SteelS);
            }
            return kit;
        }

        private static MeshKit Buckle(Vector3 centre)
        {
            var kit = new MeshKit();
            Matrix4x4 m = Matrix4x4.TRS(centre, Quaternion.LookRotation(Vector3.up, Vector3.forward), Vector3.one);
            var ring = new Vector3[17, 6];
            for (int i = 0; i <= 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Vector2 c = new Vector2(0.022f * SignedPow(Mathf.Cos(a), 0.4f), 0.017f * SignedPow(Mathf.Sin(a), 0.4f));
                Vector2 o = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (int j = 0; j < 6; j++)
                {
                    float b = j * Mathf.PI * 2f / 6f;
                    Vector2 xy = c + o * (0.004f * Mathf.Cos(b));
                    ring[i, j] = m.MultiplyPoint3x4(new Vector3(xy.y, 0.004f * Mathf.Sin(b), xy.x));
                }
            }
            kit.Grid(ring, true, BrassS, (i, j) => Brass);
            return kit;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Raised strip between two edge curves (edge(t) for t in t0..t1); closed ends.</summary>
        private static void Strip(MeshKit kit, Func<float, Vector3> edgeA, Func<float, Vector3> edgeB, float t0, float t1, int n, Vector3 surface, Color color, Vector3? outward)
        {
            var p = new Vector3[n + 1, 2];
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Lerp(t0, t1, i / (float)n);
                p[i, 0] = edgeA(t); p[i, 1] = edgeB(t);
            }
            kit.Grid(p, false, surface, (i, j) => color, outward);
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

        private static Vector2 V2(float x, float y) => new Vector2(x, y);
        private static float Sq(float x) => x * x;
        private static float SignedPow(float v, float p) => Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), p);
    }
}
