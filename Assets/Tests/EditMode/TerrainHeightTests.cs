using NUnit.Framework;
using UnityEngine;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class TerrainHeightTests
    {
        private static float RingAverage(float radius, int samples = 72)
        {
            float sum = 0f;
            for (int i = 0; i < samples; i++)
            {
                float a = i * Mathf.PI * 2f / samples;
                sum += InfiniteWorldManager.GetSampledTerrainHeight(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
            }
            return sum / samples;
        }

        [Test]
        public void BasePlateau_IsLevelWithSurroundingTerrain()
        {
            float baseHeight = InfiniteWorldManager.GetSampledTerrainHeight(0f, 0f);
            float surroundings = (RingAverage(100f) + RingAverage(130f) + RingAverage(160f)) / 3f;

            Assert.AreEqual(surroundings, baseHeight, 3f, "The spawn base must not sit in a pit (or on a mound) relative to the terrain around it.");
        }

        [Test]
        public void BasePlateau_IsFlatWithinBaseRadius()
        {
            float center = InfiniteWorldManager.GetSampledTerrainHeight(0f, 0f);
            for (int i = 0; i < 36; i++)
            {
                float a = i * Mathf.PI * 2f / 36;
                for (float r = 5f; r < 50f; r += 5f)
                {
                    Assert.AreEqual(center, InfiniteWorldManager.GetSampledTerrainHeight(Mathf.Cos(a) * r, Mathf.Sin(a) * r), 0.001f);
                }
            }
        }

        [Test]
        public void BaseTransition_HasNoCliffs()
        {
            const float maxRisePerMeter = 0.5f; // ~27 degrees
            for (int i = 0; i < 72; i++)
            {
                float a = i * Mathf.PI * 2f / 72;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                for (float r = 40f; r < 95f; r += 1f) // flat base edge + blend ring into natural terrain
                {
                    float h0 = InfiniteWorldManager.GetSampledTerrainHeight(cos * r, sin * r);
                    float h1 = InfiniteWorldManager.GetSampledTerrainHeight(cos * (r + 1f), sin * (r + 1f));
                    Assert.Less(Mathf.Abs(h1 - h0), maxRisePerMeter, $"Too steep at radius {r}m, angle {a * Mathf.Rad2Deg:F0} deg.");
                }
            }
        }
    }
}
