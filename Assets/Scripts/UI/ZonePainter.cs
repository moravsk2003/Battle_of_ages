using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Marking zones on the map (M5 step 3): pick a zone (ZoneToolbar), drag with the left button to paint, drag with
    /// the right button to clear, Esc to stop (PauseMenuController: the key does nothing else then). The marked cells lie
    /// over the ground as tinted squares on the map, draped on the relief; one mesh, rebuilt only when the zones change.
    /// While a brush is up, the cells it would mark show under the cursor (red: refused) and a ring shows how far the
    /// settlement reaches.
    /// </summary>
    public sealed class ZonePainter : MonoBehaviour
    {
        /// <summary>The brush's radius, metres, for each size.</summary>
        public static readonly float[] Brushes = { 8f, 14f, 26f };
        public static int BrushIndex { get; set; } = 1;
        public static float Brush => Brushes[Mathf.Clamp(BrushIndex, 0, Brushes.Length - 1)];
        private const float Lift = 0.3f, Alpha = 0.38f, RingWidth = 2.5f, RingEvery = 1f;
        private const int RingSegments = 96;

        public static ZonePainter Instance { get; private set; }
        public static ZoneType Tool { get; private set; }
        public static bool Erasing { get; private set; }
        public static bool Painting => Tool != ZoneType.None || Erasing;

        private GameObject overlay;
        private Mesh mesh;
        private int builtVersion = -1;
        private string lastRefusal;
        private float refusalAt;
        private bool strokeOn;
        private Vector3 strokeFrom;
        private GameObject preview, ring;
        private Mesh previewMesh, ringMesh;
        private Material material;
        private Vector2Int previewCell;
        private ZoneType previewTool;
        private bool previewErase;
        private float previewBrush;
        private int previewVersion = -1;
        private float nextRing;
        private readonly List<Vector2Int> brushCells = new List<Vector2Int>();
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> tris = new List<int>();

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
            Erasing = false;
        }

        /// <summary>Takes up the eraser: the left button clears as well.</summary>
        public static void BeginEraser()
        {
            Tool = ZoneType.None;
            Erasing = true;
        }

        public static void Stop()
        {
            Tool = ZoneType.None;
            Erasing = false;
        }

        private void Update()
        {
            bool map = GameContext.MapOpen;
            if (!map) Stop();
            else if (!GameWindows.BlockGameplay && UniversalInput.IsKeyDown(KeyCode.Delete))
            {
                if (Erasing) Stop();
                else BeginEraser();
            }
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool free = Painting && !overUi && !GameWindows.BlockGameplay && GroundUnderCursor(out cursor);
            if (free) PaintUnderCursor();
            else strokeOn = false;
            if (builtVersion != ZoneMap.Version) Rebuild();
            if (overlay != null && overlay.activeSelf != map) overlay.SetActive(map);
            if (material != null) material.color = new Color(1f, 1f, 1f, Painting ? 1.3f : 1f);
            UpdatePreview(free);
            UpdateRing(map && Painting);
        }

        private Vector3 cursor;

        /// <summary>Where the cursor's ray meets the land itself (not a tree or a roof in the way).</summary>
        private static bool GroundUnderCursor(out Vector3 at)
        {
            at = default;
            Camera cam = ViewCamera.Current;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(UniversalInput.GetMousePosition());
            if (ray.direction.y > -0.01f) return false;
            float ground = InfiniteWorldManager.GetSampledTerrainHeight(ray.origin.x, ray.origin.z);
            for (int i = 0; i < 5; i++)
            {
                at = ray.GetPoint((ray.origin.y - ground) / -ray.direction.y);
                ground = InfiniteWorldManager.GetSampledTerrainHeight(at.x, at.z);
            }
            at.y = ground;
            return true;
        }

        private void PaintUnderCursor()
        {
            bool erase = UniversalInput.GetMouseButton(1) || (Erasing && UniversalInput.GetMouseButton(0));
            bool paint = !erase && UniversalInput.GetMouseButton(0);
            if (!paint && !erase) { strokeOn = false; return; }
            Vector3 from = strokeOn ? strokeFrom : cursor;
            strokeFrom = cursor;
            strokeOn = true;
            if (erase) { EraseStroke(from, cursor, Brush); return; }
            PaintStroke(from, cursor, Brush, Tool, out string refused);
            if (refused != null && (refused != lastRefusal || Time.unscaledTime - refusalAt > 3f))
            {
                lastRefusal = refused;
                refusalAt = Time.unscaledTime;
                StrategyHud.Notify(refused);
            }
        }

        /// <summary>The brush along a line: however fast the mouse goes, no gaps. Returns the cells changed.</summary>
        public static int PaintStroke(Vector3 from, Vector3 to, float radius, ZoneType type, out string refused)
        {
            refused = null;
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / (radius * 0.5f))), changed = 0;
            for (int i = 1; i <= steps; i++)
            {
                changed += ZoneMap.Paint(Vector3.Lerp(from, to, i / (float)steps), radius, type, out string why);
                if (why != null) refused = why;
            }
            return changed;
        }

        private static void EraseStroke(Vector3 from, Vector3 to, float radius)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / (radius * 0.5f)));
            for (int i = 1; i <= steps; i++) ZoneMap.Erase(Vector3.Lerp(from, to, i / (float)steps), radius);
        }

        private Material Material()
        {
            if (material == null)
            {
                Shader s = Shader.Find("Sprites/Default");
                if (s != null) material = new Material(s) { name = "MAT_ZoneOverlay" };
            }
            return material;
        }

        private Mesh Layer(string name, ref GameObject go)
        {
            go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Material();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return m;
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            colors.Add(col); colors.Add(col); colors.Add(col); colors.Add(col);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        private void Fill(Mesh m)
        {
            m.Clear();
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            verts.Clear(); colors.Clear(); tris.Clear();
        }

        /// <summary>The cells the brush would take, in its colour; red where they are refused.</summary>
        private void UpdatePreview(bool show)
        {
            if (preview != null && preview.activeSelf != show) preview.SetActive(show);
            if (!show || Material() == null) return;
            if (preview == null) previewMesh = Layer("ZoneBrush", ref preview);
            Vector2Int cell = ZoneMap.CellOf(cursor);
            if (cell == previewCell && Tool == previewTool && Erasing == previewErase && Brush == previewBrush && ZoneMap.Version == previewVersion) return;
            previewCell = cell; previewTool = Tool; previewErase = Erasing; previewBrush = Brush; previewVersion = ZoneMap.Version;

            Color good = Erasing ? new Color(1f, 1f, 1f, 0.55f) : ZoneMap.Colour(Tool);
            good.a = Erasing ? 0.55f : 0.72f;
            Color bad = new Color(0.86f, 0.2f, 0.16f, 0.5f);
            ZoneMap.CellsIn(cursor, Brush, brushCells);
            float e = ZoneMap.CellSize * 0.96f;
            foreach (Vector2Int c in brushCells)
            {
                if (Erasing && ZoneMap.At(ZoneMap.CenterOf(c)) == ZoneType.None) continue;
                float x = c.x * ZoneMap.CellSize, z = c.y * ZoneMap.CellSize;
                Color col = !Erasing && ZoneMap.Forbidden(c, Tool) != null ? bad : good;
                Quad(Corner(x, z, 0.15f), Corner(x, z + e, 0.15f), Corner(x + e, z + e, 0.15f), Corner(x + e, z, 0.15f), col);
            }
            Fill(previewMesh);
        }

        /// <summary>How far each settlement marks zones, a ring on the land while a brush is up.</summary>
        private void UpdateRing(bool show)
        {
            if (ring != null && ring.activeSelf != show) ring.SetActive(show);
            if (!show || Material() == null) { nextRing = 0f; return; }
            if (ring == null) ringMesh = Layer("ZoneReach", ref ring);
            if (Time.unscaledTime < nextRing) return;
            nextRing = Time.unscaledTime + RingEvery;
            Color gold = new Color(0.96f, 0.8f, 0.45f, 0.85f);
            var halls = Building.TownHall.All;
            if (halls.Count == 0) Ring(Vector3.zero, ZoneMap.Reach(null), gold);
            foreach (Building.TownHall hall in halls) Ring(hall.transform.position, ZoneMap.Reach(hall), gold);
            Fill(ringMesh);
        }

        private void Ring(Vector3 mid, float radius, Color col)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float a0 = i * Mathf.PI * 2f / RingSegments, a1 = (i + 1) * Mathf.PI * 2f / RingSegments;
                float c0 = Mathf.Cos(a0), s0 = Mathf.Sin(a0), c1 = Mathf.Cos(a1), s1 = Mathf.Sin(a1);
                float inner = radius - RingWidth;
                Quad(Corner(mid.x + c0 * inner, mid.z + s0 * inner, 0.4f), Corner(mid.x + c0 * radius, mid.z + s0 * radius, 0.4f),
                     Corner(mid.x + c1 * radius, mid.z + s1 * radius, 0.4f), Corner(mid.x + c1 * inner, mid.z + s1 * inner, 0.4f), col);
            }
        }

        /// <summary>All marked cells as tinted quads on the relief.</summary>
        private void Rebuild()
        {
            builtVersion = ZoneMap.Version;
            if (Material() == null) return;
            if (overlay == null) mesh = Layer("ZoneOverlay", ref overlay);
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

        private static Vector3 Corner(float x, float z, float extra = 0f) => new Vector3(x, InfiniteWorldManager.GetSampledTerrainHeight(x, z) + Lift + extra, z);
    }
}
