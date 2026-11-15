using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Player;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for the first-person sword swings (not a regular test): the forehand and the backhand frozen
    /// at eight moments each, as the player sees them in SampleScene. CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class ViewmodelCapture
    {
        private static readonly float[] Phases = { 0f, 0.12f, 0.24f, 0.33f, 0.42f, 0.5f, 0.56f, 0.78f };

        [UnityTest]
        public IEnumerator CaptureSwings()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "viewmodel");
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            GrassFieldRenderer grass = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            if (grass != null) grass.enabled = false;

            FpsViewmodelAnimation arms = UnityEngine.Object.FindFirstObjectByType<FpsViewmodelAnimation>();
            Assert.IsNotNull(arms, "SampleScene has the first-person arms");
            Transform eye = Camera.main.transform;
            var capture = new CaptureRenderer(dir, Phases.Length * 2, 60f, drawGrass: false);
            capture.Camera.cullingMask |= 1 << LayerMask.NameToLayer("Viewmodel");
            for (int v = 0; v < 2; v++)
                for (int i = 0; i < Phases.Length; i++)
                {
                    arms.PoseSwing(Phases[i], v);
                    yield return null;
                    capture.Shoot(v * Phases.Length + i, $"{(v == 0 ? "fore" : "back")}_{Phases[i]:0.00}", eye.position, eye.position + eye.forward);
                    yield return null;
                }
            arms.PoseSwing(-1f);
            capture.Finish();
        }
    }
}
