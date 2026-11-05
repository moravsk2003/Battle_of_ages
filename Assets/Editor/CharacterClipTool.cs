#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// The FBX knight's clips were not made for a sword and shield: the walk swings the sword like an empty hand, the idle
    /// and the run hold the arm out stiff with the shield flat like a tray, the attack is a running one, and a dead knight
    /// keeps his sword standing up out of the body. This keeps the legs and the body and bakes new clips: both arms hold a
    /// guard (sword up in front, shield upright on the left side), the attack is a swing keyed here, the sword falls with
    /// the body. The villager rig (villagers, bandits, goblins) keeps its clips but the attack, which flung both arms out
    /// like a dance: it gets the same swing. Writes Assets/Characters/KnightClips and VillagerClips and points the Animators there.
    /// The clips keep their names: CharacterAnimationDriver and GpuSkinBake look them up by name.
    /// </summary>
    public static class CharacterClipTool
    {
        private const string ModelPath = "Assets/Models/KnightGuard.fbx";
        private const string ControllerPath = "Assets/Characters/KnightAnimator.controller";
        private const string OutDir = "Assets/Characters/KnightClips";
        private const string VillagerModelPath = "Assets/Models/Villager.fbx";
        private const string VillagerControllerPath = "Assets/Characters/VillagerAnimator.controller";
        private const string VillagerOutDir = "Assets/Characters/VillagerClips";
        private const float FramesPerSecond = 30f;

        /// <summary>The sword arm in the chest's frame (x to the knight's right, y up, z forward), the chest's turn to his right in degrees.</summary>
        private struct Pose
        {
            public float Time, Twist;
            public Vector3 Upper, Fore, Blade;
            public Pose(float time, float twist, Vector3 upper, Vector3 fore, Vector3 blade) { Time = time; Twist = twist; Upper = upper; Fore = fore; Blade = blade; }
        }

        private static readonly Vector3 SwordUpper = new Vector3(0.28f, -0.92f, 0.22f), SwordFore = new Vector3(0.12f, -0.12f, 1f), Blade = new Vector3(0.06f, 0.78f, 0.62f);
        private static readonly Vector3 ShieldUpper = new Vector3(-0.38f, -0.72f, 0.58f), ShieldFore = new Vector3(0.20f, -0.84f, 0.50f), ShieldFace = new Vector3(-0.30f, 0.05f, 0.95f); // the forearm mostly down: the shield's point follows it

        private const float AttackLength = 0.8f;

        /// <summary>From the guard up over the right shoulder, down across at the moment the hit lands (GameBalance.MeleeWindup, 0.35 s), back to the guard.</summary>
        private static readonly Pose[] Swing =
        {
            new Pose(0f, 0f, SwordUpper, SwordFore, Blade),
            new Pose(0.20f, 28f, new Vector3(0.82f, 0.30f, -0.25f), new Vector3(0.28f, 0.85f, 0f), new Vector3(-0.10f, 0.55f, -0.80f)), // the elbow out: the hand goes beside the head, not over it
            new Pose(0.33f, -5f, new Vector3(0.42f, 0.10f, 0.90f), new Vector3(0.05f, 0.05f, 1f), new Vector3(-0.10f, 0.35f, 0.95f)),
            new Pose(0.46f, -26f, new Vector3(0.22f, -0.60f, 0.78f), new Vector3(-0.28f, -0.48f, 0.84f), new Vector3(-0.62f, -0.48f, 0.62f)),
            new Pose(AttackLength, 0f, SwordUpper, SwordFore, Blade),
        };

        [MenuItem("Battle of Ages/Build Character Clips")]
        public static void Build()
        {
            BuildKnight();
            BuildVillager();
            AssetDatabase.SaveAssets();
        }

        private static void BuildVillager()
        {
            AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(VillagerModelPath).OfType<AnimationClip>().First(c => c.name == "Idle");
            GameObject model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(VillagerModelPath));
            try
            {
                Directory.CreateDirectory(VillagerOutDir);
                var rig = new Rig(model, idle, false);
                var made = new Dictionary<string, AnimationClip> { ["Attack"] = Save(Bake("Attack", idle, AttackLength, rig, rig.SwingAt), VillagerOutDir) };
                PointAnimator(VillagerControllerPath, made);
                Debug.Log($"[KnightClips] villager attack {made["Attack"].length:F2} s, blade along palm {(rig.BladeSign > 0f ? "+" : "-")}X");
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        private static void BuildKnight()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            AnimationClip[] sources = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
            AnimationClip Source(string name) => sources.First(c => c.name == name);
            GameObject model = Object.Instantiate(prefab);
            try
            {
                Directory.CreateDirectory(OutDir);
                var rig = new Rig(model, Source("Idle"), true);
                var made = new Dictionary<string, AnimationClip>
                {
                    // How much of the original arm swing is kept
                    ["Idle"] = Save(Bake("Idle", Source("Idle"), Source("Idle").length, rig, t => rig.HoldGuard(0.05f)), OutDir),
                    ["Walk"] = Save(Bake("Walk", Source("Walk"), Source("Walk").length, rig, t => rig.HoldGuard(0.10f)), OutDir),
                    ["Run"] = Save(Bake("Run", Source("Run"), Source("Run").length, rig, t => rig.HoldGuard(0.14f)), OutDir),
                    ["Attack"] = Save(Bake("Attack", Source("Idle"), AttackLength, rig, rig.SwingAt), OutDir),
                    ["Death"] = Save(Bake("Death", Source("Death"), Source("Death").length, rig, t => rig.FallWithSword(t, Source("Death").length)), OutDir),
                };
                PointAnimator(ControllerPath, made);
                Debug.Log($"[KnightClips] baked {string.Join(", ", made.Select(m => $"{m.Key} {m.Value.length:F2} s"))}");
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        private static AnimationClip Save(AnimationClip clip, string folder)
        {
            string path = $"{folder}/{clip.name}.anim";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private sealed class Rig
        {
            public readonly GameObject Model;
            public readonly float BladeSign; // along palm X: the two rigs differ, the blade leaves the fist on the thumb's side
            private readonly bool shield;
            private readonly Transform torso, head, upperR, foreR, palmR, knucklesR, upperL, foreL, palmL;
            private readonly Vector3 right, up, forward;
            private readonly Quaternion restTorso;

            public Rig(GameObject model, AnimationClip idle, bool knight)
            {
                Model = model;
                shield = knight;
                Transform Find(string name) => model.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
                torso = Find("Torso"); head = Find("Head");
                upperR = Find("UpperArm.R"); foreR = Find("LowerArm.R"); palmR = Find("Palm.R"); knucklesR = Find("MiddleHand.R");
                upperL = Find("UpperArm.L"); foreL = Find("LowerArm.L"); palmL = Find("Palm.L");

                // The chest's frame from the standing pose, then turned with the torso frame by frame
                idle.SampleAnimation(model, 0f);
                right = (upperR.position - upperL.position).normalized;
                up = Vector3.ProjectOnPlane(Find("Neck").position - Find("Hips").position, right).normalized;
                forward = Vector3.Cross(right, up);
                restTorso = torso.rotation;
                BladeSign = knight ? -1f : Mathf.Sign(Vector3.Dot(palmR.right, Find("Thumb1.R").position - palmR.position));
            }

            private Vector3 Chest(Vector3 d) => torso.rotation * Quaternion.Inverse(restTorso) * (right * d.x + up * d.y + forward * d.z).normalized;

            private static void Aim(Transform bone, Transform child, Vector3 direction) =>
                bone.rotation = Quaternion.FromToRotation(child.position - bone.position, direction) * bone.rotation;

            private void AimBlade(Vector3 direction)
            {
                palmR.rotation = Quaternion.FromToRotation(palmR.right * BladeSign, direction) * palmR.rotation;

                // Around the blade the hand is free: it continues the forearm, or it hangs off the wrist
                Vector3 knuckles = Vector3.ProjectOnPlane(knucklesR.position - palmR.position, direction);
                Vector3 forearm = Vector3.ProjectOnPlane(palmR.position - foreR.position, direction);
                if (knuckles.sqrMagnitude < 1e-8f || forearm.sqrMagnitude < 1e-8f) return;
                palmR.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(knuckles, forearm, direction), direction) * palmR.rotation;
            }

            private void HoldShield(float follow)
            {
                Vector3 original = (foreL.position - upperL.position).normalized;
                Aim(upperL, foreL, Vector3.Slerp(Chest(ShieldUpper), original, follow));
                Aim(foreL, palmL, Chest(ShieldFore));
                Vector3 along = (palmL.position - foreL.position).normalized;
                Vector3 now = Vector3.ProjectOnPlane(-foreL.right, along), want = Vector3.ProjectOnPlane(Chest(ShieldFace), along);
                foreL.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(now, want, along), along) * foreL.rotation; // the face looks along forearm -X
            }

            public void HoldGuard(float follow)
            {
                Vector3 original = (foreR.position - upperR.position).normalized;
                Aim(upperR, foreR, Vector3.Slerp(Chest(SwordUpper), original, follow));
                Aim(foreR, palmR, Chest(SwordFore));
                AimBlade(Chest(Blade));
                HoldShield(follow);
            }

            public void SwingAt(float t)
            {
                int next = 1;
                while (next < Swing.Length - 1 && Swing[next].Time < t) next++;
                Pose a = Swing[next - 1], b = Swing[next];
                float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.Time, b.Time, t));

                // The chest turns into the swing, the head stays on the enemy
                float twist = Mathf.Lerp(a.Twist, b.Twist, s);
                Quaternion turn = Quaternion.AngleAxis(twist, up);
                torso.rotation = turn * torso.rotation;
                head.rotation = Quaternion.AngleAxis(-twist * 0.7f, up) * head.rotation;

                Aim(upperR, foreR, Chest(Vector3.Slerp(a.Upper.normalized, b.Upper.normalized, s)));
                Aim(foreR, palmR, Chest(Vector3.Slerp(a.Fore.normalized, b.Fore.normalized, s)));
                AimBlade(Chest(Vector3.Slerp(a.Blade.normalized, b.Blade.normalized, s)));
                if (shield) HoldShield(0f);
            }

            /// <summary>The fall stays as it was; the blade leaves the guard and ends flat on the ground beside the body.</summary>
            public void FallWithSword(float t, float length)
            {
                float s = Mathf.SmoothStep(0f, 1f, t / (length * 0.42f)); // flat before he is on his knees: else the tip goes into the ground
                Vector3 flat = (right * 0.75f - forward * 0.65f).normalized;
                Vector3 hanging = Vector3.Slerp(Chest(new Vector3(0.15f, -0.75f, 0.65f)), flat, s);
                AimBlade(hanging);
            }
        }

        private static AnimationClip Bake(string name, AnimationClip source, float length, Rig rig, Action<float> pose)
        {
            GameObject model = rig.Model;
            Transform[] bones = model.GetComponentsInChildren<Transform>(true).Where(t => t != model.transform && t.GetComponent<Renderer>() == null).ToArray();
            int frames = Mathf.Max(2, Mathf.RoundToInt(length * FramesPerSecond) + 1);
            float step = length / (frames - 1);
            var localPosition = new Vector3[frames, bones.Length];
            var localRotation = new Quaternion[frames, bones.Length];

            for (int f = 0; f < frames; f++)
            {
                source.SampleAnimation(model, Mathf.Min(f * step, source.length));
                pose(f * step);
                for (int b = 0; b < bones.Length; b++)
                {
                    localPosition[f, b] = bones[b].localPosition;
                    Quaternion q = bones[b].localRotation;
                    if (f > 0 && Quaternion.Dot(localRotation[f - 1, b], q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // no flips between keys
                    localRotation[f, b] = q;
                }
            }

            var clip = new AnimationClip { name = name, frameRate = FramesPerSecond };
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
            settings.loopTime = source.isLooping && name != "Attack";
            settings.stopTime = length;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        private static void Curve(AnimationClip clip, string path, string property, int frames, float step, Func<int, float> value)
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

        private static void PointAnimator(string controllerPath, Dictionary<string, AnimationClip> made)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            foreach (AnimatorControllerLayer layer in controller.layers)
                foreach (ChildAnimatorState child in layer.stateMachine.states)
                {
                    AnimatorState state = child.state;
                    if (state.motion is BlendTree tree)
                    {
                        ChildMotion[] children = tree.children;
                        for (int i = 0; i < children.Length; i++)
                            if (children[i].motion != null && made.TryGetValue(children[i].motion.name, out AnimationClip clip)) children[i].motion = clip;
                        tree.children = children;
                        EditorUtility.SetDirty(tree);
                    }
                    else if (state.motion != null && made.TryGetValue(state.motion.name, out AnimationClip clip))
                    {
                        state.motion = clip;
                        EditorUtility.SetDirty(state);
                    }
                }
            EditorUtility.SetDirty(controller);
        }
    }
}
#endif
