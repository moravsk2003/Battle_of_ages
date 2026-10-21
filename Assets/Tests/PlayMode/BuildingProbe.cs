using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// What buildings cost (a player report: "the houses load the PC"): puts 15 of each type in front of the player and
    /// measures the whole frame on the GPU (1920x1080, readback-synced) and the CPU frame, against the same view
    /// without them; counts their triangles, renderers and lights. Writes building_report.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Performance review tool, run on demand")]
    public class BuildingProbe
    {
        private Camera cam;
        private RenderTexture rt;

        [UnityTest, Timeout(600000)]
        public IEnumerator BuildingCost()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(12f); // chunks, trees and shaders settle

            cam = Camera.main;
            rt = new RenderTexture(1920, 1080, 24);
            GameObject player = GameObject.Find("Player");
            Vector3 p = player.transform.position;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);

            var report = new StringBuilder();
            report.AppendLine("# Building cost");
            report.AppendLine($"{SystemInfo.graphicsDeviceName}; 15 of a type in a 5x3 grid 25-60 m ahead; medians, ms.");
            report.AppendLine();
            report.AppendLine("| type | tris each (k) | renderers each | lights each (shadowed) | GPU without | GPU with | GPU +15 | CPU without | CPU with | CPU +15 |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            var rows = new List<string>();
            foreach (BuildingType type in Enum.GetValues(typeof(BuildingType)))
            {
                yield return null;
                float gpu0 = 0, cpu0 = 0;
                yield return Measure((g, c) => { gpu0 = g; cpu0 = c; });

                var made = new List<GameObject>();
                for (int i = 0; i < 15; i++)
                {
                    GameObject b = BuildingPlacementSystem.CreateBuilding(type);
                    if (b == null) break;
                    Vector3 at = p + fwd * (25f + (i / 5) * 14f) + side * ((i % 5 - 2) * 12f);
                    at.y = InfiniteWorldManager.GetSampledTerrainHeight(at.x, at.z);
                    b.transform.position = at;
                    b.transform.rotation = Quaternion.LookRotation(-fwd);
                    made.Add(b);
                }
                if (made.Count == 0) continue;
                yield return null;
                yield return null;
                Shot(Path.Combine(dir, $"buildings_{type}.png"));
                if (type == BuildingType.WorkerHouse)
                {
                    float bias = QualitySettings.lodBias;
                    QualitySettings.lodBias = 0.05f; // every house on its far model
                    Shot(Path.Combine(dir, "buildings_WorkerHouse_far.png"));
                    QualitySettings.lodBias = bias;
                }
                float gpu1 = 0, cpu1 = 0;
                yield return Measure((g, c) => { gpu1 = g; cpu1 = c; });

                long tris = 0; int renderers = 0, lights = 0, shadowed = 0;
                foreach (Renderer r in made[0].GetComponentsInChildren<Renderer>())
                {
                    renderers++;
                    Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (m != null) for (int sm = 0; sm < m.subMeshCount; sm++) tris += (long)m.GetIndexCount(sm) / 3;
                }
                foreach (Light l in made[0].GetComponentsInChildren<Light>()) { lights++; if (l.shadows != LightShadows.None) shadowed++; }
                foreach (GameObject b in made) UnityEngine.Object.Destroy(b);
                yield return null;
                float gpu2 = 0, cpu2 = 0;
                yield return Measure((g, c) => { gpu2 = g; cpu2 = c; }); // the base again after (the world still drifts)
                gpu0 = (gpu0 + gpu2) * 0.5f; cpu0 = (cpu0 + cpu2) * 0.5f;
                rows.Add($"| {type} | {tris / 1000f:0.0} | {renderers} | {lights} ({shadowed}) | {gpu0:0.00} | {gpu1:0.00} | {gpu1 - gpu0:+0.00;-0.00} | {cpu0:0.00} | {cpu1:0.00} | {cpu1 - cpu0:+0.00;-0.00} |");
            }
            foreach (string r in rows) report.AppendLine(r);
            File.WriteAllText(Path.Combine(dir, "building_report.md"), report.ToString());
            Debug.Log(report.ToString());
            rt.Release();
        }

        private void Shot(string path)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        /// <summary>GPU: the view rendered into 1920x1080 and read back; CPU: the frame's time without that render.</summary>
        private IEnumerator Measure(Action<float, float> result)
        {
            var gpu = new List<float>(); var cpu = new List<float>();
            float last = Time.realtimeSinceStartup;
            for (int i = 0; i < 40; i++)
            {
                cam.targetTexture = rt;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                cam.Render();
                AsyncGPUReadback.Request(rt, 0, 0, 1, 0, 1, 0, 1).WaitForCompletion();
                float g = (float)sw.Elapsed.TotalMilliseconds;
                cam.targetTexture = null;
                yield return null;
                float now = Time.realtimeSinceStartup;
                if (i >= 8) { gpu.Add(g); cpu.Add((now - last) * 1000f - g); }
                last = now;
            }
            gpu.Sort(); cpu.Sort();
            result(gpu[gpu.Count / 2], cpu[cpu.Count / 2]);
        }
    }
}
