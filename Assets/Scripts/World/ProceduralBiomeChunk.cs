using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.Utils;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class ProceduralBiomeChunk : MonoBehaviour
    {
        /// <summary>Height above which ground turns to snow (terrain shader _SnowHeight, biome and spawn thresholds).</summary>
        public const float SnowLineHeight = 46f;

        /// <summary>
        /// Hermite 0..1 ramp of x between edge0 and edge1 (like HLSL smoothstep). Note: Mathf.SmoothStep(from, to, t)
        /// is an interpolation between from and to, not a ramp - using it here made every vertex snow-coloured.
        /// </summary>
        public static float SmoothRange(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>0..1 sandiness (hot and dry), same formula as the desert blend in GetBlendedBiomeColor.</summary>
        public static float GetDesertWeight(float worldX, float worldZ)
        {
            float tempNoise = Mathf.PerlinNoise(worldX * 0.003f + 500f, worldZ * 0.003f + 500f);
            float moistureNoise = Mathf.PerlinNoise(worldX * 0.004f + 1500f, worldZ * 0.004f + 1500f);
            return SmoothRange(0.55f, 0.75f, tempNoise) * (1f - SmoothRange(0.3f, 0.5f, moistureNoise));
        }

        public Vector2Int ChunkCoord { get; private set; }
        public BiomeType MainBiome { get; private set; }
        public bool IsLowLOD { get; private set; }

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Mesh sharedChunkMesh;

        private readonly List<GameObject> activeTrees = new List<GameObject>(16);
        private readonly List<GameObject> activePebbles = new List<GameObject>(16);

        /// <summary>True once the chunk has ground (terrain mesh and collider) at ChunkCoord; a LOD rebuild keeps the old one meanwhile.</summary>
        public bool HasTerrain { get; private set; }

        private IEnumerator steps;
        private Random.State random; // this chunk's scatter sequence, kept apart from everything that runs between its steps

        private void Awake()
        {
            EnsureComponents();
        }

        // (a chunk taken from the pool may be generated before it was ever active, so Awake has not run)
        private void EnsureComponents()
        {
            if (meshFilter != null) return;
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
        }

        /// <summary>Generates the whole chunk at once.</summary>
        public void GenerateChunk(Vector2Int coord, float size, int resolution, bool isLowLOD, Material steppeMat, Material forestMat, Material desertMat, Material snowMat)
        {
            BeginGenerate(coord, size, resolution, isLowLOD, steppeMat, forestMat, desertMat, snowMat);
            while (Step()) { }
        }

        /// <summary>
        /// Starts (or restarts) generation; the work is done by Step calls, so the world manager can spread it over frames:
        /// the first step builds the terrain mesh and collider (and activates the chunk), each later one places one tree,
        /// pebble, animal or raider camp. The result is the same as GenerateChunk.
        /// </summary>
        public void BeginGenerate(Vector2Int coord, float size, int resolution, bool isLowLOD, Material steppeMat, Material forestMat, Material desertMat, Material snowMat)
        {
            if (coord != ChunkCoord) HasTerrain = false;
            ChunkCoord = coord;
            IsLowLOD = isLowLOD;
            steps = GenerateSteps(coord, size, resolution, isLowLOD, steppeMat, forestMat, desertMat, snowMat);
        }

        /// <summary>Runs the next generation step; false when the chunk is complete.</summary>
        public bool Step()
        {
            if (steps == null) return false;
            Random.State outer = Random.state;
            Random.state = random;
            bool more;
            try { more = steps.MoveNext(); }
            finally { random = Random.state; Random.state = outer; }
            if (!more) steps = null;
            return more;
        }

        private IEnumerator GenerateSteps(Vector2Int coord, float size, int resolution, bool isLowLOD, Material steppeMat, Material forestMat, Material desertMat, Material snowMat)
        {
            ReleaseChunkResources(true); // the previous content (another place or LOD)
            BuildTerrain(coord, size, resolution, isLowLOD, steppeMat, forestMat, desertMat, snowMat);
            HasTerrain = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            yield return null;

            float startWorldX = coord.x * size - size * 0.5f;
            float startWorldZ = coord.y * size - size * 0.5f;
            IEnumerator scatter = ScatterBiomeResources(startWorldX, startWorldZ, size, isLowLOD);
            while (scatter.MoveNext()) yield return null;
        }

        private void BuildTerrain(Vector2Int coord, float size, int resolution, bool isLowLOD, Material steppeMat, Material forestMat, Material desertMat, Material snowMat)
        {
            EnsureComponents();
            float startWorldX = coord.x * size - size * 0.5f;
            float startWorldZ = coord.y * size - size * 0.5f;
            transform.position = new Vector3(startWorldX, 0f, startWorldZ);

            if (isLowLOD)
            {
                resolution = Mathf.Max(8, resolution / 2);
            }

            if (sharedChunkMesh == null)
            {
                sharedChunkMesh = new Mesh();
                sharedChunkMesh.name = $"ChunkMesh_{coord.x}_{coord.y}";
            }
            else
            {
                sharedChunkMesh.Clear();
            }

            int numVertices = (resolution + 1) * (resolution + 1);
            Vector3[] vertices = new Vector3[numVertices];
            Vector2[] uvs = new Vector2[numVertices];
            Color[] colors = new Color[numVertices];

            float step = size / resolution;

            // Determine dominant biome of the chunk center
            Vector3 chunkCenter = new Vector3(coord.x * size, 0f, coord.y * size);
            MainBiome = InfiniteWorldManager.Instance != null 
                ? InfiniteWorldManager.Instance.GetBiomeAtWorldPos(chunkCenter) 
                : BiomeType.Steppe;

            for (int z = 0; z <= resolution; z++)
            {
                for (int x = 0; x <= resolution; x++)
                {
                    int index = z * (resolution + 1) + x;
                    float worldX = startWorldX + (x * step);
                    float worldZ = startWorldZ + (z * step);

                    float height = InfiniteWorldManager.GetSampledTerrainHeight(worldX, worldZ);
                    vertices[index] = new Vector3(x * step, height, z * step);
                    uvs[index] = new Vector2((float)x / resolution * 8f, (float)z / resolution * 8f);

                    colors[index] = GetBlendedBiomeColor(worldX, worldZ, height);
                }
            }

            int[] triangles = new int[resolution * resolution * 6];
            int triIndex = 0;

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int current = z * (resolution + 1) + x;
                    int next = current + resolution + 1;

                    triangles[triIndex++] = current;
                    triangles[triIndex++] = next;
                    triangles[triIndex++] = current + 1;

                    triangles[triIndex++] = current + 1;
                    triangles[triIndex++] = next;
                    triangles[triIndex++] = next + 1;
                }
            }

            sharedChunkMesh.vertices = vertices;
            sharedChunkMesh.uv = uvs;
            sharedChunkMesh.colors = colors;
            sharedChunkMesh.triangles = triangles;
            sharedChunkMesh.RecalculateNormals();
            sharedChunkMesh.RecalculateBounds();

            meshFilter.mesh = sharedChunkMesh;
            meshCollider.sharedMesh = sharedChunkMesh;

            // Assign Material based on dominant Biome
            meshRenderer.sharedMaterial = GetBiomeMaterial(MainBiome, steppeMat, forestMat, desertMat, snowMat);
        }

        /// <summary>Per-vertex biome colour (smoothly blended); used as the grass tint by the terrain shader.</summary>
        public static Color GetBlendedBiomeColor(float worldX, float worldZ, float height)
        {
            Color steppeColor = new Color(0.35f, 0.55f, 0.22f); // lush steppe meadow
            Color forestColor = new Color(0.20f, 0.38f, 0.16f); // conifer forest floor
            Color desertColor = new Color(0.74f, 0.64f, 0.40f); // sand
            Color highlandColor = new Color(0.42f, 0.48f, 0.32f); // alpine meadow (snow and rock come from the shader)

            float tempNoise = Mathf.PerlinNoise(worldX * 0.003f + 500f, worldZ * 0.003f + 500f);
            float moistureNoise = Mathf.PerlinNoise(worldX * 0.004f + 1500f, worldZ * 0.004f + 1500f);

            // 1. Base Steppe & Pine Forest smooth blend based on moisture
            float forestWeight = SmoothRange(0.35f, 0.65f, moistureNoise);
            Color baseColor = Color.Lerp(steppeColor, forestColor, forestWeight);

            // 2. Desert blend based on high temperature & low moisture
            float desertWeight = SmoothRange(0.55f, 0.75f, tempNoise) * (1f - SmoothRange(0.3f, 0.5f, moistureNoise));
            baseColor = Color.Lerp(baseColor, desertColor, desertWeight);

            // 3. Snow blend based on height elevation
            float highlandWeight = SmoothRange(SnowLineHeight - 20f, SnowLineHeight, height);
            baseColor = Color.Lerp(baseColor, highlandColor, highlandWeight);

            return baseColor;
        }

        private Material GetBiomeMaterial(BiomeType biome, Material steppeMat, Material forestMat, Material desertMat, Material snowMat)
        {
            switch (biome)
            {
                case BiomeType.PineForest: return forestMat ?? steppeMat;
                case BiomeType.Desert: return desertMat ?? steppeMat;
                case BiomeType.SnowyPeaks: return snowMat ?? steppeMat;
                case BiomeType.Steppe:
                default: return steppeMat;
            }
        }

        private IEnumerator ScatterBiomeResources(float startX, float startZ, float size, bool isLowLOD)
        {
            int seed = Mathf.Abs((ChunkCoord.x * 73856093) ^ (ChunkCoord.y * 19349663));
            Random.InitState(seed);

            int baseTreeCount = (MainBiome == BiomeType.PineForest) ? 26 : (MainBiome == BiomeType.Steppe ? 7 : (MainBiome == BiomeType.SnowyPeaks ? 4 : 2));
            // Share of conifers: the conifer-forest biome is spruce, the steppe keeps its broadleaf trees.
            float coniferShare = (MainBiome == BiomeType.PineForest) ? 0.9f : (MainBiome == BiomeType.Steppe ? 0.1f : (MainBiome == BiomeType.SnowyPeaks ? 1f : 0.1f));
            int basePebbleCount = (MainBiome == BiomeType.Desert) ? 12 : 5;

            int treeCount = isLowLOD ? Mathf.Max(1, baseTreeCount / 2) : baseTreeCount;
            int pebbleCount = isLowLOD ? Mathf.Max(1, basePebbleCount / 2) : basePebbleCount;

            EnvironmentPoolManager pool = EnvironmentPoolManager.Instance;

            // Trees
            for (int i = 0; i < treeCount; i++)
            {
                float localX = Random.Range(4f, size - 4f);
                float localZ = Random.Range(4f, size - 4f);
                float worldX = startX + localX;
                float worldZ = startZ + localZ;

                if (Vector2.Distance(new Vector2(worldX, worldZ), Vector2.zero) < 12f) continue;

                float y = InfiniteWorldManager.GetSampledTerrainHeight(worldX, worldZ);
                if (y > SnowLineHeight + 4f) continue; // Don't spawn trees on high snow peaks

                // No trees on cliffs (rise over 2 m of more than ~1.3 m = steeper than ~33 degrees)
                float rise = Mathf.Max(
                    Mathf.Abs(InfiniteWorldManager.GetSampledTerrainHeight(worldX + 2f, worldZ) - y),
                    Mathf.Abs(InfiniteWorldManager.GetSampledTerrainHeight(worldX, worldZ + 2f) - y));
                bool conifer = Random.value < coniferShare || y > SnowLineHeight - 16f; // only spruce up high
                if (rise > 1.3f) continue;

                Vector3 treePos = new Vector3(worldX, y - 0.15f, worldZ);
                float scaleVal = conifer ? Random.Range(0.8f, 1.5f) : Random.Range(0.85f, 1.35f);
                Vector3 treeScale = new Vector3(scaleVal, scaleVal, scaleVal);
                Quaternion treeRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

                GameObject tree = (pool != null)
                    ? pool.GetTree(transform, treePos, treeRot, treeScale, i, conifer)
                    : (conifer
                        ? SteppeTreeBuilder.CreatePineTree($"Pine_{ChunkCoord.x}_{ChunkCoord.y}_{i}", i)
                        : SteppeTreeBuilder.CreateSteppeTree($"Tree_{ChunkCoord.x}_{ChunkCoord.y}_{i}", i));

                if (pool == null)
                {
                    tree.transform.SetParent(transform, false);
                    tree.transform.position = treePos;
                    tree.transform.localScale = treeScale;
                    tree.transform.rotation = treeRot;
                }

                activeTrees.Add(tree);
                WorldPersistence.RegisterTree(tree.GetComponent<ResourceNodeTree>(), ChunkCoord.x + "," + ChunkCoord.y + ":" + i);
                yield return null;
            }

            // Ore Pebbles
            for (int i = 0; i < pebbleCount; i++)
            {
                float localX = Random.Range(4f, size - 4f);
                float localZ = Random.Range(4f, size - 4f);
                float worldX = startX + localX;
                float worldZ = startZ + localZ;

                float y = InfiniteWorldManager.GetSampledTerrainHeight(worldX, worldZ);
                Vector3 pebblePos = new Vector3(worldX, y, worldZ);

                GameObject pebble = (pool != null)
                    ? pool.GetPebble(transform, pebblePos)
                    : OrePebbleNode.CreatePebblePrefab($"Pebble_{ChunkCoord.x}_{ChunkCoord.y}_{i}");

                if (pool == null)
                {
                    pebble.transform.SetParent(transform, false);
                    pebble.transform.position = pebblePos;
                }

                activePebbles.Add(pebble);
                yield return null;
            }

            // Wildlife Spawning (Deer, Boar, Rabbit)
            if (!isLowLOD && (ChunkCoord.x != 0 || ChunkCoord.y != 0))
            {
                int animalCount = (MainBiome == BiomeType.Steppe || MainBiome == BiomeType.PineForest) ? Random.Range(1, 3) : (Random.value > 0.6f ? 1 : 0);
                for (int a = 0; a < animalCount; a++)
                {
                    float localX = Random.Range(6f, size - 6f);
                    float localZ = Random.Range(6f, size - 6f);
                    float worldX = startX + localX;
                    float worldZ = startZ + localZ;
                    float y = InfiniteWorldManager.GetSampledTerrainHeight(worldX, worldZ);

                    AI.AnimalType type = (MainBiome == BiomeType.PineForest) ? AI.AnimalType.Boar :
                                         (MainBiome == BiomeType.Desert) ? AI.AnimalType.Rabbit :
                                         (Random.value > 0.5f ? AI.AnimalType.Deer : AI.AnimalType.Rabbit);

                    GameObject animal = Utils.AnimalModelBuilder.CreateAnimalPrefab(type, $"Wild_{type}_{ChunkCoord.x}_{ChunkCoord.y}_{a}");
                    animal.transform.SetParent(transform, false);
                    animal.transform.position = new Vector3(worldX, y, worldZ);
                    animal.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    if (WorldPersistence.RegisterAnimal(animal)) activeEntities.Add(animal);
                    yield return null;
                }
            }

            // Hostile Raider Camp Spawning (At distance >= 2 chunks from spawn)
            if (!isLowLOD && (Mathf.Abs(ChunkCoord.x) >= 2 || Mathf.Abs(ChunkCoord.y) >= 2) && (seed % 5 == 0))
            {
                float campX = startX + size * 0.5f;
                float campZ = startZ + size * 0.5f;
                float campY = InfiniteWorldManager.GetSampledTerrainHeight(campX, campZ);

                if (campY < SnowLineHeight - 10f)
                {
                    GameObject camp = new GameObject($"HostileCamp_{ChunkCoord.x}_{ChunkCoord.y}");
                    camp.transform.SetParent(transform);
                    buildingCamp = camp; // until complete it is destroyed on release without being recorded
                    IEnumerator build = Utils.HostileCampBuilder.BuildHostileCamp(camp, new Vector3(campX, campY, campZ));
                    while (build.MoveNext()) yield return null;
                    buildingCamp = null;
                    WorldPersistence.RegisterCamp(camp);
                    activeEntities.Add(camp);
                }
            }
        }

        private readonly List<GameObject> activeEntities = new List<GameObject>();
        private GameObject buildingCamp;

        /// <summary>Clears the chunk for the pool: stops any generation in progress and returns its content.</summary>
        public void ReleaseChunkResources()
        {
            steps = null;
            HasTerrain = false;
            ReleaseChunkResources(true);
        }

        /// <summary>returnToPool = false while the chunk itself is destroyed (scene unload): the pool may be going away too.</summary>
        private void ReleaseChunkResources(bool returnToPool)
        {
            EnvironmentPoolManager pool = returnToPool ? EnvironmentPoolManager.Instance : null;

            for (int i = 0; i < activeTrees.Count; i++)
            {
                if (activeTrees[i] != null)
                {
                    WorldPersistence.RecordTree(activeTrees[i].GetComponent<ResourceNodeTree>());
                    if (pool != null) pool.ReturnTree(activeTrees[i]);
                    else Destroy(activeTrees[i]);
                }
            }
            activeTrees.Clear();

            for (int i = 0; i < activePebbles.Count; i++)
            {
                if (activePebbles[i] != null)
                {
                    if (pool != null) pool.ReturnPebble(activePebbles[i]);
                    else Destroy(activePebbles[i]);
                }
            }
            activePebbles.Clear();

            for (int i = 0; i < activeEntities.Count; i++)
            {
                if (activeEntities[i] != null)
                {
                    if (activeEntities[i].name.StartsWith("HostileCamp_")) WorldPersistence.RecordCamp(activeEntities[i]);
                    Destroy(activeEntities[i]);
                }
            }
            activeEntities.Clear();

            if (buildingCamp != null) Destroy(buildingCamp);
            buildingCamp = null;
        }

        private void OnDestroy()
        {
            ReleaseChunkResources(false);
        }
    }
}
