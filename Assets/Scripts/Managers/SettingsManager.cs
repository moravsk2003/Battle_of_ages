using System;
using UnityEngine;

namespace BattleOfAges.Managers
{
    /// <summary>
    /// Global settings manager handling configuration persistent storage and change notifications.
    /// Uses deferred saving to avoid I/O disk stuttering during UI adjustments.
    /// </summary>
    [DisallowMultipleComponent]
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        private static class PlayerPrefsKeys
        {
            public const string FPS_SENSITIVITY = "Settings_FpsSensitivity";
            public const string MAP_SENSITIVITY = "Settings_MapSensitivity";
            public const string MASTER_VOLUME = "Settings_MasterVolume";
        }

        public const float MIN_SENSITIVITY = 0.1f;
        public const float MAX_SENSITIVITY = 8.0f;
        public const float MIN_VOLUME = 0.0f;
        public const float MAX_VOLUME = 1.0f;

        [Header("Default Settings")]
        [SerializeField, Range(MIN_SENSITIVITY, MAX_SENSITIVITY)] private float defaultFpsSensitivity = 1.0f;
        [SerializeField, Range(MIN_SENSITIVITY, MAX_SENSITIVITY)] private float defaultMapSensitivity = 1.0f;
        [SerializeField, Range(MIN_VOLUME, MAX_VOLUME)] private float defaultMasterVolume = 1.0f;

        public float FpsSensitivity { get; private set; }
        public float MapSensitivity { get; private set; }
        public float MasterVolume { get; private set; }

        public event Action<float> OnFpsSensitivityChanged;
        public event Action<float> OnMapSensitivityChanged;
        public event Action<float> OnMasterVolumeChanged;

        private bool isDirty = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSettings();
        }

        private void OnDisable()
        {
            SavePendingSettings();
        }

        private void OnApplicationQuit()
        {
            SavePendingSettings();
        }

        public void LoadSettings()
        {
            FpsSensitivity = PlayerPrefs.GetFloat(PlayerPrefsKeys.FPS_SENSITIVITY, defaultFpsSensitivity);
            MapSensitivity = PlayerPrefs.GetFloat(PlayerPrefsKeys.MAP_SENSITIVITY, defaultMapSensitivity);
            MasterVolume = PlayerPrefs.GetFloat(PlayerPrefsKeys.MASTER_VOLUME, defaultMasterVolume);

            AudioListener.volume = MasterVolume;
        }

        public void SetFpsSensitivity(float value)
        {
            float clamped = Mathf.Clamp(value, MIN_SENSITIVITY, MAX_SENSITIVITY);
            if (Mathf.Approximately(FpsSensitivity, clamped)) return;

            FpsSensitivity = clamped;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.FPS_SENSITIVITY, FpsSensitivity);
            isDirty = true;

            OnFpsSensitivityChanged?.Invoke(FpsSensitivity);
        }

        public void SetMapSensitivity(float value)
        {
            float clamped = Mathf.Clamp(value, MIN_SENSITIVITY, MAX_SENSITIVITY);
            if (Mathf.Approximately(MapSensitivity, clamped)) return;

            MapSensitivity = clamped;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.MAP_SENSITIVITY, MapSensitivity);
            isDirty = true;

            OnMapSensitivityChanged?.Invoke(MapSensitivity);
        }

        public void SetMasterVolume(float value)
        {
            float clamped = Mathf.Clamp(value, MIN_VOLUME, MAX_VOLUME);
            if (Mathf.Approximately(MasterVolume, clamped)) return;

            MasterVolume = clamped;
            AudioListener.volume = MasterVolume;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.MASTER_VOLUME, MasterVolume);
            isDirty = true;

            OnMasterVolumeChanged?.Invoke(MasterVolume);
        }

        /// <summary>
        /// Flushes pending changes to disk.
        /// </summary>
        public void SavePendingSettings()
        {
            if (isDirty)
            {
                PlayerPrefs.Save();
                isDirty = false;
            }
        }
    }
}
