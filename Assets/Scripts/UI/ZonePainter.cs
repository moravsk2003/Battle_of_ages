using UnityEngine;
using UnityEngine.EventSystems;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Marking zones on the map (M5 step 3): pick a zone in the build menu, drag with the left button to paint, drag with
    /// the right button to clear, Esc to stop. The marked cells lie over the ground as tinted squares on the map, draped on
    /// the relief; one mesh, rebuilt only when the zones change.
    /// </summary>
    public sealed class ZonePainter : MonoBehaviour
    {
        /// <summary>The brush's radius, metres.</summary>
        public const float Brush = 14f;
        private const float Lift = 0.3f, Alpha = 0.38f;

        public static ZonePainter Instance { get; private set; }
        public static ZoneType Tool { get; private set; }
        public static bool Painting => Tool != ZoneType.None;

        private GameObject overlay;
        private Mesh mesh;
        private int builtVersion = -1;
        private string lastRefusal;
        private float refusalAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<ZonePainter>() != null) return;
            var go = new GameObject("ZonePainter");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<ZonePainter>();
        }

        /// <summary>Takes up the brush of this zone (on the map).</summary>
        public static void Begin(ZoneType type)
        {
            Tool = type;
            StrategyHud.Notify($"{ZoneMap.Name(type)}: ЛКМ — малювати, ПКМ — стерти, Esc — готово");
        }

        public static void Stop() => Tool = ZoneType.None;

        private void Update()
        {
            bool map = GameContext.MapOpen;
            if (!map) Tool = ZoneType.None;
            if (Painting)
            {
                if (UniversalInput.GetKeyDownEscape()) Stop();
                else PaintUnderCursor();
            }
            if (builtVersion != ZoneMap.Version) Rebuild();
            if (overlay != null && overlay.activeSelf != map) overlay.SetActive(map);
        }

        private void PaintUnderCursor()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            bool paint = UniversalInput.GetMouseButton(0), erase = UniversalInput.GetMouseButton(1);
            if (!paint && !erase) return;
            Camera cam = ViewCamera.Current;
            if (cam == null || !Physics.Raycast(cam.ScreenPointToRay(UniversalInput.GetMousePosition()), out RaycastHit hit, 3000f)) return;
            if (erase) { ZoneMap.Erase(hit.point, Brush); return; }
            ZoneMap.Paint(hit.point, Brush, Tool, out string refused);
            if (refused != null && (refused != lastRefusal || Time.unscaledTime - refusalAt > 3f))
            {
                lastRefusal = refused;
                refusalAt = Time.unscaledTime;
                StrategyHud.Notify(refused);
            }
        }

        /// <summary>All marked cells as tinted quads on the relief.</summary>
        private void Rebuild()
        {
            builtVersion = ZoneMap.Version;
            if (overlay == null)
            {
                Shader s = Shader.Find("Sprites/Default");
                if (s == null) return;
                overlay = new GameObject("ZoneOverlay");
                overlay.transform.SetParent(transform, false);
                mesh = new Mesh { name = "ZoneOverlay", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer r = overlay.AddComponent<MeshRenderer>();
                r.sharedMaterial = new Material(s) { name = "MAT_ZoneOverlay" };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            int n = ZoneMap.Cells.Count;
            var v = new Vector3[n * 4];
            var c = new Color[n * 4];
            var t = new int[n * 6];
            int i = 0;
            foreach (var pair in ZoneMap.Cells)
            {
                float x = pair.Key.x * ZoneMap.CellSize, z = pair.Key.y * ZoneMap.CellSize, e = ZoneMap.CellSize * 0.96f;
                v[i] = Corner(x, z); v[i + 1] = Corner(x, z + e); v[i + 2] = Corner(x + e, z + e); v[i + 3] = Corner(x + e, z);
                Color col = ZoneMap.Colour(pair.Value);
                col.a = Alpha;
                c[i] = c[i + 1] = c[i + 2] = c[i + 3] = col;
                int k = i / 4 * 6;
                t[k] = i; t[k + 1] = i + 1; t[k + 2] = i + 2;
                t[k + 3] = i; t[k + 4] = i + 2; t[k + 5] = i + 3;
                i += 4;
            }
            mesh.Clear();
            mesh.vertices = v;
            mesh.colors = c;
            mesh.triangles = t;
            mesh.RecalculateBounds();
        }

        private static Vector3 Corner(float x, float z) => new Vector3(x, InfiniteWorldManager.GetSampledTerrainHeight(x, z) + Lift, z);
    }
}
