using System.Collections;
using UnityEngine;

namespace BattleOfAges.World
{
    /// <summary>
    /// The relief of the whole world as a picture (the global map, docs/GAME_DESIGN.md §5.4): biomes in their colours, the land's big
    /// forms lit from the north-west, contour lines, lakes, the mountain wall round the edge. Measured from the ground's own functions on
    /// a 32 m grid; the shading goes by heights smoothed over about 100 m (the ground's small bumps made it look like leather). Made once
    /// for a world, a little each frame (Advance) from the start of the game, so that the map opens at once.
    /// </summary>
    public static class WorldMapPicture
    {
        /// <summary>Pixels of the picture for a cell of the coarse map.</summary>
        public const int PixelsPerCell = 4;
        private const int GridPerCell = 2;
        private const float ContourStep = 25f, Relief = 1.5f;

        private static readonly Color Steppe = new Color(0.62f, 0.66f, 0.40f), Forest = new Color(0.27f, 0.43f, 0.25f), Desert = new Color(0.82f, 0.71f, 0.47f),
                                      Snow = new Color(0.90f, 0.91f, 0.93f), Water = new Color(0.25f, 0.45f, 0.62f), Shore = new Color(0.45f, 0.62f, 0.70f),
                                      Wall = new Color(0.36f, 0.33f, 0.31f), Contour = new Color(0.22f, 0.18f, 0.12f);

        private static Texture2D finished;
        private static IEnumerator making;
        private static int forSeed;
        private static float forSize = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { finished = null; making = null; forSeed = 0; forSize = -1f; }

        /// <summary>The picture of the current world, finished now if it is not yet.</summary>
        public static Texture2D Current
        {
            get
            {
                Begin();
                while (making != null && making.MoveNext()) { }
                making = null;
                return finished;
            }
        }

        /// <summary>The current world's picture is drawn already (nothing to wait for).</summary>
        public static bool Ready => finished != null && making == null && forSeed == WorldSettings.Seed && forSize == WorldSettings.Size;

        /// <summary>Goes on drawing for about this long.</summary>
        public static void Advance(float milliseconds)
        {
            if (Ready) return;
            Begin();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (making != null && clock.Elapsed.TotalMilliseconds < milliseconds)
                if (!making.MoveNext()) making = null;
        }

        private static void Begin()
        {
            if (forSeed == WorldSettings.Seed && forSize == WorldSettings.Size && (finished != null || making != null)) return;
            forSeed = WorldSettings.Seed;
            forSize = WorldSettings.Size;
            if (finished != null) Object.Destroy(finished);
            finished = null;
            making = Steps(CoarseWorldMap.Current, t => finished = t);
        }

        /// <summary>A world point on the picture, 0..1 from the bottom left (the world's +x to the right, +z up).</summary>
        public static Vector2 Place(CoarseWorldMap map, Vector3 world)
        {
            float extent = map.Cells * CoarseWorldMap.CellSize;
            return new Vector2((world.x - map.Origin) / extent, (world.z - map.Origin) / extent);
        }

        /// <summary>The whole picture of this map, at once.</summary>
        public static Texture2D Draw(CoarseWorldMap map)
        {
            Texture2D picture = null;
            IEnumerator steps = Steps(map, t => picture = t);
            while (steps.MoveNext()) { }
            return picture;
        }

        private static IEnumerator Steps(CoarseWorldMap map, System.Action<Texture2D> done)
        {
            int n = map.Cells * GridPerCell + 1, side = map.Cells * PixelsPerCell;
            float step = CoarseWorldMap.CellSize / GridPerCell;
            var height = new float[n * n];
            var wet = new float[n * n];
            var ground = new Color[n * n];
            float low = float.MaxValue, high = float.MinValue;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float wx = map.Origin + x * step, wz = map.Origin + z * step;
                    float h = InfiniteWorldManager.GetSampledTerrainHeight(wx, wz);
                    int i = z * n + x;
                    height[i] = h;
                    wet[i] = Lakes.InWater(wx, wz) ? 1f : 0f;
                    ground[i] = GroundAt(map, wx, wz, h);
                    if (WorldSettings.OutsideBy(wx, wz) < 0f) { low = Mathf.Min(low, h); high = Mathf.Max(high, h); }
                }
                yield return null;
            }
            float[] smooth = Blurred(Blurred(height, n), n);
            yield return null;

            var pixels = new Color32[side * side];
            float perPixel = (float)GridPerCell / PixelsPerCell, metres = CoarseWorldMap.CellSize / PixelsPerCell;
            for (int py = 0; py < side; py++)
            {
                float gz = (py + 0.5f) * perPixel, worldZ = map.Origin + (py + 0.5f) * metres;
                for (int px = 0; px < side; px++)
                {
                    float gx = (px + 0.5f) * perPixel, worldX = map.Origin + (px + 0.5f) * metres;
                    pixels[py * side + px] = Pixel(smooth, height, wet, ground, n, gx, gz, perPixel, step, worldX, worldZ, low, high);
                }
                yield return null;
            }
            var picture = new Texture2D(side, side, TextureFormat.RGBA32, false) { name = "WorldMapPicture", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            picture.SetPixels32(pixels);
            picture.Apply(false, false);
            done(picture);
        }

        private static Color32 Pixel(float[] smooth, float[] height, float[] wet, Color[] ground, int n, float gx, float gz, float perPixel, float step, float worldX, float worldZ, float low, float high)
        {
            float big = Between(smooth, n, gx, gz);
            float east = Between(smooth, n, gx + 1f, gz), west = Between(smooth, n, gx - 1f, gz), north = Between(smooth, n, gx, gz + 1f), south = Between(smooth, n, gx, gz - 1f);
            float dx = (east - west) / (2f * step), dz = (north - south) / (2f * step);
            // Lit from the north-west: what faces it is lighter
            float light = Mathf.Clamp(1f + (dz - dx) * Relief, 0.6f, 1.25f);
            float rise = high > low ? Mathf.Clamp01((big - low) / (high - low)) : 0.5f;
            Color c = Between(ground, n, gx, gz) * light * Mathf.Lerp(0.92f, 1.1f, rise);

            float outside = WorldSettings.OutsideBy(worldX, worldZ);
            if (outside > -24f) c = Color.Lerp(c, Wall * light, Mathf.Clamp01((outside + 24f) / 48f));
            else
            {
                int here = Mathf.FloorToInt(big / ContourStep);
                if (here != Mathf.FloorToInt(Between(smooth, n, gx + perPixel, gz) / ContourStep) || here != Mathf.FloorToInt(Between(smooth, n, gx, gz + perPixel) / ContourStep))
                    c = Color.Lerp(c, Contour, 0.2f);
            }
            float lake = Between(wet, n, gx, gz);
            if (lake > 0.4f) c = lake < 0.55f ? Shore : Water;
            c.a = 1f;
            return c;
        }

        private static Color GroundAt(CoarseWorldMap map, float x, float z, float h)
        {
            BiomeType b = InfiniteWorldManager.BiomeAt(x, z, h);
            if (b == BiomeType.Desert) return Desert;
            if (b == BiomeType.SnowyPeaks) return Snow;
            if (b == BiomeType.PineForest) return Forest;
            float woods = map.TryCell(new Vector3(x, 0f, z), out int cx, out int cz) ? map.Forest(cx, cz) : 0f;
            return Color.Lerp(Steppe, Forest, woods * 0.7f);
        }

        private static float[] Blurred(float[] values, int n)
        {
            var result = new float[values.Length];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int dz = Mathf.Max(0, z - 1); dz <= Mathf.Min(n - 1, z + 1); dz++)
                        for (int dx = Mathf.Max(0, x - 1); dx <= Mathf.Min(n - 1, x + 1); dx++) { sum += values[dz * n + dx]; count++; }
                    result[z * n + x] = sum / count;
                }
            return result;
        }

        private static float Between(float[] values, int n, float x, float z)
        {
            Corners(n, x, z, out int a, out int b, out int c, out int d, out float fx, out float fz);
            return Mathf.Lerp(Mathf.Lerp(values[a], values[b], fx), Mathf.Lerp(values[c], values[d], fx), fz);
        }

        private static Color Between(Color[] values, int n, float x, float z)
        {
            Corners(n, x, z, out int a, out int b, out int c, out int d, out float fx, out float fz);
            return Color.LerpUnclamped(Color.LerpUnclamped(values[a], values[b], fx), Color.LerpUnclamped(values[c], values[d], fx), fz);
        }

        private static void Corners(int n, float x, float z, out int a, out int b, out int c, out int d, out float fx, out float fz)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, n - 1), z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, n - 1);
            int x1 = Mathf.Min(x0 + 1, n - 1), z1 = Mathf.Min(z0 + 1, n - 1);
            fx = Mathf.Clamp01(x - x0);
            fz = Mathf.Clamp01(z - z0);
            a = z0 * n + x0; b = z0 * n + x1; c = z1 * n + x0; d = z1 * n + x1;
        }
    }
}
