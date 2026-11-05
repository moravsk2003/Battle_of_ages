#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// The FBX knight's walk swings its sword like an empty hand, its idle and run hold the arm out stiff with the shield
    /// flat like a tray. This keeps those clips' legs and body and bakes new ones in which both arms hold a guard: sword
    /// up in front, shield across the left side. The arms still follow the original swing a little, so they do not look
    /// bolted on. Writes Assets/Characters/KnightClips/*.anim and points the knight's Animator at them.
    /// The clips keep their names: CharacterAnimationDriver and GpuSkinBake look them up by name.
    /// </summary>
    public static class KnightClipTool
    {
        private const string ModelPath = "Assets/Models/KnightGuard.fbx";
        private const string ControllerPath = "Assets/Characters/KnightAnimator.controller";
        private const string OutDir = "Assets/Characters/KnightClips";
        private const float FramesPerSecond = 30f;

        // In the chest's frame: x to the knight's right, y up, z forward
        private static readonly Vector3 SwordUpper = new Vector3(0.28f, -0.92f, 0.22f), SwordFore = new Vector3(0.12f, -0.12f, 1f), Blade = new Vector3(0.06f, 0.78f, 0.62f);
        private static readonly Vector3 ShieldUpper = new Vector3(-0.38f, -0.72f, 0.58f), ShieldFore = new Vector3(0.20f, -0.84f, 0.50f), ShieldFace = new Vector3(-0.30f, 0.05f, 0.95f); // the forearm mostly down: the shield's point follows it

        private struct Held { public string Name; public float Follow; }

        // How much of the original arm swing is kept
        private static readonly Held[] Clips =
        {
            new Held { Name = "Idle", Follow = 0.05f },
            new Held { Name = "Walk", Follow = 0.10f },
            new Held { Name = "Run", Follow = 0.14f },
        };

        [MenuItem("Battle of Ages/Build Knight Held Clips")]
        public static void Build()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            AnimationClip[] sources = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            GameObject model = Object.Instantiate(prefab);
            try
            {
                Directory.CreateDirectory(OutDir);
                var made = new Dictionary<string, AnimationClip>();
                foreach (Held held in Clips)
                {
                    AnimationClip source = sources.First(c => c.name == held.Name);
                    string path = $"{OutDir}/{held.Name}.anim";
                    AnimationClip clip = Bake(source, sources.First(c => c.name == "Idle"), model, held.Follow);
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(clip, path);
                    made[held.Name] = clip;
                }
                PointAnimator(made);
                AssetDatabase.SaveAssets();
                Debug.Log($"[KnightClips] baked {string.Join(", ", made.Select(m => $"{m.Key} {m.Value.length:F2} s"))}");
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        private static Transform Find(GameObject model, string name) =>
            model.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        private static AnimationClip Bake(AnimationClip source, AnimationClip idle, GameObject model, float follow)
        {
            Transform torso = Find(model, "Torso"), hips = Find(model, "Hips"), neck = Find(model, "Neck");
            Transform upperR = Find(model, "UpperArm.R"), foreR = Find(model, "LowerArm.R"), palmR = Find(model, "Palm.R");
            Transform upperL = Find(model, "UpperArm.L"), foreL = Find(model, "LowerArm.L"), palmL = Find(model, "Palm.L");

            // The chest's frame from the standing pose, then turned with the torso frame by frame
            idle.SampleAnimation(model, 0f);
            Vector3 right = (upperR.position - upperL.position).normalized;
            Vector3 up = Vector3.ProjectOnPlane(neck.position - hips.position, right).normalized;
            Vector3 forward = Vector3.Cross(right, up);
            Quaternion restTorso = torso.rotation;

            Transform[] bones = model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform && t.GetComponent<Renderer>() == null).ToArray();
            int frames = Mathf.Max(2, Mathf.RoundToInt(source.length * FramesPerSecond) + 1);
            float step = source.length / (frames - 1);
            var localPosition = new Vector3[frames, bones.Length];
            var localRotation = new Quaternion[frames, bones.Length];

            for (int f = 0; f < frames; f++)
            {
                source.SampleAnimation(model, f * step);
                Quaternion turn = torso.rotation * Quaternion.Inverse(restTorso);
                Vector3 Chest(Vector3 d) => turn * (right * d.x + up * d.y + forward * d.z).normalized;
                Vector3 originalR = (foreR.position - upperR.position).normalized, originalL = (foreL.position - upperL.position).normalized;

                Aim(upperR, foreR, Vector3.Slerp(Chest(SwordUpper), originalR, follow));
                Aim(foreR, palmR, Chest(SwordFore));
                palmR.rotation = Quaternion.FromToRotation(-palmR.right, Chest(Blade)) * palmR.rotation; // the blade runs along palm -X

                Aim(upperL, foreL, Vector3.Slerp(Chest(ShieldUpper), originalL, follow));
                Aim(foreL, palmL, Chest(ShieldFore));
                Vector3 along = (palmL.position - foreL.position).normalized;
                Vector3 now = Vector3.ProjectOnPlane(-foreL.right, along), want = Vector3.ProjectOnPlane(Chest(ShieldFace), along);
                foreL.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(now, want, along), along) * foreL.rotation; // the face looks along forearm -X

                for (int b = 0; b < bones.Length; b++)
                {
                    localPosition[f, b] = bones[b].localPosition;
                    Quaternion q = bones[b].localRotation;
                    if (f > 0 && Quaternion.Dot(localRotation[f - 1, b], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // no flips between keys
                    localRotation[f, b] = q;
                }
            }

            var clip = new AnimationClip { name = source.name, frameRate = FramesPerSecond };
            for (int b = 0; b < bones.Length; b++)
            {
                string path = AnimationUtility.CalculateTransformPath(bones[b], model.transform);
                int bone = b;
                Curve(clip, path, "m_LocalPosition.x", frames, step, f => localPosition[f, bone].x);
                Curve(clip, path, "m_LocalPosition.y", frames, step, f => localPosition[f, bone].y);
                Curve(clip, path, "m_LocalPosition.z", frames, step, f => localPosition[f, bone].z);
                Curve(clip, path, "m_LocalRotation.x", frames, step, f => localRotation[f, bone].x);
                Curve(clip, path, "m_LocalRotation.y", frames, step, f => localRotation[f, bone].y);
                Curve(clip, path, "m_LocalRotation.z", frames, step, f => localRotation[f, bone].z);
                Curve(clip, path, "m_LocalRotation.w", frames, step, f => localRotation[f, bone].w);
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static void Aim(Transform bone, Transform child, Vector3 direction) =>
            bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;

        private static void Curve(AnimationClip clip, string path, string property, int frames, float step, System.Func<int, float> value)
        {
            var keys = new Keyframe[frames];
            for (int f = 0; f < frames; f++)
            {
                float before = value(Mathf.Max(0, f - 1)), after = value(Mathf.Min(frames - 1, f + 1));
                float slope = (after - before) / (step * (Mathf.Min(frames - 1, f + 1) - Mathf.Max(0, f - 1)));
                keys[f] = new Keyframe(f * step, value(f), slope, slope);
            }
            clip.SetCurve(path, typeof(Transform), property, new AnimationCurve(keys));
        }

        private static void PointAnimator(Dictionary<string, AnimationClip> made)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimatorState locomotion = controller.layers[0].stateMachine.states.Select(s => s.state).First(s => s.name == "Locomotion");
            var tree = (BlendTree)locomotion.motion;
            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length; i++)
                if (children[i].motion != null && made.TryGetValue(children[i].motion.name, out AnimationClip clip)) children[i].motion = clip;
            tree.children = children;
            EditorUtility.SetDirty(tree);
            EditorUtility.SetDirty(controller);
        }
    }
}
#endif
