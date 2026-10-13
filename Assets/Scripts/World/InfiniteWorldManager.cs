using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Player;
using BattleOfAges.Managers;

namespace BattleOfAges.World
{
    public enum BiomeType
    {
        Steppe,      // Степ / Зелений Луг
        PineForest,  // Хвойний Ліс
        Desert,      // Пустеля / Скелястий Степ
        SnowyPeaks   // Сніжні Гори
    }

    [DisallowMultipleComponent]
    public class InfiniteWorldManager : MonoBehaviour
    {
        public static InfiniteWorldManager Instance { get; private set; }

        [Header("Infinite Generation Settings")]
        [SerializeField] private float chunkSize = 64f;
        [SerializeField] private int chunkResolution = 32;
        [SerializeField] private int renderDistanceRadius = 3; // 7x7 active chunks around player/camera

        [Header("Target Tracking")]
        [SerializeField] private Transform viewerTarget;

        private readonly Dictionary<Vector2Int, ProceduralBiomeChunk> activeChunks = new Dictionary<Vector2Int, ProceduralBiomeChunk>();
        private readonly HashSet<Vector2Int> requiredChunksBuffer = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> chunksToRemoveBuffer = new List<Vector2Int>();

        private Vector2Int currentViewerChunk = new Vector2Int(99999, 99999);
        private Material steppeMat, forestMat, desertMat, snowMat;

        public float ChunkSize => chunkSize;
        public int ChunkResolution => chunkResolution;

        private GameObject steppeTerrainRef;
        private FarTerrainRenderer farTerrain;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsurePoolManager();
            CreateBiomeMaterials();
            CreateFarTerrain();
        }

        private void EnsurePoolManager()
        {
            if (EnvironmentPoolManager.Instance == null && FindFirstObjectByType<EnvironmentPoolManager>() == null)
            {
                GameObject poolObj = new GameObject("EnvironmentPoolManager");
                poolObj.transform.SetParent(transform);
                poolObj.AddComponent<EnvironmentPoolManager>();
            }
        }

        private void Start()
        {
            steppeTerrainRef = GameObject.Find("SteppeTerrain");
            FindViewerTarget();
            if (viewerTarget != null) currentViewerChunk = GetChunkCoordFromWorldPos(viewerTarget.position); // not the (99999, 99999) placeholder
            UpdateChunksAroundViewer();
        }

        public void FindViewerTarget()
        {
            if (viewerTarget == null)
            {
                GameObject player = GameObject.Find("Player");
                if (player != null) viewerTarget = player.transform;
                else if (Camera.main != null) viewerTarget = Camera.main.transform;
            }
        }

        private void Update()
        {
            if (viewerTarget == null)
            {
                FindViewerTarget();
                if (viewerTarget == null) return;
            }

            Vector2Int newChunkPos = GetChunkCoordFromWorldPos(viewerTarget.position);
            if (newChunkPos != currentViewerChunk)
            {
                currentViewerChunk = newChunkPos;
                UpdateChunksAroundViewer();
            }
        }

        public Vector2Int GetChunkCoordFromWorldPos(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt((worldPos.x + chunkSize * 0.5f) / chunkSize);
            int z = Mathf.FloorToInt((worldPos.z + chunkSize * 0.5f) / chunkSize);
            return new Vector2Int(x, z);
        }

        public bool IsBaseZoneChunk(Vector2Int coord)
        {
            if (steppeTerrainRef == null)
            {
                steppeTerrainRef = GameObject.Find("SteppeTerrain");
            }
            if (steppeTerrainRef == null) return false;

            // Chunks (-1..1, -1..1) cover [-96, 96]m which is precisely rendered by SteppeTerrain (192m)
            return Mathf.Abs(coord.x) <= 1 && Mathf.Abs(coord.y) <= 1;
        }

        private void UpdateChunksAroundViewer()
        {
            requiredChunksBuffer.Clear();

            for (int x = -renderDistanceRadius; x <= renderDistanceRadius; x++)
            {
                for (int z = -renderDistanceRadius; z <= renderDistanceRadius; z++)
                {
                    Vector2Int chunkCoord = new Vector2Int(currentViewerChunk.x + x, currentViewerChunk.y + z);

                    // Skip chunks that overlap the central base SteppeTerrain to prevent duplicate z-fighting on spawn
                    if (IsBaseZoneChunk(chunkCoord))
                    {
                        continue;
                    }

                    requiredChunksBuffer.Add(chunkCoord);

                    bool isLowLOD = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) >= 2;

                    if (!activeChunks.ContainsKey(chunkCoord))
                    {
                        ProceduralBiomeChunk newChunk = CreateChunkAt(chunkCoord, isLowLOD);
                        activeChunks.Add(chunkCoord, newChunk);
                    }
                    else
                    {
                        ProceduralBiomeChunk existingChunk = activeChunks[chunkCoord];
                        if (existingChunk != null)
                        {
                            if (existingChunk.IsLowLOD != isLowLOD)
                            {
                                existingChunk.GenerateChunk(chunkCoord, chunkSize, chunkResolution, isLowLOD, steppeMat, forestMat, desertMat, snowMat);
                            }
                            existingChunk.gameObject.SetActive(true);
                        }
                    }
                }
            }

            // Deactivate or return chunks outside render radius
            chunksToRemoveBuffer.Clear();
            foreach (var kvp in activeChunks)
            {
                if (!requiredChunksBuffer.Contains(kvp.Key))
                {
                    chunksToRemoveBuffer.Add(kvp.Key);
                }
            }

            EnvironmentPoolManager pool = EnvironmentPoolManager.Instance;
            int removeCount = chunksToRemoveBuffer.Count;
            for (int i = 0; i < removeCount; i++)
            {
                Vector2Int coord = chunksToRemoveBuffer[i];
                if (activeChunks.TryGetValue(coord, out ProceduralBiomeChunk chunk))
                {
                    activeChunks.Remove(coord);
                    if (pool != null)
                    {
                        pool.ReturnChunk(chunk);
                    }
                    else if (chunk != null)
                    {
                        chunk.gameObject.SetActive(false);
                    }
                }
            }

            if (farTerrain != null)
            {
                farTerrain.Rebuild(currentViewerChunk, chunkSize, renderDistanceRadius);
            }
        }

        private void CreateFarTerrain()
        {
            GameObject farObj = new GameObject("FarTerrain");
            farObj.transform.SetParent(transform, false);
            farTerrain = farObj.AddComponent<FarTerrainRenderer>();
            farTerrain.Initialize(steppeMat);

            gameObject.AddComponent<GrassFieldRenderer>().Initialize(Shader.Find("Custom/GrassBlades"));
        }

        private ProceduralBiomeChunk CreateChunkAt(Vector2Int coord, bool isLowLOD)
        {
            EnvironmentPoolManager pool = EnvironmentPoolManager.Instance;
            ProceduralBiomeChunk chunk = (pool != null)
                ? pool.GetChunk(transform, coord)
                : null;

            if (chunk == null)
            {
                GameObject chunkObj = new GameObject($"Chunk_{coord.x}_{coord.y}");
                chunkObj.transform.SetParent(transform, false);
                chunk = chunkObj.AddComponent<ProceduralBiomeChunk>();
            }

            chunk.GenerateChunk(coord, chunkSize, chunkResolution, isLowLOD, steppeMat, forestMat, desertMat, snowMat);
            return chunk;
        }

        private void CreateBiomeMaterials()
        {
            Shader terrainShader = Shader.Find("Custom/StylizedTerrain") 
                ?? Shader.Find("Universal Render Pipeline/Lit") 
                ?? Shader.Find("Standard");

            steppeMat = new Material(terrainShader);
            steppeMat.name = "MAT_Infinite_Steppe";
            steppeMat.SetColor("_GrassDeep", new Color(0.19f, 0.42f, 0.12f));
            steppeMat.SetColor("_GrassMid", new Color(0.28f, 0.54f, 0.16f));
            steppeMat.SetColor("_GrassLight", new Color(0.44f, 0.68f, 0.22f));
            steppeMat.SetColor("_DirtColor", new Color(0.35f, 0.24f, 0.14f));
            steppeMat.SetColor("_RockColor", new Color(0.45f, 0.43f, 0.40f));

            forestMat = new Material(terrainShader);
            forestMat.name = "MAT_Infinite_Forest";
            forestMat.SetColor("_GrassDeep", new Color(0.12f, 0.28f, 0.10f));
            forestMat.SetColor("_GrassMid", new Color(0.18f, 0.38f, 0.14f));
            forestMat.SetColor("_GrassLight", new Color(0.32f, 0.52f, 0.18f));
            forestMat.SetColor("_DirtColor", new Color(0.35f, 0.24f, 0.14f));
            forestMat.SetColor("_RockColor", new Color(0.45f, 0.43f, 0.40f));

            desertMat = new Material(terrainShader);
            desertMat.name = "MAT_Infinite_Desert";
            desertMat.SetColor("_GrassDeep", new Color(0.72f, 0.60f, 0.32f));
            desertMat.SetColor("_GrassMid", new Color(0.82f, 0.72f, 0.40f));
            desertMat.SetColor("_GrassLight", new Color(0.90f, 0.82f, 0.52f));
            desertMat.SetColor("_DirtColor", new Color(0.35f, 0.24f, 0.14f));
            desertMat.SetColor("_RockColor", new Color(0.45f, 0.43f, 0.40f));

            snowMat = new Material(terrainShader);
            snowMat.name = "MAT_Infinite_Snow";
            snowMat.SetColor("_GrassDeep", new Color(0.82f, 0.88f, 0.94f));
            snowMat.SetColor("_GrassMid", new Color(0.92f, 0.95f, 0.98f));
            snowMat.SetColor("_GrassLight", new Color(0.98f, 0.99f, 1.00f));
            snowMat.SetColor("_DirtColor", new Color(0.35f, 0.24f, 0.14f));
            snowMat.SetColor("_RockColor", new Color(0.45f, 0.43f, 0.40f));
        }

        public BiomeType GetBiomeAtWorldPos(Vector3 worldPos)
        {
            float tempNoise = Mathf.PerlinNoise(worldPos.x * 0.003f + 500f, worldPos.z * 0.003f + 500f);
            float moistureNoise = Mathf.PerlinNoise(worldPos.x * 0.004f + 1500f, worldPos.z * 0.004f + 1500f);

            float height = GetSampledTerrainHeight(worldPos.x, worldPos.z);

            if (height > ProceduralBiomeChunk.SnowLineHeight) return BiomeType.SnowyPeaks;
            if (tempNoise > 0.65f && moistureNoise < 0.4f) return BiomeType.Desert;
            if (moistureNoise > 0.55f) return BiomeType.PineForest;

            return BiomeType.Steppe;
        }

        private const float BaseFlatRadius = 50f;
        private const float BaseBlendRadius = 85f;
        private static float? baseZoneHeight;

        /// <summary>
        /// Height of the flat spawn plateau: the average natural terrain height on the outer edge of the
        /// blend ring, so the base sits level with its surroundings instead of in a pit at y = 0.
        /// </summary>
        public static float BaseZoneHeight
        {
            get
            {
                if (!baseZoneHeight.HasValue)
                {
                    const int samples = 72;
                    float sum = 0f;
                    for (int i = 0; i < samples; i++)
                    {
                        float a = i * Mathf.PI * 2f / samples;
                        sum += GetNaturalTerrainHeight(Mathf.Cos(a) * BaseBlendRadius, Mathf.Sin(a) * BaseBlendRadius);
                    }
                    baseZoneHeight = sum / samples;
                }
                return baseZoneHeight.Value;
            }
        }

        public static float GetSampledTerrainHeight(float worldX, float worldZ)
        {
            float y = GetNaturalTerrainHeight(worldX, worldZ);

            // Flat spawn base plateau within 50m radius, smooth transition to natural terrain up to 85m
            float distFromOrigin = new Vector2(worldX, worldZ).magnitude;
            if (distFromOrigin < BaseFlatRadius)
            {
                return BaseZoneHeight;
            }
            else if (distFromOrigin < BaseBlendRadius)
            {
                float t = Mathf.Clamp01((BaseBlendRadius - distFromOrigin) / (BaseBlendRadius - BaseFlatRadius));
                float smoothT = t * t * (3f - 2f * t);
                y = Mathf.Lerp(y, BaseZoneHeight, smoothT);
            }

            return y;
        }

        /// <summary>Coherent Perlin noise heightmap across all chunks (without the base plateau).</summary>
        private static float GetNaturalTerrainHeight(float worldX, float worldZ)
        {
            float continentNoise = Mathf.PerlinNoise(worldX * 0.005f + 100f, worldZ * 0.005f + 100f) * 22.0f;
            float hillNoise = Mathf.PerlinNoise(worldX * 0.02f + 300f, worldZ * 0.02f + 300f) * 6.5f;
            float detailNoise = Mathf.PerlinNoise(worldX * 0.08f + 700f, worldZ * 0.08f + 700f) * 1.5f;

            return continentNoise + hillNoise + detailNoise + GetMountainHeight(worldX, worldZ);
        }

        /// <summary>
        /// Ridged mountain ranges: large regions (low-frequency mask) of sharp ridges (ridged noise), kept away from the
        /// spawn base so the area around it stays gentle hills. Peaks reach ~130 m and carry snow above the snow line.
        /// </summary>
        private static float GetMountainHeight(float worldX, float worldZ)
        {
            float distFromOrigin = new Vector2(worldX, worldZ).magnitude;
            float zone = ProceduralBiomeChunk.SmoothRange(260f, 560f, distFromOrigin);
            if (zone <= 0f) return 0f;

            float region = Mathf.PerlinNoise(worldX * 0.0019f + 913f, worldZ * 0.0019f + 271f);
            float mask = ProceduralBiomeChunk.SmoothRange(0.40f, 0.70f, region) * zone;
            if (mask <= 0f) return 0f;

            float r1 = 1f - Mathf.Abs(Mathf.PerlinNoise(worldX * 0.0065f + 55f, worldZ * 0.0065f + 77f) * 2f - 1f);
            float r2 = 1f - Mathf.Abs(Mathf.PerlinNoise(worldX * 0.017f + 12f, worldZ * 0.017f + 91f) * 2f - 1f);
            float r3 = 1f - Mathf.Abs(Mathf.PerlinNoise(worldX * 0.047f + 3f, worldZ * 0.047f + 41f) * 2f - 1f);
            float ridges = r1 * r1 * 0.64f + r2 * r2 * 0.24f + r3 * r3 * 0.12f; // r3: craggy detail
            return mask * (ridges * 105f + 12f);
        }
    }
}
