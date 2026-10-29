using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// The one world clock (docs/GAME_DESIGN.md §4): every timer of the economy, growth and healing counts game hours
    /// from here, not frames. One pace for everybody; a game hour is GameBalance.GameHourSeconds real seconds.
    /// </summary>
    public static class WorldClock
    {
        /// <summary>Game hours since the campaign started.</summary>
        public static double GameHours { get; set; }

        public static float HoursOf(float realSeconds) => realSeconds / GameBalance.GameHourSeconds;

        /// <summary>A campaign starts at this hour of its first day (morning: the day and night of §4, M1).</summary>
        public const float DayStartHour = 8f;
        /// <summary>The hour of the day, 0..24.</summary>
        public static float HourOfDay => (float)((GameHours + DayStartHour) % 24.0);
        /// <summary>The day of the campaign, from 1.</summary>
        public static int Day => (int)((GameHours + DayStartHour) / 24.0) + 1;

        // Natural regeneration runs over every fighter once per real second (cheap for thousands of units)
        private const float RegenInterval = 1f;
        private static readonly List<HealthComponent> fighters = new List<HealthComponent>(256);
        private static float regenTimer;

        /// <summary>Units and heroes heal naturally (§11.2); buildings and wildlife do not register.</summary>
        public static void RegisterFighter(HealthComponent health) { if (health != null && !fighters.Contains(health)) fighters.Add(health); }
        public static void UnregisterFighter(HealthComponent health) => fighters.Remove(health);

        public static void Advance(float realSeconds)
        {
            GameHours += HoursOf(realSeconds);
            regenTimer += realSeconds;
            if (regenTimer < RegenInterval) return;
            float hours = HoursOf(regenTimer);
            regenTimer = 0f;
            for (int i = fighters.Count - 1; i >= 0; i--)
            {
                if (fighters[i] == null) { fighters.RemoveAt(i); continue; }
                fighters[i].Regenerate(hours);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { GameHours = 0; fighters.Clear(); regenTimer = 0f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateDriver()
        {
            if (Object.FindAnyObjectByType<WorldClockDriver>() != null) return;
            GameObject go = new GameObject("WorldClock");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<WorldClockDriver>();
        }
    }
}
