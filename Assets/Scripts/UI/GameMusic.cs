using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Dynamic adaptive music system with dual-source crossfading:
    /// - Menu: heroic medieval theme when in the main menu (!SaveSystem.InGame).
    /// - PeaceDay: warm Dorian lute & pastoral flute exploration music by day.
    /// - PeaceNight: calm, nocturnal ambient lute & drone by night.
    /// - Combat: intense war drums & brass/strings when SettlementAlarm.Active or GameSound.BattleIntensity >= 0.25.
    /// Loads CC0 music tracks from Resources/Audio/Music/ when present, with procedural Karplus-Strong & modal synthesis
    /// as fallback and for unit tests.
    /// </summary>
    public sealed class GameMusic : MonoBehaviour
    {
        public enum Mood { Menu, PeaceDay, PeaceNight, Combat }

        private const float BaseGain = 0.28f;
        private const float CrossfadeSpeed = 0.4f; // ~2.5 s crossfade
        private const float CombatHoldSeconds = 8f;

        private AudioSource activeSource, fadingSource;
        private AudioClip menuClip, peaceDayClip, peaceNightClip, combat1Clip, combat2Clip;
        private Mood currentMood = (Mood)(-1);
        private float crossfade = 1f;
        private float combatHoldUntil = float.NegativeInfinity;
        private int combatToggle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || FindFirstObjectByType<GameMusic>() != null) return;
            var go = new GameObject("GameMusic");
            DontDestroyOnLoad(go);
            go.AddComponent<GameMusic>();
        }

        private AudioSource CreateSource(string label)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.volume = 0f;
            return s;
        }

        private static AudioClip LoadOrSynth(string resourceName, Mood fallbackMood)
        {
            AudioClip c = UnityEngine.Resources.Load<AudioClip>("Audio/Music/" + resourceName);
            if (c != null) return c;
            float[] d = Synth(fallbackMood);
            c = AudioClip.Create("Music_" + resourceName, d.Length, 1, GameSound.Rate, false);
            c.SetData(d, 0);
            return c;
        }

        private void Start()
        {
            activeSource = CreateSource("MusicA");
            fadingSource = CreateSource("MusicB");

            menuClip = LoadOrSynth("Menu", Mood.Menu);
            peaceDayClip = LoadOrSynth("PeaceDay", Mood.PeaceDay);
            peaceNightClip = LoadOrSynth("PeaceNight", Mood.PeaceNight);
            combat1Clip = LoadOrSynth("Combat1", Mood.Combat);
            combat2Clip = UnityEngine.Resources.Load<AudioClip>("Audio/Music/Combat2");
            if (combat2Clip == null) combat2Clip = combat1Clip;

            SwitchMood(DesiredMood(), immediate: true);
        }

        public Mood DesiredMood()
        {
            if (!SaveLoad.SaveSystem.InGame) return Mood.Menu;
            if (RTS.SettlementAlarm.Active || GameSound.BattleIntensity >= 0.25f)
            {
                combatHoldUntil = Time.unscaledTime + CombatHoldSeconds;
                return Mood.Combat;
            }
            if (Time.unscaledTime < combatHoldUntil) return Mood.Combat;
            return Ambience.IsNight(WorldClock.HourOfDay) ? Mood.PeaceNight : Mood.PeaceDay;
        }

        private AudioClip ClipFor(Mood mood)
        {
            switch (mood)
            {
                case Mood.Menu: return menuClip;
                case Mood.PeaceNight: return peaceNightClip;
                case Mood.Combat: return (combatToggle++ & 1) == 0 ? combat1Clip : combat2Clip;
                default: return peaceDayClip;
            }
        }

        private void SwitchMood(Mood next, bool immediate)
        {
            if (next == currentMood) return;
            currentMood = next;
            AudioClip clip = ClipFor(next);
            if (clip == null) return;

            if (immediate)
            {
                activeSource.clip = clip;
                activeSource.Play();
                fadingSource.Stop();
                crossfade = 1f;
                return;
            }

            // Swap active and fading
            AudioSource tmp = activeSource;
            activeSource = fadingSource;
            fadingSource = tmp;

            activeSource.clip = clip;
            activeSource.volume = 0f;
            activeSource.Play();
            crossfade = 0f;
        }

        private void Update()
        {
            Mood desired = DesiredMood();
            if (desired != currentMood) SwitchMood(desired, immediate: false);

            crossfade = Mathf.MoveTowards(crossfade, 1f, Time.unscaledDeltaTime * CrossfadeSpeed);
            float pauseFactor = Time.timeScale <= 0f && SaveLoad.SaveSystem.InGame ? 0.55f : 1f;
            float targetGain = BaseGain * Managers.SettingsManager.MusicShare * GameSound.DuckShare * pauseFactor;

            if (activeSource != null) activeSource.volume = targetGain * crossfade;
            if (fadingSource != null)
            {
                fadingSource.volume = targetGain * (1f - crossfade);
                if (crossfade >= 1f && fadingSource.isPlaying) fadingSource.Stop();
            }
        }

        /// <summary>
        /// Procedural medieval modal music synthesis (Dorian scale in D: D, E, F, G, A, B, C) using Karplus-Strong
        /// plucked strings, soft drone fifths, and war drums. Used as fallback and verified in EditMode tests.
        /// </summary>
        public static float[] Synth(Mood mood)
        {
            int rate = GameSound.Rate;
            float seconds = 8f;
            int n = (int)(seconds * rate), fade = rate / 2;
            var d = new float[n + fade];
            var g = new GameSound.Gen(900 + (int)mood * 37);

            // D Dorian frequencies
            float[] scale = { 146.83f, 164.81f, 174.61f, 196.00f, 220.00f, 246.94f, 261.63f, 293.66f, 329.63f, 349.23f, 392.00f, 440.00f };

            if (mood == Mood.Combat)
            {
                // Driving war drums + aggressive low brass/string ostinato
                float beat = 0.25f;
                int steps = (int)(seconds / beat);
                int[] ostinato = { 0, 0, 3, 4, 0, 2, 3, 1 };
                for (int s = 0; s < steps; s++)
                {
                    float t0 = s * beat;
                    if (s % 2 == 0) g.Drop(d, t0, 95f, 48f, 0.65f, 10f); // heavy war drum
                    if (s % 4 == 2) g.Band(d, t0, 0.12f, 1200f, 2.2f, 0.28f, 18f); // rim/snare hit
                    float f = scale[ostinato[s % ostinato.Length]];
                    g.Pluck(d, t0, 0.24f, f, 0.38f, 0.994f);
                    if (s % 4 == 0) g.Horn(d, t0, 0.45f, f * 0.5f, 0.35f);
                }
            }
            else if (mood == Mood.PeaceNight)
            {
                // Slow, contemplative nocturnal lute arpeggios + warm fifth drone
                g.Horn(d, 0f, seconds + 0.4f, 73.42f, 0.14f);
                g.Horn(d, 0f, seconds + 0.4f, 110.00f, 0.11f);
                int[] notes = { 0, 4, 7, 6, 4, 2, 4, 0 };
                float step = seconds / notes.Length;
                for (int i = 0; i < notes.Length; i++)
                {
                    float f = scale[notes[i]];
                    g.Pluck(d, i * step, step * 1.35f, f * 2f, 0.36f, 0.997f);
                }
            }
            else
            {
                // Menu / PeaceDay: bright medieval lute chords + Dorian flute/horn melody
                g.Horn(d, 0f, seconds + 0.4f, 146.83f, 0.13f);
                int[] lute = { 0, 4, 7, 4, 2, 6, 9, 6, 3, 7, 10, 7, 4, 7, 11, 7 };
                float step = seconds / lute.Length;
                for (int i = 0; i < lute.Length; i++)
                {
                    float f = scale[lute[i]];
                    g.Pluck(d, i * step, step * 1.2f, f * (mood == Mood.Menu ? 1.5f : 2f), 0.42f, 0.996f);
                    if (i % 4 == 0)
                        g.Horn(d, i * step, step * 3.2f, f * 2f, 0.20f, vibrato: true);
                }
            }

            // Crossfade loop seam
            var loop = new float[n];
            for (int i = 0; i < n; i++) loop[i] = d[i];
            for (int i = 0; i < fade && n + i < d.Length; i++)
            {
                float u = i / (float)fade;
                loop[i] = d[i] * u + d[n + i] * (1f - u);
            }
            float peak = 0f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(loop[i]));
            if (peak > 1e-6f) for (int i = 0; i < n; i++) loop[i] *= 0.85f / peak;
            return loop;
        }
    }
}
