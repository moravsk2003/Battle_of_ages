using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Resources;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// The world's trees, modelled in code: a broadleaf steppe tree and a spruce, four variants each.
    /// The wood (trunk with a root flare and bark ridges, forked limbs and branches) is MeshKit tubes on the shared prop
    /// material; the crown is leaf clumps at the branch ends on the wind-swayed foliage shader, their normals pointing out
    /// of the whole crown so it is lit as one soft mass, darker inside and underneath. A coarse distant LOD of each, and
    /// a stump and logs for a felled tree. Meshes are built once per variant and shared by every tree. Child names keep
    /// ResourceNodeTree's contract: "StylizedTrunk*" is the wood, "*Crown" the foliage, both hidden when it is felled.
    /// </summary>
    public static class SteppeTreeBuilder
    {
        private sealed class Model { public Mesh Wood, Crown, WoodFar, CrownFar; }

        private static readonly Dictionary<int, Model> broadleafModels = new Dictionary<int, Model>(), spruceModels = new Dictionary<int, Model>();
        private static Mesh stumpMesh, logMesh;

        // MeshKit surfaces: (pattern, metallic, smoothness); 4 = strands along the length (bark furrows), 8 = grit (cut wood)
        private static readonly Vector3 BarkS = new Vector3(4f, 0f, 0.12f), CutS = new Vector3(8f, 0f, 0.18f);

        /// <summary>Broadleaf trunk radius at the ground and the spruce's: the stump of a felled tree is sized by them.</summary>
        public const float BroadleafStump = 0.42f, SpruceStump = 0.30f;

        // ------------------------------------------------------------------ public API

        public static GameObject CreateSteppeTree(string name = "SteppeTree", int variant = 0)
        {
            int v = Mathf.Abs(variant) % 4;
            Model m = Broadleaf(v);
            Color[] leaves = { new Color(0.32f, 0.52f, 0.14f), new Color(0.22f, 0.44f, 0.15f), new Color(0.56f, 0.52f, 0.12f), new Color(0.30f, 0.50f, 0.17f) };
            Material foliage = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Foliage, leaves[v], 0f, 0.25f);
            GameObject root = Assemble(name, m, foliage, "SteppeCrown", BroadleafStump);
            return root;
        }

        /// <summary>Tall northern spruce: a straight trunk and drooping whorls of needle sprays, long below, short at the top.</summary>
        public static GameObject CreatePineTree(string name = "PineTree", int variant = 0)
        {
            int v = Mathf.Abs(variant) % 4;
            Model m = Spruce(v);
            Color[] needleColors = { new Color(0.20f, 0.38f, 0.20f), new Color(0.25f, 0.42f, 0.18f), new Color(0.19f, 0.36f, 0.27f), new Color(0.30f, 0.44f, 0.18f) };
            Color needles = needleColors[v];
            Material needleMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Foliage, needles, 0f, 0.15f);
            needleMat.SetColor("_TopColor", Color.Lerp(needles, new Color(0.62f, 0.78f, 0.36f), 0.35f));
            needleMat.SetColor("_BottomColor", needles * 0.6f);
            needleMat.SetFloat("_Translucency", 0.25f);
            needleMat.SetFloat("_WindStrength", 0.04f);
            return Assemble(name, m, needleMat, "PineFoliageCrown", SpruceStump);
        }

        /// <summary>Stump of a felled tree (radius 0.36 at the cut): flared bark sides, a pale cut with growth rings.</summary>
        public static Mesh StumpMesh => stumpMesh != null ? stumpMesh : (stumpMesh = BuildStump());

        /// <summary>A cut log along X (1.6 m, radius 0.17): bark with pale ringed ends.</summary>
        public static Mesh LogMesh => logMesh != null ? logMesh : (logMesh = BuildLog());

        private static GameObject Assemble(string name, Model m, Material foliage, string crownName, float stump)
        {
            var root = new GameObject(name);
            ResourceNodeTree node = root.AddComponent<ResourceNodeTree>();
            node.StumpRadius = stump;
            Material wood = MeshKit.PropMaterial();
            Renderer w0 = Part(root.transform, "StylizedTrunk", m.Wood, wood);
            Renderer c0 = Part(root.transform, crownName, m.Crown, foliage);
            Renderer w1 = Part(root.transform, "StylizedTrunk_Far", m.WoodFar, wood);
            Renderer c1 = Part(root.transform, crownName + "_Far", m.CrownFar, foliage);
            w1.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // far away the crown's shadow is enough
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.14f, new[] { w0, c0 }), new LOD(0.006f, new[] { w1, c1 }) });
            lod.RecalculateBounds();

            CapsuleCollider trunkCollider = root.AddComponent<CapsuleCollider>();
            trunkCollider.center = new Vector3(0f, 1.7f, 0f);
            trunkCollider.radius = 0.45f;
            trunkCollider.height = 3.4f;
            return root;
        }

        private static Renderer Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
            return r;
        }

        // ------------------------------------------------------------------ branches

        private sealed class Limb
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<float> R = new List<float>();
            public Vector3 Tip => P[P.Count - 1];
            public Vector3 At(float u) { float f = u * (P.Count - 1); int i = Mathf.Min((int)f, P.Count - 2); return Vector3.Lerp(P[i], P[i + 1], f - i); }
            public float RadiusAt(float u) { float f = u * (R.Count - 1); int i = Mathf.Min((int)f, R.Count - 2); return Mathf.Lerp(R[i], R[i + 1], f - i); }
            public Vector3 DirAt(float u) { float f = u * (P.Count - 1); int i = Mathf.Min((int)f, P.Count - 2); return (P[i + 1] - P[i]).normalized; }
        }

        private static float Rand(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        private static Vector3 RandDir(System.Random r) { Vector3 d = new Vector3(Rand(r, -1f, 1f), Rand(r, -1f, 1f), Rand(r, -1f, 1f)); return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.up; }

        /// <summary>A bending branch: rows points from start along dir, the direction pulled up by bendUp per step and wandering by wobble.</summary>
        private static Limb Grow(System.Random rnd, Vector3 start, Vector3 dir, float length, float r0, float r1, int rows, float bendUp, float wobble)
        {
            var l = new Limb();
            Vector3 p = start, d = dir.normalized;
            float step = length / (rows - 1);
            for (int i = 0; i < rows; i++)
            {
                float u = i / (rows - 1f);
                l.P.Add(p);
                l.R.Add(Mathf.Lerp(r0, r1, Mathf.Pow(u, 0.8f)));
                d = (d + Vector3.up * bendUp + RandDir(rnd) * wobble).normalized;
                p += d * step;
            }
            return l;
        }

        /// <summary>
        /// The tube of a branch into the kit: rings of sides points (parallel-transported frames), the radius shaped by
        /// shape(row, angle) (root flare, bark ridges), coloured by colour(row, angle, ridge).
        /// </summary>
        private static void Tube(MeshKit kit, Limb l, int sides, System.Func<int, float, float> shape, System.Func<int, float, Vector3, Color> colour, int stride = 1)
        {
            var rows = new List<int>();
            for (int i = 0; i < l.P.Count; i += stride) rows.Add(i);
            if (rows[rows.Count - 1] != l.P.Count - 1) rows.Add(l.P.Count - 1);
            var p = new Vector3[rows.Count, sides];
            var col = new Color[rows.Count, sides];
            Vector3 n = Vector3.Cross(l.DirAt(0f), Vector3.right);
            if (n.sqrMagnitude < 1e-4f) n = Vector3.Cross(l.DirAt(0f), Vector3.forward);
            n.Normalize();
            for (int k = 0; k < rows.Count; k++)
            {
                int i = rows[k];
                Vector3 t = (l.P[Mathf.Min(i + 1, l.P.Count - 1)] - l.P[Mathf.Max(i - 1, 0)]).normalized;
                n = (n - t * Vector3.Dot(n, t)).normalized;
                Vector3 b = Vector3.Cross(t, n);
                for (int j = 0; j < sides; j++)
                {
                    float a = j * Mathf.PI * 2f / sides;
                    Vector3 radial = n * Mathf.Cos(a) + b * Mathf.Sin(a);
                    float r = l.R[i] * shape(i, a);
                    p[k, j] = l.P[i] + radial * r;
                    col[k, j] = colour(i, a, radial);
                }
            }
            kit.Grid(p, true, BarkS, (i, j) => col[i, j]);
        }

        private static float Ridge(float a, int i, float phase) => Mathf.Abs(Mathf.Sin(a * 3f + phase + i * 0.37f + Mathf.Sin(i * 1.3f + phase) * 0.4f)); // six furrows, wandering

        // ------------------------------------------------------------------ leaf clumps

        /// <summary>Foliage being assembled: positions, normals, colours, triangles (the wind shader takes plain vertex colours).</summary>
        private sealed class Leaves
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetTriangles(T, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        private static readonly Dictionary<int, (Vector3[] v, int[] t)> spheres = new Dictionary<int, (Vector3[], int[])>();

        /// <summary>Unit icosphere (subdiv 0: 12 points, 20 faces; 1: 42 / 80), cached.</summary>
        private static (Vector3[] v, int[] t) Sphere(int subdiv)
        {
            if (spheres.TryGetValue(subdiv, out var s)) return s;
            float g = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
                new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
                new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var t = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                                    3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int k = 0; k < subdiv; k++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int m)) return m;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized);
                    mid[key] = v.Count - 1;
                    return v.Count - 1;
                }
                var nt = new List<int>();
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                t = nt;
            }
            s = (v.ToArray(), t.ToArray());
            spheres[subdiv] = s;
            return s;
        }

        /// <summary>
        /// One clump of leaves: a lumpy, slightly flattened ball (or a spray stretched along axis), its normals pulled out
        /// of the whole crown (crownCentre, by `soft`), darker towards the crown's heart (`ao` at the centre) and underneath.
        /// </summary>
        private static void Clump(Leaves leaves, System.Random rnd, Vector3 centre, Vector3 size, Quaternion rot, int subdiv, Vector3 crownCentre, float crownRadius,
                                  float soft, float tint, float lump = 0.22f, Vector3? radialAxis = null, float taper = 0f)
        {
            var (sv, st) = Sphere(subdiv);
            int start = leaves.V.Count;
            float seed = Rand(rnd, 0f, 100f);
            Color hue = new Color(tint * Rand(rnd, 0.97f, 1.03f), tint * Rand(rnd, 0.98f, 1.06f), tint * Rand(rnd, 0.88f, 1.02f));
            for (int i = 0; i < sv.Length; i++)
            {
                Vector3 d = sv[i];
                float bump = 1f + lump * (Mathf.PerlinNoise(d.x * 1.7f + seed, d.z * 1.7f + d.y * 0.9f + seed) - 0.5f) * 2f;
                Vector3 local = Vector3.Scale(d, size) * bump;
                if (taper > 0f && d.z > 0f) { local.x *= 1f - taper * d.z; local.y *= 1f - taper * d.z; } // a spray narrows to its tip
                if (local.y < 0f) local.y *= 0.72f; // clumps hang a little flat underneath
                Vector3 p = centre + rot * local;
                Vector3 out_ = p - crownCentre;
                if (radialAxis.HasValue) out_ -= radialAxis.Value * Vector3.Dot(out_, radialAxis.Value) * 0.7f; // a spruce: out from the trunk, not the centre
                Vector3 nrm = Vector3.Slerp((rot * d).normalized, out_.sqrMagnitude > 1e-4f ? out_.normalized : Vector3.up, soft);
                float inner = Mathf.Clamp01(out_.magnitude / Mathf.Max(0.1f, crownRadius));
                float ao = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, inner)) * Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(d.y * 0.5f + 0.5f));
                leaves.V.Add(p);
                leaves.N.Add(nrm);
                leaves.C.Add(new Color(Mathf.Clamp01(hue.r * ao), Mathf.Clamp01(hue.g * ao), Mathf.Clamp01(hue.b * ao), 1f));
            }
            for (int i = 0; i < st.Length; i++) leaves.T.Add(start + st[i]);
        }

        // ------------------------------------------------------------------ broadleaf

        private static Model Broadleaf(int v)
        {
            if (broadleafModels.TryGetValue(v, out Model cached) && cached.Wood != null) return cached;
            var rnd = new System.Random(1000 + v * 7919);
            Color bark = Color.Lerp(new Color(0.30f, 0.24f, 0.19f), new Color(0.40f, 0.31f, 0.22f), v / 3f);
            Color moss = new Color(0.30f, 0.34f, 0.17f);

            // Trunk: up from below the ground with a little lean, to the fork
            float fork = Rand(rnd, 2.2f, 2.7f);
            Vector3 lean = new Vector3(Rand(rnd, -0.12f, 0.12f), 1f, Rand(rnd, -0.12f, 0.12f));
            float r0 = 0.33f + v * 0.015f;
            Limb trunk = Grow(rnd, new Vector3(0f, -0.35f, 0f), lean, fork + 0.35f + 0.5f, r0, r0 * 0.45f, 10, 0f, 0.04f);
            float flarePhase = Rand(rnd, 0f, 6.28f);
            float TrunkShape(int i, float a)
            {
                float y = trunk.P[i].y;
                float flare = Mathf.Exp(-Mathf.Max(0f, y + 0.1f) / 0.32f);
                float lobes = 1f + 0.45f * flare * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a * 5f + flarePhase)), 2f);
                return (1f + 0.75f * flare) * lobes * (1f + 0.12f * (Ridge(a, i, flarePhase) - 0.5f));
            }
            Color BarkColour(int i, float a, Vector3 radial, Limb l)
            {
                float ridge = Ridge(a, i, flarePhase);
                Color c = bark * Mathf.Lerp(0.48f, 1.12f, ridge * ridge);
                float low = Mathf.Clamp01(1f - (l.P[i].y + 0.2f) / 1.4f);
                float north = Mathf.Clamp01(radial.z * 0.8f + 0.3f);
                return Color.Lerp(c, moss * Mathf.Lerp(0.7f, 1f, ridge), low * north * 0.55f);
            }

            var limbs = new List<Limb>();
            var twigs = new List<Limb>();
            int count = 3 + v % 2;
            Vector3 forkPoint = trunk.At(fork / (fork + 0.85f));
            float az0 = Rand(rnd, 0f, 360f);
            for (int k = 0; k < count; k++)
            {
                float az = (az0 + k * 360f / count + Rand(rnd, -18f, 18f)) * Mathf.Deg2Rad;
                float tilt = Rand(rnd, 32f, 52f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(az) * Mathf.Sin(tilt), Mathf.Cos(tilt), Mathf.Sin(az) * Mathf.Sin(tilt));
                Limb limb = Grow(rnd, forkPoint - Vector3.up * 0.25f, dir, Rand(rnd, 2.3f, 3.1f), r0 * 0.52f, 0.035f, 7, 0.06f, 0.07f);
                limbs.Add(limb);
                foreach (float u in new[] { 0.38f, 0.58f, 0.78f })
                {
                    Vector3 side = Vector3.Cross(limb.DirAt(u), Vector3.up).normalized * (twigs.Count % 2 == 0 ? 1f : -1f);
                    Vector3 td = (limb.DirAt(u) * 0.6f + side * 0.7f + Vector3.up * 0.35f).normalized;
                    twigs.Add(Grow(rnd, limb.At(u), td, Rand(rnd, 1.0f, 1.5f), limb.RadiusAt(u) * 0.55f, 0.015f, 4, 0.08f, 0.1f));
                }
            }
            Limb leader = Grow(rnd, trunk.Tip - Vector3.up * 0.3f, new Vector3(Rand(rnd, -0.2f, 0.2f), 1f, Rand(rnd, -0.2f, 0.2f)), Rand(rnd, 1.6f, 2.1f), r0 * 0.4f, 0.03f, 5, 0f, 0.08f);
            limbs.Add(leader);

            // Wood: near and far
            var wood = new MeshKit();
            Tube(wood, trunk, 24, TrunkShape, (i, a, rad) => BarkColour(i, a, rad, trunk));
            foreach (Limb l in limbs) Tube(wood, l, 7, (i, a) => 1f + 0.06f * (Ridge(a, i, 1f) - 0.5f), (i, a, rad) => bark * Mathf.Lerp(0.72f, 1.05f, Ridge(a, i, 1f)));
            foreach (Limb l in twigs) Tube(wood, l, 5, (i, a) => 1f, (i, a, rad) => bark * 0.92f);
            var woodFar = new MeshKit();
            Tube(woodFar, trunk, 6, (i, a) => TrunkShape(i, a), (i, a, rad) => bark * 0.9f, 2);
            foreach (Limb l in limbs) Tube(woodFar, l, 4, (i, a) => 1f, (i, a, rad) => bark * 0.9f, 2);

            // Crown: clumps at the branch ends and along the limbs, and a few filling its heart
            // (p, radius, outer: the clumps on the outside get the finer ball)
            var anchors = new List<(Vector3 p, float r, bool outer)>();
            foreach (Limb l in limbs) { anchors.Add((l.At(0.55f), Rand(rnd, 0.75f, 0.9f), false)); anchors.Add((l.At(0.8f), Rand(rnd, 0.8f, 0.95f), false)); anchors.Add((l.Tip, Rand(rnd, 0.85f, 1.05f), true)); }
            foreach (Limb l in twigs) { anchors.Add((l.At(0.6f), Rand(rnd, 0.7f, 0.85f), false)); anchors.Add((l.Tip, Rand(rnd, 0.75f, 0.95f), true)); }
            Vector3 centre = Vector3.zero;
            foreach (var a in anchors) centre += a.p;
            centre /= anchors.Count;
            float radius = 0f;
            foreach (var a in anchors) radius = Mathf.Max(radius, (a.p - centre).magnitude + a.r);
            for (int k = 0; k < 6; k++) anchors.Add((centre + Vector3.Scale(RandDir(rnd), new Vector3(1f, 0.6f, 1f)) * radius * 0.4f, Rand(rnd, 1.0f, 1.2f), false));
            anchors.Add((centre + Vector3.up * radius * 0.42f, 1.1f, true)); // the top, seen from a hill

            var crown = new Leaves();
            foreach (var a in anchors)
                Clump(crown, rnd, a.p, Vector3.one * a.r, Quaternion.Euler(Rand(rnd, -20f, 20f), Rand(rnd, 0f, 360f), 0f), a.outer ? 2 : 1, centre, radius, 0.8f, Rand(rnd, 0.86f, 1.0f), a.outer ? 0.2f : 0.15f);
            var crownFar = new Leaves();
            foreach (Limb l in limbs) Clump(crownFar, rnd, l.At(0.8f), Vector3.one * 1.55f, Quaternion.identity, 0, centre, radius, 0.75f, 0.95f, 0.15f);
            Clump(crownFar, rnd, centre, new Vector3(radius * 0.7f, radius * 0.5f, radius * 0.7f), Quaternion.identity, 1, centre, radius, 0.9f, 0.9f, 0.15f);

            var m = new Model
            {
                Wood = wood.ToMesh($"BroadleafWood_{v}"), WoodFar = woodFar.ToMesh($"BroadleafWoodFar_{v}"),
                Crown = crown.ToMesh($"BroadleafCrown_{v}"), CrownFar = crownFar.ToMesh($"BroadleafCrownFar_{v}")
            };
            broadleafModels[v] = m;
            return m;
        }

        // ------------------------------------------------------------------ spruce

        private static Model Spruce(int v)
        {
            if (spruceModels.TryGetValue(v, out Model cached) && cached.Wood != null) return cached;
            var rnd = new System.Random(5000 + v * 104729);
            Color bark = Color.Lerp(new Color(0.33f, 0.24f, 0.19f), new Color(0.40f, 0.30f, 0.23f), v / 3f);
            float height = 8.5f + v * 0.6f;
            float r0 = 0.22f + v * 0.01f;
            Limb trunk = Grow(rnd, new Vector3(0f, -0.3f, 0f), Vector3.up, height + 0.3f, r0, 0.025f, 12, 0f, 0.012f);
            float phase = Rand(rnd, 0f, 6.28f);
            float TrunkShape(int i, float a)
            {
                float flare = Mathf.Exp(-Mathf.Max(0f, trunk.P[i].y + 0.1f) / 0.3f);
                return (1f + 0.6f * flare) * (1f + 0.3f * flare * Mathf.Max(0f, Mathf.Cos(a * 4f + phase))) * (1f + 0.04f * (Ridge(a, i, phase) - 0.5f));
            }

            var wood = new MeshKit();
            Tube(wood, trunk, 9, TrunkShape, (i, a, rad) => bark * Mathf.Lerp(0.7f, 1.06f, Ridge(a, i, phase)));
            var woodFar = new MeshKit();
            Tube(woodFar, trunk, 5, TrunkShape, (i, a, rad) => bark * 0.9f, 3);

            var crown = new Leaves();
            var crownFar = new Leaves();
            float bottom = 1.7f + Rand(rnd, -0.2f, 0.2f), top = height - 0.5f;
            Vector3 axisCentre = new Vector3(0f, (bottom + top) * 0.5f, 0f);
            float maxReach = 2.5f + v * 0.12f;

            // Dead stubs on the bare trunk under the crown
            for (int k = 0; k < 5; k++)
            {
                float y = Rand(rnd, 0.9f, bottom);
                float az = Rand(rnd, 0f, 6.28f);
                Vector3 d = new Vector3(Mathf.Cos(az), -0.25f, Mathf.Sin(az));
                Tube(wood, Grow(rnd, new Vector3(0f, y, 0f), d, Rand(rnd, 0.25f, 0.6f), 0.03f, 0.008f, 3, 0f, 0.15f), 4, (i, a) => 1f, (i, a, rad) => bark * 0.75f);
            }

            int whorl = 0;
            for (float y = bottom; y < top; y += Rand(rnd, 0.36f, 0.5f), whorl++)
            {
                float h = Mathf.InverseLerp(bottom, top, y);
                float reach = 0.35f + maxReach * Mathf.Pow(1f - h, 1.05f);
                int n = 6 + (whorl % 2);
                float az0 = whorl * 0.4f + Rand(rnd, 0f, 1f);
                for (int k = 0; k < n; k++)
                {
                    float az = az0 + k * Mathf.PI * 2f / n + Rand(rnd, -0.25f, 0.25f);
                    Vector3 outDir = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                    Vector3 dir = (outDir + Vector3.down * Rand(rnd, 0.15f, 0.35f)).normalized;
                    float len = reach * Rand(rnd, 0.85f, 1.1f);
                    Limb branch = Grow(rnd, new Vector3(0f, y, 0f), dir, len, Mathf.Lerp(0.06f, 0.025f, h), 0.01f, 4, 0.12f, 0.05f);
                    if (whorl < 3) Tube(wood, branch, 4, (i, a) => 1f, (i, a, rad) => bark * 0.85f);
                    // The needle spray: a long flat pad along the branch, drooping, its normals out from the trunk
                    Vector3 mid = branch.At(0.55f) + Vector3.down * 0.08f;
                    Quaternion rot = Quaternion.LookRotation(branch.DirAt(0.5f), Vector3.up) * Quaternion.Euler(Rand(rnd, 8f, 22f), 0f, Rand(rnd, -18f, 18f)); // drooping, a little rolled
                    Vector3 size = new Vector3(Mathf.Lerp(0.55f, 0.3f, h) + len * 0.1f, Mathf.Lerp(0.34f, 0.24f, h), len * 0.58f);
                    float tint = Mathf.Lerp(0.74f, 1f, h) * Rand(rnd, 0.9f, 1.04f);
                    Clump(crown, rnd, mid, size, rot, len > 1.1f ? 1 : 0, axisCentre, reach + 0.4f, 0.55f, tint, 0.3f, Vector3.up, 0.55f);
                    if (len > 1.2f) Clump(crown, rnd, branch.At(0.25f), size * 0.6f, rot, 0, axisCentre, reach + 0.4f, 0.55f, tint * 0.9f, 0.3f, Vector3.up, 0.4f);
                    if (k % 2 == 0 && whorl % 2 == 0)
                        Clump(crownFar, rnd, mid, size * 1.35f, rot, 0, axisCentre, maxReach, 0.7f, tint, 0.15f, Vector3.up);
                }
            }
            // The leader: a tuft at the very top
            Quaternion upRot = Quaternion.LookRotation(Vector3.up, Vector3.forward);
            Clump(crown, rnd, new Vector3(0f, top + 0.15f, 0f), new Vector3(0.2f, 0.2f, 0.55f), upRot, 0, axisCentre, maxReach, 0.4f, 1f, 0.2f, Vector3.up, 0.7f);
            Clump(crownFar, rnd, new Vector3(0f, top - 0.3f, 0f), new Vector3(0.5f, 0.5f, 1.2f), upRot, 0, axisCentre, maxReach, 0.5f, 1f, 0.1f, Vector3.up);

            var m = new Model
            {
                Wood = wood.ToMesh($"SpruceWood_{v}"), WoodFar = woodFar.ToMesh($"SpruceWoodFar_{v}"),
                Crown = crown.ToMesh($"SpruceCrown_{v}"), CrownFar = crownFar.ToMesh($"SpruceCrownFar_{v}")
            };
            spruceModels[v] = m;
            return m;
        }

        // ------------------------------------------------------------------ stump and logs

        private static Color CutWood(float r, float max)
        {
            float rings = Mathf.Abs(Mathf.Sin(r / max * Mathf.PI * 9f));
            Color pale = new Color(0.78f, 0.64f, 0.44f), dark = new Color(0.62f, 0.47f, 0.30f);
            Color c = Color.Lerp(pale, dark, rings * 0.6f);
            if (r > max * 0.86f) c = new Color(0.55f, 0.40f, 0.26f); // the sapwood ring under the bark
            if (r < max * 0.08f) c = new Color(0.50f, 0.34f, 0.20f); // the pith
            return c;
        }

        private static Mesh BuildStump()
        {
            var kit = new MeshKit();
            Color bark = new Color(0.34f, 0.26f, 0.20f);
            var prof = new[] { new Vector2(0.62f, -0.15f), new Vector2(0.55f, 0.02f), new Vector2(0.44f, 0.14f), new Vector2(0.38f, 0.28f), new Vector2(0.36f, 0.44f) };
            kit.Lathe(Matrix4x4.identity, prof, 14, BarkS, (i, a) => bark * Mathf.Lerp(0.7f, 1.05f, Mathf.Abs(Mathf.Sin(a * 4.5f + i * 0.4f))),
                      (i, a) => 1f + (i < 2 ? 0.25f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a * 5f)), 2f) : 0f) + 0.04f * Mathf.Sin(a * 9f));
            // The cut: slightly slanted, a little ragged at the hinge
            var cut = new List<Vector3>();
            for (int j = 0; j < 14; j++)
            {
                float a = j * Mathf.PI * 2f / 14f;
                float r = 0.36f * (1f + 0.04f * Mathf.Sin(a * 9f)) * 0.98f;
                cut.Add(new Vector3(Mathf.Cos(a) * r, 0.44f + Mathf.Cos(a) * 0.04f, Mathf.Sin(a) * r));
            }
            kit.Polygon(cut, new Vector3(0f, 0f, 0f), CutS, q => CutWood(new Vector2(q.x, q.z).magnitude, 0.36f));
            return kit.ToMesh("TreeStump");
        }

        private static Mesh BuildLog()
        {
            var kit = new MeshKit();
            Color bark = new Color(0.36f, 0.27f, 0.20f);
            const float half = 0.8f, r = 0.17f;
            var prof = new Vector2[6];
            for (int i = 0; i < prof.Length; i++) prof[i] = new Vector2(r * (1f + 0.04f * Mathf.Sin(i * 2.1f)), Mathf.Lerp(-half, half, i / (prof.Length - 1f)));
            Matrix4x4 alongX = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -90f));
            kit.Lathe(alongX, prof, 10, BarkS, (i, a) => bark * Mathf.Lerp(0.7f, 1.05f, Mathf.Abs(Mathf.Sin(a * 4.5f + i * 0.5f))));
            foreach (float s in new[] { -1f, 1f })
            {
                var end = new List<Vector3>();
                for (int j = 0; j < 10; j++)
                {
                    float a = j * Mathf.PI * 2f / 10f;
                    end.Add(alongX.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * prof[s < 0 ? 0 : prof.Length - 1].x, s * half, Mathf.Sin(a) * prof[s < 0 ? 0 : prof.Length - 1].x)));
                }
                kit.Polygon(end, Vector3.zero, CutS, q => CutWood(new Vector2(q.y, q.z).magnitude, r));
            }
            return kit.ToMesh("TreeLog");
        }
    }
}
