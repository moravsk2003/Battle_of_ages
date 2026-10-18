using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Distant characters are drawn on the GPU (GpuSkinnedCharacter): far from the camera the Animator and the skinned
    /// renderer stop and the GPU body draws, up close the Animator is back. Any shader or skinning error fails the test.
    /// </summary>
    public class GpuSkinTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private Camera cam;
        private RenderTexture rt;

        [SetUp]
        public void SetUp()
        {
            rt = new RenderTexture(640, 480, 24);
            cam = new GameObject("GpuSkinCam").AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.fieldOfView = 60f;
            GpuSkinnedCharacter.ViewerOverride = cam;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            spawned.Add(sun.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            GpuSkinnedCharacter.ViewerOverride = null;
            GpuSkinnedCharacter.ForceGpu = false;
            foreach (GameObject g in spawned) if (g != null) UnityEngine.Object.Destroy(g);
            spawned.Clear();
            if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
            if (rt != null) rt.Release();
        }

        private GameObject Unit(HumanoidModelBuilder.HumanType type, Vector3 pos)
        {
            GameObject go = GpuSkinCapture.MakeUnit(type);
            go.transform.position = pos;
            spawned.Add(go);
            return go;
        }

        private IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) { cam.Render(); yield return null; }
        }

        [UnityTest]
        public IEnumerator FarCharacters_AreDrawnOnTheGpu_NearOnesByTheAnimator()
        {
            var units = new[]
            {
                Unit(HumanoidModelBuilder.HumanType.KnightGuard, new Vector3(-1f, 0f, 0f)),
                Unit(HumanoidModelBuilder.HumanType.Bandit, new Vector3(1f, 0f, 0f)),
            };
            cam.transform.position = new Vector3(0f, 1.5f, -45f);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            yield return Frames(12);

            foreach (GameObject u in units)
            {
                GpuSkinnedCharacter gpu = u.GetComponentInChildren<GpuSkinnedCharacter>();
                Assert.IsNotNull(gpu, $"{u.name}: one skinned body, so it can be drawn on the GPU");
                Assert.IsTrue(gpu.IsFar, $"{u.name}: drawn on the GPU 45 m away");
                Assert.IsFalse(u.GetComponentInChildren<Animator>().enabled, $"{u.name}: Animator off while far");
                Assert.IsFalse(u.GetComponentInChildren<SkinnedMeshRenderer>().enabled, $"{u.name}: skinned body off while far");
                MeshRenderer body = System.Array.Find(u.GetComponentsInChildren<MeshRenderer>(true), r => r.name == "GpuBody");
                Assert.IsNotNull(body, $"{u.name}: GPU body");
                Assert.IsTrue(body.enabled, $"{u.name}: GPU body drawn");
            }

            // An attack and a death while far must not break anything
            units[0].GetComponent<CombatUnit>().TriggerAttackAnimation();
            yield return Frames(5);
            units[1].GetComponent<HealthComponent>().TakeDamage(1000);
            yield return Frames(10);

            cam.transform.position = new Vector3(0f, 1.5f, -3f);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            yield return Frames(12);
            GpuSkinnedCharacter knight = units[0].GetComponentInChildren<GpuSkinnedCharacter>();
            Assert.IsFalse(knight.IsFar, "close up: the Animator again");
            Assert.IsTrue(units[0].GetComponentInChildren<Animator>().enabled);
            Assert.IsTrue(units[0].GetComponentInChildren<SkinnedMeshRenderer>().enabled);
        }
    }

    /// <summary>
    /// Review tool: the same characters drawn by the Animator (top row) and on the GPU (bottom row) in each clip:
    /// idle, walk, run, attack while standing, death. Writes gpu_skin_sheet.png to CAPTURE_DIR.
    /// </summary>
    [Explicit("Review tool, run on demand")]
    public class GpuSkinCapture
    {
        /// <summary>A knight like the stress test's, or a raider as the camps make it (its AI off: it stays put).</summary>
        public static GameObject MakeUnit(HumanoidModelBuilder.HumanType type)
        {
            if (type == HumanoidModelBuilder.HumanType.Bandit)
            {
                GameObject mob = HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "Unit_Bandit");
                mob.GetComponent<HostileMobAI>().enabled = false;
                return mob;
            }
            var go = new GameObject("Unit_" + type);
            go.AddComponent<HealthComponent>().InitializeHealth(100);
            go.AddComponent<CombatUnit>().InitializeCombatStats(10, 1f, 2f);
            GameObject model = HumanoidModelBuilder.CreateHumanoidModel(type, "VisualModel");
            model.transform.SetParent(go.transform, false);
            return go;
        }

        [UnityTest]
        public IEnumerator CaptureAnimatorVsGpu()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(400, 520, 24);
            Camera cam = new GameObject("Cam").AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.fieldOfView = 30f;
            cam.backgroundColor = new Color(0.55f, 0.62f, 0.7f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
            GpuSkinnedCharacter.ViewerOverride = cam;

            string[] poses = { "idle", "walk", "run", "attack", "death" };
            var types = new[] { HumanoidModelBuilder.HumanType.KnightGuard, HumanoidModelBuilder.HumanType.Bandit };
            var sheet = new Texture2D(400 * poses.Length, 520 * 2 * types.Length, TextureFormat.RGB24, false);
            for (int t = 0; t < types.Length; t++)
                for (int gpu = 0; gpu < 2; gpu++)
                    for (int p = 0; p < poses.Length; p++)
                    {
                        GpuSkinnedCharacter.ForceGpu = gpu == 1;
                        GameObject go = MakeUnit(types[t]);
                        go.transform.rotation = Quaternion.Euler(0f, 160f, 0f);
                        CharacterAnimationDriver driver = go.GetComponentInChildren<CharacterAnimationDriver>();
                        driver.SpeedOverride = poses[p] == "walk" ? 1.4f : poses[p] == "run" ? 4.2f : 0f;
                        cam.transform.position = new Vector3(0f, 1.1f, 7.5f);
                        cam.transform.LookAt(new Vector3(0f, 1f, 0f));
                        for (int i = 0; i < 20; i++) { cam.Render(); yield return null; }
                        if (poses[p] == "attack") { go.GetComponent<CombatUnit>().TriggerAttackAnimation(); for (float end = Time.time + 0.35f; Time.time < end;) { cam.Render(); yield return null; } }
                        if (poses[p] == "death") { go.GetComponent<HealthComponent>().TakeDamage(1000); for (float end = Time.time + 2.2f; Time.time < end;) { cam.Render(); yield return null; } }
                        GpuSkinnedCharacter.RefreshAll(cam);
                        cam.Render();
                        RenderTexture.active = rt;
                        var shot = new Texture2D(400, 520, TextureFormat.RGB24, false);
                        shot.ReadPixels(new Rect(0, 0, 400, 520), 0, 0);
                        RenderTexture.active = null;
                        int row = (types.Length - 1 - t) * 2 + (1 - gpu); // knight on top, Animator above GPU
                        sheet.SetPixels(p * 400, row * 520, 400, 520, shot.GetPixels());
                        UnityEngine.Object.Destroy(shot);
                        UnityEngine.Object.Destroy(go);
                        yield return null;
                    }
            File.WriteAllBytes(Path.Combine(dir, "gpu_skin_sheet.png"), sheet.EncodeToPNG());
            GpuSkinnedCharacter.ForceGpu = false;
            GpuSkinnedCharacter.ViewerOverride = null;
            UnityEngine.Object.Destroy(cam.gameObject);
            UnityEngine.Object.Destroy(sun.gameObject);
            rt.Release();
        }
    }
}
