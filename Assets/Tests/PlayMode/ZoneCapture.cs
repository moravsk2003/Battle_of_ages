using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Managers;
using BattleOfAges.Player;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// All six zones of a new campaign, marked round the base and left to the workers (M5): shots from the map and the
    /// ground, and a report of what they raised and how the stock went (CAPTURE_DIR, SIM_SPEED default 6, SIM_HOURS default 6).
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class ZoneCapture
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator CaptureZones()
        {
            string Env(string n, string d) { string v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
            string dir = Env("CAPTURE_DIR", Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "zones"));
            Directory.CreateDirectory(dir);
            float speed = float.Parse(Env("SIM_SPEED", "6")), hours = float.Parse(Env("SIM_HOURS", "6"));
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.FolderOverride = Path.Combine(Path.GetTempPath(), "boa_zone_capture");
            SaveSystem.NewGame(new CampaignSetup { Seed = 777, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            GameBalance.BanditFirstRaidHours = 1000f; BanditRaids.NextRaidHour = 1000.0; // the look, not the fight

            ResourceManager rm = ResourceManager.Instance;
            rm.SetState(400, new[] { new KeyValuePair<int, int>(0, 80), new KeyValuePair<int, int>(1, 30) });
            GameObject hallObj = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
            Vector3 hallAt = new Vector3(0f, 0f, -44f);
            hallAt.y = RtsUnit.GetTerrainHeight(hallAt);
            hallObj.transform.position = hallAt;
            hallObj.GetComponent<BuildingNode>().OnBuilt();
            hallObj.GetComponent<TownHall>().Level = 2;
            yield return null;

            void Mark(ZoneType t, Vector3 at, int strokes) { for (int i = 0; i < strokes; i++) ZoneMap.Paint(at + new Vector3(i * 16f, 0f, 0f), 14f, t, out _); }
            Mark(ZoneType.Housing, new Vector3(-90f, 0f, -80f), 3);
            Mark(ZoneType.Fields, new Vector3(30f, 0f, -90f), 3);
            Mark(ZoneType.Forest, new Vector3(-100f, 0f, 30f), 2);
            Mark(ZoneType.Crafts, new Vector3(60f, 0f, 20f), 2);
            Mark(ZoneType.Mining, new Vector3(60f, 0f, 70f), 2);
            Mark(ZoneType.Trade, new Vector3(-60f, 0f, 80f), 2);

            var report = new StringBuilder("# Zones left to the workers\n\n");
            foreach (ZoneType t in ZoneMap.Types) report.AppendLine($"- {ZoneMap.Name(t)}: {ZoneMap.Count(t)} cells, calls for {ZoneGrower.Wanted(t, ZoneMap.Count(t))}");
            report.AppendLine("\n| game hour | sites | built | wood | food | workers | at places | state |\n|---|---|---|---|---|---|---|---|");
            Time.timeScale = speed;
            float nextRow = 0f;
            while (WorldClock.GameHours < hours)
            {
                if (WorldClock.GameHours >= nextRow)
                {
                    nextRow += 0.5f;
                    int atPlaces = 0;
                    foreach (WorkPlace p in WorkPlace.All) atPlaces += p.Present;
                    var states = new Dictionary<string, int>();
                    foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers) if (w != null) { string k = w.CurrentState.ToString(); states.TryGetValue(k, out int c); states[k] = c + 1; }
                    var parts = new List<string>();
                    foreach (var st in states) parts.Add(st.Key + " " + st.Value);
                    int raiding = 0;
                    foreach (NpcKingdom k in NpcKingdom.All) if (k != null && k.Raiding) raiding++;
                    int chasing = 0;
                    foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers) if (w != null && w.GetComponent<RtsUnit>().HasAttackTarget) chasing++;
                    report.AppendLine($"| {WorldClock.GameHours:0.0} | {ConstructionSite.All.Count} | {BuildingNode.Active.Count - ConstructionSite.All.Count} | {rm.WoodCount} | {rm.FoodCount} | {WorkerRegistry.WorkerCount} | {atPlaces} | alarm {SettlementAlarm.Active}, raids {raiding}, fighting {chasing}; {string.Join(", ", parts)} |");
                }
                yield return null;
            }
            Time.timeScale = 1f;
            report.AppendLine();
            var built = new Dictionary<BuildingType, int>();
            foreach (BuildingNode b in BuildingNode.Active)
                if (b != null && b.GetComponent<ConstructionSite>() == null) { built.TryGetValue(b.Type, out int n); built[b.Type] = n + 1; }
            foreach (var pair in built) report.AppendLine($"- built {pair.Key}: {pair.Value}");
            File.WriteAllText(Path.Combine(dir, "zones_report.md"), report.ToString());

            GameContext.Modes?.SetGameMode(GameMode.RtsMap);
            yield return new WaitForSeconds(0.5f);
            var cap = new CaptureRenderer(dir, 2);
            cap.Camera.orthographic = true;
            cap.Camera.orthographicSize = 95f;
            Vector3 mid = new Vector3(0f, RtsUnit.GetTerrainHeight(Vector3.zero), 0f);
            cap.Shoot(0, "map", mid + new Vector3(0f, 170f, -98f), mid);
            cap.Camera.orthographic = false;
            cap.Camera.fieldOfView = 60f;
            cap.Shoot(1, "ground", new Vector3(0f, mid.y + 14f, -70f), new Vector3(30f, mid.y, -90f));
            cap.Finish();
            GameBalance.BanditFirstRaidHours = 6f;
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll(); KingdomRealm.Clear(); ZoneMap.Clear(); WorldSettings.Set(0, 0f);
        }
    }
}
