using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Distance LOD for crowds of skinned characters with ONE renderer: swaps its shared mesh between a near and a far
    /// mesh (same bones) and hides it when it is a speck, by height on screen like a LODGroup. A LODGroup would need a
    /// renderer per level, doubling the per-character renderer cost in 1000-unit battles. Seen from ViewCamera (or
    /// ViewerOverride); a slice of all characters is re-evaluated each frame, capture tools call RefreshAll.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkinnedMeshLod : MonoBehaviour
    {
        public SkinnedMeshRenderer Target;
        public Mesh Near, Far;
        public float Size = 2f;            // world size used for the screen height (like LODGroup.size)
        public float NearScreenHeight = 0.2f;
        public float CullScreenHeight = 0.006f;

        /// <summary>Viewer for capture / preview cameras instead of Camera.main.</summary>
        public static Camera ViewerOverride;

        private static readonly List<SkinnedMeshLod> Active = new List<SkinnedMeshLod>();
        private static int cursor;
        private static Driver driver;
        private int index = -1;
        private bool near;

        private void OnEnable()
        {
            index = Active.Count;
            Active.Add(this);
            Refresh(Viewer());
            if (Application.isPlaying && driver == null)
            {
                var go = new GameObject("SkinnedMeshLodDriver") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                driver = go.AddComponent<Driver>();
            }
        }

        private void OnDisable()
        {
            if (index < 0) return;
            int last = Active.Count - 1;
            Active[index] = Active[last];
            Active[index].index = index;
            Active.RemoveAt(last);
            index = -1;
        }

        /// <summary>One updater for all characters: re-evaluates about a quarter of them per frame.</summary>
        private sealed class Driver : MonoBehaviour
        {
            private void Update()
            {
                Camera cam = Viewer();
                if (cam == null || Active.Count == 0) return;
                int n = Mathf.Min(Active.Count, Mathf.Max(32, Active.Count / 4));
                for (int i = 0; i < n; i++)
                {
                    cursor = (cursor + 1) % Active.Count;
                    Active[cursor].Refresh(cam);
                }
            }
        }

        /// <summary>Re-evaluates every character for this camera (before a screenshot from a capture camera).</summary>
        public static void RefreshAll(Camera cam)
        {
            foreach (SkinnedMeshLod lod in Active) lod.Refresh(cam);
        }

        /// <summary>Picks the mesh now (after the fields were set on a new component).</summary>
        public void Apply() => Refresh(Viewer());

        private static Camera Viewer() => ViewerOverride != null ? ViewerOverride : ViewCamera.Current;

        private void Refresh(Camera cam)
        {
            if (Target == null || cam == null) return;
            float h;
            if (cam.orthographic) h = Size / (2f * cam.orthographicSize);
            else
            {
                float d = Vector3.Distance(cam.transform.position, transform.position);
                h = Size / (2f * Mathf.Max(0.01f, d) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            }
            h *= QualitySettings.lodBias;
            bool wantNear = Near != null && (near ? h > NearScreenHeight * 0.9f : h > NearScreenHeight * 1.1f); // hysteresis
            if (wantNear != near || Target.sharedMesh == null)
            {
                near = wantNear;
                SetMesh(Target, near ? Near : Far);
            }
            bool show = h >= CullScreenHeight;
            if (Target.enabled != show) Target.enabled = show;
        }

        /// <summary>
        /// Swaps a skinned renderer's mesh so it keeps rendering. A renderer that has been drawn keeps its GPU skinning
        /// buffers sized for the old mesh, and a swap while it is enabled stops it rendering for good ("does not match
        /// the expected mesh data size and vertex stride", Unity 6000.3); swapped while disabled, it rebuilds them.
        /// </summary>
        public static void SetMesh(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            if (renderer.sharedMesh == mesh) return;
            bool on = renderer.enabled;
            renderer.enabled = false;
            renderer.sharedMesh = mesh;
            renderer.enabled = on;
        }
    }
}
