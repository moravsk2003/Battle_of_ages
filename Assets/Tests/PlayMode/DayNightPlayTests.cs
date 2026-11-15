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
            CaptureRenderer cap = string.IsNullOrEmpty(dir) ? null : new CaptureRenderer(dir, 6);
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
            Assert.AreEqual(DayNightCycle.MoonShare, cycle.Brightness, 0.02f, "midnight of the first night: a full moon's light");
            Assert.That(cycle.AmbientShare, Is.InRange(0.25f, 0.5f), "darker, but one still sees (a full moon)");
            Assert.Greater(RenderSettings.sun.transform.forward.y, -1f);
            Assert.Less(RenderSettings.sun.transform.forward.y, 0f, "the moonlight comes from above");
            cap?.Shoot(2, "midnight", eye.transform.position, eye.transform.position + eye.transform.forward * 10f);
            Vector3 Moon() { Vector4 m = RenderSettings.skybox.GetVector("_MoonDir"); return new Vector3(m.x, m.y, m.z); }
            cap?.Shoot(3, "full_moon_sky", eye.transform.position, eye.transform.position + Moon() * 10f);

            // First quarter, an evening (six days on): half a face in the evening sky
            WorldClock.GameHours = 24.0 * 6 + 21.0 - WorldClock.DayStartHour;
            yield return null; yield return null;
            Assert.AreEqual(0.25f, DayNightCycle.Phase, 0.07f, "a first quarter");
            cap?.Shoot(4, "quarter_moon_sky", eye.transform.position, eye.transform.position + Moon() * 10f);

            // New moon, midnight (4 days on): darker than a full-moon night
            WorldClock.GameHours = 24.0 * 5 - WorldClock.DayStartHour;
            yield return null; yield return null;
            Assert.Less(cycle.Brightness, DayNightCycle.MoonShare * 0.2f, "a new moon's night has hardly any direct light");
            Assert.That(cycle.AmbientShare, Is.InRange(0.15f, 0.35f), "and a dimmer ambient, still not black");
            cap?.Shoot(5, "new_moon_midnight", eye.transform.position, eye.transform.position + eye.transform.forward * 10f);
            cap?.Finish();
        }
    }
}
