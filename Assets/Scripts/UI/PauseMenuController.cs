using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Manages Pause Menu UI state, settings adjustment sliders, and cursor lock states.
    /// Safely restores gameplay states upon resuming.
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenuController : MonoBehaviour
    {
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

        private void Start()
        {
            InitializeReferences();
            SetupButtonListeners();
            InitializeSliders();

            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.SetActive(false);
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
            if (UniversalInput.GetKeyDownEscape())
            {
                TogglePause();
            }
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
        }

        public void CloseSettings()
        {
            if (settingsPanel != null) settingsPanel.SetActive(false);
            if (pauseMenuPanel != null) pauseMenuPanel.SetActive(true);
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
