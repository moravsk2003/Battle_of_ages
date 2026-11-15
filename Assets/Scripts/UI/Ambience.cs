using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The background soundscape of the world:
    /// - Wind loop with slow gusts (stronger high over the map, softer near the ground).
    /// - Day vs Night cycle: birds calling by day (06:00..19:30), cricket chorus + owls hooting by night (19:30..06:00).
    /// - Spatial proximity loops (checked every 0.25 s, zero GC): water lapping near lake shores, warm hearth/campfire
    ///   crackle near settlements, and distant clatter & war cries during large battles (GameSound.BattleIntensity).
    /// </summary>
    public sealed class Ambience : MonoBehaviour
    {
        private const float WindLoopSeconds = 10f, CricketLoopSeconds = 6f, WaterLoopSeconds = 8f, CampfireLoopSeconds = 5f, BattleLoopSeconds = 4f;
        private const int BirdVariants = 6, OwlVariants = 3;

        private AudioSource wind, crickets, water, campfire, battle, birds;
        private AudioClip[] birdClips, owlClips;
        private float nextBird, nextProximityCheck;
        private float waterProximity, settlementProximity;
        private readonly System.Random rng = new System.Random();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode || FindFirstObjectByType<Ambience>() != null) return;
            var go = new GameObject("Ambience");
            DontDestroyOnLoad(go);
            go.AddComponent<Ambience>();
        }

        private AudioSource MakeLoop(string name, AudioClip clip)
        {
            AudioSource s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            s.playOnAwake = false;
            s.Play();
            return s;
        }

        private static AudioClip CreateLoopClip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, GameSound.Rate, false);
            c.SetData(data, 0);
            return c;
        }

        private void Start()
        {
            wind = MakeLoop("Wind", CreateLoopClip("Wind", Wind(WindLoopSeconds, 11)));
            crickets = MakeLoop("Crickets", CreateLoopClip("Crickets", Crickets(CricketLoopSeconds, 22)));

            AudioClip waterClip = UnityEngine.Resources.Load<AudioClip>("Audio/Ambience/WaterLoop");
            if (waterClip == null) waterClip = CreateLoopClip("Water", Water(WaterLoopSeconds, 33));
            water = MakeLoop("Water", waterClip);

            campfire = MakeLoop("Campfire", CreateLoopClip("Campfire", Campfire(CampfireLoopSeconds, 44)));
            battle = MakeLoop("BattleClatter", CreateLoopClip("BattleClatter", BattleClatter(BattleLoopSeconds, 55)));

            birds = gameObject.AddComponent<AudioSource>();
            birds.playOnAwake = false;
            birdClips = new AudioClip[BirdVariants];
            for (int i = 0; i < BirdVariants; i++)
                birdClips[i] = CreateLoopClip("Bird" + i, Bird(40 + i));

            owlClips = new AudioClip[OwlVariants];
            for (int i = 0; i < OwlVariants; i++)
                owlClips[i] = CreateLoopClip("Owl" + i, Owl(70 + i));

            nextBird = Time.unscaledTime + 3f;
        }

        public static bool IsNight(float hour) => hour >= 19.5f || hour < 6f;

        private void Update()
        {
            float ambShare = Managers.SettingsManager.AmbienceShare * GameSound.DuckShare;
            Camera cam = Utils.ViewCamera.Current;
            // How far out: the map's zoom (orthographic, 10..100), or first person near the ground
            float height = cam == null ? 0.5f : cam.orthographic ? Mathf.Clamp01(cam.orthographicSize / 100f) : 0f;
            bool paused = Time.timeScale <= 0f;
            float pauseFactor = paused ? 0.45f : 1f;
            bool night = IsNight(WorldClock.HourOfDay);

            if (Time.unscaledTime >= nextProximityCheck)
            {
                nextProximityCheck = Time.unscaledTime + 0.25f;
                SampleProximity(cam);
            }

            float dt = Time.unscaledDeltaTime * 0.25f;
            float groundFactor = 1f - 0.75f * height;

            float windTarget = (0.065f + 0.10f * height) * pauseFactor * ambShare;
            wind.volume = Mathf.MoveTowards(wind.volume, windTarget, dt);

            float cricketTarget = (night ? 0.085f * groundFactor : 0f) * pauseFactor * ambShare;
            crickets.volume = Mathf.MoveTowards(crickets.volume, cricketTarget, dt);

            float waterTarget = waterProximity * 0.14f * groundFactor * pauseFactor * ambShare;
            water.volume = Mathf.MoveTowards(water.volume, waterTarget, dt);

            float fireTarget = settlementProximity * 0.10f * groundFactor * pauseFactor * ambShare;
            campfire.volume = Mathf.MoveTowards(campfire.volume, fireTarget, dt);

            float battleLevel = Mathf.Clamp01((GameSound.BattleIntensity - 0.15f) * 1.25f);
            float battleTarget = battleLevel * 0.15f * pauseFactor * ambShare;
            battle.volume = Mathf.MoveTowards(battle.volume, battleTarget, dt * 1.6f);

            if (Time.unscaledTime < nextBird || paused || ambShare <= 0.001f) return;
            nextBird = Time.unscaledTime + (night ? 7f + (float)rng.NextDouble() * 12f : 4f + (float)rng.NextDouble() * 9f);
            birds.panStereo = (float)rng.NextDouble() * 1.4f - 0.7f;
            birds.pitch = 0.92f + (float)rng.NextDouble() * 0.18f;
            AudioClip call = night ? owlClips[rng.Next(OwlVariants)] : birdClips[rng.Next(BirdVariants)];
            birds.PlayOneShot(call, (night ? 0.13f : 0.14f) * (1f - 0.8f * height) * ambShare);
        }

        private void SampleProximity(Camera cam)
        {
            Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
            GameObject player = Managers.GameContext.Player;
            if (player != null && (cam == null || !cam.orthographic)) pos = player.transform.position;

            // Water proximity (within 24m of lake shore)
            if (World.Lakes.TryLake(pos.x, pos.z, 8f, out _)) waterProximity = 1f;
            else if (World.Lakes.TryLake(pos.x, pos.z, 24f, out _)) waterProximity = 0.45f;
            else waterProximity = 0f;

            // Settlement hearth proximity (within 28m of nearest TownHall)
            Building.TownHall hall = Building.TownHall.Nearest(pos);
            if (hall != null)
            {
                Vector3 d = hall.transform.position - pos;
                d.y = 0f;
                float dist = d.magnitude;
                settlementProximity = Mathf.Clamp01(1f - (dist - 8f) / 24f);
            }
            else settlementProximity = 0f;
        }

        private static float[] CrossfadeLoop(float[] d, int n, int fade, float targetPeak)
        {
            var loop = new float[n];
            for (int i = 0; i < n; i++) loop[i] = d[i];
            for (int i = 0; i < fade; i++) { float u = i / (float)fade; loop[i] = d[i] * u + d[n + i] * (1f - u); }
            float peak = 0f;
            foreach (float x in loop) peak = Mathf.Max(peak, Mathf.Abs(x));
            if (peak > 1e-6f) for (int i = 0; i < n; i++) loop[i] *= targetPeak / peak;
            return loop;
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
            GameSound.Biquad low = GameSound.Biquad.LowPass(220f), dark = GameSound.Biquad.LowPass(900f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate, w = 2f * Mathf.PI / seconds;
                float gust = 0.55f + 0.25f * Mathf.Sin(w * t) + 0.15f * Mathf.Sin(3f * w * t + 1f) + 0.05f * Mathf.Sin(7f * w * t + 2f);
                if (i % 64 == 0) bq.SetBand(300f + 450f * gust, 1.1f);
                float white = (float)r.NextDouble() * 2f - 1f;
                d[i] = dark.Run(bq.Run(white) * 1.4f + low.Run(white) * 0.6f) * gust;
            }
            return CrossfadeLoop(d, n, fade, 0.8f);
        }

        /// <summary>Night cricket chorus: layered high-Q 4.1–4.6 kHz pulse trains with natural rhythmic modulation.</summary>
        public static float[] Crickets(float seconds, int seed)
        {
            var r = new System.Random(seed);
            int rate = GameSound.Rate, n = (int)(seconds * rate), fade = rate / 2;
            var d = new float[n + fade];
            GameSound.Biquad bp1 = GameSound.Biquad.BandPass(4150f, 12f), bp2 = GameSound.Biquad.BandPass(4420f, 14f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate;
                float pulse1 = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 3.2f * t)) * Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 28f * t));
                float pulse2 = Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 2.5f * t + 1.7f)) * Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * 31f * t));
                float carrier1 = Mathf.Sin(2f * Mathf.PI * 4180f * t) * pulse1;
                float carrier2 = Mathf.Sin(2f * Mathf.PI * 4440f * t) * pulse2;
                float nse = ((float)r.NextDouble() * 2f - 1f) * 0.18f;
                d[i] = bp1.Run(carrier1 + nse * pulse1) * 0.6f + bp2.Run(carrier2 + nse * pulse2) * 0.5f;
            }
            return CrossfadeLoop(d, n, fade, 0.75f);
        }

        /// <summary>Gentle lake shore water lapping loop with soft droplet texture.</summary>
        public static float[] Water(float seconds, int seed)
        {
            var r = new System.Random(seed);
            int rate = GameSound.Rate, n = (int)(seconds * rate), fade = rate;
            var d = new float[n + fade];
            var bq = new GameSound.Biquad();
            GameSound.Biquad lp = GameSound.Biquad.LowPass(1250f), shimmer = GameSound.Biquad.BandPass(2200f, 3f);
            float bubble = 0f, ph = 0f, bf = 900f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)rate, w = 2f * Mathf.PI / seconds;
                float wave = 0.52f + 0.30f * Mathf.Sin(2f * w * t) + 0.14f * Mathf.Sin(5f * w * t + 0.8f) + 0.04f * Mathf.Sin(9f * w * t + 2.1f);
                if (i % 64 == 0) bq.SetBand(340f + 560f * wave, 1.9f);
                float white = (float)r.NextDouble() * 2f - 1f;
                if (r.NextDouble() < 0.0005) { bubble = 0.25f + (float)r.NextDouble() * 0.35f; bf = 750f + (float)r.NextDouble() * 900f; }
                bubble *= 0.996f;
                ph += 2f * Mathf.PI * bf / rate;
                d[i] = (lp.Run(bq.Run(white)) * wave + shimmer.Run(white) * 0.12f * wave + Mathf.Sin(ph) * bubble * 0.15f);
            }
            return CrossfadeLoop(d, n, fade, 0.78f);
        }

        /// <summary>Warm hearth / campfire crackle loop near settlements.</summary>
        public static float[] Campfire(float seconds, int seed)
        {
            var r = new System.Random(seed);
            int rate = GameSound.Rate, n = (int)(seconds * rate), fade = rate / 2;
            var d = new float[n + fade];
            GameSound.Biquad rumble = GameSound.Biquad.LowPass(260f), snapBp = GameSound.Biquad.BandPass(2400f, 3.5f);
            float popEnv = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float white = (float)r.NextDouble() * 2f - 1f;
                if (r.NextDouble() < 0.0018) popEnv = 0.5f + (float)r.NextDouble() * 0.5f;
                popEnv *= 0.985f;
                d[i] = rumble.Run(white) * 0.55f + snapBp.Run(white) * popEnv * 1.8f;
            }
            return CrossfadeLoop(d, n, fade, 0.8f);
        }

        /// <summary>Distant mass-battle clatter & low war rumble loop driven by GameSound.BattleIntensity.</summary>
        public static float[] BattleClatter(float seconds, int seed)
        {
            var r = new System.Random(seed);
            int rate = GameSound.Rate, n = (int)(seconds * rate), fade = rate / 2;
            var d = new float[n + fade];
            GameSound.Biquad rumble = GameSound.Biquad.LowPass(320f), steel = GameSound.Biquad.BandPass(2100f, 6f);
            float clangEnv = 0f, clangFreq = 1900f, ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float white = (float)r.NextDouble() * 2f - 1f;
                if (r.NextDouble() < 0.0009)
                {
                    clangEnv = 0.4f + (float)r.NextDouble() * 0.6f;
                    clangFreq = 1600f + (float)r.NextDouble() * 1100f;
                }
                clangEnv *= 0.9965f;
                ph += 2f * Mathf.PI * clangFreq / rate;
                float ring = (Mathf.Sin(ph) + 0.5f * Mathf.Sin(ph * 1.47f)) * clangEnv;
                d[i] = rumble.Run(white) * 0.65f + (steel.Run(white) * clangEnv + ring * 0.4f);
            }
            return CrossfadeLoop(d, n, fade, 0.8f);
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

        /// <summary>An owl's nocturnal hoot ("hoo-hoo"): soft, hollow low-mid formant with gentle pitch droop.</summary>
        public static float[] Owl(int seed)
        {
            var r = new System.Random(seed);
            float R(float a, float b) => a + (float)r.NextDouble() * (b - a);
            int rate = GameSound.Rate;
            float baseF = R(390f, 490f);
            var d = new float[(int)(0.95f * rate)];
            (float start, float len, float fMul)[] hoots = { (0.02f, 0.28f, 1.04f), (0.38f, 0.48f, 0.94f) };
            foreach (var h in hoots)
            {
                int s0 = (int)(h.start * rate), m = (int)(h.len * rate);
                float ph = 0f;
                for (int i = 0; i < m && s0 + i < d.Length; i++)
                {
                    float u = i / (float)m, t = i / (float)rate;
                    float f = baseF * h.fMul * (1f - 0.06f * u * u) * (1f + 0.008f * Mathf.Sin(2f * Mathf.PI * 5.5f * t));
                    ph += 2f * Mathf.PI * f / rate;
                    float env = Mathf.Sin(Mathf.PI * u);
                    d[s0 + i] += (Mathf.Sin(ph) + 0.2f * Mathf.Sin(2f * ph)) * env * env;
                }
            }
            GameSound.Finish(d);
            return d;
        }
    }
}
