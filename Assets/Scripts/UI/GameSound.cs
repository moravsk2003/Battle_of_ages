using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The world's sound engine: hybrid CC0 audio assets (Resources/Audio/Sfx) combined with procedural physical-modelling
    /// DSP synthesis. Heard from the camera with distance attenuation, stereo panning, and air-absorption low-pass filtering.
    /// Tracks battle intensity for crowd clatter and dynamic combat music, and provides audio ducking for major events.
    /// </summary>
    public static class GameSound
    {
        public enum Kind
        {
            SelectPerson, SelectWarrior, SelectKnight, SelectHero, OrderMove, OrderAttack, OrderWork,
            Sword, Thud, Swing, BowShot, ArrowHit, Magic, Chop, Branches, Pick, Hammer, Place, Fall, Collapse, Fanfare,
            FireFan, MeteorDrop, MeteorImpact, Lightning, Heal, Dash, CritHit, PiercingShot, Artifact,
            LevelUp, Promote, HeroDeath,
            StepGrass, StepSplash, Jump,
            AlarmBell, StandDown, Coins, Gate, ZonePaint,
            AnimalHurt, RaiderHorn,
            OrePickup, TreeFall, Land, HeroHurt, ShieldBlock,
            BuildingFinished, UpgradeBuilding, CraftComplete, EquipGear,
            MagicHit, ZoneErase, AnimalSmallHurt
        }

        public const int Rate = 44100, Variants = 3;
        /// <summary>Beyond this (metres from the camera's view point, zoom counted in) a sound is not heard.</summary>
        public const float HearRange = 90f;
        private const int Voices = 24, PerFrame = 8;

        private static AudioSource[] pool;
        private static AudioLowPassFilter[] poolFilter;
        private static Kind[] poolKind;
        private static readonly Dictionary<int, AudioClip> clips = new Dictionary<int, AudioClip>();
        private static float[] lastPlayed = new float[64];
        private static readonly System.Random rng = new System.Random(); // not UnityEngine.Random: sounds do not shift the game's rolls
        private static int frame, startedThisFrame;
        private static float duckUntil;
        private static float battleEnergy, lastEnergyTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pool = null;
            poolFilter = null;
            clips.Clear();
            System.Array.Clear(lastPlayed, 0, lastPlayed.Length);
            duckUntil = 0f;
            battleEnergy = 0f;
            lastEnergyTime = 0f;
        }

        public static bool Enabled => Application.isPlaying && !Application.isBatchMode;

        /// <summary>Temporarily lowers music and ambience volume during major alerts, fanfares or explosions.</summary>
        public static void Duck(float seconds = 1.2f)
        {
            if (Application.isPlaying) duckUntil = Mathf.Max(duckUntil, Time.unscaledTime + seconds);
        }

        /// <summary>Multiplier (0.6..1.0) applied to background ambience and music when a major sound ducks them.</summary>
        public static float DuckShare => Application.isPlaying && Time.unscaledTime < duckUntil ? 0.6f : 1f;

        /// <summary>How intense the fighting near the camera is right now (0..1), decaying smoothly over time.</summary>
        public static float BattleIntensity
        {
            get
            {
                if (!Application.isPlaying) return 0f;
                float dt = Mathf.Max(0f, Time.unscaledTime - lastEnergyTime);
                float current = battleEnergy * Mathf.Exp(-dt * 0.85f);
                return Mathf.Clamp01(current / 10f);
            }
        }

        private static void AddBattleEnergy(float amount)
        {
            float now = Time.unscaledTime;
            float dt = Mathf.Max(0f, now - lastEnergyTime);
            battleEnergy = Mathf.Min(35f, battleEnergy * Mathf.Exp(-dt * 0.85f) + amount);
            lastEnergyTime = now;
        }

        private static bool IsCombatKind(Kind k)
        {
            switch (k)
            {
                case Kind.Sword:
                case Kind.Thud:
                case Kind.BowShot:
                case Kind.ArrowHit:
                case Kind.Magic:
                case Kind.FireFan:
                case Kind.MeteorImpact:
                case Kind.Lightning:
                case Kind.CritHit:
                case Kind.PiercingShot:
                case Kind.HeroHurt:
                case Kind.ShieldBlock:
                case Kind.MagicHit:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsMajorEvent(Kind k)
        {
            switch (k)
            {
                case Kind.Fanfare:
                case Kind.MeteorImpact:
                case Kind.Lightning:
                case Kind.LevelUp:
                case Kind.HeroDeath:
                case Kind.AlarmBell:
                case Kind.RaiderHorn:
                case Kind.BuildingFinished:
                case Kind.UpgradeBuilding:
                    return true;
                default:
                    return false;
            }
        }

        private static float Volume(Kind k)
        {
            switch (k)
            {
                case Kind.SelectPerson: case Kind.SelectWarrior: case Kind.SelectKnight: case Kind.SelectHero: return 0.45f;
                case Kind.OrderMove: case Kind.OrderAttack: case Kind.OrderWork: return 0.4f;
                case Kind.Branches: case Kind.Pick: case Kind.ZonePaint: case Kind.ZoneErase: case Kind.Land: return 0.24f;
                case Kind.StepGrass: return 0.20f;
                case Kind.StepSplash: case Kind.Jump: return 0.28f;
                case Kind.BowShot: case Kind.ArrowHit: case Kind.Swing: case Kind.Dash: return 0.30f;
                case Kind.OrePickup: case Kind.EquipGear: case Kind.MagicHit: case Kind.ShieldBlock: return 0.36f;
                case Kind.HeroHurt: case Kind.TreeFall: case Kind.CraftComplete: case Kind.AnimalSmallHurt: return 0.45f;
                case Kind.Place: case Kind.Fanfare: case Kind.LevelUp: case Kind.Promote: case Kind.Artifact:
                case Kind.BuildingFinished: case Kind.UpgradeBuilding: return 0.52f;
                case Kind.Collapse: case Kind.MeteorImpact: case Kind.Lightning: case Kind.HeroDeath: case Kind.AlarmBell: return 0.65f;
                case Kind.FireFan: case Kind.CritHit: case Kind.PiercingShot: case Kind.Heal: case Kind.StandDown: case Kind.RaiderHorn: return 0.48f;
                default: return 0.34f;
            }
        }

        /// <summary>Voices of a kind at once (the rest of a battle's clatter is dropped and handled by the crowd layer).</summary>
        private static int Cap(Kind k) =>
            k == Kind.Sword ? 4 :
            k == Kind.Thud || k == Kind.BowShot || k == Kind.ArrowHit || k == Kind.Chop || k == Kind.MagicHit ? 3 :
            k <= Kind.OrderWork || k == Kind.Fanfare || k == Kind.LevelUp || k == Kind.HeroDeath || k == Kind.AlarmBell || k == Kind.StandDown || k == Kind.RaiderHorn || k == Kind.UpgradeBuilding || k == Kind.BuildingFinished ? 1 : 2;

        /// <summary>A sound not in the world (a reply to the player's click, a fanfare).</summary>
        public static void Play2D(Kind kind, float volume = 1f) => Start(kind, volume, 0f, 22000f);

        /// <summary>A sound at a point of the world, heard from the camera.</summary>
        public static void At(Kind kind, Vector3 position, float volume = 1f)
        {
            if (!Enabled || !Hear(position, out float gain, out float pan, out float distance)) return;
            if (IsCombatKind(kind)) AddBattleEnergy(kind == Kind.MeteorImpact || kind == Kind.Lightning ? 4f : 1f);
            Start(kind, volume * gain, pan, DistanceCutoff(distance));
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
            if (target.TryGetComponent<AI.PeacefulAnimalAI>(out AI.PeacefulAnimalAI animal))
            {
                Kind hurtKind = animal.Type == AI.AnimalType.Boar ? Kind.AnimalHurt : Kind.AnimalSmallHurt;
                At(hurtKind, target.transform.position, 0.85f);
            }
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
        public static bool Hear(Vector3 position, out float gain, out float pan) =>
            Hear(position, out gain, out pan, out _);

        public static bool Hear(Vector3 position, out float gain, out float pan, out float distance)
        {
            gain = 0f; pan = 0f; distance = HearRange;
            Camera cam = Utils.ViewCamera.Current; // the map camera (not tagged MainCamera) or first person
            if (cam == null) return false;
            Transform c = cam.transform;
            Vector3 cp = c.position, fwd = c.forward;
            if (cam.orthographic && fwd.y < -0.1f)
            {
                Vector3 focus = cp + fwd * ((cp.y - position.y) / -fwd.y);
                Vector3 flat = position - focus; flat.y = 0f;
                distance = flat.magnitude + cam.orthographicSize * 0.5f;
            }
            else distance = Vector3.Distance(cp, position);
            gain = Loudness(distance);
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

        /// <summary>Air absorption: high frequencies roll off smoothly beyond 18 metres.</summary>
        public static float DistanceCutoff(float distance)
        {
            float u = Mathf.Clamp01((distance - 18f) / (HearRange - 18f));
            return Mathf.Lerp(22000f, 1900f, u * u);
        }

        private static void Start(Kind kind, float volume, float pan, float cutoffHz)
        {
            float sfxShare = Managers.SettingsManager.SfxShare;
            if (!Enabled || volume * sfxShare <= 0.01f) return;
            if (Time.frameCount != frame) { frame = Time.frameCount; startedThisFrame = 0; }
            int idx = (int)kind;
            if (idx >= lastPlayed.Length) System.Array.Resize(ref lastPlayed, idx + 16);
            if (startedThisFrame >= PerFrame || Time.unscaledTime - lastPlayed[idx] < 0.035f) return;
            if (pool == null || pool[0] == null) MakePool();
            int same = 0, free = -1;
            for (int i = 0; i < pool.Length; i++)
            {
                if (pool[i].isPlaying) { if (poolKind[i] == kind) same++; }
                else if (free < 0) free = i;
            }
            if (free < 0 || same >= Cap(kind)) return;
            if (IsMajorEvent(kind)) Duck(1.3f);

            AudioSource s = pool[free];
            poolKind[free] = kind;
            s.clip = Clip(kind, rng.Next(Variants));
            s.volume = Mathf.Clamp01(Volume(kind) * volume * sfxShare);
            s.panStereo = pan;
            s.pitch = 0.93f + (float)rng.NextDouble() * 0.14f;
            if (poolFilter != null && poolFilter[free] != null)
            {
                poolFilter[free].cutoffFrequency = cutoffHz;
                poolFilter[free].enabled = cutoffHz < 20000f;
            }
            s.Play();
            lastPlayed[idx] = Time.unscaledTime;
            startedThisFrame++;
        }

        private static void MakePool()
        {
            var go = new GameObject("GameSound");
            Object.DontDestroyOnLoad(go);
            pool = new AudioSource[Voices];
            poolFilter = new AudioLowPassFilter[Voices];
            poolKind = new Kind[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var child = new GameObject("Voice_" + i);
                child.transform.SetParent(go.transform, false);
                AudioSource s = child.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                AudioLowPassFilter f = child.AddComponent<AudioLowPassFilter>();
                f.cutoffFrequency = 22000f;
                f.enabled = false;
                pool[i] = s;
                poolFilter[i] = f;
            }
        }

        public static AudioClip Clip(Kind kind, int variant)
        {
            int key = (int)kind * 16 + variant;
            if (clips.TryGetValue(key, out AudioClip c) && c != null) return c;

            c = UnityEngine.Resources.Load<AudioClip>($"Audio/Sfx/{kind}_{variant}");
            if (c == null && variant != 0) c = UnityEngine.Resources.Load<AudioClip>($"Audio/Sfx/{kind}_0");
            if (c == null)
            {
                float[] data = Synth(kind, variant);
                c = AudioClip.Create("Sfx_" + kind + variant, data.Length, 1, Rate, false);
                c.SetData(data, 0);
            }
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
                case Kind.SelectPerson: // cloth rustle and a warm wooden tok
                    d = Buf(0.24f);
                    s.Rustle(d, 0f, 0.19f, 1750f, 0.5f);
                    s.Modes(d, 0.015f, new[] { 380f * (1f + 0.06f * v), 1030f }, new[] { 0.6f, 0.22f }, new[] { 42f, 65f });
                    break;
                case Kind.SelectWarrior: // mail jingling, a leather creak
                    d = Buf(0.32f);
                    s.Rustle(d, 0f, 0.12f, 650f, 0.38f);
                    s.Jingle(d, 0f, 0.17f, 11, 0.52f, v);
                    break;
                case Kind.SelectKnight: // a firm plate clank, then mail
                    d = Buf(0.45f);
                    s.Click(d, 0f, 0.008f, 0.55f);
                    s.Drop(d, 0f, 130f * (1f + 0.06f * v), 65f, 0.45f, 22f);
                    s.Modes(d, 0f, Ratios(280f * (1f + 0.06f * v), 1f, 2.32f, 4.25f, 6.63f), new[] { 0.6f, 0.4f, 0.3f, 0.15f }, new[] { 9f, 14f, 20f, 28f });
                    s.Jingle(d, 0.05f, 0.16f, 9, 0.42f, v);
                    break;
                case Kind.SelectHero: // a soft clank and a rising horn call
                    d = Buf(0.62f);
                    s.Modes(d, 0f, Ratios(240f, 1f, 2.32f, 4.25f), new[] { 0.3f, 0.2f, 0.12f }, new[] { 10f, 16f, 24f });
                    s.Horn(d, 0.03f, 0.14f, 392f * (1f + 0.03f * v), 0.48f);
                    s.Horn(d, 0.16f, 0.44f, 587f * (1f + 0.03f * v), 0.55f, vibrato: true);
                    break;
                case Kind.OrderMove: // two crisp marching steps on earth
                    d = Buf(0.32f);
                    s.Step(d, 0f, 1.0f, v);
                    s.Step(d, 0.14f, 0.85f, v);
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
                    s.Band(d, 0f, 0.014f, 3200f, 1f, 0.75f, 220f);
                    float[] r = Ratios(f0, 1f, 2.76f, 5.40f, 8.93f, 13.3f), a = { 0.52f, 0.45f, 0.32f, 0.2f, 0.12f }, k = { 7f, 9f, 13f, 18f, 25f };
                    s.Modes(d, 0f, r, a, k);
                    s.Modes(d, 0f, Ratios(f0 * 1.004f, 1f, 2.76f), new[] { 0.32f, 0.25f }, new[] { 8f, 10f });
                    break;
                }
                case Kind.Thud: // a blow on cloth and leather
                    d = Buf(0.22f);
                    s.Low(d, 0f, 0.2f, 400f, 0.8f, 35f);
                    s.Drop(d, 0f, 140f * (1f + 0.08f * v), 70f, 0.7f, 25f);
                    s.Band(d, 0f, 0.03f, 1200f, 1.5f, 0.4f, 90f);
                    break;
                case Kind.Swing: // the whoosh of a swing
                    d = Buf(0.28f);
                    s.Sweep(d, 0f, 0.26f, 600f * (1f + 0.1f * v), 1850f, 1.5f, 0.72f, arch: true);
                    break;
                case Kind.BowShot: // Karplus-Strong plucked bowstring + arrow hiss
                {
                    d = Buf(0.38f);
                    s.Pluck(d, 0f, 0.28f, 155f * (1f + 0.07f * v), 0.75f, 0.988f);
                    s.Sweep(d, 0.015f, 0.22f, 2600f, 1450f, 1.2f, 0.25f, arch: true);
                    break;
                }
                case Kind.ArrowHit: // an arrow thunks in with shaft vibration
                    d = Buf(0.22f);
                    s.Click(d, 0f, 0.004f, 0.6f);
                    s.Modes(d, 0f, new[] { 240f * (1f + 0.07f * v), 610f, 960f }, new[] { 0.6f, 0.35f, 0.18f }, new[] { 32f, 45f, 60f });
                    s.Drop(d, 0f, 120f, 75f, 0.45f, 28f);
                    s.Pluck(d, 0.01f, 0.16f, 310f * (1f + 0.06f * v), 0.28f, 0.982f);
                    break;
                case Kind.Magic: // a shimmer rising, with sparkles and airy whoosh
                {
                    d = Buf(0.55f);
                    float b = 900f * (1f + 0.06f * v);
                    float[] ph = new float[5];
                    for (int i = 0; i < d.Length; i++)
                    {
                        float t = i / (float)Rate, u = t / 0.55f, x = 0f;
                        for (int n = 0; n < 5; n++) { ph[n] += 2f * Mathf.PI * b * (1f + 0.5f * n) * (1f + 0.6f * u) / Rate; x += Mathf.Sin(ph[n]) / (n + 1); }
                        d[i] += x * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 18f * t)) * Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t * 4.5f) * 0.55f;
                    }
                    s.Sweep(d, 0f, 0.42f, 1200f * (1f + 0.05f * v), 3600f, 1.8f, 0.3f, arch: true);
                    for (int p = 0; p < 8; p++) s.Modes(d, s.Range(0.02f, 0.35f), new[] { s.Range(4000f, 8000f) }, new[] { 0.18f }, new[] { 38f });
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
                case Kind.FireFan: // roaring burst of flame and crackling embers
                    d = Buf(0.65f);
                    s.Low(d, 0f, 0.6f, 380f * (1f + 0.06f * v), 0.85f, 4.5f);
                    s.Sweep(d, 0f, 0.55f, 450f, 1600f * (1f + 0.05f * v), 0.9f, 0.7f, arch: true);
                    for (int p = 0; p < 12; p++) s.Click(d, s.Range(0.03f, 0.5f), 0.003f, 0.35f);
                    break;
                case Kind.MeteorDrop: // falling whistle and roaring flame from the sky
                    d = Buf(0.85f);
                    s.Sweep(d, 0f, 0.82f, 2200f * (1f + 0.05f * v), 320f, 3.2f, 0.68f, arch: true);
                    s.Low(d, 0.15f, 0.68f, 280f * (1f + 0.05f * v), 0.58f, 2.5f);
                    break;
                case Kind.MeteorImpact: // tectonic explosion, debris shatter and flame roar
                    d = Buf(1.35f);
                    s.Click(d, 0f, 0.01f, 0.95f);
                    s.Drop(d, 0f, 135f * (1f + 0.06f * v), 32f, 0.98f, 2.8f);
                    s.Low(d, 0f, 1.3f, 250f, 0.92f, 2.4f);
                    for (int p = 0; p < 16; p++)
                        s.Band(d, s.Range(0.02f, 0.82f), 0.07f, s.Range(450f, 1900f), 1.8f, 0.48f, 32f);
                    break;
                case Kind.Lightning: // sharp electric arc crack followed by rolling thunder
                    d = Buf(1.15f);
                    s.Click(d, 0f, 0.012f, 0.98f);
                    s.Sweep(d, 0f, 0.16f, 6200f * (1f + 0.05f * v), 1800f, 1.4f, 0.7f);
                    s.Band(d, 0f, 0.16f, 3900f * (1f + 0.06f * v), 0.8f, 0.85f, 20f);
                    s.Drop(d, 0.02f, 185f, 40f, 0.88f, 3.4f);
                    s.Low(d, 0.04f, 1.05f, 320f * (1f + 0.05f * v), 0.82f, 2.9f);
                    break;
                case Kind.Heal: // warm harp chord and crystalline chimes
                    d = Buf(0.75f);
                    s.Pluck(d, 0f, 0.68f, 523.25f * (1f + 0.03f * v), 0.45f, 0.994f);
                    s.Pluck(d, 0.05f, 0.65f, 659.25f * (1f + 0.03f * v), 0.42f, 0.994f);
                    s.Modes(d, 0f, new[] { 523.25f * (1f + 0.03f * v), 659.25f, 783.99f, 1046.5f }, new[] { 0.38f, 0.32f, 0.28f, 0.20f }, new[] { 4.2f, 4.8f, 5.2f, 6.2f });
                    s.Modes(d, 0.12f, new[] { 1567.98f * (1f + 0.04f * v), 2093f }, new[] { 0.22f, 0.16f }, new[] { 10f, 13f });
                    break;
                case Kind.Dash: // swift air rush and low impulse
                    d = Buf(0.28f);
                    s.Drop(d, 0f, 160f * (1f + 0.06f * v), 65f, 0.55f, 22f);
                    s.Sweep(d, 0f, 0.25f, 480f, 2400f * (1f + 0.07f * v), 1.4f, 0.75f, arch: true);
                    break;
                case Kind.CritHit: // heavy metallic cleave and deep impact
                {
                    d = Buf(0.45f);
                    float f0 = 540f * (1f + 0.06f * v);
                    s.Click(d, 0f, 0.006f, 0.95f);
                    s.Drop(d, 0f, 175f, 48f, 0.9f, 12f);
                    s.Band(d, 0f, 0.025f, 3400f, 1f, 0.65f, 140f);
                    s.Modes(d, 0f, Ratios(f0, 1f, 2.41f, 4.78f, 7.9f), new[] { 0.62f, 0.45f, 0.35f, 0.2f }, new[] { 6.5f, 8.5f, 12f, 16f });
                    s.Modes(d, 0f, Ratios(f0 * 1.005f, 1f, 2.41f), new[] { 0.32f, 0.22f }, new[] { 8f, 10f });
                    break;
                }
                case Kind.PiercingShot: // high-tension bowstring and supersonic whistle
                    d = Buf(0.45f);
                    s.Pluck(d, 0f, 0.35f, 220f * (1f + 0.06f * v), 0.8f, 0.991f);
                    s.Sweep(d, 0.01f, 0.40f, 4100f * (1f + 0.05f * v), 1350f, 2.6f, 0.58f, arch: true);
                    break;
                case Kind.Artifact: // mystical resonant bell and plucked harp triad
                    d = Buf(0.78f);
                    s.Pluck(d, 0f, 0.68f, 440f * (1f + 0.04f * v), 0.55f, 0.994f);
                    s.Pluck(d, 0.07f, 0.64f, 659.25f * (1f + 0.04f * v), 0.5f, 0.994f);
                    s.Modes(d, 0.14f, new[] { 880f * (1f + 0.04f * v), 1760f, 2640f }, new[] { 0.42f, 0.28f, 0.16f }, new[] { 3.6f, 5.5f, 8.5f });
                    break;
                case Kind.LevelUp: // noble medieval brass & harp fanfare
                    d = Buf(1.05f);
                    s.Horn(d, 0f, 0.18f, 440f * (1f + 0.02f * v), 0.50f);
                    s.Horn(d, 0.17f, 0.18f, 554.37f * (1f + 0.02f * v), 0.54f);
                    s.Horn(d, 0.34f, 0.65f, 659.25f * (1f + 0.02f * v), 0.65f, vibrato: true);
                    s.Pluck(d, 0.34f, 0.65f, 1318.5f * (1f + 0.02f * v), 0.38f, 0.994f);
                    s.Modes(d, 0.34f, new[] { 659.25f * (1f + 0.02f * v), 1318.5f }, new[] { 0.22f, 0.15f }, new[] { 3.5f, 5f });
                    break;
                case Kind.Promote: // armour clank and proud double horn call
                    d = Buf(0.62f);
                    s.Jingle(d, 0f, 0.18f, 8, 0.45f, v);
                    s.Horn(d, 0.05f, 0.15f, 392f * (1f + 0.03f * v), 0.48f);
                    s.Horn(d, 0.19f, 0.40f, 493.88f * (1f + 0.03f * v), 0.58f, vibrato: true);
                    break;
                case Kind.HeroDeath: // deep solemn castle tolling bell and mournful low horn
                    d = Buf(1.45f);
                    s.Modes(d, 0f, new[] { 174.61f * (1f + 0.03f * v), 349.23f, 415.30f, 698.46f }, new[] { 0.68f, 0.42f, 0.32f, 0.18f }, new[] { 2.0f, 2.8f, 3.6f, 5.2f });
                    s.Horn(d, 0.08f, 1.25f, 174.61f * (1f + 0.02f * v), 0.48f);
                    break;
                case Kind.StepGrass: // soft earth step and grass brush
                    d = Buf(0.18f);
                    s.Step(d, 0f, 0.85f, v);
                    s.Rustle(d, 0.01f, 0.12f, 2400f * (1f + 0.06f * v), 0.35f);
                    break;
                case Kind.StepSplash: // crisp shallow water splash and droplets
                    d = Buf(0.28f);
                    s.Band(d, 0f, 0.22f, 1150f * (1f + 0.07f * v), 0.85f, 0.7f, 16f);
                    s.Drop(d, 0f, 180f, 90f, 0.35f, 24f);
                    for (int p = 0; p < 6; p++)
                        s.Modes(d, s.Range(0.02f, 0.20f), new[] { s.Range(950f, 2500f) }, new[] { 0.25f }, new[] { 48f });
                    break;
                case Kind.Jump: // quick cloth rustle and springy push-off
                    d = Buf(0.22f);
                    s.Drop(d, 0f, 85f, 135f * (1f + 0.06f * v), 0.65f, 18f);
                    s.Rustle(d, 0f, 0.18f, 1600f, 0.45f);
                    break;
                case Kind.AlarmBell: // urgent town-hall tocsin bell struck thrice
                    d = Buf(1.3f);
                    for (int hit = 0; hit < 3; hit++)
                        s.Modes(d, hit * 0.36f, new[] { 520f * (1f + 0.03f * v), 1040f, 1380f, 2120f }, new[] { 0.6f, 0.38f, 0.28f, 0.16f }, new[] { 3.5f, 5f, 7f, 11f });
                    break;
                case Kind.StandDown: // warm descending horn call (all-clear)
                    d = Buf(0.75f);
                    s.Horn(d, 0f, 0.26f, 523.25f * (1f + 0.03f * v), 0.48f);
                    s.Horn(d, 0.24f, 0.46f, 392f * (1f + 0.03f * v), 0.52f);
                    break;
                case Kind.Coins: // clinking gold coins
                    d = Buf(0.38f);
                    for (int c = 0; c < 5; c++)
                        s.Modes(d, c * 0.055f + s.Range(0f, 0.015f), new[] { (2800f + c * 320f) * (1f + 0.04f * v), 5800f + c * 410f }, new[] { 0.45f, 0.28f }, new[] { 28f, 42f });
                    break;
                case Kind.Gate: // heavy castle timber gate and iron bolt
                    d = Buf(0.55f);
                    s.Sweep(d, 0f, 0.42f, 230f * (1f + 0.06f * v), 150f, 4f, 0.58f, arch: true);
                    s.Drop(d, 0.36f, 115f, 55f, 0.78f, 16f);
                    s.Modes(d, 0.36f, new[] { 420f * (1f + 0.04f * v), 980f }, new[] { 0.45f, 0.28f }, new[] { 22f, 34f });
                    break;
                case Kind.ZonePaint: // soft parchment brush and wooden peg tap
                    d = Buf(0.18f);
                    s.Rustle(d, 0f, 0.14f, 2100f * (1f + 0.06f * v), 0.45f);
                    s.Modes(d, 0.04f, new[] { 620f * (1f + 0.05f * v), 1480f }, new[] { 0.45f, 0.2f }, new[] { 55f, 75f });
                    break;
                case Kind.AnimalHurt: // low guttural boar/beast grunt
                    d = Buf(0.32f);
                    s.Horn(d, 0f, 0.26f, 115f * (1f + 0.08f * v), 0.55f);
                    s.Band(d, 0f, 0.26f, 340f * (1f + 0.06f * v), 1.8f, 0.6f, 12f);
                    break;
                case Kind.RaiderHorn: // low menacing war-horn blast with brass fifth resonance
                    d = Buf(1.15f);
                    s.Horn(d, 0f, 1.08f, 146.83f * (1f + 0.04f * v), 0.72f, vibrato: true);
                    s.Horn(d, 0.04f, 1.00f, 220f * (1f + 0.04f * v), 0.32f);
                    s.Low(d, 0f, 0.6f, 220f, 0.32f, 3.5f);
                    break;
                case Kind.OrePickup: // stony crystal clink dropped into pouch
                    d = Buf(0.26f);
                    s.Click(d, 0f, 0.003f, 0.65f);
                    s.Modes(d, 0f, new[] { 1450f * (1f + 0.07f * v), 2900f * (1f + 0.07f * v), 4350f }, new[] { 0.48f, 0.34f, 0.22f }, new[] { 24f, 36f, 52f });
                    s.Step(d, 0.05f, 0.55f, v);
                    s.Modes(d, 0.09f, new[] { 1720f * (1f + 0.06f * v), 3440f }, new[] { 0.35f, 0.22f }, new[] { 30f, 44f });
                    break;
                case Kind.TreeFall: // wood splits, branches swoosh, heavy trunk hits earth
                    d = Buf(1.25f);
                    for (int k = 0; k < 5; k++)
                        s.Band(d, k * 0.09f + s.Range(0f, 0.03f), 0.04f, s.Range(900f, 2100f) * (1f + 0.06f * v), 2.2f, 0.58f, 65f);
                    s.Sweep(d, 0.16f, 0.68f, 1900f * (1f + 0.05f * v), 520f, 1.3f, 0.65f, arch: true);
                    s.Drop(d, 0.62f, 95f * (1f + 0.06f * v), 36f, 0.96f, 4.5f);
                    s.Low(d, 0.62f, 0.58f, 260f, 0.80f, 4.0f);
                    s.Rustle(d, 0.64f, 0.42f, 1350f, 0.40f);
                    break;
                case Kind.Land: // weighted landing thud with cloth rustle
                    d = Buf(0.20f);
                    s.Drop(d, 0f, 115f * (1f + 0.06f * v), 52f, 0.78f, 22f);
                    s.Step(d, 0f, 0.9f, v);
                    s.Rustle(d, 0f, 0.14f, 1400f, 0.35f);
                    break;
                case Kind.HeroHurt: // blunt impact on hero armour/flesh and sharp exhale
                    d = Buf(0.30f);
                    s.Drop(d, 0f, 155f * (1f + 0.07f * v), 64f, 0.85f, 18f);
                    s.Band(d, 0f, 0.18f, 460f * (1f + 0.06f * v), 1.6f, 0.65f, 14f);
                    s.Modes(d, 0.01f, new[] { 190f * (1f + 0.05f * v), 380f }, new[] { 0.42f, 0.2f }, new[] { 12f, 18f });
                    break;
                case Kind.ShieldBlock: // crisp shield parry (iron boss + hardwood absorb)
                    d = Buf(0.36f);
                    s.Click(d, 0f, 0.005f, 0.85f);
                    s.Drop(d, 0f, 185f * (1f + 0.06f * v), 82f, 0.75f, 20f);
                    s.Modes(d, 0f, new[] { 440f * (1f + 0.06f * v), 1020f, 1680f, 2490f }, new[] { 0.55f, 0.42f, 0.30f, 0.18f }, new[] { 12f, 16f, 22f, 30f });
                    break;
                case Kind.BuildingFinished: // final hammer taps + warm wooden/brass completion chord
                    d = Buf(0.85f);
                    for (int tap = 0; tap < 2; tap++)
                        s.Modes(d, tap * 0.12f, new[] { 820f * (1f + 0.04f * v), 1950f }, new[] { 0.45f, 0.25f }, new[] { 35f, 50f });
                    s.Horn(d, 0.20f, 0.22f, 392f * (1f + 0.03f * v), 0.48f);
                    s.Horn(d, 0.36f, 0.44f, 587.33f * (1f + 0.03f * v), 0.56f, vibrato: true);
                    s.Pluck(d, 0.36f, 0.44f, 1174.66f, 0.32f, 0.993f);
                    break;
                case Kind.UpgradeBuilding: // masonry set + triumphant brass & harp upgrade fanfare
                    d = Buf(1.05f);
                    s.Modes(d, 0f, new[] { 480f * (1f + 0.05f * v), 1240f }, new[] { 0.48f, 0.26f }, new[] { 25f, 38f });
                    s.Horn(d, 0.08f, 0.18f, 440f * (1f + 0.03f * v), 0.48f);
                    s.Horn(d, 0.25f, 0.18f, 554.37f * (1f + 0.03f * v), 0.52f);
                    s.Horn(d, 0.42f, 0.58f, 659.25f * (1f + 0.03f * v), 0.62f, vibrato: true);
                    s.Pluck(d, 0.42f, 0.58f, 880f, 0.38f, 0.994f);
                    break;
                case Kind.CraftComplete: // anvil bell strike + quenching steam hiss
                    d = Buf(0.65f);
                    s.Click(d, 0f, 0.004f, 0.7f);
                    s.Modes(d, 0f, new[] { 1150f * (1f + 0.05f * v), 2480f, 3920f }, new[] { 0.58f, 0.40f, 0.24f }, new[] { 8f, 12f, 18f });
                    s.Band(d, 0.14f, 0.46f, 3200f * (1f + 0.06f * v), 0.9f, 0.55f, 6f);
                    break;
                case Kind.EquipGear: // leather strap pull, buckle snap and mail clink
                    d = Buf(0.32f);
                    s.Rustle(d, 0f, 0.18f, 950f * (1f + 0.06f * v), 0.48f);
                    s.Jingle(d, 0.04f, 0.18f, 7, 0.46f, v);
                    s.Modes(d, 0.08f, new[] { 360f * (1f + 0.06f * v), 860f }, new[] { 0.45f, 0.30f }, new[] { 24f, 36f });
                    break;
                case Kind.MagicHit: // crystalline arcane impact and spark discharge
                    d = Buf(0.34f);
                    s.Drop(d, 0f, 220f * (1f + 0.07f * v), 72f, 0.65f, 18f);
                    s.Sweep(d, 0f, 0.28f, 2500f * (1f + 0.06f * v), 620f, 1.8f, 0.58f, arch: true);
                    s.Modes(d, 0f, new[] { 1320f * (1f + 0.06f * v), 2640f, 3960f }, new[] { 0.42f, 0.28f, 0.18f }, new[] { 16f, 24f, 32f });
                    break;
                case Kind.ZoneErase: // descending parchment brush swipe
                    d = Buf(0.18f);
                    s.Sweep(d, 0f, 0.16f, 1850f * (1f + 0.07f * v), 680f, 1.3f, 0.55f, arch: true);
                    s.Modes(d, 0.02f, new[] { 410f * (1f + 0.06f * v), 920f }, new[] { 0.45f, 0.20f }, new[] { 55f, 75f });
                    break;
                case Kind.AnimalSmallHurt: // high-pitched wildlife squeak / startled cry (deer/rabbit)
                    d = Buf(0.26f);
                    s.Sweep(d, 0f, 0.22f, 1550f * (1f + 0.08f * v), 820f, 2.2f, 0.62f, arch: true);
                    s.Modes(d, 0.01f, new[] { 960f * (1f + 0.08f * v), 1920f * (1f + 0.08f * v) }, new[] { 0.48f, 0.28f }, new[] { 18f, 26f });
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

        /// <summary>Subtle early-reflection ambience, 1 ms fade-in and 5 ms fade-out (no clicks), normalized to peak 0.9.</summary>
        public static void Finish(float[] d)
        {
            int tap1 = (int)(0.017f * Rate), tap2 = (int)(0.031f * Rate);
            for (int i = d.Length - 1; i >= tap1; i--)
            {
                float echo = d[i - tap1] * 0.14f + (i >= tap2 ? d[i - tap2] * 0.07f : 0f);
                d[i] += echo;
            }
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
        public sealed class Gen
        {
            private readonly System.Random r;
            public Gen(int seed) { r = new System.Random(seed); }
            public float Noise() => (float)r.NextDouble() * 2f - 1f;
            public float Range(float a, float b) => a + (float)r.NextDouble() * (b - a);

            private static int From(float t) => Mathf.Max(0, (int)(t * Rate));

            /// <summary>Karplus-Strong plucked string physical model (bowstring, lute, harp).</summary>
            public void Pluck(float[] d, float start, float length, float f, float amp, float damping = 0.992f)
            {
                int period = Mathf.Max(4, Mathf.RoundToInt(Rate / Mathf.Max(40f, f)));
                float[] ring = new float[period];
                for (int i = 0; i < period; i++) ring[i] = Noise();
                int s0 = From(start), n = Mathf.Min(d.Length, s0 + (int)(length * Rate));
                int p = 0;
                float prev = ring[period - 1];
                for (int i = s0; i < n; i++)
                {
                    float cur = ring[p];
                    float next = (cur + prev) * 0.5f * damping;
                    prev = cur;
                    ring[p] = next;
                    if (++p >= period) p = 0;
                    d[i] += cur * amp;
                }
            }

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
