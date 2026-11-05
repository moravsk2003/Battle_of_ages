using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Review tool for the characters' clips (not a regular test): measures every clip of every role and shoots each as
    /// a sheet of 8 frames from the side and 8 from the front, so motion can be judged without watching it.
    /// CAPTURE_ROLES = "Villager,KnightGuard" (HumanType names), CAPTURE_DIR = output folder, CAPTURE_NO_SHOTS = 1 only measures,
    /// CAPTURE_WEAPON = Sword / Bow / ... arms them (a bare model has no weapon: units get theirs from Equipment).
    /// report.txt holds, per clip: the seam of a loop, the fastest joint and its jump over the joint's usual pace, how far
    /// the body sinks or floats, the speed the feet push the ground at against the speed the game plays it for, how far
    /// a foot slides sideways, the knees, the hands against the body, the head against the hips.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class AnimationReview
    {
        private const int SamplesPerSecond = 60, SheetFrames = 8;
        private static readonly string[] ClipOrder = { "Idle", "Walk", "Run", "Attack", "Death", "Shoot" };
        /// <summary>CAPTURE_CLIPS = "Shoot,Attack": only these (all when unset).</summary>
        private static bool Wanted(string clip)
        {
            string only = Environment.GetEnvironmentVariable("CAPTURE_CLIPS");
            return string.IsNullOrEmpty(only) || Array.IndexOf(only.Split(','), clip) >= 0;
        }

        [TearDown]
        public void GpuBackOn() => GpuSkinnedCharacter.Enabled = true;

        /// <summary>Quaternion.Angle reads anything below ~10 deg/s at this rate as nothing.</summary>
        private static float Angle(Quaternion a, Quaternion b)
        {
            double dot = Math.Min(1.0, Math.Abs((double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w));
            return (float)(2.0 * Math.Asin(Math.Sqrt(Math.Max(0.0, 1.0 - dot * dot))) * (180.0 / Math.PI));
        }

        [UnityTest]
        public IEnumerator Review()
        {
            // The GPU draws its own baked idle over a far character: the sampled pose would never show
            GpuSkinnedCharacter.Enabled = false;

            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "anim");
            Directory.CreateDirectory(dir);
            string roles = Environment.GetEnvironmentVariable("CAPTURE_ROLES");
            if (string.IsNullOrEmpty(roles)) roles = "Villager,KnightGuard";
            bool shots = Environment.GetEnvironmentVariable("CAPTURE_NO_SHOTS") != "1";

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            Vector3 at = new Vector3(18f, 0f, -24f);
            at.y = InfiniteWorldManager.GetSampledTerrainHeight(at.x, at.z);
            ModelCapture.ClearStage(new[] { at }, 10f);
            GrassFieldRenderer grass = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            if (grass != null) grass.enabled = false;

            Light sun = RenderSettings.sun;
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.back;
            toSun.y = 0f;
            Quaternion facing = Quaternion.LookRotation(toSun.sqrMagnitude > 1e-4f ? toSun.normalized : Vector3.back);

            var report = new StringBuilder();
            foreach (string roleName in roles.Split(','))
            {
                if (!Enum.TryParse(roleName.Trim(), out HumanoidModelBuilder.HumanType role)) continue;
                GameObject model = HumanoidModelBuilder.CreateHumanoidModel(role, "Review_" + role);
                model.transform.SetPositionAndRotation(at, facing);
                if (Enum.TryParse(Environment.GetEnvironmentVariable("CAPTURE_WEAPON"), out Crafting.ItemType weapon))
                    model.AddComponent<Crafting.Equipment>().Equip(new Crafting.Item(weapon, 4));
                yield return null;
                yield return null;

                // Only the sampled clip moves the bones
                foreach (CharacterAnimationDriver driver in model.GetComponentsInChildren<CharacterAnimationDriver>()) driver.enabled = false;
                foreach (FootIK ik in model.GetComponentsInChildren<FootIK>()) ik.enabled = false;
                foreach (GpuSkinnedCharacter gpu in model.GetComponentsInChildren<GpuSkinnedCharacter>())
                {
                    Assert.IsFalse(gpu.IsFar, "the GPU would draw this one, not its bones");
                    gpu.enabled = false;
                }
                foreach (SkinnedMeshLod lod in model.GetComponentsInChildren<SkinnedMeshLod>()) { lod.NearScreenHeight = 0.0001f; lod.Apply(); }
                yield return null;
                Animator animator = model.GetComponentInChildren<Animator>();
                Assert.IsNotNull(animator, role.ToString());
                animator.enabled = false;
                yield return null;

                SkinnedMeshRenderer skin = model.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.IsNotNull(skin, role.ToString());
                skin.updateWhenOffscreen = true;
                Assert.IsTrue(skin.enabled, "the skinned body is what must be drawn");

                report.AppendLine($"=== {role}: {skin.sharedMesh.vertexCount} verts, {skin.bones.Length} bones");
                report.AppendLine("bones: " + string.Join(" ", skin.bones.Where(b => b != null).Select(b => b.name)));
                AnimationClip[] clips = animator.runtimeAnimatorController.animationClips
                    .GroupBy(c => c.name).Select(g => g.First())
                    .OrderBy(c => { int i = Array.IndexOf(ClipOrder, c.name); return i < 0 ? 99 : i; }).ToArray();

                foreach (AnimationClip clip in clips)
                {
                    if (!Wanted(clip.name)) continue;
                    Measure(report, clip, animator, skin, model.transform);
                    if (!shots) continue;
                    string clipDir = Path.Combine(dir, $"{role}_{clip.name}");
                    var capture = new CaptureRenderer(clipDir, SheetFrames * 2, 30f, drawGrass: false);
                    Vector3 middle = at + Vector3.up * 0.95f;
                    Vector3 sideCam = middle + facing * Vector3.right * 4.6f + Vector3.up * 0.1f;
                    Vector3 frontCam = middle + facing * new Vector3(-1.6f, 0.2f, 4.2f);
                    bool loop = clip.isLooping;
                    for (int view = 0; view < 2; view++)
                        for (int i = 0; i < SheetFrames; i++)
                        {
                            float t = clip.length * i / (loop ? SheetFrames : SheetFrames - 1);
                            clip.SampleAnimation(animator.gameObject, t);
                            yield return null; // skinned by the frame
                            capture.Shoot(view * SheetFrames + i, $"{(view == 0 ? "side" : "front")}_{t:F2}s", view == 0 ? sideCam : frontCam, middle);
                        }
                    capture.Finish();
                    yield return null;
                }
                report.AppendLine();
                UnityEngine.Object.Destroy(model);
                yield return null;
            }

            string path = Path.Combine(dir, "report.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log("[AnimReview] written to " + path + "\n" + report);
        }

        private static void Measure(StringBuilder report, AnimationClip clip, Animator animator, SkinnedMeshRenderer skin, Transform root)
        {
            Transform[] bones = skin.bones.Where(b => b != null).ToArray();
            int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length * SamplesPerSecond) + 1);
            float dt = clip.length / (frames - 1);
            var position = new Vector3[frames, bones.Length];
            var local = new Quaternion[frames, bones.Length];
            var lowest = new float[frames];
            var baked = new Mesh();
            var verts = new List<Vector3>();

            for (int f = 0; f < frames; f++)
            {
                clip.SampleAnimation(animator.gameObject, f * dt);
                for (int b = 0; b < bones.Length; b++)
                {
                    position[f, b] = root.InverseTransformPoint(bones[b].position);
                    local[f, b] = bones[b].localRotation;
                }
                skin.BakeMesh(baked, true);
                baked.GetVertices(verts);
                Matrix4x4 toRoot = root.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                float low = float.MaxValue;
                for (int v = 0; v < verts.Count; v++)
                {
                    float y = toRoot.MultiplyPoint3x4(verts[v]).y;
                    if (y < low) low = y;
                }
                lowest[f] = low;
            }
            UnityEngine.Object.Destroy(baked);

            int Index(string name) => Array.FindIndex(bones, b => b.name == name);
            report.AppendLine($"-- {clip.name}: {clip.length:F2} s, loop {clip.isLooping}, root motion {clip.hasRootCurves || clip.hasMotionCurves}");

            if (clip.isLooping)
            {
                float gap = 0f, turn = 0f;
                string gapBone = "", turnBone = "";
                for (int b = 0; b < bones.Length; b++)
                {
                    float g = Vector3.Distance(position[0, b], position[frames - 1, b]);
                    float a = Angle(local[0, b], local[frames - 1, b]);
                    if (g > gap) { gap = g; gapBone = bones[b].name; }
                    if (a > turn) { turn = a; turnBone = bones[b].name; }
                }
                report.AppendLine($"   loop seam: {gap * 100f:F1} cm ({gapBone}), {turn:F1} deg ({turnBone})");
            }

            // A pop: one step far above what that joint usually does in this clip
            float fastest = 0f, fastestAt = 0f, jump = 0f, jumpAt = 0f;
            string fastestBone = "", jumpBone = "";
            var steps = new List<float>(frames);
            for (int b = 0; b < bones.Length; b++)
            {
                steps.Clear();
                for (int f = 1; f < frames; f++)
                {
                    float step = Angle(local[f - 1, b], local[f, b]);
                    steps.Add(step);
                    if (step / dt > fastest) { fastest = step / dt; fastestAt = f * dt; fastestBone = bones[b].name; }
                }
                float usual = steps.OrderBy(s => s).ElementAt(steps.Count / 2);
                float largest = steps.Max();
                float ratio = largest / Mathf.Max(usual, 0.5f); // still joints do not count
                if (largest > 3f && ratio > jump) { jump = ratio; jumpAt = (steps.IndexOf(largest) + 1) * dt; jumpBone = bones[b].name; }
            }
            report.AppendLine($"   fastest joint: {fastest:F0} deg/s ({fastestBone}, {fastestAt:F2} s); biggest jump x{jump:F1} ({jumpBone}, {jumpAt:F2} s)");
            report.AppendLine($"   mesh lowest point over the ground: {lowest.Min() * 100f:F1} .. {lowest.Max() * 100f:F1} cm");

            int hips = Index("Hips"), neck = Index("Neck"), head = Index("Head");
            foreach (string side in new[] { "L", "R" })
            {
                int upper = Index("UpperLeg." + side), knee = Index("LowerLeg." + side), foot = Index("Foot." + side);
                if (upper < 0 || knee < 0 || foot < 0 || hips < 0) { report.AppendLine($"   leg {side}: bones not found"); continue; }

                // The stance: while the foot is down its speed against the hips is the speed it pushes the ground at
                float footLow = float.MaxValue;
                for (int f = 0; f < frames; f++) footLow = Mathf.Min(footLow, position[f, foot].y);
                float back = 0f, sideways = 0f, kneeFront = float.MaxValue, liftAll = 0f;
                int stance = 0;
                for (int f = 0; f < frames; f++)
                {
                    liftAll = Mathf.Max(liftAll, position[f, foot].y - footLow);
                    Vector3 a = position[f, upper], k = position[f, knee], c = position[f, foot];
                    Vector3 axis = c - a;
                    Vector3 onLine = a + axis * (axis.sqrMagnitude > 1e-6f ? Vector3.Dot(k - a, axis) / axis.sqrMagnitude : 0f);
                    kneeFront = Mathf.Min(kneeFront, k.z - onLine.z);
                    if (f == 0 || position[f, foot].y > footLow + 0.03f) continue;
                    Vector3 was = position[f - 1, foot] - position[f - 1, hips];
                    Vector3 now = position[f, foot] - position[f, hips];
                    back += (was.z - now.z) / dt;
                    sideways += Mathf.Abs(now.x - was.x) / dt;
                    stance++;
                }
                string push = stance > 0 ? $"pushes {back / stance:F2} m/s, sideways {sideways / stance:F2} m/s" : "never down";
                report.AppendLine($"   leg {side}: down {100f * stance / frames:F0}% of the clip, {push}, lifts {liftAll * 100f:F0} cm, knee in front of the leg line by {kneeFront * 100f:F1} cm at least");
            }
            if (clip.name == "Walk") report.AppendLine($"   the game plays it for {CharacterAnimationDriver.WalkClipSpeed:F2} m/s");
            if (clip.name == "Run") report.AppendLine($"   the game plays it for {CharacterAnimationDriver.RunClipSpeed:F2} m/s");

            if (hips >= 0 && neck >= 0)
                foreach (string side in new[] { "L", "R" })
                {
                    int hand = Index("Palm." + side);
                    if (hand < 0) continue;
                    float near = float.MaxValue, nearAt = 0f;
                    for (int f = 0; f < frames; f++)
                    {
                        Vector3 a = position[f, hips], axis = position[f, neck] - a;
                        Vector3 onAxis = a + axis * Mathf.Clamp01(Vector3.Dot(position[f, hand] - a, axis) / Mathf.Max(axis.sqrMagnitude, 1e-6f));
                        float d = Vector3.Distance(position[f, hand], onAxis);
                        if (d < near) { near = d; nearAt = f * dt; }
                    }
                    report.AppendLine($"   hand {side}: closest to the spine {near * 100f:F0} cm at {nearAt:F2} s");
                }

            if (hips >= 0 && head >= 0)
            {
                float above = float.MaxValue;
                for (int f = 0; f < frames; f++) above = Mathf.Min(above, position[f, head].y - position[f, hips].y);
                report.AppendLine($"   head above the hips by {above * 100f:F0} cm at least");
            }

            int palm = Index("Palm.R");
            if (clip.name == "Attack" && palm >= 0)
            {
                float peak = 0f, peakAt = 0f;
                for (int f = 1; f < frames; f++)
                {
                    float speed = Vector3.Distance(position[f - 1, palm], position[f, palm]) / dt;
                    if (speed > peak) { peak = speed; peakAt = f * dt; }
                }
                report.AppendLine($"   swing: right hand peaks {peak:F1} m/s at {peakAt:F2} s; a melee hit lands at {Combat.GameBalance.MeleeWindup:F2} s");
            }
        }
    }
}
