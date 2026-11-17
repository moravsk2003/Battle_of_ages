using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
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
            int warningsBefore = GpuSkinnedCharacter.SizeWarnings;
            var units = new[]
            {
                Unit(HumanoidModelBuilder.HumanType.KnightGuard, new Vector3(-1f, 0f, 0f)),
                Unit(HumanoidModelBuilder.HumanType.Bandit, new Vector3(1f, 0f, 0f)),
                Unit(HumanoidModelBuilder.HumanType.Villager, new Vector3(2.5f, 0f, 0f)),
            };
            cam.transform.position = new Vector3(0f, 1.5f, -45f);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            yield return Frames(12);
            cam.Render();
            Assert.GreaterOrEqual(CrowdDraw.LastSeen, units.Length, "the batch drew them for the camera (and whoever other tests left in sight)");

            foreach (GameObject u in units)
            {
                GpuSkinnedCharacter gpu = u.GetComponentInChildren<GpuSkinnedCharacter>();
                Assert.IsNotNull(gpu, $"{u.name}: one skinned body, so it can be drawn on the GPU");
                Assert.IsTrue(gpu.IsFar, $"{u.name}: drawn on the GPU 45 m away");
                Assert.IsFalse(u.GetComponentInChildren<Animator>().enabled, $"{u.name}: Animator off while far");
                Assert.IsFalse(u.GetComponentInChildren<SkinnedMeshRenderer>().enabled, $"{u.name}: skinned body off while far");
                Assert.IsTrue(gpu.DrawnOnGpu, $"{u.name}: a place in the crowd's batch");
            }

            // An attack and a death while far must not break anything
            units[0].GetComponent<CombatUnit>().TriggerAttackAnimation();
            yield return Frames(5);
            for (int swing = 0; swing < 3; swing++) { units[2].GetComponent<CombatUnit>().TriggerAttackAnimation(); yield return Frames(8); } // a builder's hammer
            units[1].GetComponent<HealthComponent>().TakeDamage(1000);
            yield return Frames(40);
            Assert.AreEqual(warningsBefore, GpuSkinnedCharacter.SizeWarnings, "no sound body is drawn the wrong size (the check would send it back)");
            Assert.IsTrue(units[2].GetComponentInChildren<GpuSkinnedCharacter>().DrawnOnGpu, "the villager still in the batch after hammering");

            cam.transform.position = new Vector3(0f, 1.5f, -3f);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            yield return Frames(12);
            GpuSkinnedCharacter knight = units[0].GetComponentInChildren<GpuSkinnedCharacter>();
            Assert.IsFalse(knight.IsFar, "close up: the Animator again");
            Assert.IsTrue(units[0].GetComponentInChildren<Animator>().enabled);
            Assert.IsTrue(units[0].GetComponentInChildren<SkinnedMeshRenderer>().enabled);
        }

        /// <summary>
        /// Every bone a body's points hang on (with any weight) is one of its skeleton's: the GPU skin reads bone b at columns
        /// 3b..3b+2 of the bake, and past the last bone lie the anchor's matrix and its inverse (the rig's unit scale, ×100):
        /// a point given a bone the skeleton has not is thrown across the sky. Each look of a man with each kind of gear.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryBodyWithAnyGear_HangsOnlyOnItsOwnBones()
        {
            var problems = new System.Text.StringBuilder();
            var types = new[] { HumanoidModelBuilder.HumanType.Villager, HumanoidModelBuilder.HumanType.Soldier, HumanoidModelBuilder.HumanType.KnightGuard,
                                HumanoidModelBuilder.HumanType.General, HumanoidModelBuilder.HumanType.Bandit };
            var kits = new[]
            {
                new Item[0],
                new[] { new Item(ItemType.Axe, 1) }, new[] { new Item(ItemType.Pickaxe, 2) }, new[] { new Item(ItemType.Sword, 3), new Item(ItemType.Shield, 2) },
                new[] { new Item(ItemType.Bow, 2) }, new[] { new Item(ItemType.Staff, 2) },
                new[] { new Item(ItemType.Helmet, 3), new Item(ItemType.Armor, 3), new Item(ItemType.Gloves, 3), new Item(ItemType.Boots, 3), new Item(ItemType.Sword, 2) },
            };
            var weights = new List<Vector4>();
            var bones = new List<Vector4>();
            foreach (HumanoidModelBuilder.HumanType type in types)
                foreach (Item[] kit in kits)
                {
                    GameObject go = Unit(type, new Vector3(0f, -300f, 0f));
                    Equipment gear = go.GetComponent<Equipment>() ?? go.AddComponent<Equipment>();
                    foreach (Item item in kit) gear.Equip(item);
                    yield return null;
                    foreach (SkinnedMeshRenderer body in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var meshes = new List<Mesh> { body.sharedMesh };
                        SkinnedMeshLod lod = body.GetComponentInParent<SkinnedMeshLod>();
                        if (lod != null) { meshes.Add(lod.Near); meshes.Add(lod.Far); }
                        foreach (Mesh source in meshes)
                        {
                            if (source == null) continue;
                            Mesh skin = GpuSkinBake.SkinMesh(source);
                            if (skin == null) continue;
                            foreach (Mesh m in new[] { skin, GpuSkinBake.LowMesh(skin) })
                            {
                                if (m == null) continue;
                                m.GetUVs(4, weights); m.GetUVs(5, bones);
                                int bad = 0, worst = -1, unweighted = 0;
                                float worstSum = 1f;
                                for (int i = 0; i < bones.Count; i++)
                                {
                                    float sum = 0f;
                                    for (int k = 0; k < 4; k++)
                                    {
                                        sum += weights[i][k];
                                        if (weights[i][k] > 0f && (bones[i][k] < 0f || (int)bones[i][k] >= body.bones.Length)) { bad++; worst = Mathf.Max(worst, (int)bones[i][k]); }
                                    }
                                    if (Mathf.Abs(sum - 1f) > 0.02f) { unweighted++; if (Mathf.Abs(sum - 1f) > Mathf.Abs(worstSum - 1f)) worstSum = sum; }
                                }
                                if (bad > 0) problems.AppendLine($"{type} with [{string.Join(", ", kit)}]: {m.name}: {bad} weights on bones up to {worst}, the skeleton has {body.bones.Length}");
                                if (unweighted > 0) problems.AppendLine($"{type} with [{string.Join(", ", kit)}]: {m.name}: {unweighted} of {bones.Count} points weigh not 1 in all (one {worstSum:0.###})");
                            }
                        }
                    }
                    UnityEngine.Object.Destroy(go);
                    yield return null;
                }
            Assert.AreEqual("", problems.ToString());
        }

        /// <summary>
        /// A unit drawn far off whose model is swapped (a promotion, a bow on a knight) leaves nothing behind in the batch:
        /// the crowd's job stops writing it into its old place, and no character plays clips outside its own bake. Once the
        /// job went on writing a promoted unit into the freed place, and the next character given it stood stretched across
        /// the sky where that unit was (a tester's shot, 0.3.10).
        /// </summary>
        [UnityTest]
        public IEnumerator AModelSwappedFarOff_LeavesNothingBehindInTheBatch()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            spawned.Add(ground);
            var units = new List<GameObject>();
            for (int i = 0; i < 3; i++)
            {
                // One the crowd moves (an AI unit) and one followed by its transform (a player's unit), side by side
                GameObject mob = HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "CrowdUnit_" + i);
                mob.transform.position = new Vector3(i * 0.7f - 1f, 0.05f, 0f);
                mob.AddComponent<Equipment>();
                UnitProgress.Setup(mob, UnitRank.Person);
                mob.AddComponent<RankModel>();
                spawned.Add(mob);
                units.Add(mob);

                GameObject loose = Unit(HumanoidModelBuilder.HumanType.Villager, new Vector3(i * 0.7f - 0.6f, 0f, 0.5f));
                loose.name = "LooseUnit_" + i;
                loose.AddComponent<Equipment>();
                UnitProgress.Setup(loose, UnitRank.Person);
                loose.AddComponent<RankModel>();
                units.Add(loose);
            }

            cam.transform.position = new Vector3(0f, 1.5f, -45f);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            yield return Frames(12);
            foreach (GameObject u in units) Assert.IsTrue(u.GetComponentInChildren<GpuSkinnedCharacter>().DrawnOnGpu, $"{u.name}: in the batch 45 m away");
            Assert.AreEqual("", GpuSkinnedCharacter.AuditBatch(), "before any swap");

            // Up to the edge: still far by the slack, while a body made there starts near (the Animator's)
            float d = GpuSkinnedCharacter.ViewScale(cam, out _) / (GpuSkinnedCharacter.FarScreenHeight * 1.07f);
            cam.transform.position = new Vector3(0f, 1.5f, -d);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            yield return Frames(4);

            foreach (GameObject u in units) u.GetComponent<UnitProgress>().Promote(); // a new model for every one
            yield return Frames(3);
            Assert.AreEqual("", GpuSkinnedCharacter.AuditBatch(), "after the promotions");
            foreach (GameObject u in units)
            {
                Equipment e = u.GetComponent<Equipment>();
                e.Equip(new Item(ItemType.Bow, 2)); // (a knight's model is sculpted with its bow: swapped again)
                u.GetComponent<UnitProgress>().Promote();
            }
            yield return Frames(3);
            Assert.AreEqual("", GpuSkinnedCharacter.AuditBatch(), "after a bow and another promotion");

            // Back far: everyone in the batch again, every place its own
            cam.transform.position = new Vector3(0f, 1.5f, -45f);
            cam.transform.LookAt(new Vector3(0f, 1f, 0f));
            yield return Frames(30);
            Assert.AreEqual("", GpuSkinnedCharacter.AuditBatch(), "far again");
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

            // "work": a builder's hammer, a swing every 0.8 s, shot in the middle of the third
            string[] poses = { "idle", "walk", "run", "attack", "work", "death" };
            var types = new[] { HumanoidModelBuilder.HumanType.KnightGuard, HumanoidModelBuilder.HumanType.Bandit, HumanoidModelBuilder.HumanType.Villager };
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
                        if (poses[p] == "work")
                            for (int swing = 0; swing < 3; swing++)
                            {
                                go.GetComponent<CombatUnit>().TriggerAttackAnimation();
                                for (float end = Time.time + (swing < 2 ? 0.8f : 0.3f); Time.time < end;) { cam.Render(); yield return null; }
                            }
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
