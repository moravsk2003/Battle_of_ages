using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.World
{
    /// <summary>
    /// The lakes of a campaign's world (docs/GAME_DESIGN.md §18.2, M1 step 3; rivers come later). Placed from the seed
    /// in lowlands and on gentle ground, each with an uneven shore, and cut into the relief itself
    /// (InfiniteWorldManager.GetSampledTerrainHeight), so the chunks, the far terrain and the coarse map all agree. Water
    /// stops walkers (they slide along the shore) and builders. The endless seed-0 world has none.
    /// </summary>
    public static class Lakes
    {
        public struct Lake
        {
            public Vector2 Center;
            public float Radius, Level, Depth;
            public float P1, P2, P3; // the shore's wobble

            /// <summary>The shore's distance from the centre in a direction (the lake is not round).</summary>
            public float ShoreRadius(float angle) =>
                Radius * (0.84f + 0.1f * Mathf.Sin(2f * angle + P1) + 0.06f * Mathf.Sin(3f * angle + P2) + 0.04f * Mathf.Sin(5f * angle + P3));

            /// <summary>Distance from the centre in shore radii: under 1 inside.</summary>
            public float Reach(float x, float z)
            {
                float dx = x - Center.x, dz = z - Center.y;
                return Mathf.Sqrt(dx * dx + dz * dz) / ShoreRadius(Mathf.Atan2(dz, dx));
            }
        }

        /// <summary>One lake to so many square kilometres of a world; their sizes; how far from the base and the edge.</summary>
        public const float KmSquaredPerLake = 6f, MinRadius = 60f, MaxRadius = 240f, BaseClearance = 450f, EdgeClearance = 500f;
        /// <summary>Under this many shore radii from the centre one is in the water (the bank above starts near 0.95).</summary>
        public const float WaterReach = 0.92f;
        private const float BankOuter = 1.3f, BankInner = 0.85f, GridCell = 512f;

        private static readonly List<Lake> lakes = new List<Lake>();
        private static List<int>[] grid;
        private static int gridCells;
        private static float gridOrigin;
        private static bool building;

        public static IReadOnlyList<Lake> All => lakes;

        /// <summary>Places this world's lakes (WorldSettings.Set calls it): none in an endless world.</summary>
        public static void Rebuild()
        {
            lakes.Clear();
            grid = null;
            if (!WorldSettings.Bounded) return;
            building = true;
            try { Place(); }
            finally { building = false; }
            Index();
        }

        private static void Place()
        {
            var rng = new System.Random(WorldSettings.Seed ^ 0x51A7E);
            float half = WorldSettings.PlayableHalf - EdgeClearance;
            if (half <= MaxRadius) return;
            float area = (2f * half) * (2f * half) / 1e6f;
            int want = Mathf.Max(1, Mathf.RoundToInt(area / KmSquaredPerLake));
            for (int attempt = 0; attempt < want * 40 && lakes.Count < want; attempt++)
            {
                float u = (float)rng.NextDouble();
                var lake = new Lake
                {
                    Center = new Vector2(((float)rng.NextDouble() * 2f - 1f) * half, ((float)rng.NextDouble() * 2f - 1f) * half),
                    Radius = MinRadius + (MaxRadius - MinRadius) * u * u, // mostly small ones
                    P1 = (float)rng.NextDouble() * 6.28f, P2 = (float)rng.NextDouble() * 6.28f, P3 = (float)rng.NextDouble() * 6.28f
                };
                if (lake.Center.magnitude < BaseClearance + lake.Radius) continue;
                if (TooClose(lake)) continue;
                if (!Fits(ref lake)) continue;
                lakes.Add(lake);
            }
        }

        private static bool TooClose(Lake l)
        {
            foreach (Lake o in lakes)
                if (Vector2.Distance(o.Center, l.Center) < (o.Radius + l.Radius) * 1.5f + 150f) return true;
            return false;
        }

        /// <summary>
        /// A lake sits in a hollow on gentle, low ground (not in the snowy mountains): its level is the lowest ground
        /// round its shore, a little under it, so the bank rises above the water all round.
        /// </summary>
        private static bool Fits(ref Lake l)
        {
            float centre = Natural(l.Center.x, l.Center.y);
            if (centre > ProceduralBiomeChunk.SnowLineHeight - 12f) return false;
            float rimLow = float.MaxValue, rimHigh = float.MinValue, ringSum = 0f;
            const int samples = 16;
            for (int i = 0; i < samples; i++)
            {
                float a = i * Mathf.PI * 2f / samples;
                float r = l.ShoreRadius(a) * 1.05f;
                float h = Natural(l.Center.x + Mathf.Cos(a) * r, l.Center.y + Mathf.Sin(a) * r);
                rimLow = Mathf.Min(rimLow, h); rimHigh = Mathf.Max(rimHigh, h);
                ringSum += Natural(l.Center.x + Mathf.Cos(a) * l.Radius * 2f, l.Center.y + Mathf.Sin(a) * l.Radius * 2f);
            }
            if (rimHigh - rimLow > 4f + l.Radius * 0.05f) return false; // a slope, not a hollow
            if (centre > ringSum / samples + 1f) return false;          // on a rise
            l.Level = rimLow - 0.4f;
            l.Depth = Mathf.Clamp(l.Radius * 0.04f, 2.5f, 8f);
            return true;
        }

        private static float Natural(float x, float z) => InfiniteWorldManager.GetSampledTerrainHeight(x, z); // (no lakes while placing)

        private static void Index()
        {
            float half = WorldSettings.Size * 0.5f;
            gridCells = Mathf.Max(1, Mathf.CeilToInt(WorldSettings.Size / GridCell));
            gridOrigin = -half;
            grid = new List<int>[gridCells * gridCells];
            for (int i = 0; i < lakes.Count; i++)
            {
                Lake l = lakes[i];
                float reach = l.Radius * 1.25f * BankOuter;
                int x0 = Cell(l.Center.x - reach), x1 = Cell(l.Center.x + reach), z0 = Cell(l.Center.y - reach), z1 = Cell(l.Center.y + reach);
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int k = z * gridCells + x;
                        (grid[k] ?? (grid[k] = new List<int>(2))).Add(i);
                    }
            }
        }

        private static int Cell(float v) => Mathf.Clamp(Mathf.FloorToInt((v - gridOrigin) / GridCell), 0, gridCells - 1);

        private static List<int> Near(float x, float z) => grid == null ? null : grid[Cell(z) * gridCells + Cell(x)];

        /// <summary>The relief with the lakes cut in: the bed under the water, the bank easing up to the land round it.</summary>
        public static float Carve(float x, float z, float h)
        {
            if (building) return h;
            List<int> near = Near(x, z);
            if (near == null) return h;
            for (int i = 0; i < near.Count; i++)
            {
                Lake l = lakes[near[i]];
                float d = l.Reach(x, z);
                if (d >= BankOuter) continue;
                float bed = l.Level - l.Depth * (1f - Mathf.Min(d, 1f) * Mathf.Min(d, 1f));
                float t = Mathf.Clamp01((BankOuter - d) / (BankOuter - BankInner));
                float s = t * t * (3f - 2f * t);
                h = Mathf.Lerp(h, Mathf.Min(h, bed), s);
            }
            return h;
        }

        /// <summary>Is the point in a lake (with a margin out from the water, e.g. a walker's radius)?</summary>
        public static bool InWater(float x, float z, float margin = 0f) => TryLake(x, z, margin, out _);

        public static bool TryLake(float x, float z, float margin, out Lake lake)
        {
            lake = default;
            List<int> near = Near(x, z);
            if (near == null) return false;
            for (int i = 0; i < near.Count; i++)
            {
                Lake l = lakes[near[i]];
                float dx = x - l.Center.x, dz = z - l.Center.y;
                float shore = l.ShoreRadius(Mathf.Atan2(dz, dx)) * WaterReach + margin;
                if (dx * dx + dz * dz < shore * shore) { lake = l; return true; }
            }
            return false;
        }

        /// <summary>
        /// A step that does not enter the water: what heads into a lake is turned along its shore; one already in the
        /// water (dropped there) is pushed out.
        /// </summary>
        public static Vector3 Constrain(Vector3 from, Vector3 step, float radius)
        {
            if (grid == null) return step;
            Vector3 to = from + step;
            if (!TryLake(to.x, to.z, radius, out Lake l)) return step;
            Vector3 outward = new Vector3(to.x - l.Center.x, 0f, to.z - l.Center.y);
            if (outward.sqrMagnitude < 1e-6f) outward = Vector3.right;
            outward.Normalize();
            float inward = -Vector3.Dot(step, outward);
            if (inward > 0f)
            {
                // Along the shore at its full pace, round the side nearer to where it was heading
                var tangent = new Vector3(-outward.z, 0f, outward.x);
                float side = Vector3.Dot(step, tangent);
                step = tangent * (side >= 0f ? 1f : -1f) * new Vector3(step.x, 0f, step.z).magnitude;
            }
            if (TryLake(from.x, from.z, radius, out _)) step += outward * Mathf.Max(step.magnitude, 0.05f); // out of it
            step.y = 0f;
            return step;
        }
    }
}
