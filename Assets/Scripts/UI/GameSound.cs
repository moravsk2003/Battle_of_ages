using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The world's sounds made in code like UiSound (no audio assets): a unit's reply when selected (by rank) and to an
    /// order, swords, fists, bows, spells, axes, picks, hammers, a fall, a building's collapse, a fanfare. A world sound
    /// is heard from the camera: loud by the point it looks at, quieter with distance and height, panned left or right.
    /// A pool of 2D sources plays them; each kind has a cap of voices at once, so a mass battle drops what does not fit.
    /// </summary>
    public static class GameSound
    {
        public enum Kind
        {
            SelectPerson, SelectWarrior, SelectKnight, SelectHero, OrderMove, OrderAttack, OrderWork,
            Sword, Thud, Swing, BowShot, ArrowHit, Magic, Chop, Branches, Pick, Hammer, Place, Fall, Collapse, Fanfare
        }

        public const int Rate = 44100, Variants = 3;
        /// <summary>Beyond this (metres from the camera's view point, zoom counted in) a sound is not heard.</summary>
        public const float HearRange = 90f;
        private const int Voices = 20, PerFrame = 6;

        private static AudioSource[] pool;
        private static Kind[] poolKind;
        private static readonly Dictionary<int, AudioClip> clips = new Dictionary<int, AudioClip>();
        private static readonly float[] lastPlayed = new float[System.Enum.GetValues(typeof(Kind)).Length];
        private static readonly System.Random rng = new System.Random(); // not UnityEngine.Random: sounds do not shift the game's rolls
        private static int frame, startedThisFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { pool = null; clips.Clear(); System.Array.Clear(lastPlayed, 0, lastPlayed.Length); }

        public static bool Enabled => Application.isPlaying && !Application.isBatchMode;

        private static float Volume(Kind k)
        {
            switch (k)
            {
                case Kind.SelectPerson: case Kind.SelectWarrior: case Kind.SelectKnight: case Kind.SelectHero: return 0.45f;
                case Kind.OrderMove: case Kind.OrderAttack: case Kind.OrderWork: return 0.4f;
                case Kind.Branches: case Kind.Pick: return 0.22f;
                case Kind.BowShot: case Kind.ArrowHit: case Kind.Swing: return 0.28f;
                case Kind.Place: case Kind.Fanfare: return 0.5f;
                case Kind.Collapse: return 0.6f;
                default: return 0.32f;
            }
        }

        /// <summary>Voices of a kind at once (the rest of a battle's clatter is dropped).</summary>
        private static int Cap(Kind k) => k == Kind.Sword ? 4 : k == Kind.Thud || k == Kind.BowShot || k == Kind.ArrowHit || k == Kind.Chop ? 3 : k <= Kind.OrderWork || k >= Kind.Place && k != Kind.Fall ? 1 : 2;

        /// <summary>A sound not in the world (a reply to the player's click, a fanfare).</summary>
        public static void Play2D(Kind kind, float volume = 1f) => Start(kind, volume, 0f);

        /// <summary>A sound at a point of the world, heard from the camera.</summary>
        public static void At(Kind kind, Vector3 position, float volume = 1f)
        {
            if (!Enabled || !Hear(position, out float gain, out float pan)) return;
            Start(kind, volume * gain, pan);
        }

        /// <summary>The unit answers being selected: cloth for a person, mail for a warrior, plate for a knight, a horn for a hero.</summary>
        public static void Select(RtsUnit unit)
        {
            if (unit == null || !Enabled) return;
            UnitProgress p = unit.GetComponent<UnitProgress>();
            Kind k = p == null ? Kind.SelectPerson
                : p.IsHero ? Kind.SelectHero
                : p.Rank >= UnitRank.Knight ? Kind.SelectKnight
                : p.Rank == UnitRank.Warrior ? Kind.SelectWarrior : Kind.SelectPerson;
            Play2D(k);
        }

        /// <summary>A melee hit: steel on an armoured target, a thud on an unarmoured one.</summary>
        public static void Hit(HealthComponent target)
        {
            if (target == null || !Enabled) return;
            At(target.DamageReduction > 0.05f ? Kind.Sword : Kind.Thud, target.transform.position);
        }

        /// <summary>Something died: a body falls, a building or a wall collapses.</summary>
        public static void Died(HealthComponent who)
        {
            if (who == null || !Enabled) return;
            bool building = who.GetComponent<Building.BuildingNode>() != null || who.GetComponent<Building.WallSegment>() != null;
            At(building ? Kind.Collapse : Kind.Fall, who.transform.position);
        }

        /// <summary>
        /// How loud and where a world point sounds. On the map (an orthographic camera) it hears around the ground point
        /// in the middle of the view, zoomed out as from farther (half the view's half-height is added); in first person
        /// from where it stands.
        /// </summary>
        public static bool Hear(Vector3 position, out float gain, out float pan)
        {
            gain = 0f; pan = 0f;
            Camera cam = Utils.ViewCamera.Current; // the map camera (not tagged MainCamera) or first person
            if (cam == null) return false;
            Transform c = cam.transform;
            Vector3 cp = c.position, fwd = c.forward;
            float d;
            if (cam.orthographic && fwd.y < -0.1f)
            {
                Vector3 focus = cp + fwd * ((cp.y - position.y) / -fwd.y);
                Vector3 flat = position - focus; flat.y = 0f;
                d = flat.magnitude + cam.orthographicSize * 0.5f;
            }
            else d = Vector3.Distance(cp, position);
            gain = Loudness(d);
            if (gain < 0.02f) return false;
            Vector3 dir = position - cp; dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) pan = Vector3.Dot(c.right, dir.normalized) * 0.6f;
            return true;
        }

        /// <summary>Loudness over the hearing distance: full up close, gone at HearRange.</summary>
        public static float Loudness(float distance)
        {
            float u = Mathf.Clamp01(1f - Mathf.Max(0f, distance - 6f) / (HearRange - 6f));
            return u * u;
        }

        private static void Start(Kind kind, float volume, float pan)
        {
            if (!Enabled || volume <= 0.01f) return;
            if (Time.frameCount != frame) { frame = Time.frameCount; startedThisFrame = 0; }
            if (startedThisFrame >= PerFrame || Time.unscaledTime - lastPlayed[(int)kind] < 0.04f) return;
            if (pool == null || pool[0] == null) MakePool();
            int same = 0, free = -1;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].isPlaying) { if (poolKind[i] == kind) same++; }
                else if (free < 0) free = i;
            }
            if (free < 0 || same >= Cap(kind)) return;
            AudioSource s = pool[free];
            poolKind[free] = kind;
            s.clip = Clip(kind, rng.Next(Variants));
            s.volume = Mathf.Clamp01(Volume(kind) * volume);
            s.panStereo = pan;
            s.pitch = 0.93f + (float)rng.NextDouble() * 0.14f;
            s.Play();
            lastPlayed[(int)kind] = Time.unscaledTime;
            startedThisFrame++;
        }

        private static void MakePool()
        {
            var go = new GameObject("GameSound");
            Object.DontDestroyOnLoad(go);
            pool = new AudioSource[Voices];
            poolKind = new Kind[Voices];
            for (int i = 0; i < Voices; i++)
            {
                AudioSource s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                pool[i] = s;
            }
        }

        public static AudioClip Clip(Kind kind, int variant)
        {
            int key = (int)kind * 16 + variant;
            if (clips.TryGetValue(key, out AudioClip c) && c != null) return c;
            float[] data = Synth(kind, variant);
            c = AudioClip.Create("Sfx_" + kind + variant, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            clips[key] = c;
            return c;
        }

        // ---------------------------------------------------------------- synthesis

        /// <summary>The samples of a kind's variant (each variant its own seed and a slightly other pitch), peak 0.9.</summary>
        public static float[] Synth(Kind kind, int variant)
        {
            var s = new Gen((int)kind * 101 + variant * 7 + 3);
            float v = variant - 1f; // -1, 0, 1: the variants' pitch spread
            float[] d;
            switch (kind)
            {
                case Kind.SelectPerson: // cloth rustle and a wooden tok
                    d = Buf(0.22f);
                    s.Rustle(d, 0f, 0.18f, 1800f, 0.5f);
                    s.Modes(d, 0.015f, new[] { 380f * (1f + 0.05f * v), 1030f }, new[] { 0.6f, 0.2f }, new[] { 45f, 70f });
                    break;
                case Kind.SelectWarrior: // mail jingling, a leather creak
                    d = Buf(0.32f);
                    s.Rustle(d, 0f, 0.1f, 650f, 0.35f);
                    s.Jingle(d, 0f, 0.16f, 10, 0.5f, v);
                    break;
                case Kind.SelectKnight: // a plate clank, then mail
                    d = Buf(0.45f);
                    s.Click(d, 0f, 0.008f, 0.5f);
                    s.Modes(d, 0f, Ratios(260f * (1f + 0.06f * v), 1f, 2.32f, 4.25f, 6.63f), new[] { 0.6f, 0.4f, 0.3f, 0.15f }, new[] { 9f, 14f, 20f, 28f });
                    s.Jingle(d, 0.04f, 0.14f, 7, 0.35f, v);
                    break;
                case Kind.SelectHero: // a soft clank and a rising horn call
                    d = Buf(0.62f);
                    s.Modes(d, 0f, Ratios(240f, 1f, 2.32f, 4.25f), new[] { 0.3f, 0.2f, 0.12f }, new[] { 10f, 16f, 24f });
                    s.Horn(d, 0.03f, 0.13f, 392f * (1f + 0.03f * v), 0.45f);
                    s.Horn(d, 0.15f, 0.45f, 587f * (1f + 0.03f * v), 0.5f);
                    break;
                case Kind.OrderMove: // two steps on earth
                    d = Buf(0.3f);
                    s.Step(d, 0f, 1f, v);
                    s.Step(d, 0.13f, 0.8f, v);
                    break;
                case Kind.OrderAttack: // a sword drawn: rising scrape, a ring at the end
                    d = Buf(0.5f);
                    s.Sweep(d, 0f, 0.3f, 2500f, 6000f, 6f, 0.6f);
                    s.Modes(d, 0.26f, Ratios(1900f * (1f + 0.04f * v), 1f, 1.48f, 2.03f, 2.71f), new[] { 0.35f, 0.25f, 0.2f, 0.12f }, new[] { 6f, 8f, 10f, 13f });
                    break;
                case Kind.OrderWork: // tok-tok of a tool on a handle
                    d = Buf(0.26f);
                    for (int i = 0; i < 2; i++)
                    {
                        s.Modes(d, i * 0.1f, new[] { 520f * (1f + 0.05f * v), 1400f }, new[] { 0.6f, 0.25f }, new[] { 55f, 80f });
                        s.Band(d, i * 0.1f, 0.03f, 900f, 2f, 0.3f, 90f);
                    }
                    break;
                case Kind.Sword: // steel on steel: a short scrape and inharmonic ringing, a twin a hair off for the beating
                {
                    d = Buf(0.5f);
                    float f0 = 700f + 120f * v + s.Range(-40f, 40f);
                    s.Band(d, 0f, 0.012f, 3000f, 1f, 0.7f, 250f);
                    float[] r = Ratios(f0, 1f, 2.76f, 5.40f, 8.93f, 13.3f), a = { 0.5f, 0.45f, 0.3f, 0.2f, 0.1f }, k = { 7f, 9f, 13f, 18f, 25f };
                    s.Modes(d, 0f, r, a, k);
                    s.Modes(d, 0f, Ratios(f0 * 1.004f, 1f, 2.76f), new[] { 0.3f, 0.25f }, new[] { 8f, 10f });
                    break;
                }
                case Kind.Thud: // a blow on cloth and leather
                    d = Buf(0.22f);
                    s.Low(d, 0f, 0.2f, 400f, 0.8f, 35f);
                    s.Drop(d, 0f, 140f * (1f + 0.08f * v), 70f, 0.7f, 25f);
                    s.Band(d, 0f, 0.03f, 1200f, 1.5f, 0.4f, 90f);
                    break;
                case Kind.Swing: // the whoosh of a swing
                    d = Buf(0.26f);
                    s.Sweep(d, 0f, 0.24f, 600f * (1f + 0.1f * v), 1800f, 1.5f, 0.7f, arch: true);
                    break;
                case Kind.BowShot: // the string's twang and the arrow's hiss
                {
                    d = Buf(0.36f);
                    float f = 165f + 15f * v;
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate, ph = 2f * Mathf.PI * f * (t + 0.08f * (1f - Mathf.Exp(-t * 30f)) / 30f), x = 0f;
                        for (int n = 1; n <= 6; n++) x += Mathf.Sin(ph * n) / n * Mathf.Exp(-t * (10f + 3f * n));
                        d[i] += x * 0.6f * Mathf.Clamp01(t / 0.002f);
                    }
                    s.Sweep(d, 0.02f, 0.2f, 2500f, 1500f, 1.2f, 0.2f, arch: true);
                    break;
                }
                case Kind.ArrowHit: // an arrow thunks in
                    d = Buf(0.18f);
                    s.Click(d, 0f, 0.004f, 0.5f);
                    s.Modes(d, 0f, new[] { 240f * (1f + 0.07f * v), 610f }, new[] { 0.6f, 0.3f }, new[] { 35f, 50f });
                    s.Drop(d, 0f, 110f, 80f, 0.4f, 30f);
                    break;
                case Kind.Magic: // a shimmer rising, with sparkles
                {
                    d = Buf(0.55f);
                    float b = 900f * (1f + 0.05f * v);
                    float[] ph = new float[5];
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate, u = t / 0.55f, x = 0f;
                        for (int n = 0; n < 5; n++) { ph[n] += 2f * Mathf.PI * b * (1f + 0.5f * n) * (1f + 0.6f * u) / Rate; x += Mathf.Sin(ph[n]) / (n + 1); }
                        d[i] += x * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 18f * t)) * Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t * 5f) * 0.5f;
                    }
                    for (int p = 0; p < 8; p++) s.Modes(d, s.Range(0.02f, 0.35f), new[] { s.Range(4000f, 8000f) }, new[] { 0.15f }, new[] { 40f });
                    break;
                }
                case Kind.Chop: // an axe bites wood: a crack and the trunk's hollow knock
                    d = Buf(0.26f);
                    s.Click(d, 0f, 0.003f, 0.8f);
                    s.Band(d, 0f, 0.2f, 320f * (1f + 0.06f * v), 5f, 1f, 28f);
                    s.Modes(d, 0f, new[] { 180f * (1f + 0.06f * v), 410f }, new[] { 0.5f, 0.3f }, new[] { 30f, 40f });
                    s.Band(d, 0f, 0.02f, 2500f, 1.5f, 0.4f, 120f);
                    break;
                case Kind.Branches: // twigs snapping in rustling leaves
                    d = Buf(0.3f);
                    s.Rustle(d, 0f, 0.28f, 2500f, 0.35f);
                    for (int p = 0; p < 4; p++) s.Band(d, s.Range(0.02f, 0.22f), 0.012f, s.Range(1200f, 2200f), 2f, 0.6f, 200f);
                    break;
                case Kind.Pick: // steel on stone: a click, a high ring, grit
                    d = Buf(0.25f);
                    s.Click(d, 0f, 0.002f, 0.7f);
                    s.Modes(d, 0f, new[] { 2100f * (1f + 0.04f * v), 3300f, 4700f }, new[] { 0.4f, 0.3f, 0.2f }, new[] { 30f, 40f, 55f });
                    s.Band(d, 0.005f, 0.15f, 1200f, 1f, 0.25f, 25f);
                    break;
                case Kind.Hammer: // a hammer on wood and a nail
                    d = Buf(0.22f);
                    s.Modes(d, 0f, new[] { 900f * (1f + 0.05f * v), 2300f }, new[] { 0.45f, 0.25f }, new[] { 25f, 40f });
                    s.Band(d, 0f, 0.12f, 450f, 3f, 0.6f, 40f);
                    s.Click(d, 0f, 0.002f, 0.4f);
                    break;
                case Kind.Place: // a heavy thing set down, a rattle of planks
                    d = Buf(0.5f);
                    s.Drop(d, 0f, 70f, 45f, 0.9f, 8f);
                    s.Low(d, 0f, 0.4f, 250f, 0.7f, 12f);
                    s.Band(d, 0.01f, 0.25f, 900f * (1f + 0.05f * v), 2f, 0.3f, 20f);
                    break;
                case Kind.Fall: // a body drops
                    d = Buf(0.45f);
                    s.Low(d, 0f, 0.3f, 300f, 0.8f, 14f);
                    s.Drop(d, 0f, 90f * (1f + 0.06f * v), 55f, 0.8f, 14f);
                    s.Low(d, 0.12f, 0.2f, 350f, 0.4f, 20f);
                    s.Rustle(d, 0.05f, 0.25f, 1500f, 0.2f);
                    break;
                case Kind.Collapse: // stone and timber come down
                    d = Buf(1.4f);
                    s.Low(d, 0f, 1.35f, 300f, 0.8f, 2.5f);
                    s.Drop(d, 0f, 55f, 40f, 0.7f, 3f);
                    for (int p = 0; p < 18; p++)
                    {
                        float at = 1f - Mathf.Sqrt(s.Range(0f, 1f));
                        s.Band(d, at, 0.05f, s.Range(500f, 1500f), 2f, 0.5f * (1f - at), 45f);
                    }
                    break;
                default: // Fanfare: three rising notes on a brass horn
                    d = Buf(1.15f);
                    s.Horn(d, 0f, 0.17f, 392f * (1f + 0.02f * v), 0.5f);
                    s.Horn(d, 0.18f, 0.17f, 523f * (1f + 0.02f * v), 0.5f);
                    s.Horn(d, 0.36f, 0.75f, 659f * (1f + 0.02f * v), 0.6f, vibrato: true);
                    break;
            }
            Finish(d);
            return d;
        }

        private static float[] Buf(float seconds) => new float[Mathf.CeilToInt(Rate * seconds)];

        private static float[] Ratios(float f0, params float[] ratios)
        {
            var f = new float[ratios.Length];
            for (int i = 0; i < f.Length; i++) f[i] = f0 * ratios[i];
            return f;
        }

        /// <summary>A 1 ms fade in and 5 ms out (no clicks), then the peak to 0.9.</summary>
        public static void Finish(float[] d)
        {
            int fin = Mathf.Min(44, d.Length), fout = Mathf.Min(220, d.Length);
            for (int i = 0; i < fin; i++) d[i] *= i / (float)fin;
            for (int i = 0; i < fout; i++) d[d.Length - 1 - i] *= i / (float)fout;
            float peak = 0f;
            foreach (float x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            if (peak > 1e-6f) for (int i = 0; i < d.Length; i++) d[i] *= 0.9f / peak;
        }

        /// <summary>A second-order filter (the RBJ cookbook): band-pass at a peak gain of one, or low-pass.</summary>
        public struct Biquad
        {
            private float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad BandPass(float f, float q) { var b = new Biquad(); b.SetBand(f, q); return b; }
            public static Biquad LowPass(float f, float q = 0.707f) { var b = new Biquad(); b.SetLow(f, q); return b; }

            public void SetBand(float f, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(f, 20f, Rate * 0.45f) / Rate, alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
                b0 = alpha / a0; b1 = 0f; b2 = -alpha / a0; a1 = -2f * Mathf.Cos(w) / a0; a2 = (1f - alpha) / a0;
            }

            public void SetLow(float f, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(f, 20f, Rate * 0.45f) / Rate, alpha = Mathf.Sin(w) / (2f * q), cw = Mathf.Cos(w), a0 = 1f + alpha;
                b0 = (1f - cw) / 2f / a0; b1 = (1f - cw) / a0; b2 = b0; a1 = -2f * cw / a0; a2 = (1f - alpha) / a0;
            }

            public float Run(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        /// <summary>The building blocks the sounds are mixed from, each added into the buffer from a start time.</summary>
        private sealed class Gen
        {
            private readonly System.Random r;
            public Gen(int seed) { r = new System.Random(seed); }
            public float Noise() => (float)r.NextDouble() * 2f - 1f;
            public float Range(float a, float b) => a + (float)r.NextDouble() * (b - a);

            private static int From(float t) => Mathf.Max(0, (int)(t * Rate));

            /// <summary>Struck modes: sines with their own amplitudes and decays (a bell, a blade, a plank).</summary>
            public void Modes(float[] d, float start, float[] f, float[] a, float[] k)
            {
                for (int i = From(start); i < d.Length; i++)
                {
                    float t = (i - From(start)) / (float)Rate, x = 0f;
                    for (int m = 0; m < f.Length; m++) x += Mathf.Sin(2f * Mathf.PI * f[m] * t) * a[m] * Mathf.Exp(-t * k[m]);
                    d[i] += x;
                }
            }

            /// <summary>Band-passed noise with an exponential decay.</summary>
            public void Band(float[] d, float start, float length, float f, float q, float amp, float decay)
            {
                Biquad bq = Biquad.BandPass(f, q);
                int s0 = From(start), n = Mathf.Min(d.Length, s0 + (int)(length * Rate));
                for (int i = s0; i < n; i++) { float t = (i - s0) / (float)Rate; d[i] += bq.Run(Noise()) * amp * 2.5f * Mathf.Exp(-t * decay); }
            }

            /// <summary>Low-passed noise with an exponential decay (thumps, rumble).</summary>
            public void Low(float[] d, float start, float length, float f, float amp, float decay)
            {
                Biquad bq = Biquad.LowPass(f);
                int s0 = From(start), n = Mathf.Min(d.Length, s0 + (int)(length * Rate));
                for (int i = s0; i < n; i++) { float t = (i - s0) / (float)Rate; d[i] += bq.Run(Noise()) * amp * 2f * Mathf.Exp(-t * decay); }
            }

            /// <summary>A broadband tick a few ms long.</summary>
            public void Click(float[] d, float start, float length, float amp)
            {
                int s0 = From(start), n = Mathf.Min(d.Length, s0 + Mathf.Max(1, (int)(length * Rate)));
                for (int i = s0; i < n; i++) d[i] += Noise() * amp * (1f - (i - s0) / (float)(n - s0));
            }

            /// <summary>A sine falling in pitch (a body's weight hitting the ground).</summary>
            public void Drop(float[] d, float start, float f1, float f2, float amp, float decay)
            {
                float ph = 0f;
                for (int i = From(start); i < d.Length; i++)
                {
                    float t = (i - From(start)) / (float)Rate;
                    ph += 2f * Mathf.PI * Mathf.Lerp(f2, f1, Mathf.Exp(-t * 20f)) / Rate;
                    d[i] += Mathf.Sin(ph) * amp * Mathf.Exp(-t * decay);
                }
            }

            /// <summary>Cloth or leaves: band noise swelling and fading.</summary>
            public void Rustle(float[] d, float start, float length, float f, float amp)
            {
                Biquad bq = Biquad.BandPass(f, 0.7f);
                int s0 = From(start), n = Mathf.Min(d.Length, s0 + (int)(length * Rate));
                for (int i = s0; i < n; i++)
                {
                    float u = (i - s0) / (float)(n - s0);
                    float grain = 0.6f + 0.4f * Mathf.Abs(Noise()); // uneven, like cloth
                    d[i] += bq.Run(Noise()) * amp * 2.5f * grain * Mathf.Sin(Mathf.PI * Mathf.Sqrt(u));
                }
            }

            /// <summary>Mail rings: short high pings scattered over a time, on a hiss.</summary>
            public void Jingle(float[] d, float start, float spread, int rings, float amp, float v)
            {
                Band(d, start, spread + 0.08f, 5000f, 1.2f, amp * 0.5f, 14f);
                for (int p = 0; p < rings; p++)
                    Modes(d, start + Range(0f, spread), new[] { Range(3500f, 8000f) * (1f + 0.03f * v) }, new[] { Range(0.3f, 0.6f) * amp }, new[] { Range(60f, 90f) });
            }

            /// <summary>Noise through a band-pass gliding from one pitch to another; arch: swells and fades (a whoosh).</summary>
            public void Sweep(float[] d, float start, float length, float f1, float f2, float q, float amp, bool arch = false)
            {
                var bq = new Biquad();
                int s0 = From(start), n = Mathf.Min(d.Length, s0 + (int)(length * Rate));
                for (int i = s0; i < n; i++)
                {
                    float u = (i - s0) / (float)(n - s0);
                    if ((i - s0) % 32 == 0) bq.SetBand(Mathf.Lerp(f1, f2, arch ? Mathf.Sin(Mathf.PI * u * 0.5f) : u), q);
                    float env = arch ? Mathf.Sin(Mathf.PI * u) : Mathf.Clamp01(u / 0.15f) * (1f - u * 0.6f);
                    d[i] += bq.Run(Noise()) * amp * 3f * env;
                }
            }

            /// <summary>One step on earth: a dull thump, a little grit.</summary>
            public void Step(float[] d, float start, float amp, float v)
            {
                Low(d, start, 0.1f, 500f, 0.6f * amp, 40f);
                Drop(d, start, 95f * (1f + 0.05f * v), 75f, 0.6f * amp, 30f);
                Band(d, start, 0.05f, 3000f, 1f, 0.2f * amp, 60f);
            }

            /// <summary>A brass note: harmonics falling off, brighter in the attack, a soft start and end.</summary>
            public void Horn(float[] d, float start, float length, float f, float amp, bool vibrato = false)
            {
                int s0 = From(start), n = Mathf.Min(d.Length, s0 + (int)(length * Rate));
                float ph = 0f;
                for (int i = s0; i < n; i++)
                {
                    float t = (i - s0) / (float)Rate, left = (n - i) / (float)Rate;
                    float vib = vibrato ? 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 5.5f * t) * Mathf.Clamp01(t / 0.2f) : 1f;
                    ph += 2f * Mathf.PI * f * vib / Rate;
                    float bright = 0.55f + 0.45f * Mathf.Exp(-t * 8f), x = 0f;
                    for (int h = 1; h <= 8; h++) x += Mathf.Sin(ph * h) * Mathf.Pow(bright, h - 1) / h;
                    d[i] += x * amp * Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01(left / 0.06f);
                }
            }
        }
    }
}
