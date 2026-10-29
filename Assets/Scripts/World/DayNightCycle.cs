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
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        /// <summary>The sun's highest elevation (noon), degrees.</summary>
        public const float NoonElevation = 62f;
        /// <summary>At night: the moon's share of the sun's light, the ambient light's share of the day's.</summary>
        public const float MoonShare = 0.16f, NightAmbient = 0.38f;

        private static readonly Color Moon = new Color(0.62f, 0.72f, 1f), Sunset = new Color(1f, 0.58f, 0.34f);
        private static readonly Color NightFog = new Color(0.07f, 0.09f, 0.16f), NightZenith = new Color(0.02f, 0.03f, 0.08f), NightHorizon = new Color(0.08f, 0.1f, 0.18f);

        private Light sun;
        private float baseIntensity, baseYaw;
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

        private static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        private void Update()
        {
            if (!SaveLoad.SaveSystem.InGame) return;
            if (RenderSettings.sun == null) return;
            if (RenderSettings.sun != sun) Capture(RenderSettings.sun);

            SunAt(WorldClock.HourOfDay, out Vector3 towards, out float elevation, out float day);
            bool moonUp = elevation < 0f;
            Vector3 from = moonUp ? -towards : towards;
            if (moonUp) { from.y = Mathf.Max(from.y, 0.5f); from.Normalize(); } // the moon rides high all night (a low one lit nothing)
            Vector3 lightFrom = Quaternion.Euler(0f, baseYaw, 0f) * from;
            sun.transform.rotation = Quaternion.LookRotation(-lightFrom);

            float high = Smooth(0.05f, 0.45f, elevation);
            Color sunColor = Color.Lerp(Sunset, baseColor, high);
            sun.color = Color.Lerp(Moon, sunColor, day);
            sun.intensity = Mathf.Lerp(baseIntensity * MoonShare, baseIntensity * (0.45f + 0.55f * high), day);

            float ambient = Mathf.Lerp(NightAmbient, 1f, day);
            Color night = new Color(0.75f, 0.85f, 1.15f); // the night's ambient leans blue
            Color tint = Color.Lerp(night, Color.white, day);
            RenderSettings.ambientSkyColor = baseSky * ambient * tint;
            RenderSettings.ambientEquatorColor = baseEquator * ambient * tint;
            RenderSettings.ambientGroundColor = baseGround * ambient * tint;
            RenderSettings.fogColor = Color.Lerp(NightFog, Color.Lerp(Sunset * 0.8f, baseFog, high), day);

            if (sky != null)
            {
                sky.SetColor("_ZenithColor", Color.Lerp(NightZenith, skyZenith, day));
                sky.SetColor("_HorizonColor", Color.Lerp(NightHorizon, Color.Lerp(Sunset, skyHorizon, high), day));
                sky.SetColor("_SunColor", Color.Lerp(Moon * 0.9f, skySun, day));
                sky.SetColor("_CloudColor", Color.Lerp(new Color(0.2f, 0.23f, 0.32f), skyCloud, day));
                sky.SetColor("_CloudShadowColor", Color.Lerp(new Color(0.06f, 0.07f, 0.12f), skyCloudShadow, day));
            }
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
            if (s != null && s.HasProperty("_ZenithColor"))
            {
                sky = new Material(s) { name = s.name + " (day and night)" };
                RenderSettings.skybox = sky;
                skyZenith = sky.GetColor("_ZenithColor");
                skyHorizon = sky.GetColor("_HorizonColor");
                skySun = sky.GetColor("_SunColor");
                skyCloud = sky.GetColor("_CloudColor");
                skyCloudShadow = sky.GetColor("_CloudShadowColor");
            }
        }

        /// <summary>The current light's share of the scene's noon light (tests).</summary>
        public float Brightness => sun != null && baseIntensity > 0f ? sun.intensity / baseIntensity : 1f;
        public float AmbientShare => baseSky.maxColorComponent > 0f ? RenderSettings.ambientSkyColor.maxColorComponent / baseSky.maxColorComponent : 1f;
    }
}
