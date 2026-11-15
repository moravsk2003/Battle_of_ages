using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.World
{
    /// <summary>
    /// Day and night from the world clock (docs/GAME_DESIGN.md §4, M1 step 7): the sun rises at 6, stands highest at
    /// noon and sets at 18, warmer and dimmer low over the horizon; at night the main light is a pale moon and the
    /// ambient light, fog and sky go dark blue — darker, but one still sees to play. A game hour is a real minute, so a
    /// day is 24 minutes. The scene's own light, ambient and sky are the noon look (the sky material is copied, not
    /// changed).
    /// The moon (the author's word, 2026-11-15: a darker night, moonlight and the moon's phases) goes round in
    /// LunarDays game days, one phase a day: it lags the sun by its phase (full: opposite it, new: beside it), so it is
    /// up for all of a full-moon night, half of a quarter's and none of a new moon's. Its light is MoonShare of the sun's
    /// at full and goes with the lit part of its face; the night's ambient is darker the less moon there is. The sky
    /// draws its disc with the phase's shadow, and the stars.
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        /// <summary>The sun's highest elevation (noon), degrees.</summary>
        public const float NoonElevation = 62f;
        /// <summary>At night: the moon's share of the sun's light, the ambient light's share of the day's.</summary>
        public const float MoonShare = 0.16f, NightAmbient = 0.3f;
        /// <summary>A moon-less night's ambient against a full-moon night's.</summary>
        public const float NewMoonAmbient = 0.7f;
        /// <summary>Game days from new moon to new moon: eight phases, one a day.</summary>
        public const float LunarDays = 8f;
        /// <summary>The phase at the game's start: its first midnight comes at full moon (a new player sees the first night).</summary>
        private const double PhaseAtStart = 0.5 - 1.0 / LunarDays;

        private static readonly Color Moon = new Color(0.62f, 0.72f, 1f), Sunset = new Color(1f, 0.58f, 0.34f);
        private static readonly Color NightFog = new Color(0.05f, 0.065f, 0.12f), NightZenith = new Color(0.012f, 0.018f, 0.05f), NightHorizon = new Color(0.055f, 0.07f, 0.13f);
        private static readonly int ZenithColorId = Shader.PropertyToID("_ZenithColor");
        private static readonly int HorizonColorId = Shader.PropertyToID("_HorizonColor");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int CloudColorId = Shader.PropertyToID("_CloudColor");
        private static readonly int CloudShadowColorId = Shader.PropertyToID("_CloudShadowColor");
        private static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");
        private static readonly int MoonPhaseId = Shader.PropertyToID("_MoonPhase");
        private static readonly int MoonVisId = Shader.PropertyToID("_MoonVis");
        private static readonly int NightId = Shader.PropertyToID("_Night");

        private Light sun;
        private float baseIntensity, baseYaw, lastHour = -999f;
        private Color baseColor, baseSky, baseEquator, baseGround, baseFog;
        private Material sky;
        private Color skyZenith, skyHorizon, skySun, skyCloud, skyCloudShadow;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<DayNightCycle>() != null) return;
            var go = new GameObject("DayNightCycle");
            DontDestroyOnLoad(go);
            go.AddComponent<DayNightCycle>();
        }

        /// <summary>
        /// The sun at an hour: the direction its light comes from (towards the sky), its elevation's sine, and how much
        /// of the day it is (1 by day, 0 at night, between in the twilight).
        /// </summary>
        public static void SunAt(float hour, out Vector3 towardsSun, out float elevation, out float day)
        {
            float a = (hour - 6f) / 24f * Mathf.PI * 2f; // 0 at sunrise, pi at sunset
            float tilt = NoonElevation * Mathf.Deg2Rad;
            towardsSun = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(tilt), Mathf.Sin(a) * Mathf.Cos(tilt)).normalized;
            elevation = towardsSun.y;
            day = Smooth(-0.2f, 0.15f, elevation); // a twilight of a game hour or so either side
        }

        /// <summary>The moon's phase, 0..1: 0 new, 0.25 first quarter, 0.5 full, 0.75 last quarter.</summary>
        public static float MoonPhase(double gameHours)
        {
            double days = (gameHours + WorldClock.DayStartHour) / 24.0;
            double p = days / LunarDays + PhaseAtStart;
            return (float)(p - System.Math.Floor(p));
        }

        /// <summary>The lit share of the moon's face at a phase: 0 new, 1 full.</summary>
        public static float Illumination(float phase) => (1f - Mathf.Cos(phase * Mathf.PI * 2f)) * 0.5f;

        /// <summary>Where the moon is at an hour of a day of this phase: it lags the sun by phase x 24 hours.</summary>
        public static void MoonAt(float hour, float phase, out Vector3 towardsMoon, out float elevation)
        {
            SunAt(hour - phase * 24f, out towardsMoon, out elevation, out _);
        }

        /// <summary>The moon's light as a share of the sun's: its lit face, while it is over the horizon.</summary>
        public static float MoonLight(float phase, float moonElevation) => MoonShare * Illumination(phase) * Smooth(-0.03f, 0.12f, moonElevation);

        private static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        private void Update()
        {
            if (!SaveLoad.SaveSystem.InGame) return;
            if (RenderSettings.sun == null) return;
            if (RenderSettings.sun != sun)
            {
                Capture(RenderSettings.sun);
                lastHour = -999f;
            }

            float hour = WorldClock.HourOfDay;
            if (Mathf.Abs(Mathf.DeltaAngle(hour * 15f, lastHour * 15f)) < 0.05f) return;
            lastHour = hour;

            SunAt(hour, out Vector3 towards, out float elevation, out float day);
            float phase = MoonPhase(WorldClock.GameHours);
            MoonAt(hour, phase, out Vector3 towardsMoon, out float moonElevation);
            float moonLight = MoonLight(phase, moonElevation), moonFace = Illumination(phase) * Smooth(-0.03f, 0.12f, moonElevation);
            bool sunDown = elevation < 0f;
            Vector3 from = towards;
            if (sunDown)
            {
                // From the moon while it is up, riding no lower than this (a low one lit nothing); with none, from high above
                from = moonElevation > -0.03f ? towardsMoon : new Vector3(0.3f, 1f, 0.2f);
                from.y = Mathf.Max(from.y, 0.45f);
                from.Normalize();
            }
            Quaternion yaw = Quaternion.Euler(0f, baseYaw, 0f);
            Vector3 lightFrom = yaw * from;
            sun.transform.rotation = Quaternion.LookRotation(-lightFrom);

            float high = Smooth(0.05f, 0.45f, elevation);
            Color sunColor = Color.Lerp(Sunset, baseColor, high);
            sun.color = Color.Lerp(Moon, sunColor, day);
            sun.intensity = Mathf.Lerp(baseIntensity * moonLight, baseIntensity * (0.45f + 0.55f * high), day);

            float ambient = Mathf.Lerp(NightAmbient * Mathf.Lerp(NewMoonAmbient, 1f, moonFace), 1f, day);
            Color night = new Color(0.75f, 0.85f, 1.15f); // the night's ambient leans blue
            Color tint = Color.Lerp(night, Color.white, day);
            RenderSettings.ambientSkyColor = baseSky * ambient * tint;
            RenderSettings.ambientEquatorColor = baseEquator * ambient * tint;
            RenderSettings.ambientGroundColor = baseGround * ambient * tint;
            RenderSettings.fogColor = Color.Lerp(NightFog, Color.Lerp(Sunset * 0.8f, baseFog, high), day);

            if (sky != null)
            {
                sky.SetColor(ZenithColorId, Color.Lerp(NightZenith, skyZenith, day));
                sky.SetColor(HorizonColorId, Color.Lerp(NightHorizon, Color.Lerp(Sunset, skyHorizon, high), day));
                sky.SetColor(SunColorId, Color.Lerp(Moon * 0.9f, skySun, day));
                float moonlit = Mathf.Lerp(0.45f, 1f, moonFace); // clouds at night shine with the moon, dull without it
                sky.SetColor(CloudColorId, Color.Lerp(new Color(0.2f, 0.23f, 0.32f) * moonlit, skyCloud, day));
                sky.SetColor(CloudShadowColorId, Color.Lerp(new Color(0.06f, 0.07f, 0.12f) * moonlit, skyCloudShadow, day));
                Vector3 moonDir = yaw * towardsMoon;
                sky.SetVector(MoonDirId, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
                sky.SetFloat(MoonPhaseId, phase);
                sky.SetFloat(MoonVisId, Smooth(-0.02f, 0.06f, moonElevation) * Mathf.Lerp(1f, 0.3f, day)); // pale by day
                sky.SetFloat(NightId, 1f - day);
            }
            Phase = phase;
        }

        /// <summary>The scene's light, ambient and sky: the noon look the cycle goes from.</summary>
        private void Capture(Light light)
        {
            sun = light;
            baseIntensity = light.intensity;
            baseColor = light.color;
            baseYaw = light.transform.eulerAngles.y;
            baseSky = RenderSettings.ambientSkyColor;
            baseEquator = RenderSettings.ambientEquatorColor;
            baseGround = RenderSettings.ambientGroundColor;
            baseFog = RenderSettings.fogColor;
            sky = null;
            Material s = RenderSettings.skybox;
            if (s != null && s.HasProperty(ZenithColorId))
            {
                sky = new Material(s) { name = s.name + " (day and night)" };
                RenderSettings.skybox = sky;
                skyZenith = sky.GetColor(ZenithColorId);
                skyHorizon = sky.GetColor(HorizonColorId);
                skySun = sky.GetColor(SunColorId);
                skyCloud = sky.GetColor(CloudColorId);
                skyCloudShadow = sky.GetColor(CloudShadowColorId);
            }
        }

        /// <summary>The moon's phase now (tests, the HUD).</summary>
        public static float Phase { get; private set; }

        /// <summary>The phase's name: молодик, серп, перша чверть, ... (one a day).</summary>
        public static string PhaseName(float phase)
        {
            int i = Mathf.RoundToInt(phase * 8f) % 8;
            string[] uk = { "молодик", "молодий серп", "перша чверть", "місяць росте", "повня", "місяць спадає", "остання чверть", "старий серп" };
            string[] en = { "new moon", "waxing crescent", "first quarter", "waxing gibbous", "full moon", "waning gibbous", "last quarter", "waning crescent" };
            return UI.Loc.Pick(uk[i], en[i]);
        }

        /// <summary>The current light's share of the scene's noon light (tests).</summary>
        public float Brightness => sun != null && baseIntensity > 0f ? sun.intensity / baseIntensity : 1f;
        public float AmbientShare => baseSky.maxColorComponent > 0f ? RenderSettings.ambientSkyColor.maxColorComponent / baseSky.maxColorComponent : 1f;
    }
}
