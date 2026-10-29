using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Day and night in the game scene (M1): full light at noon, a dim moon and darker ambient at midnight, still enough to see.</summary>
    public class DayNightPlayTests
    {
        [TearDown]
        public void TearDown() => WorldClock.GameHours = 0.0;

        [UnityTest, Timeout(120000)]
        public IEnumerator Noon_IsBright_Midnight_IsDarkButNotBlack()
        {
            yield return SceneManager.LoadSceneAsync(SaveSystem.GameScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            DayNightCycle cycle = Object.FindFirstObjectByType<DayNightCycle>();
            Assert.IsNotNull(cycle);
            Assert.AreEqual(WorldClock.DayStartHour, WorldClock.HourOfDay, 0.1f, "the game starts in the morning");

            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR");
            CaptureRenderer cap = string.IsNullOrEmpty(dir) ? null : new CaptureRenderer(dir, 3);
            Camera eye = Camera.main;

            WorldClock.GameHours = 12.0 - WorldClock.DayStartHour;
            yield return null; yield return null;
            Assert.AreEqual(1f, cycle.Brightness, 0.02f, "noon: the scene's full light");
            Assert.AreEqual(1f, cycle.AmbientShare, 0.05f);
            cap?.Shoot(0, "noon", eye.transform.position, eye.transform.position + eye.transform.forward * 10f);

            WorldClock.GameHours = 18.5 - WorldClock.DayStartHour;
            yield return null; yield return null;
            cap?.Shoot(1, "dusk", eye.transform.position, eye.transform.position + eye.transform.forward * 10f);

            WorldClock.GameHours = 24.0 - WorldClock.DayStartHour;
            yield return null; yield return null;
            Assert.AreEqual(DayNightCycle.MoonShare, cycle.Brightness, 0.02f, "midnight: a dim moon");
            Assert.That(cycle.AmbientShare, Is.InRange(0.3f, 0.6f), "darker, but one still sees");
            Assert.Greater(RenderSettings.sun.transform.forward.y, -1f);
            Assert.Less(RenderSettings.sun.transform.forward.y, 0f, "the moonlight comes from above");
            cap?.Shoot(2, "midnight", eye.transform.position, eye.transform.position + eye.transform.forward * 10f);
            cap?.Finish();
        }
    }
}
