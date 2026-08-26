using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    public static class ProceduralMeshBuilder
    {
        // ----------------------------------------------------
        // 1. Faceted Stylized Boulder & Ore Mesh
        // ----------------------------------------------------
        public static Mesh CreateFacetedRockMesh(float radius = 1.0f, int subdivisions = 2, int seed = 0, float roughness = 0.35f)
        {
            Random.InitState(seed);
            Mesh baseIco = CreateIcoSphere(radius, subdivisions);
            Vector3[] origVerts = baseIco.vertices;
            int[] origTris = baseIco.triangles;

            Vector3[] displacedVerts = new Vector3[origVerts.Length];
            for (int i = 0; i < origVerts.Length; i++)
            {
                Vector3 v = origVerts[i];
                float noise = Mathf.PerlinNoise(v.x * 2.2f + seed, v.z * 2.2f + seed) * 2f - 1f;
                float verticalSquash = v.y > 0 ? 0.85f : 0.6f;
                displacedVerts[i] = new Vector3(
                    v.x * (1f + noise * roughness),
                    v.y * verticalSquash * (1f + noise * roughness * 0.5f),
                    v.z * (1f + noise * roughness)
                );
            }

            int numTris = origTris.Length / 3;
            Vector3[] flatVerts = new Vector3[numTris * 3];
            Vector3[] flatNormals = new Vector3[numTris * 3];
            Vector2[] flatUVs = new Vector2[numTris * 3];
            Color[] flatColors = new Color[numTris * 3];
            int[] flatTris = new int[numTris * 3];

            for (int t = 0; t < numTris; t++)
            {
                int i0 = origTris[t * 3 + 0];
                int i1 = origTris[t * 3 + 1];
                int i2 = origTris[t * 3 + 2];

                Vector3 v0 = displacedVerts[i0];
                Vector3 v1 = displacedVerts[i1];
                Vector3 v2 = displacedVerts[i2];

                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                float mossFactor = Mathf.Clamp01(Vector3.Dot(normal, Vector3.up) * 1.6f - 0.3f);
                Color faceColor = Color.Lerp(new Color(0.48f, 0.46f, 0.44f), new Color(0.35f, 0.52f, 0.18f), mossFactor);

                for (int v = 0; v < 3; v++)
                {
                    int index = t * 3 + v;
                    flatVerts[index] = (v == 0) ? v0 : ((v == 1) ? v1 : v2);
                    flatNormals[index] = normal;
                    flatUVs[index] = new Vector2(flatVerts[index].x + flatVerts[index].y, flatVerts[index].z + flatVerts[index].y);
                    flatColors[index] = faceColor;
                    flatTris[index] = index;
                }
            }

            Mesh flatMesh = new Mesh();
            flatMesh.name = "FacetedRock";
            flatMesh.vertices = flatVerts;
            flatMesh.normals = flatNormals;
            flatMesh.uv = flatUVs;
            flatMesh.colors = flatColors;
            flatMesh.triangles = flatTris;
            flatMesh.RecalculateBounds();
            return flatMesh;
        }

        // ----------------------------------------------------
        // 2. Stylized Foliage Cloud Cluster Mesh
        // ----------------------------------------------------
        public static Mesh CreateStylizedFoliageCluster(Vector3 scale, int seed = 0, Color? bottomAO = null, Color? topHighlight = null)
        {
            Random.InitState(seed);
            Mesh baseIco = CreateIcoSphere(1.0f, 2);
            Vector3[] origVerts = baseIco.vertices;
            int[] origTris = baseIco.triangles;

            Vector3[] displaced = new Vector3[origVerts.Length];
            for (int i = 0; i < origVerts.Length; i++)
            {
                Vector3 v = origVerts[i];
                float noise = (Mathf.PerlinNoise(v.x * 2.5f + seed, v.y * 2.5f + seed) - 0.5f) * 0.35f;
                displaced[i] = new Vector3(
                    v.x * scale.x * (1f + noise),
                    v.y * scale.y * (1f + noise),
                    v.z * scale.z * (1f + noise)
                );
            }

            int numTris = origTris.Length / 3;
            Vector3[] flatVerts = new Vector3[numTris * 3];
            Vector3[] flatNormals = new Vector3[numTris * 3];
            Vector2[] flatUVs = new Vector2[numTris * 3];
            Color[] flatColors = new Color[numTris * 3];
            int[] flatTris = new int[numTris * 3];

            for (int t = 0; t < numTris; t++)
            {
                int i0 = origTris[t * 3 + 0];
                int i1 = origTris[t * 3 + 1];
                int i2 = origTris[t * 3 + 2];

                Vector3 v0 = displaced[i0];
                Vector3 v1 = displaced[i1];
                Vector3 v2 = displaced[i2];

                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                for (int v = 0; v < 3; v++)
                {
                    int index = t * 3 + v;
                    flatVerts[index] = (v == 0) ? v0 : ((v == 1) ? v1 : v2);
                    flatNormals[index] = normal;
                    flatUVs[index] = new Vector2(flatVerts[index].x * 0.5f, flatVerts[index].z * 0.5f);
                    flatColors[index] = Color.white;
                    flatTris[index] = index;
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "StylizedFoliageCluster";
            mesh.vertices = flatVerts;
            mesh.normals = flatNormals;
            mesh.uv = flatUVs;
            mesh.colors = flatColors;
            mesh.triangles = flatTris;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ----------------------------------------------------
        // 3. Stylized Tapered & Flared Tree Trunk Mesh
        // ----------------------------------------------------
        public static Mesh CreateStylizedTrunkMesh(float baseRadius = 0.45f, float topRadius = 0.22f, float height = 3.2f, int sides = 7, int verticalSegments = 5, int seed = 0)
        {
            Random.InitState(seed);
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int> triangles = new List<int>();

            float[] segHeights = new float[verticalSegments + 1];
            float[] segRadii = new float[verticalSegments + 1];

            for (int s = 0; s <= verticalSegments; s++)
            {
                float t = (float)s / verticalSegments;
                segHeights[s] = t * height;
                float flare = (1f - t) * (1f - t) * 0.35f;
                segRadii[s] = Mathf.Lerp(baseRadius, topRadius, t) + flare;
            }

            for (int s = 0; s < verticalSegments; s++)
            {
                float y0 = segHeights[s];
                float y1 = segHeights[s + 1];
                float r0 = segRadii[s];
                float r1 = segRadii[s + 1];

                for (int i = 0; i < sides; i++)
                {
                    float angle0 = (float)i / sides * Mathf.PI * 2f;
                    float angle1 = (float)(i + 1) / sides * Mathf.PI * 2f;

                    float rootNoise0 = (s == 0) ? (Mathf.Sin(angle0 * 3f + seed) * 0.10f) : 0f;
                    float rootNoise1 = (s == 0) ? (Mathf.Sin(angle1 * 3f + seed) * 0.10f) : 0f;

                    Vector3 p00 = new Vector3(Mathf.Cos(angle0) * (r0 + rootNoise0), y0, Mathf.Sin(angle0) * (r0 + rootNoise0));
                    Vector3 p10 = new Vector3(Mathf.Cos(angle1) * (r1 + rootNoise1), y0, Mathf.Sin(angle1) * (r1 + rootNoise1));
                    Vector3 p01 = new Vector3(Mathf.Cos(angle0) * r1, y1, Mathf.Sin(angle0) * r1);
                    Vector3 p11 = new Vector3(Mathf.Cos(angle1) * r1, y1, Mathf.Sin(angle1) * r1);

                    Vector3 n1 = Vector3.Cross(p01 - p00, p10 - p00).normalized;
                    Vector3 n2 = Vector3.Cross(p11 - p10, p01 - p10).normalized;

                    int baseIdx = vertices.Count;

                    vertices.Add(p00); vertices.Add(p01); vertices.Add(p10);
                    normals.Add(n1); normals.Add(n1); normals.Add(n1);
                    uvs.Add(new Vector2(0, y0)); uvs.Add(new Vector2(0, y1)); uvs.Add(new Vector2(1, y0));
                    colors.Add(Color.white); colors.Add(Color.white); colors.Add(Color.white);
                    triangles.Add(baseIdx); triangles.Add(baseIdx + 1); triangles.Add(baseIdx + 2);

                    baseIdx = vertices.Count;
                    vertices.Add(p10); vertices.Add(p01); vertices.Add(p11);
                    normals.Add(n2); normals.Add(n2); normals.Add(n2);
                    uvs.Add(new Vector2(1, y0)); uvs.Add(new Vector2(0, y1)); uvs.Add(new Vector2(1, y1));
                    colors.Add(Color.white); colors.Add(Color.white); colors.Add(Color.white);
                    triangles.Add(baseIdx); triangles.Add(baseIdx + 1); triangles.Add(baseIdx + 2);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "StylizedTrunk";
            mesh.vertices = vertices.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.colors = colors.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ----------------------------------------------------
        // 4. Stylized 3D Grass & Flower Tufts Mesh
        // ----------------------------------------------------
        public static Mesh CreateGrassTuftMesh(float width = 0.75f, float height = 0.65f, int bladePlanes = 4, int seed = 0)
        {
            Random.InitState(seed);
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            Color rootCol = new Color(0.24f, 0.44f, 0.16f);
            Color tipCol = new Color(0.48f, 0.72f, 0.24f);

            for (int p = 0; p < bladePlanes; p++)
            {
                float angle = (float)p / bladePlanes * Mathf.PI + Random.Range(-0.15f, 0.15f);
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vector3 right = new Vector3(-dir.z, 0, dir.x) * (width * 0.45f);

                Vector3 curveOffset = dir * Random.Range(0.10f, 0.22f);
                float bladeHeight = height * Random.Range(0.85f, 1.15f);

                Vector3 bL = -right;
                Vector3 bR = right;
                Vector3 tL = -right * 0.4f + Vector3.up * (bladeHeight * 0.65f) + curveOffset * 0.5f;
                Vector3 tR = right * 0.4f + Vector3.up * (bladeHeight * 0.65f) + curveOffset * 0.5f;
                Vector3 tip = Vector3.up * bladeHeight + curveOffset;

                int baseIdx = verts.Count;
                verts.Add(bL); verts.Add(tL); verts.Add(bR);
                verts.Add(bR); verts.Add(tL); verts.Add(tR);
                verts.Add(tL); verts.Add(tip); verts.Add(tR);

                for (int i = 0; i < 9; i++) norms.Add(Vector3.up);

                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, 0.7f)); uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 0.7f)); uvs.Add(new Vector2(1, 0.7f));
                uvs.Add(new Vector2(0, 0.7f)); uvs.Add(new Vector2(0.5f, 1f)); uvs.Add(new Vector2(1, 0.7f));

                cols.Add(rootCol); cols.Add(tipCol); cols.Add(rootCol);
                cols.Add(rootCol); cols.Add(tipCol); cols.Add(tipCol);
                cols.Add(tipCol); cols.Add(tipCol); cols.Add(tipCol);

                for (int i = 0; i < 9; i++) tris.Add(baseIdx + i);

                int backBase = verts.Count;
                for (int i = 0; i < 9; i++)
                {
                    verts.Add(verts[baseIdx + i]);
                    norms.Add(Vector3.up);
                    uvs.Add(uvs[baseIdx + i]);
                    cols.Add(cols[baseIdx + i]);
                }
                tris.Add(backBase + 2); tris.Add(backBase + 1); tris.Add(backBase + 0);
                tris.Add(backBase + 5); tris.Add(backBase + 4); tris.Add(backBase + 3);
                tris.Add(backBase + 8); tris.Add(backBase + 7); tris.Add(backBase + 6);
            }

            Mesh mesh = new Mesh();
            mesh.name = "GrassTuft";
            mesh.vertices = verts.ToArray();
            mesh.normals = norms.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.colors = cols.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateIcoSphere(float radius, int subdivisions)
        {
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();
            Dictionary<long, int> middlePointIndexCache = new Dictionary<long, int>();

            float t = (1.0f + Mathf.Sqrt(5.0f)) / 2.0f;

            verts.Add(new Vector3(-1, t, 0).normalized * radius);
            verts.Add(new Vector3(1, t, 0).normalized * radius);
            verts.Add(new Vector3(-1, -t, 0).normalized * radius);
            verts.Add(new Vector3(1, -t, 0).normalized * radius);

            verts.Add(new Vector3(0, -1, t).normalized * radius);
            verts.Add(new Vector3(0, 1, t).normalized * radius);
            verts.Add(new Vector3(0, -1, -t).normalized * radius);
            verts.Add(new Vector3(0, 1, -t).normalized * radius);

            verts.Add(new Vector3(t, 0, -1).normalized * radius);
            verts.Add(new Vector3(t, 0, 1).normalized * radius);
            verts.Add(new Vector3(-t, 0, -1).normalized * radius);
            verts.Add(new Vector3(-t, 0, 1).normalized * radius);

            int[] initialTris = new int[] {
                0, 11, 5,   0, 5, 1,   0, 1, 7,   0, 7, 10,   0, 10, 11,
                1, 5, 9,    5, 11, 4,  11, 10, 2, 10, 7, 6,   7, 1, 8,
                3, 9, 4,    3, 4, 2,   3, 2, 6,   3, 6, 8,    3, 8, 9,
                4, 9, 5,    2, 4, 11,  6, 2, 10,  8, 6, 7,    9, 8, 1
            };
            tris.AddRange(initialTris);

            for (int s = 0; s < subdivisions; s++)
            {
                List<int> subTris = new List<int>();
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int v1 = tris[i];
                    int v2 = tris[i + 1];
                    int v3 = tris[i + 2];

                    int a = GetMiddlePoint(v1, v2, verts, middlePointIndexCache, radius);
                    int b = GetMiddlePoint(v2, v3, verts, middlePointIndexCache, radius);
                    int c = GetMiddlePoint(v3, v1, verts, middlePointIndexCache, radius);

                    subTris.Add(v1); subTris.Add(a); subTris.Add(c);
                    subTris.Add(v2); subTris.Add(b); subTris.Add(a);
                    subTris.Add(v3); subTris.Add(c); subTris.Add(b);
                    subTris.Add(a); subTris.Add(b); subTris.Add(c);
                }
                tris = subTris;
            }

            Mesh mesh = new Mesh();
            mesh.vertices = verts.ToArray();
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            return mesh;
        }

        private static int GetMiddlePoint(int p1, int p2, List<Vector3> verts, Dictionary<long, int> cache, float radius)
        {
            bool firstIsSmaller = p1 < p2;
            long smallerIndex = firstIsSmaller ? p1 : p2;
            long greaterIndex = firstIsSmaller ? p2 : p1;
            long key = (smallerIndex << 32) + greaterIndex;

            if (cache.TryGetValue(key, out int ret)) return ret;

            Vector3 point1 = verts[p1];
            Vector3 point2 = verts[p2];
            Vector3 middle = ((point1 + point2) / 2f).normalized * radius;

            int i = verts.Count;
            verts.Add(middle);
            cache.Add(key, i);
            return i;
        }
    }
}
