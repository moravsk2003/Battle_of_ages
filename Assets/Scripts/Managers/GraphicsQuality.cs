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

        public static string GetName(Level level)
        {
            switch (level)
            {
                case Level.Low: return UI.Loc.Pick("Низька", "Low");
                case Level.Medium: return UI.Loc.Pick("Середня", "Medium");
                default: return UI.Loc.Pick("Висока", "High");
            }
        }

        public static string CurrentName => GetName(Current);

        public static Level Current { get; private set; } = Level.High;
        /// <summary>Render scale the dynamic resolution is at now (1 = the screen's resolution).</summary>
        public static float Scale { get; private set; } = 1f;
        public static bool DynamicResolution = true;

        /// <summary>
        /// The dynamic resolution keeps 60 frames a second where the graphics card is what holds the frame: frames slower
        /// than LowerMs (ms, averaged over a second) with the card busy for most of them push the scale down; a card done
        /// in under RaiseMs lets it up. Where the card's time is not known, the frame's own time decides both.
        /// (It used to wait for 33 frames a second before it did anything: a middling card sat at 40.)
        /// </summary>
        public static float LowerMs = 17.5f, RaiseMs = 12f, MinScale = 0.5f;
        /// <summary>The share of a slow frame the card must be busy for the resolution to be what is lowered (a slow processor is not helped by fewer pixels).</summary>
        public const float GpuBoundShare = 0.7f;

        /// <summary>What the dynamic resolution does with a second like this: -1 lower, 1 raise, 0 stay (gpuMs 0: the card's time is not known).</summary>
        public static int Verdict(float frameMs, float gpuMs)
        {
            if (gpuMs > 0f)
            {
                if (frameMs > LowerMs && gpuMs > frameMs * GpuBoundShare) return -1;
                return gpuMs < RaiseMs ? 1 : 0;
            }
            return frameMs > LowerMs ? -1 : frameMs < RaiseMs ? 1 : 0;
        }

        private static GraphicsQuality instance;
        private static UniversalRenderPipelineAsset original, runtimeCopy;
        private static RenderPipelineAsset qualityAssetBefore;
        private static float lodBiasBefore;
        private static readonly Dictionary<ScriptableRendererFeature, bool> featureDefaults = new Dictionary<ScriptableRendererFeature, bool>();

        private float sumMs, windowStart, slowFor, fastFor, gpuSum, workSum, cpuSlowFor, cpuFastFor, lastRaise = -1000f, holdUntil;
        private int frames, gpuFrames;
        private static float raiseHold = 20f;

        /// <summary>
        /// Where the processor is what holds the frame under 60 a second, fewer pixels do not help: the sun's shadows are
        /// what the main thread pays most for on top of the game itself (every cascade culls and draws the casters again),
        /// so they step down first (1: a cascade less, shorter), then to one short cascade with no ambient occlusion and
        /// coarser distant models (2). Back up when the main thread has room again. 0: as the preset has it.
        /// </summary>
        public static int CpuRelief { get; private set; }
        /// <summary>The main thread's own work a frame (ms, without waiting for the screen) under which the relief steps back up.</summary>
        public static float CpuRaiseMs = 9f;
        private static float presetShadowDistance, presetLodBias;
        private static int presetCascades;
        private static bool presetOcclusion;

        /// <summary>What the processor's relief does with a second like this: 1 step down (less work), -1 step back up, 0 stay (workMs 0: not known).</summary>
        public static int CpuVerdict(float frameMs, float gpuMs, float workMs)
        {
            if (workMs <= 0f) return 0;
            if (frameMs > LowerMs && workMs > frameMs * GpuBoundShare && !(gpuMs > frameMs * GpuBoundShare)) return 1;
            return workMs < CpuRaiseMs ? -1 : 0;
        }

        private static void ApplyRelief()
        {
            UniversalRenderPipelineAsset a = runtimeCopy;
            if (a == null) return;
            a.shadowCascadeCount = CpuRelief == 0 ? presetCascades : CpuRelief == 1 ? Mathf.Max(1, presetCascades - 1) : 1;
            a.shadowDistance = CpuRelief == 0 ? presetShadowDistance : CpuRelief == 1 ? Mathf.Max(60f, presetShadowDistance * 0.65f) : Mathf.Min(presetShadowDistance, 60f);
            QualitySettings.lodBias = CpuRelief == 2 ? presetLodBias * 0.75f : presetLodBias;
            SetFeature("ScreenSpaceAmbientOcclusion", presetOcclusion && CpuRelief < 2);
        }
        private readonly FrameTiming[] timing = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { instance = null; original = runtimeCopy = null; featureDefaults.Clear(); Scale = 1f; CpuRelief = 0; raiseHold = 20f; }

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
            if (vram < 6000) return Level.Medium; // (a 4 GB card, GTX 1650 or RX 570, is a middling one: High held it under 60 in a town)
            return Level.High;
        }

        public static void Set(Level level)
        {
            PlayerPrefs.SetInt(PrefKey, (int)level);
            PlayerPrefs.Save();
            Apply(level);
        }

        public static void Cycle() => Set((Level)(((int)Current + 1) % 3));

        /// <summary>Global shader float: how much of the surfaces' fine detail is cut (0 = all of it drawn, the default).</summary>
        /// <summary>How far shadows reach on High (metres).</summary>
        public const float HighShadowDistance = 170f;

        public static readonly int SurfaceDetailCutId = Shader.PropertyToID("_SurfaceDetailCut");

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
                    GrassFieldRenderer.DrawRadius = 24f;
                    GrassFieldRenderer.NearRadius = 7f;
                    GrassFieldRenderer.Stride = 1;
                    break;
                case Level.Medium:
                    a.supportsHDR = true;
                    a.shadowDistance = 110f;
                    a.shadowCascadeCount = 2;
                    a.mainLightShadowmapResolution = 2048;
                    a.maxAdditionalLightsCount = 2;
                    QualitySettings.lodBias = 1.3f;
                    GrassFieldRenderer.DrawRadius = 30f;
                    GrassFieldRenderer.NearRadius = 10f;
                    GrassFieldRenderer.Stride = 1;
                    break;
                default:
                    a.supportsHDR = original.supportsHDR;
                    // (three cascades to 170 m instead of four to 200: every caster drawn once less, the last 30 m of shadows
                    // were faint in the haze anyway)
                    a.shadowDistance = Mathf.Min(original.shadowDistance, HighShadowDistance);
                    a.shadowCascadeCount = Mathf.Min(original.shadowCascadeCount, 3);
                    a.mainLightShadowmapResolution = original.mainLightShadowmapResolution;
                    a.maxAdditionalLightsCount = original.maxAdditionalLightsCount;
                    QualitySettings.lodBias = lodBiasBefore;
                    GrassFieldRenderer.DrawRadius = GrassFieldRenderer.FullDrawRadius;
                    GrassFieldRenderer.NearRadius = GrassFieldRenderer.FullNearRadius;
                    GrassFieldRenderer.Stride = 1;
                    break;
            }
            presetShadowDistance = a.shadowDistance;
            presetCascades = a.shadowCascadeCount;
            presetLodBias = QualitySettings.lodBias;
            presetOcclusion = level == Level.High;
            CpuRelief = 0;
            ApplyRelief();
            // The surfaces' fine procedural detail (weave, pores, pebbles, ragged leafy crowns): none on Low, half on Medium
            Shader.SetGlobalFloat(SurfaceDetailCutId, level == Level.Low ? 1f : level == Level.Medium ? 0.5f : 0f);
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
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0.0)
            {
                gpuSum += (float)timing[0].gpuFrameTime;
                workSum += (float)System.Math.Max(0.0, timing[0].cpuMainThreadFrameTime - timing[0].cpuMainThreadPresentWaitTime);
                gpuFrames++;
            }
            if (Time.unscaledTime - windowStart < 1f) return;
            float avg = sumMs / Mathf.Max(1, frames), gpu = gpuFrames > frames / 2 ? gpuSum / gpuFrames : 0f, work = gpuFrames > frames / 2 ? workSum / gpuFrames : 0f;
            float window = Time.unscaledTime - windowStart;
            sumMs = 0f; frames = 0; gpuSum = 0f; workSum = 0f; gpuFrames = 0; windowStart = Time.unscaledTime;
            if (window > 3f) return; // a load or a pause, not the frame rate

            // The processor's side: three slow seconds step the shadows down; a good while of room steps them back up, and
            // each time that only brings the slow frames back the wait before the next try doubles
            int cpu = CpuVerdict(avg, gpu, work);
            if (cpu > 0) { cpuSlowFor += window; cpuFastFor = 0f; }
            else if (cpu < 0) { cpuFastFor += window; cpuSlowFor = 0f; }
            else cpuSlowFor = cpuFastFor = 0f;
            if (cpuSlowFor >= 3f && CpuRelief < 2)
            {
                if (Time.unscaledTime - lastRaise < 30f) raiseHold = Mathf.Min(raiseHold * 2f, 600f);
                CpuRelief++; cpuSlowFor = 0f; holdUntil = Time.unscaledTime + raiseHold;
                ApplyRelief();
            }
            else if (cpuFastFor >= 10f && CpuRelief > 0 && Time.unscaledTime >= holdUntil)
            {
                CpuRelief--; cpuFastFor = 0f; lastRaise = Time.unscaledTime;
                ApplyRelief();
            }

            int verdict = Verdict(avg, gpu);
            if (Scale >= MaxScale && verdict > 0) verdict = 0;
            if (verdict < 0) { slowFor += window; fastFor = 0f; }
            else if (verdict > 0) { fastFor += window; slowFor = 0f; }
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
