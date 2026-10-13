using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Procedural 3D Model and Prefab Builder for Wildlife (Deer, Boar, Rabbit).
    /// Generates stylized low-poly animal anatomy with realistic proportions.
    /// </summary>
    public static class AnimalModelBuilder
    {
        public static GameObject CreateAnimalPrefab(AnimalType type, string name = "Animal")
        {
            GameObject root = new GameObject(name);

            CharacterController charCtrl = root.AddComponent<CharacterController>();
            charCtrl.height = (type == AnimalType.Rabbit) ? 0.8f : 1.6f;
            charCtrl.radius = (type == AnimalType.Rabbit) ? 0.35f : 0.6f;
            charCtrl.center = new Vector3(0f, charCtrl.height * 0.5f, 0f);
            charCtrl.stepOffset = 0.4f;

            HealthComponent hp = root.AddComponent<HealthComponent>();
            int maxHp = (type == AnimalType.Boar) ? 120 : (type == AnimalType.Deer ? 80 : 30);
            hp.InitializeHealth(maxHp);

            PeacefulAnimalAI ai = root.AddComponent<PeacefulAnimalAI>();

            GameObject visualModel = new GameObject("VisualModel");
            visualModel.transform.SetParent(root.transform, false);

            switch (type)
            {
                case AnimalType.Deer:
                    BuildDeerVisuals(visualModel.transform);
                    break;
                case AnimalType.Boar:
                    BuildBoarVisuals(visualModel.transform);
                    break;
                case AnimalType.Rabbit:
                    BuildRabbitVisuals(visualModel.transform);
                    break;
            }

            // Applies per-type stats and caches the VisualModel/Head nodes, which did not exist yet
            // when the AI's Awake ran (otherwise every animal behaved as a Deer and was never animated).
            ai.InitializeAnimal(type);

            return root;
        }

        private static void BuildDeerVisuals(Transform root)
        {
            Material deerFur = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.68f, 0.42f, 0.22f), 0f, 0.25f);
            Material bellyFur = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Foliage, new Color(0.92f, 0.86f, 0.74f), 0f, 0.18f);
            Material antlerMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, new Color(0.35f, 0.25f, 0.18f), 0f, 0.20f);
            Material darkMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.10f, 0.08f, 0.06f), 0f, 0.12f);

            // 1. Torso
            GameObject body = CreatePart(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 1.05f, 0f), new Vector3(0.58f, 0.75f, 0.58f), Quaternion.Euler(90f, 0f, 0f), deerFur);

            // 2. Neck & Head
            GameObject neck = CreatePart(PrimitiveType.Cylinder, "Neck", root, new Vector3(0f, 1.45f, 0.62f), new Vector3(0.24f, 0.42f, 0.24f), Quaternion.Euler(38f, 0f, 0f), deerFur);
            GameObject head = CreatePart(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 1.78f, 0.95f), new Vector3(0.34f, 0.32f, 0.48f), Quaternion.identity, deerFur);
            GameObject muzzle = CreatePart(PrimitiveType.Cube, "Muzzle", root, new Vector3(0f, 1.68f, 1.20f), new Vector3(0.20f, 0.16f, 0.25f), Quaternion.identity, bellyFur);
            GameObject nose = CreatePart(PrimitiveType.Sphere, "Nose", root, new Vector3(0f, 1.72f, 1.34f), new Vector3(0.10f, 0.08f, 0.08f), Quaternion.identity, darkMat);

            // 3. Antlers (Majestic branching antlers)
            for (int i = -1; i <= 1; i += 2)
            {
                CreatePart(PrimitiveType.Cylinder, $"AntlerMain_{i}", root, new Vector3(i * 0.18f, 2.05f, 0.92f), new Vector3(0.045f, 0.35f, 0.045f), Quaternion.Euler(20f, i * 25f, i * 35f), antlerMat);
                CreatePart(PrimitiveType.Cylinder, $"AntlerBranch1_{i}", root, new Vector3(i * 0.28f, 2.22f, 0.98f), new Vector3(0.035f, 0.22f, 0.035f), Quaternion.Euler(45f, i * 40f, i * 15f), antlerMat);
                CreatePart(PrimitiveType.Cylinder, $"AntlerBranch2_{i}", root, new Vector3(i * 0.35f, 2.38f, 0.88f), new Vector3(0.030f, 0.18f, 0.030f), Quaternion.Euler(-10f, i * 20f, i * 50f), antlerMat);
            }

            // 4. Ears
            for (int i = -1; i <= 1; i += 2)
            {
                CreatePart(PrimitiveType.Capsule, $"Ear_{i}", root, new Vector3(i * 0.22f, 1.90f, 0.80f), new Vector3(0.08f, 0.18f, 0.06f), Quaternion.Euler(30f, i * 45f, i * 45f), deerFur);
            }

            // 5. 4 Slender Legs
            Vector3[] legOffsets = new Vector3[]
            {
                new Vector3(-0.24f, 0.52f, 0.55f),
                new Vector3(0.24f, 0.52f, 0.55f),
                new Vector3(-0.24f, 0.52f, -0.55f),
                new Vector3(0.24f, 0.52f, -0.55f)
            };

            for (int i = 0; i < 4; i++)
            {
                CreatePart(PrimitiveType.Cylinder, $"Leg_{i}", root, legOffsets[i], new Vector3(0.12f, 0.52f, 0.12f), Quaternion.identity, deerFur);
                CreatePart(PrimitiveType.Cube, $"Hoof_{i}", root, legOffsets[i] - new Vector3(0, 0.48f, 0), new Vector3(0.13f, 0.08f, 0.15f), Quaternion.identity, darkMat);
            }

            // 6. Tail
            CreatePart(PrimitiveType.Sphere, "Tail", root, new Vector3(0f, 1.15f, -0.72f), new Vector3(0.12f, 0.18f, 0.12f), Quaternion.Euler(-30f, 0, 0), bellyFur);
        }

        private static void BuildBoarVisuals(Transform root)
        {
            Material boarFur = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, new Color(0.25f, 0.18f, 0.12f), 0f, 0.20f);
            Material snoutMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.38f, 0.26f, 0.20f), 0f, 0.22f);
            Material tuskMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.95f, 0.93f, 0.85f), 0.2f, 0.35f);
            Material darkMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.08f, 0.06f, 0.05f), 0f, 0.10f);

            // 1. Heavy Stocky Body
            GameObject body = CreatePart(PrimitiveType.Capsule, "Body", root, new Vector3(0f, 0.80f, 0f), new Vector3(0.75f, 0.78f, 0.75f), Quaternion.Euler(90f, 0f, 0f), boarFur);

            // 2. Powerful Wedge Head & Snout
            GameObject head = CreatePart(PrimitiveType.Cube, "Head", root, new Vector3(0f, 0.88f, 0.72f), new Vector3(0.55f, 0.55f, 0.65f), Quaternion.Euler(15f, 0f, 0f), boarFur);
            GameObject snout = CreatePart(PrimitiveType.Cylinder, "Snout", root, new Vector3(0f, 0.72f, 1.12f), new Vector3(0.26f, 0.12f, 0.26f), Quaternion.Euler(90f, 0f, 0f), snoutMat);

            // 3. Curved Tusks
            for (int i = -1; i <= 1; i += 2)
            {
                CreatePart(PrimitiveType.Cylinder, $"Tusk_{i}", root, new Vector3(i * 0.24f, 0.78f, 1.05f), new Vector3(0.05f, 0.16f, 0.05f), Quaternion.Euler(-35f, i * 20f, i * 40f), tuskMat);
            }

            // 4. Bristle Spine Ridge
            GameObject ridge = CreatePart(PrimitiveType.Cube, "BristleRidge", root, new Vector3(0f, 1.18f, 0.1f), new Vector3(0.12f, 0.18f, 0.95f), Quaternion.identity, darkMat);

            // 5. 4 Short Strong Legs
            Vector3[] legOffsets = new Vector3[]
            {
                new Vector3(-0.32f, 0.36f, 0.50f),
                new Vector3(0.32f, 0.36f, 0.50f),
                new Vector3(-0.32f, 0.36f, -0.50f),
                new Vector3(0.32f, 0.36f, -0.50f)
            };

            for (int i = 0; i < 4; i++)
            {
                CreatePart(PrimitiveType.Cylinder, $"Leg_{i}", root, legOffsets[i], new Vector3(0.18f, 0.36f, 0.18f), Quaternion.identity, boarFur);
            }
        }

        private static void BuildRabbitVisuals(Transform root)
        {
            Material furMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, new Color(0.88f, 0.85f, 0.80f), 0f, 0.15f);
            Material earInner = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.95f, 0.65f, 0.65f), 0f, 0.18f);
            Material eyeMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.12f, 0.08f, 0.08f), 0.5f, 0.6f);

            // 1. Compact Furry Body
            GameObject body = CreatePart(PrimitiveType.Sphere, "Body", root, new Vector3(0f, 0.32f, 0f), new Vector3(0.42f, 0.36f, 0.52f), Quaternion.identity, furMat);

            // 2. Head
            GameObject head = CreatePart(PrimitiveType.Sphere, "Head", root, new Vector3(0f, 0.48f, 0.32f), new Vector3(0.30f, 0.28f, 0.32f), Quaternion.identity, furMat);

            // 3. Long Upright Ears
            for (int i = -1; i <= 1; i += 2)
            {
                CreatePart(PrimitiveType.Capsule, $"Ear_{i}", root, new Vector3(i * 0.10f, 0.72f, 0.28f), new Vector3(0.07f, 0.26f, 0.04f), Quaternion.Euler(-10f, i * 15f, i * 12f), furMat);
                CreatePart(PrimitiveType.Capsule, $"EarInner_{i}", root, new Vector3(i * 0.10f, 0.72f, 0.30f), new Vector3(0.04f, 0.20f, 0.02f), Quaternion.Euler(-10f, i * 15f, i * 12f), earInner);
            }

            // 4. Eyes & Pink Nose
            for (int i = -1; i <= 1; i += 2)
            {
                CreatePart(PrimitiveType.Sphere, $"Eye_{i}", root, new Vector3(i * 0.14f, 0.52f, 0.42f), new Vector3(0.06f, 0.06f, 0.06f), Quaternion.identity, eyeMat);
            }
            CreatePart(PrimitiveType.Sphere, "Nose", root, new Vector3(0f, 0.44f, 0.48f), new Vector3(0.04f, 0.04f, 0.04f), Quaternion.identity, earInner);

            // 5. Fluffy Tail
            CreatePart(PrimitiveType.Sphere, "Tail", root, new Vector3(0f, 0.36f, -0.30f), new Vector3(0.14f, 0.14f, 0.14f), Quaternion.identity, furMat);

            // 6. Paws
            Vector3[] paws = new Vector3[]
            {
                new Vector3(-0.14f, 0.10f, 0.20f),
                new Vector3(0.14f, 0.10f, 0.20f),
                new Vector3(-0.16f, 0.12f, -0.15f),
                new Vector3(0.16f, 0.12f, -0.15f)
            };
            for (int i = 0; i < 4; i++)
            {
                CreatePart(PrimitiveType.Sphere, $"Paw_{i}", root, paws[i], new Vector3(0.12f, 0.10f, 0.18f), Quaternion.identity, furMat);
            }
        }

        private static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;

            Collider col = part.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }

            return part;
        }
    }
}
