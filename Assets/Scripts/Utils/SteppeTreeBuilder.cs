using UnityEngine;
using BattleOfAges.Resources;

namespace BattleOfAges.Utils
{
    /// <summary>Builds a varied, low-poly steppe tree with a real trunk, branch silhouette and layered crown.</summary>
    public static class SteppeTreeBuilder
    {
        public static GameObject CreateSteppeTree(string name = "SteppeTree", int variant = 0)
        {
            GameObject root = new GameObject(name);
            root.AddComponent<ResourceNodeTree>();

            Color barkColor = Color.Lerp(new Color(0.22f, 0.105f, 0.035f), new Color(0.38f, 0.21f, 0.07f), Mathf.Repeat(variant * 0.27f, 1f));
            Color leafColor = (variant % 3 == 0)
                ? new Color(0.30f, 0.46f, 0.10f)
                : (variant % 3 == 1 ? new Color(0.19f, 0.40f, 0.12f) : new Color(0.53f, 0.48f, 0.09f));
            Color leafHighlight = Color.Lerp(leafColor, new Color(0.82f, 0.72f, 0.24f), 0.18f);

            Material bark = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, barkColor, 0f, 0.22f);
            Material leaf = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Foliage, leafColor, 0f, 0.18f);
            Material leafLight = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Foliage, leafHighlight, 0f, 0.16f);

            GameObject trunk = CreatePart(PrimitiveType.Cylinder, "Trunk", root.transform, new Vector3(0f, 1.45f, 0f), new Vector3(0.38f, 1.45f, 0.38f), Quaternion.identity, bark);
            CapsuleCollider trunkCollider = trunk.AddComponent<CapsuleCollider>();
            trunkCollider.radius = 0.43f;
            trunkCollider.height = 3.0f;

            // Four asymmetric, upward branches avoid the old stacked-sphere silhouette.
            float phase = variant * 37f;
            CreateBranch(root.transform, bark, new Vector3(0f, 2.15f, 0f), Quaternion.Euler(20f, phase + 25f, -38f), 0.72f, 0.16f, "Branch_A");
            CreateBranch(root.transform, bark, new Vector3(0f, 2.35f, 0f), Quaternion.Euler(-18f, phase + 125f, 42f), 0.84f, 0.14f, "Branch_B");
            CreateBranch(root.transform, bark, new Vector3(0f, 2.60f, 0f), Quaternion.Euler(28f, phase + 220f, -34f), 0.67f, 0.13f, "Branch_C");
            CreateBranch(root.transform, bark, new Vector3(0f, 2.82f, 0f), Quaternion.Euler(-12f, phase + 305f, 30f), 0.55f, 0.11f, "Branch_D");

            CreateCrown(root.transform, leaf, new Vector3(0.00f, 3.08f, 0.00f), new Vector3(1.55f, 1.10f, 1.45f), "Crown_Main");
            CreateCrown(root.transform, leaf, new Vector3(0.55f, 3.34f, 0.18f), new Vector3(1.04f, 0.85f, 1.02f), "Crown_East");
            CreateCrown(root.transform, leaf, new Vector3(-0.48f, 3.30f, -0.22f), new Vector3(1.10f, 0.82f, 1.00f), "Crown_West");
            CreateCrown(root.transform, leafLight, new Vector3(0.08f, 3.78f, -0.05f), new Vector3(0.95f, 0.70f, 0.92f), "Crown_Top");

            return root;
        }

        private static void CreateBranch(Transform parent, Material material, Vector3 localPosition, Quaternion rotation, float length, float radius, string name)
        {
            CreatePart(PrimitiveType.Cylinder, name, parent, localPosition, new Vector3(radius, length * 0.5f, radius), rotation, material);
        }

        private static void CreateCrown(Transform parent, Material material, Vector3 localPosition, Vector3 localScale, string name)
        {
            GameObject crown = CreatePart(PrimitiveType.Sphere, name, parent, localPosition, localScale, Quaternion.identity, material);
            crown.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
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

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }

            return part;
        }
    }
}
