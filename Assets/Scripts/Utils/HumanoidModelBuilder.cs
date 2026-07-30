using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Creates compact, readable RTS characters from low-poly primitives. The layered
    /// silhouettes make classes identifiable from above while remaining inexpensive.
    /// </summary>
    public static class HumanoidModelBuilder
    {
        public enum HumanType
        {
            HeroPlayer,
            KnightGuard,
            Villager
        }

        public static GameObject CreateHumanoidModel(HumanType type, string name = "HumanoidModel")
        {
            GameObject root = new GameObject(name);

            CharacterPalette palette = CharacterPalette.For(type);
            Material cloth = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Cloth);
            Material accent = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Accent);
            Material skin = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Skin, 0f, 0.28f);
            Material leather = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Leather, 0f, 0.25f);
            Material metal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.Metal, 0.75f, 0.62f);
            Material darkMetal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.DarkMetal, 0.62f, 0.46f);

            // Boots and legs establish a strong ground contact silhouette.
            CreatePart(PrimitiveType.Cylinder, "LeftLeg", root.transform, new Vector3(-0.18f, 0.43f, 0f), new Vector3(0.18f, 0.42f, 0.18f), Quaternion.identity, cloth);
            CreatePart(PrimitiveType.Cylinder, "RightLeg", root.transform, new Vector3(0.18f, 0.43f, 0f), new Vector3(0.18f, 0.42f, 0.18f), Quaternion.identity, cloth);
            CreatePart(PrimitiveType.Cube, "LeftBoot", root.transform, new Vector3(-0.18f, 0.08f, 0.08f), new Vector3(0.25f, 0.17f, 0.38f), Quaternion.identity, leather);
            CreatePart(PrimitiveType.Cube, "RightBoot", root.transform, new Vector3(0.18f, 0.08f, 0.08f), new Vector3(0.25f, 0.17f, 0.38f), Quaternion.identity, leather);

            // A tunic and a separate belt add depth without expensive meshes.
            CreatePart(PrimitiveType.Cube, "Tunic", root.transform, new Vector3(0f, 1.12f, 0f), new Vector3(0.63f, 0.68f, 0.38f), Quaternion.identity, cloth);
            CreatePart(PrimitiveType.Cube, "Belt", root.transform, new Vector3(0f, 0.88f, 0.02f), new Vector3(0.67f, 0.10f, 0.42f), Quaternion.identity, leather);
            CreatePart(PrimitiveType.Cube, "BeltBuckle", root.transform, new Vector3(0f, 0.88f, 0.235f), new Vector3(0.12f, 0.13f, 0.035f), Quaternion.identity, metal);

            // Shoulders, arms and hands.
            CreatePart(PrimitiveType.Sphere, "LeftShoulder", root.transform, new Vector3(-0.42f, 1.36f, 0f), new Vector3(0.25f, 0.22f, 0.24f), Quaternion.identity, type == HumanType.Villager ? cloth : metal);
            CreatePart(PrimitiveType.Sphere, "RightShoulder", root.transform, new Vector3(0.42f, 1.36f, 0f), new Vector3(0.25f, 0.22f, 0.24f), Quaternion.identity, type == HumanType.Villager ? cloth : metal);
            Transform leftArm = CreatePart(PrimitiveType.Cylinder, "LeftArm", root.transform, new Vector3(-0.45f, 1.04f, 0f), new Vector3(0.16f, 0.36f, 0.16f), Quaternion.Euler(0f, 0f, -6f), cloth).transform;
            Transform rightArm = CreatePart(PrimitiveType.Cylinder, "RightArm", root.transform, new Vector3(0.45f, 1.04f, 0f), new Vector3(0.16f, 0.36f, 0.16f), Quaternion.Euler(0f, 0f, 6f), cloth).transform;
            CreatePart(PrimitiveType.Sphere, "LeftHand", root.transform, new Vector3(-0.49f, 0.70f, 0.02f), new Vector3(0.18f, 0.18f, 0.18f), Quaternion.identity, skin);
            CreatePart(PrimitiveType.Sphere, "RightHand", root.transform, new Vector3(0.49f, 0.70f, 0.02f), new Vector3(0.18f, 0.18f, 0.18f), Quaternion.identity, skin);

            // Head remains separate from headgear so villagers retain a friendly face.
            CreatePart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 1.78f, 0f), new Vector3(0.40f, 0.44f, 0.39f), Quaternion.identity, skin);
            if (type == HumanType.Villager)
            {
                CreatePart(PrimitiveType.Cylinder, "WoolCap", root.transform, new Vector3(0f, 2.04f, 0f), new Vector3(0.42f, 0.11f, 0.42f), Quaternion.identity, accent);
                CreatePart(PrimitiveType.Sphere, "Nose", root.transform, new Vector3(0f, 1.75f, 0.36f), new Vector3(0.08f, 0.09f, 0.07f), Quaternion.identity, skin);
                CreatePart(PrimitiveType.Cube, "Pack", root.transform, new Vector3(0f, 1.12f, -0.27f), new Vector3(0.45f, 0.52f, 0.15f), Quaternion.identity, leather);
                CreateTool(rightArm, metal, leather);
            }
            else
            {
                CreatePart(PrimitiveType.Sphere, "Helmet", root.transform, new Vector3(0f, 1.84f, -0.02f), new Vector3(0.44f, 0.49f, 0.43f), Quaternion.identity, metal);
                CreatePart(PrimitiveType.Cube, "HelmetVisor", root.transform, new Vector3(0f, 1.79f, 0.37f), new Vector3(0.58f, 0.15f, 0.09f), Quaternion.identity, darkMetal);
                CreatePart(PrimitiveType.Cylinder, "HelmetCrest", root.transform, new Vector3(0f, 2.28f, 0f), new Vector3(0.08f, 0.20f, 0.08f), Quaternion.identity, accent);
                CreatePart(PrimitiveType.Cube, "ChestPlate", root.transform, new Vector3(0f, 1.18f, 0.21f), new Vector3(0.52f, 0.44f, 0.06f), Quaternion.identity, metal);
                CreatePart(PrimitiveType.Cube, "Tabard", root.transform, new Vector3(0f, 1.02f, 0.245f), new Vector3(0.34f, 0.48f, 0.03f), Quaternion.identity, accent);
                CreateSword(rightArm, metal, leather);
                CreateShield(leftArm, accent, metal);
            }

            if (type == HumanType.HeroPlayer)
            {
                CreatePart(PrimitiveType.Cube, "HeroCape", root.transform, new Vector3(0f, 1.18f, -0.25f), new Vector3(0.62f, 1.05f, 0.055f), Quaternion.Euler(8f, 0f, 0f), accent);
                CreatePart(PrimitiveType.Cube, "CapeClasp", root.transform, new Vector3(0f, 1.45f, -0.28f), new Vector3(0.22f, 0.12f, 0.05f), Quaternion.identity, metal);
            }

            return root;
        }

        /// <summary>
        /// Creates an arms-only model for the local first-person camera. It intentionally
        /// has no head, torso or colliders, so it cannot clip into the world-body camera.
        /// </summary>
        public static GameObject CreateViewmodelArms(string name = "ViewmodelArms")
        {
            GameObject root = new GameObject(name);
            CharacterPalette palette = CharacterPalette.For(HumanType.HeroPlayer);

            Material sleeve = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Cloth);
            Material skin = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Skin, 0f, 0.28f);
            Material leather = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Leather, palette.Leather, 0f, 0.25f);
            Material metal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.Metal, 0.75f, 0.62f);
            Material darkMetal = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, palette.DarkMetal, 0.62f, 0.46f);
            Material accent = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, palette.Accent);

            // Camera space: +Z is forward, +Y is up, +X is right.
            // Hands positioned in lower left (-X) and lower right (+X) out of the center view.

            // --- RIGHT ARM & SWORD (Main Weapon) ---
            Transform rightHand = CreatePart(
                PrimitiveType.Sphere, "ViewmodelRightHand", root.transform,
                new Vector3(0.32f, -0.32f, 0.62f), new Vector3(0.13f, 0.13f, 0.13f),
                Quaternion.Euler(15f, -15f, 20f), skin).transform;

            CreatePart(
                PrimitiveType.Cylinder, "ViewmodelRightSleeve", root.transform,
                new Vector3(0.40f, -0.46f, 0.46f), new Vector3(0.11f, 0.30f, 0.11f),
                Quaternion.Euler(45f, -22f, -10f), sleeve);

            CreatePart(
                PrimitiveType.Cylinder, "ViewmodelRightBracer", root.transform,
                new Vector3(0.36f, -0.38f, 0.54f), new Vector3(0.13f, 0.16f, 0.13f),
                Quaternion.Euler(45f, -22f, -10f), leather);

            // Sword held upright pointing up and forward into view
            Transform swordGrip = CreatePart(
                PrimitiveType.Cylinder, "ViewmodelSwordGrip", rightHand,
                new Vector3(0.01f, 0.04f, 0.06f), new Vector3(0.045f, 0.20f, 0.045f),
                Quaternion.Euler(55f, -15f, -25f), leather).transform;

            CreatePart(
                PrimitiveType.Cube, "ViewmodelSwordGuard", swordGrip,
                new Vector3(0f, 0.11f, 0f), new Vector3(0.30f, 0.045f, 0.06f),
                Quaternion.Euler(0f, 0f, 90f), metal);

            CreatePart(
                PrimitiveType.Cube, "ViewmodelSwordBlade", swordGrip,
                new Vector3(0f, 0.48f, 0f), new Vector3(0.065f, 0.72f, 0.025f),
                Quaternion.identity, metal);

            CreatePart(
                PrimitiveType.Cube, "ViewmodelSwordFuller", swordGrip,
                new Vector3(0f, 0.48f, 0f), new Vector3(0.025f, 0.68f, 0.032f),
                Quaternion.identity, darkMetal);

            // --- LEFT ARM & OFFHAND BUCKLER SHIELD ---
            Transform leftHand = CreatePart(
                PrimitiveType.Sphere, "ViewmodelLeftHand", root.transform,
                new Vector3(-0.32f, -0.32f, 0.62f), new Vector3(0.13f, 0.13f, 0.13f),
                Quaternion.Euler(15f, 15f, -20f), skin).transform;

            CreatePart(
                PrimitiveType.Cylinder, "ViewmodelLeftSleeve", root.transform,
                new Vector3(-0.40f, -0.46f, 0.46f), new Vector3(0.11f, 0.30f, 0.11f),
                Quaternion.Euler(45f, 22f, 10f), sleeve);

            CreatePart(
                PrimitiveType.Cylinder, "ViewmodelLeftBracer", root.transform,
                new Vector3(-0.36f, -0.38f, 0.54f), new Vector3(0.13f, 0.16f, 0.13f),
                Quaternion.Euler(45f, 22f, 10f), leather);

            CreatePart(
                PrimitiveType.Cylinder, "ViewmodelLeftShieldRim", leftHand,
                new Vector3(-0.02f, 0.02f, 0.08f), new Vector3(0.32f, 0.035f, 0.32f),
                Quaternion.Euler(85f, 20f, -10f), metal);

            CreatePart(
                PrimitiveType.Sphere, "ViewmodelLeftShieldBoss", leftHand,
                new Vector3(-0.02f, 0.02f, 0.10f), new Vector3(0.12f, 0.12f, 0.12f),
                Quaternion.identity, accent);

            return root;
        }

        private static void CreateSword(Transform handParent, Material blade, Material grip)
        {
            CreatePart(PrimitiveType.Cube, "SwordBlade", handParent, new Vector3(0f, -0.61f, 0.13f), new Vector3(0.10f, 0.78f, 0.06f), Quaternion.Euler(8f, 0f, 0f), blade);
            CreatePart(PrimitiveType.Cube, "SwordGuard", handParent, new Vector3(0f, -0.22f, 0.10f), new Vector3(0.34f, 0.06f, 0.08f), Quaternion.Euler(8f, 0f, 0f), grip);
        }

        private static void CreateShield(Transform armParent, Material face, Material rim)
        {
            CreatePart(PrimitiveType.Cylinder, "ShieldRim", armParent, new Vector3(0f, -0.28f, 0.23f), new Vector3(0.40f, 0.08f, 0.40f), Quaternion.Euler(90f, 0f, 0f), rim);
            CreatePart(PrimitiveType.Cylinder, "ShieldFace", armParent, new Vector3(0f, -0.28f, 0.27f), new Vector3(0.34f, 0.09f, 0.34f), Quaternion.Euler(90f, 0f, 0f), face);
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

            public static CharacterPalette For(HumanType type)
            {
                switch (type)
                {
                    case HumanType.HeroPlayer:
                        return new CharacterPalette
                        {
                            Cloth = new Color(0.075f, 0.19f, 0.48f), Accent = new Color(0.90f, 0.60f, 0.12f),
                            Skin = new Color(0.90f, 0.59f, 0.43f), Leather = new Color(0.23f, 0.11f, 0.045f),
                            Metal = new Color(0.52f, 0.58f, 0.66f), DarkMetal = new Color(0.10f, 0.13f, 0.19f)
                        };
                    case HumanType.KnightGuard:
                        return new CharacterPalette
                        {
                            Cloth = new Color(0.38f, 0.055f, 0.045f), Accent = new Color(0.72f, 0.14f, 0.08f),
                            Skin = new Color(0.82f, 0.50f, 0.34f), Leather = new Color(0.18f, 0.09f, 0.04f),
                            Metal = new Color(0.55f, 0.57f, 0.61f), DarkMetal = new Color(0.14f, 0.15f, 0.18f)
                        };
                    default:
                        return new CharacterPalette
                        {
                            Cloth = new Color(0.19f, 0.38f, 0.14f), Accent = new Color(0.65f, 0.49f, 0.20f),
                            Skin = new Color(0.86f, 0.54f, 0.37f), Leather = new Color(0.30f, 0.15f, 0.055f),
                            Metal = new Color(0.40f, 0.43f, 0.43f), DarkMetal = new Color(0.16f, 0.17f, 0.16f)
                        };
                }
            }
        }
    }
}
