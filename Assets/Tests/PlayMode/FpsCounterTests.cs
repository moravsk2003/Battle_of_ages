using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    public class FpsCounterTests
    {
        [UnityTest]
        public IEnumerator FpsCounter_IsCreatedAutomatically_AndShowsFrameStats()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(1.2f);

            FpsCounter counter = Object.FindFirstObjectByType<FpsCounter>();
            Assert.IsNotNull(counter, "FPS counter must be created in every scene.");
            Assert.AreEqual(1, Object.FindObjectsByType<FpsCounter>(FindObjectsSortMode.None).Length, "Only one FPS counter.");
            string text = counter.GetComponentInChildren<Text>().text;
            StringAssert.Contains("FPS", text);
            StringAssert.Contains("мс", text);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(text, @"FPS \d+"), $"No FPS value in '{text}'.");
        }

        /// <summary>Visual check: renders the overlay through a camera (overlay canvases are not in camera renders).</summary>
        [UnityTest, Explicit("Visual capture tool, run on demand")]
        public IEnumerator CaptureFpsCounter()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(2f);
            FpsCounter counter = Object.FindFirstObjectByType<FpsCounter>();
            Canvas canvas = counter.GetComponent<Canvas>();
            Camera cam = Camera.main;
            var rt = new RenderTexture(1280, 720, 24);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            cam.targetTexture = rt;
            yield return new WaitForSecondsRealtime(1f);
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "fps_counter.png"), tex.EncodeToPNG());
        }
    }
}
