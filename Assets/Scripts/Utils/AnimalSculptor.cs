using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Turns an animal's primitive "blockout" (spheres / capsules / cylinders parented to its joints) into one smooth,
    /// skinned body: the shapes become a signed distance field joined with smooth-min (so legs, neck and head flow into
    /// the body), surface nets extract a mesh, normals come from the field gradient, colours blend between parts and
    /// each vertex is skinned to the joints of the nearby parts. Small rigid details (eyes, nose, antlers, tusks) stay
    /// separate. The mesh is built once per species and shared.
    /// </summary>
    public static class AnimalSculptor
    {
        private struct Shape
        {
            public Vector3 A, B;       // segment (A == B for ellipsoids)
            public float Radius;
            public Matrix4x4 ToUnit;   // ellipsoid: world -> unit sphere space (identity for capsules)
            public bool Ellipsoid;
            public float Scale;        // ellipsoid: smallest semi-axis, to keep its distance in metres
            public int Bone;
            public Color Color;
        }

        private class Cached
        {
            public Mesh Mesh;
            public string[] BonePaths;
        }

        private static readonly Dictionary<string, Cached> Cache = new Dictionary<string, Cached>();

        /// <summary>Names of parts that stay rigid (not melted into the skin).</summary>
        private static bool IsRigid(string n) =>
            n.StartsWith("Eye") || n.StartsWith("Nose") || n.StartsWith("Beam") || n.StartsWith("Tine") || n.StartsWith("Tusk") ||
            n.StartsWith("SnoutDisc") || n.StartsWith("Inner");

        public static void Sculpt(Transform visualRoot, string speciesKey, float cellSize, float blend)
        {
            Shader shader = Shader.Find("Custom/CharacterSurface");
            if (shader == null) return;

            if (!Cache.TryGetValue(speciesKey, out Cached cached) || cached.Mesh == null)
            {
                // A mesh baked in the editor (Battle of Ages/Bake Creature Meshes) avoids a ~1 s freeze on first spawn.
                string meshName = "Blockout_" + speciesKey + "_" + Signature(visualRoot, cellSize, blend);
                var loadTimer = System.Diagnostics.Stopwatch.StartNew();
                Mesh baked = UnityEngine.Resources.Load<Mesh>(SdfCreature.BakedFolder + "/" + meshName);
                if (loadTimer.ElapsedMilliseconds > 5) Debug.Log($"[Creatures] loaded {meshName} in {loadTimer.ElapsedMilliseconds} ms");
                if (baked != null)
                {
                    cached = new Cached { Mesh = baked, BonePaths = BonePaths(visualRoot) };
                }
                else
                {
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    cached = Build(visualRoot, cellSize, blend);
                    if (cached == null) return;
                    cached.Mesh.name = meshName;
                    Debug.LogWarning($"[AnimalSculptor] No baked {meshName}; sculpted {speciesKey} at runtime in {timer.ElapsedMilliseconds} ms (run Battle of Ages/Bake Creature Meshes).");
                }
                Cache[speciesKey] = cached;
            }

            // Bones of this instance, by path
            var bones = new Transform[cached.BonePaths.Length];
            for (int i = 0; i < bones.Length; i++)
                bones[i] = cached.BonePaths[i].Length == 0 ? visualRoot : visualRoot.Find(cached.BonePaths[i]);

            // Hide the melted primitives
            foreach (MeshRenderer mr in visualRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsRigid(mr.name) && SoftShape(mr)) mr.enabled = false;
            }

            GameObject skinObj = new GameObject("Skin");
            skinObj.transform.SetParent(visualRoot, false);
            SkinnedMeshRenderer smr = skinObj.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = cached.Mesh;
            smr.bones = bones;
            smr.rootBone = visualRoot;
            smr.localBounds = cached.Mesh.bounds;
            smr.updateWhenOffscreen = false;
            smr.sharedMaterial = FurMaterial(shader);
        }

        /// <summary>The mesh built (or loaded) for a species in this session, for the bake tool.</summary>
        public static Mesh CachedMesh(string speciesKey) => Cache.TryGetValue(speciesKey, out Cached c) ? c.Mesh : null;

        public static void ClearCache() => Cache.Clear();

        /// <summary>Bone paths in the same order Build assigns them (first soft shape of each parent joint).</summary>
        private static string[] BonePaths(Transform visualRoot)
        {
            var paths = new List<string> { "" };
            foreach (MeshRenderer mr in visualRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsRigid(mr.name) || !SoftShape(mr)) continue;
                string path = RelativePath(visualRoot, mr.transform.parent);
                if (!paths.Contains(path)) paths.Add(path);
            }
            return paths.ToArray();
        }

        /// <summary>Hash of the blockout (shape types, transforms, colours) and build settings; quantized like SdfCreature.</summary>
        private static string Signature(Transform visualRoot, float h, float blend)
        {
            uint hash = 2166136261;
            void F(float v) { hash = (hash ^ (uint)Mathf.RoundToInt(v * 10000f)) * 16777619; }
            F(1); F(h); F(blend);
            Matrix4x4 toRoot = visualRoot.worldToLocalMatrix;
            foreach (MeshRenderer mr in visualRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsRigid(mr.name) || !SoftShape(mr)) continue;
                Matrix4x4 m = toRoot * mr.transform.localToWorldMatrix;
                for (int i = 0; i < 16; i++) F(m[i]);
                foreach (char ch in mr.GetComponent<MeshFilter>().sharedMesh.name) F(ch);
                foreach (char ch in RelativePath(visualRoot, mr.transform.parent)) F(ch);
                Color c = mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_BaseColor") ? mr.sharedMaterial.GetColor("_BaseColor") : Color.gray;
                F(c.r); F(c.g); F(c.b);
            }
            return hash.ToString("x8");
        }

        private static Material furMaterial;
        private static Material FurMaterial(Shader shader)
        {
            if (furMaterial != null) return furMaterial;
            furMaterial = new Material(shader) { name = "MAT_AnimalFur", enableInstancing = true };
            furMaterial.SetColor("_BaseColor", Color.white);
            furMaterial.SetFloat("_Pattern", 6f);
            furMaterial.SetFloat("_Smoothness", 0.18f);
            furMaterial.SetFloat("_UseVertexColor", 1f);
            return furMaterial;
        }

        private static bool SoftShape(MeshRenderer mr)
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            string mesh = mf.sharedMesh.name;
            return mesh.StartsWith("Sphere") || mesh.StartsWith("Capsule") || mesh.StartsWith("Cylinder");
        }

        private static Cached Build(Transform visualRoot, float h, float blend)
        {
            // ---- 1. Shapes from the blockout, expressed in visualRoot space
            var bonePaths = new List<string> { "" };
            var shapes = new List<Shape>();
            Matrix4x4 toRoot = visualRoot.worldToLocalMatrix;
            foreach (MeshRenderer mr in visualRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (IsRigid(mr.name) || !SoftShape(mr)) continue;
                Transform t = mr.transform;
                string shapeMesh = t.GetComponent<MeshFilter>().sharedMesh.name;
                Matrix4x4 m = toRoot * t.localToWorldMatrix;
                Vector3 c = m.MultiplyPoint3x4(Vector3.zero);
                Vector3 ax = m.MultiplyVector(Vector3.right), ay = m.MultiplyVector(Vector3.up), az = m.MultiplyVector(Vector3.forward);

                Transform boneT = t.parent;
                string path = RelativePath(visualRoot, boneT);
                int bone = bonePaths.IndexOf(path);
                if (bone < 0) { bone = bonePaths.Count; bonePaths.Add(path); }

                Color col = mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_BaseColor") ? mr.sharedMaterial.GetColor("_BaseColor") : Color.gray;
                var s = new Shape { Bone = bone, Color = col };
                if (shapeMesh.StartsWith("Sphere"))
                {
                    // Ellipsoid with semi-axes |ax|/2.. ; distance approximated in unit-sphere space * smallest axis
                    float rx = ax.magnitude * 0.5f, ry = ay.magnitude * 0.5f, rz = az.magnitude * 0.5f;
                    Matrix4x4 basis = Matrix4x4.identity;
                    basis.SetColumn(0, ax.normalized * rx); basis.SetColumn(1, ay.normalized * ry); basis.SetColumn(2, az.normalized * rz);
                    basis.SetColumn(3, new Vector4(c.x, c.y, c.z, 1f));
                    s.Ellipsoid = true;
                    s.ToUnit = basis.inverse;
                    s.Scale = Mathf.Min(rx, Mathf.Min(ry, rz));
                    s.A = s.B = c;
                    s.Radius = Mathf.Max(rx, Mathf.Max(ry, rz));
                }
                else
                {
                    // Capsule / cylinder: segment along local Y, radius from the X/Z scale
                    float r = (ax.magnitude + az.magnitude) * 0.25f;
                    float half = Mathf.Max(0f, ay.magnitude - r);
                    Vector3 dir = ay.normalized;
                    s.A = c - dir * half;
                    s.B = c + dir * half;
                    s.Radius = r;
                }
                shapes.Add(s);
            }
            if (shapes.Count == 0) return null;

            // ---- 2. Grid over the shapes' bounds
            Bounds bounds = new Bounds(shapes[0].A, Vector3.zero);
            foreach (Shape s in shapes)
            {
                bounds.Encapsulate(new Bounds(s.A, Vector3.one * (s.Radius * 2f)));
                bounds.Encapsulate(new Bounds(s.B, Vector3.one * (s.Radius * 2f)));
            }
            bounds.Expand(h * 4f + blend * 2f);
            Vector3 origin = bounds.min;
            int nx = Mathf.CeilToInt(bounds.size.x / h), ny = Mathf.CeilToInt(bounds.size.y / h), nz = Mathf.CeilToInt(bounds.size.z / h);
            int sx = nx + 1, sy = ny + 1, sz = nz + 1;
            var field = new float[sx * sy * sz];
            for (int z = 0; z < sz; z++)
            for (int y = 0; y < sy; y++)
            for (int x = 0; x < sx; x++)
                field[x + sx * (y + sy * z)] = Field(shapes, origin + new Vector3(x, y, z) * h, blend);

            // ---- 3. Surface nets: one vertex per cell crossing the surface
            var cellVertex = new int[nx * ny * nz];
            for (int i = 0; i < cellVertex.Length; i++) cellVertex[i] = -1;
            var verts = new List<Vector3>();
            int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            var cornerVal = new float[8];
            var cornerPos = new Vector3[8];
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int inside = 0;
                for (int k = 0; k < 8; k++)
                {
                    int dx = k & 1, dy = (k >> 1) & 1, dz = (k >> 2) & 1;
                    cornerVal[k] = field[(x + dx) + sx * ((y + dy) + sy * (z + dz))];
                    cornerPos[k] = origin + new Vector3(x + dx, y + dy, z + dz) * h;
                    if (cornerVal[k] < 0f) inside++;
                }
                if (inside == 0 || inside == 8) continue;

                Vector3 sum = Vector3.zero; int n = 0;
                for (int e = 0; e < 12; e++)
                {
                    float a = cornerVal[edges[e, 0]], b = cornerVal[edges[e, 1]];
                    if ((a < 0f) == (b < 0f)) continue;
                    sum += Vector3.Lerp(cornerPos[edges[e, 0]], cornerPos[edges[e, 1]], a / (a - b));
                    n++;
                }
                Vector3 p = sum / n;
                // One Newton step onto the surface
                Vector3 g = Gradient(shapes, p, blend, h * 0.5f);
                float f = Field(shapes, p, blend);
                if (g.sqrMagnitude > 1e-8f) p -= g * (f / g.sqrMagnitude);
                cellVertex[x + nx * (y + ny * z)] = verts.Count;
                verts.Add(p);
            }

            var tris = new List<int>();
            // Quads across every grid edge with a sign change (edges shared by 4 cells)
            for (int z = 1; z < nz; z++)
            for (int y = 1; y < ny; y++)
            for (int x = 0; x < nx; x++)
                Quad(field, cellVertex, tris, x, y, z, 0, sx, sy, nx, ny);
            for (int z = 1; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 1; x < nx; x++)
                Quad(field, cellVertex, tris, x, y, z, 1, sx, sy, nx, ny);
            for (int z = 0; z < nz; z++)
            for (int y = 1; y < ny; y++)
            for (int x = 1; x < nx; x++)
                Quad(field, cellVertex, tris, x, y, z, 2, sx, sy, nx, ny);

            // ---- 4. Normals, colours, skin weights, rest positions
            var normals = new Vector3[verts.Count];
            var colors = new Color[verts.Count];
            var weights = new BoneWeight[verts.Count];
            var boneScore = new float[bonePaths.Count];
            float sigma = blend * 0.6f + h;        // skin weights: wide, smooth bends
            float colorSigma = h * 0.8f;           // colours: crisp regions (belly, hooves, snout)
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 p = verts[i];
                normals[i] = Gradient(shapes, p, blend, h * 0.5f).normalized;

                float dMin = float.MaxValue;
                var d = new float[shapes.Count];
                for (int s = 0; s < shapes.Count; s++) { d[s] = Distance(shapes[s], p); dMin = Mathf.Min(dMin, d[s]); }

                Color col = Color.clear; float wSum = 0f;
                System.Array.Clear(boneScore, 0, boneScore.Length);
                for (int s = 0; s < shapes.Count; s++)
                {
                    float w = Mathf.Exp(-(d[s] - dMin) / sigma);
                    float wc = Mathf.Exp(-(d[s] - dMin) / colorSigma);
                    col += shapes[s].Color * wc;
                    wSum += wc;
                    boneScore[shapes[s].Bone] = Mathf.Max(boneScore[shapes[s].Bone], w);
                }
                colors[i] = col / wSum;
                weights[i] = TopFour(boneScore);
            }

            Mesh mesh = new Mesh { name = "SculptedAnimal", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(3, verts);           // rest position (metres) for the fur pattern
            mesh.SetTriangles(tris, 0);
            mesh.boneWeights = weights;

            var bindposes = new Matrix4x4[bonePaths.Count];
            for (int b = 0; b < bonePaths.Count; b++)
            {
                Transform bt = bonePaths[b].Length == 0 ? visualRoot : visualRoot.Find(bonePaths[b]);
                bindposes[b] = bt.worldToLocalMatrix * visualRoot.localToWorldMatrix;
            }
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            return new Cached { Mesh = mesh, BonePaths = bonePaths.ToArray() };
        }

        private static void Quad(float[] field, int[] cellVertex, List<int> tris, int x, int y, int z, int axis, int sx, int sy, int nx, int ny)
        {
            int ox = axis == 0 ? 1 : 0, oy = axis == 1 ? 1 : 0, oz = axis == 2 ? 1 : 0;
            float a = field[x + sx * (y + sy * z)];
            float b = field[(x + ox) + sx * ((y + oy) + sy * (z + oz))];
            if ((a < 0f) == (b < 0f)) return;

            // The four cells around this edge
            int c0, c1, c2, c3;
            if (axis == 0) { c0 = Cell(x, y - 1, z - 1); c1 = Cell(x, y, z - 1); c2 = Cell(x, y, z); c3 = Cell(x, y - 1, z); }
            else if (axis == 1) { c0 = Cell(x - 1, y, z - 1); c1 = Cell(x - 1, y, z); c2 = Cell(x, y, z); c3 = Cell(x, y, z - 1); }
            else { c0 = Cell(x - 1, y - 1, z); c1 = Cell(x, y - 1, z); c2 = Cell(x, y, z); c3 = Cell(x - 1, y, z); }
            if (c0 < 0 || c1 < 0 || c2 < 0 || c3 < 0) return;

            if (a < 0f) { tris.Add(c0); tris.Add(c1); tris.Add(c2); tris.Add(c0); tris.Add(c2); tris.Add(c3); }
            else { tris.Add(c0); tris.Add(c2); tris.Add(c1); tris.Add(c0); tris.Add(c3); tris.Add(c2); }

            int Cell(int cx, int cy, int cz) => cellVertex[cx + nx * (cy + ny * cz)];
        }

        private static float Field(List<Shape> shapes, Vector3 p, float k)
        {
            float d = float.MaxValue;
            foreach (Shape s in shapes) d = SmoothMin(d, Distance(s, p), k);
            return d;
        }

        private static float Distance(Shape s, Vector3 p)
        {
            if (s.Ellipsoid)
            {
                Vector3 q = s.ToUnit.MultiplyPoint3x4(p);
                return (q.magnitude - 1f) * s.Scale;
            }
            Vector3 ab = s.B - s.A;
            float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - s.A, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, s.A + ab * t) - s.Radius;
        }

        private static float SmoothMin(float a, float b, float k)
        {
            if (a == float.MaxValue) return b;
            float hh = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, hh) - k * hh * (1f - hh);
        }

        private static Vector3 Gradient(List<Shape> shapes, Vector3 p, float k, float e)
        {
            return new Vector3(
                Field(shapes, p + new Vector3(e, 0, 0), k) - Field(shapes, p - new Vector3(e, 0, 0), k),
                Field(shapes, p + new Vector3(0, e, 0), k) - Field(shapes, p - new Vector3(0, e, 0), k),
                Field(shapes, p + new Vector3(0, 0, e), k) - Field(shapes, p - new Vector3(0, 0, e), k)) / (2f * e);
        }

        private static BoneWeight TopFour(float[] score)
        {
            int[] idx = { 0, 0, 0, 0 };
            float[] val = { 0f, 0f, 0f, 0f };
            for (int b = 0; b < score.Length; b++)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (score[b] > val[k])
                    {
                        for (int m = 3; m > k; m--) { val[m] = val[m - 1]; idx[m] = idx[m - 1]; }
                        val[k] = score[b]; idx[k] = b;
                        break;
                    }
                }
            }
            float total = val[0] + val[1] + val[2] + val[3];
            if (total <= 0f) total = 1f;
            return new BoneWeight
            {
                boneIndex0 = idx[0], weight0 = val[0] / total,
                boneIndex1 = idx[1], weight1 = val[1] / total,
                boneIndex2 = idx[2], weight2 = val[2] / total,
                boneIndex3 = idx[3], weight3 = val[3] / total
            };
        }

        private static string RelativePath(Transform root, Transform t)
        {
            if (t == null || t == root) return "";
            string path = t.name;
            for (Transform p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
