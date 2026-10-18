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
                gameModeManager = FindFirstObjectByType<GameModeManager>();
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
                mapSensSlider.maxValue = SettingsManager.MAX_SENSITIVITY;
                mapSensSlider.value = SettingsManager.Instance.MapSensitivity;
                mapSensSlider.onValueChanged.AddListener(OnMapSensChanged);
            }

            if (volumeSlider != null)
            {
                volumeSlider.minValue = SettingsManager.MIN_VOLUME;
                volumeSlider.maxValue = SettingsManager.MAX_VOLUME;
                volumeSlider.value = SettingsManager.Instance.MasterVolume;
                volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            }
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

        private void BuildKeybindsUI()
        {
            if (settingsPanel == null || keybindsContainerObj != null) return;

            // Shift existing settingsPanel elements to the left side to prevent overlap
            for (int i = 0; i < settingsPanel.transform.childCount; i++)
            {
                Transform child = settingsPanel.transform.GetChild(i);
                RectTransform rt = child.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Vector2 pos = rt.anchoredPosition;
                    rt.anchoredPosition = new Vector2(-220f, pos.y);
                }
            }

            Font font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            RectTransform containerRect = UiTheme.Child(settingsPanel.transform, "KeybindsPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            keybindsContainerObj = containerRect.gameObject;
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(220f, 0f);
            containerRect.sizeDelta = new Vector2(400f, 480f);

            Image bg = keybindsContainerObj.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.10f, 0.18f, 0.95f);

            // Header Title
            RectTransform headerRect = UiTheme.Child(keybindsContainerObj.transform, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f));
            GameObject headerObj = headerRect.gameObject;
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 45f);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.12f, 0.20f, 0.35f, 0.98f);

            // A GameObject holds one Graphic: the title text goes on a child of the header background
            GameObject headerTxtObj = new GameObject("Text", typeof(RectTransform));
            headerTxtObj.transform.SetParent(headerObj.transform, false);
            RectTransform headerTxtRect = headerTxtObj.GetComponent<RectTransform>();
            headerTxtRect.anchorMin = Vector2.zero; headerTxtRect.anchorMax = Vector2.one;
            headerTxtRect.offsetMin = headerTxtRect.offsetMax = Vector2.zero;
            Text headerTxt = UiTheme.PlainText(headerTxtObj, font, 18, TextAnchor.MiddleCenter, new Color(0.3f, 0.95f, 1.0f), "🎮 НАЛАШТУВАННЯ КЕРУВАННЯ", style: FontStyle.Bold);

            // Scrollable List Container
            RectTransform scrollRect = UiTheme.Child(keybindsContainerObj.transform, "ScrollView", Vector2.zero, Vector2.one, new Vector2(10f, 55f), new Vector2(-10f, -50f));
            GameObject scrollObj = scrollRect.gameObject;

            ScrollRect scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            RectTransform viewportRect = UiTheme.Child(scrollObj.transform, "Viewport", Vector2.zero, Vector2.one);
            GameObject viewportObj = viewportRect.gameObject;

            Image vpMask = viewportObj.AddComponent<Image>();
            vpMask.color = Color.white;
            Mask mask = viewportObj.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = viewportRect;

            RectTransform contentRect = UiTheme.Child(viewportObj.transform, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            GameObject contentObj = contentRect.gameObject;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);

            VerticalLayoutGroup layout = contentObj.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.padding = new RectOffset(5, 5, 5, 5);

            ContentSizeFitter fitter = contentObj.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = contentRect;

            // Create rows for each KeyAction
            foreach (KeyAction action in System.Enum.GetValues(typeof(KeyAction)))
            {
                CreateKeybindRow(contentObj.transform, font, action);
            }

            // Reset Defaults Button
            RectTransform resetRect = UiTheme.Child(keybindsContainerObj.transform, "ResetDefaultsBtn", new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.09f), Vector2.zero, Vector2.zero);
            GameObject resetBtnObj = resetRect.gameObject;

            Image resetBg = resetBtnObj.AddComponent<Image>();
            resetBg.color = new Color(0.45f, 0.18f, 0.18f, 0.95f);

            Button resetBtn = resetBtnObj.AddComponent<Button>();
            ColorBlock cb = resetBtn.colors;
            cb.normalColor = new Color(0.45f, 0.18f, 0.18f, 0.95f);
            cb.highlightedColor = new Color(0.65f, 0.25f, 0.25f, 1.0f);
            cb.pressedColor = new Color(0.35f, 0.10f, 0.10f, 1.0f);
            resetBtn.colors = cb;
            resetBtn.onClick.AddListener(() =>
            {
                activeRebindAction = null;
                KeybindsManager.ResetToDefaults();
            });

            RectTransform resetTxtRect = UiTheme.Child(resetBtnObj.transform, "Text", Vector2.zero, Vector2.one);
            GameObject resetTxtObj = resetTxtRect.gameObject;

            Text resetTxt = UiTheme.PlainText(resetTxtObj, font, 15, TextAnchor.MiddleCenter, Color.white, "🔄 Скинути за замовчуванням", style: FontStyle.Bold);

            RefreshKeybindButtonTexts();
        }

        private void CreateKeybindRow(Transform parent, Font font, KeyAction action)
        {
            GameObject rowObj = new GameObject($"Row_{action}", typeof(RectTransform));
            rowObj.transform.SetParent(parent, false);

            LayoutElement element = rowObj.AddComponent<LayoutElement>();
            element.minHeight = 44f;
            element.preferredHeight = 44f;

            Image rowBg = rowObj.AddComponent<Image>();
            rowBg.color = new Color(0.10f, 0.15f, 0.25f, 0.9f);

            // Label
            RectTransform labelRect = UiTheme.Child(rowObj.transform, "Label", new Vector2(0.03f, 0f), new Vector2(0.65f, 1f), Vector2.zero, Vector2.zero);
            GameObject labelObj = labelRect.gameObject;

            Text labelTxt = UiTheme.PlainText(labelObj, font, 14, TextAnchor.MiddleLeft, Color.white, KeybindsManager.GetActionDisplayName(action), style: FontStyle.Bold);

            // Key Button
            RectTransform btnRect = UiTheme.Child(rowObj.transform, "KeyBtn", new Vector2(0.67f, 0.10f), new Vector2(0.97f, 0.90f), Vector2.zero, Vector2.zero);
            GameObject btnObj = btnRect.gameObject;

            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = new Color(0.18f, 0.35f, 0.55f, 0.95f);

            Button btn = btnObj.AddComponent<Button>();
            KeyAction capturedAction = action;
            btn.onClick.AddListener(() => StartRebinding(capturedAction));

            RectTransform txtRect = UiTheme.Child(btnObj.transform, "Text", Vector2.zero, Vector2.one);
            GameObject txtObj = txtRect.gameObject;

            Text keyTxt = UiTheme.PlainText(txtObj, font, 15, TextAnchor.MiddleCenter, new Color(0.3f, 0.95f, 1.0f), style: FontStyle.Bold);

            keybindButtonTexts[action] = keyTxt;
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
                    txt.color = Color.yellow;
                }
                else
                {
                    KeyCode key = KeybindsManager.GetKey(action);
                    txt.text = $"[ {KeybindsManager.GetKeyDisplayString(key)} ]";
                    txt.color = new Color(0.3f, 0.95f, 1.0f);
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
