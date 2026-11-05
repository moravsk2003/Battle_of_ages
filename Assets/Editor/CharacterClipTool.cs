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

        private const float AttackLength = 0.8f, ShootLength = 1f;

        /// <summary>A bow shot: the chest's turn, how far the bow arm is up on the target (0..1), the drawing arm in the chest's frame.</summary>
        private struct Shot
        {
            public float Time, Twist, Aim;
            public Vector3 Upper, Fore;
            public Shot(float time, float twist, float aim, Vector3 upper, Vector3 fore) { Time = time; Twist = twist; Aim = aim; Upper = upper; Fore = fore; }
        }

        private static readonly Vector3 BowArmUpper = new Vector3(-0.15f, -0.95f, 0.2f), BowArmFore = new Vector3(0f, -0.6f, 0.8f);

        /// <summary>
        /// Up on the target, the string drawn to the cheek, loosed when the arrow leaves (GameBalance.BowWindup, 0.5 s),
        /// the hand flying back past the ear, the bow lowered. The chest turns side-on to the target, the head stays on it.
        /// </summary>
        private static readonly Shot[] Shooting =
        {
            new Shot(0f, 0f, 0f, new Vector3(0.20f, -0.95f, 0.15f), new Vector3(0.05f, -0.6f, 0.8f)),
            new Shot(0.15f, 60f, 1f, new Vector3(0.10f, -0.40f, 0.90f), new Vector3(-0.90f, 0.30f, 0.30f)), // the hand on the string
            new Shot(0.30f, 60f, 1f, new Vector3(0.85f, 0.10f, 0.20f), new Vector3(-0.60f, 0.45f, 0.65f)),  // the elbow comes round the side, not through the chest
            new Shot(0.46f, 60f, 1f, new Vector3(0.55f, 0.25f, -0.80f), new Vector3(-0.75f, 0.24f, 0.62f)), // drawn to the cheek
            new Shot(0.52f, 60f, 1f, new Vector3(0.60f, 0.25f, -0.76f), new Vector3(-0.55f, 0.45f, 0.40f)), // loosed: the hand jumps back by the ear, the arm stays bent
            new Shot(0.75f, 60f, 1f, new Vector3(0.60f, 0.18f, -0.78f), new Vector3(-0.50f, 0.35f, 0.45f)),
            new Shot(ShootLength, 0f, 0f, new Vector3(0.20f, -0.95f, 0.15f), new Vector3(0.05f, -0.6f, 0.8f)),
        };

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
                var made = new Dictionary<string, AnimationClip>
                {
                    ["Attack"] = Save(Bake("Attack", idle, AttackLength, rig, rig.SwingAt), VillagerOutDir),
                    ["Shoot"] = Save(Bake("Shoot", idle, ShootLength, rig, rig.ShootAt), VillagerOutDir),
                };
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
                    ["Shoot"] = Save(Bake("Shoot", Source("Idle"), ShootLength, rig, rig.ShootAt), OutDir),
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
            private readonly Transform torso, head, headEnd, upperR, foreR, palmR, knucklesR, upperL, foreL, palmL;
            private float lastBowTurn;
            private readonly float metre; // the FBX rig is not fitted to height here: its units per game metre
            private readonly float bowSign; // the bow's grip along the left palm's X column, the way GearMeshBuilder builds it
            private readonly Vector3 right, up, forward;
            private readonly Quaternion restTorso;

            public Rig(GameObject model, AnimationClip idle, bool knight)
            {
                Model = model;
                shield = knight;
                Transform Find(string name) => model.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
                torso = Find("Torso"); head = Find("Head");
                headEnd = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Head_end") ?? head;
                upperR = Find("UpperArm.R"); foreR = Find("LowerArm.R"); palmR = Find("Palm.R"); knucklesR = Find("MiddleHand.R");
                upperL = Find("UpperArm.L"); foreL = Find("LowerArm.L"); palmL = Find("Palm.L");

                // The chest's frame from the standing pose, then turned with the torso frame by frame
                idle.SampleAnimation(model, 0f);
                right = (upperR.position - upperL.position).normalized;
                up = Vector3.ProjectOnPlane(Find("Neck").position - Find("Hips").position, right).normalized;
                forward = Vector3.Cross(right, up);
                restTorso = torso.rotation;
                float feet = Mathf.Min(Find("Foot.L").position.y, Find("Foot.R").position.y);
                metre = (headEnd.position.y - feet) / 1.75f; // head top to the ankles: a man of 1.8 m
                BladeSign = knight ? -1f : Mathf.Sign(Vector3.Dot(palmR.right, Find("Thumb1.R").position - palmR.position));
                // GearMeshBuilder: the grip along -X of the bind matrix (whose axes may be mirrored), turned to point forward
                SkinnedMeshRenderer body = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                int palm = Array.IndexOf(body.bones, palmL);
                Vector3 bindX = palm >= 0 ? body.transform.localToWorldMatrix.MultiplyVector(body.sharedMesh.bindposes[palm].inverse.GetColumn(0)).normalized : -forward;
                Vector3 grip = Vector3.Dot(-bindX, forward) >= 0f ? -bindX : bindX;
                bowSign = Vector3.Dot(grip, bindX) > 0f ? 1f : -1f;
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

            /// <summary>A bone's axis as its matrix has it (mirrored where the rig is), as the bind matrices see it.</summary>
            private static Vector3 Column(Transform bone, int axis) => ((Vector3)bone.localToWorldMatrix.GetColumn(axis)).normalized;

            public void ShootAt(float t)
            {
                int next = 1;
                while (next < Shooting.Length - 1 && Shooting[next].Time < t) next++;
                Shot a = Shooting[next - 1], b = Shooting[next];
                float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a.Time, b.Time, t));

                float twist = Mathf.Lerp(a.Twist, b.Twist, s), aim = Mathf.Lerp(a.Aim, b.Aim, s);
                torso.rotation = Quaternion.AngleAxis(twist, up) * torso.rotation;
                head.rotation = Quaternion.AngleAxis(-twist * 0.9f, up) * head.rotation; // eyes on the target

                // The bow arm straight out at the target (the body's forward, not the turned chest's)
                Vector3 atTarget = (forward + up * 0.14f).normalized; // a little up: the grip level with the jaw, the arrow's line nearly flat
                Aim(upperL, foreL, Vector3.Slerp(Chest(BowArmUpper), atTarget, aim));
                Aim(foreL, palmL, Vector3.Slerp(Chest(BowArmFore), atTarget, aim));
                Quaternion hanging = palmL.rotation;
                palmL.rotation = Quaternion.FromToRotation(Column(palmL, 0) * bowSign, forward) * palmL.rotation;
                // The limbs (across the grip and the palm's Z) upright, canted a little
                Vector3 limbs = Vector3.ProjectOnPlane(Vector3.Cross(forward, Column(palmL, 2)), forward);
                Vector3 upright = Quaternion.AngleAxis(-12f, forward) * up;
                float turn = Vector3.SignedAngle(limbs, upright, forward);
                // The bow is the same either way up: of the two, the turn nearest the last frame's (no flip between frames)
                float other = turn > 0f ? turn - 180f : turn + 180f;
                if (t > 0f && Mathf.Abs(Mathf.DeltaAngle(other, lastBowTurn)) < Mathf.Abs(Mathf.DeltaAngle(turn, lastBowTurn))) turn = other;
                else if (t <= 0f && Mathf.Abs(other) < Mathf.Abs(turn)) turn = other;
                lastBowTurn = turn;
                palmL.rotation = Quaternion.AngleAxis(turn, forward) * palmL.rotation;
                palmL.rotation = Quaternion.Slerp(hanging, palmL.rotation, aim);

                Draw(t, aim);
            }

            /// <summary>
            /// The drawing hand by where it must be, not by guessed bone directions: on the string by the bow, drawn along
            /// the arrow's line (from the grip to the corner of the mouth) to the anchor at the jaw, loosed back along the
            /// same line past the ear. The elbow lies behind on that line's extension, a little above the shoulder; the
            /// fingers point along the string to the bow. Two-bone IK with that elbow, then AimArm (the elbow a hinge).
            /// </summary>
            private void Draw(float t, float aim)
            {
                Vector3 rest = palmR.position; // the clip's own hand (the idle), before this arm is touched
                Vector3 right = Vector3.Cross(up, forward);
                Vector3 grip = palmL.position;
                float m = metre;
                Vector3 shoulders = (upperL.position + upperR.position) * 0.5f;
                Vector3 mouth = shoulders + up * (0.1f * m) + forward * (0.12f * m) + right * (0.05f * m); // the anchor: the right side of the jaw
                Vector3 line = (grip - mouth).normalized; // the arrow, from the anchor to the bow
                Vector3 nock = grip - line * (0.1f * m), loosed = mouth - line * (0.13f * m) + right * (0.05f * m) + up * (0.02f * m);

                // Where the hand is: keys by time, smooth between
                (float time, Vector3 at)[] path =
                {
                    (0f, rest), (0.15f, nock), (0.46f, mouth), (0.52f, loosed), (0.75f, loosed - line * (0.02f * m)), (ShootLength, rest)
                };
                int k = 1;
                while (k < path.Length - 1 && path[k].time < t) k++;
                float u = Mathf.InverseLerp(path[k - 1].time, path[k].time, t);
                if (k != 4) u = Mathf.SmoothStep(0f, 1f, u); // the loose itself is a snap
                Vector3 hand = Vector3.Lerp(path[k - 1].at, path[k].at, u);

                // Where the elbow goes: down and out at rest, behind on the arrow's line and a little up when drawing
                Vector3 pole = Vector3.Slerp((Vector3.down + right * 0.4f).normalized, (-line + right * 0.3f - up * 0.05f).normalized, aim); // drawing: behind, out, at the arrow's height
                Vector3 shoulder = upperR.position;
                float upperLen = Vector3.Distance(upperR.position, foreR.position), foreLen = Vector3.Distance(foreR.position, palmR.position);
                Vector3 toHand = hand - shoulder;
                float d = Mathf.Clamp(toHand.magnitude, Mathf.Abs(upperLen - foreLen) + 0.01f * m, upperLen + foreLen - 0.002f * m);
                Vector3 along = toHand.normalized;
                float x = (upperLen * upperLen - foreLen * foreLen + d * d) / (2f * d), h = Mathf.Sqrt(Mathf.Max(0f, upperLen * upperLen - x * x));
                // Drawn, the forearm lies on the arrow's line: the elbow as near as it can be to the point a forearm's length behind the hand on it
                Vector3 onLine = hand - line * foreLen - up * (0.09f * m); // a little under the line: the upper arm near level from a shoulder joint that sits low in these rigs
                pole = Vector3.Slerp(pole, (onLine - (shoulder + along * x)).normalized, aim);
                Vector3 bend = Vector3.ProjectOnPlane(pole, along);
                bend = bend.sqrMagnitude > 1e-6f ? bend.normalized : Vector3.ProjectOnPlane(Vector3.down, along).normalized;
                Vector3 elbow = shoulder + along * x + bend * h;
                AimArm(upperR, foreR, palmR, elbow - shoulder, shoulder + along * d - elbow, t <= 0f);

                // The fingers on the string, pointing to the bow
                Vector3 fingers = knucklesR.position - palmR.position;
                Vector3 want = Vector3.Slerp(fingers.normalized, line, aim);
                palmR.rotation = Quaternion.FromToRotation(fingers, want) * palmR.rotation;
                if (t > 0.3f && t < 0.5f && Mathf.Abs(t - 0.46f) < 0.02f)
                    Debug.Log($"[Draw] {Model.name} t={t:F2} hand off the anchor {Vector3.Distance(palmR.position, mouth) / m * 100f:F1} cm, elbow above the shoulder {(foreR.position.y - shoulder.y) / m * 100f:F0} cm, mouth {(mouth - shoulder).magnitude / m * 100f:F0} cm from the shoulder, forearm off the arrow's line {Vector3.Angle(palmR.position - foreR.position, line):F0} deg");
            }

            private Vector3 lastHinge;

            /// <summary>
            /// Two bones with the elbow a hinge: the upper arm turns about itself so the arm bends in the plane of the two
            /// directions (the way its elbow bent before), the forearm folds about that hinge only. Aim's shortest turn
            /// is unstable for a forearm folded back near 180 degrees: it spins about its own length from frame to frame.
            /// The hinge keeps its side from frame to frame (the first frame: the way the elbow bent in the clip).
            /// </summary>
            private void AimArm(Transform upper, Transform fore, Transform hand, Vector3 upperDir, Vector3 foreDir, bool first)
            {
                Vector3 hinge = Vector3.Cross(fore.position - upper.position, hand.position - fore.position);
                Quaternion before = upper.rotation;
                Aim(upper, fore, upperDir);
                hinge = upper.rotation * Quaternion.Inverse(before) * hinge;
                Vector3 want = Vector3.Cross(upperDir.normalized, foreDir.normalized);
                if (hinge.sqrMagnitude < 1e-10f) { Aim(fore, hand, foreDir); return; }
                if (want.magnitude < 0.15f) // the arm nearly straight: its bend's plane is noise, the last frame's hinge stays
                {
                    if (first) { Aim(fore, hand, foreDir); return; }
                    want = Vector3.ProjectOnPlane(lastHinge, upperDir);
                    if (want.sqrMagnitude < 1e-6f) { Aim(fore, hand, foreDir); return; }
                }
                else if (Vector3.Dot(want, first ? hinge : lastHinge) < 0f) want = -want;
                want.Normalize();
                lastHinge = want;
                upper.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(Vector3.ProjectOnPlane(hinge, upperDir), want, upperDir), upperDir) * upper.rotation;
                Vector3 now = Vector3.ProjectOnPlane(hand.position - fore.position, want);
                fore.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(now, foreDir, want), want) * fore.rotation;
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
            settings.loopTime = source.isLooping && name != "Attack" && name != "Shoot";
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
            AnimatorControllerLayer[] layers = controller.layers;
            if (made.TryGetValue("Shoot", out AnimationClip shot) && layers.Length > 1 && !layers[1].stateMachine.states.Any(s => s.state.name == "Shoot"))
                layers[1].stateMachine.AddState("Shoot").motion = shot; // CharacterAnimationDriver plays it for a bow
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
