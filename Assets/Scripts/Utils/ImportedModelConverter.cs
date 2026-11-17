using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Turns an imported Blender hierarchy (an FBX instance) into an ImportedModel by the rules of docs/MODELS.md:
    /// children named &lt;Model&gt;_&lt;Part&gt; (not _Body) are moving parts with their own frame, Socket_* empties are sockets,
    /// &lt;Model&gt;_Far is the hand-made distant mesh, everything else merges into the body (frame = the &lt;Model&gt; object,
    /// its origin is the pivot). Materials named S_&lt;Surface&gt; become the per-vertex surface (uv2) and their base colour the
    /// vertex colour; a mesh with its own colour attribute keeps that instead. Without _Far the distant mesh is the
    /// whole model clustered on a grid down to the category's budget. Pure UnityEngine, so EditMode tests can run it.
    /// </summary>
    public static class ImportedModelConverter
    {
        public static readonly string[] Categories = { "Weapons", "Armor", "Props", "Buildings", "Creatures" };

        /// <summary>Triangle budgets near / far per category (docs/MODELS.md §5).</summary>
        public static (int near, int far) BudgetFor(string category)
        {
            switch (category)
            {
                case "Weapons": return (400, 80);
                case "Armor": return (600, 120);
                case "Props": return (2000, 300);
                case "Buildings": return (6000, 800);
                default: return (3000, 600);
            }
        }

        /// <summary>S_&lt;name&gt; → (pattern, metallic, smoothness) of Custom/CharacterSurface, values as the code builders use.</summary>
        public static readonly IReadOnlyDictionary<string, Vector3> Surfaces = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase)
        {
            ["Skin"] = new Vector3(0f, 0f, 0.22f),
            ["Cloth"] = new Vector3(1f, 0f, 0.15f),
            ["Leather"] = new Vector3(2f, 0f, 0.36f),
            ["Metal"] = new Vector3(3f, 0.8f, 0.42f),
            ["Iron"] = new Vector3(3f, 0.8f, 0.42f),
            ["Blade"] = new Vector3(3f, 0.7f, 0.28f),
            ["Steel"] = new Vector3(3f, 0.9f, 0.7f),
            ["Bronze"] = new Vector3(3f, 0.85f, 0.5f),
            ["Gold"] = new Vector3(3f, 0.95f, 0.72f),
            ["Hair"] = new Vector3(4f, 0f, 0.3f),
            ["Wood"] = new Vector3(4f, 0f, 0.22f),
            ["Bark"] = new Vector3(4f, 0f, 0.1f),
            ["Eyes"] = new Vector3(5f, 0f, 0.9f),
            ["Fur"] = new Vector3(6f, 0f, 0.1f),
            ["Mail"] = new Vector3(7f, 0.85f, 0.5f),
            ["Stone"] = new Vector3(8f, 0f, 0.2f),
            ["Plaster"] = new Vector3(8f, 0f, 0.15f),
        };

        private static readonly Vector3 FallbackSurface = new Vector3(1f, 0f, 0.15f);

        /// <summary>Surface of a material name ("S_Metal", "S_Metal.001", "S_Wood_Dark"); false and cloth if unknown.</summary>
        public static bool TryGetSurface(string materialName, out Vector3 surface)
        {
            string n = materialName ?? "";
            int cut = n.IndexOf(" (", StringComparison.Ordinal); // " (Instance)"
            if (cut >= 0) n = n.Substring(0, cut);
            cut = n.IndexOf('.'); // Blender's duplicate suffix
            if (cut >= 0) n = n.Substring(0, cut);
            if (n.StartsWith("S_", StringComparison.OrdinalIgnoreCase))
            {
                string key = n.Substring(2);
                if (Surfaces.TryGetValue(key, out surface)) return true;
                int us = key.IndexOf('_');
                if (us > 0 && Surfaces.TryGetValue(key.Substring(0, us), out surface)) return true;
            }
            surface = FallbackSurface;
            return false;
        }

        private sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(), S = new List<Vector3>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();
            public int Triangles => T.Count / 3;

            public void Add(Mesh mesh, Material[] mats, Matrix4x4 toFrame, ICollection<string> unknown, IDictionary<string, string> seen, List<string> report, string where)
            {
                Vector3[] verts = mesh.vertices, normals = mesh.normals;
                Color[] colors = mesh.colors;
                bool hasNormals = normals.Length == verts.Length, hasColors = colors.Length == verts.Length;
                Matrix4x4 nm = toFrame.inverse.transpose;
                bool mirrored = toFrame.determinant < 0f;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) { report.Add($"  ! {where}: submesh {sub} is not triangles, skipped"); continue; }
                    Material mat = mats != null && mats.Length > 0 ? mats[Mathf.Min(sub, mats.Length - 1)] : null;
                    string matName = mat != null ? mat.name : "(no material)";
                    if (!TryGetSurface(matName, out Vector3 surface)) unknown.Add(matName);
                    Color matColor = mat == null ? Color.white : mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.HasProperty("_Color") ? mat.color : Color.white;
                    seen[matName] = $"{(hasColors ? "vertex colours" : "#" + ColorUtility.ToHtmlStringRGB(matColor))} ({surface.x:0}, {surface.y:0.##}, {surface.z:0.##})";
                    int[] tris = mesh.GetTriangles(sub);
                    var remap = new Dictionary<int, int>();
                    int Vert(int i)
                    {
                        if (remap.TryGetValue(i, out int o)) return o;
                        o = V.Count;
                        V.Add(toFrame.MultiplyPoint3x4(verts[i]));
                        N.Add(hasNormals ? nm.MultiplyVector(normals[i]).normalized : Vector3.zero);
                        Color c = hasColors ? colors[i] : matColor;
                        C.Add(new Color(c.r, c.g, c.b, 1f).linear); // Blender and Unity colours are sRGB; the shader takes linear
                        S.Add(surface);
                        remap[i] = o;
                        return o;
                    }
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        int a = Vert(tris[t]), b = Vert(tris[t + 1]), c = Vert(tris[t + 2]);
                        T.Add(a);
                        if (mirrored) { T.Add(c); T.Add(b); } else { T.Add(b); T.Add(c); }
                    }
                    if (!hasNormals) report.Add($"  ! {where}: no normals, recalculated");
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(V);
                mesh.SetColors(C);
                mesh.SetUVs(2, S);
                mesh.SetTriangles(T, 0);
                bool normalsOk = N.Count == V.Count && N.TrueForAll(n => n.sqrMagnitude > 0.5f);
                if (normalsOk) mesh.SetNormals(N); else mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }

            public Bounds Bounds()
            {
                if (V.Count == 0) return new Bounds();
                var b = new Bounds(V[0], Vector3.zero);
                foreach (Vector3 v in V) b.Encapsulate(v);
                return b;
            }
        }

        private sealed class PartBuilder
        {
            public string Name, Parent;
            public Matrix4x4 Frame; // in the source root's space, no scale
            public readonly Builder Mesh = new Builder();
        }

        private enum Kind { Merge, Part, Socket, Far }

        /// <summary>
        /// Blender's front (-Y, the Front view) arrives in Unity as -Z; the game's models face +Z (WorkerHouseBuilder,
        /// WorkbenchBuilder), so Blender files are turned half round Y. Unity = (-x, z, -y) of Blender.
        /// </summary>
        public static readonly Quaternion BlenderToGame = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>
        /// Converts the hierarchy under source (an FBX instance at the origin), turned by fileToGame (BlenderToGame for
        /// Blender files); report gets a summary and warnings ("  ! ...").
        /// </summary>
        public static ImportedModel Convert(GameObject source, string category, List<string> report, Quaternion? fileToGame = null)
        {
            Transform src = source.transform;
            Transform modelRoot = src;
            foreach (Transform c in src) if (c.name == src.name) { modelRoot = c; break; }
            string modelName = modelRoot.name, prefix = modelName + "_";
            Matrix4x4 srcInv = src.worldToLocalMatrix;
            Quaternion turn = fileToGame ?? Quaternion.identity;
            Matrix4x4 Rel(Transform t) => srcInv * t.localToWorldMatrix;
            // Every frame turned in itself: meshes, poses and sockets all come out in the game's axes
            Matrix4x4 FrameOf(Transform t) { Matrix4x4 m = Rel(t); return Matrix4x4.TRS(m.GetColumn(3), m.rotation * turn, Vector3.one); }
            if (modelRoot != src)
            {
                Matrix4x4 r = Rel(modelRoot);
                if (Quaternion.Angle(r.rotation, Quaternion.identity) > 1f) report.Add($"  ! {modelName} is turned in the file: apply its rotation in Blender (the pivot frame must be the world's)");
                if (Mathf.Abs(r.lossyScale.x - 1f) > 0.01f || Mathf.Abs(r.lossyScale.y - 1f) > 0.01f || Mathf.Abs(r.lossyScale.z - 1f) > 0.01f)
                    report.Add($"  ! {modelName} is scaled {r.lossyScale}: apply its scale in Blender (sizes are baked into the meshes anyway)");
            }

            Kind KindOf(Transform t)
            {
                string n = t.name;
                if (n.StartsWith("Socket_", StringComparison.OrdinalIgnoreCase)) return Kind.Socket;
                if (n == prefix + "Far" || n.EndsWith("_LOD1", StringComparison.Ordinal)) return Kind.Far;
                if (n.StartsWith(prefix, StringComparison.Ordinal) && n != prefix + "Body") return Kind.Part;
                return Kind.Merge;
            }

            var body = new PartBuilder { Name = prefix + "Body", Parent = "", Frame = FrameOf(modelRoot) };
            var parts = new List<PartBuilder> { body };
            var sockets = new List<ImportedModel.Socket>();
            var all = new Builder();
            var far = new Builder();
            var unknown = new SortedSet<string>(StringComparer.Ordinal);
            var seen = new SortedDictionary<string, string>(StringComparer.Ordinal);
            Matrix4x4 bodyInv = body.Frame.inverse;
            bool ownFar = false;

            void AddMeshOf(Transform node, Builder into, Matrix4x4 frameInv, Builder alsoAll)
            {
                MeshFilter mf = node.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) return;
                Renderer r = node.GetComponent<Renderer>();
                Material[] mats = r != null ? r.sharedMaterials : null;
                into.Add(mf.sharedMesh, mats, frameInv * Rel(node), unknown, seen, report, node.name);
                alsoAll?.Add(mf.sharedMesh, mats, bodyInv * Rel(node), new List<string>(), new Dictionary<string, string>(), new List<string>(), node.name);
            }

            void CollectFar(Transform node)
            {
                AddMeshOf(node, far, bodyInv, null);
                foreach (Transform c in node) CollectFar(c);
            }

            void Collect(Transform node, PartBuilder owner)
            {
                AddMeshOf(node, owner.Mesh, owner.Frame.inverse, all);
                foreach (Transform c in node)
                {
                    switch (KindOf(c))
                    {
                        case Kind.Socket:
                            Matrix4x4 pose = owner.Frame.inverse * FrameOf(c);
                            sockets.Add(new ImportedModel.Socket { Name = c.name, Parent = owner == body ? "" : owner.Name, Position = pose.GetColumn(3), Rotation = pose.rotation });
                            break;
                        case Kind.Far:
                            ownFar = true;
                            CollectFar(c);
                            break;
                        case Kind.Part:
                            var part = new PartBuilder { Name = c.name, Parent = owner == body ? "" : owner.Name, Frame = FrameOf(c) };
                            parts.Add(part);
                            Collect(c, part);
                            break;
                        default:
                            Collect(c, owner);
                            break;
                    }
                }
            }

            Collect(src, body);

            ImportedModel model = ScriptableObject.CreateInstance<ImportedModel>();
            model.name = modelName;
            model.Category = category;
            var frames = new Dictionary<string, Matrix4x4> { [""] = body.Frame };
            foreach (PartBuilder p in parts)
            {
                Matrix4x4 parentFrame = frames.TryGetValue(p.Parent, out Matrix4x4 f) ? f : body.Frame;
                Matrix4x4 pose = p == body ? Matrix4x4.identity : parentFrame.inverse * p.Frame;
                frames[p.Name] = p.Frame;
                model.Parts.Add(new ImportedModel.Part
                {
                    Name = p.Name, Parent = p.Parent, Position = pose.GetColumn(3), Rotation = pose.rotation,
                    Mesh = p.Mesh.Triangles > 0 ? p.Mesh.ToMesh(p.Name) : null,
                });
                model.NearTriangles += p.Mesh.Triangles;
            }
            model.Sockets.AddRange(sockets);
            model.Bounds = all.Bounds();

            (int nearBudget, int farBudget) = BudgetFor(category);
            Builder farMesh = ownFar ? far : all.Triangles <= farBudget ? all : Simplify(all, farBudget);
            if (farMesh.Triangles > 0) model.Far = farMesh.ToMesh(modelName + "_Far");
            model.FarTriangles = farMesh.Triangles;

            Vector3 size = model.Bounds.size;
            report.Add($"{category}/{modelName}: near {model.NearTriangles}/{nearBudget} tris, far {model.FarTriangles}/{farBudget} ({(ownFar ? "own _Far" : "auto")}), " +
                       $"size {size.x:0.###} x {size.y:0.###} x {size.z:0.###} m");
            report.Add($"  parts: {string.Join(", ", model.Parts.ConvertAll(p => $"{p.Name} ({(p.Mesh != null ? p.Mesh.triangles.Length / 3 : 0)} tris{(p.Parent == "" ? "" : " on " + p.Parent)} at ({p.Position.x:0.###}, {p.Position.y:0.###}, {p.Position.z:0.###}), turned {p.Rotation.eulerAngles.x:0}/{p.Rotation.eulerAngles.y:0}/{p.Rotation.eulerAngles.z:0})"))}");
            report.Add($"  sockets: {(sockets.Count == 0 ? "none" : string.Join(", ", sockets.ConvertAll(s => $"{s.Name}{(s.Parent == "" ? "" : " on " + s.Parent)} at ({s.Position.x:0.###}, {s.Position.y:0.###}, {s.Position.z:0.###})")))}");
            report.Add($"  materials: {string.Join(", ", System.Linq.Enumerable.Select(seen, kv => kv.Key + " " + kv.Value))}");
            if (model.NearTriangles == 0) report.Add("  ! no geometry");
            if (model.NearTriangles > nearBudget) report.Add($"  ! near mesh over budget ({model.NearTriangles} > {nearBudget})");
            if (model.FarTriangles > farBudget) report.Add($"  ! far mesh over budget ({model.FarTriangles} > {farBudget})");
            foreach (string u in unknown) report.Add($"  ! material \"{u}\" is not S_<Surface>: drawn as cloth");
            return model;
        }

        /// <summary>
        /// Vertex clustering on a grid laid over the bounds (n cells per axis, so thin blades keep their width), split by
        /// normal direction and surface pattern so faces and materials do not melt together; the finest grid within target.
        /// </summary>
        private static Builder Simplify(Builder src, int target)
        {
            Builder best = null, coarsestNonEmpty = null;
            for (int n = 48; n >= 1; n--)
            {
                Builder b = Cluster(src, n);
                if (b.Triangles == 0) continue;
                coarsestNonEmpty = b;
                if (b.Triangles <= target) { best = b; break; }
            }
            return best ?? coarsestNonEmpty ?? src;
        }

        private static Builder Cluster(Builder src, int n)
        {
            Bounds bb = src.Bounds();
            Vector3 min = bb.min, size = Vector3.Max(bb.size, Vector3.one * 1e-4f);
            var index = new Dictionary<long, int>();
            var remap = new int[src.V.Count];
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var col = new List<Color>(); var cnt = new List<int>();
            var outB = new Builder();
            for (int i = 0; i < src.V.Count; i++)
            {
                Vector3 r = src.V[i] - min;
                int cx = Mathf.Clamp((int)(r.x / size.x * n), 0, n - 1), cy = Mathf.Clamp((int)(r.y / size.y * n), 0, n - 1), cz = Mathf.Clamp((int)(r.z / size.z * n), 0, n - 1);
                Vector3 nv = src.N[i];
                Vector3 an = new Vector3(Mathf.Abs(nv.x), Mathf.Abs(nv.y), Mathf.Abs(nv.z));
                int axis = an.x >= an.y && an.x >= an.z ? 0 : an.y >= an.z ? 1 : 2;
                int bucket = axis * 2 + (nv[axis] >= 0f ? 1 : 0);
                int pattern = Mathf.Clamp(Mathf.RoundToInt(src.S[i].x), 0, 15);
                long key = ((((long)cx * n + cy) * n + cz) * 6 + bucket) * 16 + pattern;
                if (!index.TryGetValue(key, out int o))
                {
                    o = pos.Count;
                    index[key] = o;
                    pos.Add(Vector3.zero); nrm.Add(Vector3.zero); col.Add(Color.clear); cnt.Add(0);
                    outB.S.Add(src.S[i]);
                }
                pos[o] += src.V[i]; nrm[o] += nv; col[o] += src.C[i]; cnt[o]++;
                remap[i] = o;
            }
            for (int o = 0; o < pos.Count; o++)
            {
                outB.V.Add(pos[o] / cnt[o]);
                outB.N.Add(nrm[o].sqrMagnitude > 1e-8f ? nrm[o].normalized : Vector3.zero);
                Color c = col[o] / cnt[o];
                c.a = 1f;
                outB.C.Add(c);
            }
            var seen = new HashSet<(int, int, int)>();
            for (int t = 0; t + 2 < src.T.Count; t += 3)
            {
                int a = remap[src.T[t]], b = remap[src.T[t + 1]], c = remap[src.T[t + 2]];
                if (a == b || b == c || a == c) continue;
                // the same triangle once (rotations of it too)
                int lo = Mathf.Min(a, Mathf.Min(b, c));
                (int, int, int) k = lo == a ? (a, b, c) : lo == b ? (b, c, a) : (c, a, b);
                if (!seen.Add(k)) continue;
                outB.T.Add(a); outB.T.Add(b); outB.T.Add(c);
            }
            return outB;
        }
    }
}
