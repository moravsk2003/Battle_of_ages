using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>Day and night (M1): the sun's path over the hours and the clock's day starting in the morning.</summary>
    [TestFixture]
    public class DayNightTests
    {
        [TearDown]
        public void TearDown() => WorldClock.GameHours = 0.0;

        [Test]
        public void TheSun_RisesAtSix_StandsHighAtNoon_AndIsDownAtMidnight()
        {
            DayNightCycle.SunAt(12f, out _, out float noon, out float noonDay);
            Assert.AreEqual(Mathf.Sin(DayNightCycle.NoonElevation * Mathf.Deg2Rad), noon, 1e-3f);
            Assert.AreEqual(1f, noonDay, 1e-4f, "full day at noon");
            DayNightCycle.SunAt(0f, out _, out float midnight, out float night);
            Assert.Less(midnight, -0.5f);
            Assert.AreEqual(0f, night, 1e-4f, "night at midnight");
            DayNightCycle.SunAt(6f, out _, out float dawn, out float twilight);
            Assert.AreEqual(0f, dawn, 1e-3f, "on the horizon at six");
            Assert.That(twilight, Is.InRange(0.3f, 0.8f), "twilight");
            DayNightCycle.SunAt(9f, out _, out float morning, out _);
            DayNightCycle.SunAt(15f, out _, out float afternoon, out _);
            Assert.AreEqual(morning, afternoon, 1e-3f, "as high at nine as at three");
        }

        [Test]
        public void ACampaign_StartsOnDayOne_InTheMorning()
        {
            WorldClock.GameHours = 0.0;
            Assert.AreEqual(1, WorldClock.Day);
            Assert.AreEqual(WorldClock.DayStartHour, WorldClock.HourOfDay, 1e-4f);
            WorldClock.GameHours = 24.0 - WorldClock.DayStartHour; // midnight
            Assert.AreEqual(2, WorldClock.Day);
            Assert.AreEqual(0f, WorldClock.HourOfDay, 1e-4f);
        }
    }
}
