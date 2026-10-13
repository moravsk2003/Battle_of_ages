using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Large-battle stress tool (not a regular test): 1000 units at once (500 raiders patrolling + 500 knights),
    /// a 100 vs 100 battle and a 500 vs 500 battle, with the player watching from nearby and the main camera rendered
    /// at 1920x1080 every frame. Reports frame times, hitches, spawn cost, per-system times and a per-script ablation.
    /// Writes stress_report.md to CAPTURE_DIR. STRESS_SCALE (default 1) scales the unit counts.
    /// </summary>
    [Explicit("Stress tool, run on demand")]
    public class StressTest
    {
        private ProfilerRecorder mainThread, gcAlloc, batches, behaviour, lateBehaviour, physics, animators, charCtrl;
        private readonly StringBuilder report = new StringBuilder();
        private Camera cam;
        private RenderTexture rt;
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<HealthComponent> mobs = new List<HealthComponent>(), knights = new List<HealthComponent>();
        private Vector3 arena;

        [UnityTest]
        public IEnumerator StressBattles()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            shotDir = dir;
            float scale = float.TryParse(Environment.GetEnvironmentVariable("STRESS_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sc) ? sc : 1f;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(1f);
            LogAssert.ignoreFailingMessages = true;

            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 15);
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 15);
            behaviour = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BehaviourUpdate", 15);
            lateBehaviour = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LateBehaviourUpdate", 15);
            physics = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate", 15);
            animators = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animators.Update", 15);
            charCtrl = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "CharacterController.Move", 15);

            cam = Camera.main;
            rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            cam.enabled = false;                    // rendered once per frame by hand, never twice
            FootIK.LodViewerOverride = cam.transform; // Camera.main is null while the camera is disabled

            report.AppendLine("# Battle of Ages — stress test (large battles)");
            report.AppendLine($"{SystemInfo.processorType}, {SystemInfo.graphicsDeviceName}, render 1920x1080 every frame, scale {scale}. Recorder CharacterController.Move valid: {charCtrl.Valid}");
            report.AppendLine();
            report.AppendLine("| Scenario | units alive | frames | avg FPS | avg ms | p95 | p99 | max | main | render | scripts | late scripts | physics | char ctrl | animators | GC KB/f | batches |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            // Arena: open plateau east of the base; the player stands at its edge looking in.
            arena = new Vector3(150f, 0f, -40f);
            arena.y = InfiniteWorldManager.GetSampledTerrainHeight(arena.x, arena.z);
            GameObject player = GameObject.Find("Player");
            player.GetComponent<HealthComponent>()?.InitializeHealth(100000000);
            PlaceViewer(player, arena + new Vector3(-45f, 0f, 0f));
            yield return Wait(3f); // chunks around the arena

            // ---- 1. 1000 units: two separate crowds (no fighting), raiders patrol, knights idle
            int half = Mathf.RoundToInt(500 * scale);
            float t0 = Time.realtimeSinceStartup;
            yield return SpawnMobs(half, arena + new Vector3(0f, 0f, 40f), 70f, patrol: true);
            float mobSpawn = (Time.realtimeSinceStartup - t0) * 1000f;
            t0 = Time.realtimeSinceStartup;
            yield return SpawnKnights(half, arena + new Vector3(0f, 0f, -60f), 70f);
            float knightSpawn = (Time.realtimeSinceStartup - t0) * 1000f;
            yield return Wait(2f);
            yield return Phase($"{half * 2} units, patrol / idle", 8f, null);
            report.AppendLine();
            report.AppendLine($"Spawn: {half} raiders in {mobSpawn:0} ms ({mobSpawn / half:0.00} ms each), {half} knights in {knightSpawn:0} ms ({knightSpawn / half:0.00} ms each).");
            Renderer[] unitRenderers = spawned.Take(5).SelectMany(g => g.GetComponentsInChildren<Renderer>()).ToArray();
            report.AppendLine($"Per raider: {spawned[0].GetComponentsInChildren<Renderer>().Length} renderers, {spawned[0].GetComponentsInChildren<Collider>().Length} colliders, {spawned[0].GetComponentsInChildren<MonoBehaviour>().Length} scripts, {spawned[0].GetComponentsInChildren<Animator>().Length} animators.");
            SkinnedMeshRenderer body = spawned[spawned.Count - 1].GetComponentInChildren<SkinnedMeshRenderer>();
            report.AppendLine($"Per knight: {spawned[spawned.Count - 1].GetComponentsInChildren<Renderer>().Length} renderers, body submeshes {body.sharedMesh.subMeshCount}, body vertices {body.sharedMesh.vertexCount}.");
            report.AppendLine();
            yield return TopMarkers(30);
            yield return Ablation();
            ClearUnits();
            yield return Wait(2f);

            // ---- 2. 100 vs 100 battle
            int small = Mathf.RoundToInt(100 * scale);
            yield return SpawnMobs(small, arena + new Vector3(0f, 0f, 14f), 22f, patrol: false);
            yield return SpawnKnights(small, arena + new Vector3(0f, 0f, -14f), 22f);
            yield return Battle($"battle {small} vs {small}", 20f);
            ClearUnits();
            yield return Wait(2f);

            // ---- 3. 500 vs 500 battle
            yield return SpawnMobs(half, arena + new Vector3(0f, 0f, 22f), 50f, patrol: false);
            yield return SpawnKnights(half, arena + new Vector3(0f, 0f, -22f), 50f);
            yield return Battle($"battle {half} vs {half}", 25f);

            report.AppendLine();
            report.AppendLine("Hitches > 50 ms:");
            foreach (string h in hitchLog) report.AppendLine(h);
            if (hitchLog.Count == 0) report.AppendLine("- none");

            File.WriteAllText(Path.Combine(dir, "stress_report.md"), report.ToString());
            Debug.Log("[Stress] report\n" + report);
            ClearUnits();
            cam.targetTexture = null;
            cam.enabled = true;
            FootIK.LodViewerOverride = null;
            rt.Release();
            foreach (ProfilerRecorder r in new[] { mainThread, gcAlloc, batches, behaviour, lateBehaviour, physics, animators, charCtrl }) r.Dispose();
        }

        private static void PlaceViewer(GameObject player, Vector3 pos)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            pos.y = InfiniteWorldManager.GetSampledTerrainHeight(pos.x, pos.z) + 1f;
            cc.enabled = false;
            player.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.right));
            cc.enabled = true;
        }

        private IEnumerator SpawnMobs(int count, Vector3 center, float size, bool patrol)
        {
            int perFrame = 50;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Grid(center, size, i, count);
                HostileMobType type = i % 7 == 0 ? HostileMobType.Goblin : HostileMobType.Bandit;
                GameObject mob = HostileCampBuilder.CreateHostileMob(type, "StressMob_" + i);
                mob.transform.position = p;
                HostileMobAI ai = mob.GetComponent<HostileMobAI>();
                ai.SetGuardOrigin(p);
                spawned.Add(mob);
                mobs.Add(mob.GetComponent<HealthComponent>());
                if (i % perFrame == perFrame - 1) yield return null;
            }
            yield return null;
        }

        private IEnumerator SpawnKnights(int count, Vector3 center, float size)
        {
            int perFrame = 50;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = Grid(center, size, i, count);
                GameObject go = new GameObject("StressKnight_" + i);
                go.transform.position = p;
                CharacterController cc = go.AddComponent<CharacterController>();
                cc.height = 2f; cc.center = new Vector3(0f, 1f, 0f); cc.radius = 0.45f;
                go.AddComponent<HealthComponent>().InitializeHealth(150);
                go.AddComponent<CombatUnit>().InitializeCombatStats(18, 1.6f, 2.5f);
                go.AddComponent<RtsUnit>();
                GameObject model = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.KnightGuard, "VisualModel");
                model.transform.SetParent(go.transform, false);
                spawned.Add(go);
                knights.Add(go.GetComponent<HealthComponent>());
                if (i % perFrame == perFrame - 1) yield return null;
            }
            yield return null;
        }

        private static Vector3 Grid(Vector3 center, float size, int i, int count)
        {
            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            float step = size / Mathf.Max(1, side - 1);
            Vector3 p = center + new Vector3((i % side) * step - size * 0.5f, 0f, (i / side) * step * 0.6f - size * 0.3f);
            p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
            return p;
        }

        /// <summary>Knights get attack orders on the nearest raider (like a player's army command), refreshed every second.</summary>
        private IEnumerator Battle(string name, float seconds)
        {
            float commandTimer = 0f, elapsed = 0f;
            int shot = 0;
            yield return Phase(name, seconds, dt =>
            {
                elapsed += dt;
                if (shot < 2 && elapsed > (shot == 0 ? 5f : 16f)) SaveShot($"{name.Replace(' ', '_')}_{(shot++ == 0 ? "clash" : "aftermath")}");
                commandTimer -= dt;
                if (commandTimer > 0f) return;
                commandTimer = 1f;
                var aliveMobs = mobs.Where(m => m != null && m.IsAlive).ToList();
                if (aliveMobs.Count == 0) return;
                foreach (HealthComponent k in knights)
                {
                    if (k == null || !k.IsAlive) continue;
                    HealthComponent best = null; float bestD = float.MaxValue;
                    Vector3 kp = k.transform.position;
                    foreach (HealthComponent m in aliveMobs)
                    {
                        float d = (m.transform.position - kp).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = m; }
                    }
                    k.GetComponent<RtsUnit>().IssueAttackCommand(best);
                }
            });
            report.AppendLine($"  ↳ after {name}: raiders alive {mobs.Count(m => m != null && m.IsAlive)}/{mobs.Count}, knights alive {knights.Count(k => k != null && k.IsAlive)}/{knights.Count}");
        }

        private string shotDir;

        /// <summary>Saves the last rendered frame (the main camera's 1920x1080 target) as a PNG.</summary>
        private void SaveShot(string fileName)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(shotDir, fileName + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        private void ClearUnits()
        {
            foreach (GameObject g in spawned) if (g != null) UnityEngine.Object.Destroy(g);
            spawned.Clear(); mobs.Clear(); knights.Clear();
        }

        private IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { cam.Render(); yield return null; }
        }

        private readonly List<string> hitchLog = new List<string>();

        private IEnumerator Phase(string name, float seconds, Action<float> step)
        {
            var frame = new List<float>(); var main = new List<float>(); var render = new List<float>(); var scripts = new List<float>(); var late = new List<float>();
            var phys = new List<float>(); var cc = new List<float>(); var anim = new List<float>(); var gc = new List<float>(); var bat = new List<float>();
            yield return null;
            float last = Time.realtimeSinceStartup, end = last + seconds;
            int gcCount = GC.CollectionCount(0);
            while (Time.realtimeSinceStartup < end)
            {
                step?.Invoke(Time.deltaTime);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                cam.Render();
                float r = (float)sw.Elapsed.TotalMilliseconds;
                yield return null;
                float now = Time.realtimeSinceStartup;
                float ms = (now - last) * 1000f; last = now;
                frame.Add(ms); render.Add(r); main.Add(mainThread.LastValue * 1e-6f);
                scripts.Add(behaviour.LastValue * 1e-6f); late.Add(lateBehaviour.LastValue * 1e-6f);
                phys.Add(physics.LastValue * 1e-6f); cc.Add(charCtrl.LastValue * 1e-6f); anim.Add(animators.LastValue * 1e-6f);
                gc.Add(gcAlloc.LastValue / 1024f); bat.Add(batches.LastValue);
                int gcNow = GC.CollectionCount(0);
                if (ms > 50f) hitchLog.Add($"- {name}: {ms:0} ms{(gcNow != gcCount ? " [GC]" : "")} (scripts {scripts[scripts.Count - 1]:0}, physics {phys[phys.Count - 1]:0}, render {r:0})");
                gcCount = gcNow;
            }
            var sorted = frame.OrderBy(x => x).ToList();
            float P(float q) => sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
            int alive = mobs.Count(m => m != null && m.IsAlive) + knights.Count(k => k != null && k.IsAlive);
            report.AppendLine($"| {name} | {alive} | {frame.Count} | {1000f / frame.Average():0} | {frame.Average():0.0} | {P(0.95f):0.0} | {P(0.99f):0.0} | {sorted[sorted.Count - 1]:0} | " +
                              $"{main.Average():0.0} | {render.Average():0.0} | {scripts.Average():0.0} | {late.Average():0.0} | {phys.Average():0.0} | {cc.Average():0.0} | {anim.Average():0.0} | {gc.Average():0} | {bat.Average():0} |");
        }

        /// <summary>Top profiler markers (main thread time) over a few frames: shows where unattributed time goes.</summary>
        private IEnumerator TopMarkers(int frames)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var recs = new List<(string name, ProfilerRecorder rec)>();
            foreach (ProfilerRecorderHandle h in handles)
            {
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                recs.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 1)));
            }
            var sums = new double[recs.Count];
            for (int f = 0; f < frames; f++)
            {
                cam.Render();
                yield return null;
                for (int i = 0; i < recs.Count; i++) sums[i] += recs[i].rec.LastValue * 1e-6;
            }
            report.AppendLine();
            report.AppendLine("Top profiler markers with 1000 units (ms per frame, nested markers overlap):");
            foreach (int i in Enumerable.Range(0, recs.Count).OrderByDescending(i => sums[i]).Take(40))
                report.AppendLine($"- {recs[i].name}: {sums[i] / frames:0.00}");
            foreach (var r in recs) r.rec.Dispose();
        }

        /// <summary>Cost of each script type on the 1000-unit scene: Update-loop time with the type enabled vs disabled.</summary>
        private IEnumerator Ablation()
        {
            report.AppendLine("Per-system cost with 1000 units (enabled vs disabled; frame ms and script ms):");
            report.AppendLine("| System | instances | frame ms saved | scripts ms saved |");
            report.AppendLine("|---|---|---|---|");
            var groups = spawned.Where(g => g != null).SelectMany(g => g.GetComponentsInChildren<Behaviour>())
                .Where(b => b.enabled && !(b is Transform))
                .GroupBy(b => b.GetType()).OrderByDescending(g => g.Count()).Take(12).ToList();
            foreach (var g in groups)
            {
                float f1 = 0, s1 = 0, f0 = 0, s0 = 0;
                yield return Measure(45, (f, s) => { f1 = f; s1 = s; });
                foreach (Behaviour b in g) if (b != null) b.enabled = false;
                yield return Measure(45, (f, s) => { f0 = f; s0 = s; });
                foreach (Behaviour b in g) if (b != null) b.enabled = true;
                report.AppendLine($"| {g.Key.Name} | {g.Count()} | {f1 - f0:0.00} | {s1 - s0:0.00} |");
            }
            // Renderers: all unit renderers off
            var rends = spawned.Where(g => g != null).SelectMany(g => g.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToList();
            float fr1 = 0, fr0 = 0, x = 0;
            yield return Measure(45, (f, s) => fr1 = f);
            foreach (Renderer r in rends) r.enabled = false;
            yield return Measure(45, (f, s) => fr0 = f);
            foreach (Renderer r in rends) r.enabled = true;
            report.AppendLine($"| (all unit renderers) | {rends.Count} | {fr1 - fr0:0.00} | - |");
            float fs1 = 0, fs0 = 0;
            var modes = rends.Select(r => r.shadowCastingMode).ToList();
            yield return Measure(45, (f, s) => fs1 = f);
            foreach (Renderer r in rends) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            yield return Measure(45, (f, s) => fs0 = f);
            for (int i = 0; i < rends.Count; i++) rends[i].shadowCastingMode = modes[i];
            report.AppendLine($"| (unit shadows) | {rends.Count} | {fs1 - fs0:0.00} | - |");

            // Wildlife around the arena (spawned by the world chunks): renderers and AI off
            var animals = UnityEngine.Object.FindObjectsByType<PeacefulAnimalAI>(FindObjectsSortMode.None);
            var animalRends = animals.SelectMany(a => a.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToList();
            float fa1 = 0, fa0 = 0;
            yield return Measure(45, (f, s) => fa1 = f);
            foreach (Renderer r in animalRends) r.enabled = false;
            foreach (PeacefulAnimalAI a in animals) a.enabled = false;
            yield return Measure(45, (f, s) => fa0 = f);
            foreach (Renderer r in animalRends) r.enabled = true;
            foreach (PeacefulAnimalAI a in animals) a.enabled = true;
            string counts = string.Join(", ", animals.GroupBy(a => a.Type).Select(g => $"{g.Count()} {g.Key}"));
            report.AppendLine($"| (wildlife: {counts}) | {animals.Length} | {fa1 - fa0:0.00} | - |");
        }

        private IEnumerator Measure(int frames, Action<float, float> result)
        {
            yield return null; yield return null;
            var f = new List<float>(); var s = new List<float>();
            float last = Time.realtimeSinceStartup;
            for (int i = 0; i < frames; i++)
            {
                cam.Render();
                yield return null;
                float now = Time.realtimeSinceStartup;
                f.Add((now - last) * 1000f); last = now;
                s.Add((behaviour.LastValue + lateBehaviour.LastValue) * 1e-6f);
            }
            f.Sort(); s.Sort();
            result(f[f.Count / 2], s[s.Count / 2]); // medians
        }
    }
}
