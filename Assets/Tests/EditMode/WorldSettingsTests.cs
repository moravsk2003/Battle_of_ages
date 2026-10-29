using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>The campaign's world (M1): a seed makes a world of its own, a size walls it in; seed 0 is the old fixed world.</summary>
    [TestFixture]
    public class WorldSettingsTests
    {
        [TearDown]
        public void TearDown() => WorldSettings.Set(0, 0f);

        private static float H(float x, float z) => InfiniteWorldManager.GetSampledTerrainHeight(x, z);

        [Test]
        public void ASeed_MakesItsOwnRelief_AndTheSameSeedTheSame()
        {
            var points = new List<Vector2>();
            for (int i = 0; i < 24; i++) points.Add(new Vector2(150f + i * 97f, -300f + i * 61f));
            WorldSettings.Set(0, 0f);
            var fixedWorld = points.ConvertAll(p => H(p.x, p.y));
            Assert.AreEqual(Vector2.zero, WorldSettings.Shift, "seed 0 is the world of before");

            WorldSettings.Set(12345, 0f);
            var a = points.ConvertAll(p => H(p.x, p.y));
            int differ = 0;
            for (int i = 0; i < points.Count; i++) if (Mathf.Abs(a[i] - fixedWorld[i]) > 1f) differ++;
            Assert.Greater(differ, points.Count / 2, "another seed, another relief");
            WorldSettings.Set(12345, 0f);
            for (int i = 0; i < points.Count; i++) Assert.AreEqual(a[i], H(points[i].x, points[i].y), 1e-4f, "the same seed, the same world");
            Assert.AreNotEqual(WorldSettings.ChunkSeed(new Vector2Int(3, 4)), SeedOf(0, 3, 4), "the chunks' content too");
        }

        private static int SeedOf(int seed, int x, int y) { WorldSettings.Set(seed, 0f); return WorldSettings.ChunkSeed(new Vector2Int(x, y)); }

        [Test]
        public void ABoundedWorld_HasAMountainWall_AndNothingGoesPastIt()
        {
            WorldSettings.Set(7, 4000f);
            float half = WorldSettings.PlayableHalf;
            Assert.AreEqual(2000f - WorldSettings.EdgeBand, half, 1e-3f);
            Assert.AreEqual(0f, WorldSettings.EdgeHeight(half - 1f, 0f), 1e-4f, "nothing inside");
            Assert.Greater(WorldSettings.EdgeHeight(2000f, 300f), WorldSettings.EdgeRise * 0.7f, "a wall at the edge");
            Assert.Greater(H(1990f, 0f) - H(half - 50f, 0f), 80f, "the ground rises into it");
            Vector3 p = WorldSettings.ClampInside(new Vector3(5000f, 3f, -5000f));
            Assert.AreEqual(half - 2f, p.x, 1e-3f);
            Assert.AreEqual(-(half - 2f), p.z, 1e-3f);
            Assert.IsFalse(WorldSettings.Inside(new Vector3(half + 5f, 0f, 0f)));
            WorldSettings.Set(7, 0f);
            Assert.IsTrue(WorldSettings.Inside(new Vector3(1e6f, 0f, 0f)), "an endless world has no edge");
        }

        [Test]
        public void TheScenesOwnObjects_LandOnTheNewGround()
        {
            WorldSettings.Set(0, 0f);
            var tree = new GameObject("BakedTree").transform;
            tree.position = new Vector3(120f, H(120f, -40f) + 0.3f, -40f); // baked on the fixed world, 0.3 m above it
            WorldSettings.Set(999, WorldSettings.DefaultSize);
            WorldSettings.Reground(new List<Transform> { tree });
            Assert.AreEqual(H(120f, -40f) + 0.3f, tree.position.y, 1e-3f, "on this world's ground, as high above it");
            Object.DestroyImmediate(tree.gameObject);
        }
    }
}
