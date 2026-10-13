#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.IO;

namespace BattleOfAges.EditorTools
{
    public static class SetupLightingAndPostProcessingTool
    {
        [MenuItem("Battle of Ages/Setup AAA Lighting & Post Processing")]
        public static void SetupLightingAndPostProcessing()
        {
            string scenePath = "Assets/Scenes/SampleScene.unity";
            if (File.Exists(scenePath) && EditorSceneManager.GetActiveScene().path != scenePath)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            // 1. Setup Main Directional Light (Warm Golden Sun)
            GameObject sunObj = GameObject.Find("Directional Light");
            if (sunObj == null)
            {
                sunObj = new GameObject("Directional Light");
            }

            Light sun = sunObj.GetComponent<Light>();
            if (sun == null) sun = sunObj.AddComponent<Light>();

            sun.type = LightType.Directional;
            sun.color = new Color(1.0f, 0.95f, 0.86f); // Warm golden sun
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1.0f;
            sun.shadowNormalBias = 0.2f;
            sun.shadowBias = 0.05f;

            UniversalAdditionalLightData sunData = sunObj.GetComponent<UniversalAdditionalLightData>();
            if (sunData == null) sunData = sunObj.AddComponent<UniversalAdditionalLightData>();
            sunData.usePipelineSettings = true;

            sunObj.transform.rotation = Quaternion.Euler(40f, -38f, 0f); // afternoon sun: long readable shadows
            RenderSettings.sun = sun;

            // 2. Setup Environment Ambient & Fog
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.74f, 0.94f) * 0.9f;   // Sky blue
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.62f, 0.38f) * 0.85f; // Steppe foliage bounce
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.24f, 0.16f) * 0.8f;  // Warm earth bounce
            RenderSettings.ambientIntensity = 1.0f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.74f, 0.82f, 0.90f); // warm light haze = sky horizon colour
            RenderSettings.fogDensity = 0.0014f; // aerial perspective: clear nearby, hazy distant mountains (far terrain ~1.5 km)

            GameObject mainCamObj = GameObject.FindWithTag("MainCamera");
            if (mainCamObj != null && mainCamObj.TryGetComponent(out Camera mainCam)) mainCam.farClipPlane = 2000f;

            // Procedural sky with clouds; its horizon colour matches the fog colour.
            const string skyPath = "Assets/Settings/AtmosphericSky.mat";
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            Shader skyShader = Shader.Find("Custom/AtmosphericSky");
            if (sky == null && skyShader != null)
            {
                sky = new Material(skyShader) { name = "AtmosphericSky" };
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            if (sky != null)
            {
                sky.SetColor("_HorizonColor", RenderSettings.fogColor);
                sky.SetColor("_ZenithColor", new Color(0.30f, 0.50f, 0.80f));
                RenderSettings.skybox = sky;
                EditorUtility.SetDirty(sky);
            }

            // 3. Setup Global Volume with ACES Tonemapping, Bloom, SSAO, Color Adjustments, Vignette
            GameObject volumeObj = GameObject.Find("Global Volume");
            if (volumeObj == null)
            {
                volumeObj = new GameObject("Global Volume");
            }

            Volume volume = volumeObj.GetComponent<Volume>();
            if (volume == null) volume = volumeObj.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            string profilePath = "Assets/Settings/AAA_MasterVolumeProfile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                Directory.CreateDirectory("Assets/Settings");
                AssetDatabase.CreateAsset(profile, profilePath);
            }

            // Configure Bloom
            if (!profile.TryGet(out Bloom bloom))
            {
                bloom = profile.Add<Bloom>(true);
            }
            bloom.active = true;
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            bloom.tint.Override(new Color(1.0f, 0.96f, 0.90f));
            bloom.highQualityFiltering.Override(true);

            // Configure Tonemapping (ACES Film Curve)
            if (!profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping = profile.Add<Tonemapping>(true);
            }
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);

            // Configure Color Adjustments
            if (!profile.TryGet(out ColorAdjustments colorAdj))
            {
                colorAdj = profile.Add<ColorAdjustments>(true);
            }
            colorAdj.active = true;
            colorAdj.postExposure.Override(0.08f);
            colorAdj.contrast.Override(20f);
            colorAdj.saturation.Override(6f);
            colorAdj.colorFilter.Override(Color.white);

            // Configure White Balance
            if (!profile.TryGet(out WhiteBalance whiteBal))
            {
                whiteBal = profile.Add<WhiteBalance>(true);
            }
            whiteBal.active = true;
            whiteBal.temperature.Override(3f);
            whiteBal.tint.Override(0f);

            // Configure Shadows, Midtones, Highlights
            if (!profile.TryGet(out ShadowsMidtonesHighlights smh))
            {
                smh = profile.Add<ShadowsMidtonesHighlights>(true);
            }
            smh.active = true;
            smh.shadows.Override(new Vector4(0.95f, 0.98f, 1.04f, 0.01f));
            smh.highlights.Override(new Vector4(1.03f, 1.01f, 0.98f, 0.02f));

            // Configure Vignette
            if (!profile.TryGet(out Vignette vignette))
            {
                vignette = profile.Add<Vignette>(true);
            }
            vignette.active = true;
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.35f);
            vignette.rounded.Override(false);

            EditorUtility.SetDirty(profile);
            volume.profile = profile;

            // 4. Create Atmospheric Floating Pollen / Dust Motes Particle System
            SetupAtmosphericDustMotes();

            // 5. Update URP Quality & Renderer Assets
            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset != null)
            {
                urpAsset.shadowDistance = 200f;
                urpAsset.shadowCascadeCount = 4;
                urpAsset.cascade4Split = new Vector3(0.08f, 0.22f, 0.50f);
                urpAsset.supportsHDR = true;
                EditorUtility.SetDirty(urpAsset);
            }

            // Sky reflections for metal (helmets, swords, armour). Nothing is baked in this project, so without a
            // probe metallic surfaces reflect black. A realtime probe that renders only the sky, once on Awake.
            GameObject probeObj = GameObject.Find("SkyReflectionProbe") ?? new GameObject("SkyReflectionProbe");
            probeObj.transform.position = new Vector3(0f, 40f, 0f);
            ReflectionProbe probe = probeObj.GetComponent<ReflectionProbe>();
            if (probe == null) probe = probeObj.AddComponent<ReflectionProbe>(); // not "??": Unity fake-null
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = 0;
            probe.size = new Vector3(6000f, 3000f, 6000f);
            probe.resolution = 128;
            probe.importance = 0;
            probe.boxProjection = false;

            // Shaders only created from code (Shader.Find) must be in the build.
            EnsureAlwaysIncluded("Custom/GrassBlades");
            EnsureAlwaysIncluded("Custom/StylizedTerrain");
            EnsureAlwaysIncluded("Custom/ProceduralRock");

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log("[Battle of Ages] Balanced AAA Lighting, Atmosphere & Post Processing Volume configured!");
        }

        private static void EnsureAlwaysIncluded(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) return;
            var graphicsSettings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(graphicsSettings);
            SerializedProperty list = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            }
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
        }

        private static void SetupAtmosphericDustMotes()
        {
            GameObject dustObj = GameObject.Find("AtmosphericDustMotes");
            if (dustObj == null)
            {
                dustObj = new GameObject("AtmosphericDustMotes");
            }

            ParticleSystem ps = dustObj.GetComponent<ParticleSystem>();
            if (ps == null) ps = dustObj.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.duration = 10f;
            main.startLifetime = 8f;
            main.startSpeed = 0.3f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.startColor = new Color(1.0f, 0.95f, 0.75f, 0.5f);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 20f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 12f, 40f);
            shape.position = new Vector3(0f, 6f, 0f);

            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.4f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.15f);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.4f;

            ParticleSystemRenderer psr = dustObj.GetComponent<ParticleSystemRenderer>();
            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            Material dustMat = new Material(particleShader);
            dustMat.name = "MAT_DustMotes";
            dustMat.SetColor("_BaseColor", new Color(1.0f, 0.95f, 0.70f, 0.5f));
            psr.sharedMaterial = dustMat;
        }
    }
}
#endif
