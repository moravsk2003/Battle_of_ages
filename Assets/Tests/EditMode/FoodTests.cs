using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Resources;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>Food (M5, §10.3): one per person per game day; on an empty larder births wait; only in a campaign.</summary>
    [TestFixture]
    public class FoodTests
    {
        private GameObject go;
        private ResourceManager rm;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("Resources");
            rm = go.AddComponent<ResourceManager>();
        }

        [TearDown]
        public void TearDown()
        {
            WorldSettings.Set(0, 0f);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void APersonEats_OneADay()
        {
            Assert.AreEqual(GameBalance.FoodPerPersonPerDay, ResourceManager.Eaten(1, 24f), 1e-4f);
            Assert.AreEqual(10f * GameBalance.FoodPerPersonPerDay * 0.5f, ResourceManager.Eaten(10, 12f), 1e-4f, "ten people, half a day");
            rm.SetFood(10f);
            rm.Eat(10, 12f);
            Assert.AreEqual(10f - 5f * GameBalance.FoodPerPersonPerDay, rm.Food, 1e-3f);
            rm.Eat(1000, 240f);
            Assert.AreEqual(0f, rm.Food, 1e-4f, "not below nothing");
        }

        [Test]
        public void TheEmptyLarder_StopsBirths_InACampaignOnly()
        {
            rm.SetFood(0f);
            WorldSettings.Set(0, 0f);
            Assert.IsFalse(rm.Hungry, "the fixed world has no food rules");
            WorldSettings.Set(5, WorldSettings.DefaultSize);
            Assert.IsTrue(ResourceManager.FoodRules);
            Assert.IsTrue(rm.Hungry, "a campaign's empty larder");
            rm.AddFood(1f);
            Assert.IsFalse(rm.Hungry);
        }

        [Test]
        public void ANewCampaign_StartsWithFoodForDays()
        {
            rm.ResetToInitial();
            Assert.AreEqual(GameBalance.FoodStart, rm.Food, 1e-3f);
            float days = rm.Food / (5 * GameBalance.FoodPerPersonPerDay);
            Assert.Greater(days, 5f, "five people have days to get fields going");
        }
    }
}
