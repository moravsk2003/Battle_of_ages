using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool (not a regular test): plays SampleScene and renders screenshots from a fixed list of
    /// viewpoints, so graphics changes can be compared run to run. Explicit, so "Run All" skips it.
    /// Environment: CAPTURE_VIEWS = number of viewpoints (1-14, in priority order), CAPTURE_DIR = output folder.
    /// Writes NN_name.png per view plus sheet.png (a contact sheet of all views).
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class GraphicsCapture
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int ThumbWidth = 640;
        private const int ThumbHeight = 360;

        private class View
        {
            public string Name;
            public Vector2? PlayerXZ;                 // teleport the player (world streams around it) before shooting
            public Func<(Vector3 pos, Vector3 target)> Pose;
        }

        private static float H(float x, float z) => InfiniteWorldManager.GetSampledTerrainHeight(x, z);
        private static Vector3 Ground(float x, float z, float up = 0f) => new Vector3(x, H(x, z) + up, z);

        private Vector3 campCenter;
        private Vector2 forestXZ, mountainXZ, desertXZ;

        [UnityTest]
        public IEnumerator CaptureViews()
        {
            int count = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_VIEWS"), out int n) ? n : 4, 1, 14);
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "capture");
            Directory.CreateDirectory(dir);
            foreach (string old in Directory.GetFiles(dir, "*.png")) File.Delete(old);

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            GameObject player = GameObject.Find("Player");
            Assert.IsNotNull(player);
            HealthComponent hp = player.GetComponent<HealthComponent>();
            if (hp != null) hp.InitializeHealth(1000000);

            FindLandmarks();
            List<View> views = BuildViews();

            GrassFieldRenderer grass = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            Camera main = Camera.main;
            GameObject camObj = new GameObject("CaptureCamera");
            Camera cam = camObj.AddComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2000f);
            cam.enabled = false;
            var camData = camObj.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.renderShadows = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            int cols = count <= 2 ? count : count <= 4 ? 2 : 4;
            int rows = Mathf.CeilToInt(count / (float)cols);
            var sheet = new Texture2D(cols * ThumbWidth, rows * ThumbHeight, TextureFormat.RGB24, false);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var thumbRt = new RenderTexture(ThumbWidth, ThumbHeight, 0, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);

            for (int i = 0; i < count; i++)
            {
                View v = views[i];
                if (v.PlayerXZ.HasValue)
                {
                    Teleport(player, v.PlayerXZ.Value);
                    yield return new WaitForSeconds(1.5f); // chunks, props and AI settle around the player
                }

                (Vector3 pos, Vector3 target) = v.Pose();
                cam.transform.position = pos;
                cam.transform.LookAt(target);
                cam.targetTexture = rt;
                yield return null;
                if (grass != null) grass.SubmitFor(cam);
                cam.Render();

                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{i + 1:00}_{v.Name}.png"), tex.EncodeToPNG());

                Graphics.Blit(rt, thumbRt);
                RenderTexture.active = thumbRt;
                int col = i % cols, row = rows - 1 - i / cols; // first view at top-left
                sheet.ReadPixels(new Rect(0, 0, ThumbWidth, ThumbHeight), col * ThumbWidth, row * ThumbHeight);
                RenderTexture.active = null;
                Debug.Log($"[GraphicsCapture] {i + 1:00}_{v.Name} pos={pos} target={target}");
            }

            sheet.Apply();
            File.WriteAllBytes(Path.Combine(dir, "sheet.png"), sheet.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.Destroy(camObj);
            rt.Release();
            thumbRt.Release();
        }

        private void FindLandmarks()
        {
            InfiniteWorldManager world = InfiniteWorldManager.Instance;
            forestXZ = mountainXZ = desertXZ = new Vector2(300f, 300f);
            float bestHeight = float.MinValue;
            bool forest = false, desert = false;
            for (float r = 160f; r <= 900f; r += 40f)
            {
                for (int a = 0; a < 24; a++)
                {
                    float ang = a * Mathf.PI * 2f / 24f;
                    Vector2 p = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                    float h = H(p.x, p.y);
                    if (h > bestHeight) { bestHeight = h; mountainXZ = p; }
                    if (world == null) continue;
                    BiomeType biome = world.GetBiomeAtWorldPos(new Vector3(p.x, 0f, p.y));
                    if (!forest && biome == BiomeType.PineForest) { forest = true; forestXZ = p; }
                    if (!desert && biome == BiomeType.Desert) { desert = true; desertXZ = p; }
                }
            }
            Debug.Log($"[GraphicsCapture] forest={forest}:{forestXZ} desert={desert}:{desertXZ} peak={bestHeight:F1}m at {mountainXZ}");
        }

        private List<View> BuildViews()
        {
            float g = H(0f, 0f);
            return new List<View>
            {
                // Priority order: the first N are used for quick iterations.
                new View { Name = "fps_base", PlayerXZ = new Vector2(0f, -6f), Pose = () => (new Vector3(0f, g + 1.7f, -7f), new Vector3(1.5f, g + 1.0f, 6f)) },
                new View { Name = "base_overview", Pose = () => (new Vector3(-45f, g + 24f, -45f), new Vector3(0f, g, 0f)) },
                new View { Name = "vista_north", Pose = () => (new Vector3(0f, g + 4f, 35f), Ground(0f, 260f, 10f)) },
                new View { Name = "hostile_camp", PlayerXZ = new Vector2(-416f, 416f), Pose = () =>
                    {
                        campCenter = FindCamp(new Vector3(-416f, 0f, 416f));
                        Vector3 eye = Ground(campCenter.x - 16f, campCenter.z - 16f, 6f);
                        return (eye, campCenter + Vector3.up * 1.5f);
                    } },
                new View { Name = "base_iso", PlayerXZ = new Vector2(0f, -6f), Pose = () => (new Vector3(-13f, g + 8f, -13f), new Vector3(0f, g + 1f, 2f)) },
                new View { Name = "vista_east", Pose = () => (new Vector3(35f, g + 4f, 0f), Ground(260f, 0f, 10f)) },
                new View { Name = "pine_forest", PlayerXZ = forestXZ, Pose = () => (Ground(forestXZ.x, forestXZ.y, 1.8f), Ground(forestXZ.x + 40f, forestXZ.y + 40f, 4f)) },
                new View { Name = "mountains", PlayerXZ = mountainXZ * 0.8f, Pose = () =>
                    {
                        Vector2 from = mountainXZ * 0.8f;
                        return (Ground(from.x, from.y, 12f), Ground(mountainXZ.x, mountainXZ.y, 2f));
                    } },
                new View { Name = "vista_south", PlayerXZ = new Vector2(0f, -6f), Pose = () => (new Vector3(0f, g + 4f, -35f), Ground(0f, -260f, 10f)) },
                new View { Name = "vista_west", Pose = () => (new Vector3(-35f, g + 4f, 0f), Ground(-260f, 0f, 10f)) },
                new View { Name = "top_down", Pose = () => (new Vector3(0f, g + 140f, -1f), new Vector3(0f, g, 0f)) },
                new View { Name = "desert", PlayerXZ = desertXZ, Pose = () => (Ground(desertXZ.x, desertXZ.y, 3f), Ground(desertXZ.x + 60f, desertXZ.y + 30f, 3f)) },
                new View { Name = "grass_closeup", PlayerXZ = new Vector2(0f, -6f), Pose = () => (new Vector3(-6f, g + 0.7f, -14f), new Vector3(-2f, g + 0.3f, -4f)) },
                new View { Name = "sky_low_angle", Pose = () => (new Vector3(6f, g + 1.2f, -12f), new Vector3(0f, g + 14f, 25f)) },
            };
        }

        private static Vector3 FindCamp(Vector3 near)
        {
            Vector3 best = near;
            float bestDist = float.MaxValue;
            foreach (HostileSettlementLeader leader in UnityEngine.Object.FindObjectsByType<HostileSettlementLeader>(FindObjectsSortMode.None))
            {
                Vector3 p = leader.transform.parent != null ? leader.transform.parent.position : leader.transform.position;
                float d = Vector3.Distance(p, near);
                if (d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        private static void Teleport(GameObject player, Vector2 xz)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = Ground(xz.x, xz.y, 1f);
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
        }
    }
}
