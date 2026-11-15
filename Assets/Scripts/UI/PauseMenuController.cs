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
        private readonly Dictionary<KeyAction, Text> keybindLabelTexts = new Dictionary<KeyAction, Text>();
        private readonly List<System.Action> cycleRefreshers = new List<System.Action>();
        private GameObject keybindsContainerObj;
        private Slider zoomSlider;
        private Slider musicSlider;
        private Slider sfxSlider;
        private Slider ambienceSlider;

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
            Loc.Changed += OnLanguageChanged;
            if (pauseMenuPanel != null)
                foreach (Text t in pauseMenuPanel.GetComponentsInChildren<Text>(true))
                    LocText.Attach(t);
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
            if (label != null)
            {
                label.text = text;
                LocText.SetSource(label, text);
            }
        }

        private void OnDestroy()
        {
            KeybindsManager.OnKeybindsChanged -= RefreshKeybindButtonTexts;
            Loc.Changed -= OnLanguageChanged;
        }

        private void OnLanguageChanged()
        {
            foreach (var refresh in cycleRefreshers) refresh?.Invoke();
            RefreshKeybindButtonTexts();
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
                AddAudioSliders();
            }
        }

        private void AddAudioSliders()
        {
            if (settingsPanel == null || musicSlider != null) return;
            musicSlider = CloneVolumeSlider("MusicSlider", SettingsManager.Instance.MusicVolume, v => SettingsManager.Instance?.SetMusicVolume(v));
            sfxSlider = CloneVolumeSlider("SfxSlider", SettingsManager.Instance.SfxVolume, v => SettingsManager.Instance?.SetSfxVolume(v));
            ambienceSlider = CloneVolumeSlider("AmbienceSlider", SettingsManager.Instance.AmbienceVolume, v => SettingsManager.Instance?.SetAmbienceVolume(v));
        }

        private Slider CloneVolumeSlider(string sliderName, float initialValue, UnityEngine.Events.UnityAction<float> onChanged)
        {
            GameObject go = Instantiate(volumeSlider.gameObject, settingsPanel.transform);
            go.name = sliderName;
            Slider s = go.GetComponent<Slider>();
            s.onValueChanged = new Slider.SliderEvent();
            s.minValue = SettingsManager.MIN_VOLUME;
            s.maxValue = SettingsManager.MAX_VOLUME;
            s.value = initialValue;
            s.onValueChanged.AddListener(onChanged);
            return s;
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
                    // First Esc lets go of those chosen, the next leaves the map
                    RTS.RtsUnitCommander commander = Managers.GameContext.Commander;
                    if (commander != null && commander.SelectedUnits.Count > 0) { commander.DeselectAll(); return; }
                    if (commander != null && commander.SelectedBuilding != null) { commander.SelectBuilding(null); return; }
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

            // One tab a category (a tester asked: all of it lay in two boxes)
            RectTransform control = SettingsTab(win, "Керування", 0);
            int row = 0;
            SliderRow(control, row++, "Миша від першої особи", fpsSensSlider, v => v.ToString("0.0"));
            SliderRow(control, row++, "Перетяг мапи", mapSensSlider, v => v.ToString("0.0"));
            SliderRow(control, row, "Зум колесом на мапі", zoomSlider, v => v.ToString("0.0"));

            RectTransform sound = SettingsTab(win, "Звук", 1);
            int sRow = 0;
            SliderRow(sound, sRow++, "Гучність", volumeSlider, v => Mathf.RoundToInt(v * 100f) + "%");
            SliderRow(sound, sRow++, "Музика", musicSlider, v => Mathf.RoundToInt(v * 100f) + "%");
            SliderRow(sound, sRow++, "Ефекти світу", sfxSlider, v => Mathf.RoundToInt(v * 100f) + "%");
            SliderRow(sound, sRow, "Оточення та інтерфейс", ambienceSlider, v => Mathf.RoundToInt(v * 100f) + "%");

            Transform screen = ScrollList(SettingsTab(win, "Графіка", 2));
            CreateGraphicsRow(screen);
            CycleRow(screen, "Row_UiScale", "Масштаб інтерфейсу", () => DisplaySettings.ScaleName, DisplaySettings.CycleScale);
            CycleRow(screen, "Row_FullScreen", "Повний екран", () => DisplaySettings.FullScreen ? Loc.Pick("так", "Yes") : Loc.Pick("вікно", "Windowed"), DisplaySettings.ToggleFullScreen);
            CycleRow(screen, "Row_Resolution", "Роздільність", () => DisplaySettings.ResolutionName, DisplaySettings.CycleResolution);
            CycleRow(screen, "Row_VSync", "VSync", () => DisplaySettings.VSync ? Loc.Pick("увімк.", "On") : Loc.Pick("вимк.", "Off"), DisplaySettings.ToggleVSync);
            CycleRow(screen, "Row_FrameCap", "Ліміт кадрів", () => DisplaySettings.FrameCapName, DisplaySettings.CycleFrameCap);

            RectTransform keys = SettingsTab(win, "Клавіші", 3);
            keybindsContainerObj = keys.gameObject;
            Transform keyRows = ScrollList(keys);
            foreach (KeyAction action in System.Enum.GetValues(typeof(KeyAction)))
            {
                KeyAction captured = action;
                var rowTexts = CreateRowWithLabel(keyRows, $"Row_{action}", KeybindsManager.GetActionDisplayName(action), () => StartRebinding(captured));
                LocText.Detach(rowTexts.label);
                LocText.Detach(rowTexts.value);
                keybindLabelTexts[action] = rowTexts.label;
                keybindButtonTexts[action] = rowTexts.value;
            }

            Transform game = ScrollList(SettingsTab(win, "Гра", 4));
            CycleRow(game, "Row_Language", "Мова", () => Loc.Name, Loc.Cycle);
            CycleRow(game, "Row_Advisor", "Поради «Перші кроки»", () => Advisor.Hidden ? Loc.Pick("сховано", "Hidden") : Loc.Pick("показано", "Shown"), () => Advisor.Hidden = !Advisor.Hidden);

            if (closeSettingsButton != null)
            {
                var back = (RectTransform)closeSettingsButton.transform;
                back.SetParent(win, false);
                back.anchorMin = back.anchorMax = back.pivot = new Vector2(0.5f, 0f);
                back.sizeDelta = new Vector2(260f, 52f);
                back.anchoredPosition = new Vector2(-145f, 24f);
            }
            resetKeysButton = UiTheme.TextButton(win, "ResetDefaultsBtn", "Скинути клавіші", new Vector2(0.5f, 0f), new Vector2(145f, 24f), new Vector2(260f, 52f), () =>
            {
                activeRebindAction = null;
                KeybindsManager.ResetToDefaults();
            }).gameObject;
            ShowSettingsTab(0);
            RefreshKeybindButtonTexts();
        }

        private readonly System.Collections.Generic.List<(GameObject page, Image tab)> settingsTabs = new System.Collections.Generic.List<(GameObject, Image)>();
        private GameObject resetKeysButton;
        private const int SettingsTabCount = 5;

        /// <summary>A category: its button along the top and its page under them.</summary>
        private RectTransform SettingsTab(RectTransform win, string title, int index)
        {
            // the tabs stand wholly above the page (they used to dip 16 px under it and the page cut their labels)
            const float w = 184f, gap = 10f;
            float x = (index - (SettingsTabCount - 1) * 0.5f) * (w + gap);
            Image tab = UiTheme.TextButton(win, "Tab_" + title, title, new Vector2(0.5f, 1f), new Vector2(x, -80f), new Vector2(w, 42f), () => ShowSettingsTab(index), 19).GetComponent<Image>();
            RectTransform page = Section(win, "Page_" + title, Vector2.zero, Vector2.one, new Vector2(28f, 96f), new Vector2(-28f, -132f));
            settingsTabs.Add((page.gameObject, tab));
            return page;
        }

        /// <summary>Opens a category's page (its key-reset button only with the keys).</summary>
        public void ShowSettingsTab(int index)
        {
            activeRebindAction = null;
            for (int i = 0; i < settingsTabs.Count; i++)
            {
                bool open = i == index;
                settingsTabs[i].page.SetActive(open);
                settingsTabs[i].tab.color = open ? Color.white : new Color(0.62f, 0.6f, 0.58f);
                Text label = settingsTabs[i].tab.GetComponentInChildren<Text>();
                if (label != null) label.color = open ? UiTheme.Gold : UiTheme.InkDim;
            }
            bool keys = index == 3;
            if (resetKeysButton != null) resetKeysButton.SetActive(keys);
            // Back alone stands in the middle; beside the key reset it moves left
            if (closeSettingsButton != null && closeSettingsButton.transform.parent == settingsTabs[0].page.transform.parent)
                ((RectTransform)closeSettingsButton.transform).anchoredPosition = new Vector2(keys ? -145f : 0f, 24f);
        }

        /// <summary>A category's page: a sunk box (its name is on the tab above, not repeated inside).</summary>
        private static RectTransform Section(RectTransform win, string name, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            RectTransform s = UiTheme.Child(win, name, min, max, offMin, offMax);
            UiTheme.PanelImage(s, UiTheme.SlotSprite);
            return s;
        }

        /// <summary>One slider row: name on the left, current value on the right, the slider under them.</summary>
        private static void SliderRow(RectTransform section, int index, string title, Slider slider, System.Func<float, string> format)
        {
            if (slider == null) return;
            const float h = 92f;
            RectTransform r = UiTheme.Child(section, "Row_" + slider.name, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(22f, -16f - h * (index + 1)), new Vector2(-22f, -16f - h * index));
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

        /// <summary>A list of rows scrolling under the heading; returns where the rows go.</summary>
        private static Transform ScrollList(RectTransform section)
        {
            RectTransform scrollRect = UiTheme.Child(section, "ScrollView", Vector2.zero, Vector2.one, new Vector2(14f, 14f), new Vector2(-14f, -14f));
            ScrollRect scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewportRect = UiTheme.Child(scrollRect, "Viewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewportRect.gameObject.AddComponent<RectMask2D>();
            // an invisible backdrop: the wheel and a drag work between the rows too, not only over them
            viewportRect.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            scroll.viewport = viewportRect;

            // a visible bar on the right; hidden (and the rows widen) when everything fits
            RectTransform barRect = UiTheme.Child(scrollRect, "Scrollbar", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-12f, 0f), Vector2.zero);
            Image track = UiTheme.PanelImage(barRect, UiTheme.BarSprite, new Color(0.03f, 0.025f, 0.02f, 0.95f));
            Scrollbar bar = barRect.gameObject.AddComponent<Scrollbar>();
            RectTransform slide = UiTheme.Child(barRect, "Sliding Area", Vector2.zero, Vector2.one, new Vector2(1f, 1f), new Vector2(-1f, -1f));
            RectTransform handle = UiTheme.Child(slide, "Handle", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image handleImg = UiTheme.PanelImage(handle, UiTheme.BarSprite, UiTheme.Rim);
            bar.handleRect = handle;
            bar.targetGraphic = handleImg;
            bar.direction = Scrollbar.Direction.BottomToTop;
            ColorBlock cb = bar.colors;
            cb.highlightedColor = new Color(1.25f, 1.2f, 1.1f); cb.pressedColor = new Color(0.8f, 0.75f, 0.7f); cb.colorMultiplier = 1.25f;
            bar.colors = cb;
            track.raycastTarget = true;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 6f;

            RectTransform contentRect = UiTheme.Child(viewportRect, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            contentRect.pivot = new Vector2(0.5f, 1f);
            VerticalLayoutGroup layout = contentRect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true; // rows take their LayoutElement height (without it they stayed 100 px tall)
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(4, 4, 4, 4);
            contentRect.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;
            return contentRect;
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
            LocText.Detach(text);
            text.text = value();
            cycleRefreshers.Add(() => { if (text != null) text.text = value(); });
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
            CycleRow(parent, "Row_Graphics", "Графіка", () => GraphicsQuality.CurrentName, GraphicsQuality.Cycle);
        }

        private static Text CreateRow(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            return CreateRowWithLabel(parent, name, label, onClick).value;
        }

        private static (Text label, Text value) CreateRowWithLabel(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            LayoutElement element = row.AddComponent<LayoutElement>();
            element.minHeight = element.preferredHeight = 48f;
            var rt = (RectTransform)row.transform;
            UiTheme.PanelImage(rt, UiTheme.SlotSprite, new Color(1f, 1f, 1f, 0.9f));

            Text lbl = UiTheme.Label(UiTheme.Child(rt, "Label", new Vector2(0f, 0f), new Vector2(0.62f, 1f), new Vector2(14f, 0f), Vector2.zero), "Text", label, 17, TextAnchor.MiddleLeft);
            Button btn = UiTheme.TextButton(rt, "KeyBtn", "", new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(180f, 36f), onClick, 17);
            return (lbl, btn.GetComponentInChildren<Text>());
        }

        private void StartRebinding(KeyAction action)
        {
            activeRebindAction = action;
            RefreshKeybindButtonTexts();
        }

        private void RefreshKeybindButtonTexts()
        {
            if (this == null || keybindButtonTexts == null) return;

            foreach (var kvp in keybindLabelTexts)
            {
                if (kvp.Value != null) kvp.Value.text = KeybindsManager.GetActionDisplayName(kvp.Key);
            }

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
