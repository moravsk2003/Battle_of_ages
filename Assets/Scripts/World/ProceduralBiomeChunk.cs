using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.Utils;

namespace BattleOfAges.World
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class ProceduralBiomeChunk : MonoBehaviour
    {
        public Vector2Int ChunkCoord { get; private set; }
        public BiomeType MainBiome { get; private set; }
        public bool IsLowLOD { get; private set; }

        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;
        private Mesh sharedChunkMesh;

        private readonly List<GameObject> activeTrees = new List<GameObject>(16);
        private readonly List<GameObject> activePebbles = new List<GameObject>(16);

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
            meshCollider = GetComponent<MeshCollider>();
        }

        public void GenerateChunk(Vector2Int coord, float size, int resolution, bool isLowLOD, Material steppeMat, Material forestMat, Material desertMat, Material snowMat)
        {
            ChunkCoord = coord;
            IsLowLOD = isLowLOD;

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

            // Populate Biome-specific resources (trees & ore pebbles) using pooling
            ReleaseChunkResources();
            ScatterBiomeResources(startWorldX, startWorldZ, size, isLowLOD);
        }

        private Color GetBlendedBiomeColor(float worldX, float worldZ, float height)
        {
            Color steppeColor = new Color(0.35f, 0.55f, 0.22f);
            Color forestColor = new Color(0.18f, 0.38f, 0.15f);
            Color desertColor = new Color(0.78f, 0.68f, 0.38f);
            Color snowColor = new Color(0.92f, 0.95f, 0.98f);

            float tempNoise = Mathf.PerlinNoise(worldX * 0.003f + 500f, worldZ * 0.003f + 500f);
            float moistureNoise = Mathf.PerlinNoise(worldX * 0.004f + 1500f, worldZ * 0.004f + 1500f);

            // 1. Base Steppe & Pine Forest smooth blend based on moisture
            float forestWeight = Mathf.SmoothStep(0.35f, 0.65f, moistureNoise);
            Color baseColor = Color.Lerp(steppeColor, forestColor, forestWeight);

            // 2. Desert blend based on high temperature & low moisture
            float desertWeight = Mathf.SmoothStep(0.55f, 0.75f, tempNoise) * (1f - Mathf.SmoothStep(0.3f, 0.5f, moistureNoise));
            baseColor = Color.Lerp(baseColor, desertColor, desertWeight);

            // 3. Snow blend based on height elevation
            float snowWeight = Mathf.SmoothStep(14f, 20f, height);
            baseColor = Color.Lerp(baseColor, snowColor, snowWeight);

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

        private void ScatterBiomeResources(float startX, float startZ, float size, bool isLowLOD)
        {
            int seed = Mathf.Abs((ChunkCoord.x * 73856093) ^ (ChunkCoord.y * 19349663));
            Random.InitState(seed);

            int baseTreeCount = (MainBiome == BiomeType.PineForest) ? 14 : (MainBiome == BiomeType.Steppe ? 6 : 2);
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
                if (y > 22f) continue; // Don't spawn trees on high snow peaks

                Vector3 treePos = new Vector3(worldX, y, worldZ);
                float scaleVal = Random.Range(0.85f, 1.35f);
                Vector3 treeScale = new Vector3(scaleVal, scaleVal, scaleVal);
                Quaternion treeRot = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

                GameObject tree = (pool != null)
                    ? pool.GetTree(transform, treePos, treeRot, treeScale, i)
                    : SteppeTreeBuilder.CreateSteppeTree($"Tree_{ChunkCoord.x}_{ChunkCoord.y}_{i}", i);

                if (pool == null)
                {
                    tree.transform.SetParent(transform, false);
                    tree.transform.position = treePos;
                    tree.transform.localScale = treeScale;
                    tree.transform.rotation = treeRot;
                }

                activeTrees.Add(tree);
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
                    activeEntities.Add(animal);
                }
            }

            // Hostile Raider Camp Spawning (At distance >= 2 chunks from spawn)
            if (!isLowLOD && (Mathf.Abs(ChunkCoord.x) >= 2 || Mathf.Abs(ChunkCoord.y) >= 2) && (seed % 5 == 0))
            {
                float campX = startX + size * 0.5f;
                float campZ = startZ + size * 0.5f;
                float campY = InfiniteWorldManager.GetSampledTerrainHeight(campX, campZ);

                if (campY < 20f)
                {
                    GameObject camp = Utils.HostileCampBuilder.CreateHostileCamp(new Vector3(campX, campY, campZ), $"HostileCamp_{ChunkCoord.x}_{ChunkCoord.y}");
                    camp.transform.SetParent(transform);
                    activeEntities.Add(camp);
                }
            }
        }

        private readonly List<GameObject> activeEntities = new List<GameObject>();

        public void ReleaseChunkResources()
        {
            EnvironmentPoolManager pool = EnvironmentPoolManager.Instance;

            for (int i = 0; i < activeTrees.Count; i++)
            {
                if (activeTrees[i] != null)
                {
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
                    Destroy(activeEntities[i]);
                }
            }
            activeEntities.Clear();
        }

        private void OnDestroy()
        {
            ReleaseChunkResources();
        }
    }
}
