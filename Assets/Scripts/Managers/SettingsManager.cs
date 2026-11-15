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
            public const string FPS_SENSITIVITY = "Settings_FpsSensitivity2"; // new scale (FirstPersonController.LookScale): old values start over at 1
            public const string MAP_SENSITIVITY = "Settings_MapSensitivity";
            public const string MASTER_VOLUME = "Settings_MasterVolume";
            public const string MUSIC_VOLUME = "Settings_MusicVolume";
            public const string SFX_VOLUME = "Settings_SfxVolume";
            public const string AMBIENCE_VOLUME = "Settings_AmbienceVolume";
            public const string MAP_ZOOM_SENSITIVITY = "Settings_MapZoomSensitivity";
        }

        public const float MIN_SENSITIVITY = 0.1f;
        public const float MAX_SENSITIVITY = 15.0f;
        public const float MIN_VOLUME = 0.0f;
        public const float MAX_VOLUME = 1.0f;
        /// <summary>The map camera has its own top (it pans a whole map), and its wheel zoom its own speed.</summary>
        public const float MAX_MAP_SENSITIVITY = 12.0f;
        public const float MIN_ZOOM_SENSITIVITY = 0.2f, MAX_ZOOM_SENSITIVITY = 5.0f;

        [Header("Default Settings")]
        [SerializeField, Range(MIN_SENSITIVITY, MAX_SENSITIVITY)] private float defaultFpsSensitivity = 1.0f;
        [SerializeField, Range(MIN_SENSITIVITY, MAX_MAP_SENSITIVITY)] private float defaultMapSensitivity = 1.0f;
        [SerializeField, Range(MIN_VOLUME, MAX_VOLUME)] private float defaultMasterVolume = 1.0f;
        [SerializeField, Range(MIN_VOLUME, MAX_VOLUME)] private float defaultMusicVolume = 0.85f;
        [SerializeField, Range(MIN_VOLUME, MAX_VOLUME)] private float defaultSfxVolume = 1.0f;
        [SerializeField, Range(MIN_VOLUME, MAX_VOLUME)] private float defaultAmbienceVolume = 0.9f;
        [SerializeField, Range(MIN_ZOOM_SENSITIVITY, MAX_ZOOM_SENSITIVITY)] private float defaultZoomSensitivity = 1.0f;

        public float FpsSensitivity { get; private set; }
        public float MapSensitivity { get; private set; }
        public float MasterVolume { get; private set; } = 1.0f;
        public float MusicVolume { get; private set; } = 0.85f;
        public float SfxVolume { get; private set; } = 1.0f;
        public float AmbienceVolume { get; private set; } = 0.9f;
        public float ZoomSensitivity { get; private set; }

        /// <summary>Channel multipliers (safe when SettingsManager is not yet spawned).</summary>
        public static float MusicShare => Instance != null ? Instance.MusicVolume : 0.85f;
        public static float SfxShare => Instance != null ? Instance.SfxVolume : 1.0f;
        public static float AmbienceShare => Instance != null ? Instance.AmbienceVolume : 0.9f;

        public event Action<float> OnFpsSensitivityChanged;
        public event Action<float> OnMapSensitivityChanged;
        public event Action<float> OnMasterVolumeChanged;
        public event Action<float> OnMusicVolumeChanged;
        public event Action<float> OnSfxVolumeChanged;
        public event Action<float> OnAmbienceVolumeChanged;
        public event Action<float> OnZoomSensitivityChanged;

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
            MusicVolume = PlayerPrefs.GetFloat(PlayerPrefsKeys.MUSIC_VOLUME, defaultMusicVolume);
            SfxVolume = PlayerPrefs.GetFloat(PlayerPrefsKeys.SFX_VOLUME, defaultSfxVolume);
            AmbienceVolume = PlayerPrefs.GetFloat(PlayerPrefsKeys.AMBIENCE_VOLUME, defaultAmbienceVolume);
            ZoomSensitivity = PlayerPrefs.GetFloat(PlayerPrefsKeys.MAP_ZOOM_SENSITIVITY, defaultZoomSensitivity);

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
            float clamped = Mathf.Clamp(value, MIN_SENSITIVITY, MAX_MAP_SENSITIVITY);
            if (Mathf.Approximately(MapSensitivity, clamped)) return;

            MapSensitivity = clamped;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.MAP_SENSITIVITY, MapSensitivity);
            isDirty = true;

            OnMapSensitivityChanged?.Invoke(MapSensitivity);
        }

        public void SetZoomSensitivity(float value)
        {
            float clamped = Mathf.Clamp(value, MIN_ZOOM_SENSITIVITY, MAX_ZOOM_SENSITIVITY);
            if (Mathf.Approximately(ZoomSensitivity, clamped)) return;

            ZoomSensitivity = clamped;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.MAP_ZOOM_SENSITIVITY, ZoomSensitivity);
            isDirty = true;

            OnZoomSensitivityChanged?.Invoke(ZoomSensitivity);
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

        public void SetMusicVolume(float value)
        {
            float clamped = Mathf.Clamp(value, MIN_VOLUME, MAX_VOLUME);
            if (Mathf.Approximately(MusicVolume, clamped)) return;

            MusicVolume = clamped;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.MUSIC_VOLUME, MusicVolume);
            isDirty = true;

            OnMusicVolumeChanged?.Invoke(MusicVolume);
        }

        public void SetSfxVolume(float value)
        {
            float clamped = Mathf.Clamp(value, MIN_VOLUME, MAX_VOLUME);
            if (Mathf.Approximately(SfxVolume, clamped)) return;

            SfxVolume = clamped;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.SFX_VOLUME, SfxVolume);
            isDirty = true;

            OnSfxVolumeChanged?.Invoke(SfxVolume);
        }

        public void SetAmbienceVolume(float value)
        {
            float clamped = Mathf.Clamp(value, MIN_VOLUME, MAX_VOLUME);
            if (Mathf.Approximately(AmbienceVolume, clamped)) return;

            AmbienceVolume = clamped;
            PlayerPrefs.SetFloat(PlayerPrefsKeys.AMBIENCE_VOLUME, AmbienceVolume);
            isDirty = true;

            OnAmbienceVolumeChanged?.Invoke(AmbienceVolume);
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
