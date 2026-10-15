using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Builds worn gear into the villager-rig character mesh (one skinned mesh, one material, colour and surface per
    /// vertex like RiggedCharacterBuilder.BakeCharacter). Armour is fitted to the body: rings of the body's own rest
    /// vertices (picked by their dominant bone) are measured around the limb or torso axis and pushed out by a margin,
    /// so a cuirass, helm, gauntlet cuff or greave hugs that body. Gloves and boots also recolour the hands and feet.
    /// Weapons and the shield are MeshKit models in the hand / forearm frame. Every piece is skinned to its bone(s);
    /// pieces and composed meshes are cached (identically equipped units share one mesh). The level sets the finish:
    /// I leather / bronze / plain wood, II iron and mail, III steel, IV blued steel, V steel with gold.
    /// Work happens in root space (metres, +Y up, +Z forward), converted to the mesh's bind space at the end.
    /// </summary>
    public static class GearMeshBuilder
    {
        // ------------------------------------------------------------------ finishes

        private struct Finish
        {
            public Color Main, Trim, Dark;
            public Vector3 MainS, TrimS, DarkS;
        }

        private static readonly Vector3 LeatherS = new Vector3(2f, 0f, 0.36f);
        private static readonly Vector3 WoodS = new Vector3(4f, 0f, 0.24f);
        private static readonly Vector3 MailS = new Vector3(7f, 0.85f, 0.5f);
        private static Vector3 MetalS(float metallic, float smooth) => new Vector3(3f, metallic, smooth);

        private static readonly Color Leather = new Color(0.44f, 0.27f, 0.14f);
        private static readonly Color DarkLeather = new Color(0.20f, 0.12f, 0.07f);
        private static readonly Color Gold = new Color(0.93f, 0.70f, 0.28f);
        private static readonly Vector3 GoldS = new Vector3(3f, 0.95f, 0.72f);

        private static Finish ArmourFinish(int level, bool mailAtTwo)
        {
            switch (ItemStatCalculator.ClampLevel(level))
            {
                case 1:
                    return new Finish { Main = Leather, MainS = LeatherS, Trim = DarkLeather, TrimS = LeatherS, Dark = new Color(0.13f, 0.08f, 0.05f), DarkS = LeatherS };
                case 2:
                    return mailAtTwo
                        ? new Finish { Main = new Color(0.50f, 0.50f, 0.53f), MainS = MailS, Trim = DarkLeather, TrimS = LeatherS, Dark = new Color(0.13f, 0.08f, 0.05f), DarkS = LeatherS }
                        : new Finish { Main = new Color(0.46f, 0.47f, 0.49f), MainS = MetalS(0.82f, 0.42f), Trim = new Color(0.30f, 0.30f, 0.32f), TrimS = MetalS(0.8f, 0.35f), Dark = DarkLeather, DarkS = LeatherS };
                case 3:
                    return new Finish { Main = new Color(0.74f, 0.76f, 0.80f), MainS = MetalS(0.9f, 0.68f), Trim = new Color(0.44f, 0.45f, 0.48f), TrimS = MetalS(0.85f, 0.5f), Dark = DarkLeather, DarkS = LeatherS };
                case 4:
                    return new Finish { Main = new Color(0.16f, 0.20f, 0.30f), MainS = MetalS(0.88f, 0.78f), Trim = new Color(0.80f, 0.82f, 0.86f), TrimS = MetalS(0.9f, 0.74f), Dark = new Color(0.10f, 0.08f, 0.07f), DarkS = LeatherS };
                default:
                    return new Finish { Main = new Color(0.84f, 0.85f, 0.88f), MainS = MetalS(0.92f, 0.8f), Trim = Gold, TrimS = GoldS, Dark = new Color(0.30f, 0.07f, 0.06f), DarkS = new Vector3(1f, 0f, 0.3f) };
            }
        }

        /// <summary>Blade / axe head / boss colour and the fittings (guard, pommel) for a weapon level.</summary>
        private static Finish WeaponFinish(int level)
        {
            switch (ItemStatCalculator.ClampLevel(level))
            {
                case 1: return new Finish { Main = new Color(0.74f, 0.50f, 0.27f), MainS = MetalS(0.9f, 0.55f), Trim = new Color(0.60f, 0.40f, 0.22f), TrimS = MetalS(0.85f, 0.45f), Dark = DarkLeather, DarkS = LeatherS };
                case 2: return new Finish { Main = new Color(0.52f, 0.53f, 0.55f), MainS = MetalS(0.85f, 0.45f), Trim = new Color(0.32f, 0.32f, 0.34f), TrimS = MetalS(0.8f, 0.4f), Dark = DarkLeather, DarkS = LeatherS };
                case 3: return new Finish { Main = new Color(0.80f, 0.82f, 0.86f), MainS = MetalS(0.9f, 0.75f), Trim = new Color(0.40f, 0.41f, 0.44f), TrimS = MetalS(0.85f, 0.55f), Dark = DarkLeather, DarkS = LeatherS };
                case 4: return new Finish { Main = new Color(0.20f, 0.25f, 0.36f), MainS = MetalS(0.88f, 0.8f), Trim = new Color(0.80f, 0.82f, 0.86f), TrimS = MetalS(0.9f, 0.74f), Dark = new Color(0.10f, 0.08f, 0.07f), DarkS = LeatherS };
                default: return new Finish { Main = new Color(0.88f, 0.89f, 0.92f), MainS = MetalS(0.92f, 0.82f), Trim = Gold, TrimS = GoldS, Dark = new Color(0.32f, 0.07f, 0.06f), DarkS = LeatherS };
            }
        }

        /// <summary>Weapon metal and fittings for a level (shared with the first-person weapon).</summary>
        public static void LevelMetals(int level, out Color main, out Vector3 mainS, out Color trim, out Vector3 trimS)
        {
            Finish f = WeaponFinish(level);
            main = f.Main; mainS = f.MainS; trim = f.Trim; trimS = f.TrimS;
        }

        private static Color WoodColour(int level) => level <= 1 ? new Color(0.52f, 0.36f, 0.20f) : level == 2 ? new Color(0.42f, 0.27f, 0.15f) : new Color(0.30f, 0.18f, 0.10f);

        // ------------------------------------------------------------------ rig (measured once)

        private sealed class Rig
        {
            public string[] Names;
            public Matrix4x4 MeshToRoot, RootToMesh, NormalToMesh;
            public Vector3[] BonePos;       // root space, rest pose
            public Vector3[] BoneX, BoneZ;   // bone axes (root space, unit; the FBX matrices may be mirrored)
            public Vector3[] Body;          // body (source mesh) vertices, root space
            public int[] Dominant;          // dominant bone per body vertex
            public int BodyCount;
            public Vector3 EyeMid;
            public bool HasEyes;
            public bool Knight;             // knight skeleton: the held weapon points along palm -X
            public int Id;

            public int Bone(string name) => Array.IndexOf(Names, name);
            public Vector3 Pos(string name) { int b = Bone(name); return b >= 0 ? BonePos[b] : Vector3.zero; }
        }

        private static readonly Dictionary<Mesh, Rig> Rigs = new Dictionary<Mesh, Rig>();
        private static readonly Dictionary<string, Piece> Pieces = new Dictionary<string, Piece>();
        private static readonly Dictionary<string, Mesh> Composed = new Dictionary<string, Mesh>();

        private static Rig RigFor(CharacterGear g)
        {
            CharacterModelLibrary lib = CharacterModelLibrary.Instance;
            Mesh src = lib == null ? null : g.KnightRig ? lib.KnightMesh : lib.VillagerMesh;
            if (src == null || !src.isReadable || g.Body == null || g.BaseMesh == null) return null;
            if (Rigs.TryGetValue(src, out Rig known)) return known;

            Transform[] bones = g.Body.bones;
            Matrix4x4[] bind = g.BaseMesh.bindposes;
            if (bones == null || bind == null || bones.Length != bind.Length) return null;

            var r = new Rig
            {
                Names = new string[bones.Length],
                BonePos = new Vector3[bones.Length],
                BoneX = new Vector3[bones.Length],
                BoneZ = new Vector3[bones.Length],
                MeshToRoot = g.transform.worldToLocalMatrix * g.Body.transform.localToWorldMatrix
            };
            r.RootToMesh = r.MeshToRoot.inverse;
            r.NormalToMesh = r.MeshToRoot.transpose; // inverse-transpose of RootToMesh
            for (int b = 0; b < bones.Length; b++)
            {
                r.Names[b] = bones[b] != null ? bones[b].name : "";
                Matrix4x4 m = r.MeshToRoot * bind[b].inverse;
                r.BonePos[b] = m.GetColumn(3);
                r.BoneX[b] = ((Vector3)m.GetColumn(0)).normalized;
                r.BoneZ[b] = ((Vector3)m.GetColumn(2)).normalized;
            }

            Vector3[] v = src.vertices;
            BoneWeight[] w = src.boneWeights;
            r.BodyCount = v.Length;
            r.Body = new Vector3[v.Length];
            r.Dominant = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                r.Body[i] = r.MeshToRoot.MultiplyPoint3x4(v[i]);
                r.Dominant[i] = w.Length == v.Length ? w[i].boneIndex0 : 0;
            }
            r.Knight = g.KnightRig;
            r.Id = src.GetInstanceID();
            r.HasEyes = !g.KnightRig && lib.VillagerEyeLeft != Vector3.zero && lib.VillagerEyeRight != Vector3.zero;
            r.EyeMid = (lib.VillagerEyeLeft + lib.VillagerEyeRight) * 0.5f;
            Rigs[src] = r;
            return r;
        }

        // ------------------------------------------------------------------ pieces

        private sealed class Piece
        {
            public Vector3[] V, N, S, Rest;
            public Color[] C;
            public BoneWeight[] W;
            public int[] T;
            public HashSet<int> RecolourVerts;  // body vertices this piece recolours (gloves, boots)
            public Color RecolourColour;        // linear
            public Vector3 RecolourSurface;
        }

        /// <summary>MeshKit plus a bone weight per vertex.</summary>
        private sealed class Kit
        {
            public readonly MeshKit K = new MeshKit();
            public readonly List<BoneWeight> W = new List<BoneWeight>();
            public void Weigh(Func<Vector3, BoneWeight> f) { for (int i = W.Count; i < K.V.Count; i++) W.Add(f(K.V[i])); }
            public void Weigh(int bone) => Weigh(_ => One(bone));
        }

        private static BoneWeight One(int bone) => new BoneWeight { boneIndex0 = Mathf.Max(0, bone), weight0 = 1f };
        private static BoneWeight Blend(int a, int b, float tb)
        {
            tb = Mathf.Clamp01(tb);
            if (tb <= 0.001f || b < 0) return One(a);
            if (tb >= 0.999f || a < 0) return One(b);
            return tb > 0.5f ? new BoneWeight { boneIndex0 = b, weight0 = tb, boneIndex1 = a, weight1 = 1f - tb }
                             : new BoneWeight { boneIndex0 = a, weight0 = 1f - tb, boneIndex1 = b, weight1 = tb };
        }

        private static Piece ToPiece(Rig r, Kit kit)
        {
            int n = kit.K.V.Count;
            var p = new Piece { V = new Vector3[n], N = new Vector3[n], S = kit.K.S.ToArray(), Rest = kit.K.V.ToArray(), C = kit.K.C.ToArray(), W = kit.W.ToArray(), T = kit.K.T.ToArray() };
            for (int i = 0; i < n; i++)
            {
                p.V[i] = r.RootToMesh.MultiplyPoint3x4(kit.K.V[i]);
                p.N[i] = r.NormalToMesh.MultiplyVector(kit.K.N[i]).normalized;
            }
            return p;
        }

        private static Piece PieceFor(Rig r, Item item, string side = "")
        {
            string key = r.Id + ":" + (int)item.Type + ":" + item.Level + side;
            if (Pieces.TryGetValue(key, out Piece p)) return p;
            var kit = new Kit();
            int level = ItemStatCalculator.ClampLevel(item.Level);
            HashSet<int> recolour = null;
            Color recolourColour = Color.white;
            Vector3 recolourSurface = Vector3.zero;
            switch (item.Type)
            {
                case ItemType.Armor: BuildChest(r, kit, level); break;
                case ItemType.Helmet: BuildHelmet(r, kit, level); break;
                case ItemType.Shield: BuildShield(r, kit, level); break;
                case ItemType.Gloves:
                    BuildGloves(r, kit, level, out recolour);
                    Finish gf = ArmourFinish(level, true);
                    recolourColour = gf.Main; recolourSurface = gf.MainS;
                    break;
                case ItemType.Boots:
                    BuildBoots(r, kit, level, out recolour);
                    recolourColour = level == 1 || level == 2 ? DarkLeather : ArmourFinish(level, false).Main;
                    recolourSurface = level <= 2 ? LeatherS : ArmourFinish(level, false).MainS;
                    break;
                default: BuildWeapon(r, kit, item.Type, level); break;
            }
            p = ToPiece(r, kit);
            if (recolour != null)
            {
                p.RecolourVerts = recolour;
                p.RecolourColour = QualitySettings.activeColorSpace == ColorSpace.Linear ? recolourColour.linear : recolourColour;
                p.RecolourSurface = recolourSurface;
            }
            Pieces[key] = p;
            return p;
        }

        // ------------------------------------------------------------------ composing

        public static Mesh Compose(CharacterGear g, IList<Item> items, string itemsKey)
        {
            Rig r = RigFor(g);
            if (r == null) return g.BaseMesh;
            string key = g.BaseMesh.GetInstanceID() + "|" + itemsKey;
            if (Composed.TryGetValue(key, out Mesh cached) && cached != null) return cached;

            Mesh b = g.BaseMesh;
            var verts = new List<Vector3>(); var normals = new List<Vector3>(); var colors = new List<Color>();
            var surface = new List<Vector3>(); var rest = new List<Vector3>(); var tris = new List<int>();
            b.GetVertices(verts); b.GetNormals(normals); b.GetColors(colors); b.GetUVs(2, surface); b.GetUVs(3, rest);
            var weights = new List<BoneWeight>(b.boneWeights);
            for (int s = 0; s < b.subMeshCount; s++) tris.AddRange(b.GetTriangles(s));
            int n0 = verts.Count;
            if (colors.Count != n0) { colors.Clear(); for (int i = 0; i < n0; i++) colors.Add(Color.gray); }
            if (surface.Count != n0) { surface.Clear(); for (int i = 0; i < n0; i++) surface.Add(new Vector3(1f, 0f, 0.3f)); }
            if (rest.Count != n0) { rest.Clear(); rest.AddRange(verts); }

            foreach (Item item in items)
            {
                if (item.IsEmpty) continue;
                Piece p = PieceFor(r, item);
                if (p.RecolourVerts != null && n0 >= r.BodyCount)
                    foreach (int i in p.RecolourVerts) { colors[i] = p.RecolourColour; surface[i] = p.RecolourSurface; }
                int start = verts.Count;
                verts.AddRange(p.V); normals.AddRange(p.N); colors.AddRange(p.C); surface.AddRange(p.S); rest.AddRange(p.Rest); weights.AddRange(p.W);
                foreach (int t in p.T) tris.Add(start + t);
            }

            var mesh = new Mesh { name = b.name + "_Gear", indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetColors(colors);
            mesh.SetUVs(2, surface); mesh.SetUVs(3, rest);
            mesh.SetTriangles(tris, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = b.bindposes;
            mesh.RecalculateBounds();
            Composed[key] = mesh;
            return mesh;
        }

        // ------------------------------------------------------------------ fitting

        private static HashSet<int> BoneSet(Rig r, params string[] names)
        {
            var set = new HashSet<int>();
            foreach (string n in names) { int b = r.Bone(n); if (b >= 0) set.Add(b); }
            return set;
        }

        private static void Frame(Vector3 axis, out Vector3 u, out Vector3 w)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) < 0.8f ? Vector3.forward : Vector3.up;
            u = Vector3.ProjectOnPlane(reference, axis).normalized;
            w = Vector3.Cross(axis, u).normalized;
        }

        /// <summary>
        /// Rings around the axis a→b at the fractions ts: for each, the body vertices of the given bones within band of
        /// that cross-section, their centre, and per angle the furthest of them; returned as points pushed out by
        /// offset(row, angle). Rows or angles without vertices are filled from their neighbours.
        /// </summary>
        private static Vector3[,] FitRings(Rig r, HashSet<int> bones, Vector3 a, Vector3 b, float[] ts, int segs, float band, Func<int, float, float> offset)
        {
            Vector3 axis = b - a;
            float len = axis.magnitude;
            axis /= Mathf.Max(1e-5f, len);
            Frame(axis, out Vector3 u, out Vector3 w);
            int rows = ts.Length;
            var radius = new float[rows, segs];
            var centre = new Vector3[rows];
            var have = new bool[rows];
            var bin = new List<Vector3>();
            for (int i = 0; i < rows; i++)
            {
                float t = ts[i] * len;
                bin.Clear();
                Vector3 c = Vector3.zero;
                for (int v = 0; v < r.BodyCount; v++)
                {
                    if (!bones.Contains(r.Dominant[v])) continue;
                    Vector3 d = r.Body[v] - a;
                    if (Mathf.Abs(Vector3.Dot(d, axis) - t) > band) continue;
                    bin.Add(r.Body[v]);
                    c += r.Body[v];
                }
                Vector3 onAxis = a + axis * t;
                if (bin.Count < 4) { centre[i] = onAxis; continue; }
                c /= bin.Count;
                centre[i] = onAxis + Vector3.ProjectOnPlane(c - onAxis, axis);
                have[i] = true;
                var got = new bool[segs];
                foreach (Vector3 q in bin)
                {
                    Vector3 d = Vector3.ProjectOnPlane(q - centre[i], axis);
                    float ang = Mathf.Atan2(Vector3.Dot(d, w), Vector3.Dot(d, u));
                    int k = Mathf.RoundToInt(Mathf.Repeat(ang / (Mathf.PI * 2f), 1f) * segs) % segs;
                    float m = d.magnitude;
                    for (int dk = -1; dk <= 1; dk++)
                    {
                        int kk = (k + dk + segs) % segs;
                        if (m > radius[i, kk]) radius[i, kk] = m;
                        got[kk] = true;
                    }
                }
                FillCircular(radius, i, segs, got);
            }
            FillRows(radius, centre, have, rows, segs);

            var p = new Vector3[rows, segs];
            for (int i = 0; i < rows; i++)
            for (int k = 0; k < segs; k++)
            {
                float ang = k * Mathf.PI * 2f / segs;
                // light smoothing that never pulls the shell inside the body
                float rr = Mathf.Max(radius[i, k], (radius[i, (k + segs - 1) % segs] + radius[i, k] + radius[i, (k + 1) % segs]) / 3f);
                Vector3 dir = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                p[i, k] = centre[i] + dir * Mathf.Max(0.005f, rr + offset(i, ang));
            }
            return p;
        }

        private static void FillCircular(float[,] radius, int row, int segs, bool[] got)
        {
            for (int k = 0; k < segs; k++)
            {
                if (got[k]) continue;
                int lo = k, hi = k;
                for (int s = 1; s < segs && !got[lo]; s++) lo = (k - s + segs) % segs;
                for (int s = 1; s < segs && !got[hi]; s++) hi = (k + s) % segs;
                radius[row, k] = got[lo] && got[hi] ? (radius[row, lo] + radius[row, hi]) * 0.5f : 0.05f;
            }
        }

        private static void FillRows(float[,] radius, Vector3[] centre, bool[] have, int rows, int segs)
        {
            for (int i = 0; i < rows; i++)
            {
                if (have[i]) continue;
                int lo = -1, hi = -1;
                for (int j = i - 1; j >= 0; j--) if (have[j]) { lo = j; break; }
                for (int j = i + 1; j < rows; j++) if (have[j]) { hi = j; break; }
                int src = lo >= 0 && (hi < 0 || i - lo <= hi - i) ? lo : hi;
                if (src < 0) { for (int k = 0; k < segs; k++) radius[i, k] = 0.05f; continue; }
                for (int k = 0; k < segs; k++) radius[i, k] = radius[src, k];
                centre[i] += Vector3.ProjectOnPlane(centre[src] - centre[i], (centre[Mathf.Min(rows - 1, i + 1)] - centre[Mathf.Max(0, i - 1)]).normalized);
            }
        }

        private static Vector3 AxisPoint(Vector3[,] ring, int row)
        {
            Vector3 c = Vector3.zero;
            int segs = ring.GetLength(1);
            for (int k = 0; k < segs; k++) c += ring[row, k];
            return c / segs;
        }

        // ------------------------------------------------------------------ chest

        private static void BuildChest(Rig r, Kit kit, int level)
        {
            int hips = r.Bone("Hips"), torso = r.Bone("Torso");
            Vector3 a = r.Pos("Hips"), b = r.Pos("Neck");
            if (hips < 0 || torso < 0 || b == Vector3.zero) return;
            Finish f = ArmourFinish(level, true);
            bool plate = level >= 3;
            float margin = plate ? 0.022f : 0.014f;
            const int Segs = 26;

            // rows: bottom lip (inside), bottom edge (crease), belt, body ..., shoulders; then the collar onto the neck
            float[] body = { -0.06f, -0.06f, -0.03f, 0.04f, 0.10f, 0.22f, 0.36f, 0.50f, 0.63f, 0.74f, 0.83f };
            HashSet<int> torsoSet = BoneSet(r, "Hips", "Torso", "Shoulder.L", "Shoulder.R", "Neck");
            float FrontRidge(float ang) => plate ? 0.012f * Mathf.Exp(-Sq(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, FrontAngle(r, a, b)) / 22f)) : 0f;
            Vector3[,] ring = FitRings(r, torsoSet, a, b, body, Segs, 0.035f, (i, ang) => i == 0 ? margin - 0.012f : margin + (i >= 4 ? FrontRidge(ang) : 0f));
            Vector3[,] collar = FitRings(r, BoneSet(r, "Neck"), a, b, new[] { 0.93f, 0.93f }, Segs, 0.03f, (i, ang) => i == 0 ? margin + 0.004f : margin - 0.01f);

            int rows = body.Length + 2;
            var p = new Vector3[rows, Segs];
            for (int k = 0; k < Segs; k++)
            {
                for (int i = 0; i < body.Length; i++) p[i, k] = ring[i, k];
                p[body.Length, k] = collar[0, k];
                p[body.Length + 1, k] = collar[1, k];
            }
            // The bottom lip sits a little higher than the edge: a visible thickness
            for (int k = 0; k < Segs; k++) p[0, k] += Vector3.up * 0.012f;

            Color RowColour(int i, float ang)
            {
                if (i <= 1 || i >= rows - 2) return level >= 4 ? f.Trim : (plate ? f.Trim : f.Main * 0.85f);
                if (i == 2 || i == 3) return f.Dark;                                         // belt
                if (level == 1) return (i % 2 == 0 ? f.Main : f.Main * 0.88f);               // quilted leather bands
                if (plate && i == 4) return f.Main * 0.82f;                                  // lame edge
                return f.Main;
            }
            int start = kit.K.V.Count;
            kit.K.Grid(p, true, f.MainS, (i, j) => RowColour(i, j * 360f / Segs));
            // belt / trim rows carry their own surface
            for (int v = start; v < kit.K.V.Count; v++)
            {
                int row = (v - start) / Segs;
                if (row == 2 || row == 3) kit.K.S[v] = f.DarkS;
                else if (row <= 1 || row >= rows - 2) kit.K.S[v] = plate || level >= 4 ? f.TrimS : f.MainS;
            }
            float tA = Vector3.Dot(a, Vector3.up), tB = Vector3.Dot(b, Vector3.up);
            kit.Weigh(q => Blend(hips, torso, Mathf.InverseLerp(tA + (tB - tA) * 0.12f, tA + (tB - tA) * 0.5f, q.y)));

            // Belt buckle at the front
            Vector3 front = ring[3, FrontIndex(r, a, b, Segs)];
            Vector3 n = Vector3.ProjectOnPlane(front - AxisPoint(ring, 3), Vector3.up).normalized;
            kit.K.Box(Matrix4x4.TRS(front + n * 0.006f + Vector3.up * 0.018f, Quaternion.LookRotation(n), Vector3.one), new Vector3(0.05f, 0.04f, 0.01f), 0.004f, level >= 5 ? GoldS : MetalS(0.85f, 0.5f), _ => level >= 5 ? Gold : new Color(0.62f, 0.55f, 0.40f));
            kit.Weigh(hips);

            if (plate)
            {
                // Rivets along the lame and pauldrons on the shoulders
                for (int k = 0; k < Segs; k += 2)
                {
                    Vector3 q = ring[4, k] + Vector3.up * 0.01f;
                    kit.K.Dome(q, Vector3.ProjectOnPlane(q - AxisPoint(ring, 4), Vector3.up).normalized, 0.006f, f.Trim, f.TrimS);
                }
                kit.Weigh(q => Blend(hips, torso, Mathf.InverseLerp(tA + (tB - tA) * 0.12f, tA + (tB - tA) * 0.5f, q.y)));
                Pauldron(r, kit, f, "L");
                Pauldron(r, kit, f, "R");
            }
        }

        private static float FrontAngle(Rig r, Vector3 a, Vector3 b)
        {
            Frame((b - a).normalized, out Vector3 u, out Vector3 w);
            Vector3 fwd = Vector3.ProjectOnPlane(Vector3.forward, (b - a).normalized);
            return Mathf.Atan2(Vector3.Dot(fwd, w), Vector3.Dot(fwd, u)) * Mathf.Rad2Deg;
        }

        private static int FrontIndex(Rig r, Vector3 a, Vector3 b, int segs) =>
            Mathf.RoundToInt(Mathf.Repeat(FrontAngle(r, a, b) / 360f, 1f) * segs) % segs;

        private static void Pauldron(Rig r, Kit kit, Finish f, string side)
        {
            int arm = r.Bone("UpperArm." + side);
            if (arm < 0) return;
            Vector3 joint = r.Pos("UpperArm." + side), elbow = r.Pos("LowerArm." + side);
            Vector3 along = (elbow - joint).normalized;
            Vector3 outward = Vector3.ProjectOnPlane(joint - r.Pos("Neck"), Vector3.up).normalized;
            Vector3 axis = (outward + Vector3.up * 0.9f).normalized;
            Vector3[,] armRing = FitRings(r, BoneSet(r, "UpperArm." + side, "Shoulder." + side), joint, elbow, new[] { 0.12f }, 16, 0.04f, (i, ang) => 0f);
            float armR = 0f;
            for (int k = 0; k < 16; k++) armR = Mathf.Max(armR, (armRing[0, k] - AxisPoint(armRing, 0)).magnitude);
            float R = Mathf.Clamp(armR * 1.55f, 0.07f, 0.13f);
            Vector3 centre = joint + along * (R * 0.15f) - axis * (R * 0.25f);
            var prof = new Vector2[8];
            for (int k = 0; k < 8; k++)
            {
                float t = k / 7f * Mathf.PI * 0.5f;
                prof[k] = new Vector2(R * Mathf.Cos(t), R * 0.8f * Mathf.Sin(t));
            }
            prof[7].x = 0f;
            Matrix4x4 m = Matrix4x4.TRS(centre, Quaternion.FromToRotation(Vector3.up, axis), Vector3.one);
            kit.K.Lathe(m, prof, 18, f.MainS, (i, ang) => i <= 1 ? f.Trim : (i == 3 ? f.Main * 0.85f : f.Main));
            kit.Weigh(arm);
        }

        // ------------------------------------------------------------------ helmet

        private static void BuildHelmet(Rig r, Kit kit, int level)
        {
            int head = r.Bone("Head");
            if (head < 0) return;
            HashSet<int> headSet = BoneSet(r, "Head");
            Finish f = ArmourFinish(level, false);
            float top = float.MinValue;
            Vector3 c = Vector3.zero;
            int n = 0;
            for (int v = 0; v < r.BodyCount; v++)
            {
                if (!headSet.Contains(r.Dominant[v])) continue;
                top = Mathf.Max(top, r.Body[v].y);
                c += r.Body[v]; n++;
            }
            if (n == 0) return;
            c /= n;
            float eye = r.HasEyes ? r.EyeMid.y : c.y;
            float rim = eye + (level == 1 ? 0.05f : 0.032f);
            float margin = level == 1 ? 0.012f : 0.018f;
            const int Segs = 24;
            Vector3 a = new Vector3(c.x, rim, c.z), b = new Vector3(c.x, top, c.z);
            float[] ts = { 0f, 0f, 0f, 0.12f, 0.35f, 0.6f, 0.8f };
            Vector3[,] ring = FitRings(r, headSet, a, b, ts, Segs, 0.018f, (i, ang) => i == 0 ? margin - 0.009f : margin);

            // dome closing over the crown
            const int Cap = 4;
            int rows = ts.Length + Cap;
            var p = new Vector3[rows, Segs];
            Vector3 crown = new Vector3(c.x, top + margin, c.z);
            int last = ts.Length - 1;
            for (int k = 0; k < Segs; k++)
            {
                for (int i = 0; i < ts.Length; i++) p[i, k] = ring[i, k];
                for (int j = 1; j <= Cap; j++)
                {
                    float phi = j / (float)Cap * Mathf.PI * 0.5f;
                    Vector3 rad = ring[last, k] - new Vector3(c.x, ring[last, k].y, c.z);
                    p[last + j, k] = new Vector3(c.x, Mathf.Lerp(ring[last, k].y, crown.y, Mathf.Sin(phi)), c.z) + rad * Mathf.Cos(phi);
                }
                // the rim drops at the back to cover the nape; the lip sits just inside the edge
                Vector3 dir = Vector3.ProjectOnPlane(ring[1, k] - a, Vector3.up).normalized;
                float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.9f, -Vector3.Dot(dir, Vector3.forward)));
                float drop = (level == 1 ? 0.025f : 0.05f) * back;
                p[0, k] += Vector3.down * drop + Vector3.up * 0.008f;
                p[1, k] += Vector3.down * drop;
                p[2, k] += Vector3.down * drop;
            }
            bool banded = level >= 2;
            kit.K.Grid(p, true, f.MainS, (i, j) => i <= 2 || (banded && i == 3) ? f.Trim : (level == 1 && i % 2 == 0 ? f.Main * 0.9f : f.Main));
            int start = kit.K.V.Count - rows * Segs;
            for (int v = start; v < kit.K.V.Count; v++) if ((v - start) / Segs <= (banded ? 3 : 2)) kit.K.S[v] = f.TrimS;
            kit.Weigh(head);

            Vector3 fwdDir = Vector3.forward;
            int frontK = 0;
            float best = -2f;
            for (int k = 0; k < Segs; k++)
            {
                float d = Vector3.Dot(Vector3.ProjectOnPlane(ring[1, k] - a, Vector3.up).normalized, fwdDir);
                if (d > best) { best = d; frontK = k; }
            }
            Vector3 browFront = p[1, frontK];

            if (level >= 2)
            {
                // nasal guard down over the nose
                kit.K.Box(Matrix4x4.TRS(browFront + Vector3.down * 0.04f + fwdDir * 0.004f, Quaternion.identity, Vector3.one), new Vector3(0.022f, 0.085f, 0.009f), 0.003f, f.TrimS, _ => f.Trim);
                kit.Weigh(head);
                // rivets around the band
                for (int k = 0; k < Segs; k += 3)
                {
                    Vector3 q = p[2, k] + Vector3.up * 0.012f;
                    kit.K.Dome(q, Vector3.ProjectOnPlane(q - a, Vector3.up).normalized, 0.006f, level >= 5 ? Gold : f.Trim * 0.8f, level >= 5 ? GoldS : f.TrimS);
                }
                kit.Weigh(head);
            }
            if (level >= 3)
            {
                // ridge over the crown, front to back
                Vector3 backTop = p[ts.Length + 1, (frontK + Segs / 2) % Segs], frontTop = p[ts.Length + 1, frontK];
                Vector3 mid = (backTop + frontTop) * 0.5f + Vector3.up * 0.012f;
                kit.K.Box(Matrix4x4.TRS(mid, Quaternion.LookRotation(Vector3.ProjectOnPlane(frontTop - backTop, Vector3.up).normalized), Vector3.one),
                          new Vector3(0.012f, 0.02f, (frontTop - backTop).magnitude * 0.9f), 0.004f, level >= 5 ? GoldS : f.TrimS, _ => level >= 5 ? Gold : f.Trim);
                kit.Weigh(head);
            }
        }

        // ------------------------------------------------------------------ gloves

        private static void BuildGloves(Rig r, Kit kit, int level, out HashSet<int> recolour)
        {
            recolour = new HashSet<int>();
            Finish f = ArmourFinish(level, true);
            foreach (string side in new[] { "L", "R" })
            {
                int fore = r.Bone("LowerArm." + side), palm = r.Bone("Palm." + side), mid = r.Bone("MiddleHand." + side);
                if (fore < 0 || palm < 0) continue;
                HashSet<int> hand = BoneSet(r, "Palm." + side, "MiddleHand." + side, "Fingers." + side, "Thumb1." + side, "Thumb2." + side);
                for (int v = 0; v < r.BodyCount; v++) if (hand.Contains(r.Dominant[v])) recolour.Add(v);

                // Cuff over the wrist, flared towards the elbow
                Vector3 elbow = r.Pos("LowerArm." + side), wrist = r.Pos("Palm." + side);
                float[] ts = { 0.52f, 0.52f, 0.58f, 0.72f, 0.86f, 0.97f };
                float flare = level >= 3 ? 0.02f : 0.012f;
                Vector3[,] cuff = FitRings(r, BoneSet(r, "LowerArm." + side), elbow, wrist, ts, 16, 0.025f,
                    (i, ang) => i == 0 ? 0.004f : 0.011f + (i <= 2 ? flare * (i == 1 ? 1f : 0.6f) : 0f));
                kit.K.Grid(cuff, true, f.MainS, (i, j) => i <= 1 ? f.Trim : f.Main);
                kit.Weigh(fore);

                if (level >= 3 && mid >= 0)
                {
                    // plate over the back of the hand
                    Vector3 knuckles = r.Pos("Fingers." + side);
                    if (knuckles == Vector3.zero) continue;
                    Vector3[,] plate = FitRings(r, BoneSet(r, "Palm." + side, "MiddleHand." + side), wrist, knuckles, new[] { 0.08f, 0.4f, 0.75f, 0.95f }, 14, 0.02f, (i, ang) => 0.008f);
                    kit.K.Grid(plate, true, f.MainS, (i, j) => i == 3 ? f.Trim : f.Main);
                    Vector3 mp = r.Pos("MiddleHand." + side);
                    kit.Weigh(q => Vector3.Dot(q - wrist, knuckles - wrist) < Vector3.Dot(mp - wrist, knuckles - wrist) ? One(palm) : One(mid));
                }
            }
        }

        // ------------------------------------------------------------------ boots

        private static void BuildBoots(Rig r, Kit kit, int level, out HashSet<int> recolour)
        {
            recolour = new HashSet<int>();
            Finish f = ArmourFinish(level, false);
            foreach (string side in new[] { "L", "R" })
            {
                int shin = r.Bone("LowerLeg." + side), foot = r.Bone("Foot." + side);
                if (shin < 0 || foot < 0) continue;
                Vector3 knee = r.Pos("LowerLeg." + side), ankle = r.Pos("Foot." + side);
                Vector3 axis = ankle - knee;
                const float BootTop = 0.55f;
                for (int v = 0; v < r.BodyCount; v++)
                {
                    int d = r.Dominant[v];
                    if (d == foot) recolour.Add(v);
                    else if (d == shin && Vector3.Dot(r.Body[v] - knee, axis) / axis.sqrMagnitude > BootTop) recolour.Add(v);
                }
                HashSet<int> shinSet = BoneSet(r, "LowerLeg." + side);

                if (level == 1)
                {
                    // turned-down leather top of the boot
                    Vector3[,] top = FitRings(r, shinSet, knee, ankle, new[] { BootTop - 0.02f, BootTop + 0.01f, BootTop + 0.06f, BootTop + 0.09f }, 16, 0.03f,
                        (i, ang) => i == 0 || i == 3 ? 0.003f : 0.012f);
                    kit.K.Grid(top, true, LeatherS, (i, j) => i == 1 ? Leather : Leather * 0.8f);
                    kit.Weigh(shin);
                    continue;
                }

                // Greave from below the knee to the ankle
                float[] ts = { 0.14f, 0.14f, 0.2f, 0.4f, 0.6f, 0.8f, 0.92f };
                Vector3[,] greave = FitRings(r, shinSet, knee, ankle, ts, 18, 0.03f, (i, ang) => i == 0 ? 0.004f : 0.015f);
                kit.K.Grid(greave, true, f.MainS, (i, j) => i <= 1 || i == ts.Length - 1 ? f.Trim : f.Main);
                kit.Weigh(shin);

                if (level >= 3)
                {
                    // knee cop
                    Vector3[,] kr = FitRings(r, BoneSet(r, "LowerLeg." + side, "UpperLeg." + side), knee, ankle, new[] { 0.02f }, 16, 0.04f, (i, ang) => 0f);
                    Vector3 kc = AxisPoint(kr, 0);
                    float kneeR = 0f;
                    int frontK = 0;
                    float best = -2f;
                    for (int k = 0; k < 16; k++)
                    {
                        Vector3 d = kr[0, k] - kc;
                        kneeR = Mathf.Max(kneeR, d.magnitude);
                        float dot = Vector3.Dot(d.normalized, Vector3.forward);
                        if (dot > best) { best = dot; frontK = k; }
                    }
                    Vector3 cop = kr[0, frontK] + Vector3.forward * 0.004f;
                    kit.K.Dome(cop, Vector3.forward, Mathf.Clamp(kneeR * 0.6f, 0.028f, 0.048f), f.Main, f.MainS);
                    kit.Weigh(shin);
                }
            }
        }

        // ------------------------------------------------------------------ shield

        private static void BuildShield(Rig r, Kit kit, int level)
        {
            int fore = r.Bone("LowerArm.L");
            if (fore < 0) return;
            Vector3 elbow = r.Pos("LowerArm.L"), wrist = r.Pos("Palm.L");
            Vector3 along = (wrist - elbow).normalized;
            // Face out sideways, away from the body, across the forearm (stays outward when the elbow bends)
            Vector3 lateral = Vector3.right * Mathf.Sign(elbow.x - r.Pos("Neck").x);
            Vector3 n = Vector3.ProjectOnPlane(lateral, along).normalized;
            // like the role shields: the forearm bone's X axis is the outward face, signed away from the body
            Vector3 bx = Vector3.ProjectOnPlane(r.BoneX[fore], along).normalized;
            if (bx.sqrMagnitude > 0.5f) n = Vector3.Dot(bx, lateral) >= 0f ? bx : -bx;
            Vector3[,] arm = FitRings(r, BoneSet(r, "LowerArm.L"), elbow, wrist, new[] { 0.45f }, 12, 0.03f, (i, ang) => 0f);
            float armR = 0f;
            Vector3 ac = AxisPoint(arm, 0);
            for (int k = 0; k < 12; k++) armR = Mathf.Max(armR, (arm[0, k] - ac).magnitude);
            Vector3 centre = Vector3.Lerp(elbow, wrist, 0.45f) + n * (armR + 0.035f);

            Finish f = ArmourFinish(level, false);
            Finish w = WeaponFinish(level);
            const float R = 0.27f;
            bool wooden = level <= 2;
            Color face = wooden ? WoodColour(level) : f.Main;
            Vector3 faceS = wooden ? WoodS : f.MainS;
            Color rimC = level == 1 ? WoodColour(1) * 0.7f : (level >= 5 ? Gold : (level == 4 ? f.Trim : w.Trim));
            Vector3 rimS = level == 1 ? WoodS : (level >= 5 ? GoldS : MetalS(0.85f, 0.5f));
            Color boss = level >= 5 ? Gold : w.Main;
            Vector3 bossS = level >= 5 ? GoldS : w.MainS;

            Quaternion rot = Quaternion.LookRotation(along, n); // local +Y = n (the face), the disc spans X-Z
            Matrix4x4 m = Matrix4x4.TRS(centre, rot, Vector3.one);
            Matrix4x4 inv = m.inverse;
            // Face: shallow dome from the boss to the rim (rows 0..), then rim and back
            var prof = new[]
            {
                new Vector2(0.075f, 0.022f), new Vector2(0.08f, 0.021f), new Vector2(0.14f, 0.018f), new Vector2(0.2f, 0.014f),
                new Vector2(R - 0.025f, 0.010f), new Vector2(R - 0.025f, 0.010f), new Vector2(R - 0.012f, 0.016f), new Vector2(R, 0.010f),
                new Vector2(R, -0.006f), new Vector2(R - 0.02f, -0.008f), new Vector2(R - 0.02f, -0.008f), new Vector2(0.001f, -0.008f)
            };
            int start = kit.K.V.Count;
            kit.K.Lathe(m, prof, 28, faceS, (i, ang) =>
            {
                if (i >= 5 && i <= 9) return rimC;
                if (i >= 10) return wooden ? WoodColour(level) * 0.75f : f.Main * 0.6f;
                return face;
            });
            for (int v = start; v < kit.K.V.Count; v++)
            {
                int row = (v - start) / 28;
                Vector3 lp = inv.MultiplyPoint3x4(kit.K.V[v]);
                if (row >= 5 && row <= 9) kit.K.S[v] = rimS;
                else if (wooden && row < 5)
                {
                    // planks: darker seams every 7 cm across the face
                    float seam = Mathf.Abs(Mathf.Repeat(lp.x + 0.035f, 0.07f) - 0.035f);
                    if (seam < 0.004f) kit.K.C[v] = kit.K.C[v] * 0.55f;
                }
                else if (level == 4 && row < 5) kit.K.C[v] = kit.K.C[v];
            }
            // Boss
            var bossProf = new Vector2[6];
            for (int k = 0; k < 6; k++)
            {
                float t = k / 5f * Mathf.PI * 0.5f;
                bossProf[k] = new Vector2(0.078f * Mathf.Cos(t), 0.018f + 0.045f * Mathf.Sin(t));
            }
            bossProf[5].x = 0f;
            kit.K.Lathe(m, bossProf, 18, bossS, (i, ang) => boss);
            if (level >= 2)
                for (int k = 0; k < 12; k++)
                {
                    float ang = k * Mathf.PI * 2f / 12f;
                    Vector3 q = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(ang) * (R - 0.012f), 0.017f, Mathf.Sin(ang) * (R - 0.012f)));
                    kit.K.Dome(q, n, 0.007f, level >= 5 ? Gold : w.Trim, level >= 5 ? GoldS : w.TrimS);
                }
            if (level >= 3)
            {
                // cross bands
                for (int k = 0; k < 2; k++)
                {
                    Quaternion br = rot * Quaternion.Euler(0f, 45f + k * 90f, 0f);
                    kit.K.Box(Matrix4x4.TRS(centre + n * 0.016f, br, Vector3.one), new Vector3(0.03f, 0.006f, 2f * (R - 0.03f)), 0.002f, level >= 5 ? GoldS : f.TrimS, _ => level >= 5 ? Gold : f.Trim);
                }
            }
            kit.Weigh(fore);
        }

        // ------------------------------------------------------------------ weapons

        private static void BuildWeapon(Rig r, Kit kit, ItemType type, int level)
        {
            int hand = r.Bone("Palm.R");
            if (hand < 0) return;
            // As the role weapons: out of the fist along palm +X (villager rig) or -X (knight rig); built along +Y
            Vector3 blade = -r.BoneX[hand]; // (both skeletons: the bind matrices are mirrored relative to the attachment frames)
            Matrix4x4 m = Matrix4x4.TRS(r.BonePos[hand], Quaternion.LookRotation(Vector3.ProjectOnPlane(r.BoneZ[hand], blade).normalized, blade), Vector3.one);
            Finish f = WeaponFinish(level);
            switch (type)
            {
                case ItemType.Bow: Bow(kit, m, level, f); break;
                case ItemType.Axe: Axe(kit, m, level, f); break;
                case ItemType.Pickaxe: Pickaxe(kit, m, level, f); break;
                default: Sword(kit, m, level, f); break;
            }
            kit.Weigh(hand);
        }

        private static void Grip(Kit kit, Matrix4x4 m, float from, float to, float radius, Color c, Vector3 s)
        {
            var prof = new[] { new Vector2(radius, from), new Vector2(radius, to) };
            kit.K.Lathe(m, prof, 8, s, (i, a) => c);
        }

        private static void Sword(Kit kit, Matrix4x4 m, int level, Finish f)
        {
            float size = level >= 4 ? 1.08f : 1f;
            // Blade: diamond section with crisp edges, tapering to the point (along +Y)
            float[] ys = { 0.12f, 0.2f, 0.4f, 0.6f, 0.72f, 0.8f, 0.86f };
            var blade = new Vector3[ys.Length, 6];
            for (int i = 0; i < ys.Length; i++)
            {
                float y = ys[i] * size;
                float tip = Mathf.InverseLerp(0.7f, 0.86f, ys[i]);
                float wdt = Mathf.Lerp(0.03f, 0.024f, ys[i] / 0.7f) * (1f - tip * tip * 0.97f);
                float th = 0.006f * (1f - tip * 0.8f);
                Vector3[] sec = { new Vector3(wdt, y, 0f), new Vector3(wdt, y, 0f), new Vector3(0f, y, th), new Vector3(-wdt, y, 0f), new Vector3(-wdt, y, 0f), new Vector3(0f, y, -th) };
                for (int j = 0; j < 6; j++) blade[i, j] = m.MultiplyPoint3x4(sec[j]);
            }
            kit.K.Grid(blade, true, f.MainS, (i, j) => j == 2 || j == 5 ? f.Main * 0.82f : f.Main);
            // Crossguard, grip, pommel
            Color fit = level >= 5 ? Gold : f.Trim;
            Vector3 fitS = level >= 5 ? GoldS : f.TrimS;
            kit.K.Box(m * Matrix4x4.Translate(new Vector3(0f, 0.105f * size, 0f)), new Vector3(0.2f * size, 0.022f, 0.03f), 0.006f, fitS, _ => fit);
            Grip(kit, m, -0.09f, 0.095f, 0.016f, level == 1 ? DarkLeather : new Color(0.24f, 0.13f, 0.07f), LeatherS);
            var pom = new Vector2[5];
            for (int k = 0; k < 5; k++)
            {
                float t = -Mathf.PI * 0.5f + k / 4f * Mathf.PI;
                pom[k] = new Vector2(0.028f * Mathf.Cos(t), -0.11f + 0.024f * Mathf.Sin(t));
            }
            pom[0].x = 0f; pom[4].x = 0f;
            kit.K.Lathe(m, pom, 10, fitS, (i, a) => fit);
        }

        private static void Axe(Kit kit, Matrix4x4 m, int level, Finish f)
        {
            Grip(kit, m, -0.12f, 0.62f, 0.02f, WoodColour(level), WoodS);
            // Bearded head: a wedge outward along +X from the haft near the top, cutting edge in the X-Y plane
            var pts = new List<Vector3>
            {
                new Vector3(0.02f, 0.62f, 0f), new Vector3(0.1f, 0.6f, 0f), new Vector3(0.17f, 0.66f, 0f),
                new Vector3(0.18f, 0.52f, 0f), new Vector3(0.1f, 0.5f, 0f), new Vector3(0.02f, 0.53f, 0f)
            };
            var head = new Vector3[2, pts.Count];
            for (int j = 0; j < pts.Count; j++)
            {
                float th = Mathf.Lerp(0.012f, 0.002f, Mathf.InverseLerp(0.05f, 0.18f, pts[j].x));
                head[0, j] = m.MultiplyPoint3x4(pts[j] + new Vector3(0f, 0f, th));
                head[1, j] = m.MultiplyPoint3x4(pts[j] - new Vector3(0f, 0f, th));
            }
            kit.K.Grid(head, true, f.MainS, (i, j) => j == 2 || j == 3 ? f.Main * 1.1f : f.Main);
            kit.K.Polygon(new[] { head[0, 0], head[0, 1], head[0, 2], head[0, 3], head[0, 4], head[0, 5] }, m.MultiplyPoint3x4(new Vector3(0.09f, 0.57f, -1f)), f.MainS, _ => f.Main);
            kit.K.Polygon(new[] { head[1, 0], head[1, 1], head[1, 2], head[1, 3], head[1, 4], head[1, 5] }, m.MultiplyPoint3x4(new Vector3(0.09f, 0.57f, 1f)), f.MainS, _ => f.Main);
            kit.K.Box(m * Matrix4x4.Translate(new Vector3(0f, 0.575f, 0f)), new Vector3(0.05f, 0.1f, 0.035f), 0.006f, level >= 5 ? GoldS : f.TrimS, _ => level >= 5 ? Gold : f.Trim);
        }

        private static void Pickaxe(Kit kit, Matrix4x4 m, int level, Finish f)
        {
            Grip(kit, m, -0.12f, 0.66f, 0.02f, WoodColour(level), WoodS);
            // Curved double pick across the haft top
            const int N = 9;
            var head = new Vector3[N, 4];
            for (int i = 0; i < N; i++)
            {
                float s = -1f + 2f * i / (N - 1);
                float x = s * 0.2f, y = 0.62f - s * s * 0.05f;
                float half = Mathf.Lerp(0.016f, 0.003f, Mathf.Abs(s));
                head[i, 0] = m.MultiplyPoint3x4(new Vector3(x, y + half, half));
                head[i, 1] = m.MultiplyPoint3x4(new Vector3(x, y + half, -half));
                head[i, 2] = m.MultiplyPoint3x4(new Vector3(x, y - half, -half));
                head[i, 3] = m.MultiplyPoint3x4(new Vector3(x, y - half, half));
            }
            kit.K.Grid(head, true, f.MainS, (i, j) => f.Main);
            kit.K.Box(m * Matrix4x4.Translate(new Vector3(0f, 0.62f, 0f)), new Vector3(0.045f, 0.06f, 0.045f), 0.006f, level >= 5 ? GoldS : f.TrimS, _ => level >= 5 ? Gold : f.Trim);
        }

        private static void Bow(Kit kit, Matrix4x4 m, int level, Finish f)
        {
            // Held upright through the fist (grip at the origin): limbs along ±X of the hand frame, curved forward (+Y)
            Color wood = WoodColour(level);
            const int N = 15;
            var limb = new Vector3[N, 6];
            for (int i = 0; i < N; i++)
            {
                float s = -1f + 2f * i / (N - 1);
                float z = s * 0.62f;
                float y = (1f - s * s) * 0.09f - (level >= 3 ? 0.05f * Mathf.Pow(Mathf.Abs(s), 6f) : 0f); // recurve tips at III+
                float half = Mathf.Lerp(0.017f, 0.006f, Mathf.Abs(s));
                for (int j = 0; j < 6; j++)
                {
                    float a = j * Mathf.PI * 2f / 6f;
                    limb[i, j] = m.MultiplyPoint3x4(new Vector3(z, y + Mathf.Sin(a) * half * 0.7f, Mathf.Cos(a) * half));
                }
            }
            kit.K.Grid(limb, true, WoodS, (i, j) => Mathf.Abs(i - (N - 1) / 2) <= 1 ? DarkLeather : (level >= 5 && (i == 2 || i == N - 3) ? Gold : wood));
            // String
            Vector3 top = m.MultiplyPoint3x4(new Vector3(0.62f, -0.04f * (level >= 3 ? 1f : 0f), 0f)), bottom = m.MultiplyPoint3x4(new Vector3(-0.62f, -0.04f * (level >= 3 ? 1f : 0f), 0f));
            Vector3 dir = (top - bottom).normalized;
            Frame(dir, out Vector3 u, out Vector3 w2);
            var str = new Vector3[2, 4];
            for (int j = 0; j < 4; j++)
            {
                Vector3 off = (u * Mathf.Cos(j * Mathf.PI * 0.5f) + w2 * Mathf.Sin(j * Mathf.PI * 0.5f)) * 0.0025f;
                str[0, j] = bottom + off; str[1, j] = top + off;
            }
            kit.K.Grid(str, true, new Vector3(1f, 0f, 0.3f), (i, j) => new Color(0.86f, 0.82f, 0.70f));
            if (level >= 3)
                foreach (float s in new[] { -1f, 1f })
                    kit.K.Box(m * Matrix4x4.Translate(new Vector3(s * 0.6f, -0.03f, 0f)), new Vector3(0.05f, 0.03f, 0.02f), 0.004f, level >= 5 ? GoldS : f.MainS, _ => level >= 5 ? Gold : f.Main);
        }

        private static float Sq(float x) => x * x;
    }
}
