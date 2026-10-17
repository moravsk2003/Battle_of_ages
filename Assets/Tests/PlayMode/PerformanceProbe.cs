using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Performance review tool (not a regular test). Plays SampleScene through gameplay phases (idle at the base,
    /// travelling across the world while chunks stream, near a raider camp) rendering the main camera at 1920x1080
    /// every frame, and records frame time, main-thread time, GC allocations, draw calls / set-pass calls / triangles
    /// and hitches. Then measures each script type's share of the Update loops by disabling it for a while (ablation).
    /// Writes perf_report.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Performance review tool, run on demand")]
    public class PerformanceProbe
    {
        private sealed class Sample
        {
            public float FrameMs, MainMs, RenderMs;
            public long GcBytes, Batches, SetPass, Tris;
        }

        private ProfilerRecorder mainThread, gcAlloc, batches, setPass, tris, behaviourUpdate, lateBehaviour, fixedBehaviour, physics, animators;
        private readonly StringBuilder report = new StringBuilder();
        private RenderTexture rt;
        private Camera cam;

        [UnityTest]
        public IEnumerator ProfileGameplay()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);

            LogAssert.ignoreFailingMessages = true; // (a known SkinnedMeshRenderer error must not abort the review)
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);

            mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 15);
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 15);
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 15);
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 15);
            behaviourUpdate = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "BehaviourUpdate", 15);
            lateBehaviour = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "LateBehaviourUpdate", 15);
            fixedBehaviour = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "FixedBehaviourUpdate", 15);
            physics = ProfilerRecorder.StartNew(ProfilerCategory.Physics, "Physics.Simulate", 15);
            animators = ProfilerRecorder.StartNew(ProfilerCategory.Animation, "Animators.Update", 15);

            cam = Camera.main;
            rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;

            report.AppendLine("# Battle of Ages — performance probe");
            report.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.processorType} ({SystemInfo.processorCount} threads), {SystemInfo.graphicsDeviceName}, quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', render 1920x1080 each frame.");
            report.AppendLine($"Recorders valid: main {mainThread.Valid}, gc {gcAlloc.Valid}, batches {batches.Valid}, behaviour {behaviourUpdate.Valid}, physics {physics.Valid}, animators {animators.Valid}");
            report.AppendLine();
            report.AppendLine("| Phase | frames | avg FPS | avg ms | p95 ms | p99 ms | max ms | main ms | render ms | scripts ms | physics ms | anim ms | GC KB/frame (max) | batches | setpass | tris (k) |");
            report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

            GameObject player = GameObject.Find("Player");
            CharacterController cc = player.GetComponent<CharacterController>();
            player.GetComponent<HealthComponent>()?.InitializeHealth(1000000);

            // Phase 1: the first seconds (start-up streaming), then idle at the base
            yield return Phase("startup (first 3 s)", 3f, null);
            yield return Phase("base idle", 6f, null);

            // Phase 2: travel across the world at 14 m/s (sprint-ish) while chunks stream in
            Vector3 dir2 = new Vector3(1f, 0f, 0.6f).normalized;
            yield return Phase("travel 14 m/s (chunk streaming)", 18f, dt =>
            {
                Vector3 p = player.transform.position + dir2 * 14f * dt;
                p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z) + 1f;
                cc.enabled = false; player.transform.position = p; cc.enabled = true;
                player.transform.rotation = Quaternion.LookRotation(dir2);
            });

            // Phase 3: near a raider camp (mobs patrol / attack)
            Vector3 camp = new Vector3(-416f, 0f, 416f);
            Vector3 approach = camp + new Vector3(18f, 0f, -18f);
            approach.y = InfiniteWorldManager.GetSampledTerrainHeight(approach.x, approach.z) + 1f;
            cc.enabled = false; player.transform.position = approach; cc.enabled = true;
            yield return Phase("teleport + camp load", 3f, null);
            foreach (HostileSettlementLeader l in UnityEngine.Object.FindObjectsByType<HostileSettlementLeader>(FindObjectsSortMode.None)) camp = l.transform.position;
            player.transform.rotation = Quaternion.LookRotation((camp - approach).WithY(0f).normalized);
            yield return Phase("raider camp (combat)", 8f, null);

            report.AppendLine();
            AppendSceneCounts();
            yield return RenderCensus();
            report.AppendLine();
            report.AppendLine($"Memory: total used {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576} MB, mono heap {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576} MB, gfx driver {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576} MB");

            // Ablation: back at the base; per script type, average Update-loop time with the type on vs off.
            cc.enabled = false; player.transform.position = new Vector3(0f, InfiniteWorldManager.GetSampledTerrainHeight(0f, 0f) + 1f, 0f); cc.enabled = true;
            yield return Wait(2f);
            yield return Ablation(player);

            File.WriteAllText(Path.Combine(dir, "perf_report.md"), report.ToString());
            Debug.Log("[Perf] report written\n" + report);
            cam.targetTexture = null;
            rt.Release();
            foreach (ProfilerRecorder r in new[] { mainThread, gcAlloc, batches, setPass, tris, behaviourUpdate, lateBehaviour, fixedBehaviour, physics, animators }) r.Dispose();
        }

        private IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { RenderFrame(); yield return null; }
        }

        private float RenderFrame()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cam.Render();
            return (float)sw.Elapsed.TotalMilliseconds;
        }

        private IEnumerator Phase(string name, float seconds, Action<float> step)
        {
            var samples = new List<Sample>();
            var hitches = new List<string>();
            float end = Time.realtimeSinceStartup + seconds;
            float last = Time.realtimeSinceStartup;
            float renderMs = 0f;
            var scriptMs = new List<float>(); var physMs = new List<float>(); var animMs = new List<float>();
            yield return null;
            last = Time.realtimeSinceStartup;
            int gcCount = System.GC.CollectionCount(0);
            while (Time.realtimeSinceStartup < end)
            {
                step?.Invoke(Time.deltaTime);
                renderMs = RenderFrame();
                yield return null;
                float now = Time.realtimeSinceStartup;
                var s = new Sample
                {
                    FrameMs = (now - last) * 1000f, MainMs = mainThread.LastValue * 1e-6f, RenderMs = renderMs,
                    GcBytes = gcAlloc.LastValue, Batches = batches.LastValue, SetPass = setPass.LastValue, Tris = tris.LastValue
                };
                scriptMs.Add((behaviourUpdate.LastValue + lateBehaviour.LastValue + fixedBehaviour.LastValue) * 1e-6f);
                physMs.Add(physics.LastValue * 1e-6f);
                animMs.Add(animators.LastValue * 1e-6f);
                last = now;
                samples.Add(s);
                int gcNow = System.GC.CollectionCount(0);
                bool collected = gcNow != gcCount; gcCount = gcNow;
                if (s.FrameMs > 50f) hitches.Add($"{s.FrameMs:0} ms{(collected ? " [GC collection]" : "")} (main {s.MainMs:0}, render {s.RenderMs:0}, scripts {scriptMs[scriptMs.Count - 1]:0}, GC {s.GcBytes / 1024} KB)");
            }

            var ms = samples.Select(x => x.FrameMs).OrderBy(x => x).ToList();
            float P(float q) => ms[Mathf.Clamp(Mathf.RoundToInt(q * (ms.Count - 1)), 0, ms.Count - 1)];
            float avg = ms.Average();
            report.AppendLine($"| {name} | {samples.Count} | {1000f / avg:0} | {avg:0.0} | {P(0.95f):0.0} | {P(0.99f):0.0} | {ms[ms.Count - 1]:0.0} | " +
                              $"{samples.Average(x => x.MainMs):0.0} | {samples.Average(x => x.RenderMs):0.0} | {scriptMs.Average():0.00} | {physMs.Average():0.00} | {animMs.Average():0.00} | " +
                              $"{samples.Average(x => x.GcBytes) / 1024f:0.0} ({samples.Max(x => x.GcBytes) / 1024} ) | {samples.Average(x => x.Batches):0} | {samples.Average(x => x.SetPass):0} | {samples.Average(x => x.Tris) / 1000f:0} |");
            if (hitches.Count > 0) pendingHitches.Add($"- **{name}**: {hitches.Count} hitches > 50 ms: " + string.Join("; ", hitches.Take(8)));
            if (name.StartsWith("raider")) { report.AppendLine(); report.AppendLine("Hitches:"); foreach (string h in pendingHitches) report.AppendLine(h); if (pendingHitches.Count == 0) report.AppendLine("- none"); }
        }

        private readonly List<string> pendingHitches = new List<string>();

        private void AppendSceneCounts()
        {
            int Count<T>() where T : UnityEngine.Object => UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None).Length;
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            report.AppendLine("Scene at the camp: " +
                $"GameObjects {Count<Transform>()}, renderers {renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy)} " +
                $"(skinned {renderers.Count(r => r is SkinnedMeshRenderer && r.enabled)}, shadow casting {renderers.Count(r => r.enabled && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)}), " +
                $"lights {lights.Length} (shadowed {lights.Count(l => l.shadows != LightShadows.None && l.enabled)}), colliders {Count<Collider>()}, " +
                $"animators {Count<Animator>()}, particle systems {Count<ParticleSystem>()}, AI controllers {Count<AIBaseController>()}, " +
                $"MonoBehaviours {Count<MonoBehaviour>()}");
        }

        /// <summary>Renderers grouped by their top-level object (digits stripped); batches and render time saved when hidden.</summary>
        private IEnumerator RenderCensus()
        {
            string Group(Renderer r)
            {
                Transform t = r.transform;
                while (t.parent != null && !t.parent.name.StartsWith("Chunk_") && !t.parent.name.StartsWith("[")) t = t.parent;
                string n = t.name;
                int cut = n.IndexOfAny("0123456789(".ToCharArray());
                n = cut > 0 ? n.Substring(0, cut).TrimEnd('_', ' ') : n;
                return (t.parent != null && t.parent.name.StartsWith("Chunk_") ? "chunk/" : "") + n;
            }
            var groups = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is ParticleSystemRenderer))
                .GroupBy(Group).OrderByDescending(g => g.Count()).Take(14).ToList();

            report.AppendLine();
            report.AppendLine("## Renderers by group (camp view; savings when the group is hidden)");
            report.AppendLine("| Group | renderers | shadow casters | tris (k) | batches saved | render ms saved |");
            report.AppendLine("|---|---|---|---|---|---|");
            foreach (var g in groups)
            {
                float b0 = 0, r0 = 0, b1 = 0, r1 = 0;
                yield return MeasureRender(20, (b, r) => { b0 = b; r0 = r; });
                foreach (Renderer r in g) r.enabled = false;
                yield return MeasureRender(20, (b, r) => { b1 = b; r1 = r; });
                foreach (Renderer r in g) r.enabled = true;
                long triCount = 0;
                foreach (Renderer r in g)
                {
                    Mesh m = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (m != null && m.isReadable) for (int s = 0; s < m.subMeshCount; s++) triCount += (long)m.GetIndexCount(s) / 3;
                }
                report.AppendLine($"| {g.Key} | {g.Count()} | {g.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)} | {triCount / 1000} | {b0 - b1:0} | {r0 - r1:0.00} |");
            }
            UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (urp != null)
                report.AppendLine($"\nURP: shadow distance {urp.shadowDistance} m, cascades {urp.shadowCascadeCount}, main shadow res {urp.mainLightShadowmapResolution}, SRP batcher {urp.useSRPBatcher}, MSAA {urp.msaaSampleCount}, render scale {urp.renderScale}; camera far clip {cam.farClipPlane} m");
        }

        private IEnumerator MeasureRender(int frames, Action<float, float> result)
        {
            yield return null;
            var b = new List<float>(); var r = new List<float>();
            for (int i = 0; i < frames; i++)
            {
                float ms = RenderFrame();
                yield return null;
                b.Add(batches.LastValue); r.Add(ms);
            }
            r.Sort();
            result(b.Average(), r.Take(frames * 3 / 4).Average());
        }

        private IEnumerator Ablation(GameObject player)
        {
            var byType = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(b => b.enabled && b.isActiveAndEnabled && b.gameObject != player && !b.transform.IsChildOf(player.transform))
                .Where(b => !(b is BattleOfAges.World.InfiniteWorldManager) && b.GetType().Namespace != null && b.GetType().Namespace.StartsWith("BattleOfAges"))
                .GroupBy(b => b.GetType())
                .OrderByDescending(g => g.Count()).Take(18).ToList();

            report.AppendLine();
            report.AppendLine("## Script cost by type (Update + LateUpdate + FixedUpdate, ablation at the base)");
            report.AppendLine("| Type | instances | cost ms/frame | per instance µs | GC bytes/frame |");
            report.AppendLine("|---|---|---|---|---|");
            var rows = new List<(string, int, float, float)>();
            foreach (var group in byType)
            {
                float on1 = 0f, off = 0f, on2 = 0f;
                yield return MeasureScripts(40, v => on1 = v);
                float gcOn1 = lastGc;
                foreach (MonoBehaviour b in group) if (b != null) b.enabled = false;
                yield return MeasureScripts(40, v => off = v);
                float gcOff = lastGc;
                foreach (MonoBehaviour b in group) if (b != null) b.enabled = true;
                yield return MeasureScripts(40, v => on2 = v);
                float cost = (on1 + on2) * 0.5f - off;
                rows.Add((group.Key.Name, group.Count(), cost, (gcOn1 + lastGc) * 0.5f - gcOff));
            }
            foreach (var r in rows.OrderByDescending(r => r.Item3))
                report.AppendLine($"| {r.Item1} | {r.Item2} | {r.Item3:0.000} | {Mathf.Max(0f, r.Item3) * 1000f / Mathf.Max(1, r.Item2):0.0} | {r.Item4:0} |");
        }

        private float lastGc;

        private IEnumerator MeasureScripts(int frames, Action<float> result)
        {
            yield return null; yield return null;
            var v = new List<float>();
            var gc = new List<float>();
            for (int i = 0; i < frames; i++)
            {
                yield return null;
                gc.Add(gcAlloc.LastValue);
                v.Add((behaviourUpdate.LastValue + lateBehaviour.LastValue + fixedBehaviour.LastValue) * 1e-6f);
            }
            v.Sort(); gc.Sort();
            lastGc = gc[gc.Count / 2]; // median
            result(v.Take(frames * 3 / 4).Average()); // drop the slowest quarter (GC / streaming noise)
        }
    }

    internal static class VectorExtensions
    {
        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
    }
}
