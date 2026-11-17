using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Bone animation baked into a texture, for drawing distant characters on the GPU (Custom/CharacterSurface with
    /// _GPU_SKIN, driven by GpuSkinnedCharacter). Each clip of the character's Animator is sampled at 30 frames per
    /// second on the character's own skeleton; a row holds every bone's skin matrix (bone * bindpose, in the body
    /// renderer's space) as three texels, then the matrix of the bone the upper body hangs from and its inverse (to
    /// lay an attack over walking, like the Animator's masked layer). The last row is the upper-body mask.
    /// One bake per skeleton layout (controller, bone paths, bindposes), made the first time it is needed.
    /// </summary>
    public sealed class GpuSkinBake
    {
        public const float FramesPerSecond = 30f;
        private const string UpperBodyRoot = "Torso";

        public struct Clip
        {
            public int FirstRow, Frames;
            public bool Loop;
            public float Length => Frames / FramesPerSecond;
            public bool Valid => Frames > 0;

            /// <summary>The shader's clip vector: first row, frames (negative: holds the last one), fps, start time.</summary>
            public Vector4 Play(float startTime) => new Vector4(FirstRow, Loop ? Frames : -Frames, FramesPerSecond, startTime);
        }

        public Texture2D Texture { get; private set; }
        public Clip Idle, Walk, Run, Attack, Death, Shoot;
        public Vector4 Info;   // anchor column, mask row
        /// <summary>The skin matrices of the first baked row (idle, frame 0): a check of the size a body is drawn at.</summary>
        public Matrix4x4[] FirstPose;

        private static readonly Dictionary<string, GpuSkinBake> Bakes = new Dictionary<string, GpuSkinBake>();
        private static readonly Dictionary<Mesh, Mesh> SkinMeshes = new Dictionary<Mesh, Mesh>();
        private static readonly Dictionary<(Material, GpuSkinBake), Material> Materials = new Dictionary<(Material, GpuSkinBake), Material>();

        /// <summary>The bake for this body (its bones by path from the character root, mesh bindposes and the Animator's clips); null if it cannot be baked.</summary>
        public static GpuSkinBake For(Animator animator, Transform root, SkinnedMeshRenderer body, Mesh mesh)
        {
            if (animator == null || animator.runtimeAnimatorController == null || body == null || mesh == null) return null;
            Transform[] bones = body.bones;
            Matrix4x4[] bindposes = mesh.bindposes;
            if (bones.Length == 0 || bones.Length != bindposes.Length) return null;

            var key = new StringBuilder(animator.runtimeAnimatorController.name).Append('|').Append(bindposes.Length);
            foreach (Transform bone in bones)
            {
                string path = PathFrom(root, bone);
                if (path == null) return null; // a bone outside the character
                key.Append('|').Append(path);
            }
            // Every bindpose (every 7th was taken, and two bodies on one skeleton fitted apart, the sculpted peasants' styles,
            // could share a bake made for the other: the bones it skipped drawn by the wrong matrices)
            unchecked
            {
                int h = 17;
                foreach (Matrix4x4 b in bindposes)
                    for (int e = 0; e < 16; e++) h = h * 31 + Mathf.RoundToInt(b[e] * 10000f);
                key.Append('|').Append(h);
            }
            string k = key.ToString();
            if (Bakes.TryGetValue(k, out GpuSkinBake bake)) return bake;
            bake = Bake(animator, body.transform, bones, bindposes);
            Bakes[k] = bake;
            return bake;
        }

        private static string PathFrom(Transform root, Transform t)
        {
            if (t == null) return null;
            var parts = new List<string>();
            for (Transform c = t; c != root; c = c.parent)
            {
                if (c == null) return null;
                parts.Add(c.name);
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static GpuSkinBake Bake(Animator animator, Transform space, Transform[] bones, Matrix4x4[] bindposes)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (AnimationClip c in animator.runtimeAnimatorController.animationClips) clips[c.name] = c;
            if (!clips.ContainsKey("Idle")) return null;

            // Upper body: the Torso and everything under it; its parent is the anchor
            int anchor = -1;
            var mask = new float[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                for (Transform c = bones[i]; c != null && c != animator.transform; c = c.parent)
                    if (c.name == UpperBodyRoot) { mask[i] = 1f; break; }
                if (bones[i].name == UpperBodyRoot && bones[i].parent != null)
                    anchor = System.Array.IndexOf(bones, bones[i].parent);
            }
            if (anchor < 0) for (int i = 0; i < mask.Length; i++) mask[i] = 1f; // no upper body: attacks take it all

            // Rows per clip
            var bake = new GpuSkinBake();
            var order = new[] { "Idle", "Walk", "Run", "Attack", "Death", "Shoot" };
            var plan = new List<(AnimationClip clip, int frames)>();
            int rows = 0;
            foreach (string name in order)
            {
                Clip info = default;
                if (clips.TryGetValue(name, out AnimationClip clip))
                {
                    bool loop = clip.isLooping;
                    int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * FramesPerSecond)) + (loop ? 0 : 1);
                    info = new Clip { FirstRow = rows, Frames = frames, Loop = loop };
                    plan.Add((clip, frames));
                    rows += frames;
                }
                switch (name)
                {
                    case "Idle": bake.Idle = info; break;
                    case "Walk": bake.Walk = info; break;
                    case "Run": bake.Run = info; break;
                    case "Attack": bake.Attack = info; break;
                    case "Death": bake.Death = info; break;
                    case "Shoot": bake.Shoot = info; break;
                }
            }
            if (!bake.Walk.Valid) bake.Walk = bake.Idle;
            if (!bake.Run.Valid) bake.Run = bake.Walk;

            int anchorColumn = bones.Length * 3, width = anchorColumn + 6, maskRow = rows;
            var data = new ushort[width * (rows + 1) * 4];

            // Sample on the character itself, then put its pose back (the Animator takes over next frame anyway)
            Transform[] all = animator.GetComponentsInChildren<Transform>(true);
            var saved = new (Vector3, Quaternion, Vector3)[all.Length];
            for (int i = 0; i < all.Length; i++) saved[i] = (all[i].localPosition, all[i].localRotation, all[i].localScale);
            int row = 0;
            foreach (var (clip, frames) in plan)
            {
                for (int f = 0; f < frames; f++, row++)
                {
                    clip.SampleAnimation(animator.gameObject, Mathf.Min(f / FramesPerSecond, clip.length));
                    Matrix4x4 toSpace = space.worldToLocalMatrix;
                    if (row == 0) bake.FirstPose = new Matrix4x4[bones.Length];
                    for (int b = 0; b < bones.Length; b++)
                    {
                        Matrix4x4 skin = toSpace * bones[b].localToWorldMatrix * bindposes[b];
                        if (row == 0) bake.FirstPose[b] = skin;
                        Write(data, width, b * 3, row, skin);
                    }
                    Matrix4x4 anchorM = anchor >= 0 ? toSpace * bones[anchor].localToWorldMatrix : Matrix4x4.identity;
                    Write(data, width, anchorColumn, row, anchorM);
                    Write(data, width, anchorColumn + 3, row, anchorM.inverse);
                }
            }
            for (int i = 0; i < all.Length; i++) { all[i].localPosition = saved[i].Item1; all[i].localRotation = saved[i].Item2; all[i].localScale = saved[i].Item3; }
            for (int b = 0; b < bones.Length; b++) Set(data, width, b * 3, maskRow, new Vector4(mask[b], 0f, 0f, 0f));

            var tex = new Texture2D(width, rows + 1, TextureFormat.RGBAHalf, false, true)
            {
                name = "GpuSkin_" + animator.runtimeAnimatorController.name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixelData(data, 0);
            tex.Apply(false, true);
            bake.Texture = tex;
            bake.Info = new Vector4(anchorColumn, maskRow, 0f, 0f);
            bake.data = data; bake.width = width; bake.height = rows + 1;
            return bake;
        }

        // ------------------------------------------------------------------ the shader's skinning on the CPU (a check: GpuSkinnedCharacter)

        private ushort[] data;
        private int width, height;

        /// <summary>A texel as the shader loads it (zero outside the texture, as a GPU load out of range gives).</summary>
        private Vector4 Texel(int column, int row)
        {
            if (data == null || column < 0 || row < 0 || column >= width || row >= height) return Vector4.zero;
            int i = (row * width + column) * 4;
            return new Vector4(Mathf.HalfToFloat(data[i]), Mathf.HalfToFloat(data[i + 1]), Mathf.HalfToFloat(data[i + 2]), Mathf.HalfToFloat(data[i + 3]));
        }

        private Matrix4x4 Load(int column, int row)
        {
            var m = Matrix4x4.zero;
            m.SetRow(0, Texel(column, row)); m.SetRow(1, Texel(column + 1, row)); m.SetRow(2, Texel(column + 2, row));
            m.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            return m;
        }

        private static Matrix4x4 Lerp(Matrix4x4 a, Matrix4x4 b, float k)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i] = a[i] + (b[i] - a[i]) * k;
            return m;
        }

        /// <summary>A bone's skin matrix between two rows (the shader's BoneAt).</summary>
        internal Matrix4x4 Bone(int column, int r0, int r1, float k) => Lerp(Load(column, r0), Load(column, r1), k);

        /// <summary>The upper-body mask of a bone's column.</summary>
        internal float Mask(int column) => Mathf.Clamp01(Texel(column, (int)Info.y).x);

        /// <summary>The two rows around a clip's time now and the blend between them (the shader's ClipRows).</summary>
        internal static void ClipRows(Vector4 clip, float now, out int r0, out int r1, out float k)
        {
            float frames = Mathf.Abs(clip.y);
            float t = Mathf.Max(0f, (now - clip.w) * clip.z);
            t = clip.y > 0f ? (frames > 0f ? t - Mathf.Floor(t / frames) * frames : 0f) : Mathf.Min(t, frames - 1f);
            float f = Mathf.Floor(t);
            k = t - f;
            float next = f + 1f;
            if (next >= frames) next = clip.y > 0f ? 0f : frames - 1f;
            r0 = (int)(clip.x + f);
            r1 = (int)(clip.x + next);
        }

        /// <summary>The bake a GPU material samples (MaterialFor made it), if any.</summary>
        internal static GpuSkinBake OfMaterial(Material m)
        {
            if (m == null) return null;
            foreach (var pair in Materials) if (pair.Value == m) return pair.Key.Item2;
            return null;
        }

        private static void Write(ushort[] data, int width, int column, int row, Matrix4x4 m)
        {
            Set(data, width, column, row, m.GetRow(0));
            Set(data, width, column + 1, row, m.GetRow(1));
            Set(data, width, column + 2, row, m.GetRow(2));
        }

        private static void Set(ushort[] data, int width, int column, int row, Vector4 v)
        {
            int i = (row * width + column) * 4;
            data[i] = Mathf.FloatToHalf(v.x);
            data[i + 1] = Mathf.FloatToHalf(v.y);
            data[i + 2] = Mathf.FloatToHalf(v.z);
            data[i + 3] = Mathf.FloatToHalf(v.w);
        }

        /// <summary>A copy of a skinned mesh for a plain MeshRenderer: bone weights in UV4, bone indices in UV5.</summary>
        public static Mesh SkinMesh(Mesh source)
        {
            if (SkinMeshes.TryGetValue(source, out Mesh cached) && cached != null) return cached;
            if (!source.isReadable) return null;
            BoneWeight[] weights = source.boneWeights;
            if (weights.Length != source.vertexCount) return null;
            var w = new List<Vector4>(weights.Length);
            var b = new List<Vector4>(weights.Length);
            foreach (BoneWeight bw in weights)
            {
                w.Add(new Vector4(bw.weight0, bw.weight1, bw.weight2, bw.weight3));
                b.Add(new Vector4(bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3));
            }
            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + "_Gpu";
            mesh.SetUVs(4, w);
            mesh.SetUVs(5, b);
            Bounds bounds = mesh.bounds;
            bounds.Expand(1.6f); // attacks and the fall reach out of the rest pose
            mesh.bounds = bounds;
            SkinMeshes[source] = mesh;
            return mesh;
        }

        private static readonly Dictionary<Mesh, Mesh> LowMeshes = new Dictionary<Mesh, Mesh>();
        /// <summary>How many cells across its longest side a body is gathered into for its light mesh (a man: 8 cm each).</summary>
        private const float LowCells = 24f;

        /// <summary>
        /// A light copy of a GPU skin mesh (SkinMesh) for a character small on the screen: the points of each small cell that
        /// hang on the same bone become one (the one found first, with its colour, surface and weights), and the faces that
        /// close up are dropped. A distant crowd was a third of the frame's triangles, 6-7 thousand a man 50 px tall.
        /// </summary>
        public static Mesh LowMesh(Mesh skin)
        {
            if (skin == null) return null;
            if (LowMeshes.TryGetValue(skin, out Mesh cached) && cached != null) return cached;
            if (!skin.isReadable) return null;
            Vector3[] v = skin.vertices, n = skin.normals;
            Color[] colors = skin.colors;
            var uv2 = new List<Vector4>(); var uv3 = new List<Vector4>(); var weights = new List<Vector4>(); var bones = new List<Vector4>();
            skin.GetUVs(2, uv2); skin.GetUVs(3, uv3); skin.GetUVs(4, weights); skin.GetUVs(5, bones);
            if (v.Length == 0 || bones.Count != v.Length) return null;
            Vector3 min = v[0], max = v[0];
            foreach (Vector3 p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            float cell = Mathf.Max(1e-4f, Mathf.Max(max.x - min.x, Mathf.Max(max.y - min.y, max.z - min.z)) / LowCells);

            var found = new Dictionary<(int, int, int, int), int>(v.Length / 4);
            var remap = new int[v.Length];
            var kept = new List<int>(v.Length / 4);
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = (v[i] - min) / cell;
                var key = ((int)p.x, (int)p.y, (int)p.z, (int)bones[i].x);
                if (!found.TryGetValue(key, out int at)) { at = kept.Count; found[key] = at; kept.Add(i); }
                remap[i] = at;
            }

            List<T> Pick<T>(IList<T> from) { var to = new List<T>(kept.Count); foreach (int i in kept) to.Add(from[i]); return to; }
            var mesh = new Mesh { name = skin.name + "_Low", indexFormat = kept.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(Pick(v));
            if (n.Length == v.Length) mesh.SetNormals(Pick(n));
            if (colors.Length == v.Length) mesh.SetColors(Pick(colors));
            if (uv2.Count == v.Length) mesh.SetUVs(2, Pick(uv2));
            if (uv3.Count == v.Length) mesh.SetUVs(3, Pick(uv3));
            mesh.SetUVs(4, Pick(weights));
            mesh.SetUVs(5, Pick(bones));
            mesh.subMeshCount = skin.subMeshCount;
            var seen = new HashSet<(int, int, int)>();
            var triangles = new List<int>();
            for (int s = 0; s < skin.subMeshCount; s++)
            {
                int[] t = skin.GetTriangles(s);
                triangles.Clear();
                seen.Clear();
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = remap[t[i]], b = remap[t[i + 1]], c = remap[t[i + 2]];
                    if (a == b || b == c || a == c) continue;
                    // (the same face once, whichever of its corners comes first)
                    var face = a < b && a < c ? (a, b, c) : b < c ? (b, c, a) : (c, a, b);
                    if (!seen.Add(face)) continue;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                }
                mesh.SetTriangles(triangles, s, false);
            }
            mesh.bounds = skin.bounds;
            mesh.UploadMeshData(false);
            LowMeshes[skin] = mesh;
            return mesh;
        }

        /// <summary>The material drawing this bake on the GPU (the body's material with _GPU_SKIN and instancing).</summary>
        public Material MaterialFor(Material source)
        {
            if (source == null) return null;
            if (Materials.TryGetValue((source, this), out Material m) && m != null) return m;
            m = new Material(source) { name = source.name + "_Gpu", enableInstancing = true };
            m.EnableKeyword("_GPU_SKIN");
            m.SetTexture("_BoneTex", Texture);
            m.SetVector("_BoneTexInfo", Info);
            Materials[(source, this)] = m;
            return m;
        }
    }
}
