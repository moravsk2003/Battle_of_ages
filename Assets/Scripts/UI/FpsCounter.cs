using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.InputSystem;

namespace BattleOfAges.UI
{
    /// <summary>
    /// On-screen performance meter (top-right, toggled with F3): average FPS and frame time, the worst frame of the last
    /// second, the number of hitches (frames over 50 ms) in the last 10 seconds, and a frame-time graph of the last
    /// 160 frames where every spike shows as a tall red bar. Created automatically in every scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FpsCounter : MonoBehaviour
    {
        private const int GraphFrames = 160, GraphHeight = 48;
        private const float HitchMs = 50f, GraphMaxMs = 66.7f, RefreshInterval = 0.5f;

        private static FpsCounter instance;

        private Text label;
        private RawImage graphImage;
        private Texture2D graph;
        private Color32[] pixels;
        private GameObject panel;
        private readonly float[] frameMs = new float[GraphFrames];
        private readonly float[] hitchTimes = new float[64];
        private int frameIndex, hitchIndex;
        private float accumulated, worstMs, refreshTimer;
        private int accumulatedFrames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (instance != null) return;
            GameObject go = new GameObject("FpsCounter");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FpsCounter>();
        }

        private void Awake()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000; // above every game UI
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(transform, false);
            RectTransform pr = (RectTransform)panel.transform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(1f, 1f);
            pr.anchoredPosition = new Vector2(-12f, -12f);
            pr.sizeDelta = new Vector2(GraphFrames * 2f + 16f, GraphHeight + 84f);
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = false;

            GameObject textObj = new GameObject("Text", typeof(RectTransform));
            textObj.transform.SetParent(panel.transform, false);
            RectTransform tr = (RectTransform)textObj.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -4f);
            tr.sizeDelta = new Vector2(-16f, 74f);
            label = textObj.AddComponent<Text>();
            label.font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 17;
            label.supportRichText = true;
            label.alignment = TextAnchor.UpperLeft;
            label.raycastTarget = false;
            label.text = "FPS …";

            graph = new Texture2D(GraphFrames, GraphHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            pixels = new Color32[GraphFrames * GraphHeight];
            GameObject graphObj = new GameObject("Graph", typeof(RectTransform));
            graphObj.transform.SetParent(panel.transform, false);
            RectTransform gr = (RectTransform)graphObj.transform;
            gr.anchorMin = gr.anchorMax = gr.pivot = new Vector2(0.5f, 0f);
            gr.anchoredPosition = new Vector2(0f, 6f);
            gr.sizeDelta = new Vector2(GraphFrames * 2f, GraphHeight);
            graphImage = graphObj.AddComponent<RawImage>();
            graphImage.texture = graph;
            graphImage.raycastTarget = false;
        }

        private void Update()
        {
            if (UniversalInput.IsKeyDown(KeyCode.F3)) panel.SetActive(!panel.activeSelf);

            float ms = Time.unscaledDeltaTime * 1000f;
            frameMs[frameIndex] = ms;
            frameIndex = (frameIndex + 1) % GraphFrames;
            accumulated += ms;
            accumulatedFrames++;
            worstMs = Mathf.Max(worstMs, ms);
            if (ms > HitchMs)
            {
                hitchTimes[hitchIndex] = Time.unscaledTime;
                hitchIndex = (hitchIndex + 1) % hitchTimes.Length;
            }

            if (!panel.activeSelf) return;
            DrawGraph();

            refreshTimer += Time.unscaledDeltaTime;
            if (refreshTimer < RefreshInterval) return;
            float avgMs = accumulated / Mathf.Max(1, accumulatedFrames);
            float fps = 1000f / Mathf.Max(avgMs, 0.01f);
            int hitches = 0;
            foreach (float t in hitchTimes) if (t > 0f && Time.unscaledTime - t < 10f) hitches++;
            label.text = $"<color={Grade(fps)}><b>FPS {fps:0}</b></color>  {avgMs:0.0} мс\n" +
                         $"найгірший кадр за 1 с: <color={GradeMs(worstMs)}>{worstMs:0.0} мс</color>\n" +
                         $"лаги (>{HitchMs:0} мс) за 10 с: <color={(hitches > 0 ? "#ff5050" : "#7CFC7C")}>{hitches}</color>";
            refreshTimer = 0f;
            accumulated = 0f;
            accumulatedFrames = 0;
            worstMs = 0f;
        }

        /// <summary>Bar per frame, oldest on the left; green under 16.7 ms, yellow under 33 ms, red above. Lines at 60 / 30 FPS.</summary>
        private void DrawGraph()
        {
            var clear = new Color32(0, 0, 0, 0);
            var green = new Color32(90, 220, 90, 230);
            var yellow = new Color32(240, 200, 60, 240);
            var red = new Color32(255, 70, 60, 255);
            var guide = new Color32(255, 255, 255, 70);
            int line60 = Mathf.RoundToInt(16.7f / GraphMaxMs * GraphHeight), line30 = Mathf.RoundToInt(33.3f / GraphMaxMs * GraphHeight);
            for (int x = 0; x < GraphFrames; x++)
            {
                float ms = frameMs[(frameIndex + x) % GraphFrames];
                int h = Mathf.Clamp(Mathf.CeilToInt(ms / GraphMaxMs * GraphHeight), 0, GraphHeight);
                Color32 c = ms <= 16.7f ? green : ms <= 33.3f ? yellow : red;
                for (int y = 0; y < GraphHeight; y++)
                    pixels[y * GraphFrames + x] = y < h ? c : (y == line60 || y == line30 ? guide : clear);
            }
            graph.SetPixels32(pixels);
            graph.Apply(false);
        }

        private static string Grade(float fps) => fps >= 55f ? "#7CFC7C" : fps >= 30f ? "#FFD84A" : "#FF5050";
        private static string GradeMs(float ms) => ms <= 20f ? "#7CFC7C" : ms <= HitchMs ? "#FFD84A" : "#FF5050";

        private void OnDestroy()
        {
            if (graph != null) Destroy(graph);
            if (instance == this) instance = null;
        }
    }
}
