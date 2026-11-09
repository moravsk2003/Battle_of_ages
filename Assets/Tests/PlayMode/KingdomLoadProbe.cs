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
using BattleOfAges.SaveLoad;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Performance review tool (not a regular test): what real NPC kingdoms cost. A 4 km campaign with fifty kingdoms, all of
    /// them grown far away first for KINGDOM_GROW_HOURS game hours (default 96: four days with no player about), then the
    /// nearest 1, 10, 20, 40 and 50 to the base are made real in turn (KINGDOM_STEPS) and each step measured while the main
    /// camera renders 1920x1080. Writes kingdom_load.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Performance review tool, run on demand")]
    public class KingdomLoadProbe
    {
        [UnityTest, Timeout(1800000)]
        public IEnumerator RealKingdoms_Cost()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            float growHours = float.TryParse(Environment.GetEnvironmentVariable("KINGDOM_GROW_HOURS"), out float g) ? g : 96f;
            string stepsText = Environment.GetEnvironmentVariable("KINGDOM_STEPS");
            int[] steps = string.IsNullOrEmpty(stepsText) ? new[] { 1, 10, 20, 40, 50 } : stepsText.Split(',').Select(int.Parse).ToArray();

            LogAssert.ignoreFailingMessages = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            int nearHome = GameBalance.RealKingdomsNearHome, byPlayers = GameBalance.RealKingdomsByPlayers;
            GameBalance.RealKingdomsNearHome = 0; // none real until grown
            GameBalance.RealKingdomsByPlayers = 0;
            var report = new StringBuilder();
            RenderTexture rt = null;
            Camera cam = null;
            var recorders = new List<ProfilerRecorder>();
            try
            {
                BattleOfAges.SaveLoad.SaveSystem.FolderOverride = Path.Combine(Path.GetTempPath(), "boa_kingdom_probe");
                BattleOfAges.SaveLoad.SaveSystem.NewGame(new BattleOfAges.SaveLoad.CampaignSetup
                    { Seed = 777, Size = 4000f, Heroes = 51, KingShare = 1f, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
                while (SceneManager.GetActiveScene().name != BattleOfAges.SaveLoad.SaveSystem.GameScene) yield return null;
                yield return new WaitForSeconds(3f);
                GameObject player = GameObject.Find("Player");
                player.GetComponent<HealthComponent>()?.InitializeHealth(1000000);

                // Grown far away, as with no player about: hour by hour, as the realm does
                float growStart = Time.realtimeSinceStartup;
                foreach (KingdomState s in KingdomRealm.Far) for (int h = 0; h < growHours; h++) KingdomRealm.Advance(s, 1f);
                List<KingdomState> grown = KingdomRealm.Far.Where(s => !s.Defeated).ToList();
                report.AppendLine($"# Real NPC kingdoms: what they cost");
                report.AppendLine($"Campaign seed 777, 4 km, {grown.Count} kingdoms standing after {growHours:0} game hours grown far away ({Time.realtimeSinceStartup - growStart:0.0} s to grow them).");
                report.AppendLine($"People per kingdom: avg {grown.Average(s => s.Subjects.Count):0.0}, max {grown.Max(s => s.Subjects.Count)}; hall avg {grown.Average(s => s.Hall):0.0}, max {grown.Max(s => s.Hall)}; armies out {grown.Sum(s => s.Armies.Count)}.");
                report.AppendLine($"Unity {Application.unityVersion}, {SystemInfo.processorType} ({SystemInfo.processorCount} threads), {SystemInfo.graphicsDeviceName}, render 1920x1080 each frame, the hero at the base.");
                report.AppendLine();
                report.AppendLine("| real kingdoms | their people | units in scene | buildings | avg FPS | avg ms | p95 ms | p99 ms | main ms | scripts ms | anim ms | physics ms | GC KB/frame | batches | tris (k) | settle s |");
                report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

                ProfilerRecorder Rec(ProfilerCategory c, string n) { var r = ProfilerRecorder.StartNew(c, n, 15); recorders.Add(r); return r; }
                ProfilerRecorder main = Rec(ProfilerCategory.Internal, "Main Thread"), scripts = Rec(ProfilerCategory.Scripts, "BehaviourUpdate"),
                    late = Rec(ProfilerCategory.Scripts, "LateBehaviourUpdate"), anim = Rec(ProfilerCategory.Animation, "Animators.Update"),
                    phys = Rec(ProfilerCategory.Physics, "Physics.Simulate"), gc = Rec(ProfilerCategory.Memory, "GC Allocated In Frame"),
                    batches = Rec(ProfilerCategory.Render, "Batches Count"), tris = Rec(ProfilerCategory.Render, "Triangles Count");
                cam = Camera.main;
                rt = new RenderTexture(1920, 1080, 24);
                cam.targetTexture = rt;

                foreach (int n in steps)
                {
                    GameBalance.RealKingdomsNearHome = n;
                    // They come gradually: until the scene stops growing (or 40 s)
                    float settleStart = Time.realtimeSinceStartup, stableSince = Time.realtimeSinceStartup;
                    int last = -1;
                    while (Time.realtimeSinceStartup - settleStart < 40f)
                    {
                        yield return new WaitForSeconds(1f);
                        int now = NpcKingdom.All.Count * 1000 + NpcKingdom.All.Sum(k => k != null ? k.Population : 0);
                        if (now != last) { last = now; stableSince = Time.realtimeSinceStartup; }
                        else if (Time.realtimeSinceStartup - stableSince > 3f && NpcKingdom.All.Count >= Mathf.Min(n, grown.Count)) break;
                    }
                    float settled = Time.realtimeSinceStartup - settleStart;

                    var frames = new List<float>();
                    double mainSum = 0, scriptSum = 0, animSum = 0, physSum = 0, gcSum = 0, batchSum = 0, triSum = 0;
                    float end = Time.realtimeSinceStartup + 8f;
                    while (Time.realtimeSinceStartup < end)
                    {
                        yield return null;
                        frames.Add(Time.unscaledDeltaTime * 1000f);
                        mainSum += main.LastValue * 1e-6; scriptSum += (scripts.LastValue + late.LastValue) * 1e-6; animSum += anim.LastValue * 1e-6;
                        physSum += phys.LastValue * 1e-6; gcSum += gc.LastValue / 1024.0; batchSum += batches.LastValue; triSum += tris.LastValue / 1000.0;
                    }
                    int c = frames.Count;
                    frames.Sort();
                    float avg = frames.Average();
                    int people = NpcKingdom.All.Sum(k => k != null ? k.Population : 0);
                    int units = UnityEngine.Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None).Length;
                    int buildings = BattleOfAges.Building.BuildingNode.Active.Count;
                    report.AppendLine($"| {NpcKingdom.All.Count} | {people} | {units} | {buildings} | {1000f / avg:0} | {avg:0.0} | {frames[(int)(c * 0.95f)]:0.0} | {frames[(int)(c * 0.99f)]:0.0} | {mainSum / c:0.0} | {scriptSum / c:0.0} | {animSum / c:0.0} | {physSum / c:0.0} | {gcSum / c:0} | {batchSum / c:0} | {triSum / c:0} | {settled:0} |");
                    Debug.Log($"[KingdomLoad] {NpcKingdom.All.Count} real: {1000f / avg:0} FPS, {people} people");
                }
                if (Environment.GetEnvironmentVariable("KINGDOM_ABLATE") == "1")
                {
                    // Where the time goes at the last step: every script type on the units (and the kingdoms) off for a while
                    var byType = new Dictionary<Type, List<Behaviour>>();
                    void Add(Behaviour b) { if (b == null || !b.enabled || b is GpuSkinnedCharacter || b is CharacterGear || b is Animator) return; // (drawing: switching it off upsets the scene) if (!byType.TryGetValue(b.GetType(), out var l)) byType[b.GetType()] = l = new List<Behaviour>(); l.Add(b); }
                    foreach (HostileMobAI m in UnityEngine.Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None))
                        foreach (Behaviour b in m.GetComponentsInChildren<Behaviour>()) if (b is MonoBehaviour || b is Animator) Add(b);
                    foreach (NpcKingdom k in NpcKingdom.All) if (k != null) Add(k);
                    IEnumerator Measure(float seconds, List<float> into) { float e = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < e) { yield return null; into.Add(Time.unscaledDeltaTime * 1000f); } }
                    report.AppendLine();
                    report.AppendLine("Ablation at the last step (each script type off on every unit for 3 s; saved = base minus that, ms per frame):");
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
                        Debug.Log($"[KingdomLoad] {pair.Key.Name}: {baseMs - offMs:0.0} ms");
                    }
                }
                report.AppendLine();
                report.AppendLine("main ms is the previous frame's (ProfilerRecorder.LastValue in a coroutine). Units: every HostileMobAI (kingdoms' people, camps).");
                File.WriteAllText(Path.Combine(dir, "kingdom_load.md"), report.ToString());
                Debug.Log("[KingdomLoad] report written\n" + report);
            }
            finally
            {
                GameBalance.RealKingdomsNearHome = nearHome;
                GameBalance.RealKingdomsByPlayers = byPlayers;
                if (cam != null) cam.targetTexture = null;
                if (rt != null) rt.Release();
                foreach (ProfilerRecorder r in recorders) r.Dispose();
                BattleOfAges.SaveLoad.SaveSystem.FolderOverride = null;
                NpcKingdom.RemoveAll();
                KingdomRealm.Clear();
                WorldSettings.Set(0, 0f);
            }
        }
    }
}
