using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Builds characters from the rigged, animated FBX models in CharacterModelLibrary: scaled to human height with
    /// the feet on the ground, an Animator driven by CharacterAnimationDriver, per-role colours and bone-attached
    /// weapons. Returns null when the library is not set up (callers then use the procedural primitive model).
    /// </summary>
    public static class RiggedCharacterBuilder
    {
        private const float HeadTopHeight = 1.78f;

        private struct Look
        {
            public Color Primary;   // armour / shirt
            public Color Secondary; // boots / pants
            public Color Skin;
            public Color Hair;
            public float Metal;     // metallic of the armour
        }

        private static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

        public static GameObject TryCreate(HumanoidModelBuilder.HumanType type, string name)
        {
            CharacterModelLibrary lib = CharacterModelLibrary.Instance;
            if (lib == null) return null;

            bool knight = IsKnight(type);
            GameObject source = knight ? lib.KnightModel : lib.VillagerModel;
            RuntimeAnimatorController controller = knight ? lib.KnightController : lib.VillagerController;
            Avatar avatar = knight ? lib.KnightAvatar : lib.VillagerAvatar;
            if (source == null || controller == null) return null;

            GameObject root = new GameObject(name);
            GameObject model = Object.Instantiate(source, root.transform, false);
            model.name = "Rig";
            Mesh processed = knight ? lib.KnightMesh : lib.VillagerMesh; // smoothed normals + rest positions
            if (processed != null)
                foreach (SkinnedMeshRenderer s in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.sharedMesh = processed;
            FitToHeight(root.transform, model.transform);

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            root.AddComponent<GpuSkinnedCharacter>(); // drawn on the GPU when small on screen (if its body allows it)

            // The knight guard has its own sculpted body and gear on this skeleton
            if (type == HumanoidModelBuilder.HumanType.KnightGuard && KnightModelBuilder.Build(root, model.transform))
            {
                // The Animator bound the imported body when it got its controller; with the sculpted skin unknown to
                // it, CullUpdateTransforms saw nothing visible and never moved the bones (knights spawned in play
                // stood still). Rebind makes it see the new skin.
                animator.Rebind();
                root.AddComponent<CharacterGear>().Type = type; // sculpted armour: the gear only changes stats
                model.AddComponent<FootIK>();
                root.AddComponent<CharacterAnimationDriver>();
                return root;
            }

            // Weapon, shield and helmet are worn gear (Equipment -> CharacterGear) when the character can be baked
            // into one mesh; otherwise the role's fixed props are attached as before. The warlord's horned helm is
            // part of his outfit either way.
            SkinnedMeshRenderer bodyRenderer = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            bool gearMesh = bodyRenderer != null && bodyRenderer.sharedMesh != null && bodyRenderer.sharedMesh.isReadable && Shader.Find("Custom/CharacterSurface") != null;
            bool warlord = type == HumanoidModelBuilder.HumanType.Warlord;
            ApplyLook(model, type);
            if (!gearMesh) AttachEquipment(model.transform, type);
            if (!gearMesh || warlord) AttachHelmet(model.transform, type);
            AttachOutfit(model.transform, type);
            if (!knight) AttachFaceDetails(model.transform, type, lib);

            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                smr.updateWhenOffscreen = false;
            }

            if (gearMesh && BakeCharacter(root, model.transform))
            {
                CharacterGear gear = root.AddComponent<CharacterGear>();
                gear.Type = type;
                gear.Body = bodyRenderer;
                gear.BaseMesh = bodyRenderer.sharedMesh;
                gear.FixedHelmet = warlord;
                gear.KnightRig = knight;
            }
            else
            {
                MergeAttachments(root, model.transform);
                root.AddComponent<CharacterGear>().Type = type;
            }
            model.AddComponent<FootIK>();
            root.AddComponent<CharacterAnimationDriver>();
            return root;
        }

        public static bool IsKnight(HumanoidModelBuilder.HumanType type) =>
            type == HumanoidModelBuilder.HumanType.HeroPlayer ||
            type == HumanoidModelBuilder.HumanType.KnightGuard ||
            type == HumanoidModelBuilder.HumanType.Warlord;

        /// <summary>Scales the rig so the top of the head is at HeadTopHeight and moves the lowest bone to y = 0.</summary>
        public static void FitToHeight(Transform root, Transform model)
        {
            Transform head = FindDeep(model, "Head_end") ?? FindDeep(model, "Head");
            if (head == null) return;

            float low = LowestBoneY(model);
            float height = head.position.y - low;
            if (height <= 1e-4f) return;

            model.localScale *= HeadTopHeight / height;
            model.localPosition += Vector3.up * (root.position.y - LowestBoneY(model));
        }

        private static float LowestBoneY(Transform model)
        {
            float low = float.MaxValue;
            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("PoleTarget")) continue; // IK helpers sit outside the body
                if (t.name.StartsWith("Foot") || t.name.StartsWith("LowerLeg")) low = Mathf.Min(low, t.position.y);
            }
            return low == float.MaxValue ? model.position.y : low;
        }

        private static Look LookFor(HumanoidModelBuilder.HumanType type)
        {
            switch (type)
            {
                case HumanoidModelBuilder.HumanType.HeroPlayer:
                    return new Look { Primary = new Color(0.70f, 0.74f, 0.82f), Secondary = new Color(0.22f, 0.13f, 0.08f), Skin = new Color(0.92f, 0.72f, 0.58f), Metal = 0.8f };
                case HumanoidModelBuilder.HumanType.KnightGuard:
                    return new Look { Primary = new Color(0.55f, 0.57f, 0.61f), Secondary = new Color(0.18f, 0.12f, 0.09f), Skin = new Color(0.88f, 0.68f, 0.54f), Metal = 0.75f };
                case HumanoidModelBuilder.HumanType.Warlord:
                    return new Look { Primary = new Color(0.20f, 0.18f, 0.18f), Secondary = new Color(0.10f, 0.08f, 0.07f), Skin = new Color(0.75f, 0.58f, 0.46f), Metal = 0.7f };
                case HumanoidModelBuilder.HumanType.Bandit:
                    return new Look { Primary = new Color(0.42f, 0.13f, 0.10f), Secondary = new Color(0.17f, 0.15f, 0.13f), Skin = new Color(0.80f, 0.60f, 0.46f), Hair = new Color(0.10f, 0.08f, 0.07f) };
                case HumanoidModelBuilder.HumanType.Goblin:
                    return new Look { Primary = new Color(0.33f, 0.24f, 0.15f), Secondary = new Color(0.24f, 0.19f, 0.13f), Skin = new Color(0.42f, 0.58f, 0.26f), Hair = new Color(0.15f, 0.22f, 0.10f) };
                default: // Villager
                    return new Look { Primary = new Color(0.36f, 0.48f, 0.26f), Secondary = new Color(0.35f, 0.25f, 0.16f), Skin = new Color(0.90f, 0.70f, 0.55f), Hair = new Color(0.30f, 0.20f, 0.12f) };
            }
        }

        /// <summary>Recolours the FBX materials (by material name) per role; materials are shared per role.</summary>
        private static void ApplyLook(GameObject model, HumanoidModelBuilder.HumanType type)
        {
            Look look = LookFor(type);
            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string key = type + "_" + mats[i].name;
                    if (!MaterialCache.TryGetValue(key, out Material m) || m == null)
                    {
                        Shader surface = Shader.Find("Custom/CharacterSurface");
                        Color original = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor") : Color.white;
                        m = surface != null ? new Material(surface) { name = "MAT_" + key, enableInstancing = true } : new Material(mats[i]) { name = "MAT_" + key };
                        string n = mats[i].name.ToLowerInvariant();
                        // Pattern: 0 skin, 1 cloth, 2 leather, 3 metal, 4 hair, 5 eyes (Custom/CharacterSurface)
                        if (n.Contains("armor")) Style(m, look.Primary, look.Metal, 0.55f, 3);
                        else if (n.Contains("shirt")) Style(m, look.Primary, 0f, 0.15f, 1);
                        else if (n.Contains("boots")) Style(m, look.Secondary, 0f, 0.3f, 2);
                        else if (n.Contains("pants")) Style(m, look.Secondary, 0f, 0.15f, 1);
                        else if (n.Contains("skin")) Style(m, look.Skin, 0f, 0.35f, 0);
                        else if (n.Contains("hair")) Style(m, look.Hair.a > 0f ? look.Hair : original, 0f, 0.3f, 4);
                        else if (n.Contains("socks")) Style(m, Color.Lerp(look.Secondary, Color.white, 0.35f), 0f, 0.15f, 1);
                        else if (n.Contains("eye")) Style(m, original, 0f, 0.6f, 5);
                        else Style(m, original, 0f, 0.2f, 1);
                        MaterialCache[key] = m;
                    }
                    mats[i] = m;
                }
                smr.sharedMaterials = mats;
            }
        }

        private static void Style(Material m, Color color, float metallic, float smoothness, int pattern, bool objectSpace = false)
        {
            Set(m, color, metallic, smoothness);
            if (m.HasProperty("_Pattern")) m.SetFloat("_Pattern", pattern);
            if (m.HasProperty("_ObjectSpace")) m.SetFloat("_ObjectSpace", objectSpace ? 1f : 0f);
        }

        private static void Set(Material m, Color color, float metallic, float smoothness)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        }

        // ---------------------------------------------------------------- equipment

        // Hand-bone frame adjustments (tuned against screenshots).
        public static Vector3 HandWeaponEuler = new Vector3(0f, 0f, 90f);
        public static Vector3 ShieldEuler = new Vector3(0f, 0f, 90f);   // shield face points out from the forearm

        private static void AttachEquipment(Transform model, HumanoidModelBuilder.HumanType type)
        {
            Transform rightHand = FindDeep(model, "Palm.R");
            Transform leftForearm = FindDeep(model, "LowerArm.L");

            Material steel = PlainMetal(new Color(0.74f, 0.76f, 0.80f), 0.85f, 0.7f);
            Material darkSteel = PlainMetal(new Color(0.22f, 0.23f, 0.26f), 0.8f, 0.5f);
            Material gold = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(0.90f, 0.70f, 0.20f), 0.9f, 0.7f);
            Material wood = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.40f, 0.26f, 0.14f), 0f, 0.25f);
            Material leather = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, new Color(0.22f, 0.12f, 0.07f), 0f, 0.25f);

            if (rightHand != null)
            {
                // The knight rig has sword clips that expect the blade along the palm -X axis; the villager rig has
                // none, so there the weapon points forward out of the fist (palm +X) instead of backwards.
                Transform held = NewAttachment("HeldWeapon", rightHand, IsKnight(type) ? HandWeaponEuler : -HandWeaponEuler);
                switch (type)
                {
                    case HumanoidModelBuilder.HumanType.Villager:
                        BuildWoodAxe(held, wood, steel);
                        break;
                    case HumanoidModelBuilder.HumanType.Goblin:
                        BuildClub(held, wood);
                        break;
                    default:
                        BuildSword(held, type == HumanoidModelBuilder.HumanType.Warlord ? darkSteel : steel, leather,
                                   type == HumanoidModelBuilder.HumanType.Bandit ? darkSteel : gold,
                                   type == HumanoidModelBuilder.HumanType.Warlord ? 1.25f : 1f);
                        break;
                }
            }

            if (leftForearm != null && (type == HumanoidModelBuilder.HumanType.HeroPlayer || type == HumanoidModelBuilder.HumanType.KnightGuard))
            {
                Color face = type == HumanoidModelBuilder.HumanType.HeroPlayer ? new Color(0.12f, 0.26f, 0.62f) : new Color(0.55f, 0.12f, 0.10f);
                Material faceMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, face, 0f, 0.3f);
                Transform shield = NewAttachment("Shield", leftForearm, ShieldEuler);
                Transform wrist = FindDeep(model, "Palm.L");
                if (wrist != null) shield.position = Vector3.Lerp(leftForearm.position, wrist.position, 0.45f) + shield.up * 0.05f; // mid-forearm, just off the arm
                BuildShield(shield, faceMat, gold, steel);
            }
        }

        /// <summary>
        /// Helmets for the armoured roles. Built in character space (the rig is still in its rest pose, facing +Z,
        /// head upright) and then parented to the Head bone keeping the world pose, so it follows head animation
        /// whatever the bone axes are.
        /// </summary>
        private static void AttachHelmet(Transform model, HumanoidModelBuilder.HumanType type)
        {
            bool knight = type == HumanoidModelBuilder.HumanType.KnightGuard;
            bool warlord = type == HumanoidModelBuilder.HumanType.Warlord;
            bool hero = type == HumanoidModelBuilder.HumanType.HeroPlayer;
            if (!knight && !warlord && !hero) return;

            Transform head = FindDeep(model, "Head");
            Transform headEnd = FindDeep(model, "Head_end");
            if (head == null || headEnd == null) return;

            HeadFrame(head, headEnd, out Vector3 c, out float r);
            Transform root = model.parent != null ? model.parent : model;

            GameObject helmet = new GameObject("Helmet");
            helmet.transform.SetPositionAndRotation(c, root.rotation);

            Material steel = PlainMetal(
                warlord ? new Color(0.16f, 0.15f, 0.15f) : new Color(0.66f, 0.68f, 0.72f), 0.8f, warlord ? 0.45f : 0.6f);
            Material slit = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.03f, 0.03f, 0.03f), 0f, 0.1f);
            Material gold = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(0.90f, 0.70f, 0.20f), 0.9f, 0.7f);
            Material bone = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, new Color(0.86f, 0.80f, 0.66f), 0f, 0.35f);
            Transform h = helmet.transform;

            if (hero)
            {
                // Open kettle helm with brim and a gold crest; the face stays visible.
                Part(PrimitiveType.Sphere, "Dome", h, new Vector3(0f, 0.30f * r, -0.05f * r), new Vector3(2.15f, 1.55f, 2.25f) * r, steel);
                Part(PrimitiveType.Cylinder, "Brim", h, new Vector3(0f, 0.05f * r, -0.05f * r), new Vector3(2.7f, 0.06f, 2.8f) * r, steel);
                Part(PrimitiveType.Cube, "Crest", h, new Vector3(0f, 1.15f * r, -0.1f * r), new Vector3(0.12f, 0.45f, 1.7f) * r, gold);
            }
            else
            {
                // Closed great helm: steel drum, flat top, eye slit and breathing cross.
                Part(PrimitiveType.Cylinder, "Drum", h, new Vector3(0f, -0.05f * r, 0f), new Vector3(2.15f, 1.18f, 2.2f) * r, steel);
                Part(PrimitiveType.Cylinder, "Top", h, new Vector3(0f, 1.15f * r, 0f), new Vector3(1.95f, 0.08f, 2.0f) * r, steel);
                Part(PrimitiveType.Cube, "EyeSlit", h, new Vector3(0f, 0.12f * r, 1.08f * r), new Vector3(1.5f, 0.13f, 0.08f) * r, slit);
                Part(PrimitiveType.Cube, "NoseRidge", h, new Vector3(0f, -0.45f * r, 1.1f * r), new Vector3(0.14f, 0.95f, 0.08f) * r, warlord ? steel : gold);
                Part(PrimitiveType.Cube, "BrowBand", h, new Vector3(0f, 0.42f * r, 1.07f * r), new Vector3(1.9f, 0.12f, 0.08f) * r, warlord ? steel : gold);
                if (warlord)
                {
                    GameObject left = new GameObject("HornL"), right = new GameObject("HornR");
                    left.transform.SetParent(h, false); right.transform.SetParent(h, false);
                    left.transform.localPosition = new Vector3(-1.05f * r, 0.6f * r, 0f);
                    right.transform.localPosition = new Vector3(1.05f * r, 0.6f * r, 0f);
                    left.transform.localRotation = Quaternion.Euler(0f, 0f, 55f);
                    right.transform.localRotation = Quaternion.Euler(0f, 0f, -55f);
                    Part(PrimitiveType.Capsule, "Base", left.transform, new Vector3(0f, 0.45f * r, 0f), new Vector3(0.38f, 0.55f, 0.38f) * r, bone);
                    Part(PrimitiveType.Capsule, "Tip", left.transform, new Vector3(0.12f * r, 1.05f * r, 0f), new Vector3(0.22f, 0.42f, 0.22f) * r, bone);
                    Part(PrimitiveType.Capsule, "Base", right.transform, new Vector3(0f, 0.45f * r, 0f), new Vector3(0.38f, 0.55f, 0.38f) * r, bone);
                    Part(PrimitiveType.Capsule, "Tip", right.transform, new Vector3(-0.12f * r, 1.05f * r, 0f), new Vector3(0.22f, 0.42f, 0.22f) * r, bone);
                }
            }

            helmet.transform.SetParent(head, true);
        }

        /// <summary>
        /// Role-specific outfit pieces (tabard, cape, fur mantle, hood, goblin ears), built in character space in the
        /// rest pose and parented to the torso / hips / head bones so they follow the animation.
        /// </summary>
        private static void AttachOutfit(Transform model, HumanoidModelBuilder.HumanType type)
        {
            Transform torso = FindDeep(model, "Torso"), hips = FindDeep(model, "Hips"), neck = FindDeep(model, "Neck");
            Transform head = FindDeep(model, "Head"), headEnd = FindDeep(model, "Head_end");
            Transform armL = FindDeep(model, "UpperArm.L"), armR = FindDeep(model, "UpperArm.R");
            if (torso == null || hips == null || neck == null || armL == null || armR == null) return;

            Transform root = model.parent != null ? model.parent : model;
            Vector3 fwd = root.forward, up = root.up;
            float shoulders = Vector3.Distance(armL.position, armR.position);
            float chestTop = neck.position.y - 0.04f, waist = hips.position.y;
            Vector3 axis = new Vector3(root.position.x, 0f, root.position.z);

            Material Cloth(Color c) => ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, c, 0f, 0.2f);
            Material gold = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(0.90f, 0.70f, 0.20f), 0.9f, 0.7f);

            Transform Piece(string name, Transform bone, Vector3 worldPos, System.Action<Transform> build)
            {
                GameObject go = new GameObject(name);
                go.transform.SetPositionAndRotation(worldPos, root.rotation);
                build(go.transform);
                go.transform.SetParent(bone, true);
                return go.transform;
            }

            switch (type)
            {
                case HumanoidModelBuilder.HumanType.KnightGuard:
                {
                    Material red = Cloth(new Color(0.55f, 0.10f, 0.09f));
                    float chestH = chestTop - waist;
                    Piece("TabardChest", torso, axis + up * (waist + chestH * 0.5f) + fwd * 0.135f, t =>
                    {
                        Part(PrimitiveType.Cube, "Panel", t, Vector3.zero, new Vector3(shoulders * 0.62f, chestH, 0.025f), red);
                        Part(PrimitiveType.Cube, "CrossV", t, new Vector3(0f, 0.02f, 0.016f), new Vector3(0.05f, chestH * 0.62f, 0.01f), gold);
                        Part(PrimitiveType.Cube, "CrossH", t, new Vector3(0f, chestH * 0.12f, 0.016f), new Vector3(shoulders * 0.36f, 0.05f, 0.01f), gold);
                    });
                    Piece("TabardSkirt", hips, axis + up * (waist - 0.2f) + fwd * 0.125f, t =>
                        Part(PrimitiveType.Cube, "Panel", t, Vector3.zero, new Vector3(shoulders * 0.5f, 0.42f, 0.02f), red));
                    break;
                }
                case HumanoidModelBuilder.HumanType.HeroPlayer:
                {
                    Material blue = Cloth(new Color(0.12f, 0.24f, 0.58f));
                    float capeLen = chestTop - (waist - 0.55f);
                    Piece("Cape", torso, axis + up * (chestTop - capeLen * 0.5f) - fwd * 0.16f, t =>
                    {
                        t.localRotation = Quaternion.Euler(-6f, 0f, 0f);
                        Part(PrimitiveType.Cube, "Cloth", t, Vector3.zero, new Vector3(shoulders * 0.85f, capeLen, 0.03f), blue);
                        Part(PrimitiveType.Sphere, "ClaspL", t, new Vector3(-shoulders * 0.3f, capeLen * 0.48f, 0.05f), Vector3.one * 0.06f, gold);
                        Part(PrimitiveType.Sphere, "ClaspR", t, new Vector3(shoulders * 0.3f, capeLen * 0.48f, 0.05f), Vector3.one * 0.06f, gold);
                    });
                    break;
                }
                case HumanoidModelBuilder.HumanType.Warlord:
                {
                    Material fur = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, new Color(0.24f, 0.17f, 0.11f), 0f, 0.1f);
                    Piece("FurMantle", torso, axis + up * (chestTop - 0.02f), t =>
                    {
                        Part(PrimitiveType.Sphere, "Mantle", t, Vector3.zero, new Vector3(shoulders * 1.25f, 0.2f, 0.42f), fur);
                        Part(PrimitiveType.Sphere, "Collar", t, new Vector3(0f, 0.06f, -0.06f), new Vector3(shoulders * 0.7f, 0.2f, 0.3f), fur);
                    });
                    break;
                }
                case HumanoidModelBuilder.HumanType.Bandit:
                case HumanoidModelBuilder.HumanType.Goblin:
                {
                    if (head == null || headEnd == null) break;
                    HeadFrame(head, headEnd, out Vector3 c, out float r);
                    if (type == HumanoidModelBuilder.HumanType.Bandit)
                    {
                        Material hood = Cloth(new Color(0.20f, 0.15f, 0.12f));
                        Piece("Hood", head, c, t =>
                        {
                            Part(PrimitiveType.Sphere, "Cowl", t, new Vector3(0f, 0.22f * r, -0.32f * r), new Vector3(2.25f, 2.15f, 2.3f) * r, hood);
                            Part(PrimitiveType.Cube, "Mask", t, new Vector3(0f, -0.42f * r, 0.88f * r), new Vector3(1.75f, 0.55f, 0.35f) * r, hood);
                        });
                    }
                    else
                    {
                        Material skin = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, new Color(0.42f, 0.58f, 0.26f), 0f, 0.3f);
                        Piece("Ears", head, c, t =>
                        {
                            foreach (int side in new[] { -1, 1 })
                            {
                                GameObject ear = new GameObject(side < 0 ? "EarL" : "EarR");
                                ear.transform.SetParent(t, false);
                                ear.transform.localPosition = new Vector3(side * 0.95f * r, 0.1f * r, -0.1f * r);
                                ear.transform.localRotation = Quaternion.Euler(-15f, 0f, side * -62f);
                                Part(PrimitiveType.Capsule, "Ear", ear.transform, new Vector3(0f, 0.55f * r, 0f), new Vector3(0.32f, 0.62f, 0.14f) * r, skin);
                            }
                        });
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Head centre and half-height from anatomy (head = 1/7 of body height, top at Head_end): the rigs place the
        /// Head bone differently (chin on the knight, crown on the villager), so its length is not the head size.
        /// </summary>
        private static void HeadFrame(Transform head, Transform headEnd, out Vector3 center, out float halfHeight)
        {
            halfHeight = Mathf.Max(0.13f, Mathf.Min(0.21f, Vector3.Distance(head.position, headEnd.position) * 0.5f));
            center = headEnd.position - Vector3.up * (halfHeight * 0.95f);
        }

        private static readonly Dictionary<string, Material> MetalCache = new Dictionary<string, Material>();

        /// <summary>Brushed, scratched procedural steel (the procedural Metal surface has a dark chain-mail texture).</summary>
        private static Material PlainMetal(Color color, float metallic, float smoothness)
        {
            string key = ColorUtility.ToHtmlStringRGB(color) + metallic + smoothness;
            if (MetalCache.TryGetValue(key, out Material m) && m != null) return m;
            Shader surface = Shader.Find("Custom/CharacterSurface"); // brushed, scratched metal in object space
            m = new Material(surface != null ? surface : Shader.Find("Universal Render Pipeline/Lit")) { name = "MAT_Steel_" + key, enableInstancing = true };
            Style(m, color, metallic, smoothness, 3, objectSpace: true);
            MetalCache[key] = m;
            return m;
        }

        /// <summary>
        /// Brows (and a beard for some villagers) anchored at the eye centres found in the villager mesh, parented to
        /// the head bone. Gives the faces expression instead of two dots and a line.
        /// </summary>
        private static void AttachFaceDetails(Transform model, HumanoidModelBuilder.HumanType type, CharacterModelLibrary lib)
        {
            if (lib.VillagerEyeLeft == Vector3.zero || lib.VillagerEyeRight == Vector3.zero) return;
            Transform head = FindDeep(model, "Head");
            if (head == null) return;
            Transform root = model.parent != null ? model.parent : model;

            Look look = LookFor(type);
            Color hair = look.Hair.a > 0f ? look.Hair : new Color(0.25f, 0.17f, 0.10f);
            if (type == HumanoidModelBuilder.HumanType.Goblin) hair = new Color(0.12f, 0.16f, 0.07f);
            Material hairMat = HairMaterial(hair);

            Vector3 eyeL = root.TransformPoint(lib.VillagerEyeLeft), eyeR = root.TransformPoint(lib.VillagerEyeRight);
            Vector3 up = root.up, fwd = root.forward;
            float frown = type == HumanoidModelBuilder.HumanType.Goblin ? 22f : (type == HumanoidModelBuilder.HumanType.Bandit ? 12f : -4f);

            GameObject face = new GameObject("FaceDetails");
            face.transform.SetPositionAndRotation((eyeL + eyeR) * 0.5f, root.rotation);
            float eyeSpan = Vector3.Distance(eyeL, eyeR);
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 eye = side < 0 ? eyeL : eyeR;
                GameObject brow = new GameObject(side < 0 ? "BrowL" : "BrowR");
                brow.transform.SetParent(face.transform, false);
                brow.transform.position = eye + up * (eyeSpan * 0.42f) + fwd * (eyeSpan * 0.12f);
                brow.transform.localRotation = Quaternion.Euler(0f, 0f, 90f + side * frown); // inner end lower = frown
                Part(PrimitiveType.Capsule, "Brow", brow.transform, Vector3.zero, new Vector3(eyeSpan * 0.16f, eyeSpan * 0.42f, eyeSpan * 0.12f), hairMat);
            }

            if (type == HumanoidModelBuilder.HumanType.Villager && Random.value < 0.6f)
            {
                // Short full beard around the jaw and chin
                Vector3 chin = (eyeL + eyeR) * 0.5f - up * (eyeSpan * 1.55f) - fwd * (eyeSpan * 0.05f);
                GameObject beard = new GameObject("Beard");
                beard.transform.SetParent(face.transform, false);
                beard.transform.position = chin;
                Part(PrimitiveType.Sphere, "Jaw", beard.transform, Vector3.zero, new Vector3(eyeSpan * 2.3f, eyeSpan * 1.25f, eyeSpan * 1.9f), hairMat);
                Part(PrimitiveType.Capsule, "Moustache", beard.transform, up * (eyeSpan * 0.72f) + fwd * (eyeSpan * 0.78f), new Vector3(eyeSpan * 0.2f, eyeSpan * 0.75f, eyeSpan * 0.18f), hairMat).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            face.transform.SetParent(head, true);
        }

        private static Material HairMaterial(Color color)
        {
            string key = "Hair" + ColorUtility.ToHtmlStringRGB(color);
            if (MetalCache.TryGetValue(key, out Material m) && m != null) return m;
            Shader surface = Shader.Find("Custom/CharacterSurface");
            m = new Material(surface != null ? surface : Shader.Find("Universal Render Pipeline/Lit")) { name = "MAT_" + key, enableInstancing = true };
            Style(m, color, 0f, 0.3f, 4, objectSpace: true);
            MetalCache[key] = m;
            return m;
        }

        /// <summary>Child of a bone whose world scale is 1 (bones carry the FBX unit scale).</summary>
        private static Transform NewAttachment(string name, Transform bone, Vector3 euler)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(bone, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            Vector3 s = bone.lossyScale;
            go.transform.localScale = new Vector3(1f / Mathf.Max(1e-5f, s.x), 1f / Mathf.Max(1e-5f, s.y), 1f / Mathf.Max(1e-5f, s.z));
            return go.transform;
        }

        // Weapons point along their local +Y (blade / haft), grip at the origin.
        private static void BuildSword(Transform t, Material blade, Material grip, Material guard, float size)
        {
            Part(PrimitiveType.Cylinder, "Grip", t, new Vector3(0f, 0f, 0f), new Vector3(0.045f, 0.10f, 0.045f) * size, grip);
            Part(PrimitiveType.Sphere, "Pommel", t, new Vector3(0f, -0.12f, 0f) * size, Vector3.one * 0.07f * size, guard);
            Part(PrimitiveType.Cube, "Guard", t, new Vector3(0f, 0.11f, 0f) * size, new Vector3(0.26f, 0.035f, 0.05f) * size, guard);
            Part(PrimitiveType.Cube, "Blade", t, new Vector3(0f, 0.52f, 0f) * size, new Vector3(0.07f, 0.78f, 0.018f) * size, blade);
        }

        private static void BuildWoodAxe(Transform t, Material wood, Material steel)
        {
            Part(PrimitiveType.Cylinder, "Haft", t, new Vector3(0f, 0.25f, 0f), new Vector3(0.04f, 0.42f, 0.04f), wood);
            Part(PrimitiveType.Cube, "AxeHead", t, new Vector3(0.08f, 0.58f, 0f), new Vector3(0.16f, 0.12f, 0.025f), steel);
        }

        private static void BuildClub(Transform t, Material wood)
        {
            Part(PrimitiveType.Cylinder, "Handle", t, new Vector3(0f, 0.18f, 0f), new Vector3(0.05f, 0.22f, 0.05f), wood);
            Part(PrimitiveType.Capsule, "Head", t, new Vector3(0f, 0.48f, 0f), new Vector3(0.12f, 0.20f, 0.12f), wood);
        }

        private static void BuildShield(Transform t, Material face, Material rim, Material boss)
        {
            Part(PrimitiveType.Cylinder, "ShieldFace", t, new Vector3(0f, 0f, 0f), new Vector3(0.52f, 0.025f, 0.52f), face);
            Part(PrimitiveType.Cylinder, "ShieldRim", t, new Vector3(0f, -0.004f, 0f), new Vector3(0.56f, 0.02f, 0.56f), rim);
            Part(PrimitiveType.Sphere, "ShieldBoss", t, new Vector3(0f, 0.03f, 0f), new Vector3(0.12f, 0.06f, 0.12f), boss);
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject p = GameObject.CreatePrimitive(type);
            p.name = name;
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Collider c = p.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
            return p;
        }

        private static readonly Dictionary<string, (Mesh mesh, Material[] materials)> MergedGear = new Dictionary<string, (Mesh, Material[])>();
        private static readonly System.Text.StringBuilder GearKey = new System.Text.StringBuilder(512);

        /// <summary>
        /// Draw-call budget for big battles: all rigid attachments (weapon, shield, helmet, outfit, brows / beard) that
        /// hang from the same bone are merged into one mesh with one submesh per material, cached and shared by every
        /// character with the same gear (~13 renderers per character become ~5). A LODGroup then shows the full gear up
        /// close, only the body at a distance (~40 px tall) and nothing for tiny specks; gear casts no shadows.
        /// </summary>
        private static void MergeAttachments(GameObject root, Transform model)
        {
            var byParent = new Dictionary<Transform, List<MeshRenderer>>();
            foreach (MeshRenderer mr in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null || mr.transform.childCount > 0) continue;
                Transform parent = mr.transform.parent;
                if (!byParent.TryGetValue(parent, out List<MeshRenderer> list)) byParent[parent] = list = new List<MeshRenderer>();
                list.Add(mr);
            }

            var gearRenderers = new List<Renderer>();
            foreach (var pair in byParent)
            {
                Transform parent = pair.Key;
                List<MeshRenderer> parts = pair.Value;
                Matrix4x4 toParent = parent.worldToLocalMatrix;

                GearKey.Clear();
                GearKey.Append(parent.name);
                foreach (MeshRenderer mr in parts)
                {
                    Matrix4x4 m = toParent * mr.transform.localToWorldMatrix;
                    GearKey.Append('|').Append(mr.GetComponent<MeshFilter>().sharedMesh.GetInstanceID()).Append(':').Append(mr.sharedMaterial != null ? mr.sharedMaterial.GetInstanceID() : 0);
                    for (int i = 0; i < 12; i++) GearKey.Append(',').Append(Mathf.RoundToInt(m[i] * 1000f));
                }
                string key = GearKey.ToString();

                if (!MergedGear.TryGetValue(key, out var merged) || merged.mesh == null)
                {
                    var materials = new List<Material>();
                    var perMaterial = new List<List<CombineInstance>>();
                    foreach (MeshRenderer mr in parts)
                    {
                        int mi = materials.IndexOf(mr.sharedMaterial);
                        if (mi < 0) { mi = materials.Count; materials.Add(mr.sharedMaterial); perMaterial.Add(new List<CombineInstance>()); }
                        perMaterial[mi].Add(new CombineInstance { mesh = mr.GetComponent<MeshFilter>().sharedMesh, transform = toParent * mr.transform.localToWorldMatrix });
                    }
                    var subMeshes = new CombineInstance[materials.Count];
                    for (int i = 0; i < materials.Count; i++)
                    {
                        var sub = new Mesh();
                        sub.CombineMeshes(perMaterial[i].ToArray(), true, true);
                        subMeshes[i] = new CombineInstance { mesh = sub, transform = Matrix4x4.identity };
                    }
                    Mesh mesh = new Mesh { name = "Gear_" + parent.name };
                    mesh.CombineMeshes(subMeshes, false, false);
                    mesh.RecalculateBounds();
                    foreach (CombineInstance c in subMeshes) { if (Application.isPlaying) Object.Destroy(c.mesh); else Object.DestroyImmediate(c.mesh); }
                    merged = (mesh, materials.ToArray());
                    MergedGear[key] = merged;
                }

                GameObject gear = new GameObject("Gear");
                gear.transform.SetParent(parent, false);
                gear.AddComponent<MeshFilter>().sharedMesh = merged.mesh;
                MeshRenderer renderer = gear.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = merged.materials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gearRenderers.Add(renderer);

                foreach (MeshRenderer mr in parts)
                {
                    mr.enabled = false;
                    if (Application.isPlaying) Object.Destroy(mr.gameObject); else Object.DestroyImmediate(mr.gameObject);
                }
            }

            var body = new List<Renderer>(model.GetComponentsInChildren<SkinnedMeshRenderer>(true));
            var all = new List<Renderer>(body);
            all.AddRange(gearRenderers);
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.045f, all.ToArray()),   // full gear while taller than ~4.5% of the screen
                new LOD(0.006f, body.ToArray()),  // body only for distant troops
            });
            lod.RecalculateBounds();
        }

        private static readonly Dictionary<string, Mesh> BakedCharacters = new Dictionary<string, Mesh>();
        private static Material uberMaterial;

        /// <summary>
        /// Draw-call budget for 1000-unit battles: the whole character (body submeshes + every rigid attachment) becomes
        /// ONE skinned mesh drawn with ONE shared material. Colour, surface pattern, metallic and smoothness of each part
        /// move into the vertices (Custom/CharacterSurface _VertexSurface); attachments are skinned rigidly to their bone.
        /// Meshes are cached per body + gear + materials, so identically equipped soldiers share one mesh.
        /// Returns false (nothing changed) when the body mesh is not readable.
        /// </summary>
        private static bool BakeCharacter(GameObject root, Transform model)
        {
            SkinnedMeshRenderer body = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (body == null || body.sharedMesh == null || !body.sharedMesh.isReadable) return false;
            Shader shader = Shader.Find("Custom/CharacterSurface");
            if (shader == null) return false;

            Mesh src = body.sharedMesh;
            Transform[] bones = body.bones;
            Matrix4x4[] bindposes = src.bindposes;
            Material[] bodyMats = body.sharedMaterials;
            var parts = new List<MeshRenderer>();
            foreach (MeshRenderer mr in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) parts.Add(mr);
            }

            // Each part rides on its nearest ancestor bone
            var partBone = new int[parts.Count];
            var partToBone = new Matrix4x4[parts.Count];
            for (int i = 0; i < parts.Count; i++)
            {
                int b = -1;
                for (Transform t = parts[i].transform.parent; t != null && b < 0; t = t.parent) b = System.Array.IndexOf(bones, t);
                partBone[i] = Mathf.Max(b, 0);
                partToBone[i] = bones[partBone[i]].worldToLocalMatrix * parts[i].transform.localToWorldMatrix;
            }

            GearKey.Clear();
            GearKey.Append(src.GetInstanceID());
            foreach (Material m in bodyMats) GearKey.Append('/').Append(m != null ? m.GetInstanceID() : 0);
            for (int i = 0; i < parts.Count; i++)
            {
                GearKey.Append('|').Append(partBone[i]).Append(':').Append(parts[i].GetComponent<MeshFilter>().sharedMesh.GetInstanceID())
                       .Append(':').Append(parts[i].sharedMaterial != null ? parts[i].sharedMaterial.GetInstanceID() : 0);
                for (int k = 0; k < 12; k++) GearKey.Append(',').Append(Mathf.RoundToInt(partToBone[i][k] * 1000f));
            }
            string key = GearKey.ToString();

            if (!BakedCharacters.TryGetValue(key, out Mesh mesh) || mesh == null)
            {
                bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
                var verts = new List<Vector3>(); var normals = new List<Vector3>(); var colors = new List<Color>();
                var surface = new List<Vector3>(); var rest = new List<Vector3>(); var weights = new List<BoneWeight>(); var tris = new List<int>();

                // Body: same vertices and weights, per-submesh surface
                src.GetVertices(verts); src.GetNormals(normals);
                var srcRest = new List<Vector3>(); src.GetUVs(3, srcRest);
                if (srcRest.Count != verts.Count) srcRest = new List<Vector3>(verts);
                rest.AddRange(srcRest);
                weights.AddRange(src.boneWeights);
                var vColor = new Color[verts.Count]; var vSurf = new Vector3[verts.Count];
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    SurfaceOf(s < bodyMats.Length ? bodyMats[s] : null, out Color c, out Vector3 surf, out float scale);
                    int[] sub = src.GetTriangles(s);
                    foreach (int v in sub) { vColor[v] = linear ? c.linear : c; vSurf[v] = surf; }
                    tris.AddRange(sub);
                }
                colors.AddRange(vColor); surface.AddRange(vSurf);

                // Attachments: into mesh space through the bone's bind pose, rigidly weighted to that bone
                Matrix4x4 restFrame = root.transform.worldToLocalMatrix; // rest positions in metres, like the body's
                for (int i = 0; i < parts.Count; i++)
                {
                    Mesh pm = parts[i].GetComponent<MeshFilter>().sharedMesh;
                    Matrix4x4 toMesh = bindposes[partBone[i]].inverse * partToBone[i];
                    Matrix4x4 toMeshN = toMesh.inverse.transpose;
                    Matrix4x4 toRest = restFrame * parts[i].transform.localToWorldMatrix;
                    SurfaceOf(parts[i].sharedMaterial, out Color c, out Vector3 surf, out float scale);
                    if (linear) c = c.linear;
                    int baseIndex = verts.Count;
                    Vector3[] pv = pm.vertices, pn = pm.normals;
                    for (int v = 0; v < pv.Length; v++)
                    {
                        verts.Add(toMesh.MultiplyPoint3x4(pv[v]));
                        normals.Add(pn.Length == pv.Length ? toMeshN.MultiplyVector(pn[v]).normalized : Vector3.up);
                        colors.Add(c); surface.Add(surf);
                        rest.Add(toRest.MultiplyPoint3x4(pv[v]) * scale);
                        weights.Add(new BoneWeight { boneIndex0 = partBone[i], weight0 = 1f });
                    }
                    bool flip = toMesh.determinant < 0f; // mirrored parts keep their faces outward
                    for (int s = 0; s < pm.subMeshCount; s++)
                    {
                        int[] pt = pm.GetTriangles(s);
                        for (int t = 0; t < pt.Length; t += 3)
                        {
                            tris.Add(baseIndex + pt[t]);
                            tris.Add(baseIndex + (flip ? pt[t + 2] : pt[t + 1]));
                            tris.Add(baseIndex + (flip ? pt[t + 1] : pt[t + 2]));
                        }
                    }
                }

                mesh = new Mesh { name = "Character_" + src.name, indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetColors(colors);
                mesh.SetUVs(2, surface); mesh.SetUVs(3, rest);
                mesh.SetTriangles(tris, 0);
                mesh.boneWeights = weights.ToArray();
                mesh.bindposes = bindposes;
                mesh.RecalculateBounds();
                BakedCharacters[key] = mesh;
            }

            if (uberMaterial == null)
            {
                uberMaterial = new Material(shader) { name = "MAT_CharacterBaked", enableInstancing = true };
                uberMaterial.SetColor("_BaseColor", Color.white);
                uberMaterial.SetFloat("_VertexSurface", 1f);
                uberMaterial.SetFloat("_ObjectSpace", 0f);
            }

            Bounds bounds = body.localBounds;
            bounds.Expand(0.6f); // weapon / shield reach beyond the body
            body.sharedMesh = mesh;
            body.sharedMaterials = new[] { uberMaterial };
            body.localBounds = bounds;
            body.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            foreach (MeshRenderer mr in parts)
            {
                mr.enabled = false;
                if (Application.isPlaying) Object.Destroy(mr.gameObject); else Object.DestroyImmediate(mr.gameObject);
            }

            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.006f, new Renderer[] { body }) }); // culled when a speck on screen
            lod.RecalculateBounds();
            return true;
        }

        /// <summary>Colour + (pattern, metallic, smoothness) of a part's material; pattern scale for its rest coordinates.</summary>
        private static void SurfaceOf(Material m, out Color color, out Vector3 surface, out float patternScale)
        {
            color = Color.gray; surface = new Vector3(1f, 0f, 0.3f); patternScale = 1f;
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) color = m.GetColor("_BaseColor");
            float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            float smoothness = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.3f;
            float pattern;
            if (m.HasProperty("_Pattern") && m.shader != null && m.shader.name == "Custom/CharacterSurface")
            {
                pattern = m.GetFloat("_Pattern");
                if (m.HasProperty("_PatternScale")) patternScale = m.GetFloat("_PatternScale");
            }
            else
            {
                // ProceduralVisualMaterials ("MAT_<Surface>_...") on URP Lit: nearest procedural pattern
                string n = m.name;
                pattern = n.StartsWith("MAT_Gold") || n.StartsWith("MAT_Metal") ? 3f
                        : n.StartsWith("MAT_Leather") || n.StartsWith("MAT_Wood") ? 2f : 1f;
            }
            surface = new Vector3(pattern, metallic, smoothness);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
