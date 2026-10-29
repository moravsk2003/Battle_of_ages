using NUnit.Framework;
using UnityEngine;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>Lakes (M1): placed from the seed in hollows, cut into the relief with banks above the water, and nobody walks in.</summary>
    [TestFixture]
    public class LakesTests
    {
        [TearDown]
        public void TearDown() => WorldSettings.Set(0, 0f);

        private static float H(float x, float z) => InfiniteWorldManager.GetSampledTerrainHeight(x, z);

        [Test]
        public void ACampaignWorld_HasLakesInHollows_WithBanksAboveTheWater()
        {
            WorldSettings.Set(31337, WorldSettings.DefaultSize);
            int n = Lakes.All.Count;
            float half = WorldSettings.PlayableHalf - Lakes.EdgeClearance;
            int target = Mathf.RoundToInt(4f * half * half / 1e6f / Lakes.KmSquaredPerLake);
            TestContext.WriteLine($"lakes: {n} of {target} wanted");
            Assert.GreaterOrEqual(n, target / 2, "most of the lakes find a hollow");
            for (int i = 0; i < n; i++)
            {
                Lakes.Lake l = Lakes.All[i];
                Assert.Greater(l.Center.magnitude, Lakes.BaseClearance, "none at the base");
                for (int j = 0; j < i; j++) Assert.Greater(Vector2.Distance(l.Center, Lakes.All[j].Center), l.Radius + Lakes.All[j].Radius, "no two in one");
                Assert.Less(H(l.Center.x, l.Center.y), l.Level - 1f, "the bed is under the water");
                Assert.IsTrue(Lakes.InWater(l.Center.x, l.Center.y));
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f, r = l.ShoreRadius(a) * 1.05f;
                    float x = l.Center.x + Mathf.Cos(a) * r, z = l.Center.y + Mathf.Sin(a) * r;
                    Assert.Greater(H(x, z), l.Level, $"lake {i}: the bank rises above the water all round");
                    Assert.IsFalse(Lakes.InWater(x, z), "and is dry land");
                }
            }
            CoarseWorldMap map = CoarseWorldMap.Current;
            int water = 0;
            for (int z = 0; z < map.Cells; z++) for (int x = 0; x < map.Cells; x++) if (map.Water(x, z)) { water++; Assert.AreEqual(0f, map.Fertility(x, z)); }
            Assert.Greater(water, 0, "the coarse map knows the water");
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (!string.IsNullOrEmpty(dir)) { System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "coarse_map_lakes.png"), CoarseWorldMapTests.Picture(map)); }
        }

        [Test]
        public void AStepIntoALake_TurnsAlongItsShore()
        {
            WorldSettings.Set(31337, WorldSettings.DefaultSize);
            Lakes.Lake l = Lakes.All[0];
            float r = l.ShoreRadius(0f) * Lakes.WaterReach;
            Vector3 from = new Vector3(l.Center.x + r + 1.5f, 0f, l.Center.y); // on the east shore
            Vector3 step = Lakes.Constrain(from, new Vector3(-1f, 0f, 0.3f), 0.4f); // west, into the water, a little north
            Vector3 to = from + step;
            Assert.IsFalse(Lakes.InWater(to.x, to.z, 0.3f), "not into the water");
            Assert.Greater(step.z, 0.1f, "but along the shore");
            Vector3 wading = new Vector3(l.Center.x + r * 0.5f, 0f, l.Center.y);
            Vector3 outStep = Lakes.Constrain(wading, Vector3.zero + new Vector3(0f, 0f, 0.01f), 0.4f);
            Assert.Greater(outStep.x, 0f, "one dropped in the water is pushed out");
        }

        [Test]
        public void TheFixedEndlessWorld_HasNoLakes()
        {
            WorldSettings.Set(0, 0f);
            Assert.AreEqual(0, Lakes.All.Count);
            Assert.IsFalse(Lakes.InWater(500f, 500f));
        }
    }
}
