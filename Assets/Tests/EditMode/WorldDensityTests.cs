using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>
    /// Content density (docs/GAME_DESIGN.md §5.1, M1): on the way from the player's base to every other hero there is
    /// something on the road — raider camps by the chunks' own rule (on level dry ground).
    /// </summary>
    [TestFixture]
    public class WorldDensityTests
    {
        private const float Chunk = 64f;

        [TearDown]
        public void TearDown() => WorldSettings.Set(0, 0f);

        /// <summary>Does this chunk get a raider camp (ProceduralBiomeChunk.ScatterBiomeResources' rule)?</summary>
        private static bool Camp(Vector2Int c)
        {
            if (!ProceduralBiomeChunk.CampChunk(c, Chunk)) return false;
            var centre = new Vector3(c.x * Chunk, 0f, c.y * Chunk);
            bool level = BuildSite.FindLevel(centre, 14f, GameBalance.CampSiteSearchRadius, GameBalance.CampSiteMaxRise,
                                             p => InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z), out Vector3 site);
            return level && WorldSettings.Inside(site, 30f) && !Lakes.InWater(site.x, site.z, 25f);
        }

        [Test]
        public void TheWayToEveryHero_PassesCamps()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            List<Vector3> sites = StartSites.Choose(GameBalance.CampaignHeroes - 1);
            float totalKm = 0f;
            int totalCamps = 0, leastPerKmRoute = int.MaxValue;
            foreach (Vector3 s in sites)
            {
                var seen = new HashSet<Vector2Int>();
                int camps = 0;
                float length = new Vector2(s.x, s.z).magnitude;
                for (float d = 0f; d <= length; d += Chunk * 0.5f)
                {
                    Vector3 p = s * (d / length);
                    var c = new Vector2Int(Mathf.FloorToInt((p.x + Chunk * 0.5f) / Chunk), Mathf.FloorToInt((p.z + Chunk * 0.5f) / Chunk));
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dz = -1; dz <= 1; dz++) // within a chunk of the road
                        {
                            var n = new Vector2Int(c.x + dx, c.y + dz);
                            if (seen.Add(n) && Camp(n)) camps++;
                        }
                }
                totalKm += length / 1000f;
                totalCamps += camps;
                leastPerKmRoute = Mathf.Min(leastPerKmRoute, Mathf.FloorToInt(camps / (length / 1000f)));
                Assert.Greater(camps, 0, $"a camp on the way to the site at {s}");
            }
            TestContext.WriteLine($"{sites.Count} routes, {totalKm:0.0} km: {totalCamps} raider camps within a chunk of the road, {totalCamps / totalKm:0.0} per km (the emptiest route {leastPerKmRoute} per km)");
            Assert.Greater(totalCamps / totalKm, 1f, "something on every kilometre");
        }
    }
}
