using UnityEngine;
using BattleOfAges.Utils;
using BattleOfAges.Resources;

namespace BattleOfAges.Utils
{
    public static class SteppeTerrainGenerator
    {
        public static GameObject CreateSteppeTerrain(string name = "SteppeTerrain", float size = 150f, int gridResolution = 75)
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

                    // Layered Perlin Noise for natural steppe rolling hills
                    float hillNoise = Mathf.PerlinNoise(worldX * 0.025f + 100f, worldZ * 0.025f + 100f) * 4.8f;
                    float detailNoise = Mathf.PerlinNoise(worldX * 0.08f + 500f, worldZ * 0.08f + 500f) * 1.2f;
                    float y = hillNoise + detailNoise;

                    // Flatten slightly near center origin for smooth player start
                    float distFromCenter = new Vector2(worldX, worldZ).magnitude;
                    if (distFromCenter < 12f)
                    {
                        y = Mathf.Lerp(y, 0.5f, 1f - (distFromCenter / 12f));
                    }

                    vertices[index] = new Vector3(x * step, y, z * step);
                    uvs[index] = new Vector2((float)x / gridResolution * 12f, (float)z / gridResolution * 12f);

                    // Vertex coloring for steppe grass & dirt slopes
                    float slope = Mathf.Clamp01(y / 5.0f);
                    Color grassColor = Color.Lerp(new Color(0.35f, 0.55f, 0.2f), new Color(0.6f, 0.65f, 0.25f), slope);
                    colors[index] = grassColor;
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

            // Generate a detailed repeating steppe albedo map without external placeholder textures.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material steppeMat = new Material(shader);
            Texture2D steppeTex = CreateSteppeTexture();
            steppeMat.mainTexture = steppeTex;
            steppeMat.SetTexture("_BaseMap", steppeTex);
            steppeMat.color = Color.white;
            steppeMat.SetColor("_BaseColor", Color.white);
            steppeMat.SetFloat("_Smoothness", 0.05f);
            steppeMat.SetFloat("_Glossiness", 0.05f);

            mr.sharedMaterial = steppeMat;

            // Scatter 45 Steppe Trees & 60 Ore Pebbles across hills
            ScatterSteppeTrees(terrainObj.transform, size);
            ScatterOrePebbles(terrainObj.transform, size);

            return terrainObj;
        }

        private static Texture2D CreateSteppeTexture()
        {
            const int texSize = 512;
            Texture2D tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, true)
            {
                name = "Steppe_Grass_Albedo",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8
            };
            Color[] pixels = new Color[texSize * texSize];

            Color grassBase = new Color(0.30f, 0.44f, 0.12f);
            Color dryGrass = new Color(0.60f, 0.57f, 0.18f);
            Color dirtSoil = new Color(0.30f, 0.20f, 0.095f);
            Color strawHighlight = new Color(0.79f, 0.69f, 0.31f);

            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    float noise1 = Mathf.PerlinNoise(x * 0.018f, y * 0.018f);
                    float noise2 = Mathf.PerlinNoise(x * 0.065f + 100f, y * 0.065f + 100f);
                    float fineNoise = Mathf.PerlinNoise(x * 0.24f + 300f, y * 0.24f + 300f);

                    Color blended = Color.Lerp(grassBase, dryGrass, noise1);
                    if (noise2 > 0.61f)
                    {
                        blended = Color.Lerp(blended, dirtSoil, (noise2 - 0.61f) * 2.2f);
                    }

                    float blade = Mathf.Abs(Mathf.Sin((x * 0.19f) + (y * 0.037f) + fineNoise * 5f));
                    if (blade > 0.975f && fineNoise > 0.42f)
                    {
                        blended = Color.Lerp(blended, strawHighlight, 0.22f);
                    }

                    pixels[y * texSize + x] = blended;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        private static void ScatterSteppeTrees(Transform terrainParent, float size)
        {
            GameObject treesGroup = new GameObject("SteppeTrees");
            treesGroup.transform.SetParent(terrainParent, false);

            Random.InitState(12345);
            int treeCount = 45;

            for (int i = 0; i < treeCount; i++)
            {
                float localX = Random.Range(10f, size - 10f);
                float localZ = Random.Range(10f, size - 10f);

                Vector3 worldPos = terrainParent.position + new Vector3(localX, 100f, localZ);
                
                if (Physics.Raycast(worldPos, Vector3.down, out RaycastHit hit, 200f))
                {
                    if (Vector3.Distance(hit.point, Vector3.zero) < 15f) continue;

                    GameObject tree = SteppeTreeBuilder.CreateSteppeTree($"SteppeTree_{i}", i);
                    tree.transform.SetParent(treesGroup.transform, false);
                    tree.transform.position = hit.point;

                    float randomScale = Random.Range(0.85f, 1.35f);
                    tree.transform.localScale = new Vector3(randomScale, randomScale, randomScale);
                    tree.transform.rotation = Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                }
            }
        }

        private static void ScatterOrePebbles(Transform terrainParent, float size)
        {
            GameObject pebblesGroup = new GameObject("OrePebbles");
            pebblesGroup.transform.SetParent(terrainParent, false);

            Random.InitState(54321);
            int pebbleCount = 60;

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
    }
}
