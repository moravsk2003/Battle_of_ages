using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Merges the still parts of a model built from many primitives (the chest, the castle wall) into one mesh per
    /// material, under the given root: every part was a renderer of its own, drawn and shadowed one by one, and the CPU
    /// of a weak PC pays per renderer (BuildingProbe: the chest had 21, the wall 20). The merged meshes are built once per
    /// key and shared by every copy (all copies are built the same). The parts keep their game objects (names and
    /// colliders stay); only their MeshRenderer and MeshFilter go.
    /// </summary>
    public static class StaticMeshMerger
    {
        private static readonly Dictionary<string, (Mesh mesh, Material material)[]> cache = new Dictionary<string, (Mesh, Material)[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => cache.Clear();

        public static void Merge(GameObject root, string key)
        {
            var parts = new List<MeshRenderer>();
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null || r.sharedMaterials.Length != 1 || !f.sharedMesh.isReadable) continue;
                parts.Add(r);
            }
            if (parts.Count < 2) return;

            if (!cache.TryGetValue(key, out var merged) || !Valid(merged))
            {
                merged = Build(root.transform, parts, key);
                cache[key] = merged;
            }

            foreach (MeshRenderer r in parts)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(f);
            }
            for (int i = 0; i < merged.Length; i++)
            {
                var go = new GameObject("Merged_" + i);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = merged[i].mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = merged[i].material;
                mr.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        private static bool Valid((Mesh mesh, Material material)[] merged)
        {
            foreach (var m in merged) if (m.mesh == null || m.material == null) return false;
            return true;
        }

        private static (Mesh, Material)[] Build(Transform root, List<MeshRenderer> parts, string key)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (MeshRenderer r in parts)
            {
                Material mat = r.sharedMaterial;
                if (!byMaterial.TryGetValue(mat, out var list)) byMaterial[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = r.GetComponent<MeshFilter>().sharedMesh, transform = toRoot * r.transform.localToWorldMatrix });
            }
            var result = new List<(Mesh, Material)>();
            foreach (var kv in byMaterial)
            {
                var mesh = new Mesh { name = key + "_" + kv.Key.name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(false);
                result.Add((mesh, kv.Key));
            }
            return result.ToArray();
        }
    }
}
