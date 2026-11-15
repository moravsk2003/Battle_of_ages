using UnityEngine;

namespace BattleOfAges.Player
{
    /// <summary>
    /// Run or walk in first person (the author's word, 2026-11-15): with auto-run on (the default) the hero runs and holding
    /// the run/walk key (Shift) walks; with it off the hero walks and the key runs. Settings → Game → "Автобіг".
    /// </summary>
    public static class MoveSettings
    {
        private const string Key = "Settings_AutoRun";

        /// <summary>Running is the way of going unless the key is held.</summary>
        public static bool AutoRun { get; private set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load() => AutoRun = PlayerPrefs.GetInt(Key, 1) != 0;

        public static void ToggleAutoRun() => SetAutoRun(!AutoRun);

        public static void SetAutoRun(bool on)
        {
            AutoRun = on;
            PlayerPrefs.SetInt(Key, on ? 1 : 0);
        }

        /// <summary>Whether the hero runs now: auto-run flips what the held key means.</summary>
        public static bool Running(bool keyHeld) => AutoRun ? !keyHeld : keyHeld;
    }
}
