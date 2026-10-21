using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Managers;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// What the grass costs the GPU (docs/GAME_DESIGN.md §17): renders the player's view into 1920x1080 and waits for
    /// the GPU to finish each frame (a readback), so the time is the whole frame on the GPU, not only its submission.
    /// Compares no grass and grass, on the High and Low presets, looking along the ground and down the slope.
    /// Writes grass_report.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Performance review tool, run on demand")]
    public class GrassProbe
    {
        private Camera cam;
        private RenderTexture rt;
        private GrassFieldRenderer grass;

        [UnityTest, Timeout(600000)]
        public IEnumerator GrassCost()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            float until = Time.realtimeSinceStartup + 6f;
            while (Time.realtimeSinceStartup < until) yield return null; // the chunks around stream in

            grass = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            Assert.IsNotNull(grass, "the world has a grass field");
            grass.enabled = false; // drawn here, for this camera only
            cam = Camera.main;
            cam.enabled = false;
            int w = int.Parse(Environment.GetEnvironmentVariable("GRASS_W") ?? "1920"), h = int.Parse(Environment.GetEnvironmentVariable("GRASS_H") ?? "1080");
            rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;

            GameObject player = GameObject.Find("Player");
            Vector3 eye = player.transform.position;
            eye.y = InfiniteWorldManager.GetSampledTerrainHeight(eye.x, eye.z) + 1.7f;

            var report = new StringBuilder();
            report.AppendLine("# Grass cost on the GPU");
            report.AppendLine($"{SystemInfo.graphicsDeviceName}, {w}x{h}, GPU time per frame (median of 40, readback-synced), ms.");
            report.AppendLine();
            report.AppendLine("| preset | view | no grass | grass | grass costs | clumps drawn |");
            report.AppendLine("|---|---|---|---|---|---|");
            foreach (GraphicsQuality.Level level in new[] { GraphicsQuality.Level.High, GraphicsQuality.Level.Low })
            {
                GraphicsQuality.Apply(level);
                foreach (var view in new[] { ("along the ground", 0f), ("down the slope", 25f) })
                {
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(view.Item2, 40f, 0f));
                    yield return null;
                    float off = Measure(false), on = Measure(true);
                    Shot(Path.Combine(dir, $"grass_{level}_{(view.Item2 > 0f ? "slope" : "ground")}.png"));
                    report.AppendLine($"| {GraphicsQuality.Names[(int)level]} | {view.Item1} | {off:0.00} | {on:0.00} | {on - off:0.00} | {GrassFieldRenderer.LastDrawn} |");
                }
            }
            GraphicsQuality.Apply(GraphicsQuality.Level.High);
            File.WriteAllText(Path.Combine(dir, "grass_report.md"), report.ToString());
            Debug.Log(report.ToString());
            cam.targetTexture = null;
            rt.Release();
        }

        private void Shot(string path)
        {
            grass.SubmitFor(cam);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        private float Measure(bool withGrass)
        {
            var times = new List<float>();
            for (int i = 0; i < 48; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (withGrass) grass.SubmitFor(cam);
                cam.Render();
                AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                if (i >= 8) times.Add((float)sw.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[times.Count / 2];
        }
    }
}
