using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Shared screenshot camera for the visual capture tools: renders views to PNG plus a contact sheet.</summary>
    public sealed class CaptureRenderer
    {
        private const int Width = 1280, Height = 720, ThumbWidth = 640, ThumbHeight = 360;

        private readonly string dir;
        private readonly Camera cam;
        private readonly RenderTexture rt;
        private readonly RenderTexture thumbRt;
        private readonly Texture2D tex;
        private readonly Texture2D sheet;
        private readonly int cols, rows;
        private readonly GrassFieldRenderer grass;

        public Camera Camera => cam;

        public CaptureRenderer(string outputDir, int viewCount, float fieldOfView = 60f, bool drawGrass = true)
        {
            dir = outputDir;
            Directory.CreateDirectory(dir);
            foreach (string old in Directory.GetFiles(dir, "*.png")) File.Delete(old);

            GameObject camObj = new GameObject("CaptureCamera");
            cam = camObj.AddComponent<Camera>();
            if (Camera.main != null) cam.CopyFrom(Camera.main);
            cam.fieldOfView = fieldOfView;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2000f);
            cam.enabled = false;
            var data = camObj.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.renderShadows = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            cols = viewCount <= 2 ? viewCount : viewCount <= 4 ? 2 : 4;
            rows = Mathf.CeilToInt(viewCount / (float)cols);
            rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            thumbRt = new RenderTexture(ThumbWidth, ThumbHeight, 0, RenderTextureFormat.ARGB32);
            tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            sheet = new Texture2D(cols * ThumbWidth, rows * ThumbHeight, TextureFormat.RGB24, false);
            grass = drawGrass ? Object.FindFirstObjectByType<GrassFieldRenderer>() : null;
        }

        public void Shoot(int index, string name, Vector3 position, Vector3 lookAt)
        {
            cam.transform.position = position;
            cam.transform.LookAt(lookAt);
            cam.targetTexture = rt;
            BattleOfAges.Utils.SkinnedMeshLod.RefreshAll(cam); // crowd LOD meshes as seen from this camera
            if (grass != null) grass.SubmitFor(cam);
            cam.Render();

            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, $"{index + 1:00}_{name}.png"), tex.EncodeToPNG());

            Graphics.Blit(rt, thumbRt);
            RenderTexture.active = thumbRt;
            int col = index % cols, row = rows - 1 - index / cols;
            sheet.ReadPixels(new Rect(0, 0, ThumbWidth, ThumbHeight), col * ThumbWidth, row * ThumbHeight);
            RenderTexture.active = null;
            Debug.Log($"[Capture] {index + 1:00}_{name} pos={position} target={lookAt}");
        }

        public void Finish()
        {
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "sheet.png"), sheet.EncodeToPNG());
            cam.targetTexture = null;
            Object.Destroy(cam.gameObject);
            rt.Release();
            thumbRt.Release();
        }
    }
}
