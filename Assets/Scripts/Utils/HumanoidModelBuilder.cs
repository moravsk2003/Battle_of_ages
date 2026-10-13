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

        public static GameObject CreateViewmodelArms(string name = "ViewmodelArms")
        {
            GameObject root = new GameObject(name);
            CharacterPalette palette = CharacterPalette.For(HumanType.HeroPlayer);

            Material sleeve = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Cloth);
            Material skin = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Skin, 0f, 0.28f);
            Material leather = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Leather, 0f, 0.25f);
            Material metal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.Metal, 0.85f, 0.72f);
            Material darkMetal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.DarkMetal, 0.70f, 0.55f);
            Material goldTrim = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, palette.Gold, 0.88f, 0.78f);
            Material accent = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Accent);

            // Right Arm & Sword
            Transform rightHand = CreatePart(PrimitiveType.Sphere, "ViewmodelRightHand", root.transform, new Vector3(0.32f, -0.32f, 0.62f), new Vector3(0.13f, 0.13f, 0.13f), Quaternion.Euler(15f, -15f, 20f), skin).transform;
            CreatePart(PrimitiveType.Cylinder, "ViewmodelRightSleeve", root.transform, new Vector3(0.40f, -0.46f, 0.46f), new Vector3(0.11f, 0.30f, 0.11f), Quaternion.Euler(45f, -22f, -10f), sleeve);
            CreatePart(PrimitiveType.Cylinder, "ViewmodelRightBracer", root.transform, new Vector3(0.36f, -0.38f, 0.54f), new Vector3(0.13f, 0.16f, 0.13f), Quaternion.Euler(45f, -22f, -10f), metal);

            Transform swordGrip = CreatePart(PrimitiveType.Cylinder, "ViewmodelSwordGrip", rightHand, new Vector3(0.01f, 0.04f, 0.06f), new Vector3(0.045f, 0.20f, 0.045f), Quaternion.Euler(55f, -15f, -25f), leather).transform;
            CreatePart(PrimitiveType.Cube, "ViewmodelSwordGuard", swordGrip, new Vector3(0f, 0.11f, 0f), new Vector3(0.30f, 0.045f, 0.06f), Quaternion.Euler(0f, 0f, 90f), goldTrim);
            CreatePart(PrimitiveType.Cube, "ViewmodelSwordBlade", swordGrip, new Vector3(0f, 0.48f, 0f), new Vector3(0.065f, 0.72f, 0.025f), Quaternion.identity, metal);
            CreatePart(PrimitiveType.Cube, "ViewmodelSwordFuller", swordGrip, new Vector3(0f, 0.48f, 0f), new Vector3(0.025f, 0.68f, 0.032f), Quaternion.identity, darkMetal);

            // Left Arm & Shield
            Transform leftHand = CreatePart(PrimitiveType.Sphere, "ViewmodelLeftHand", root.transform, new Vector3(-0.32f, -0.32f, 0.62f), new Vector3(0.13f, 0.13f, 0.13f), Quaternion.Euler(15f, 15f, -20f), skin).transform;
            CreatePart(PrimitiveType.Cylinder, "ViewmodelLeftSleeve", root.transform, new Vector3(-0.40f, -0.46f, 0.46f), new Vector3(0.11f, 0.30f, 0.11f), Quaternion.Euler(45f, 22f, 10f), sleeve);
            CreatePart(PrimitiveType.Cylinder, "ViewmodelLeftBracer", root.transform, new Vector3(-0.36f, -0.38f, 0.54f), new Vector3(0.13f, 0.16f, 0.13f), Quaternion.Euler(45f, 22f, 10f), metal);
            CreatePart(PrimitiveType.Cylinder, "ViewmodelLeftShieldRim", leftHand, new Vector3(-0.02f, 0.02f, 0.08f), new Vector3(0.32f, 0.035f, 0.32f), Quaternion.Euler(85f, 20f, -10f), goldTrim);
            CreatePart(PrimitiveType.Sphere, "ViewmodelLeftShieldBoss", leftHand, new Vector3(-0.02f, 0.02f, 0.10f), new Vector3(0.12f, 0.12f, 0.12f), Quaternion.identity, accent);

            return root;
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
