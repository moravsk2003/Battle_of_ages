using UnityEngine;
using BattleOfAges.Utils;
using BattleOfAges.Resources;
using BattleOfAges.World;

namespace BattleOfAges.Utils
{
    public static class SteppeTerrainGenerator
    {
        public static GameObject CreateSteppeTerrain(string name = "SteppeTerrain", float size = 192f, int gridResolution = 96)
        {
            GameObject terrainObj = new GameObject(name);
            terrainObj.transform.position = new Vector3(-size / 2f, 0, -size / 2f);

            MeshFilter mf = terrainObj.AddComponent<MeshFilter>();
            MeshRenderer mr = terrainObj.AddComponent<MeshRenderer>();
            MeshCollider mc = terrainObj.AddComponent<MeshCollider>();

            Mesh mesh = new Mesh();
            mesh.name = "SteppeMesh";

            int numVertices = (gridResolution + 1) * (gridResolution + 1);
            Vector3[] vertices = new Vector3[numVertices];
            Vector2[] uvs = new Vector2[numVertices];
            Color[] colors = new Color[numVertices];

            float step = size / gridResolution;

            for (int z = 0; z <= gridResolution; z++)
            {
                for (int x = 0; x <= gridResolution; x++)
                {
                    int index = z * (gridResolution + 1) + x;
                    float worldX = (x * step) - size / 2f;
                    float worldZ = (z * step) - size / 2f;

                    float y = InfiniteWorldManager.GetSampledTerrainHeight(worldX, worldZ);

                    vertices[index] = new Vector3(x * step, y, z * step);
                    uvs[index] = new Vector2((float)x / gridResolution, (float)z / gridResolution);
                    colors[index] = Color.white;
                }
            }

            int[] triangles = new int[gridResolution * gridResolution * 6];
            int triIndex = 0;

            for (int z = 0; z < gridResolution; z++)
            {
                for (int x = 0; x < gridResolution; x++)
                {
                    int current = z * (gridResolution + 1) + x;
                    int next = current + gridResolution + 1;

                    triangles[triIndex++] = current;
                    triangles[triIndex++] = next;
                    triangles[triIndex++] = current + 1;

                    triangles[triIndex++] = current + 1;
                    triangles[triIndex++] = next;
                    triangles[triIndex++] = next + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            mf.mesh = mesh;
            mc.sharedMesh = mesh;

            Shader terrainShader = Shader.Find("Custom/StylizedTerrain") 
                ?? Shader.Find("Universal Render Pipeline/Lit") 
                ?? Shader.Find("Standard");

            Material terrainMat = new Material(terrainShader);
            terrainMat.name = "MAT_StylizedSteppeTerrain";
            terrainMat.SetColor("_GrassDeep", new Color(0.19f, 0.42f, 0.12f));
            terrainMat.SetColor("_GrassMid", new Color(0.28f, 0.54f, 0.16f));
            terrainMat.SetColor("_GrassLight", new Color(0.44f, 0.68f, 0.22f));
            terrainMat.SetColor("_FlowerYellow", new Color(0.96f, 0.84f, 0.24f));
            terrainMat.SetColor("_FlowerBlue", new Color(0.38f, 0.65f, 0.92f));
            terrainMat.SetColor("_FlowerRed", new Color(0.92f, 0.24f, 0.26f));
            terrainMat.SetColor("_DirtColor", new Color(0.35f, 0.24f, 0.14f));
            terrainMat.SetColor("_RockColor", new Color(0.45f, 0.43f, 0.40f));

            mr.sharedMaterial = terrainMat;

            // Scatter Vegetation & Environment Props
            Scatter3DGrassAndFlowers(terrainObj.transform, size);
            ScatterSteppeTrees(terrainObj.transform, size);
            ScatterOrePebbles(terrainObj.transform, size);
            ScatterFacetedBoulders(terrainObj.transform, size);
            ScatterCampDecorations(terrainObj.transform);

            return terrainObj;
        }

        private static void Scatter3DGrassAndFlowers(Transform terrainParent, float size)
        {
            GameObject grassGroup = new GameObject("3D_Vegetation");
            grassGroup.transform.SetParent(terrainParent, false);

            Random.InitState(4242);
            int tuftCount = 300;

            Material grassMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Grass, new Color(0.34f, 0.58f, 0.18f));
            Material yellowFlowerMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, new Color(0.98f, 0.85f, 0.22f), 0f, 0.3f);
            Material redFlowerMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, new Color(0.92f, 0.22f, 0.25f), 0f, 0.3f);
            Material blueFlowerMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, new Color(0.32f, 0.62f, 0.92f), 0f, 0.3f);

            Mesh tuftMesh = ProceduralMeshBuilder.CreateGrassTuftMesh(0.85f, 0.70f, 3, 101);

            for (int i = 0; i < tuftCount; i++)
            {
                float localX = Random.Range(6f, size - 6f);
                float localZ = Random.Range(6f, size - 6f);
                Vector3 worldPos = terrainParent.position + new Vector3(localX, 100f, localZ);

                if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 200f))
                {
                    if (Vector3.Distance(hit.point, new Vector3(1.5f, 0f, 3.5f)) < 3.2f) continue;

                    GameObject tuft = new GameObject($"GrassTuft_{i}");
                    tuft.transform.SetParent(grassGroup.transform, false);
                    tuft.transform.position = hit.point;
                    float scale = Random.Range(0.75f, 1.35f);
                    tuft.transform.localScale = new Vector3(scale, scale, scale);
                    tuft.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);

                    MeshFilter mf = tuft.AddComponent<MeshFilter>();
                    mf.mesh = tuftMesh;
                    MeshRenderer mr = tuft.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = grassMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = true;

                    if (i % 3 == 0)
                    {
                        GameObject flower = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        flower.name = "FlowerBlossom";
                        flower.transform.SetParent(tuft.transform, false);
                        flower.transform.localPosition = new Vector3(Random.Range(-0.12f, 0.12f), 0.68f, Random.Range(-0.12f, 0.12f));
                        flower.transform.localScale = new Vector3(0.18f, 0.12f, 0.18f);

                        Material fMat = (i % 9 == 0) ? redFlowerMat : ((i % 6 == 0) ? blueFlowerMat : yellowFlowerMat);
                        flower.GetComponent<MeshRenderer>().sharedMaterial = fMat;
                        Collider fc = flower.GetComponent<Collider>();
                        if (fc != null) Object.DestroyImmediate(fc);
                    }
                }
            }
        }

        private static void ScatterSteppeTrees(Transform terrainParent, float size)
        {
            GameObject treesGroup = new GameObject("SteppeTrees");
            treesGroup.transform.SetParent(terrainParent, false);

            // 1. Guaranteed Camp Grove Trees (immediately accessible to player at spawn)
            Vector3[] campTreePositions = {
                new Vector3(7.5f, 0f, 7.5f),
                new Vector3(-8.5f, 0f, 7.0f),
                new Vector3(8.0f, 0f, -6.5f),
                new Vector3(-7.5f, 0f, -7.5f),
                new Vector3(12.0f, 0f, 1.0f)
            };

            for (int t = 0; t < campTreePositions.Length; t++)
            {
                Vector3 pos = campTreePositions[t];
                pos.y = BattleOfAges.RTS.RtsUnit.GetTerrainHeight(pos);
                GameObject tree = SteppeTreeBuilder.CreateSteppeTree($"CampGroveTree_{t + 1}", t);
                tree.transform.SetParent(treesGroup.transform, false);
                tree.transform.position = pos;
                tree.transform.localScale = Vector3.one * 1.15f;
                tree.transform.rotation = Quaternion.Euler(0, t * 72f, 0);
            }

            // 2. Wilderness Trees
            Random.InitState(12345);
            int treeCount = 40;

            for (int i = 0; i < treeCount; i++)
            {
                float localX = Random.Range(10f, size - 10f);
                float localZ = Random.Range(10f, size - 10f);

                Vector3 worldPos = terrainParent.position + new Vector3(localX, 100f, localZ);
                
                if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 200f))
                {
                    if (Vector3.Distance(hit.point, Vector3.zero) < 12f) continue;

                    GameObject tree = SteppeTreeBuilder.CreateSteppeTree($"SteppeTree_{i}", i);
                    tree.transform.SetParent(treesGroup.transform, false);
                    tree.transform.position = hit.point;

                    float randomScale = Random.Range(0.95f, 1.45f);
                    tree.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
                    tree.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                }
            }
        }

        private static void ScatterOrePebbles(Transform terrainParent, float size)
        {
            GameObject pebblesGroup = new GameObject("OrePebbles");
            pebblesGroup.transform.SetParent(terrainParent, false);

            // Camp pebbles for immediate mining test
            Vector3[] campPebbles = {
                new Vector3(4.0f, 0f, 6.0f),
                new Vector3(-4.5f, 0f, 5.5f),
                new Vector3(5.5f, 0f, -4.5f)
            };

            for (int cp = 0; cp < campPebbles.Length; cp++)
            {
                Vector3 pos = campPebbles[cp];
                pos.y = BattleOfAges.RTS.RtsUnit.GetTerrainHeight(pos);
                GameObject pebble = OrePebbleNode.CreatePebblePrefab($"CampOrePebble_{cp + 1}");
                pebble.transform.SetParent(pebblesGroup.transform, false);
                pebble.transform.position = pos;
            }

            Random.InitState(54321);
            int pebbleCount = 50;

            for (int i = 0; i < pebbleCount; i++)
            {
                float localX = Random.Range(8f, size - 8f);
                float localZ = Random.Range(8f, size - 8f);

                Vector3 worldPos = terrainParent.position + new Vector3(localX, 100f, localZ);
                
                if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 200f))
                {
                    GameObject pebble = OrePebbleNode.CreatePebblePrefab($"OrePebble_{i}");
                    pebble.transform.SetParent(pebblesGroup.transform, false);
                    pebble.transform.position = hit.point;
                }
            }
        }

        private static void ScatterFacetedBoulders(Transform terrainParent, float size)
        {
            GameObject bouldersGroup = new GameObject("StylizedBoulders");
            bouldersGroup.transform.SetParent(terrainParent, false);

            Random.InitState(9876);
            int boulderCount = 20;
            Material stoneMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.46f, 0.44f, 0.42f), 0.05f, 0.25f);

            Mesh[] boulderMeshes = new Mesh[4];
            for (int b = 0; b < 4; b++)
            {
                boulderMeshes[b] = ProceduralMeshBuilder.CreateFacetedRockMesh(1.0f, 2, b * 77, 0.4f);
                boulderMeshes[b].name = $"FacetedBoulder_{b}";
            }

            for (int i = 0; i < boulderCount; i++)
            {
                float localX = Random.Range(12f, size - 12f);
                float localZ = Random.Range(12f, size - 12f);
                Vector3 worldPos = terrainParent.position + new Vector3(localX, 100f, localZ);

                if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 200f))
                {
                    if (Vector3.Distance(hit.point, Vector3.zero) < 12f) continue;

                    GameObject boulder = new GameObject($"Boulder_{i}");
                    boulder.transform.SetParent(bouldersGroup.transform, false);
                    boulder.transform.position = hit.point + Vector3.down * 0.15f;
                    float scale = Random.Range(1.2f, 2.6f);
                    boulder.transform.localScale = new Vector3(scale, scale * 0.75f, scale * 1.1f);
                    boulder.transform.rotation = Quaternion.Euler(Random.Range(-5f, 5f), Random.Range(0f, 360f), Random.Range(-5f, 5f));

                    MeshFilter mf = boulder.AddComponent<MeshFilter>();
                    Mesh sharedBoulder = boulderMeshes[i % 4];
                    mf.sharedMesh = sharedBoulder;
                    MeshRenderer mr = boulder.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = stoneMat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    mr.receiveShadows = true;

                    MeshCollider mc = boulder.AddComponent<MeshCollider>();
                    mc.sharedMesh = sharedBoulder;
                }
            }
        }

        private static void ScatterCampDecorations(Transform terrainParent)
        {
            GameObject decoGroup = new GameObject("Camp_Props");
            decoGroup.transform.SetParent(terrainParent, false);

            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.32f, 0.20f, 0.10f), 0f, 0.22f);
            Material metalMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, new Color(0.25f, 0.26f, 0.28f), 0.7f, 0.5f);

            Vector3[] barrelPos = { new Vector3(2.2f, 0f, 5.0f), new Vector3(2.8f, 0f, 5.5f) };
            for (int b = 0; b < barrelPos.Length; b++)
            {
                Vector3 pos = barrelPos[b];
                pos.y = BattleOfAges.RTS.RtsUnit.GetTerrainHeight(pos) + 0.45f;

                GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                barrel.name = $"SupplyBarrel_{b + 1}";
                barrel.transform.SetParent(decoGroup.transform, false);
                barrel.transform.position = pos;
                barrel.transform.localScale = new Vector3(0.55f, 0.45f, 0.55f);
                barrel.GetComponent<MeshRenderer>().sharedMaterial = woodMat;
            }

            Vector3 cratePos = new Vector3(1.4f, 0f, 5.2f);
            cratePos.y = BattleOfAges.RTS.RtsUnit.GetTerrainHeight(cratePos) + 0.35f;
            GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "SupplyCrate_1";
            crate.transform.SetParent(decoGroup.transform, false);
            crate.transform.position = cratePos;
            crate.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
            crate.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
            crate.GetComponent<MeshRenderer>().sharedMaterial = woodMat;
        }
    }
}
