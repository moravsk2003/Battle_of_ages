using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleOfAges.World
{
    /// <summary>
    /// Dense instanced grass around the viewer. The world is split into 8x8 m tiles whose clump transforms are computed
    /// once (terrain height, slope and the same ground masks the terrain shader paints) and cached; every frame the
    /// tiles within the draw radius that the camera sees are drawn with Graphics.RenderMeshInstanced: near tiles with
    /// the full clump (curved blades), the rest with a light one (fewer, wider blades) that the fade sinks into the
    /// ground at the edge; a few clumps near the viewer are wild flowers. Grass is skipped on dirt patches, slopes, snow,
    /// the camp clearing and sand, so blades only grow where the terrain shader shows grass. The colour and the light
    /// are worked out per vertex (Custom/GrassBlades), the pixel only takes the shadow: an integrated GPU draws it.
    /// </summary>
    public class GrassFieldRenderer : MonoBehaviour
    {
        private const float TileSize = 8f;
        private const float Spacing = 0.55f;
        public const float FullDrawRadius = 38f, FullNearRadius = 14f;
        /// <summary>How far grass is drawn, how far with the full clumps, and every how-many-th clump (the graphics preset sets them, GraphicsQuality).</summary>
        public static float DrawRadius = FullDrawRadius, NearRadius = FullNearRadius;
        public static int Stride = 1;
        /// <summary>Share of the clumps that are wild flowers, drawn this much past the near radius.</summary>
        private const float FlowerShare = 0.03f, FlowerReach = 8f;
        /// <summary>Clumps queued by the last submit (for the probes).</summary>
        public static int LastDrawn;
        private const int BatchSize = 1023;

        private sealed class Tile
        {
            public Matrix4x4[] Grass, Flowers;
            public Bounds Bounds;
        }

        private sealed class Batch
        {
            public readonly Matrix4x4[] Items = new Matrix4x4[BatchSize];
            public int Count;
            public Mesh Mesh;
        }

        private Material material;
        private readonly Batch near = new Batch(), far = new Batch(), flowers = new Batch();
        private readonly Dictionary<Vector2Int, Tile> tileCache = new Dictionary<Vector2Int, Tile>();
        private static GrassFieldRenderer current;

        /// <summary>The land changed (a field): tiles are made anew.</summary>
        public static void ForgetTiles() { if (current != null) current.tileCache.Clear(); }
        private readonly List<Vector2Int> evictBuffer = new List<Vector2Int>();
        private readonly Plane[] frustum = new Plane[6];
        private static readonly int BiomeTintId = Shader.PropertyToID("_BiomeTint");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart"), FadeEndId = Shader.PropertyToID("_FadeEnd");

        public void Initialize(Shader grassShader)
        {
            current = this;
            if (grassShader == null) { enabled = false; return; }
            material = new Material(grassShader) { name = "MAT_GrassBlades", enableInstancing = true };
            near.Mesh = BuildClumpMesh("GrassClump", blades: 9, segments: 3, widthScale: 1f, seed: 7);
            far.Mesh = BuildClumpMesh("GrassClumpFar", blades: 6, segments: 2, widthScale: 1.6f, seed: 7);
            flowers.Mesh = BuildFlowerMesh();
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null) Submit(cam.transform.position, cam);
        }

        /// <summary>Queues grass for one camera (e.g. a manually rendered screenshot camera) around its position.</summary>
        public void SubmitFor(Camera cam)
        {
            if (cam != null) Submit(cam.transform.position, cam);
        }

        private void Submit(Vector3 c, Camera cam)
        {
            if (material == null) return;

            float groundHere = InfiniteWorldManager.GetSampledTerrainHeight(c.x, c.z);
            if (c.y - groundHere > DrawRadius) return; // high above the ground: blades would be sub-pixel anyway

            material.SetColor(BiomeTintId, ProceduralBiomeChunk.GetBlendedBiomeColor(c.x, c.z, groundHere));
            material.SetFloat(FadeStartId, DrawRadius * 0.72f);
            material.SetFloat(FadeEndId, DrawRadius);
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);

            int r = Mathf.CeilToInt(DrawRadius / TileSize);
            Vector2Int center = new Vector2Int(Mathf.FloorToInt(c.x / TileSize), Mathf.FloorToInt(c.z / TileSize));
            var rp = new RenderParams(material)
            {
                camera = cam, // only the view: not the minimap, not the shadow maps (grass casts none)
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                worldBounds = new Bounds(c, Vector3.one * (DrawRadius * 2f + 20f))
            };

            int drawn = 0;
            const float halfDiagonal = TileSize * 0.7072f;
            for (int tz = -r; tz <= r; tz++)
            {
                for (int tx = -r; tx <= r; tx++)
                {
                    Vector2Int key = new Vector2Int(center.x + tx, center.y + tz);
                    Vector2 tileMid = new Vector2((key.x + 0.5f) * TileSize, (key.y + 0.5f) * TileSize);
                    float dist = Vector2.Distance(tileMid, new Vector2(c.x, c.z)) - halfDiagonal; // to its nearest corner
                    if (dist > DrawRadius) continue;

                    if (!tileCache.TryGetValue(key, out Tile tile))
                    {
                        tile = BuildTile(key);
                        tileCache[key] = tile;
                    }
                    if (tile.Grass.Length == 0 || !GeometryUtility.TestPlanesAABB(frustum, tile.Bounds)) continue;

                    Batch into = dist < NearRadius ? near : far;
                    for (int i = 0; i < tile.Grass.Length; i += Stride) { Add(into, tile.Grass[i], ref rp); drawn++; }
                    if (dist < NearRadius + FlowerReach)
                        for (int i = 0; i < tile.Flowers.Length; i++) { Add(flowers, tile.Flowers[i], ref rp); drawn++; }
                }
            }
            Flush(near, ref rp); Flush(far, ref rp); Flush(flowers, ref rp);
            LastDrawn = drawn;

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

        private static void Add(Batch b, Matrix4x4 m, ref RenderParams rp)
        {
            b.Items[b.Count++] = m;
            if (b.Count == BatchSize) Flush(b, ref rp);
        }

        private static void Flush(Batch b, ref RenderParams rp)
        {
            if (b.Count > 0) Graphics.RenderMeshInstanced(rp, b.Mesh, 0, b.Items, b.Count);
            b.Count = 0;
        }

        private static Tile BuildTile(Vector2Int key)
        {
            var grass = new List<Matrix4x4>(256);
            var flowerList = new List<Matrix4x4>(8);
            float minY = float.MaxValue, maxY = float.MinValue;
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
                    var m = Matrix4x4.TRS(new Vector3(x, y - 0.03f, z), Quaternion.Euler(0f, r0 * 360f, 0f), new Vector3(scale, scale * Mathf.Lerp(0.8f, 1.3f, r2), scale));
                    bool flower = density > 0.5f && Hash(cz * 0.37f + 1.9f, cx * 1.3f + 4.4f) < FlowerShare;
                    (flower ? flowerList : grass).Add(m);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            }
            var tile = new Tile { Grass = grass.ToArray(), Flowers = flowerList.ToArray() };
            if (grass.Count + flowerList.Count > 0)
            {
                var min = new Vector3(key.x * TileSize - 0.5f, minY - 0.2f, key.y * TileSize - 0.5f);
                var max = new Vector3((key.x + 1) * TileSize + 0.5f, maxY + 1.2f, (key.y + 1) * TileSize + 0.5f);
                tile.Bounds.SetMinMax(min, max);
            }
            return tile;
        }

        /// <summary>0..1 chance of a clump here; mirrors the dirt/rock/snow/camp masks of Custom/StylizedTerrain.</summary>
        public static float GrassDensity(float x, float z, out float y)
        {
            y = InfiniteWorldManager.GetSampledTerrainHeight(x, z);
            if (y > ProceduralBiomeChunk.SnowLineHeight - 6f) return 0f;
            if (Building.FieldNode.IsFieldAt(x, z)) return 0f; // ploughed

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

        // ---- clump meshes. Vertex colour: r height along the blade (0 root .. 1 tip), g per-blade random,
        // b part (0 blade or stem, 0.5 flower centre, 1 petal), a the flower's colour pick.

        private sealed class MeshParts
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Tris = new List<int>();

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Verts);
                mesh.SetNormals(Normals);
                mesh.SetColors(Colors);
                mesh.SetTriangles(Tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>
        /// One tapered, curved blade of segments quads ending in a single tip vertex; the normals lean out to its edges
        /// so the flat blade shades as if rounded.
        /// </summary>
        private static void AddBlade(MeshParts p, Vector3 root, Vector3 facing, float height, float width, float lean, int segments, float rand)
        {
            Vector3 side = new Vector3(-facing.z, 0f, facing.x);
            Vector3 n0 = (-facing + Vector3.up * 0.6f).normalized;
            int start = p.Verts.Count;
            for (int s = 0; s < segments; s++)
            {
                float t = s / (float)segments;
                Vector3 center = root + Vector3.up * (height * t) + facing * (lean * t * t);
                float w = width * (1f - t * 0.85f);
                p.Verts.Add(center - side * w); p.Verts.Add(center + side * w);
                p.Normals.Add((n0 - side * 0.45f).normalized); p.Normals.Add((n0 + side * 0.45f).normalized);
                p.Colors.Add(new Color(t, rand, 0f, 0f)); p.Colors.Add(new Color(t, rand, 0f, 0f));
            }
            p.Verts.Add(root + Vector3.up * height + facing * lean);
            p.Normals.Add(n0);
            p.Colors.Add(new Color(1f, rand, 0f, 0f));
            for (int s = 0; s < segments - 1; s++)
            {
                int i = start + s * 2;
                p.Tris.Add(i); p.Tris.Add(i + 2); p.Tris.Add(i + 1);
                p.Tris.Add(i + 1); p.Tris.Add(i + 2); p.Tris.Add(i + 3);
            }
            int last = start + (segments - 1) * 2;
            p.Tris.Add(last); p.Tris.Add(last + 2); p.Tris.Add(last + 1);
        }

        /// <summary>A clump of thin, curved, tapered blades, 0.3-0.7 m tall (the far one: fewer, wider, coarser).</summary>
        private static Mesh BuildClumpMesh(string name, int blades, int segments, float widthScale, int seed)
        {
            var p = new MeshParts();
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            for (int b = 0; b < blades; b++)
            {
                float ang = b / (float)blades * Mathf.PI * 2f + R() * 0.8f;
                Vector3 facing = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 root = facing * (R() * 0.12f);
                float height = 0.30f + R() * 0.40f;
                float width = (0.026f + R() * 0.02f) * widthScale;
                float lean = 0.10f + R() * 0.28f;
                AddBlade(p, root, facing, height, width, lean, segments, R());
            }
            return p.ToMesh(name);
        }

        /// <summary>A few blades and three wild flowers: a thin stem and a six-petal head with a darker centre.</summary>
        private static Mesh BuildFlowerMesh()
        {
            var p = new MeshParts();
            var rng = new System.Random(11);
            float R() => (float)rng.NextDouble();
            for (int b = 0; b < 4; b++)
            {
                float ang = b / 4f * Mathf.PI * 2f + R();
                AddBlade(p, Vector3.zero, new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), 0.25f + R() * 0.2f, 0.03f, 0.15f, 2, R());
            }
            for (int f = 0; f < 3; f++)
            {
                float ang = f / 3f * Mathf.PI * 2f + R() * 0.6f;
                Vector3 facing = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 root = facing * (0.04f + R() * 0.10f);
                float height = 0.38f + R() * 0.22f, lean = 0.05f + R() * 0.08f, pick = R();
                AddBlade(p, root, facing, height, 0.008f, lean, 2, R()); // the stem

                Vector3 head = root + Vector3.up * height + facing * lean;
                Vector3 up = (Vector3.up + facing * 0.35f).normalized;
                Vector3 a = Vector3.Cross(up, facing).normalized, c = Vector3.Cross(up, a).normalized;
                float radius = 0.045f + R() * 0.02f;
                int centre = p.Verts.Count;
                p.Verts.Add(head + up * 0.012f); p.Normals.Add(up); p.Colors.Add(new Color(1f, 0.5f, 0.5f, pick));
                const int petals = 12; // the rim, every other vertex is a petal tip
                for (int k = 0; k < petals; k++)
                {
                    float t = k / (float)petals * Mathf.PI * 2f;
                    float rr = radius * (k % 2 == 0 ? 1f : 0.45f);
                    p.Verts.Add(head + (a * Mathf.Cos(t) + c * Mathf.Sin(t)) * rr);
                    p.Normals.Add(up);
                    p.Colors.Add(new Color(1f, 0.5f, 1f, pick));
                }
                for (int k = 0; k < petals; k++)
                {
                    p.Tris.Add(centre); p.Tris.Add(centre + 1 + k); p.Tris.Add(centre + 1 + (k + 1) % petals);
                }
            }
            return p.ToMesh("GrassFlowers");
        }
    }
}
