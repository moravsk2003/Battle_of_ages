using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;

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

            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);

            KeybindsManager.OnKeybindsChanged += RefreshKeybindButtonTexts;
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
                if (IsAnyModalOpen())
                {
                    CloseAllModalUIs();
                    return;
                }

                TogglePause();
            }
        }

        private bool IsAnyModalOpen()
        {
            return (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) ||
                   (NpcGatherCommandUI.Instance != null && NpcGatherCommandUI.Instance.IsOpen) ||
                   (BuildMenuUI.Instance != null && BuildMenuUI.Instance.IsOpen);
        }

        private void CloseAllModalUIs()
        {
            if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) InventoryUI.Instance.CloseInventory();
            if (NpcGatherCommandUI.Instance != null && NpcGatherCommandUI.Instance.IsOpen) NpcGatherCommandUI.Instance.CloseMenu();
            if (BuildMenuUI.Instance != null && BuildMenuUI.Instance.IsOpen) BuildMenuUI.Instance.CloseMenu();
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

            keybindsContainerObj = new GameObject("KeybindsPanel", typeof(RectTransform));
            keybindsContainerObj.transform.SetParent(settingsPanel.transform, false);

            RectTransform containerRect = keybindsContainerObj.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = new Vector2(220f, 0f);
            containerRect.sizeDelta = new Vector2(400f, 480f);

            Image bg = keybindsContainerObj.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.10f, 0.18f, 0.95f);

            // Header Title
            GameObject headerObj = new GameObject("Title", typeof(RectTransform));
            headerObj.transform.SetParent(keybindsContainerObj.transform, false);
            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
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
            Text headerTxt = headerTxtObj.AddComponent<Text>();
            headerTxt.font = font;
            headerTxt.fontSize = 18;
            headerTxt.fontStyle = FontStyle.Bold;
            headerTxt.alignment = TextAnchor.MiddleCenter;
            headerTxt.color = new Color(0.3f, 0.95f, 1.0f);
            headerTxt.text = "🎮 НАЛАШТУВАННЯ КЕРУВАННЯ";

            // Scrollable List Container
            GameObject scrollObj = new GameObject("ScrollView", typeof(RectTransform));
            scrollObj.transform.SetParent(keybindsContainerObj.transform, false);
            RectTransform scrollRect = scrollObj.GetComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(10f, 55f);
            scrollRect.offsetMax = new Vector2(-10f, -50f);

            ScrollRect scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform));
            viewportObj.transform.SetParent(scrollObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;

            Image vpMask = viewportObj.AddComponent<Image>();
            vpMask.color = Color.white;
            Mask mask = viewportObj.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = viewportRect;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            RectTransform contentRect = contentObj.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
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
            GameObject resetBtnObj = new GameObject("ResetDefaultsBtn", typeof(RectTransform));
            resetBtnObj.transform.SetParent(keybindsContainerObj.transform, false);
            RectTransform resetRect = resetBtnObj.GetComponent<RectTransform>();
            resetRect.anchorMin = new Vector2(0.05f, 0.02f);
            resetRect.anchorMax = new Vector2(0.95f, 0.09f);
            resetRect.offsetMin = Vector2.zero;
            resetRect.offsetMax = Vector2.zero;

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

            GameObject resetTxtObj = new GameObject("Text", typeof(RectTransform));
            resetTxtObj.transform.SetParent(resetBtnObj.transform, false);
            RectTransform resetTxtRect = resetTxtObj.GetComponent<RectTransform>();
            resetTxtRect.anchorMin = Vector2.zero;
            resetTxtRect.anchorMax = Vector2.one;

            Text resetTxt = resetTxtObj.AddComponent<Text>();
            resetTxt.font = font;
            resetTxt.fontSize = 15;
            resetTxt.fontStyle = FontStyle.Bold;
            resetTxt.alignment = TextAnchor.MiddleCenter;
            resetTxt.color = Color.white;
            resetTxt.text = "🔄 Скинути за замовчуванням";

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
            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(rowObj.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.03f, 0f);
            labelRect.anchorMax = new Vector2(0.65f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text labelTxt = labelObj.AddComponent<Text>();
            labelTxt.font = font;
            labelTxt.fontSize = 14;
            labelTxt.fontStyle = FontStyle.Bold;
            labelTxt.alignment = TextAnchor.MiddleLeft;
            labelTxt.color = Color.white;
            labelTxt.text = KeybindsManager.GetActionDisplayName(action);

            // Key Button
            GameObject btnObj = new GameObject("KeyBtn", typeof(RectTransform));
            btnObj.transform.SetParent(rowObj.transform, false);
            RectTransform btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.67f, 0.10f);
            btnRect.anchorMax = new Vector2(0.97f, 0.90f);
            btnRect.offsetMin = Vector2.zero;
            btnRect.offsetMax = Vector2.zero;

            Image btnBg = btnObj.AddComponent<Image>();
            btnBg.color = new Color(0.18f, 0.35f, 0.55f, 0.95f);

            Button btn = btnObj.AddComponent<Button>();
            KeyAction capturedAction = action;
            btn.onClick.AddListener(() => StartRebinding(capturedAction));

            GameObject txtObj = new GameObject("Text", typeof(RectTransform));
            txtObj.transform.SetParent(btnObj.transform, false);
            RectTransform txtRect = txtObj.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;

            Text keyTxt = txtObj.AddComponent<Text>();
            keyTxt.font = font;
            keyTxt.fontSize = 15;
            keyTxt.fontStyle = FontStyle.Bold;
            keyTxt.alignment = TextAnchor.MiddleCenter;
            keyTxt.color = new Color(0.3f, 0.95f, 1.0f);

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
