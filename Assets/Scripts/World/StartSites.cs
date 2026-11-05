using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.World
{
    /// <summary>
    /// "Convenient places" and the start sites of a campaign's heroes (docs/GAME_DESIGN.md §5.1, §7.1, M1 step 4), on
    /// the coarse map: level, fertile ground near a lake (not on its shore), with wood and ore about, away from the
    /// wall and from the player's base; the sites keep their distance so every hero has room to grow.
    /// </summary>
    public static class StartSites
    {
        /// <summary>
        /// No other hero's site this near the player's base (it starts at the origin): a tenth of the world, 400 m to 1.5 km
        /// (a fixed 1.5 km kept every kingdom far off, the author said, 2026-11-05).
        /// </summary>
        public static float BaseClearance => ClearanceFor(WorldSettings.Bounded ? WorldSettings.Size : 0f);

        public static float ClearanceFor(float worldSize) => worldSize > 0f ? Mathf.Clamp(worldSize * 0.1f, 400f, 1500f) : 1500f;

        /// <summary>Sites at least this far apart: up to fifty kingdoms in a 4 km world (was 700 m).</summary>
        public const float MinSpacing = 300f;
        /// <summary>A lake this near is good (water), nearer is its shore.</summary>
        public const float WaterNear = 60f, WaterFar = 900f;
        public const float MaxSlope = 0.12f;

        /// <summary>How good a place the cell is to settle, 0..~1.4; below 0 not at all.</summary>
        public static float Score(CoarseWorldMap map, int x, int z, out float waterDistance)
        {
            waterDistance = float.MaxValue;
            if (!map.Habitable(x, z) || map.Slope(x, z) > MaxSlope) return -1f;
            Vector3 c = map.CenterOf(x, z, false);
            if (!WorldSettings.Inside(c, 200f)) return -1f;
            waterDistance = ShoreDistance(c);
            if (waterDistance < WaterNear) return -1f; // the shore itself floods the fields
            float s = 0.45f * map.Fertility(x, z) + 0.25f * (1f - map.Slope(x, z) / MaxSlope) + 0.15f * map.Forest(x, z) + 0.15f * map.Ore(x, z);
            if (waterDistance < WaterFar) s += 0.35f * (1f - (waterDistance - WaterNear) / (WaterFar - WaterNear));
            return s;
        }

        /// <summary>Distance from a point to the nearest lake's shore (infinity without lakes).</summary>
        public static float ShoreDistance(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (Lakes.Lake l in Lakes.All)
            {
                float dx = p.x - l.Center.x, dz = p.z - l.Center.y;
                float d = Mathf.Sqrt(dx * dx + dz * dz) - l.ShoreRadius(Mathf.Atan2(dz, dx)) * Lakes.WaterReach;
                best = Mathf.Min(best, d);
            }
            return best;
        }

        /// <summary>The spacing for this many sites in this world (the base counts as one).</summary>
        public static float Spacing(int count)
        {
            float half = WorldSettings.Bounded ? WorldSettings.PlayableHalf : CoarseWorldMap.UnboundedExtent * 0.5f;
            float area = 4f * half * half;
            return Mathf.Clamp(0.75f * Mathf.Sqrt(area / (count + 1)), MinSpacing, 3500f);
        }

        /// <summary>
        /// The best places for this many heroes, the same for a seed: the highest scores (with a little seeded chance)
        /// that keep the spacing from each other and from the base; the spacing shrinks if the world cannot fit them.
        /// </summary>
        public static List<Vector3> Choose(int count)
        {
            CoarseWorldMap map = CoarseWorldMap.Current;
            var rng = new System.Random(WorldSettings.Seed ^ 0x5173);
            var candidates = new List<(float score, Vector3 at)>();
            for (int z = 0; z < map.Cells; z++)
                for (int x = 0; x < map.Cells; x++)
                {
                    float s = Score(map, x, z, out _);
                    if (s > 0f) candidates.Add((s + (float)rng.NextDouble() * 0.15f, map.CenterOf(x, z)));
                }
            candidates.Sort((a, b) => b.score.CompareTo(a.score));

            float spacing = Spacing(count), clearance = BaseClearance;
            var picked = new List<Vector3>();
            for (int round = 0; round < 6 && picked.Count < count; round++, spacing *= 0.8f)
                foreach (var c in candidates)
                {
                    if (picked.Count >= count) break;
                    if (new Vector2(c.at.x, c.at.z).magnitude < Mathf.Max(clearance, spacing)) continue;
                    bool free = true;
                    foreach (Vector3 p in picked)
                        if ((new Vector2(p.x - c.at.x, p.z - c.at.z)).sqrMagnitude < spacing * spacing) { free = false; break; }
                    if (free) picked.Add(c.at);
                }
            return picked;
        }
    }
}
