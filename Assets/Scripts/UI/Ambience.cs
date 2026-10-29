using UnityEngine;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The background of the world, made in code: a looping wind with slow gusts and birds calling now and then. High
    /// over the map the wind is stronger and the birds fainter; in first person the birds are close. Not in batch runs.
    /// </summary>
    public sealed class Ambience : MonoBehaviour
    {
        private const float WindLoopSeconds = 10f;
        private const int BirdVariants = 6;

        private AudioSource wind, birds;
        private AudioClip[] birdClips;
        private float nextBird;
        private readonly System.Random rng = new System.Random();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || FindFirstObjectByType<Ambience>() != null) return;
            var go = new GameObject("Ambience");
            DontDestroyOnLoad(go);
            go.AddComponent<Ambience>();
        }

        private void Start()
        {
            AudioClip loop = AudioClip.Create("Wind", (int)(WindLoopSeconds * GameSound.Rate), 1, GameSound.Rate, false);
            loop.SetData(Wind(WindLoopSeconds, 11), 0);
            wind = gameObject.AddComponent<AudioSource>();
            wind.clip = loop;
            wind.loop = true;
            wind.volume = 0f;
            wind.Play();
            birds = gameObject.AddComponent<AudioSource>();
            birds.playOnAwake = false;
            birdClips = new AudioClip[BirdVariants];
            for (int i = 0; i < BirdVariants; i++)
            {
                float[] d = Bird(40 + i);
                birdClips[i] = AudioClip.Create("Bird" + i, d.Length, 1, GameSound.Rate, false);
                birdClips[i].SetData(d, 0);
            }
            nextBird = Time.unscaledTime + 3f;
        }

        private void Update()
        {
            Camera cam = Camera.main;
            // How far out: the map's zoom (orthographic, 10..100), or first person near the ground
            float height = cam == null ? 0.5f : cam.orthographic ? Mathf.Clamp01(cam.orthographicSize / 100f) : 0f;
            bool paused = Time.timeScale <= 0f;
            float windTarget = (0.07f + 0.1f * height) * (paused ? 0.5f : 1f);
            wind.volume = Mathf.MoveTowards(wind.volume, windTarget, Time.unscaledDeltaTime * 0.1f);

            if (Time.unscaledTime < nextBird || paused) return;
            nextBird = Time.unscaledTime + 4f + (float)rng.NextDouble() * 9f;
            birds.panStereo = (float)rng.NextDouble() * 1.4f - 0.7f;
            birds.pitch = 0.9f + (float)rng.NextDouble() * 0.25f;
            birds.PlayOneShot(birdClips[rng.Next(BirdVariants)], 0.14f * (1f - 0.8f * height));
        }

        /// <summary>
        /// Wind: noise through a band-pass whose pitch and loudness follow slow gusts. The gusts are sines of whole cycles
        /// per loop and the tail is crossfaded into the start, so the loop has no seam.
        /// </summary>
        public static float[] Wind(float seconds, int seed)
        {
            var r = new System.Random(seed);
            int rate = GameSound.Rate, n = (int)(seconds * rate), fade = rate;
            var d = new float[n + fade];
            var bq = new GameSound.Biquad();
            GameSound.Biquad low = GameSound.Biquad.LowPass(220f), dark = GameSound.Biquad.LowPass(900f); // wind is a roar, not a hiss
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate, w = 2f * Mathf.PI / seconds;
                float gust = 0.55f + 0.25f * Mathf.Sin(w * t) + 0.15f * Mathf.Sin(3f * w * t + 1f) + 0.05f * Mathf.Sin(7f * w * t + 2f);
                if (i % 64 == 0) bq.SetBand(300f + 450f * gust, 1.1f);
                float white = (float)r.NextDouble() * 2f - 1f;
                d[i] = dark.Run(bq.Run(white) * 1.4f + low.Run(white) * 0.6f) * gust;
            }
            var loop = new float[n];
            for (int i = 0; i < n; i++) loop[i] = d[i];
            for (int i = 0; i < fade; i++) { float u = i / (float)fade; loop[i] = d[i] * u + d[n + i] * (1f - u); }
            float peak = 0f;
            foreach (float x in loop) peak = Mathf.Max(peak, Mathf.Abs(x));
            if (peak > 1e-6f) for (int i = 0; i < n; i++) loop[i] *= 0.8f / peak;
            return loop;
        }

        /// <summary>A bird's call: two to five quick chirps, each a warbling glide up or down.</summary>
        public static float[] Bird(int seed)
        {
            var r = new System.Random(seed);
            float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
            int rate = GameSound.Rate, chirps = 2 + r.Next(4);
            float f1 = R(2800f, 4500f), glide = R(-0.35f, 0.35f), len = R(0.04f, 0.09f), gap = R(0.03f, 0.07f), warble = R(20f, 45f);
            var d = new float[(int)((chirps * (len + gap) + 0.05f) * rate)];
            for (int c = 0; c < chirps; c++)
            {
                int s0 = (int)(c * (len + gap) * rate), m = (int)(len * rate);
                float ph = 0f, fc = f1 * (1f + R(-0.08f, 0.08f));
                for (int i = 0; i < m && s0 + i < d.Length; i++)
                {
                    float u = i / (float)m, t = i / (float)rate;
                    ph += 2f * Mathf.PI * fc * (1f + glide * u + 0.03f * Mathf.Sin(2f * Mathf.PI * warble * t)) / rate;
                    d[s0 + i] += Mathf.Sin(ph) * Mathf.Sin(Mathf.PI * u);
                }
            }
            GameSound.Finish(d);
            return d;
        }
    }
}
