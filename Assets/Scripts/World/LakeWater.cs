using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.World
{
    /// <summary>
    /// The lakes' water: one flat mesh per lake in the shape of its shore (a little past it, under the bank), on the
    /// Custom/StylizedWater shader (Always Included Shaders: no material asset uses it). Remade when the world changes.
    /// </summary>
    public sealed class LakeWater : MonoBehaviour
    {
        private const int Segments = 64;
        private int builtSeed = int.MinValue;
        private float builtSize = -1f;
        private Material material;
        private readonly List<GameObject> surfaces = new List<GameObject>();

        public int SurfaceCount => surfaces.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<LakeWater>() != null) return;
            var go = new GameObject("LakeWater");
            DontDestroyOnLoad(go);
            go.AddComponent<LakeWater>();
        }

        private void Update()
        {
            if (builtSeed == WorldSettings.Seed && builtSize == WorldSettings.Size) return;
            builtSeed = WorldSettings.Seed;
            builtSize = WorldSettings.Size;
            Rebuild();
        }

        public void Rebuild()
        {
            foreach (GameObject s in surfaces) if (s != null) Destroy(s);
            surfaces.Clear();
            if (Lakes.All.Count == 0) return;
            if (material == null)
            {
                Shader shader = Shader.Find("Custom/StylizedWater");
                if (shader == null) return; // (stripped from a build: no water drawn, the lakes still stop walkers)
                material = new Material(shader) { name = "MAT_LakeWater" };
            }
            for (int i = 0; i < Lakes.All.Count; i++)
            {
                Lakes.Lake l = Lakes.All[i];
                var go = new GameObject("Lake_" + i);
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(l.Center.x, l.Level, l.Center.y);
                go.AddComponent<MeshFilter>().sharedMesh = Surface(l);
                MeshRenderer r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                surfaces.Add(go);
            }
        }

        /// <summary>A fan in the shore's shape, facing up, centred on the lake.</summary>
        public static Mesh Surface(Lakes.Lake l)
        {
            var v = new Vector3[Segments + 1];
            var uv = new Vector2[Segments + 1];
            var tris = new int[Segments * 3];
            v[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                float r = l.ShoreRadius(a) * 1.03f;
                v[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                uv[i + 1] = new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a));
                int k = i * 3, next = (i + 1) % Segments + 1;
                tris[k] = 0; tris[k + 1] = next; tris[k + 2] = i + 1; // clockwise seen from above
            }
            var m = new Mesh { name = "LakeSurface", vertices = v, uv = uv, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
