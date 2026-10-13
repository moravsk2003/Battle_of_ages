using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleOfAges.World
{
    /// <summary>
    /// Dense instanced grass around the viewer. The world is split into 8x8 m tiles whose clump transforms are computed
    /// once (terrain height, slope and the same ground masks the terrain shader paints) and cached; every frame the
    /// tiles within the draw radius are drawn with Graphics.RenderMeshInstanced. Grass is skipped on dirt patches,
    /// slopes, snow, the camp clearing and sand, so blades only grow where the terrain shader shows grass.
    /// </summary>
    public class GrassFieldRenderer : MonoBehaviour
    {
        private const float TileSize = 8f;
        private const float Spacing = 0.55f;
        private const float DrawRadius = 38f;
        private const int BatchSize = 1023;

        private Material material;
        private Mesh clumpMesh;
        private readonly Dictionary<Vector2Int, Matrix4x4[]> tileCache = new Dictionary<Vector2Int, Matrix4x4[]>();
        private readonly List<Vector2Int> evictBuffer = new List<Vector2Int>();
        private readonly Matrix4x4[] batch = new Matrix4x4[BatchSize];
        private static readonly int BiomeTintId = Shader.PropertyToID("_BiomeTint");

        public void Initialize(Shader grassShader)
        {
            if (grassShader == null) { enabled = false; return; }
            material = new Material(grassShader) { name = "MAT_GrassBlades", enableInstancing = true };
            clumpMesh = BuildClumpMesh();
        }

        private void LateUpdate()
        {
            if (Camera.main != null) Submit(Camera.main.transform.position, null);
        }

        /// <summary>Queues grass for one camera (e.g. a manually rendered screenshot camera) around its position.</summary>
        public void SubmitFor(Camera cam)
        {
            if (cam != null) Submit(cam.transform.position, cam);
        }

        private void Submit(Vector3 c, Camera onlyCamera)
        {
            if (material == null) return;

            float groundHere = InfiniteWorldManager.GetSampledTerrainHeight(c.x, c.z);
            if (c.y - groundHere > DrawRadius) return; // high above the ground: blades would be sub-pixel anyway

            material.SetColor(BiomeTintId, ProceduralBiomeChunk.GetBlendedBiomeColor(c.x, c.z, groundHere));

            int r = Mathf.CeilToInt(DrawRadius / TileSize);
            Vector2Int center = new Vector2Int(Mathf.FloorToInt(c.x / TileSize), Mathf.FloorToInt(c.z / TileSize));
            var rp = new RenderParams(material)
            {
                camera = onlyCamera,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(c, Vector3.one * (DrawRadius * 2f + 20f))
            };

            int filled = 0;
            for (int tz = -r; tz <= r; tz++)
            {
                for (int tx = -r; tx <= r; tx++)
                {
                    Vector2Int key = new Vector2Int(center.x + tx, center.y + tz);
                    Vector2 tileMid = new Vector2((key.x + 0.5f) * TileSize, (key.y + 0.5f) * TileSize);
                    if (Vector2.Distance(tileMid, new Vector2(c.x, c.z)) > DrawRadius + TileSize * 0.75f) continue;

                    if (!tileCache.TryGetValue(key, out Matrix4x4[] tile))
                    {
                        tile = BuildTile(key);
                        tileCache[key] = tile;
                    }

                    for (int i = 0; i < tile.Length; i++)
                    {
                        batch[filled++] = tile[i];
                        if (filled == BatchSize)
                        {
                            Graphics.RenderMeshInstanced(rp, clumpMesh, 0, batch, filled);
                            filled = 0;
                        }
                    }
                }
            }
            if (filled > 0) Graphics.RenderMeshInstanced(rp, clumpMesh, 0, batch, filled);

            if (tileCache.Count > (2 * r + 3) * (2 * r + 3))
            {
                evictBuffer.Clear();
                foreach (Vector2Int key in tileCache.Keys)
                {
                    if (Mathf.Abs(key.x - center.x) > r + 2 || Mathf.Abs(key.y - center.y) > r + 2) evictBuffer.Add(key);
                }
                foreach (Vector2Int key in evictBuffer) tileCache.Remove(key);
            }
        }

        private static Matrix4x4[] BuildTile(Vector2Int key)
        {
            var list = new List<Matrix4x4>(256);
            int cells = Mathf.RoundToInt(TileSize / Spacing);
            for (int gz = 0; gz < cells; gz++)
            {
                for (int gx = 0; gx < cells; gx++)
                {
                    float cx = key.x * TileSize + gx * Spacing;
                    float cz = key.y * TileSize + gz * Spacing;
                    float r0 = Hash(cx, cz), r1 = Hash(cz + 17.3f, cx - 5.1f), r2 = Hash(cx * 1.7f + 3.1f, cz * 0.7f - 9.4f);
                    float x = cx + (r0 - 0.5f) * Spacing * 0.9f;
                    float z = cz + (r1 - 0.5f) * Spacing * 0.9f;

                    float density = GrassDensity(x, z, out float y);
                    if (r2 > density) continue;

                    float scale = Mathf.Lerp(0.75f, 1.25f, r1) * Mathf.Lerp(0.7f, 1f, density);
                    list.Add(Matrix4x4.TRS(new Vector3(x, y - 0.03f, z), Quaternion.Euler(0f, r0 * 360f, 0f), new Vector3(scale, scale * Mathf.Lerp(0.8f, 1.3f, r2), scale)));
                }
            }
            return list.ToArray();
        }

        /// <summary>0..1 chance of a clump here; mirrors the dirt/rock/snow/camp masks of Custom/StylizedTerrain.</summary>
        public static float GrassDensity(float x, float z, out float y)
        {
            y = InfiniteWorldManager.GetSampledTerrainHeight(x, z);
            if (y > ProceduralBiomeChunk.SnowLineHeight - 6f) return 0f;

            float hx = InfiniteWorldManager.GetSampledTerrainHeight(x + 1f, z);
            float hz = InfiniteWorldManager.GetSampledTerrainHeight(x, z + 1f);
            Vector3 n = new Vector3(y - hx, 1f, y - hz).normalized;
            float slope = 1f - n.y;
            if (slope > 0.2f) return 0f;

            float m2 = Fbm3(x * 0.05f + 31.7f, z * 0.05f + 7.3f);
            float m3 = Noise(x * 0.33f + 5.1f, z * 0.33f + 9.9f);
            float dirtPatch = SmoothStep(0.66f, 0.80f, m2 * 0.65f + m3 * 0.35f);
            float campDirt = 1f - SmoothStep(1.8f, 6.5f, new Vector2(x - 1.5f, z - 3.5f).magnitude);
            float sand = ProceduralBiomeChunk.GetDesertWeight(x, z);

            float density = (1f - dirtPatch) * (1f - campDirt) * (1f - SmoothStep(0.10f, 0.20f, slope)) * (1f - sand * 0.85f);
            return Mathf.Clamp01(density * Mathf.Lerp(0.75f, 1f, m3));
        }

        // ---- CPU copies of the terrain shader noise (hash21 / noise2D / fbm3)
        private static float Frac(float v) => v - Mathf.Floor(v);
        private static float Hash(float px, float py)
        {
            px = Frac(px * 234.34f); py = Frac(py * 435.345f);
            float d = px * (px + 34.23f) + py * (py + 34.23f);
            px += d; py += d;
            return Frac(px * py);
        }
        private static float Noise(float x, float y)
        {
            float ix = Mathf.Floor(x), iy = Mathf.Floor(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy), b = Hash(ix + 1f, iy), c = Hash(ix, iy + 1f), d = Hash(ix + 1f, iy + 1f);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        private static float Fbm3(float x, float y) =>
            Noise(x, y) * 0.55f + Noise(x * 2.07f + 13.1f, y * 2.07f + 13.1f) * 0.30f + Noise(x * 4.13f + 71.7f, y * 4.13f + 71.7f) * 0.15f;
        private static float SmoothStep(float e0, float e1, float x) => ProceduralBiomeChunk.SmoothRange(e0, e1, x);

        /// <summary>A clump of 9 thin, curved, tapered blades (3 segments each), 0.3-0.7 m tall.</summary>
        private static Mesh BuildClumpMesh()
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            var rng = new System.Random(7);
            float R() => (float)rng.NextDouble();

            const int blades = 9, segments = 3;
            for (int b = 0; b < blades; b++)
            {
                float ang = b / (float)blades * Mathf.PI * 2f + R() * 0.8f;
                Vector3 facing = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 side = new Vector3(-facing.z, 0f, facing.x);
                Vector3 root = facing * (R() * 0.12f);
                float height = 0.30f + R() * 0.40f;
                float width = 0.026f + R() * 0.02f;
                float lean = 0.10f + R() * 0.28f;
                float rand = R();

                int start = verts.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float t = s / (float)segments;
                    Vector3 center = root + Vector3.up * (height * t) + facing * (lean * t * t);
                    float w = width * (1f - t * 0.92f);
                    verts.Add(center - side * w);
                    verts.Add(center + side * w);
                    Vector3 n = (-facing + Vector3.up * 0.6f).normalized;
                    normals.Add(n); normals.Add(n);
                    colors.Add(new Color(t, rand, 0f, 1f)); colors.Add(new Color(t, rand, 0f, 1f));
                }
                for (int s = 0; s < segments; s++)
                {
                    int i = start + s * 2;
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + 2); tris.Add(i + 3);
                }
            }

            var mesh = new Mesh { name = "GrassClump" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
