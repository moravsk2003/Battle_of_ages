using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Look at the mountains from inside them (a player report: "nothing shows in the mountains"): finds the nearest high
    /// ground above the snow line, puts the player there, lets the chunks stream in and saves four views to CAPTURE_DIR.
    /// </summary>
    [Explicit("Visual review tool, run on demand")]
    public class MountainCapture
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator CaptureMountains()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(3f);

            GameObject player = GameObject.Find("Player");
            Vector3 start = player.transform.position;
            Vector3 best = start; float bestH = float.MinValue;
            float want = float.Parse(Environment.GetEnvironmentVariable("MOUNT_H") ?? "100", System.Globalization.CultureInfo.InvariantCulture);
            for (float r = 100f; r <= 6000f && bestH < want; r += 50f)
                for (int a = 0; a < 72; a++)
                {
                    float ang = a * 5f * Mathf.Deg2Rad;
                    float x = start.x + Mathf.Cos(ang) * r, z = start.z + Mathf.Sin(ang) * r;
                    float h = InfiniteWorldManager.GetSampledTerrainHeight(x, z);
                    if (h > bestH) { bestH = h; best = new Vector3(x, h, z); }
                }
            Debug.Log($"[MountainCapture] high point {best} ({Vector3.Distance(start, best):0} m away), height {bestH:0.0}");

            // Stand on its foot first (inside the range), then on the top
            Vector3 foot = Vector3.Lerp(start, best, 0.85f);
            foot.y = InfiniteWorldManager.GetSampledTerrainHeight(foot.x, foot.z);
            var cc = player.GetComponent<CharacterController>();
            Camera cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24);
            int shot = 0;
            foreach (Vector3 spot in new[] { foot, best })
            {
                if (cc != null) cc.enabled = false;
                player.transform.position = spot + Vector3.up * 1.2f;
                if (cc != null) cc.enabled = true;
                yield return new WaitForSecondsRealtime(8f); // chunks around stream in
                foreach (float yaw in new[] { 0f, 180f })
                {
                    cam.transform.rotation = Quaternion.Euler(8f, yaw + Quaternion.LookRotation(best - start).eulerAngles.y, 0f);
                    cam.targetTexture = rt;
                    UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>()?.SubmitFor(cam);
                    cam.Render();
                    cam.targetTexture = null;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(dir, $"mountain_{shot++}.png"), tex.EncodeToPNG());
                    UnityEngine.Object.Destroy(tex);
                }
            }
            Assert.Greater(bestH, ProceduralBiomeChunk.SnowLineHeight - 20f, "found high ground");
        }
    }
}
