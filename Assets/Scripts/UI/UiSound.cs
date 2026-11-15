using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Interface sounds made in code (no audio assets): a click, a soft hover tick, a paper swish for opening, an error
    /// buzz, an alarm bell, a short chime for a goal done. One 2D source that ignores the pause; the master volume
    /// (AudioListener) applies. Repeats of one sound closer than MinGap are dropped.
    /// </summary>
    public static class UiSound
    {
        public enum Kind { Click, Hover, Open, Error, Alert, Success }

        private const int Rate = 44100;
        private const float MinGap = 0.05f;
        private static AudioSource source;
        private static readonly Dictionary<Kind, AudioClip> clips = new Dictionary<Kind, AudioClip>();
        private static readonly Dictionary<Kind, float> lastPlayed = new Dictionary<Kind, float>();

        /// <summary>How loud each sound is next to the others.</summary>
        private static float Volume(Kind k) => k == Kind.Hover ? 0.12f : k == Kind.Click ? 0.35f : k == Kind.Open ? 0.25f : k == Kind.Alert ? 0.55f : 0.4f;

        public static void Play(Kind kind)
        {
            if (!Application.isPlaying || Application.isBatchMode) return;
            if (lastPlayed.TryGetValue(kind, out float t) && Time.unscaledTime - t < MinGap) return;
            lastPlayed[kind] = Time.unscaledTime;
            if (source == null)
            {
                var go = new GameObject("UiSound");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.ignoreListenerPause = true;
            }
            float vol = Volume(kind) * Managers.SettingsManager.AmbienceShare;
            if (vol <= 0.005f) return;
            if (kind == Kind.Alert) GameSound.Duck(1.2f);
            source.PlayOneShot(Clip(kind), vol);
        }

        public static AudioClip Clip(Kind kind)
        {
            if (clips.TryGetValue(kind, out AudioClip c) && c != null) return c;
            c = UnityEngine.Resources.Load<AudioClip>("Audio/Ui/" + kind);
            if (c == null)
            {
                float[] data = Make(kind);
                c = AudioClip.Create("Ui_" + kind, data.Length, 1, Rate, false);
                c.SetData(data, 0);
            }
            clips[kind] = c;
            return c;
        }

        public static float[] Synth(Kind kind) => Make(kind);

        private static float[] Make(Kind kind)
        {
            var rng = new System.Random((int)kind + 7);
            float Noise() => (float)rng.NextDouble() * 2f - 1f;
            float[] d;
            switch (kind)
            {
                case Kind.Click: // warm wooden-parchment tap: resonant wood modes and gentle touch noise
                {
                    d = new float[(int)(Rate * 0.055f)];
                    float lp = 0f;
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float tapEnv = Mathf.Clamp01(t / 0.002f) * Mathf.Exp(-t * 85f);
                        lp += (Noise() - lp) * 0.25f;
                        float wood = Mathf.Sin(2f * Mathf.PI * 480f * t) * 0.55f + Mathf.Sin(2f * Mathf.PI * 1180f * t) * 0.30f;
                        d[i] = (lp * 0.45f + wood) * tapEnv * 0.85f;
                    }
                    break;
                }
                case Kind.Hover: // gentle low-mid wood/parchment touch
                {
                    d = new float[(int)(Rate * 0.038f)];
                    float lp = 0f;
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate, u = t / 0.038f;
                        lp += (Noise() - lp) * 0.18f;
                        d[i] = (Mathf.Sin(2f * Mathf.PI * 680f * t) * 0.55f + lp * 0.35f) * Mathf.Sin(Mathf.PI * u) * Mathf.Exp(-t * 60f) * 0.7f;
                    }
                    break;
                }
                case Kind.Open: // a paper swish: band noise rising and falling
                {
                    d = new float[(int)(Rate * 0.16f)];
                    float lp = 0f, hp = 0f;
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate, u = t / 0.16f;
                        float n = Noise();
                        lp += (n - lp) * Mathf.Lerp(0.08f, 0.4f, u);
                        hp = lp - hp * 0.2f;
                        d[i] = hp * Mathf.Sin(Mathf.PI * u) * 0.7f;
                    }
                    break;
                }
                case Kind.Error: // dull double wooden knock (no electronic buzz)
                {
                    d = new float[(int)(Rate * 0.26f)];
                    float lp = 0f;
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        lp += (Noise() - lp) * 0.12f;
                        float s = 0f;
                        for (int k = 0; k < 2; k++)
                        {
                            float tt = t - k * 0.11f;
                            if (tt < 0f) continue;
                            float pitch = k == 0 ? 210f : 145f;
                            float thud = (Mathf.Sin(2f * Mathf.PI * pitch * tt) * 0.65f + Mathf.Sin(2f * Mathf.PI * pitch * 1.48f * tt) * 0.25f + lp * 0.35f)
                                         * Mathf.Clamp01(tt / 0.002f) * Mathf.Exp(-tt * 28f);
                            s += thud;
                        }
                        d[i] = s * 0.8f;
                    }
                    break;
                }
                case Kind.Alert: // a bell: inharmonic partials with long decays, struck twice
                {
                    d = new float[(int)(Rate * 1.6f)];
                    float[] f = { 440f, 1056f, 1430f, 1880f, 2650f };
                    float[] a = { 0.55f, 0.3f, 0.22f, 0.12f, 0.08f };
                    float[] k = { 2.2f, 3.5f, 5f, 7f, 10f };
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate, s = 0f;
                        foreach (float strike in new[] { 0f, 0.45f })
                        {
                            float tt = t - strike;
                            if (tt < 0f) continue;
                            for (int p = 0; p < f.Length; p++) s += Mathf.Sin(2f * Mathf.PI * f[p] * tt) * a[p] * Mathf.Exp(-tt * k[p]);
                        }
                        d[i] = s * 0.6f;
                    }
                    break;
                }
                default: // Success: two rising notes
                {
                    d = new float[(int)(Rate * 0.4f)];
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate;
                        float n1 = Mathf.Sin(2f * Mathf.PI * 784f * t) * Mathf.Exp(-t * 9f);
                        float t2 = t - 0.11f;
                        float n2 = t2 > 0f ? Mathf.Sin(2f * Mathf.PI * 1175f * t2) * Mathf.Exp(-t2 * 7f) : 0f;
                        d[i] = (n1 + n2) * 0.4f;
                    }
                    break;
                }
            }
            // fade the last few ms so nothing clicks at the end
            int fade = Mathf.Min(220, d.Length);
            for (int i = 0; i < fade; i++) d[d.Length - 1 - i] *= i / (float)fade;
            return d;
        }
    }
}
