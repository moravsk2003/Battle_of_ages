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
            CreateCombinedCrown(root.transform, foliageMat, varIdx); // four clusters merged: one renderer instead of four

            return root;
        }

        private static readonly System.Collections.Generic.Dictionary<int, Mesh> CachedPineCrowns = new System.Collections.Generic.Dictionary<int, Mesh>();
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> CachedPineTrunks = new System.Collections.Generic.Dictionary<int, Mesh>();

        /// <summary>
        /// Tall northern spruce: thin tapered trunk and a dense crown of drooping star-shaped tiers.
        /// Child names follow the broadleaf tree ("StylizedTrunk", "...Crown") so ResourceNodeTree can fell it.
        /// </summary>
        public static GameObject CreatePineTree(string name = "PineTree", int variant = 0)
        {
            GameObject root = new GameObject(name);
            root.AddComponent<ResourceNodeTree>();

            int varIdx = Mathf.Abs(variant) % 4;
            Color[] needleColors =
            {
                new Color(0.20f, 0.38f, 0.20f), // spruce
                new Color(0.25f, 0.42f, 0.18f), // pine green
                new Color(0.19f, 0.36f, 0.27f), // blue spruce
                new Color(0.30f, 0.44f, 0.18f)  // sunny pine
            };
            Color needles = needleColors[varIdx];
            Color bark = Color.Lerp(new Color(0.20f, 0.14f, 0.10f), new Color(0.30f, 0.21f, 0.14f), varIdx / 3f);

            Material barkMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, bark, 0f, 0.18f);
            Material needleMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Foliage, needles, 0f, 0.15f);
            needleMat.SetColor("_TopColor", Color.Lerp(needles, new Color(0.62f, 0.78f, 0.36f), 0.35f));
            needleMat.SetColor("_BottomColor", needles * 0.6f);
            needleMat.SetFloat("_Translucency", 0.25f);
            needleMat.SetFloat("_WindStrength", 0.04f);

            float height = 8.5f + varIdx * 0.6f;

            GameObject trunk = new GameObject("StylizedTrunk");
            trunk.transform.SetParent(root.transform, false);
            if (!CachedPineTrunks.TryGetValue(varIdx, out Mesh trunkMesh) || trunkMesh == null)
            {
                trunkMesh = ProceduralMeshBuilder.CreateStylizedTrunkMesh(0.30f, 0.07f, height * 0.85f, 6, 5, 500 + varIdx * 31);
                trunkMesh.name = $"PineTrunk_Var{varIdx}";
                CachedPineTrunks[varIdx] = trunkMesh;
            }
            trunk.AddComponent<MeshFilter>().sharedMesh = trunkMesh;
            MeshRenderer trunkMr = trunk.AddComponent<MeshRenderer>();
            trunkMr.sharedMaterial = barkMat;

            GameObject crown = new GameObject("PineFoliageCrown");
            crown.transform.SetParent(root.transform, false);
            if (!CachedPineCrowns.TryGetValue(varIdx, out Mesh crownMesh) || crownMesh == null)
            {
                crownMesh = ProceduralMeshBuilder.CreateConiferCrownMesh(2.0f + varIdx * 0.12f, height - 1.4f, 1.5f, 7 + varIdx % 2, 900 + varIdx * 53);
                crownMesh.name = $"PineCrown_Var{varIdx}";
                CachedPineCrowns[varIdx] = crownMesh;
            }
            crown.AddComponent<MeshFilter>().sharedMesh = crownMesh;
            MeshRenderer crownMr = crown.AddComponent<MeshRenderer>();
            crownMr.sharedMaterial = needleMat;

            CapsuleCollider trunkCollider = root.AddComponent<CapsuleCollider>();
            trunkCollider.center = new Vector3(0f, 1.7f, 0f);
            trunkCollider.radius = 0.45f;
            trunkCollider.height = 3.4f;

            return root;
        }

        /// <summary>
        /// Layered canopy (sun-dappled tops, deep AO underneath) of four foliage clusters baked into one cached mesh per
        /// variant, so a tree costs two draw calls (trunk + crown) instead of five.
        /// </summary>
        private static void CreateCombinedCrown(Transform parent, Material material, int varIdx)
        {
            if (!CachedFoliageMeshes.TryGetValue(varIdx, out Mesh crownMesh) || crownMesh == null)
            {
                (Vector3 pos, Vector3 scale)[] clusters =
                {
                    (new Vector3(0.00f, 3.35f, 0.00f), new Vector3(2.4f, 1.8f, 2.4f)),
                    (new Vector3(0.75f, 3.85f, 0.35f), new Vector3(1.7f, 1.4f, 1.6f)),
                    (new Vector3(-0.65f, 3.75f, -0.40f), new Vector3(1.8f, 1.5f, 1.7f)),
                    (new Vector3(0.15f, 4.55f, -0.10f), new Vector3(1.5f, 1.3f, 1.5f)),
                };
                var parts = new CombineInstance[clusters.Length];
                var sources = new Mesh[clusters.Length];
                for (int i = 0; i < clusters.Length; i++)
                {
                    sources[i] = ProceduralMeshBuilder.CreateStylizedFoliageCluster(clusters[i].scale, varIdx * 17 + i + 1);
                    parts[i] = new CombineInstance { mesh = sources[i], transform = Matrix4x4.Translate(clusters[i].pos) };
                }
                crownMesh = new Mesh { name = $"SteppeCrown_Var{varIdx}" };
                crownMesh.CombineMeshes(parts, true, true);
                crownMesh.RecalculateBounds();
                foreach (Mesh m in sources) { if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m); }
                CachedFoliageMeshes[varIdx] = crownMesh;
            }

            GameObject crown = new GameObject("SteppeCrown");
            crown.transform.SetParent(parent, false);
            crown.AddComponent<MeshFilter>().sharedMesh = crownMesh;
            MeshRenderer mr = crown.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
    }
}
