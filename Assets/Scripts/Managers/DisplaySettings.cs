using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattleOfAges.Managers
{
    /// <summary>
    /// Screen and interface options (docs/GAME_DESIGN.md §21, stage 1.7): interface scale 80–140% (every screen canvas,
    /// including ones made later), full screen or window, resolution, VSync, frame cap. Kept in PlayerPrefs and applied
    /// at start.
    /// </summary>
    public static class DisplaySettings
    {
        public static readonly int[] ScalePercents = { 80, 90, 100, 110, 120, 130, 140 };
        public static readonly int[] FrameCaps = { 0, 30, 60, 120, 144, 240 }; // 0: no cap

        private const string ScaleKey = "Settings_UiScale", FullKey = "Settings_FullScreen", VsyncKey = "Settings_VSync", CapKey = "Settings_FrameCap", ResKey = "Settings_Resolution";

        public static float UiScale { get; private set; } = 1f;
        public static bool VSync { get; private set; }
        public static int FrameCap { get; private set; }

        private static readonly Dictionary<int, Vector2> baseReference = new Dictionary<int, Vector2>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            UiScale = Mathf.Clamp(PlayerPrefs.GetInt(ScaleKey, 100), 80, 140) / 100f;
            VSync = PlayerPrefs.GetInt(VsyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0) == 1; // default: as the project ships
            FrameCap = PlayerPrefs.GetInt(CapKey, 0);
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
            if (!Application.isEditor)
            {
                if (PlayerPrefs.HasKey(FullKey)) Screen.fullScreenMode = PlayerPrefs.GetInt(FullKey) == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                string res = PlayerPrefs.GetString(ResKey, "");
                string[] wh = res.Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h)) Screen.SetResolution(w, h, Screen.fullScreenMode);
            }
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => ApplyScale();

        // ------------------------------------------------------------------ interface scale

        public static string ScaleName => Mathf.RoundToInt(UiScale * 100f) + "%";

        public static void CycleScale()
        {
            int now = Mathf.RoundToInt(UiScale * 100f), i = System.Array.IndexOf(ScalePercents, now);
            SetScale(ScalePercents[(i + 1) % ScalePercents.Length] / 100f);
        }

        public static void SetScale(float scale)
        {
            UiScale = Mathf.Clamp(scale, 0.8f, 1.4f);
            PlayerPrefs.SetInt(ScaleKey, Mathf.RoundToInt(UiScale * 100f));
            ApplyScale();
        }

        /// <summary>Every screen canvas shows its UI UiScale times larger (its reference resolution shrinks by as much).</summary>
        public static void ApplyScale()
        {
            foreach (CanvasScaler s in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Apply(s);
        }

        /// <summary>One canvas (UiRestyler calls it for canvases that appear later); cheap when already right.</summary>
        public static void Apply(CanvasScaler s)
        {
            if (s == null || s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return;
            int id = s.GetInstanceID();
            if (!baseReference.TryGetValue(id, out Vector2 b)) { b = s.referenceResolution; baseReference[id] = b; }
            Vector2 want = b / UiScale;
            if ((s.referenceResolution - want).sqrMagnitude > 0.01f) s.referenceResolution = want;
        }

        // ------------------------------------------------------------------ screen

        public static bool FullScreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        public static void ToggleFullScreen()
        {
            bool full = !FullScreen;
            Screen.fullScreenMode = full ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            PlayerPrefs.SetInt(FullKey, full ? 1 : 0);
        }

        public static string ResolutionName => $"{Screen.width} × {Screen.height}";

        /// <summary>The next of the monitor's resolutions (each size once, the highest refresh rate).</summary>
        public static void CycleResolution()
        {
            var sizes = new List<Vector2Int>();
            foreach (Resolution r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 1024 && !sizes.Contains(v)) sizes.Add(v);
            }
            if (sizes.Count == 0) return;
            int i = sizes.IndexOf(new Vector2Int(Screen.width, Screen.height));
            Vector2Int next = sizes[(i + 1) % sizes.Count];
            Screen.SetResolution(next.x, next.y, Screen.fullScreenMode);
            PlayerPrefs.SetString(ResKey, next.x + "x" + next.y);
        }

        public static void ToggleVSync()
        {
            VSync = !VSync;
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            PlayerPrefs.SetInt(VsyncKey, VSync ? 1 : 0);
        }

        public static string FrameCapName => FrameCap > 0 ? FrameCap + " FPS" : "без обмеження";

        public static void CycleFrameCap()
        {
            int i = System.Array.IndexOf(FrameCaps, FrameCap);
            FrameCap = FrameCaps[(i + 1) % FrameCaps.Length];
            Application.targetFrameRate = FrameCap > 0 ? FrameCap : -1;
            PlayerPrefs.SetInt(CapKey, FrameCap);
        }
    }
}
