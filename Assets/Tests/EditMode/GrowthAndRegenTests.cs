using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;

namespace BattleOfAges.Tests
{
    /// <summary>World clock pace, population growth and natural regeneration (docs/GAME_DESIGN.md §4, §10.3, §11.2).</summary>
    [TestFixture]
    public class GrowthAndRegenTests
    {
        [Test]
        public void GameHour_IsOneRealMinute()
        {
            Assert.AreEqual(1f, WorldClock.HoursOf(60f), 1e-5f);
            Assert.AreEqual(24f, WorldClock.HoursOf(24f * 60f), 1e-4f, "A game day is 24 real minutes.");
        }

        [Test]
        public void Growth_IsHalfAPersonPerHourPlusPerCapita()
        {
            Assert.AreEqual(0.5f, TownHall.GrowthPerHour(0), 1e-5f, "+1 person per 2 game hours.");
            Assert.AreEqual(0.5f + 1.5f, TownHall.GrowthPerHour(200), 1e-5f, "+1 per 20 game minutes per 200 people.");
        }

        [Test]
        public void Births_AccumulateAndStopAtTheCap()
        {
            float carry = 0f;
            Assert.AreEqual(0, TownHall.Births(ref carry, 1f, 10, 15), "Half a person after one hour.");
            Assert.AreEqual(1, TownHall.Births(ref carry, 1.2f, 10, 15), "The second hour completes the first birth.");

            carry = 0f;
            Assert.AreEqual(2, TownHall.Births(ref carry, 100f, 13, 15), "Never more than the room under the cap.");
            Assert.AreEqual(0, TownHall.Births(ref carry, 100f, 15, 15));
            Assert.LessOrEqual(carry, 1f, "Growth does not pile up while the settlement is full.");
        }

        [Test]
        public void FillingTheLevelFiveCap_TakesAboutAnHourAndForty()
        {
            float carry = 0f, hours = 0f;
            int population = 6;
            while (population < 85 && hours < 1000f)
            {
                population += TownHall.Births(ref carry, 0.1f, population, 85);
                hours += 0.1f;
            }
            Assert.AreEqual(98f, hours, 4f, "6 -> 85 people in ~98 game hours (= real minutes).");
        }

        [Test]
        public void Regeneration_IsOnePercentOfCurrentHpPerHour_PlusExtras()
        {
            var go = new GameObject("Wounded", typeof(HealthComponent));
            try
            {
                HealthComponent hp = go.GetComponent<HealthComponent>();
                hp.InitializeHealth(1000);
                hp.SetCurrentHealth(500);
                hp.Regenerate(1f);
                Assert.AreEqual(505, hp.CurrentHealth, "1% of the current 500 HP per game hour.");

                hp.SetCurrentHealth(10);
                for (int i = 0; i < 11; i++) hp.Regenerate(1f);
                Assert.AreEqual(11, hp.CurrentHealth, "Badly wounded heal slowly; fractions add up.");

                hp.SetCurrentHealth(500);
                hp.RegenMaxPerHour = 0.03f;
                hp.RegenCurrentPerHour = 0.07f;
                hp.Regenerate(1f);
                Assert.AreEqual(500 + 40 + 30, hp.CurrentHealth, "Tank: +3% of max and +7% of current on top of 1%.");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
