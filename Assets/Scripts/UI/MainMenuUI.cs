using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BattleOfAges.Managers;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Start screen (MainMenu scene): Continue (the latest save), New game, Load (slot list), Settings (sensitivity,
    /// volume) and Quit, on a painted dusk sky with the title. Built in code with UiTheme.
    /// </summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        private RectTransform menu, settings;
        private Button continueButton;
        private Text continueInfo;

        private void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (FindFirstObjectByType<EventSystem>() == null)
#if ENABLE_INPUT_SYSTEM
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            if (SettingsManager.Instance == null) new GameObject("SettingsManager").AddComponent<SettingsManager>();
            Build();
            RefreshContinue();
        }

        private void RefreshContinue()
        {
            int slot = SaveSystem.LatestSlot();
            continueButton.interactable = slot >= 0;
            if (slot >= 0)
            {
                SaveMeta m = SaveSystem.Meta(slot);
                continueInfo.text = m.Exists ? $"{SaveSystem.SlotName(slot)}  ·  {m.SavedAt:dd.MM HH:mm}" : "";
            }
            else continueInfo.text = "Збережень ще немає";
        }

        private void Continue()
        {
            int slot = SaveSystem.LatestSlot();
            if (slot >= 0) SaveSystem.Load(slot);
        }

        private void ShowSettings(bool show)
        {
            menu.gameObject.SetActive(!show);
            settings.gameObject.SetActive(show);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("MainMenuCanvas", 10);
            canvas.transform.SetParent(transform, false);
            var root = (RectTransform)canvas.transform;

            RectTransform bg = UiTheme.Rect(root, "Background", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.sizeDelta = Vector2.zero;
            var raw = bg.gameObject.AddComponent<RawImage>();
            raw.texture = Backdrop();
            raw.raycastTarget = false;

            RectTransform titleRt = UiTheme.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1200f, 120f));
            UiTheme.Label(titleRt, "Text", "Battle of Ages", 92, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            RectTransform sub = UiTheme.Rect(root, "Subtitle", new Vector2(0.5f, 1f), new Vector2(0f, -226f), new Vector2(1200f, 40f));
            UiTheme.Label(sub, "Text", "Збудуй поселення  ·  спорядь військо  ·  вистоїш проти набігів", 24, TextAnchor.MiddleCenter, UiTheme.Ink);

            menu = UiTheme.Rect(root, "Menu", new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(460f, 520f));
            UiTheme.PanelImage(menu);
            float y = -36f;
            continueButton = Big(menu, "Продовжити", ref y, Continue);
            RectTransform ci = UiTheme.Rect(menu, "ContinueInfo", new Vector2(0.5f, 1f), new Vector2(0f, y + 14f), new Vector2(400f, 24f));
            continueInfo = UiTheme.Label(ci, "Text", "", 15, TextAnchor.MiddleCenter, UiTheme.InkDim);
            y -= 18f;
            Big(menu, "Нова гра", ref y, SaveSystem.NewGame);
            Big(menu, "Завантажити", ref y, () => SaveSlotsUI.Open(false, RefreshContinue));
            Big(menu, "Налаштування", ref y, () => ShowSettings(true));
            Big(menu, "Вихід", ref y, Quit);

            settings = UiTheme.Rect(root, "Settings", new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(620f, 460f));
            UiTheme.PanelImage(settings);
            RectTransform st = UiTheme.Rect(settings, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(500f, 44f));
            UiTheme.Label(st, "Text", "Налаштування", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            SettingsManager sm = SettingsManager.Instance;
            SliderRow(settings, "Чутливість миші (від першої особи)", -100f, SettingsManager.MIN_SENSITIVITY, SettingsManager.MAX_SENSITIVITY, sm != null ? sm.FpsSensitivity : 1f, v => SettingsManager.Instance?.SetFpsSensitivity(v));
            SliderRow(settings, "Чутливість камери мапи", -190f, SettingsManager.MIN_SENSITIVITY, SettingsManager.MAX_SENSITIVITY, sm != null ? sm.MapSensitivity : 1f, v => SettingsManager.Instance?.SetMapSensitivity(v));
            SliderRow(settings, "Гучність", -280f, SettingsManager.MIN_VOLUME, SettingsManager.MAX_VOLUME, sm != null ? sm.MasterVolume : 1f, v => SettingsManager.Instance?.SetMasterVolume(v));
            RectTransform kb = UiTheme.Rect(settings, "Keys", new Vector2(0.5f, 0f), new Vector2(0f, 88f), new Vector2(560f, 26f));
            UiTheme.Label(kb, "Text", "Клавіші можна перепризначити в грі: Esc → Налаштування", 15, TextAnchor.MiddleCenter, UiTheme.InkDim);
            UiTheme.TextButton(settings, "Back", "Назад", new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(220f, 52f), () => ShowSettings(false), 20);
            settings.gameObject.SetActive(false);

            RectTransform ver = UiTheme.Rect(root, "Version", new Vector2(1f, 0f), new Vector2(-16f, 12f), new Vector2(400f, 24f));
            UiTheme.Label(ver, "Text", "v" + Application.version, 14, TextAnchor.LowerRight, UiTheme.InkDim);
        }

        private static Button Big(RectTransform parent, string text, ref float y, UnityEngine.Events.UnityAction onClick)
        {
            Button b = UiTheme.TextButton(parent, text, text, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(380f, 64f), onClick, 26);
            y -= 84f;
            return b;
        }

        private static void SliderRow(RectTransform parent, string label, float y, float min, float max, float value, System.Action<float> changed)
        {
            RectTransform lab = UiTheme.Rect(parent, label, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(540f, 28f));
            Text valueText = null;
            UiTheme.Label(lab, "Text", label, 19, TextAnchor.MiddleLeft, UiTheme.Ink);
            RectTransform valRt = UiTheme.Rect(lab, "Value", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(80f, 28f));
            valueText = UiTheme.Label(valRt, "Text", "", 19, TextAnchor.MiddleRight, UiTheme.Gold, FontStyle.Bold);

            RectTransform s = UiTheme.Rect(parent, label + "_Slider", new Vector2(0.5f, 1f), new Vector2(0f, y - 38f), new Vector2(540f, 26f));
            UiTheme.PanelImage(s, UiTheme.SlotSprite);
            Slider slider = s.gameObject.AddComponent<Slider>();
            RectTransform fillArea = UiTheme.Rect(s, "FillArea", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fillArea.anchorMin = new Vector2(0f, 0f); fillArea.anchorMax = new Vector2(1f, 1f); fillArea.offsetMin = new Vector2(6f, 6f); fillArea.offsetMax = new Vector2(-6f, -6f);
            RectTransform fill = UiTheme.Rect(fillArea, "Fill", new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.sizeDelta = Vector2.zero;
            UiTheme.PanelImage(fill, UiTheme.BarSprite, UiTheme.Gold * 0.85f);
            RectTransform handleArea = UiTheme.Rect(s, "HandleArea", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one; handleArea.offsetMin = new Vector2(10f, 0f); handleArea.offsetMax = new Vector2(-10f, 0f);
            RectTransform handle = UiTheme.Rect(handleArea, "Handle", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(24f, 34f));
            handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f); handle.sizeDelta = new Vector2(24f, 8f);
            Image hImg = UiTheme.PanelImage(handle, UiTheme.ButtonSprite);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = hImg;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = Mathf.Clamp(value, min, max);
            valueText.text = slider.value.ToString("0.##");
            slider.onValueChanged.AddListener(v => { valueText.text = v.ToString("0.##"); changed(v); });
        }

        /// <summary>Dusk sky over dark hills, painted into a small texture.</summary>
        private static Texture2D Backdrop()
        {
            const int W = 480, H = 270;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MenuBackdrop" };
            var px = new Color[W * H];
            Color top = new Color(0.10f, 0.12f, 0.22f), horizon = new Color(0.85f, 0.52f, 0.30f), hillsFar = new Color(0.20f, 0.17f, 0.20f), hillsNear = new Color(0.08f, 0.07f, 0.07f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (float)H, u = x / (float)W;
                Color c = Color.Lerp(horizon, top, Mathf.Pow(Mathf.Clamp01((v - 0.25f) / 0.75f), 0.7f));
                float sun = Mathf.Exp(-((u - 0.62f) * (u - 0.62f) * 40f + (v - 0.33f) * (v - 0.33f) * 120f));
                c = Color.Lerp(c, new Color(1f, 0.85f, 0.55f), sun * 0.8f);
                float far = 0.30f + 0.05f * Mathf.Sin(u * 9f + 1.3f) + 0.03f * Mathf.Sin(u * 23f);
                float near = 0.20f + 0.06f * Mathf.Sin(u * 5f + 0.4f) + 0.02f * Mathf.Sin(u * 31f + 2f);
                if (v < far) c = Color.Lerp(hillsFar, c, 0.25f);
                if (v < near) c = hillsNear;
                px[x + y * W] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }
    }
}
