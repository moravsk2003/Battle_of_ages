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
            FitToHeight(root.transform, model.transform);

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            ApplyLook(model, type);
            AttachEquipment(model.transform, type);
            AttachHelmet(model.transform, type);
            AttachOutfit(model.transform, type);

            foreach (SkinnedMeshRenderer smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                smr.updateWhenOffscreen = false;
            }

            root.AddComponent<CharacterAnimationDriver>();
            return root;
        }

        public static bool IsKnight(HumanoidModelBuilder.HumanType type) =>
            type == HumanoidModelBuilder.HumanType.HeroPlayer ||
            type == HumanoidModelBuilder.HumanType.KnightGuard ||
            type == HumanoidModelBuilder.HumanType.Warlord;

        /// <summary>Scales the rig so the top of the head is at HeadTopHeight and moves the lowest bone to y = 0.</summary>
        private static void FitToHeight(Transform root, Transform model)
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
                        m = new Material(mats[i]) { name = "MAT_" + key };
                        string n = mats[i].name.ToLowerInvariant();
                        if (n.Contains("armor")) Set(m, look.Primary, look.Metal, 0.55f);
                        else if (n.Contains("shirt")) Set(m, look.Primary, 0f, 0.15f);
                        else if (n.Contains("boots") || n.Contains("pants")) Set(m, look.Secondary, 0f, 0.2f);
                        else if (n.Contains("skin")) Set(m, look.Skin, 0f, 0.32f);
                        else if (n.Contains("hair") && look.Hair.a > 0f) Set(m, look.Hair, 0f, 0.25f);
                        else if (n.Contains("socks")) Set(m, Color.Lerp(look.Secondary, Color.white, 0.35f), 0f, 0.15f);
                        MaterialCache[key] = m;
                    }
                    mats[i] = m;
                }
                smr.sharedMaterials = mats;
            }
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

        /// <summary>Clean URP Lit metal (the procedural Metal surface has a dark chain-mail texture).</summary>
        private static Material PlainMetal(Color color, float metallic, float smoothness)
        {
            string key = ColorUtility.ToHtmlStringRGB(color) + metallic + smoothness;
            if (MetalCache.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MAT_Steel_" + key, enableInstancing = true };
            Set(m, color, metallic, smoothness);
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

        private static void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            GameObject p = GameObject.CreatePrimitive(type);
            p.name = name;
            p.transform.SetParent(parent, false);
            p.transform.localPosition = pos;
            p.transform.localScale = scale;
            p.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Collider c = p.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(c); else Object.DestroyImmediate(c);
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
