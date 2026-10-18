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

            return FlatShaded("FacetedRock", displacedVerts, origTris,
                normal => Color.Lerp(new Color(0.48f, 0.46f, 0.44f), new Color(0.35f, 0.52f, 0.18f), Mathf.Clamp01(Vector3.Dot(normal, Vector3.up) * 1.6f - 0.3f)), // moss on top
                p => new Vector2(p.x + p.y, p.z + p.y));
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

            return FlatShaded("StylizedFoliageCluster", displaced, origTris, _ => Color.white, p => new Vector2(p.x * 0.5f, p.z * 0.5f));
        }

        /// <summary>Unshares the vertices so every triangle is flat shaded; colour per face (from its normal), UV per vertex.</summary>
        private static Mesh FlatShaded(string name, Vector3[] verts, int[] tris, System.Func<Vector3, Color> faceColor, System.Func<Vector3, Vector2> uv)
        {
            int n = tris.Length;
            var flatVerts = new Vector3[n];
            var flatNormals = new Vector3[n];
            var flatUVs = new Vector2[n];
            var flatColors = new Color[n];
            var flatTris = new int[n];
            for (int t = 0; t < n; t += 3)
            {
                Vector3 v0 = verts[tris[t]], v1 = verts[tris[t + 1]], v2 = verts[tris[t + 2]];
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                Color color = faceColor(normal);
                for (int k = 0; k < 3; k++)
                {
                    int index = t + k;
                    flatVerts[index] = k == 0 ? v0 : k == 1 ? v1 : v2;
                    flatNormals[index] = normal;
                    flatUVs[index] = uv(flatVerts[index]);
                    flatColors[index] = color;
                    flatTris[index] = index;
                }
            }

            var mesh = new Mesh { name = name, vertices = flatVerts, normals = flatNormals, uv = flatUVs, colors = flatColors, triangles = flatTris };
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

        // ----------------------------------------------------
        // 4. Conifer (spruce / pine) crown
        // ----------------------------------------------------
        /// <summary>
        /// Spruce crown made of stacked drooping star-shaped tiers (branch tips hang lower than the gaps between
        /// them), faceted, with vertex colours darkening towards the inside/underside for cheap foliage AO.
        /// </summary>
        public static Mesh CreateConiferCrownMesh(float baseRadius, float crownHeight, float bottomY, int tiers, int seed)
        {
            System.Random rng = new System.Random(seed);
            List<Vector3> verts = new List<Vector3>();
            List<Color> cols = new List<Color>();
            List<int> tris = new List<int>();

            void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                cols.Add(ca); cols.Add(cb); cols.Add(cc);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }

            float R() => (float)rng.NextDouble();
            float tierStep = crownHeight / (tiers + 0.6f);

            for (int t = 0; t < tiers; t++)
            {
                float k = (float)t / Mathf.Max(1, tiers - 1);              // 0 = bottom tier, 1 = top tier
                float radius = Mathf.Lerp(baseRadius, baseRadius * 0.22f, k) * (0.92f + R() * 0.16f);
                float tierBase = bottomY + t * tierStep;
                float tierHeight = tierStep * (1.85f - k * 0.45f);
                int points = Mathf.Max(5, Mathf.RoundToInt(Mathf.Lerp(10f, 6f, k)));
                int rim = points * 2;
                float twist = R() * Mathf.PI * 2f;

                Vector3 apex = new Vector3((R() - 0.5f) * 0.08f, tierBase + tierHeight, (R() - 0.5f) * 0.08f);
                Vector3 underCenter = new Vector3(0f, tierBase + tierHeight * 0.30f, 0f);
                Vector3[] ring = new Vector3[rim];
                for (int i = 0; i < rim; i++)
                {
                    bool branchTip = i % 2 == 0;
                    float a = twist + (i + (R() - 0.5f) * 0.35f) / rim * Mathf.PI * 2f;
                    float r = branchTip ? radius * (0.88f + R() * 0.24f) : radius * (0.50f + R() * 0.10f);
                    float y = branchTip ? tierBase - tierHeight * (0.10f + R() * 0.10f) : tierBase + tierHeight * 0.10f;
                    ring[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                }

                Color top = Color.Lerp(new Color(0.92f, 0.93f, 0.92f), Color.white, k);
                Color tip = new Color(0.86f, 0.88f, 0.86f);
                Color inner = new Color(0.62f, 0.65f, 0.63f);
                for (int i = 0; i < rim; i++)
                {
                    Vector3 a0 = ring[i], a1 = ring[(i + 1) % rim];
                    Color c0 = (i % 2 == 0) ? tip : inner, c1 = ((i + 1) % 2 == 0) ? tip : inner;
                    Tri(apex, a1, a0, top, c1, c0);                                                  // outer surface
                    Tri(underCenter, a0, a1, inner * 0.8f, c0 * 0.75f, c1 * 0.75f);                    // underside
                }
            }

            Mesh mesh = new Mesh { name = "ConiferCrown" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
