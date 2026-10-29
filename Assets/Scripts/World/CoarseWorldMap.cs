using UnityEngine;

namespace BattleOfAges.World
{
    /// <summary>
    /// The whole world on a coarse grid (docs/GAME_DESIGN.md §5.1, M1): for every 64 m cell its height and steepness,
    /// biome, fertility, forest, ore richness, water, and whether it lies on the wall round the world. Made from the
    /// same functions as the chunks, so it agrees with what one walks on; rebuilt from the seed whenever the world
    /// changes (it is not saved). The far kingdoms, the start sites and later the strategic map stand on it.
    /// </summary>
    public sealed class CoarseWorldMap
    {
        public const float CellSize = 64f;
        /// <summary>An endless world (seed-0 saves, tests) is mapped this far round the origin.</summary>
        public const float UnboundedExtent = 4096f;
        private const float SlopeProbe = 16f;

        public readonly int Seed;
        public readonly float Size;
        /// <summary>Cells along a side; cell (0, 0) starts at (Origin, Origin).</summary>
        public readonly int Cells;
        public readonly float Origin;

        private readonly float[] height, slope, fertility, forest, ore;
        private readonly BiomeType[] biome;
        private readonly bool[] water, wall;

        private static CoarseWorldMap current;

        /// <summary>The map of the current world (built on first use after the world changed).</summary>
        public static CoarseWorldMap Current
        {
            get
            {
                if (current == null || current.Seed != WorldSettings.Seed || current.Size != WorldSettings.Size) current = new CoarseWorldMap();
                return current;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => current = null;

        private CoarseWorldMap()
        {
            Seed = WorldSettings.Seed;
            Size = WorldSettings.Size;
            float extent = WorldSettings.Bounded ? Size : UnboundedExtent;
            Cells = Mathf.CeilToInt(extent / CellSize);
            Origin = -Cells * CellSize * 0.5f;
            int n = Cells * Cells;
            height = new float[n]; slope = new float[n]; fertility = new float[n]; forest = new float[n]; ore = new float[n];
            biome = new BiomeType[n]; water = new bool[n]; wall = new bool[n];
            for (int z = 0; z < Cells; z++)
                for (int x = 0; x < Cells; x++) Fill(x, z);
        }

        private void Fill(int x, int z)
        {
            int i = z * Cells + x;
            Vector3 c = CenterOf(x, z, false);
            float h = H(c.x, c.z);
            float steep = Mathf.Max(Mathf.Max(Mathf.Abs(H(c.x + SlopeProbe, c.z) - h), Mathf.Abs(H(c.x - SlopeProbe, c.z) - h)),
                                    Mathf.Max(Mathf.Abs(H(c.x, c.z + SlopeProbe) - h), Mathf.Abs(H(c.x, c.z - SlopeProbe) - h))) / SlopeProbe;
            BiomeType b = InfiniteWorldManager.BiomeAt(c.x, c.z, h);
            float moisture = ProceduralBiomeChunk.Moisture(c.x, c.z);
            float flat = 1f - Mathf.Clamp01(steep / 0.35f);
            height[i] = h;
            slope[i] = steep;
            biome[i] = b;
            wall[i] = WorldSettings.OutsideBy(c.x, c.z) > -CellSize * 0.5f;
            water[i] = Lakes.InWater(c.x, c.z);
            float soil = b == BiomeType.Steppe ? 1f : b == BiomeType.PineForest ? 0.7f : b == BiomeType.Desert ? 0.15f : 0f;
            fertility[i] = wall[i] || water[i] ? 0f : Mathf.Clamp01(soil * (0.5f + 0.5f * moisture) * flat);
            float woods = b == BiomeType.PineForest ? 0.9f : b == BiomeType.Steppe ? 0.25f : b == BiomeType.SnowyPeaks ? 0.1f : 0.05f;
            forest[i] = Mathf.Clamp01(woods * (0.6f + 0.4f * moisture));
            float vein = ProceduralBiomeChunk.SmoothRange(0.45f, 0.85f, OreField(c.x, c.z));
            ore[i] = Mathf.Clamp01(vein + 0.3f * Mathf.Clamp01(steep / 0.4f) + (b == BiomeType.Desert ? 0.2f : 0f));
        }

        private static float H(float x, float z) => InfiniteWorldManager.GetSampledTerrainHeight(x, z);

        /// <summary>The ore-richness field, 0..1, shifted by the seed (hills and deserts add to it in the map).</summary>
        public static float OreField(float x, float z) =>
            Mathf.PerlinNoise((x + WorldSettings.Shift.x) * 0.0031f + 5150f, (z + WorldSettings.Shift.y) * 0.0031f + 2290f);

        public bool InRange(int x, int z) => x >= 0 && z >= 0 && x < Cells && z < Cells;

        /// <summary>The cell a world point falls in (false outside the map).</summary>
        public bool TryCell(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt((p.x - Origin) / CellSize);
            z = Mathf.FloorToInt((p.z - Origin) / CellSize);
            return InRange(x, z);
        }

        /// <summary>The cell's centre (on its ground if asked).</summary>
        public Vector3 CenterOf(int x, int z, bool onGround = true)
        {
            var p = new Vector3(Origin + (x + 0.5f) * CellSize, 0f, Origin + (z + 0.5f) * CellSize);
            if (onGround) p.y = height[z * Cells + x];
            return p;
        }

        public float Height(int x, int z) => height[z * Cells + x];
        /// <summary>The steepest rise round the centre, metres per metre (0.35 and over: no field, no town).</summary>
        public float Slope(int x, int z) => slope[z * Cells + x];
        public BiomeType Biome(int x, int z) => biome[z * Cells + x];
        public float Fertility(int x, int z) => fertility[z * Cells + x];
        public float Forest(int x, int z) => forest[z * Cells + x];
        public float Ore(int x, int z) => ore[z * Cells + x];
        public bool Water(int x, int z) => water[z * Cells + x];
        /// <summary>On the mountain wall round the world: nobody lives or walks there.</summary>
        public bool Wall(int x, int z) => wall[z * Cells + x];
        public bool Habitable(int x, int z) => !wall[z * Cells + x] && !water[z * Cells + x] && biome[z * Cells + x] != BiomeType.SnowyPeaks;

    }
}
