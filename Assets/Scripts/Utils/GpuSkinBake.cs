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
        public Clip Idle, Walk, Run, Attack, Death;
        public Vector4 Info;   // anchor column, mask row

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
            for (int i = 0; i < bindposes.Length; i += 7) key.Append('|').Append(bindposes[i].GetHashCode());
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
            var order = new[] { "Idle", "Walk", "Run", "Attack", "Death" };
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
                    for (int b = 0; b < bones.Length; b++)
                        Write(data, width, b * 3, row, toSpace * bones[b].localToWorldMatrix * bindposes[b]);
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
            return bake;
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
