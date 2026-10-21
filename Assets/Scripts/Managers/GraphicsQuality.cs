using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BattleOfAges.World;

namespace BattleOfAges.Managers
{
    /// <summary>
    /// Graphics presets (docs/GAME_DESIGN.md §17): Low for integrated GPUs (Intel UHD / Iris, AMD APUs), Medium, High
    /// (the look the game is made at). The first launch picks one by the GPU; the player changes it in the settings.
    /// On top of the preset a dynamic resolution keeps the frame rate: when the frames are slow for a while the render
    /// scale steps down (FSR upscales it back to the screen), and goes back up when there is room again.
    /// The URP asset is a runtime copy, so the project's asset is never changed; the SSAO feature lives on the shared
    /// renderer data and is restored on quit.
    /// </summary>
    public sealed class GraphicsQuality : MonoBehaviour
    {
        public enum Level { Low = 0, Medium = 1, High = 2 }

        private const string PrefKey = "Settings_Graphics";
        public static readonly string[] Names = { "Низька", "Середня", "Висока" };

        public static Level Current { get; private set; } = Level.High;
        /// <summary>Render scale the dynamic resolution is at now (1 = the screen's resolution).</summary>
        public static float Scale { get; private set; } = 1f;
        public static bool DynamicResolution = true;

        /// <summary>Frames slower than this (ms, averaged over a second) push the scale down; faster than RaiseMs let it up.</summary>
        public static float LowerMs = 30f, RaiseMs = 19f, MinScale = 0.5f;

        private static GraphicsQuality instance;
        private static UniversalRenderPipelineAsset original, runtimeCopy;
        private static RenderPipelineAsset qualityAssetBefore;
        private static float lodBiasBefore;
        private static readonly Dictionary<ScriptableRendererFeature, bool> featureDefaults = new Dictionary<ScriptableRendererFeature, bool>();

        private float sumMs, windowStart, slowFor, fastFor;
        private int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; original = runtimeCopy = null; featureDefaults.Clear(); Scale = 1f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Application.isBatchMode) return; // tests and probes measure the game as it is made
            var go = new GameObject("GraphicsQuality");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GraphicsQuality>();
            int saved = PlayerPrefs.GetInt(PrefKey, -1);
            Apply(saved >= 0 && saved <= 2 ? (Level)saved : Detect());
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => ApplyCameras();
            Application.quitting += RestoreShared;
        }

        /// <summary>The preset for this machine: integrated graphics or little video memory get Low.</summary>
        public static Level Detect()
        {
            string gpu = (SystemInfo.graphicsDeviceName ?? "").ToLowerInvariant();
            int vram = SystemInfo.graphicsMemorySize;
            bool integrated = gpu.Contains("intel") || gpu.Contains("uhd") || gpu.Contains("iris")
                              || gpu.Contains("radeon(tm) graphics") || gpu.Contains("radeon graphics") || gpu.Contains("vega")
                              || gpu.Contains("microsoft basic");
            if (integrated || vram < 2000) return Level.Low;
            if (vram < 4000) return Level.Medium;
            return Level.High;
        }

        public static void Set(Level level)
        {
            PlayerPrefs.SetInt(PrefKey, (int)level);
            PlayerPrefs.Save();
            Apply(level);
        }

        public static void Cycle() => Set((Level)(((int)Current + 1) % 3));

        public static void Apply(Level level)
        {
            Current = level;
            if (original == null) original = QualitySettings.renderPipeline as UniversalRenderPipelineAsset
                                             ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (original == null) return;
            if (runtimeCopy == null)
            {
                runtimeCopy = Instantiate(original);
                qualityAssetBefore = QualitySettings.renderPipeline;
                lodBiasBefore = QualitySettings.lodBias;
                runtimeCopy.name = original.name + " (runtime)";
                QualitySettings.renderPipeline = runtimeCopy;
            }
            UniversalRenderPipelineAsset a = runtimeCopy;

            a.supportsCameraOpaqueTexture = false; // nothing samples the scene colour (water reads only the depth)
            switch (level)
            {
                case Level.Low:
                    a.supportsHDR = false;
                    a.shadowDistance = 60f;
                    a.shadowCascadeCount = 1;
                    a.mainLightShadowmapResolution = 1024;
                    a.maxAdditionalLightsCount = 1;
                    QualitySettings.lodBias = 0.8f;
                    GrassFieldRenderer.DrawRadius = 22f;
                    GrassFieldRenderer.Stride = 2;
                    break;
                case Level.Medium:
                    a.supportsHDR = true;
                    a.shadowDistance = 110f;
                    a.shadowCascadeCount = 2;
                    a.mainLightShadowmapResolution = 2048;
                    a.maxAdditionalLightsCount = 2;
                    QualitySettings.lodBias = 1.3f;
                    GrassFieldRenderer.DrawRadius = 30f;
                    GrassFieldRenderer.Stride = 1;
                    break;
                default:
                    a.supportsHDR = original.supportsHDR;
                    a.shadowDistance = original.shadowDistance;
                    a.shadowCascadeCount = original.shadowCascadeCount;
                    a.mainLightShadowmapResolution = original.mainLightShadowmapResolution;
                    a.maxAdditionalLightsCount = original.maxAdditionalLightsCount;
                    QualitySettings.lodBias = lodBiasBefore;
                    GrassFieldRenderer.DrawRadius = GrassFieldRenderer.FullDrawRadius;
                    GrassFieldRenderer.Stride = 1;
                    break;
            }
            SetFeature("ScreenSpaceAmbientOcclusion", level == Level.High);
            Scale = MaxScale;
            a.renderScale = Scale;
            a.upscalingFilter = UpscalingFilterSelection.FSR;
            ApplyCameras();
            if (instance != null) instance.slowFor = instance.fastFor = 0f;
        }

        /// <summary>The top of the dynamic resolution for the preset.</summary>
        private static float MaxScale => Current == Level.Low ? 0.8f : 1f;

        private static void SetFeature(string nameContains, bool on)
        {
            foreach (ScriptableRendererData data in original.rendererDataList)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                {
                    if (f == null || !f.name.Contains(nameContains)) continue;
                    if (!featureDefaults.ContainsKey(f)) featureDefaults[f] = f.isActive;
                    f.SetActive(on && featureDefaults[f]);
                }
            }
        }

        private static void RestoreShared()
        {
            foreach (var kv in featureDefaults) if (kv.Key != null) kv.Key.SetActive(kv.Value);
            featureDefaults.Clear();
            if (runtimeCopy != null) { QualitySettings.renderPipeline = qualityAssetBefore; QualitySettings.lodBias = lodBiasBefore; } // the editor keeps the project as it was
        }

        /// <summary>
        /// Low draws no post effects (bloom and grading are full-screen passes an integrated GPU feels) and the sun casts
        /// hard shadows (the soft ones filter every shadowed pixel many times).
        /// </summary>
        private static void ApplyCameras()
        {
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional || l.shadows == LightShadows.None) continue;
                l.shadows = Current == Level.Low ? LightShadows.Hard : LightShadows.Soft;
            }
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam.targetTexture != null) continue; // minimap and captures keep their own setup
                UniversalAdditionalCameraData d = cam.GetUniversalAdditionalCameraData();
                if (d != null && d.renderType == CameraRenderType.Base) d.renderPostProcessing = Current != Level.Low;
            }
        }

        private void Update()
        {
            if (runtimeCopy == null || !DynamicResolution) return;
            sumMs += Time.unscaledDeltaTime * 1000f;
            frames++;
            if (Time.unscaledTime - windowStart < 1f) return;
            float avg = sumMs / Mathf.Max(1, frames);
            float window = Time.unscaledTime - windowStart;
            sumMs = 0f; frames = 0; windowStart = Time.unscaledTime;
            if (window > 3f) return; // a load or a pause, not the frame rate

            if (avg > LowerMs) { slowFor += window; fastFor = 0f; }
            else if (avg < RaiseMs) { fastFor += window; slowFor = 0f; }
            else slowFor = fastFor = 0f;

            float target = Scale;
            if (slowFor >= 2f) { target = Mathf.Max(MinScale, Scale - 0.1f); slowFor = 0f; }
            else if (fastFor >= 6f) { target = Mathf.Min(MaxScale, Scale + 0.05f); fastFor = 0f; }
            if (!Mathf.Approximately(target, Scale))
            {
                Scale = target;
                runtimeCopy.renderScale = Scale;
            }
        }
    }
}
