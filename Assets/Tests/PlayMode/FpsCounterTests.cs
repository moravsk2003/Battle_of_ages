using System.Collections;
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
            Assert.IsFalse(counter.Shown, "Hidden until F3.");
            counter.Shown = true;
            yield return null;
            yield return null;
            string text = counter.GetComponentInChildren<Text>().text;
            StringAssert.Contains("FPS", text);
            StringAssert.Contains("мс", text);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(text, @"FPS \d+"), $"No FPS value in '{text}'.");
        }
    }
}
