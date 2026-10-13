using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Procedural wildlife (Deer, Boar, Rabbit) built as a small joint hierarchy that PeacefulAnimalAI animates:
    /// VisualModel / LegPivot_0..3 (hip / shoulder) / Knee_0..3, NeckPivot / Head, TailPivot.
    /// Leg order: 0 front-left, 1 front-right, 2 hind-left, 3 hind-right. Animals face +Z.
    /// Flat-coloured low-poly materials match the rigged character style.
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
                    DeerModelBuilder.Build(visualModel.transform); // sculpted directly, no blockout
                    break;
                case AnimalType.Boar:
                    BoarModelBuilder.Build(visualModel.transform);
                    break;
                case AnimalType.Rabbit:
                    BuildRabbitVisuals(visualModel.transform);
                    break;
            }

            // Applies per-type stats and caches the VisualModel/Head nodes, which did not exist yet
            // when the AI's Awake ran (otherwise every animal behaved as a Deer and was never animated).
            // Melt the rabbit blockout into one smooth skinned body (cell size and blend radius).
            if (type == AnimalType.Rabbit) AnimalSculptor.Sculpt(visualModel.transform, "Rabbit", 0.012f, 0.03f);

            ai.InitializeAnimal(type);

            return root;
        }

        // ------------------------------------------------------------------ species

        private static void BuildRabbitVisuals(Transform root)
        {
            Material fur = Fur(new Color(0.62f, 0.52f, 0.42f));
            Material white = Fur(new Color(0.92f, 0.90f, 0.86f));
            Material inner = Fur(new Color(0.92f, 0.66f, 0.64f));
            Material dark = Fur(new Color(0.08f, 0.06f, 0.06f), 0.5f);

            Part(PrimitiveType.Sphere, "Body", root, new Vector3(0f, 0.30f, -0.04f), new Vector3(0.34f, 0.32f, 0.46f), Quaternion.Euler(-12f, 0f, 0f), fur);
            Part(PrimitiveType.Sphere, "Haunch", root, new Vector3(0f, 0.26f, -0.16f), new Vector3(0.36f, 0.30f, 0.32f), Quaternion.identity, fur);
            Part(PrimitiveType.Sphere, "Chest", root, new Vector3(0f, 0.30f, 0.12f), new Vector3(0.24f, 0.24f, 0.22f), Quaternion.identity, white);

            float[] legLen = { 0.20f, 0.20f, 0.17f, 0.17f };
            BuildLegs(root, new Vector3(0.07f, 0.20f, 0.12f), new Vector3(0.11f, 0.17f, -0.18f), legLen, 0.06f, fur, white, hindHock: true, hindFootLength: 0.16f);

            Transform neck = Joint("NeckPivot", root, new Vector3(0f, 0.38f, 0.16f), Quaternion.identity);
            Transform head = Joint("Head", neck, new Vector3(0f, 0.08f, 0.06f), Quaternion.identity);
            Part(PrimitiveType.Sphere, "Skull", head, Vector3.zero, new Vector3(0.20f, 0.19f, 0.22f), Quaternion.identity, fur);
            Part(PrimitiveType.Sphere, "Cheeks", head, new Vector3(0f, -0.04f, 0.07f), new Vector3(0.15f, 0.10f, 0.12f), Quaternion.identity, white);
            Part(PrimitiveType.Sphere, "Nose", head, new Vector3(0f, -0.01f, 0.115f), Vector3.one * 0.03f, inner);
            foreach (int s in new[] { -1, 1 })
            {
                Part(PrimitiveType.Sphere, "Eye" + s, head, new Vector3(s * 0.075f, 0.03f, 0.05f), Vector3.one * 0.035f, dark);
                Transform ear = Joint("Ear" + s, head, new Vector3(s * 0.04f, 0.08f, -0.03f), Quaternion.Euler(-12f, 0f, s * -10f));
                Part(PrimitiveType.Capsule, "Outer", ear, new Vector3(0f, 0.11f, 0f), new Vector3(0.06f, 0.12f, 0.03f), Quaternion.identity, fur);
                Part(PrimitiveType.Capsule, "Inner", ear, new Vector3(0f, 0.11f, 0.008f), new Vector3(0.035f, 0.09f, 0.02f), Quaternion.identity, inner);
            }

            Transform tail = Joint("TailPivot", root, new Vector3(0f, 0.30f, -0.30f), Quaternion.identity);
            Part(PrimitiveType.Sphere, "Tail", tail, new Vector3(0f, 0f, -0.02f), Vector3.one * 0.09f, white);
        }

        // ------------------------------------------------------------------ anatomy helpers

        /// <summary>
        /// Four two-segment legs. front/hind give the right-hand shoulder/hip joint position (mirrored for the left).
        /// Each leg: LegPivot_i (swings) / upper segment / Knee_i (bends) / lower segment / hoof.
        /// </summary>
        private static void BuildLegs(Transform root, Vector3 front, Vector3 hind, float[] lengths, float thickness,
                                      Material upperMat, Material hoofMat, bool hindHock, float hindFootLength = 0f)
        {
            Vector3[] hips =
            {
                new Vector3(-front.x, front.y, front.z), new Vector3(front.x, front.y, front.z),
                new Vector3(-hind.x, hind.y, hind.z), new Vector3(hind.x, hind.y, hind.z)
            };

            for (int i = 0; i < 4; i++)
            {
                bool isHind = i >= 2;
                float len = lengths[i];
                float upper = len * (isHind ? 0.5f : 0.48f), lower = len - upper;

                Transform pivot = Joint("LegPivot_" + i, root, hips[i], Quaternion.identity);
                // Thigh / upper arm: thicker, slightly angled (hind thighs slope back, like a hock)
                float thighTilt = isHind && hindHock ? 14f : 0f;
                Part(PrimitiveType.Capsule, "Upper", pivot, new Vector3(0f, -upper * 0.5f, -Mathf.Sin(thighTilt * Mathf.Deg2Rad) * upper * 0.5f),
                     new Vector3(thickness * 1.5f, upper * 0.62f, thickness * 1.6f), Quaternion.Euler(-thighTilt, 0f, 0f), upperMat);

                Vector3 kneePos = new Vector3(0f, -upper, -Mathf.Sin(thighTilt * Mathf.Deg2Rad) * upper);
                Transform knee = Joint("Knee_" + i, pivot, kneePos, Quaternion.identity);
                Part(PrimitiveType.Capsule, "Lower", knee, new Vector3(0f, -lower * 0.5f, 0f), new Vector3(thickness, lower * 0.56f, thickness), Quaternion.identity, upperMat);
                Part(PrimitiveType.Capsule, "Hoof", knee, new Vector3(0f, -lower + thickness * 0.35f, isHind && hindFootLength > 0f ? hindFootLength * 0.3f : 0.01f),
                     new Vector3(thickness * 0.9f, (isHind && hindFootLength > 0f ? hindFootLength * 0.6f : thickness * 0.55f), thickness * 0.95f), Quaternion.Euler(90f, 0f, 0f), hoofMat); // as wide as the leg: a hoof, not a ball
            }
        }

        private static readonly Dictionary<string, Material> FurCache = new Dictionary<string, Material>();

        /// <summary>Flat-coloured matte material (same look as the rigged characters' materials).</summary>
        private static Material Fur(Color color, float smoothness = 0.12f)
        {
            string key = ColorUtility.ToHtmlStringRGB(color) + smoothness;
            if (FurCache.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MAT_Fur_" + key, enableInstancing = true };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            FurCache[key] = m;
            return m;
        }

        private static Transform Joint(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            GameObject j = new GameObject(name);
            j.transform.SetParent(parent, false);
            j.transform.localPosition = localPosition;
            j.transform.localRotation = localRotation;
            return j.transform;
        }

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material) =>
            Part(type, name, parent, localPosition, localScale, Quaternion.identity, material);

        private static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation;
            MeshRenderer mr = part.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
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
