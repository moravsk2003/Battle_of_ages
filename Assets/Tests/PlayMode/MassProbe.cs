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
    /// Performance review tool (not a regular test): ten thousand people and their towns at once. MASS_TOWNS kingdoms
    /// (default 100) of MASS_PEOPLE (100) each under a hall of MASS_HALL (4: houses, fields, mines and the wall's ring),
    /// all on one side so nobody fights, in a square grid round the hero, who stands between the middle four. Measured in
    /// first person and on the map (the main camera at 1920x1080), with the top profiler markers and each script type's
    /// cost. Writes mass_report.md to CAPTURE_DIR. MASS_ABLATE=0 skips the per-script part.
    /// </summary>
    [Explicit("Performance review tool, run on demand")]
    public class MassProbe
    {
        private static int Env(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? v : fallback;

        private readonly StringBuilder report = new StringBuilder();
        private readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
        private ProfilerRecorder main, scripts, late, anim, phys, gc, batches, tris, setPass;

        [UnityTest, Timeout(3600000)]
        public IEnumerator TenThousand()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            int towns = Env("MASS_TOWNS", 100), people = Env("MASS_PEOPLE", 100), hall = Env("MASS_HALL", 4);
            float spacing = Env("MASS_SPACING", 150);

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(1f);
            LogAssert.ignoreFailingMessages = true;

            ProfilerRecorder Rec(ProfilerCategory c, string n) { var r = ProfilerRecorder.StartNew(c, n, 15); recorders.Add(r); return r; }
            main = Rec(ProfilerCategory.Internal, "Main Thread"); scripts = Rec(ProfilerCategory.Scripts, "BehaviourUpdate");
            late = Rec(ProfilerCategory.Scripts, "LateBehaviourUpdate"); anim = Rec(ProfilerCategory.Animation, "Animators.Update");
            phys = Rec(ProfilerCategory.Physics, "Physics.Simulate"); gc = Rec(ProfilerCategory.Memory, "GC Allocated In Frame");
            batches = Rec(ProfilerCategory.Render, "Batches Count"); tris = Rec(ProfilerCategory.Render, "Triangles Count");
            setPass = Rec(ProfilerCategory.Render, "SetPass Calls Count");

            Camera cam = Camera.main;
            var rt = new RenderTexture(1920, 1080, 24);
            cam.targetTexture = rt;
            var made = new List<NpcKingdom>();
            try
            {
                Vector3 centre = new Vector3(1500f, 0f, 1500f);
                GameObject player = GameObject.Find("Player");
                player.GetComponent<HealthComponent>()?.InitializeHealth(100000000);
                CharacterController cc = player.GetComponent<CharacterController>();
                centre.y = InfiniteWorldManager.GetSampledTerrainHeight(centre.x, centre.z) + 1f;
                cc.enabled = false;
                player.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(new Vector3(1f, 0f, 1f)));
                // The land round it: held in the air till there is ground to stand on (a long first frame, shaders being
                // made, and four seconds were three frames: the hero fell through the world and the probe measured the sky)
                for (float waited = 0f; waited < 60f && !Physics.Raycast(centre + Vector3.up * 50f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore); waited += Time.unscaledDeltaTime) yield return null;
                cc.enabled = true;
                yield return new WaitForSecondsRealtime(4f);

                report.AppendLine("# Ten thousand people and their towns");
                report.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.processorType} ({SystemInfo.processorCount} threads), {SystemInfo.graphicsDeviceName}, 1920x1080.");
                report.AppendLine();
                yield return Phase("empty land (base)", 5f);

                int side = Mathf.CeilToInt(Mathf.Sqrt(towns));
                float t0 = Time.realtimeSinceStartup;
                for (int i = 0; i < towns; i++)
                {
                    Vector3 at = centre + new Vector3((i % side - (side - 1) * 0.5f) * spacing, 0f, (i / side - (side - 1) * 0.5f) * spacing);
                    NpcKingdom k = NpcKingdom.Found(at, "Mass" + i, people, 100000, HeroClass.Tank, UnitSpatialGrid.TeamKingdomFirst);
                    k.FirstRaidAt = 1e9f;
                    k.SetHallLevel(hall);
                    if (k.Town != null) k.Town.RaiseAllNow();
                    made.Add(k);
                    if (i % 4 == 3) yield return null;
                }
                float spawnSeconds = Time.realtimeSinceStartup - t0;
                yield return new WaitForSecondsRealtime(5f);

                int units = UnityEngine.Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None).Length;
                int buildings = made.Sum(k => k.Town != null ? Enum.GetValues(typeof(KingdomTown.Kind)).Cast<KingdomTown.Kind>().Sum(kind => k.Town.Standing(kind)) : 0) + made.Count;
                int renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Length;
                int objects = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
                report.AppendLine($"{made.Count} towns, {units} units, {buildings} buildings (walls and piers among them), {renderers} renderers, {objects} transforms; made in {spawnSeconds:0.0} s.");
                report.AppendLine();
                report.AppendLine("| view | avg FPS | avg ms | p95 ms | p99 ms | main ms | scripts ms | anim ms | physics ms | GC KB/frame | batches | set pass | tris (k) |");
                report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");

                yield return Phase("first person, between four towns", 8f);
                Shot(rt, Path.Combine(dir, "mass_first_person.png"));
                Census();
                yield return TopMarkers("first person", 40);
                long g0 = CrowdSim.TotalGlided, h0 = CrowdSim.TotalByHand, t0c = CrowdSim.TotalThinks, p0 = CrowdSim.TotalProbes, f0 = CrowdSim.TotalFrames;
                long[] why0 = (long[])CrowdSim.WhyByHand.Clone();
                long tl0 = CrowdSim.TicksLook, tb0 = CrowdSim.TicksByHand, tt0 = CrowdSim.TicksThink;
                yield return Hierarchy("first person", 12);
                float fr = Mathf.Max(1, CrowdSim.TotalFrames - f0);
                double tickMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency / fr;
                report.AppendLine($"Crowd on the main thread, ms a frame (with the profiler on): looks {(CrowdSim.TicksLook - tl0) * tickMs:0.00}, by the old code {(CrowdSim.TicksByHand - tb0) * tickMs:0.00}, thinking {(CrowdSim.TicksThink - tt0) * tickMs:0.00}.");
                report.AppendLine($"Crowd: {CrowdSim.Count} units; a frame: the job moved {(CrowdSim.TotalGlided - g0) / fr:0}, the old code {(CrowdSim.TotalByHand - h0) / fr:0}, AI turns {(CrowdSim.TotalThinks - t0c) / fr:0}, probes {(CrowdSim.TotalProbes - p0) / fr:0}. By the old code a frame: not gliding {(CrowdSim.WhyByHand[1] - why0[1]) / fr:0}, ground unknown {(CrowdSim.WhyByHand[2] - why0[2]) / fr:0}, in a prop {(CrowdSim.WhyByHand[3] - why0[3]) / fr:0}, ground ahead unknown {(CrowdSim.WhyByHand[4] - why0[4]) / fr:0}; looks told {(CrowdSim.WhyByHand[0] - why0[0]) / fr:0}. Ground: {CrowdSim.GroundKnown}.");
                if (Env("MASS_QUICK", 0) == 1) yield break;

                // Inside a town: its people all round
                Vector3 inTown = made[(side / 2) * side + side / 2].transform.position + new Vector3(14f, 0f, 0f);
                inTown.y = InfiniteWorldManager.GetSampledTerrainHeight(inTown.x, inTown.z) + 1f;
                cc.enabled = false;
                player.transform.SetPositionAndRotation(inTown, Quaternion.LookRotation(Vector3.left));
                cc.enabled = true;
                yield return new WaitForSecondsRealtime(3f);
                yield return Phase("first person, inside a town", 8f); Shot(rt, Path.Combine(dir, "mass_in_town.png")); yield return Hierarchy("inside a town", 12);
                if (Env("MASS_BREAKDOWN", 0) == 1) yield return Breakdown(made);

                if (Env("MASS_ABLATE", 1) == 1) yield return Ablation();

                // The map, zoomed out
                Managers.GameModeManager mode = UnityEngine.Object.FindFirstObjectByType<Managers.GameModeManager>();
                if (mode != null)
                {
                    mode.ToggleMode();
                    yield return new WaitForSecondsRealtime(2f);
                    Camera map = ViewCamera.Current;
                    if (map != null && map != cam)
                    {
                        map.targetTexture = rt;
                        if (map.orthographic) map.orthographicSize = 100f;
                        yield return new WaitForSecondsRealtime(2f);
                        yield return Phase($"map, zoomed out ({(map.orthographic ? "ortho " + map.orthographicSize : "persp")})", 8f);
                        Shot(rt, Path.Combine(dir, "mass_map.png")); yield return TopMarkers("map", 40);
                        map.targetTexture = null;
                    }
                    else report.AppendLine("(no map camera found)");
                }

                File.WriteAllText(Path.Combine(dir, "mass_report.md"), report.ToString());
                Debug.Log("[Mass] report\n" + report);
            }
            finally
            {
                File.WriteAllText(Path.Combine(dir, "mass_report.md"), report.ToString());
                if (cam != null) cam.targetTexture = null;
                rt.Release();
                foreach (ProfilerRecorder r in recorders) r.Dispose();
                NpcKingdom.RemoveAll();
            }
        }

        /// <summary>What the view shows, as a picture (to compare the look before and after a change).</summary>
        private static void Shot(RenderTexture rt, string path)
        {
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = before;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        /// <summary>What is drawn now (the view and the shadows), by mesh: how many and how many triangles.</summary>
        private void Census()
        {
            var by = new Dictionary<string, (int n, long tris)>();
            foreach (Renderer r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.isVisible) continue;
                Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent(out MeshFilter f) ? f.sharedMesh : null;
                string key = (m != null ? m.name : r.GetType().Name) + (r is SkinnedMeshRenderer ? " (skinned)" : "");
                long t = 0;
                if (m != null) for (int i = 0; i < m.subMeshCount; i++) t += (long)m.GetIndexCount(i) / 3;
                by.TryGetValue(key, out var v);
                by[key] = (v.n + 1, v.tris + t);
            }
            report.AppendLine();
            report.AppendLine("Seen by the view or the shadows, by mesh (renderers, thousand triangles once each):");
            foreach (var p in by.OrderByDescending(p => p.Value.tris).Take(24)) report.AppendLine($"- {p.Key}: {p.Value.n}, {p.Value.tris / 1000}");
            report.AppendLine($"- all: {by.Values.Sum(v => v.n)}, {by.Values.Sum(v => v.tris) / 1000}");
            report.AppendLine();
        }

        private IEnumerator Phase(string name, float seconds)
        {
            var frames = new List<float>();
            double mainSum = 0, scriptSum = 0, animSum = 0, physSum = 0, gcSum = 0, batchSum = 0, triSum = 0, passSum = 0;
            yield return null;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                mainSum += main.LastValue * 1e-6; scriptSum += (scripts.LastValue + late.LastValue) * 1e-6; animSum += anim.LastValue * 1e-6;
                physSum += phys.LastValue * 1e-6; gcSum += gc.LastValue / 1024.0; batchSum += batches.LastValue; triSum += tris.LastValue / 1000.0; passSum += setPass.LastValue;
            }
            int c = frames.Count;
            frames.Sort();
            float avg = frames.Average();
            report.AppendLine($"| {name} | {1000f / avg:0} | {avg:0.0} | {frames[(int)(c * 0.95f)]:0.0} | {frames[Mathf.Min(c - 1, (int)(c * 0.99f))]:0.0} | {mainSum / c:0.0} | {scriptSum / c:0.0} | {animSum / c:0.0} | {physSum / c:0.0} | {gcSum / c:0} | {batchSum / c:0} | {passSum / c:0} | {triSum / c:0} |");
            Debug.Log($"[Mass] {name}: {1000f / avg:0} FPS");
        }

        /// <summary>Top profiler markers (ms a frame; nested markers overlap): where the time goes.</summary>
        private IEnumerator TopMarkers(string name, int frames)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var recs = new List<(string name, ProfilerRecorder rec)>();
            foreach (ProfilerRecorderHandle h in handles)
            {
                ProfilerRecorderDescription d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                recs.Add((d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.SumAllSamplesInFrame)));
            }
            var sums = new double[recs.Count];
            for (int f = 0; f < frames; f++)
            {
                yield return null;
                for (int i = 0; i < recs.Count; i++) sums[i] += recs[i].rec.LastValue * 1e-6;
            }
            report.AppendLine();
            report.AppendLine($"Top markers, {name} (ms a frame, all threads summed, nested ones overlap):");
            foreach (int i in Enumerable.Range(0, recs.Count).OrderByDescending(i => sums[i]).Take(60))
                report.AppendLine($"- {recs[i].name}: {sums[i] / frames:0.00}");
            report.AppendLine();
            foreach (var r in recs) r.rec.Dispose();
        }

        /// <summary>The main thread's profiler tree over a few frames (editor only): total and self ms, calls and GC a frame.</summary>
        private IEnumerator Hierarchy(string name, int frames)
        {
#if UNITY_EDITOR
            // Through a file: in batch mode the editor's own profiler takes no frames
            string raw = Path.Combine(Application.temporaryCachePath, "mass_profile.raw");
            if (File.Exists(raw)) File.Delete(raw);
            UnityEditorInternal.ProfilerDriver.ClearAllFrames();
            UnityEngine.Profiling.Profiler.maxUsedMemory = 1 << 30;
            UnityEngine.Profiling.Profiler.logFile = raw;
            UnityEngine.Profiling.Profiler.enableBinaryLog = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            for (int i = 0; i < frames + 12; i++) yield return null;
            UnityEngine.Profiling.Profiler.enabled = false;
            UnityEngine.Profiling.Profiler.enableBinaryLog = false;
            UnityEngine.Profiling.Profiler.logFile = "";
            yield return null;
            if (File.Exists(raw)) UnityEditorInternal.ProfilerDriver.LoadProfile(raw, false);
            int last = UnityEditorInternal.ProfilerDriver.lastFrameIndex - 2, first = Mathf.Max(UnityEditorInternal.ProfilerDriver.firstFrameIndex, last - frames + 1);
            var total = new Dictionary<string, float[]>();
            var order = new List<string>();
            int counted = 0;
            for (int f = first; f <= last; f++)
            {
                using (var view = UnityEditorInternal.ProfilerDriver.GetHierarchyFrameDataView(f, 0, UnityEditor.Profiling.HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime, false))
                {
                    if (view == null || !view.valid) continue;
                    counted++;
                    var kids = new List<int>();
                    void Walk(int id, string path, int depth)
                    {
                        view.GetItemChildren(id, kids);
                        foreach (int kid in kids.ToArray())
                        {
                            float ms = view.GetItemColumnDataAsFloat(kid, UnityEditor.Profiling.HierarchyFrameDataView.columnTotalTime);
                            if (ms < 0.04f) continue;
                            string p = path + "/" + view.GetItemName(kid);
                            if (!total.TryGetValue(p, out float[] v)) { total[p] = v = new float[5]; order.Add(p); }
                            v[0] += ms;
                            v[1] += view.GetItemColumnDataAsFloat(kid, UnityEditor.Profiling.HierarchyFrameDataView.columnSelfTime);
                            v[2] += view.GetItemColumnDataAsFloat(kid, UnityEditor.Profiling.HierarchyFrameDataView.columnCalls);
                            v[3] += view.GetItemColumnDataAsFloat(kid, UnityEditor.Profiling.HierarchyFrameDataView.columnGcMemory);
                            v[4] = depth;
                            if (depth < 12) Walk(kid, p, depth + 1);
                        }
                    }
                    Walk(view.GetRootItemID(), "", 0);
                }
            }
            report.AppendLine();
            report.AppendLine($"Main thread tree, {name}, {counted} frames of {first}..{last} (total ms | self ms | calls | GC KB, a frame):");
            if (counted == 0) counted = 1;
            order.Sort(string.CompareOrdinal);
            foreach (string p in order)
            {
                float[] v = total[p];
                if (v[0] / counted < 0.08f) continue;
                report.AppendLine($"{new string(' ', (int)v[4] * 2)}{p.Substring(p.LastIndexOf('/') + 1)}: {v[0] / counted:0.00} | {v[1] / counted:0.00} | {v[2] / counted:0} | {v[3] / counted / 1024f:0.0}");
            }
            report.AppendLine();
#else
            yield break;
#endif
        }

        /// <summary>What each part of the picture costs: it is switched off for a moment and the triangles, batches and frame time are read again.</summary>
        private IEnumerator Breakdown(List<NpcKingdom> made)
        {
            report.AppendLine();
            report.AppendLine("Breakdown (a part off: triangles k | batches | set pass | ms | GPU ms: opaque, sun shadows, SSAO, the camera in all):");
            // The card's own time of the passes that matter (a few samplers: it works with the screen off, unlike a player)
            var gpu = new[] { "DrawOpaqueObjects", "Draw Main Light Shadowmap", "SSAO", "UniversalRenderPipeline.RenderSingleCameraInternal: Main Camera" }
                .Select(UnityEngine.Profiling.Recorder.Get).ToArray();
            foreach (var g in gpu) if (g.isValid) g.enabled = true;
            IEnumerator Read(string name)
            {
                for (int i = 0; i < 20; i++) yield return null;
                double t = 0, b = 0, s = 0, ms = 0; int n = 0;
                var g = new double[gpu.Length];
                float end = Time.realtimeSinceStartup + 2f;
                while (Time.realtimeSinceStartup < end)
                {
                    yield return null;
                    t += tris.LastValue / 1000.0; b += batches.LastValue; s += setPass.LastValue; ms += Time.unscaledDeltaTime * 1000.0; n++;
                    for (int k = 0; k < gpu.Length; k++) g[k] += gpu[k].gpuElapsedNanoseconds * 1e-6;
                }
                report.AppendLine($"- {name}: {t / n:0} | {b / n:0} | {s / n:0} | {ms / n:0.00} | {g[0] / n:0.00}, {g[1] / n:0.00}, {g[2] / n:0.00}, {g[3] / n:0.00}");
            }
            float scaleWas = Time.timeScale; Time.timeScale = 0f; // (nobody walks: the same picture for every reading)
            yield return Read("all on");
            Light sun = RenderSettings.sun != null ? RenderSettings.sun : UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (sun != null) { LightShadows was = sun.shadows; sun.shadows = LightShadows.None; yield return Read("sun shadows off"); sun.shadows = was; }
            CrowdDraw.Hide = true; yield return Read("crowd batch off"); CrowdDraw.Hide = false;
            foreach (string kind in new[] { "TownHouse", "TownWall", "TownPier", "TownField", "TownMine", "TownSawmill" })
            {
                var rs = made.SelectMany(k => k.GetComponentsInChildren<Renderer>()).Where(r => r.enabled && r.GetComponentInParent<KingdomTownPartMarker>() == null && HasParentNamed(r.transform, kind)).ToList();
                foreach (Renderer r in rs) r.enabled = false;
                yield return Read(kind + " off (" + rs.Count + " renderers)");
                foreach (Renderer r in rs) r.enabled = true;
            }
            var skinned = UnityEngine.Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(r => r.enabled).ToList();
            foreach (Renderer r in skinned) r.enabled = false;
            yield return Read("skinned renderers off (" + skinned.Count + ")");
            foreach (Renderer r in skinned) r.enabled = true;
            var others = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.GetComponentInParent<NpcKingdom>() == null && !(r is SkinnedMeshRenderer)).ToList();
            string MeshName(Renderer r) { Mesh m = r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null; return m == null ? "no mesh" : System.Text.RegularExpressions.Regex.Replace(m.name, "[-_ ]?[0-9a-f]*[0-9][0-9a-f]*$| Instance$", ""); }
            foreach (var g in others.GroupBy(r => r is ParticleSystemRenderer ? "particles" : r is MeshRenderer ? "mesh " + MeshName(r) + " / " + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "-") + (r.HasPropertyBlock() ? " +block" : "") : r.GetType().Name).OrderByDescending(g => g.Count()).Take(22))
            {
                foreach (Renderer r in g) r.enabled = false;
                yield return Read(g.Key + " off (" + g.Count() + ")");
                foreach (Renderer r in g) r.enabled = true;
            }
            // By shader, everything (towns too): which surfaces the card pays for
            var all = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && !(r is ParticleSystemRenderer) && r.sharedMaterial != null).ToList();
            foreach (var g in all.GroupBy(r => r.sharedMaterial.shader.name).OrderByDescending(g => g.Count()))
            {
                foreach (Renderer r in g) r.enabled = false;
                yield return Read("shader " + g.Key + " off (" + g.Count() + ")");
                foreach (Renderer r in g) r.enabled = true;
            }
            GrassBladesOff(true); yield return Read("grass off"); GrassBladesOff(false);
            yield return Read("all on again");
            foreach (var g in gpu) if (g.isValid) g.enabled = false;
            Time.timeScale = scaleWas;
            report.AppendLine();
        }

        private static void GrassBladesOff(bool off)
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<World.GrassFieldRenderer>(FindObjectsSortMode.None)) b.enabled = !off;
        }

        private sealed class KingdomTownPartMarker : MonoBehaviour { }
        private static bool HasParentNamed(Transform t, string name) { for (; t != null; t = t.parent) if (t.name == name) return true; return false; }

        /// <summary>Each script type on the units off for a while: what it costs.</summary>
        private IEnumerator Ablation()
        {
            var byType = new Dictionary<Type, List<Behaviour>>();
            void Add(Behaviour b)
            {
                if (b == null || !b.enabled || b is GpuSkinnedCharacter || b is CharacterGear || b is Animator) return; // drawing: switching it off upsets the scene
                if (!byType.TryGetValue(b.GetType(), out var l)) byType[b.GetType()] = l = new List<Behaviour>();
                l.Add(b);
            }
            foreach (HostileMobAI m in UnityEngine.Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None))
                foreach (Behaviour b in m.GetComponentsInChildren<Behaviour>()) if (b is MonoBehaviour) Add(b);
            foreach (KingdomTown t in UnityEngine.Object.FindObjectsByType<KingdomTown>(FindObjectsSortMode.None)) Add(t);
            IEnumerator Measure(float seconds, List<float> into) { float e = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < e) { yield return null; into.Add(Time.unscaledDeltaTime * 1000f); } }
            report.AppendLine();
            report.AppendLine("Each script type off for 3 s (inside a town; saved = base minus that, ms a frame):");
            report.AppendLine();
            report.AppendLine("| type | instances | base ms | off ms | saved ms | us per instance |");
            report.AppendLine("|---|---|---|---|---|---|");
            foreach (var pair in byType.OrderByDescending(p => p.Value.Count))
            {
                var before = new List<float>();
                yield return Measure(3f, before);
                foreach (Behaviour b in pair.Value) if (b != null) b.enabled = false;
                var off = new List<float>();
                yield return Measure(0.5f, new List<float>());
                yield return Measure(3f, off);
                foreach (Behaviour b in pair.Value) if (b != null) b.enabled = true;
                float baseMs = before.Average(), offMs = off.Average();
                report.AppendLine($"| {pair.Key.Name} | {pair.Value.Count} | {baseMs:0.0} | {offMs:0.0} | {baseMs - offMs:0.0} | {(baseMs - offMs) * 1000f / pair.Value.Count:0.0} |");
            }
            // All the units' and the towns' renderers off: what drawing them costs
            var rends = UnityEngine.Object.FindObjectsByType<NpcKingdom>(FindObjectsSortMode.None).SelectMany(k => k.GetComponentsInChildren<Renderer>()).Where(r => r.enabled).ToList();
            var b1 = new List<float>(); var b0 = new List<float>();
            yield return Measure(3f, b1);
            foreach (Renderer r in rends) r.enabled = false;
            yield return Measure(0.5f, new List<float>());
            yield return Measure(3f, b0);
            foreach (Renderer r in rends) r.enabled = true;
            report.AppendLine($"| (town renderers) | {rends.Count} | {b1.Average():0.0} | {b0.Average():0.0} | {b1.Average() - b0.Average():0.0} | - |");
            report.AppendLine();
        }
    }
}
