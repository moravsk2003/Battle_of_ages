using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Sculpts a creature from code: shapes (ellipsoids, tapered round cones, optionally flattened) are declared on the
    /// joints of a rig, joined with per-shape smooth blends, and carved (nostrils, eye sockets, mouth line). The signed
    /// distance field is polygonized with surface nets (narrow band), then "painted": colours blend between the shapes,
    /// a species paint function adds markings and ambient occlusion is baked from the field. Hair strands (mane, tufts,
    /// short body bristles) and rigid glossy details (eyes, tusks) are added as extra submeshes. Everything is skinned
    /// to the joints, so the procedural gait animation deforms it. Built once per species and shared.
    /// </summary>
    public sealed class SdfCreature
    {
        /// <summary>Paint callback: rest position and normal (VisualModel space), blended shape colour, paintable weight.</summary>
        public delegate Color PaintFunc(Vector3 position, Vector3 normal, Color shapeColor, float paintable);

        private sealed class Shape
        {
            public bool Ellipsoid, Carve;
            public Matrix4x4 ToLocal;
            public Vector3 R, A, B;
            public float RA, RB, Fix = 1f, K;
            public Color Color;
            public bool HasColor;
            public float Paintable;
            public Vector3 Surface; // (pattern, metallic, smoothness) for static props, see SurfaceBrush
            public bool HasSurface;
            public int Bone;
            public Vector3 BC;
            public float BR;
        }

        private sealed class Cached
        {
            public Mesh Mesh;
            public string[] BonePaths;
        }

        private struct StrandLine
        {
            public Vector3[] Spine;
            public int Count;
            public float Spread, Radius;
            public Func<float, float> Length;
            public Color Base, Tip;
            public Vector3 Lean;
        }

        private struct Tuft
        {
            public int Bone;
            public Vector3 Root, Dir;
            public int Count;
            public float Length, Spread, Radius;
            public Color Base, Tip;
        }

        private struct Fuzz
        {
            public int Count;
            public float MinLength, MaxLength, Radius;
            public Func<Vector3, Vector3, float> Density;
            public Vector3 Flow;
            public float TipLighten;
        }

        private struct Rigid
        {
            public int Bone;
            public Vector3[] Points;   // VisualModel space
            public float[] Radii;
            public Color Color, TipColor;
            public bool Sphere, Matte;
        }

        private static readonly Dictionary<string, Cached> Cache = new Dictionary<string, Cached>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        private readonly Transform root;
        private readonly List<Shape> unions = new List<Shape>();
        private readonly List<Shape> carves = new List<Shape>();
        private readonly List<string> bonePaths = new List<string> { "" };
        private readonly List<Transform> boneTransforms = new List<Transform>();
        private readonly List<StrandLine> strandLines = new List<StrandLine>();
        private readonly List<Tuft> tufts = new List<Tuft>();
        private readonly List<Fuzz> fuzz = new List<Fuzz>();
        private readonly List<Rigid> rigids = new List<Rigid>();

        private float inflate; // set per build: distant meshes are slightly inflated
        private bool cutCarves = true; // set per build: distant meshes keep the carve colours but not the cuts
        private List<Vector3> bodySurfaces; // per body vertex, when any shape carries a surface

        /// <summary>
        /// Surface (CharacterSurface pattern, metallic, smoothness) given to the shapes declared from now on; blended
        /// between shapes like their colours and returned by BuildStatic (props: one material, surface per vertex).
        /// </summary>
        public Vector3? SurfaceBrush;

        public PaintFunc Paint;
        public float AmbientOcclusion = 0.5f;
        /// <summary>Reach of the baked occlusion (1 = deer / boar scale, up to ~11 cm; smaller for small animals and hand-sized props).</summary>
        public float OcclusionScale = 1f;
        public float SkinSigma = 0.05f;

        public SdfCreature(Transform visualRoot)
        {
            root = visualRoot;
            boneTransforms.Add(visualRoot);
        }

        // ------------------------------------------------------------------ declarations (bone-local coordinates)

        public void Ellipsoid(Transform bone, Vector3 center, Vector3 radii, Vector3 euler, Color color, float blend, float paintable = 1f) =>
            AddEllipsoid(bone, center, radii, euler, color, blend, paintable, false);

        public void CarveEllipsoid(Transform bone, Vector3 center, Vector3 radii, Vector3 euler, float blend, Color? color = null) =>
            AddEllipsoid(bone, center, radii, euler, color ?? Color.clear, blend, 0f, true);

        /// <summary>Tapered round cone from a (radius ra) to b (radius rb); squash flattens it across its axis in the bone frame.</summary>
        public void Cone(Transform bone, Vector3 a, Vector3 b, float ra, float rb, Color color, float blend, Vector3? squash = null, float paintable = 1f) =>
            AddCone(bone, a, b, ra, rb, color, blend, squash ?? Vector3.one, paintable, false);

        public void CarveCone(Transform bone, Vector3 a, Vector3 b, float ra, float rb, float blend, Color? color = null, Vector3? squash = null) =>
            AddCone(bone, a, b, ra, rb, color ?? Color.clear, blend, squash ?? Vector3.one, 0f, true);

        /// <summary>Hair strands rooted along a spine line (VisualModel space, projected down onto the body).</summary>
        public void Strands(Vector3[] spine, int count, float spread, Func<float, float> length, float radius, Color baseColor, Color tipColor, Vector3 lean)
        {
            strandLines.Add(new StrandLine { Spine = spine, Count = count, Spread = spread, Length = length, Radius = radius, Base = baseColor, Tip = tipColor, Lean = lean });
        }

        /// <summary>A tuft of strands from one point of a bone (ear tips, tail end), rigidly attached to it.</summary>
        public void AddTuft(Transform bone, Vector3 localRoot, Vector3 localDir, int count, float length, float spread, float radius, Color baseColor, Color tipColor)
        {
            Matrix4x4 m = ToRoot(bone);
            tufts.Add(new Tuft
            {
                Bone = BoneIndex(bone), Root = m.MultiplyPoint3x4(localRoot), Dir = m.MultiplyVector(localDir).normalized,
                Count = count, Length = length, Spread = spread, Radius = radius, Base = baseColor, Tip = tipColor
            });
        }

        /// <summary>Short bristles over the coat; density(p, n) in 0..1 per surface point, flow = lie direction.</summary>
        public void BodyBristles(int count, float minLength, float maxLength, float radius, Vector3 flow, float tipLighten, Func<Vector3, Vector3, float> density)
        {
            fuzz.Add(new Fuzz { Count = count, MinLength = minLength, MaxLength = maxLength, Radius = radius, Flow = flow, TipLighten = tipLighten, Density = density });
        }

        /// <summary>Rigid glossy tube along bone-local points (tusks, claws), tapering through the given radii.</summary>
        public void GlossTube(Transform bone, Vector3[] localPoints, float[] radii, Color color)
        {
            Matrix4x4 m = ToRoot(bone);
            var pts = new Vector3[localPoints.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = m.MultiplyPoint3x4(localPoints[i]);
            rigids.Add(new Rigid { Bone = BoneIndex(bone), Points = pts, Radii = radii, Color = color });
        }

        public void GlossSphere(Transform bone, Vector3 center, float radius, Color color)
        {
            rigids.Add(new Rigid { Bone = BoneIndex(bone), Points = new[] { ToRoot(bone).MultiplyPoint3x4(center) }, Radii = new[] { radius }, Color = color, Sphere = true });
        }

        /// <summary>
        /// Rigid matte tube (antlers), shaded from baseColor to tipColor. Matte tubes form an extra last submesh, so a
        /// renderer given one material less skips them (antlerless individuals share the mesh).
        /// </summary>
        public void Tube(Transform bone, Vector3[] localPoints, float[] radii, Color baseColor, Color tipColor)
        {
            Matrix4x4 m = ToRoot(bone);
            var pts = new Vector3[localPoints.Length];
            for (int i = 0; i < pts.Length; i++) pts[i] = m.MultiplyPoint3x4(localPoints[i]);
            rigids.Add(new Rigid { Bone = BoneIndex(bone), Points = pts, Radii = radii, Color = baseColor, TipColor = tipColor, Matte = true });
        }

        // ------------------------------------------------------------------ build / apply

        /// <summary>Resources folder of meshes baked in the editor ("Battle of Ages/Bake Creature Meshes").</summary>
        public const string BakedFolder = "Creatures";
        private const int AlgorithmVersion = 1;

        /// <summary>
        /// Builds (or reuses) the species mesh and attaches it to the visual root as a SkinnedMeshRenderer. A mesh baked
        /// into Resources is used when its signature matches the current declarations; otherwise it is sculpted now.
        /// </summary>
        public SkinnedMeshRenderer Apply(string speciesKey, float cellSize) => CreateRenderer("Skin", GetMesh(speciesKey, cellSize, true));

        /// <summary>
        /// As Apply, plus a distant mesh (cell lodCellSize; no hair or carved cuts, slightly inflated so thin legs survive the
        /// coarse grid) and a LODGroup on the visual root: full mesh above lodScreenHeight, culled below cullScreenHeight.
        /// Returns both renderers (full, distant).
        /// </summary>
        public SkinnedMeshRenderer[] ApplyWithLod(string speciesKey, float cellSize, float lodCellSize, float lodScreenHeight, float cullScreenHeight)
        {
            SkinnedMeshRenderer near = CreateRenderer("Skin", GetMesh(speciesKey, cellSize, true));
            SkinnedMeshRenderer far = CreateRenderer("SkinLOD1", GetMesh(speciesKey + LodSuffix, lodCellSize, false));
            LODGroup lod = root.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(lodScreenHeight, new Renderer[] { near }), new LOD(cullScreenHeight, new Renderer[] { far }) });
            lod.RecalculateBounds();
            return new[] { near, far };
        }

        /// <summary>Cache key suffix of a species' distant mesh (the bake tool saves both).</summary>
        public const string LodSuffix = "_LOD1";

        private Cached GetMesh(string cacheKey, float cellSize, bool detail)
        {
            if (Cache.TryGetValue(cacheKey, out Cached cached) && cached.Mesh != null) return cached;
            string meshName = "Sculpted_" + cacheKey + "_" + Signature(cellSize, detail);
            // The signature is part of the file name (Unity renames an asset's main object to its file name).
            var loadTimer = System.Diagnostics.Stopwatch.StartNew();
            Mesh baked = UnityEngine.Resources.Load<Mesh>(BakedFolder + "/" + meshName);
            if (loadTimer.ElapsedMilliseconds > 5) Debug.Log($"[Creatures] loaded {meshName} in {loadTimer.ElapsedMilliseconds} ms");
            if (baked != null)
            {
                cached = new Cached { Mesh = baked, BonePaths = bonePaths.ToArray() };
            }
            else
            {
                Debug.LogWarning($"[SdfCreature] No baked {meshName} mesh; sculpting {cacheKey} at runtime (run Battle of Ages/Bake Creature Meshes).");
                cached = Build(cellSize, detail);
                cached.Mesh.name = meshName;
            }
            Cache[cacheKey] = cached;
            return cached;
        }

        private SkinnedMeshRenderer CreateRenderer(string name, Cached cached)
        {
            var bones = new Transform[cached.BonePaths.Length];
            for (int i = 0; i < bones.Length; i++)
                bones[i] = cached.BonePaths[i].Length == 0 ? root : root.Find(cached.BonePaths[i]);

            GameObject skinObj = new GameObject(name);
            skinObj.transform.SetParent(root, false);
            SkinnedMeshRenderer smr = skinObj.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = cached.Mesh;
            smr.bones = bones;
            smr.rootBone = root;
            Bounds b = cached.Mesh.bounds;
            b.Expand(0.3f); // room for the gait
            smr.localBounds = b;
            smr.sharedMaterials = cached.Mesh.subMeshCount > 3
                ? new[] { Surface("Coat", 6f, 0.16f), Surface("Hair", 4f, 0.22f), Surface("Gloss", 5f, 0.5f), Surface("Matte", 4f, 0.3f) }
                : new[] { Surface("Coat", 6f, 0.16f), Surface("Hair", 4f, 0.22f), Surface("Gloss", 5f, 0.5f) };
            return smr;
        }

        /// <summary>The mesh built (or loaded) for a species in this session, for the bake tool (distant mesh: key + LodSuffix).</summary>
        public static Mesh CachedMesh(string speciesKey) => Cache.TryGetValue(speciesKey, out Cached c) ? c.Mesh : null;

        public static void ClearCache() => Cache.Clear();

        /// <summary>
        /// Sculpts the declared shapes into plain static geometry (body only: no hair, rigid parts, skinning or cache)
        /// for props, appended to the lists in visual-root space. Colours come out linear when the project is;
        /// surfaces are the blended SurfaceBrush values (pattern, metallic, smoothness).
        /// </summary>
        public void BuildStatic(float cellSize, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<Vector3> surfaces, List<int> tris)
        {
            bodySurfaces = new List<Vector3>();
            Mesh mesh = Build(cellSize, true).Mesh;
            int start = verts.Count, count = bodySurfaces.Count;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var c = new List<Color>();
            mesh.GetVertices(v); mesh.GetNormals(n); mesh.GetColors(c);
            for (int i = 0; i < count; i++) { verts.Add(v[i]); normals.Add(n[i]); colors.Add(c[i]); surfaces.Add(bodySurfaces[i]); }
            foreach (int i in mesh.GetTriangles(0)) tris.Add(start + i);
            bodySurfaces = null;
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh);
        }

        /// <summary>
        /// Hash of everything that shapes the mesh: declarations, cell size and samples of the paint / density / length
        /// callbacks, so a baked mesh is never used after the species code changed.
        /// </summary>
        private string Signature(float h, bool detail)
        {
            uint hash = 2166136261;
            void F(float v) { hash = (hash ^ (uint)Mathf.RoundToInt(v * 10000f)) * 16777619; } // quantized: transform matrices differ in the last bits between edit and play mode
            void V(Vector3 v) { F(v.x); F(v.y); F(v.z); }
            void C(Color c) { F(c.r); F(c.g); F(c.b); F(c.a); }
            F(AlgorithmVersion); F(h); F(AmbientOcclusion); F(SkinSigma);
            if (!detail) { F(3); F(LodInflate); F(6); } // only for distant meshes (inflated, carves not cut, eyes kept): full meshes keep their signature
            if (OcclusionScale != 1f) { F(4); F(OcclusionScale); } // only when used: older species keep their signature
            foreach (List<Shape> list in new[] { unions, carves })
            {
                foreach (Shape s in list)
                {
                    for (int i = 0; i < 16; i++) F(s.ToLocal[i]);
                    V(s.R); V(s.A); V(s.B); F(s.RA); F(s.RB); F(s.Fix); F(s.K); C(s.Color); F(s.Paintable); F(s.Bone);
                }
                F(list.Count);
            }
            foreach (string p in bonePaths) foreach (char ch in p) F(ch);
            foreach (StrandLine l in strandLines)
            {
                foreach (Vector3 p in l.Spine) V(p);
                F(l.Count); F(l.Spread); F(l.Radius); C(l.Base); C(l.Tip); V(l.Lean);
                for (int i = 0; i <= 8; i++) F(l.Length(i / 8f));
            }
            foreach (Tuft t in tufts) { F(t.Bone); V(t.Root); V(t.Dir); F(t.Count); F(t.Length); F(t.Spread); F(t.Radius); C(t.Base); C(t.Tip); }
            foreach (Rigid r in rigids)
            {
                F(r.Bone); foreach (Vector3 p in r.Points) V(p); foreach (float rr in r.Radii) F(rr); C(r.Color); F(r.Sphere ? 1 : 0);
                if (r.Matte) { F(2); C(r.TipColor); } // only when used: older species keep their signature
            }
            var rng = new System.Random(7);
            for (int i = 0; i < 48; i++)
            {
                Vector3 p = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 1.2f, (float)rng.NextDouble() * 2.4f - 1.2f);
                Vector3 n = RandomVector(rng).normalized;
                if (Paint != null) C(Paint(p, n, new Color(0.3f, 0.2f, 0.1f), (float)rng.NextDouble()));
                foreach (Fuzz f in fuzz) F(f.Density(p, n));
            }
            foreach (Fuzz f in fuzz) { F(f.Count); F(f.MinLength); F(f.MaxLength); F(f.Radius); V(f.Flow); F(f.TipLighten); }
            return hash.ToString("x8");
        }

        private static Material Surface(string key, float pattern, float smoothness)
        {
            if (Materials.TryGetValue(key, out Material m) && m != null) return m;
            Shader shader = Shader.Find("Custom/CharacterSurface");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(shader) { name = "MAT_Creature" + key, enableInstancing = true };
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Pattern", pattern);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_UseVertexColor", 1f);
            Materials[key] = m;
            return m;
        }

        /// <summary>Distant meshes grow by this fraction of their cell size (thin legs would break up on the coarse grid).</summary>
        private const float LodInflate = 0.3f;

        private Cached Build(float h, bool detail)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            inflate = detail ? 0f : h * LodInflate;
            cutCarves = detail; // sockets, nostrils and ear cups would punch holes through the coarse grid

            // ---- grid over the shapes (narrow band: blocks far from the surface are filled without evaluation)
            Bounds bounds = new Bounds(unions[0].BC, Vector3.zero);
            foreach (Shape s in unions) bounds.Encapsulate(new Bounds(s.BC, Vector3.one * (2f * (s.BR + s.K))));
            bounds.Expand(h * 6f);
            Vector3 origin = bounds.min;
            int nx = Mathf.CeilToInt(bounds.size.x / h), ny = Mathf.CeilToInt(bounds.size.y / h), nz = Mathf.CeilToInt(bounds.size.z / h);
            int sx = nx + 1, sy = ny + 1, sz = nz + 1;
            var field = new float[sx * sy * sz];
            const int Block = 4;
            float skip = h * 8f;
            for (int bz = 0; bz < sz; bz += Block)
            for (int by = 0; by < sy; by += Block)
            for (int bx = 0; bx < sx; bx += Block)
            {
                float fc = Field(origin + new Vector3(bx + 2f, by + 2f, bz + 2f) * h);
                bool far = Mathf.Abs(fc) > skip;
                for (int z = bz; z < Mathf.Min(bz + Block, sz); z++)
                for (int y = by; y < Mathf.Min(by + Block, sy); y++)
                for (int x = bx; x < Mathf.Min(bx + Block, sx); x++)
                    field[x + sx * (y + sy * z)] = far ? fc : Field(origin + new Vector3(x, y, z) * h);
            }

            // ---- surface nets
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
                for (int it = 0; it < 2; it++)
                {
                    Vector3 g = Gradient(p, h * 0.5f);
                    float f = Field(p);
                    if (g.sqrMagnitude > 1e-8f) p -= g * (f / g.sqrMagnitude);
                }
                cellVertex[x + nx * (y + ny * z)] = verts.Count;
                verts.Add(p);
            }
            var bodyTris = new List<int>();
            for (int z = 1; z < nz; z++) for (int y = 1; y < ny; y++) for (int x = 0; x < nx; x++) Quad(field, cellVertex, bodyTris, x, y, z, 0, sx, sy, nx, ny);
            for (int z = 1; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 1; x < nx; x++) Quad(field, cellVertex, bodyTris, x, y, z, 1, sx, sy, nx, ny);
            for (int z = 0; z < nz; z++) for (int y = 1; y < ny; y++) for (int x = 1; x < nx; x++) Quad(field, cellVertex, bodyTris, x, y, z, 2, sx, sy, nx, ny);

            // ---- body attributes
            int bodyCount = verts.Count;
            var normals = new List<Vector3>(bodyCount);
            var colors = new List<Color>(bodyCount);
            var weights = new List<BoneWeight>(bodyCount);
            var paintable = new float[bodyCount];
            for (int i = 0; i < bodyCount; i++)
            {
                Vector3 p = verts[i];
                Vector3 nrm = Gradient(p, h * 0.5f).normalized;
                Color c = ShapeColor(p, out float paint, out Vector3 surface);
                paintable[i] = paint;
                bodySurfaces?.Add(surface);
                if (Paint != null) c = Paint(p, nrm, c, paint);
                float ao = Occlusion(p, nrm);
                c *= Mathf.Lerp(1f - AmbientOcclusion, 1f, ao);
                c.a = ao;
                normals.Add(nrm);
                colors.Add(c);
                weights.Add(Weights(p));
            }

            // ---- hair
            var hairTris = new List<int>();
            var rng = new System.Random(1234);
            if (detail)
            {
                foreach (StrandLine line in strandLines) BuildStrandLine(line, rng, verts, normals, colors, weights, hairTris);
                foreach (Tuft t in tufts) BuildTuft(t, rng, verts, normals, colors, weights, hairTris);
                foreach (Fuzz f in fuzz) BuildFuzz(f, rng, bodyCount, paintable, verts, normals, colors, weights, hairTris);
            }

            // ---- rigid glossy details
            var glossTris = new List<int>();
            var matteTris = new List<int>();
            foreach (Rigid r in rigids)
            {
                var w = new BoneWeight { boneIndex0 = r.Bone, weight0 = 1f };
                if (r.Sphere) AddSphere(r.Points[0], r.Radii[0], r.Color, w, verts, normals, colors, weights, glossTris);
                else if (r.Matte) AddTube(r.Points, r.Radii, r.Color, r.TipColor, w, verts, normals, colors, weights, matteTris);
                else AddTube(r.Points, r.Radii, r.Color, r.Color * 0.8f, w, verts, normals, colors, weights, glossTris);
            }

            // Colours are authored in sRGB; vertex colours reach the shader untouched, so convert for the linear pipeline.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                for (int i = 0; i < colors.Count; i++) { Color c = colors[i]; Color l = c.linear; l.a = c.a; colors[i] = l; }

            Mesh mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(3, verts);
            mesh.subMeshCount = matteTris.Count > 0 ? 4 : 3;
            mesh.SetTriangles(bodyTris, 0);
            mesh.SetTriangles(hairTris, 1);
            mesh.SetTriangles(glossTris, 2);
            if (matteTris.Count > 0) mesh.SetTriangles(matteTris, 3);
            mesh.boneWeights = weights.ToArray();
            var bindposes = new Matrix4x4[boneTransforms.Count];
            for (int b = 0; b < bindposes.Length; b++) bindposes[b] = boneTransforms[b].worldToLocalMatrix * root.localToWorldMatrix;
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();
            Debug.Log($"[SdfCreature] {root.parent?.name}: {verts.Count} verts, {(bodyTris.Count + hairTris.Count + glossTris.Count + matteTris.Count) / 3} tris " +
                      $"(body {bodyTris.Count / 3}, hair {hairTris.Count / 3}, rigid {(glossTris.Count + matteTris.Count) / 3}), grid {sx}x{sy}x{sz}, {timer.ElapsedMilliseconds} ms");
            return new Cached { Mesh = mesh, BonePaths = bonePaths.ToArray() };
        }

        // ------------------------------------------------------------------ field

        private float Field(Vector3 p)
        {
            float d = 1e9f;
            for (int i = 0; i < unions.Count; i++)
            {
                Shape s = unions[i];
                if ((p - s.BC).magnitude - s.BR > d + s.K) continue;
                d = SmoothMin(d, Distance(s, p), s.K);
            }
            for (int i = 0; i < (cutCarves ? carves.Count : 0); i++)
            {
                Shape s = carves[i];
                if ((p - s.BC).magnitude - s.BR > s.K - d) continue;
                d = -SmoothMin(-d, Distance(s, p), s.K);
            }
            return d - inflate;
        }

        private static float SmoothMin(float a, float b, float k)
        {
            if (a >= 1e8f) return b;
            float hh = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, hh) - k * hh * (1f - hh);
        }

        private static float Distance(Shape s, Vector3 p)
        {
            Vector3 q = s.ToLocal.MultiplyPoint3x4(p);
            if (s.Ellipsoid)
            {
                Vector3 r = s.R;
                float k0 = new Vector3(q.x / r.x, q.y / r.y, q.z / r.z).magnitude;
                float k1 = new Vector3(q.x / (r.x * r.x), q.y / (r.y * r.y), q.z / (r.z * r.z)).magnitude;
                return k1 > 1e-9f ? k0 * (k0 - 1f) / k1 : -Mathf.Min(r.x, Mathf.Min(r.y, r.z));
            }
            return RoundCone(q, s.A, s.B, s.RA, s.RB) * s.Fix;
        }

        /// <summary>Exact distance to a round cone (Inigo Quilez).</summary>
        private static float RoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
        {
            Vector3 ba = b - a;
            float l2 = Vector3.Dot(ba, ba);
            float rr = r1 - r2;
            float a2 = l2 - rr * rr;
            float il2 = 1f / l2;
            Vector3 pa = p - a;
            float y = Vector3.Dot(pa, ba);
            float z = y - l2;
            Vector3 xv = pa * l2 - ba * y;
            float x2 = Vector3.Dot(xv, xv);
            float y2 = y * y * l2;
            float z2 = z * z * l2;
            float k = Mathf.Sign(rr) * rr * rr * x2;
            if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
            if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
            return (Mathf.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
        }

        private Vector3 Gradient(Vector3 p, float e)
        {
            return new Vector3(
                Field(p + new Vector3(e, 0, 0)) - Field(p - new Vector3(e, 0, 0)),
                Field(p + new Vector3(0, e, 0)) - Field(p - new Vector3(0, e, 0)),
                Field(p + new Vector3(0, 0, e)) - Field(p - new Vector3(0, 0, e))) / (2f * e);
        }

        /// <summary>Field-based ambient occlusion (creases between legs and belly, eye sockets, ear cups).</summary>
        private float Occlusion(Vector3 p, Vector3 n)
        {
            float occ = 0f, sca = 1f;
            for (int i = 0; i < 5; i++)
            {
                float hr = (0.012f + 0.1f * i / 4f) * OcclusionScale;
                occ += (hr - Field(p + n * hr)) * sca;
                sca *= 0.75f;
            }
            return Mathf.Clamp01(1f - 3.2f * occ / OcclusionScale);
        }

        private Color ShapeColor(Vector3 p, out float paint, out Vector3 surface)
        {
            Color col = Color.clear; float wSum = 0f, pSum = 0f;
            Vector3 sSum = Vector3.zero; float sw = 0f;
            float dMin = float.MaxValue;
            var d = new float[unions.Count + carves.Count];
            for (int i = 0; i < unions.Count; i++) { d[i] = Distance(unions[i], p); dMin = Mathf.Min(dMin, d[i]); }
            for (int i = 0; i < carves.Count; i++) { d[unions.Count + i] = carves[i].HasColor ? Mathf.Abs(Distance(carves[i], p)) : float.MaxValue; dMin = Mathf.Min(dMin, d[unions.Count + i]); }
            for (int i = 0; i < d.Length; i++)
            {
                if (d[i] == float.MaxValue) continue;
                Shape s = i < unions.Count ? unions[i] : carves[i - unions.Count];
                float sigma = Mathf.Max(0.004f, s.K * 0.35f);
                float w = Mathf.Exp(-(d[i] - dMin) / sigma);
                col += s.Color * w;
                pSum += s.Paintable * w;
                wSum += w;
                if (s.HasSurface) { sSum += s.Surface * w; sw += w; }
            }
            paint = pSum / wSum;
            surface = sw > 0f ? sSum / sw : Vector3.zero;
            return col / wSum;
        }

        private BoneWeight Weights(Vector3 p)
        {
            var score = new float[boneTransforms.Count];
            float dMin = float.MaxValue;
            var d = new float[unions.Count];
            for (int i = 0; i < unions.Count; i++) { d[i] = Distance(unions[i], p); dMin = Mathf.Min(dMin, d[i]); }
            for (int i = 0; i < unions.Count; i++)
            {
                float w = Mathf.Exp(-(d[i] - dMin) / SkinSigma);
                score[unions[i].Bone] = Mathf.Max(score[unions[i].Bone], w);
            }
            return TopFour(score);
        }

        // ------------------------------------------------------------------ hair

        private void BuildStrandLine(StrandLine line, System.Random rng, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<BoneWeight> weights, List<int> tris)
        {
            float total = 0f;
            var seg = new float[line.Spine.Length - 1];
            for (int i = 0; i < seg.Length; i++) { seg[i] = Vector3.Distance(line.Spine[i], line.Spine[i + 1]); total += seg[i]; }
            for (int s = 0; s < line.Count; s++)
            {
                float t = (s + (float)rng.NextDouble()) / line.Count;
                float dist = t * total;
                int k = 0;
                while (k < seg.Length - 1 && dist > seg[k]) { dist -= seg[k]; k++; }
                Vector3 seed = Vector3.Lerp(line.Spine[k], line.Spine[k + 1], seg[k] > 0f ? dist / seg[k] : 0f);
                seed.x += ((float)rng.NextDouble() * 2f - 1f) * line.Spread;
                Vector3 p = seed + Vector3.up * 0.3f;
                for (int it = 0; it < 60; it++)
                {
                    float f = Field(p);
                    if (f < 0.002f) break;
                    p += Vector3.down * Mathf.Max(f * 0.8f, 0.001f);
                }
                Vector3 n = Gradient(p, 0.004f).normalized;
                float len = line.Length(t) * Mathf.Lerp(0.75f, 1.15f, (float)rng.NextDouble());
                Vector3 dir = (n + line.Lean + RandomVector(rng) * 0.25f).normalized;
                Color tip = Color.Lerp(line.Base, line.Tip, (float)rng.NextDouble());
                AddStrand(p - n * 0.006f, dir, n, len, line.Radius, line.Base, tip, Weights(p), 2, verts, normals, colors, weights, tris);
            }
        }

        private void BuildTuft(Tuft t, System.Random rng, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<BoneWeight> weights, List<int> tris)
        {
            var w = new BoneWeight { boneIndex0 = t.Bone, weight0 = 1f };
            for (int i = 0; i < t.Count; i++)
            {
                Vector3 dir = (t.Dir + RandomVector(rng) * t.Spread).normalized;
                Vector3 p = t.Root + RandomVector(rng) * 0.008f;
                float len = t.Length * Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble());
                AddStrand(p, dir, dir, len, t.Radius, t.Base, Color.Lerp(t.Base, t.Tip, (float)rng.NextDouble()), w, 2, verts, normals, colors, weights, tris);
            }
        }

        private void BuildFuzz(Fuzz f, System.Random rng, int bodyCount, float[] paintable, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<BoneWeight> weights, List<int> tris)
        {
            int placed = 0, attempts = 0;
            while (placed < f.Count && attempts++ < f.Count * 20)
            {
                int i = rng.Next(bodyCount);
                if (paintable[i] < 0.6f) continue;
                Vector3 p = verts[i], n = normals[i];
                if ((float)rng.NextDouble() > f.Density(p, n)) continue;
                Vector3 flow = f.Flow - n * Vector3.Dot(f.Flow, n);
                Vector3 dir = (n * 0.45f + flow.normalized + RandomVector(rng) * 0.3f).normalized;
                Color c = colors[i];
                c.a = 1f;
                c *= Mathf.Lerp(0.75f, 1.2f, (float)rng.NextDouble());
                Color tip = Color.Lerp(c, new Color(0.55f, 0.5f, 0.44f), f.TipLighten * (float)rng.NextDouble());
                float len = Mathf.Lerp(f.MinLength, f.MaxLength, (float)rng.NextDouble());
                AddStrand(p - n * 0.004f, dir, n, len, f.Radius, c, tip, weights[i], 1, verts, normals, colors, weights, tris);
                placed++;
            }
        }

        private static Vector3 RandomVector(System.Random rng) =>
            new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);

        /// <summary>A tapered three-sided strand (one or two segments, drooping slightly), shaded with the body normal.</summary>
        private static void AddStrand(Vector3 rootPos, Vector3 dir, Vector3 bodyNormal, float length, float radius, Color c0, Color c1, BoneWeight w, int segments,
                                      List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<BoneWeight> weights, List<int> tris)
        {
            Vector3 t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(dir, t1);
            Vector3 droop = Vector3.down * 0.25f;
            int start = verts.Count;
            for (int r = 0; r < segments; r++)
            {
                float u = r / (float)segments;
                Vector3 c = rootPos + dir * (length * u) + droop * (length * u * u);
                float rad = radius * (1f - u * 0.6f);
                for (int k = 0; k < 3; k++)
                {
                    float a = k * Mathf.PI * 2f / 3f;
                    Vector3 radial = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                    verts.Add(c + radial * rad);
                    normals.Add((bodyNormal * 0.7f + radial * 0.3f).normalized);
                    colors.Add(Color.Lerp(c0, c1, u));
                    weights.Add(w);
                }
            }
            verts.Add(rootPos + dir * length + droop * length);
            normals.Add(bodyNormal);
            colors.Add(c1);
            weights.Add(w);
            int tip = verts.Count - 1;
            for (int r = 0; r < segments - 1; r++)
            {
                int a0 = start + r * 3, b0 = a0 + 3;
                for (int k = 0; k < 3; k++)
                {
                    int k1 = (k + 1) % 3;
                    tris.Add(a0 + k); tris.Add(b0 + k1); tris.Add(b0 + k);
                    tris.Add(a0 + k); tris.Add(a0 + k1); tris.Add(b0 + k1);
                }
            }
            int last = start + (segments - 1) * 3;
            for (int k = 0; k < 3; k++) { tris.Add(last + k); tris.Add(last + (k + 1) % 3); tris.Add(tip); }
        }

        private static void AddTube(Vector3[] pts, float[] radii, Color color, Color tipColor, BoneWeight w, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<BoneWeight> weights, List<int> tris)
        {
            const int Sides = 10;
            int start = verts.Count;
            Vector3 prevT1 = Vector3.zero;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 dir = (pts[Mathf.Min(i + 1, pts.Length - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 t1 = i == 0 ? Vector3.Cross(dir, Vector3.up).normalized : (prevT1 - dir * Vector3.Dot(prevT1, dir)).normalized;
                if (t1.sqrMagnitude < 0.5f) t1 = Vector3.Cross(dir, Vector3.right).normalized;
                prevT1 = t1;
                Vector3 t2 = Vector3.Cross(dir, t1);
                for (int k = 0; k < Sides; k++)
                {
                    float a = k * Mathf.PI * 2f / Sides;
                    Vector3 radial = t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a);
                    verts.Add(pts[i] + radial * radii[i]);
                    normals.Add(radial);
                    colors.Add(Color.Lerp(color, tipColor, i / (float)pts.Length));
                    weights.Add(w);
                }
            }
            for (int i = 0; i < pts.Length - 1; i++)
            for (int k = 0; k < Sides; k++)
            {
                int a0 = start + i * Sides + k, a1 = start + i * Sides + (k + 1) % Sides;
                int b0 = a0 + Sides, b1 = a1 + Sides;
                tris.Add(a0); tris.Add(b1); tris.Add(b0);
                tris.Add(a0); tris.Add(a1); tris.Add(b1);
            }
        }

        private static void AddSphere(Vector3 c, float r, Color color, BoneWeight w, List<Vector3> verts, List<Vector3> normals, List<Color> colors, List<BoneWeight> weights, List<int> tris)
        {
            const int Lat = 8, Lon = 12;
            int start = verts.Count;
            for (int i = 0; i <= Lat; i++)
            {
                float th = Mathf.PI * i / Lat;
                for (int j = 0; j <= Lon; j++)
                {
                    float ph = Mathf.PI * 2f * j / Lon;
                    Vector3 n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    verts.Add(c + n * r); normals.Add(n); colors.Add(color); weights.Add(w);
                }
            }
            for (int i = 0; i < Lat; i++)
            for (int j = 0; j < Lon; j++)
            {
                int a = start + i * (Lon + 1) + j, b = a + Lon + 1;
                tris.Add(a); tris.Add(a + 1); tris.Add(b);
                tris.Add(b); tris.Add(a + 1); tris.Add(b + 1);
            }
        }

        // ------------------------------------------------------------------ helpers

        private void AddEllipsoid(Transform bone, Vector3 center, Vector3 radii, Vector3 euler, Color color, float blend, float paintable, bool carve)
        {
            Matrix4x4 m = ToRoot(bone);
            Vector3 c = m.MultiplyPoint3x4(center);
            Quaternion rot = RootRotation(bone) * Quaternion.Euler(euler);
            var s = new Shape
            {
                Ellipsoid = true, Carve = carve, R = radii, K = blend, Color = color, HasColor = color.a > 0f, Paintable = paintable,
                Surface = SurfaceBrush ?? Vector3.zero, HasSurface = SurfaceBrush.HasValue,
                Bone = BoneIndex(bone), BC = c, BR = Mathf.Max(radii.x, Mathf.Max(radii.y, radii.z)),
                ToLocal = Matrix4x4.TRS(c, rot, Vector3.one).inverse
            };
            (carve ? carves : unions).Add(s);
        }

        private void AddCone(Transform bone, Vector3 a, Vector3 b, float ra, float rb, Color color, float blend, Vector3 squash, float paintable, bool carve)
        {
            Matrix4x4 m = ToRoot(bone);
            Vector3 ar = m.MultiplyPoint3x4(a), br = m.MultiplyPoint3x4(b);
            Vector3 mid = (ar + br) * 0.5f;
            Matrix4x4 toLocal = Matrix4x4.TRS(mid, RootRotation(bone), squash).inverse;
            var s = new Shape
            {
                Ellipsoid = false, Carve = carve, K = blend, Color = color, HasColor = color.a > 0f, Paintable = paintable,
                Surface = SurfaceBrush ?? Vector3.zero, HasSurface = SurfaceBrush.HasValue,
                Bone = BoneIndex(bone), BC = mid, BR = Vector3.Distance(ar, br) * 0.5f + Mathf.Max(ra, rb),
                ToLocal = toLocal, A = toLocal.MultiplyPoint3x4(ar), B = toLocal.MultiplyPoint3x4(br), RA = ra, RB = rb,
                Fix = Mathf.Min(squash.x, Mathf.Min(squash.y, squash.z))
            };
            (carve ? carves : unions).Add(s);
        }

        private Matrix4x4 ToRoot(Transform bone) => root.worldToLocalMatrix * bone.localToWorldMatrix;
        private Quaternion RootRotation(Transform bone) => Quaternion.Inverse(root.rotation) * bone.rotation;

        private int BoneIndex(Transform bone)
        {
            int i = boneTransforms.IndexOf(bone);
            if (i >= 0) return i;
            boneTransforms.Add(bone);
            bonePaths.Add(RelativePath(root, bone));
            return boneTransforms.Count - 1;
        }

        private static void Quad(float[] field, int[] cellVertex, List<int> tris, int x, int y, int z, int axis, int sx, int sy, int nx, int ny)
        {
            int ox = axis == 0 ? 1 : 0, oy = axis == 1 ? 1 : 0, oz = axis == 2 ? 1 : 0;
            float a = field[x + sx * (y + sy * z)];
            float b = field[(x + ox) + sx * ((y + oy) + sy * (z + oz))];
            if ((a < 0f) == (b < 0f)) return;
            int c0, c1, c2, c3;
            if (axis == 0) { c0 = Cell(x, y - 1, z - 1); c1 = Cell(x, y, z - 1); c2 = Cell(x, y, z); c3 = Cell(x, y - 1, z); }
            else if (axis == 1) { c0 = Cell(x - 1, y, z - 1); c1 = Cell(x - 1, y, z); c2 = Cell(x, y, z); c3 = Cell(x, y, z - 1); }
            else { c0 = Cell(x - 1, y - 1, z); c1 = Cell(x, y - 1, z); c2 = Cell(x, y, z); c3 = Cell(x - 1, y, z); }
            if (c0 < 0 || c1 < 0 || c2 < 0 || c3 < 0) return;
            if (a < 0f) { tris.Add(c0); tris.Add(c1); tris.Add(c2); tris.Add(c0); tris.Add(c2); tris.Add(c3); }
            else { tris.Add(c0); tris.Add(c2); tris.Add(c1); tris.Add(c0); tris.Add(c3); tris.Add(c2); }

            int Cell(int cx, int cy, int cz) => cellVertex[cx + nx * (cy + ny * cz)];
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
                boneIndex0 = idx[0], weight0 = val[0] / total, boneIndex1 = idx[1], weight1 = val[1] / total,
                boneIndex2 = idx[2], weight2 = val[2] / total, boneIndex3 = idx[3], weight3 = val[3] / total
            };
        }

        private static string RelativePath(Transform rootT, Transform t)
        {
            if (t == null || t == rootT) return "";
            string path = t.name;
            for (Transform p = t.parent; p != null && p != rootT; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>Smooth 3D value noise in 0..1 for paint functions.</summary>
        public static float Noise(Vector3 p)
        {
            Vector3 i = new Vector3(Mathf.Floor(p.x), Mathf.Floor(p.y), Mathf.Floor(p.z));
            Vector3 f = p - i;
            f = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));
            float H(float x, float y, float z)
            {
                float v = Mathf.Sin(x * 127.1f + y * 311.7f + z * 74.7f) * 43758.5453f;
                return v - Mathf.Floor(v);
            }
            float x00 = Mathf.Lerp(H(i.x, i.y, i.z), H(i.x + 1, i.y, i.z), f.x);
            float x10 = Mathf.Lerp(H(i.x, i.y + 1, i.z), H(i.x + 1, i.y + 1, i.z), f.x);
            float x01 = Mathf.Lerp(H(i.x, i.y, i.z + 1), H(i.x + 1, i.y, i.z + 1), f.x);
            float x11 = Mathf.Lerp(H(i.x, i.y + 1, i.z + 1), H(i.x + 1, i.y + 1, i.z + 1), f.x);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, f.y), Mathf.Lerp(x01, x11, f.y), f.z);
        }
    }
}
