using UnityEngine;
using BattleOfAges.Resources;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Builds high-quality AAA stylized trees with organic flared trunks and faceted wind-swaying foliage clusters.
    /// </summary>
    public static class SteppeTreeBuilder
    {
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> CachedTrunkMeshes = new System.Collections.Generic.Dictionary<int, Mesh>();
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> CachedFoliageMeshes = new System.Collections.Generic.Dictionary<int, Mesh>();

        public static GameObject CreateSteppeTree(string name = "SteppeTree", int variant = 0)
        {
            GameObject root = new GameObject(name);
            root.AddComponent<ResourceNodeTree>();

            int varIdx = Mathf.Abs(variant) % 4;

            Color barkColor = Color.Lerp(new Color(0.26f, 0.16f, 0.08f), new Color(0.38f, 0.24f, 0.12f), Mathf.Repeat(varIdx * 0.31f, 1f));
            Color foliageBase = (varIdx % 3 == 0)
                ? new Color(0.32f, 0.52f, 0.14f)
                : (varIdx % 3 == 1 ? new Color(0.22f, 0.44f, 0.15f) : new Color(0.56f, 0.52f, 0.12f));

            Material barkMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, barkColor, 0f, 0.22f);
            Material foliageMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Foliage, foliageBase, 0f, 0.25f);

            // 1. Organic Tapered Trunk
            GameObject trunk = new GameObject("StylizedTrunk");
            trunk.transform.SetParent(root.transform, false);
            MeshFilter trunkMf = trunk.AddComponent<MeshFilter>();
            if (!CachedTrunkMeshes.TryGetValue(varIdx, out Mesh trunkMesh) || trunkMesh == null)
            {
                trunkMesh = ProceduralMeshBuilder.CreateStylizedTrunkMesh(0.48f, 0.22f, 3.4f, 7, 5, varIdx * 99);
                trunkMesh.name = $"StylizedTrunk_Var{varIdx}";
                CachedTrunkMeshes[varIdx] = trunkMesh;
            }
            trunkMf.sharedMesh = trunkMesh;
            MeshRenderer trunkMr = trunk.AddComponent<MeshRenderer>();
            trunkMr.sharedMaterial = barkMat;
            trunkMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            trunkMr.receiveShadows = true;

            CapsuleCollider trunkCollider = root.AddComponent<CapsuleCollider>();
            trunkCollider.center = new Vector3(0f, 1.7f, 0f);
            trunkCollider.radius = 0.45f;
            trunkCollider.height = 3.4f;

            // 2. Foliage Crown Clusters (Layered canopy with sun-dappled tops and deep AO underneath)
            CreateFoliageCloud(root.transform, foliageMat, new Vector3(0.00f, 3.35f, 0.00f), new Vector3(2.4f, 1.8f, 2.4f), varIdx * 17 + 1, varIdx * 10 + 1);
            CreateFoliageCloud(root.transform, foliageMat, new Vector3(0.75f, 3.85f, 0.35f), new Vector3(1.7f, 1.4f, 1.6f), varIdx * 17 + 2, varIdx * 10 + 2);
            CreateFoliageCloud(root.transform, foliageMat, new Vector3(-0.65f, 3.75f, -0.40f), new Vector3(1.8f, 1.5f, 1.7f), varIdx * 17 + 3, varIdx * 10 + 3);
            CreateFoliageCloud(root.transform, foliageMat, new Vector3(0.15f, 4.55f, -0.10f), new Vector3(1.5f, 1.3f, 1.5f), varIdx * 17 + 4, varIdx * 10 + 4);

            return root;
        }

        private static void CreateFoliageCloud(Transform parent, Material material, Vector3 localPos, Vector3 scale, int seed, int cacheKey)
        {
            GameObject cloud = new GameObject($"CrownCluster_{seed}");
            cloud.transform.SetParent(parent, false);
            cloud.transform.localPosition = localPos;

            MeshFilter mf = cloud.AddComponent<MeshFilter>();
            if (!CachedFoliageMeshes.TryGetValue(cacheKey, out Mesh foliageMesh) || foliageMesh == null)
            {
                foliageMesh = ProceduralMeshBuilder.CreateStylizedFoliageCluster(scale, seed);
                foliageMesh.name = $"FoliageCluster_{cacheKey}";
                CachedFoliageMeshes[cacheKey] = foliageMesh;
            }
            mf.sharedMesh = foliageMesh;

            MeshRenderer mr = cloud.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
    }
}
