#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.IO;

[InitializeOnLoad]
public static class AgentVisionCapture
{
    private static readonly string VisionDir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision");
    private static readonly string TriggerPath = Path.Combine(VisionDir, "trigger.txt");
    private static readonly string StatusPath = Path.Combine(VisionDir, "status.txt");

    static AgentVisionCapture()
    {
        EditorApplication.update += CheckTrigger;
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(TriggerPath))
            {
                CaptureMultiAngle();
            }
        };
    }

    private static void CheckTrigger()
    {
        if (File.Exists(TriggerPath))
        {
            try
            {
                File.Delete(TriggerPath);
                CaptureMultiAngle();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[AgentVision] Error processing trigger: {ex.Message}");
            }
        }
    }

    [MenuItem("AgentVision/Rebuild Environment & Capture")]
    public static void RebuildAndCapture()
    {
        BattleOfAges.EditorTools.SetupSteppeEnvironmentTool.SetupSteppeEnvironment();
        CaptureMultiAngle();
    }

    [MenuItem("AgentVision/Capture Multi-Angle")]
    public static void CaptureMultiAngle()
    {
        string scenePath = "Assets/Scenes/SampleScene.unity";
        if (File.Exists(scenePath) && EditorSceneManager.GetActiveScene().path != scenePath)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        Directory.CreateDirectory(VisionDir);

        GameObject camObj = new GameObject("AgentInspectionCamera");
        Camera cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = 60;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 1000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.allowHDR = true;
        cam.allowMSAA = true;

        UniversalAdditionalCameraData camData = camObj.AddComponent<UniversalAdditionalCameraData>();
        if (camData != null)
        {
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
        }

        // Ракурси задаються відносно реальної висоти землі в центрі бази (база не на y = 0).
        float groundY = BattleOfAges.RTS.RtsUnit.GetTerrainHeight(Vector3.zero);
        Vector3 g = Vector3.up * groundY;

        // 4 ракурси для усунення сліпих зон + 2 загальні плани бази та рельєфу навколо
        var views = new (string name, Vector3 pos, Vector3 target)[] {
            ("front", g + new Vector3(0, 3, -8), g + Vector3.up * 1.5f),
            ("side", g + new Vector3(-8, 3, 0), g + Vector3.up * 1.5f),
            ("top", g + new Vector3(0, 12, 0.1f), g),
            ("iso", g + new Vector3(-6, 6, -6), g + Vector3.up * 1.5f),
            ("overview", g + new Vector3(-75, 30, -75), g),
            ("horizon", g + new Vector3(0, 2, -45), g + new Vector3(0, 2, 60))
        };

        foreach (var v in views)
        {
            cam.transform.position = v.pos;
            cam.transform.LookAt(v.target);

            RenderTexture rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            Texture2D tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);

            File.WriteAllBytes(Path.Combine(VisionDir, $"{v.name}.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        Object.DestroyImmediate(camObj);
        File.WriteAllText(StatusPath, "READY " + System.DateTime.UtcNow.ToString("o"));
        Debug.Log($"[AgentVision] Усі {views.Length} скріншоти успішно збережено.");
    }
}
#endif
