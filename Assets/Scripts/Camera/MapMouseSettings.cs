using UnityEngine;

namespace BattleOfAges.CameraSystem
{
    /// <summary>
    /// Which mouse button drags the map (a tester's wish, 2026-11-17: in such games the middle one is usual). Right (the
    /// default, as it always was): the right button drags the map, the middle one turns the camera. Middle: the middle button
    /// drags the map, Shift with it turns the camera, and the right button is left to orders. Settings → Game → "Рух мапи".
    /// </summary>
    public static class MapMouseSettings
    {
        private const string Key = "Settings_MapPanMiddle";

        /// <summary>The middle button drags the map (else the right one).</summary>
        public static bool PanWithMiddle { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load() => PanWithMiddle = PlayerPrefs.GetInt(Key, 0) != 0;

        public static void Toggle() => Set(!PanWithMiddle);

        public static void Set(bool middle)
        {
            PanWithMiddle = middle;
            PlayerPrefs.SetInt(Key, middle ? 1 : 0);
        }

        /// <summary>The button that drags the map (1 right, 2 middle).</summary>
        public static int PanButton => PanWithMiddle ? 2 : 1;

        /// <summary>Whether the camera turns now: the middle button alone, or with Shift when the middle one drags the map.</summary>
        public static bool Turning(bool middleHeld, bool shiftHeld) => middleHeld && (!PanWithMiddle || shiftHeld);

        /// <summary>Whether the map is dragged now: the right button without Shift, or the middle one without Shift.</summary>
        public static bool Panning(bool rightHeld, bool middleHeld, bool shiftHeld) => !shiftHeld && (PanWithMiddle ? middleHeld : rightHeld);
    }
}
