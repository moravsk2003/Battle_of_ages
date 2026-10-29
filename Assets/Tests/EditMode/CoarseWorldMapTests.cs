using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>The coarse map of the whole world (M1): it covers the world, agrees with the ground, is the same for a seed, and is quick to make.</summary>
    [TestFixture]
    public class CoarseWorldMapTests
    {
        [TearDown]
        public void TearDown() => WorldSettings.Set(0, 0f);

        [Test]
        public void TheMap_CoversTheWorld_AndAgreesWithTheGround()
        {
            WorldSettings.Set(4242, 4000f);
            CoarseWorldMap map = CoarseWorldMap.Current;
            Assert.AreEqual(Mathf.CeilToInt(4000f / CoarseWorldMap.CellSize), map.Cells);
            Assert.IsTrue(map.TryCell(new Vector3(-1990f, 0f, 1990f), out int ex, out int ez), "the corners are on it");
            Assert.IsTrue(map.Wall(ex, ez), "the corner is the wall");
            Assert.IsTrue(map.TryCell(Vector3.zero, out int cx, out int cz));
            Assert.IsFalse(map.Wall(cx, cz));
            Vector3 c = map.CenterOf(cx, cz);
            Assert.AreEqual(InfiniteWorldManager.GetSampledTerrainHeight(c.x, c.z), map.Height(cx, cz), 1e-3f);

            var biomes = new HashSet<BiomeType>();
            for (int z = 0; z < map.Cells; z++)
                for (int x = 0; x < map.Cells; x++)
                {
                    biomes.Add(map.Biome(x, z));
                    foreach (float v in new[] { map.Fertility(x, z), map.Forest(x, z), map.Ore(x, z) }) Assert.That(v, Is.InRange(0f, 1f));
                    if (map.Wall(x, z)) Assert.AreEqual(0f, map.Fertility(x, z), "nothing grows on the wall");
                }
            Assert.GreaterOrEqual(biomes.Count, 2, "more than one biome in 4 km");
        }

        [Test]
        public void TheSameSeed_TheSameMap_AnotherSeed_Another()
        {
            WorldSettings.Set(77, 3000f);
            CoarseWorldMap a = CoarseWorldMap.Current;
            float h = a.Height(10, 12), f = a.Fertility(20, 5);
            WorldSettings.Set(78, 3000f);
            CoarseWorldMap b = CoarseWorldMap.Current;
            Assert.AreNotSame(a, b, "a changed world is mapped again");
            WorldSettings.Set(77, 3000f);
            Assert.AreEqual(h, CoarseWorldMap.Current.Height(10, 12), 1e-4f);
            Assert.AreEqual(f, CoarseWorldMap.Current.Fertility(20, 5), 1e-4f);
        }

        [Test]
        public void AFullSizeWorld_IsMappedQuickly()
        {
            WorldSettings.Set(9001, WorldSettings.DefaultSize);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            CoarseWorldMap map = CoarseWorldMap.Current;
            sw.Stop();
            int habitable = 0;
            for (int z = 0; z < map.Cells; z++) for (int x = 0; x < map.Cells; x++) if (map.Habitable(x, z)) habitable++;
            TestContext.WriteLine($"16 km: {map.Cells}x{map.Cells} cells in {sw.ElapsedMilliseconds} ms, habitable {habitable * 100 / (map.Cells * map.Cells)}%");
            Assert.Less(sw.ElapsedMilliseconds, 2000, "a new campaign does not wait long for its map");
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR"); // a picture of the whole world
            if (!string.IsNullOrEmpty(dir)) { System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "coarse_map.png"), Picture(map)); }
            Assert.Greater(habitable, map.Cells * map.Cells / 2, "most of the world can be lived in");
        }

        /// <summary>Biome colours shaded by height, water blue, the wall grey (north up).</summary>
        public static byte[] Picture(CoarseWorldMap map)
        {
            var tex = new Texture2D(map.Cells, map.Cells, TextureFormat.RGB24, false);
            for (int z = 0; z < map.Cells; z++)
                for (int x = 0; x < map.Cells; x++)
                {
                    Color c = map.Wall(x, z) ? new Color(0.45f, 0.45f, 0.45f)
                        : map.Water(x, z) ? new Color(0.2f, 0.4f, 0.75f)
                        : map.Biome(x, z) == BiomeType.SnowyPeaks ? new Color(0.92f, 0.92f, 0.95f)
                        : map.Biome(x, z) == BiomeType.Desert ? new Color(0.82f, 0.72f, 0.45f)
                        : map.Biome(x, z) == BiomeType.PineForest ? new Color(0.16f, 0.4f, 0.18f)
                        : new Color(0.45f, 0.65f, 0.3f);
                    float shade = 0.75f + 0.25f * Mathf.Clamp01(map.Height(x, z) / 60f) - 0.25f * Mathf.Clamp01(map.Slope(x, z) / 0.5f);
                    tex.SetPixel(x, z, c * shade);
                }
            tex.Apply();
            byte[] png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }
    }
}
