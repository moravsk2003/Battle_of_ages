using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Creates high-detail stylized fantasy characters (Hero Knight, Knight Guards, Medieval Villagers)
    /// with rich layered armor, crested helms, weapons, shields, capes, and first-person viewmodel arms.
    /// </summary>
    public static class HumanoidModelBuilder
    {
        public enum HumanType
        {
            HeroPlayer,
            KnightGuard,
            Villager,
            Bandit,
            Goblin,
            Warlord
        }

        public static GameObject CreateHumanoidModel(HumanType type, string name = "HumanoidModel")
        {
            // Prefer the rigged, animated FBX characters; fall back to the procedural primitive build.
            GameObject rigged = RiggedCharacterBuilder.TryCreate(type, name);
            if (rigged != null) return rigged;
            if (type == HumanType.Bandit || type == HumanType.Goblin) type = HumanType.Villager;
            else if (type == HumanType.Warlord) type = HumanType.KnightGuard;

            GameObject root = new GameObject(name);

            CharacterPalette palette = CharacterPalette.For(type);
            Material cloth = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Cloth);
            Material accent = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Accent);
            Material skin = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Skin, 0f, 0.28f);
            Material leather = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Leather, 0f, 0.25f);
            Material metal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.Metal, 0.85f, 0.72f);
            Material darkMetal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.DarkMetal, 0.70f, 0.55f);
            Material goldTrim = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, palette.Gold, 0.88f, 0.78f);
            Material glowEyes = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.GemSapphire, new Color(0.2f, 0.75f, 1.0f), 0.2f, 0.9f);

            // 1. Legs & Armored Boots
            Material legMat = (type == HumanType.Villager) ? cloth : darkMetal;
            Material bootMat = (type == HumanType.Villager) ? leather : metal;

            CreatePart(PrimitiveType.Cylinder, "LeftLeg", root.transform, new Vector3(-0.18f, 0.45f, 0f), new Vector3(0.16f, 0.44f, 0.16f), Quaternion.identity, legMat);
            CreatePart(PrimitiveType.Cylinder, "RightLeg", root.transform, new Vector3(0.18f, 0.45f, 0f), new Vector3(0.16f, 0.44f, 0.16f), Quaternion.identity, legMat);
            CreatePart(PrimitiveType.Cube, "LeftBoot", root.transform, new Vector3(-0.18f, 0.10f, 0.04f), new Vector3(0.22f, 0.18f, 0.32f), Quaternion.identity, bootMat);
            CreatePart(PrimitiveType.Cube, "RightBoot", root.transform, new Vector3(0.18f, 0.10f, 0.04f), new Vector3(0.22f, 0.18f, 0.32f), Quaternion.identity, bootMat);

            // 2. Torso, Tunic & Breastplate
            CreatePart(PrimitiveType.Cube, "TorsoBase", root.transform, new Vector3(0f, 1.15f, 0f), new Vector3(0.58f, 0.62f, 0.34f), Quaternion.identity, cloth);
            CreatePart(PrimitiveType.Cube, "Belt", root.transform, new Vector3(0f, 0.88f, 0f), new Vector3(0.62f, 0.11f, 0.38f), Quaternion.identity, leather);
            CreatePart(PrimitiveType.Cube, "BeltBuckle", root.transform, new Vector3(0f, 0.88f, 0.20f), new Vector3(0.14f, 0.13f, 0.05f), Quaternion.identity, goldTrim);

            // 3. Shoulders & Arms
            Material shoulderMat = (type == HumanType.Villager) ? cloth : metal;
            CreatePart(PrimitiveType.Sphere, "LeftShoulderPuldron", root.transform, new Vector3(-0.42f, 1.38f, 0f), new Vector3(0.28f, 0.25f, 0.26f), Quaternion.Euler(0f, 0f, 12f), shoulderMat);
            CreatePart(PrimitiveType.Sphere, "RightShoulderPuldron", root.transform, new Vector3(0.42f, 1.38f, 0f), new Vector3(0.28f, 0.25f, 0.26f), Quaternion.Euler(0f, 0f, -12f), shoulderMat);

            Transform leftArm = CreatePart(PrimitiveType.Cylinder, "LeftArm", root.transform, new Vector3(-0.42f, 1.05f, 0f), new Vector3(0.15f, 0.34f, 0.15f), Quaternion.Euler(0f, 0f, -8f), cloth).transform;
            Transform rightArm = CreatePart(PrimitiveType.Cylinder, "RightArm", root.transform, new Vector3(0.42f, 1.05f, 0f), new Vector3(0.15f, 0.34f, 0.15f), Quaternion.Euler(0f, 0f, 8f), cloth).transform;
            CreatePart(PrimitiveType.Sphere, "LeftHand", root.transform, new Vector3(-0.48f, 0.72f, 0.02f), new Vector3(0.16f, 0.16f, 0.16f), Quaternion.identity, type == HumanType.Villager ? skin : leather);
            CreatePart(PrimitiveType.Sphere, "RightHand", root.transform, new Vector3(0.48f, 0.72f, 0.02f), new Vector3(0.16f, 0.16f, 0.16f), Quaternion.identity, type == HumanType.Villager ? skin : leather);

            // 4. Head & Helmet
            if (type == HumanType.Villager)
            {
                CreatePart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.76f, 0f), new Vector3(0.38f, 0.42f, 0.38f), Quaternion.identity, skin);
                CreatePart(PrimitiveType.Cylinder, "WoolCap", root.transform, new Vector3(0f, 1.98f, 0f), new Vector3(0.42f, 0.12f, 0.42f), Quaternion.identity, accent);
                CreatePart(PrimitiveType.Sphere, "Nose", root.transform, new Vector3(0f, 1.74f, 0.20f), new Vector3(0.08f, 0.08f, 0.08f), Quaternion.identity, skin);
                CreatePart(PrimitiveType.Cube, "Pack", root.transform, new Vector3(0f, 1.15f, -0.24f), new Vector3(0.44f, 0.50f, 0.16f), Quaternion.identity, leather);
                CreateTool(rightArm, metal, leather);
            }
            else
            {
                // Sleek Knight Helmet
                CreatePart(PrimitiveType.Sphere, "KnightHelm", root.transform, new Vector3(0f, 1.82f, 0f), new Vector3(0.46f, 0.48f, 0.46f), Quaternion.identity, metal);
                CreatePart(PrimitiveType.Cube, "VisorSlit", root.transform, new Vector3(0f, 1.80f, 0.22f), new Vector3(0.32f, 0.055f, 0.05f), Quaternion.identity, glowEyes);
                CreatePart(PrimitiveType.Cube, "VisorGuard", root.transform, new Vector3(0f, 1.78f, 0.21f), new Vector3(0.42f, 0.14f, 0.04f), Quaternion.identity, darkMetal);

                // Golden Helmet Crest / Feather Plume
                CreatePart(PrimitiveType.Cube, "HelmetPlume", root.transform, new Vector3(0f, 2.14f, -0.06f), new Vector3(0.06f, 0.24f, 0.34f), Quaternion.Euler(-10f, 0f, 0f), goldTrim);

                // Layered Breastplate
                CreatePart(PrimitiveType.Cube, "BreastPlate", root.transform, new Vector3(0f, 1.22f, 0.14f), new Vector3(0.52f, 0.44f, 0.10f), Quaternion.identity, metal);
                CreatePart(PrimitiveType.Cube, "ChestEmblem", root.transform, new Vector3(0f, 1.25f, 0.20f), new Vector3(0.18f, 0.18f, 0.03f), Quaternion.Euler(0f, 0f, 45f), goldTrim);

                // Weapons
                CreateKnightSword(rightArm, metal, darkMetal, goldTrim);
                CreateHeaterShield(leftArm, accent, goldTrim, metal);
            }

            if (type == HumanType.HeroPlayer)
            {
                CreatePart(PrimitiveType.Cube, "HeroCape", root.transform, new Vector3(0f, 1.15f, -0.22f), new Vector3(0.64f, 1.12f, 0.05f), Quaternion.Euler(10f, 0f, 0f), accent);
                CreatePart(PrimitiveType.Sphere, "CapeClaspL", root.transform, new Vector3(-0.20f, 1.45f, -0.16f), new Vector3(0.10f, 0.10f, 0.10f), Quaternion.identity, goldTrim);
                CreatePart(PrimitiveType.Sphere, "CapeClaspR", root.transform, new Vector3(0.20f, 1.45f, -0.16f), new Vector3(0.10f, 0.10f, 0.10f), Quaternion.identity, goldTrim);
            }

            return root;
        }

        /// <summary>
        /// First-person arms (viewmodel camera space: +Z forward, +X right). Each arm hangs from a shoulder pivot just
        /// outside the view ("RightArmPivot" / "LeftArmPivot") that FpsViewmodelAnimation rotates for the sword swing.
        /// Right: sleeve, plate vambrace, leather gauntlet gripping a full sword. Left: round shield seen from behind.
        /// </summary>
        public static GameObject CreateViewmodelArms(string name = "ViewmodelArms")
        {
            GameObject root = new GameObject(name);
            CharacterPalette palette = CharacterPalette.For(HumanType.HeroPlayer);
            Material sleeve = ViewmodelSurface(palette.Cloth, 0f, 0.15f, 1);
            Material steel = ViewmodelSurface(new Color(0.74f, 0.76f, 0.80f), 0.85f, 0.65f, 3);
            Material darkSteel = ViewmodelSurface(new Color(0.30f, 0.31f, 0.34f), 0.8f, 0.5f, 3);
            Material glove = ViewmodelSurface(new Color(0.30f, 0.18f, 0.10f), 0f, 0.35f, 2);
            Material grip = ViewmodelSurface(new Color(0.18f, 0.10f, 0.06f), 0f, 0.3f, 2);
            Material gold = ViewmodelSurface(palette.Gold, 0.9f, 0.7f, 3);
            Material wood = ViewmodelSurface(new Color(0.36f, 0.24f, 0.14f), 0f, 0.2f, 2); // dark oiled planks (grain via leather pattern)
            Material shieldFace = ViewmodelSurface(palette.Cloth, 0f, 0.25f, 1);

            // ---- Right arm + sword
            Transform right = new GameObject("RightArmPivot").transform;
            right.SetParent(root.transform, false);
            right.localPosition = new Vector3(0.30f, -0.44f, 0.16f);
            Vector3 rHand = new Vector3(0.02f, 0.12f, 0.44f);
            Limb(right, "RightSleeve", Vector3.zero, rHand * 0.55f, 0.115f, sleeve);
            Limb(right, "RightVambrace", rHand * 0.45f, rHand * 0.88f, 0.105f, steel);
            CreatePart(PrimitiveType.Cylinder, "RightCuffRim", right, rHand * 0.88f, new Vector3(0.115f, 0.012f, 0.115f), Quaternion.FromToRotation(Vector3.up, rHand.normalized), darkSteel);
            CreatePart(PrimitiveType.Sphere, "RightFist", right, rHand + new Vector3(0f, 0.01f, 0.02f), new Vector3(0.10f, 0.085f, 0.11f), Quaternion.identity, glove);
            CreatePart(PrimitiveType.Capsule, "RightThumb", right, rHand + new Vector3(-0.045f, 0.03f, 0.03f), new Vector3(0.035f, 0.04f, 0.035f), Quaternion.Euler(0f, 0f, 60f), glove);

            Transform sword = new GameObject("Sword").transform;
            sword.SetParent(right, false);
            sword.localPosition = rHand + new Vector3(0f, 0.01f, 0.03f);
            sword.localRotation = Quaternion.Euler(-28f, -12f, -8f); // blade up and slightly forward, angled inwards
            CreatePart(PrimitiveType.Cylinder, "Grip", sword, Vector3.zero, new Vector3(0.032f, 0.075f, 0.032f), Quaternion.identity, grip);
            CreatePart(PrimitiveType.Sphere, "Pommel", sword, new Vector3(0f, -0.09f, 0f), Vector3.one * 0.05f, Quaternion.identity, gold);
            CreatePart(PrimitiveType.Cube, "Crossguard", sword, new Vector3(0f, 0.085f, 0f), new Vector3(0.22f, 0.026f, 0.04f), Quaternion.identity, gold);
            CreatePart(PrimitiveType.Cube, "Blade", sword, new Vector3(0f, 0.46f, 0f), new Vector3(0.052f, 0.72f, 0.011f), Quaternion.identity, steel);
            CreatePart(PrimitiveType.Cube, "Fuller", sword, new Vector3(0f, 0.42f, 0f), new Vector3(0.014f, 0.6f, 0.013f), Quaternion.identity, darkSteel);
            CreatePart(PrimitiveType.Cube, "Tip", sword, new Vector3(0f, 0.825f, 0f), new Vector3(0.037f, 0.037f, 0.0105f), Quaternion.Euler(0f, 0f, 45f), steel);

            // ---- Left arm + round shield (inner side towards the camera)
            Transform left = new GameObject("LeftArmPivot").transform;
            left.SetParent(root.transform, false);
            left.localPosition = new Vector3(-0.36f, -0.50f, 0.14f);
            Vector3 lHand = new Vector3(-0.04f, 0.10f, 0.40f);
            Limb(left, "LeftSleeve", Vector3.zero, lHand * 0.55f, 0.115f, sleeve);
            Limb(left, "LeftVambrace", lHand * 0.45f, lHand * 0.9f, 0.105f, steel);
            CreatePart(PrimitiveType.Sphere, "LeftFist", left, lHand, new Vector3(0.10f, 0.085f, 0.11f), Quaternion.identity, glove);

            Transform shield = new GameObject("Shield").transform;
            shield.SetParent(left, false);
            shield.localPosition = lHand + new Vector3(-0.08f, 0.03f, 0.06f);
            shield.localRotation = Quaternion.Euler(90f, -22f, 0f);  // face forward, angled out to the left
            CreatePart(PrimitiveType.Cylinder, "Planks", shield, Vector3.zero, new Vector3(0.46f, 0.016f, 0.46f), Quaternion.identity, wood);
            CreatePart(PrimitiveType.Cylinder, "Face", shield, new Vector3(0f, 0.018f, 0f), new Vector3(0.44f, 0.006f, 0.44f), Quaternion.identity, shieldFace);
            CreatePart(PrimitiveType.Cylinder, "Rim", shield, new Vector3(0f, 0.008f, 0f), new Vector3(0.48f, 0.016f, 0.48f), Quaternion.identity, steel); // set forward: no z-fight with the planks
            CreatePart(PrimitiveType.Cube, "StrapA", shield, new Vector3(0f, -0.02f, 0.06f), new Vector3(0.2f, 0.01f, 0.04f), Quaternion.identity, glove);
            CreatePart(PrimitiveType.Cube, "StrapB", shield, new Vector3(0f, -0.02f, -0.06f), new Vector3(0.2f, 0.01f, 0.04f), Quaternion.identity, glove);
            CreatePart(PrimitiveType.Sphere, "BossBack", shield, new Vector3(0f, -0.012f, 0f), new Vector3(0.09f, 0.025f, 0.09f), Quaternion.identity, darkSteel);

            return root;
        }

        /// <summary>A capsule segment between two points (local to parent).</summary>
        private static void Limb(Transform parent, string partName, Vector3 from, Vector3 to, float thickness, Material material)
        {
            Vector3 d = to - from;
            CreatePart(PrimitiveType.Capsule, partName, parent, (from + to) * 0.5f, new Vector3(thickness, d.magnitude * 0.5f + thickness * 0.25f, thickness),
                       Quaternion.FromToRotation(Vector3.up, d.normalized), material);
        }

        private static readonly System.Collections.Generic.Dictionary<string, Material> ViewmodelMaterials = new System.Collections.Generic.Dictionary<string, Material>();

        /// <summary>Procedural CharacterSurface material in object space (cloth 1, leather 2, metal 3).</summary>
        private static Material ViewmodelSurface(Color color, float metallic, float smoothness, int pattern)
        {
            string key = ColorUtility.ToHtmlStringRGB(color) + metallic + smoothness + pattern;
            if (ViewmodelMaterials.TryGetValue(key, out Material m) && m != null) return m;
            Shader shader = Shader.Find("Custom/CharacterSurface");
            if (shader == null) return ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, color, metallic, smoothness);
            m = new Material(shader) { name = "MAT_Viewmodel_" + key };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Pattern", pattern);
            m.SetFloat("_ObjectSpace", 1f);
            ViewmodelMaterials[key] = m;
            return m;
        }

        private static void CreateKnightSword(Transform handParent, Material blade, Material darkMetal, Material gold)
        {
            GameObject swordRoot = new GameObject("KnightSword");
            swordRoot.transform.SetParent(handParent, false);
            swordRoot.transform.localPosition = new Vector3(0f, -0.38f, 0.12f);
            swordRoot.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);

            CreatePart(PrimitiveType.Cylinder, "SwordGrip", swordRoot.transform, new Vector3(0f, -0.12f, 0f), new Vector3(0.05f, 0.14f, 0.05f), Quaternion.identity, darkMetal);
            CreatePart(PrimitiveType.Sphere, "SwordPommel", swordRoot.transform, new Vector3(0f, -0.27f, 0f), new Vector3(0.11f, 0.11f, 0.11f), Quaternion.identity, gold);
            CreatePart(PrimitiveType.Cube, "Crossguard", swordRoot.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.42f, 0.06f, 0.08f), Quaternion.identity, gold);
            CreatePart(PrimitiveType.Cube, "SteelBlade", swordRoot.transform, new Vector3(0f, 0.62f, 0f), new Vector3(0.11f, 1.15f, 0.035f), Quaternion.identity, blade);
            CreatePart(PrimitiveType.Cube, "BladeFuller", swordRoot.transform, new Vector3(0f, 0.58f, 0f), new Vector3(0.03f, 0.95f, 0.042f), Quaternion.identity, darkMetal);
        }

        private static void CreateHeaterShield(Transform armParent, Material face, Material goldTrim, Material metal)
        {
            GameObject shieldRoot = new GameObject("HeaterShield");
            shieldRoot.transform.SetParent(armParent, false);
            shieldRoot.transform.localPosition = new Vector3(0f, -0.22f, 0.24f);
            shieldRoot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            CreatePart(PrimitiveType.Cube, "ShieldBody", shieldRoot.transform, new Vector3(0f, 0f, 0f), new Vector3(0.55f, 0.06f, 0.78f), Quaternion.identity, face);
            CreatePart(PrimitiveType.Cube, "ShieldRimTop", shieldRoot.transform, new Vector3(0f, 0.02f, 0.38f), new Vector3(0.58f, 0.08f, 0.06f), Quaternion.identity, goldTrim);
            CreatePart(PrimitiveType.Sphere, "ShieldBossCenter", shieldRoot.transform, new Vector3(0f, 0.04f, 0.05f), new Vector3(0.20f, 0.10f, 0.20f), Quaternion.identity, metal);
        }

        private static void CreateTool(Transform handParent, Material blade, Material handle)
        {
            CreatePart(PrimitiveType.Cylinder, "ToolHandle", handParent, new Vector3(0f, -0.52f, 0.10f), new Vector3(0.05f, 0.58f, 0.05f), Quaternion.Euler(-25f, 0f, 0f), handle);
            CreatePart(PrimitiveType.Cube, "ToolHead", handParent, new Vector3(0f, -0.95f, 0.35f), new Vector3(0.38f, 0.10f, 0.08f), Quaternion.Euler(-25f, 0f, 0f), blade);
        }

        private static GameObject CreatePart(PrimitiveType type, string partName, Transform parent, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation;

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }

            return part;
        }

        private struct CharacterPalette
        {
            public Color Cloth;
            public Color Accent;
            public Color Skin;
            public Color Leather;
            public Color Metal;
            public Color DarkMetal;
            public Color Gold;

            public static CharacterPalette For(HumanType type)
            {
                switch (type)
                {
                    case HumanType.HeroPlayer:
                        return new CharacterPalette
                        {
                            Cloth = new Color(0.12f, 0.26f, 0.62f),
                            Accent = new Color(0.82f, 0.14f, 0.16f),
                            Skin = new Color(0.92f, 0.65f, 0.50f),
                            Leather = new Color(0.24f, 0.12f, 0.06f),
                            Metal = new Color(0.82f, 0.86f, 0.92f),
                            DarkMetal = new Color(0.18f, 0.22f, 0.28f),
                            Gold = new Color(0.98f, 0.78f, 0.18f)
                        };
                    case HumanType.KnightGuard:
                        return new CharacterPalette
                        {
                            Cloth = new Color(0.48f, 0.12f, 0.10f),
                            Accent = new Color(0.88f, 0.75f, 0.20f),
                            Skin = new Color(0.88f, 0.58f, 0.42f),
                            Leather = new Color(0.20f, 0.10f, 0.05f),
                            Metal = new Color(0.68f, 0.72f, 0.76f),
                            DarkMetal = new Color(0.16f, 0.18f, 0.22f),
                            Gold = new Color(0.92f, 0.72f, 0.15f)
                        };
                    default:
                        return new CharacterPalette
                        {
                            Cloth = new Color(0.25f, 0.45f, 0.20f),
                            Accent = new Color(0.72f, 0.55f, 0.22f),
                            Skin = new Color(0.90f, 0.60f, 0.45f),
                            Leather = new Color(0.32f, 0.18f, 0.08f),
                            Metal = new Color(0.45f, 0.48f, 0.50f),
                            DarkMetal = new Color(0.18f, 0.19f, 0.20f),
                            Gold = new Color(0.80f, 0.65f, 0.20f)
                        };
                }
            }
        }
    }
}
