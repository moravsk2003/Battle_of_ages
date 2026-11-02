using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Manages Pause Menu UI state, settings adjustment sliders, controls keybinding configuration, and cursor lock states.
    /// Safely restores gameplay states upon resuming.
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuController : MonoBehaviour
    {
        public static PauseMenuController Instance { get; private set; }

        [Header("UI Panels")]
        [SerializeField] private GameObject pauseMenuPanel;
        [SerializeField] private GameObject settingsPanel;

        [Header("UI Controls")]
        [SerializeField] private Slider fpsSensSlider;
        [SerializeField] private Slider mapSensSlider;
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button closeSettingsButton;
        [SerializeField] private Button quitButton;

        [Header("References")]
        [SerializeField] private GameModeManager gameModeManager;

        private bool isPaused = false;
        private CursorLockMode previousCursorLockMode;
        private bool previousCursorVisible;

        // Keybinding Rebind UI references
        private KeyAction? activeRebindAction = null;
        private readonly Dictionary<KeyAction, Text> keybindButtonTexts = new Dictionary<KeyAction, Text>();
        private GameObject keybindsContainerObj;
        private Slider zoomSlider;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            InitializeReferences();
            SetupButtonListeners();
            InitializeSliders();
            BuildSaveButtons();

            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
            Transform pauseWindow = pauseMenuPanel != null ? pauseMenuPanel.transform.Find("PauseWindow") : null;
            if (pauseWindow != null) PopIn.On(pauseWindow);

            KeybindsManager.OnKeybindsChanged += RefreshKeybindButtonTexts;
        }

        /// <summary>
        /// Save / Load / Main menu in the pause column: copies of the Settings button stacked under it, the Quit button
        /// moved to the bottom (Resume, Settings, Save, Load, Main menu, Quit).
        /// </summary>
        private void BuildSaveButtons()
        {
            if (resumeButton == null || settingsButton == null) return;
            var resume = (RectTransform)resumeButton.transform;
            var settings = (RectTransform)settingsButton.transform;
            Vector2 step = settings.anchoredPosition - resume.anchoredPosition;
            if (step.sqrMagnitude < 1f) step = new Vector2(0f, -(settings.rect.height + 12f));
            Vector2 at = settings.anchoredPosition;
            AddPauseButton("SaveButton", "Зберегти", at += step, () => SaveSlotsUI.Open(true));
            AddPauseButton("LoadButton", "Завантажити", at += step, () => SaveSlotsUI.Open(false));
            AddPauseButton("MainMenuButton", "Головне меню", at += step, SaveSystem.ToMainMenu);
            if (quitButton != null) ((RectTransform)quitButton.transform).anchoredPosition = at + step;

            // A window behind the column, from the title down to the last button
            Transform title = resume.parent.Find("Title");
            float top = (title != null ? ((RectTransform)title).anchoredPosition.y : resume.anchoredPosition.y) + 50f;
            float bottom = (at + step).y - 45f;
            RectTransform window = UiTheme.Rect(resume.parent, "PauseWindow", new Vector2(0.5f, 0.5f), new Vector2(0f, (top + bottom) * 0.5f), new Vector2(resume.rect.width + 140f, top - bottom));
            UiTheme.PanelImage(window).raycastTarget = false;
            window.SetAsFirstSibling();
        }

        private void AddPauseButton(string name, string text, Vector2 position, UnityEngine.Events.UnityAction onClick)
        {
            GameObject copy = Instantiate(settingsButton.gameObject, settingsButton.transform.parent);
            copy.name = name;
            ((RectTransform)copy.transform).anchoredPosition = position;
            Button b = copy.GetComponent<Button>();
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(onClick);
            Text label = copy.GetComponentInChildren<Text>(true);
            if (label != null) label.text = text;
        }

        private void OnDestroy()
        {
            KeybindsManager.OnKeybindsChanged -= RefreshKeybindButtonTexts;
        }

        private void InitializeReferences()
        {
            if (gameModeManager == null)
            {
                gameModeManager = Managers.GameContext.Modes;
            }
        }

        private void InitializeSliders()
        {
            if (SettingsManager.Instance == null) return;

            if (fpsSensSlider != null)
            {
                fpsSensSlider.minValue = SettingsManager.MIN_SENSITIVITY;
                fpsSensSlider.maxValue = SettingsManager.MAX_SENSITIVITY;
                fpsSensSlider.value = SettingsManager.Instance.FpsSensitivity;
                fpsSensSlider.onValueChanged.AddListener(OnFpsSensChanged);
            }

            if (mapSensSlider != null)
            {
                mapSensSlider.minValue = SettingsManager.MIN_SENSITIVITY;
                mapSensSlider.maxValue = SettingsManager.MAX_MAP_SENSITIVITY;
                mapSensSlider.value = SettingsManager.Instance.MapSensitivity;
                mapSensSlider.onValueChanged.AddListener(OnMapSensChanged);
                AddZoomSlider();
            }

            if (volumeSlider != null)
            {
                volumeSlider.minValue = SettingsManager.MIN_VOLUME;
                volumeSlider.maxValue = SettingsManager.MAX_VOLUME;
                volumeSlider.value = SettingsManager.Instance.MasterVolume;
                volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            }
        }

        /// <summary>The map's wheel zoom speed: a copy of the map slider (BuildSettingsWindow puts it in its row).</summary>
        private void AddZoomSlider()
        {
            if (settingsPanel == null || zoomSlider != null) return;
            GameObject slider = Instantiate(mapSensSlider.gameObject, settingsPanel.transform);
            slider.name = "ZoomSlider";
            zoomSlider = slider.GetComponent<Slider>();
            zoomSlider.onValueChanged = new Slider.SliderEvent();
            zoomSlider.minValue = SettingsManager.MIN_ZOOM_SENSITIVITY;
            zoomSlider.maxValue = SettingsManager.MAX_ZOOM_SENSITIVITY;
            zoomSlider.value = SettingsManager.Instance.ZoomSensitivity;
            zoomSlider.onValueChanged.AddListener(v => SettingsManager.Instance?.SetZoomSensitivity(v));
        }

        private void SetupButtonListeners()
        {
            if (resumeButton != null) resumeButton.onClick.AddListener(ResumeGame);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (closeSettingsButton != null) closeSettingsButton.onClick.AddListener(CloseSettings);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
        }

        private void Update()
        {
            if (Time.time < 0.5f) return;

            if (activeRebindAction != null)
            {
                CheckForRebindKeyPress();
                return;
            }

            if (UniversalInput.GetKeyDownEscape())
            {
                if (GameWindows.AnyModalOpen)
                {
                    GameWindows.CloseAllModals();
                    return;
                }
                if (ZonePainter.Painting)
                {
                    ZonePainter.Stop(); // the brush goes down, the map stays
                    return;
                }
                // Escape cancels a building being placed (BuildingPlacementSystem) and takes the map back to the first
                // person view; only there it pauses
                var placement = Building.BuildingPlacementSystem.Instance;
                if (placement != null && (placement.IsPlacing || placement.CancelledFrame == Time.frameCount)) return;
                if (!isPaused && gameModeManager != null && gameModeManager.CurrentMode == GameMode.RtsMap)
                {
                    gameModeManager.SetGameMode(GameMode.FirstPerson);
                    return;
                }


                TogglePause();
            }
        }

        private void CheckForRebindKeyPress()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.anyKey.wasPressedThisFrame)
            {
                foreach (UnityEngine.InputSystem.Controls.KeyControl keyCtrl in UnityEngine.InputSystem.Keyboard.current.allKeys)
                {
                    if (keyCtrl.wasPressedThisFrame)
                    {
                        if (System.Enum.TryParse<KeyCode>(keyCtrl.name, true, out var parsedKey))
                        {
                            if (parsedKey != KeyCode.Escape)
                            {
                                KeybindsManager.SetKey(activeRebindAction.Value, parsedKey);
                            }
                            activeRebindAction = null;
                            RefreshKeybindButtonTexts();
                            return;
                        }
                    }
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (k == KeyCode.None || (int)k >= (int)KeyCode.JoystickButton0) continue;
                    if (Input.GetKeyDown(k))
                    {
                        if (k != KeyCode.Escape)
                        {
                            KeybindsManager.SetKey(activeRebindAction.Value, k);
                        }
                        activeRebindAction = null;
                        RefreshKeybindButtonTexts();
                        return;
                    }
                }
            }
            catch { }
#endif
        }

        public void TogglePause()
        {
            if (isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }

        public void PauseGame()
        {
            isPaused = true;
            Time.timeScale = 0f;

            previousCursorLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
            if (settingsPanel != null) settingsPanel.SetActive(false);
        }

        public void ResumeGame()
        {
            isPaused = false;
            Time.timeScale = 1f;

            activeRebindAction = null;
            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);

            if (gameModeManager != null)
            {
                gameModeManager.SetGameMode(gameModeManager.CurrentMode);
            }
            else
            {
                Cursor.lockState = previousCursorLockMode;
                Cursor.visible = previousCursorVisible;
            }
        }

        public void OpenSettings()
        {
            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(true);

            BuildKeybindsUI();
            RefreshKeybindButtonTexts();
        }

        public void CloseSettings()
        {
            activeRebindAction = null;
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
        }

        /// <summary>
        /// The settings screen as one themed window: the sliders with their values on the left (the scene's sliders are
        /// moved into rows and its labels hidden), graphics and keys on the right, Back and Reset along the bottom.
        /// </summary>
        private void BuildKeybindsUI()
        {
            if (settingsPanel == null || keybindsContainerObj != null) return;
            Transform panel = settingsPanel.transform;
            foreach (Text t in panel.GetComponentsInChildren<Text>(true))
                if (t.GetComponentInParent<Slider>(true) == null && (closeSettingsButton == null || !t.transform.IsChildOf(closeSettingsButton.transform)))
                    t.gameObject.SetActive(false);

            RectTransform win = UiTheme.Rect(panel, "SettingsWindow", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 640f));
            UiTheme.PanelImage(win);
            UiTheme.Label(UiTheme.Rect(win, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 50f)), "Text", "Налаштування", 32, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.Divider(win, -64f, 520f);
            PopIn.On(win);

            RectTransform left = Section(win, "Sliders", "Миша й звук", new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(28f, 96f), new Vector2(-12f, -76f));
            int row = 0;
            SliderRow(left, row++, "Миша від першої особи", fpsSensSlider, v => v.ToString("0.0"));
            SliderRow(left, row++, "Перетяг мапи", mapSensSlider, v => v.ToString("0.0"));
            SliderRow(left, row++, "Зум колесом на мапі", zoomSlider, v => v.ToString("0.0"));
            SliderRow(left, row, "Гучність", volumeSlider, v => Mathf.RoundToInt(v * 100f) + "%");

            RectTransform right = Section(win, "KeybindsPanel", "Графіка й клавіші", new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(12f, 96f), new Vector2(-28f, -76f));
            keybindsContainerObj = right.gameObject;
            BuildKeyList(right);

            if (closeSettingsButton != null)
            {
                var back = (RectTransform)closeSettingsButton.transform;
                back.SetParent(win, false);
                back.anchorMin = back.anchorMax = back.pivot = new Vector2(0.5f, 0f);
                back.sizeDelta = new Vector2(260f, 52f);
                back.anchoredPosition = new Vector2(-145f, 24f);
            }
            UiTheme.TextButton(win, "ResetDefaultsBtn", "Скинути клавіші", new Vector2(0.5f, 0f), new Vector2(145f, 24f), new Vector2(260f, 52f), () =>
            {
                activeRebindAction = null;
                KeybindsManager.ResetToDefaults();
            });
            RefreshKeybindButtonTexts();
        }

        private static RectTransform Section(RectTransform win, string name, string title, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            RectTransform s = UiTheme.Child(win, name, min, max, offMin, offMax);
            UiTheme.PanelImage(s, UiTheme.SlotSprite);
            UiTheme.Label(UiTheme.Child(s, "Heading", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -40f), new Vector2(-18f, -6f)), "Text", title, 20, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            return s;
        }

        /// <summary>One slider row: name on the left, current value on the right, the slider under them.</summary>
        private static void SliderRow(RectTransform section, int index, string title, Slider slider, System.Func<float, string> format)
        {
            if (slider == null) return;
            const float h = 92f;
            RectTransform r = UiTheme.Child(section, "Row_" + slider.name, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(22f, -52f - h * (index + 1)), new Vector2(-22f, -52f - h * index));
            UiTheme.Label(UiTheme.Child(r, "Name", new Vector2(0f, 0.5f), new Vector2(0.75f, 1f), Vector2.zero, Vector2.zero), "Text", title, 19, TextAnchor.MiddleLeft);
            Text value = UiTheme.Label(UiTheme.Child(r, "Value", new Vector2(0.75f, 0.5f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero), "Text", format(slider.value), 19, TextAnchor.MiddleRight, UiTheme.Gold, FontStyle.Bold);
            slider.onValueChanged.AddListener(v => value.text = format(v));

            var st = (RectTransform)slider.transform;
            st.SetParent(r, false);
            st.anchorMin = new Vector2(0f, 0.08f); st.anchorMax = new Vector2(1f, 0.46f);
            st.pivot = new Vector2(0.5f, 0.5f);
            st.offsetMin = new Vector2(12f, 0f); st.offsetMax = new Vector2(-12f, 0f);
            foreach (RectTransform part in st)
            {
                if (part.name == "Handle Slide Area") continue;
                part.anchorMin = new Vector2(0f, 0.3f); part.anchorMax = new Vector2(1f, 0.7f); part.offsetMin = part.offsetMax = Vector2.zero;
            }
            foreach (Image img in slider.GetComponentsInChildren<Image>(true))
            {
                if (img.rectTransform == slider.handleRect) { img.sprite = UiTheme.RoundSprite; img.color = UiTheme.Gold; img.rectTransform.sizeDelta = new Vector2(26f, 0f); }
                else if (img.rectTransform == slider.fillRect) { img.sprite = UiTheme.BarSprite; img.color = UiTheme.Rim; }
                else { img.sprite = UiTheme.BarSprite; img.color = new Color(0.03f, 0.025f, 0.02f, 0.95f); }
                img.type = Image.Type.Sliced;
                img.preserveAspect = img.rectTransform == slider.handleRect;
            }
        }

        /// <summary>Graphics preset and one row per key, scrolling under the heading.</summary>
        private void BuildKeyList(RectTransform section)
        {
            RectTransform scrollRect = UiTheme.Child(section, "ScrollView", Vector2.zero, Vector2.one, new Vector2(14f, 14f), new Vector2(-14f, -48f));
            ScrollRect scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewportRect = UiTheme.Child(scrollRect, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewportRect.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewportRect;

            RectTransform contentRect = UiTheme.Child(viewportRect, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            contentRect.pivot = new Vector2(0.5f, 1f);
            VerticalLayoutGroup layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(4, 4, 4, 4);
            contentRect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;

            CreateGraphicsRow(contentRect);
            CycleRow(contentRect, "Row_UiScale", "Масштаб інтерфейсу", () => DisplaySettings.ScaleName, DisplaySettings.CycleScale);
            CycleRow(contentRect, "Row_FullScreen", "Повний екран", () => DisplaySettings.FullScreen ? "так" : "вікно", DisplaySettings.ToggleFullScreen);
            CycleRow(contentRect, "Row_Resolution", "Роздільність", () => DisplaySettings.ResolutionName, DisplaySettings.CycleResolution);
            CycleRow(contentRect, "Row_VSync", "VSync", () => DisplaySettings.VSync ? "увімк." : "вимк.", DisplaySettings.ToggleVSync);
            CycleRow(contentRect, "Row_FrameCap", "Ліміт кадрів", () => DisplaySettings.FrameCapName, DisplaySettings.CycleFrameCap);
            CycleRow(contentRect, "Row_Advisor", "Поради «Перші кроки»", () => Advisor.Hidden ? "сховано" : "показано", () => Advisor.Hidden = !Advisor.Hidden);
            foreach (KeyAction action in System.Enum.GetValues(typeof(KeyAction)))
            {
                KeyAction captured = action;
                keybindButtonTexts[action] = CreateRow(contentRect, $"Row_{action}", KeybindsManager.GetActionDisplayName(action), () => StartRebinding(captured));
            }
        }

        /// <summary>A row whose button steps through values (each click: the next one); the text follows the value.</summary>
        private void CycleRow(Transform parent, string name, string label, System.Func<string> value, System.Action next)
        {
            Text text = null;
            text = CreateRow(parent, name, label, () =>
            {
                next();
                text.text = value();
                // the resolution and the window change on the next frame: read them again then
                StartCoroutine(RefreshNextFrame(text, value));
            });
            text.text = value();
        }

        private static System.Collections.IEnumerator RefreshNextFrame(Text text, System.Func<string> value)
        {
            yield return null;
            yield return null;
            if (text != null) text.text = value();
        }

        /// <summary>The graphics preset (GraphicsQuality): a click goes to the next one.</summary>
        private void CreateGraphicsRow(Transform parent)
        {
            Text value = null;
            value = CreateRow(parent, "Row_Graphics", "Графіка", () =>
            {
                GraphicsQuality.Cycle();
                value.text = GraphicsQuality.Names[(int)GraphicsQuality.Current];
            });
            value.text = GraphicsQuality.Names[(int)GraphicsQuality.Current];
        }

        private static Text CreateRow(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            LayoutElement element = row.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 46f;
            var rt = (RectTransform)row.transform;
            UiTheme.PanelImage(rt, UiTheme.SlotSprite, new Color(1f, 1f, 1f, 0.9f));

            UiTheme.Label(UiTheme.Child(rt, "Label", new Vector2(0f, 0f), new Vector2(0.62f, 1f), new Vector2(14f, 0f), Vector2.zero), "Text", label, 17, TextAnchor.MiddleLeft);
            Button btn = UiTheme.TextButton(rt, "KeyBtn", "", new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(150f, 36f), onClick, 17);
            return btn.GetComponentInChildren<Text>();
        }

        private void StartRebinding(KeyAction action)
        {
            activeRebindAction = action;
            RefreshKeybindButtonTexts();
        }

        private void RefreshKeybindButtonTexts()
        {
            if (this == null || keybindButtonTexts == null) return;

            foreach (var kvp in keybindButtonTexts)
            {
                KeyAction action = kvp.Key;
                Text txt = kvp.Value;
                if (txt == null) continue;

                if (activeRebindAction.HasValue && activeRebindAction.Value == action)
                {
                    txt.text = "[ ? ... ]";
                    txt.color = UiTheme.Bad;
                }
                else
                {
                    KeyCode key = KeybindsManager.GetKey(action);
                    txt.text = $"[ {KeybindsManager.GetKeyDisplayString(key)} ]";
                    txt.color = UiTheme.Gold;
                }
            }
        }

        public void OnFpsSensChanged(float val)
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.SetFpsSensitivity(val);
            }
        }

        public void OnMapSensChanged(float val)
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.SetMapSensitivity(val);
            }
        }

        public void OnVolumeChanged(float val)
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.SetMasterVolume(val);
            }
        }

        public void QuitGame()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public bool IsPaused() => isPaused;
    }
}
