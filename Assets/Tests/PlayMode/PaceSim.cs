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
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// How fast a campaign goes from its real start (nothing given): a simple player marks the zones at once, sets a hall
    /// as soon as it can, raises it, then founds a settlement with SIM_BUILDERS workers. One game hour is a real minute.
    /// Writes pace_report.md to CAPTURE_DIR (SIM_SPEED default 12, SIM_HOURS default 90, SIM_SEED default 777, SIM_RAIDS 1: bandits on),
    /// and adds a row to zone_ab.md there: SIM_ZONES names the zones to mark (default all), SIM_FOUND 0 leaves out the second settlement,
    /// to compare what each zone gives.
    /// </summary>
    [Explicit("Balance simulation, run on demand")]
    public class PaceSim
    {
        private static string Env(string n, string d) { string v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }

        [UnityTest, Timeout(1200000)]
        public IEnumerator FromTheRealStart()
        {
            string dir = Env("CAPTURE_DIR", Directory.GetCurrentDirectory());
            Directory.CreateDirectory(dir);
            float speed = float.Parse(Env("SIM_SPEED", "12")), hours = float.Parse(Env("SIM_HOURS", "90"));
            int seed = int.Parse(Env("SIM_SEED", "777")), builders = int.Parse(Env("SIM_BUILDERS", "4"));
            float firstRaid = GameBalance.BanditFirstRaidHours;
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.FolderOverride = Path.Combine(Path.GetTempPath(), "boa_pace_sim");
            SaveSystem.NewGame(new CampaignSetup { Seed = seed, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            if (Env("SIM_RAIDS", "0") != "1") { GameBalance.BanditFirstRaidHours = 100000f; BanditRaids.NextRaidHour = 100000.0; }

            ResourceManager rm = ResourceManager.Instance;
            var report = new StringBuilder($"# Pace from the real start (seed {seed}, speed {speed}, raids {Env("SIM_RAIDS", "0")})\n\n");
            report.AppendLine($"Start: wood {rm.WoodCount}, ore {rm.OreCount}, food {rm.FoodCount}, people {WorkerRegistry.WorkerCount}. One game hour = {GameBalance.GameHourSeconds:0} real seconds.\n");
            var marks = new List<string>();
            void Mark(string what) { marks.Add($"| {WorldClock.GameHours:0.0} | {what} | wood {rm.Main.WoodCount}, ore {rm.Main.OreCount}, food {rm.Main.FoodCount}, people {WorkerRegistry.WorkerCount} |"); }
            var seen = new HashSet<BuildingType>();

            Vector3 hallAt = new Vector3(0f, 0f, -44f);
            string zoneList = Env("SIM_ZONES", "Fields,Housing,Mining,Crafts,Forest,Trade");
            void Zones(ZoneType t, Vector3 at, int strokes)
            {
                if (!zoneList.Contains(t.ToString())) return;
                for (int i = 0; i < strokes; i++) ZoneMap.Paint(at + new Vector3(i * 16f, 0f, 0f), 14f, t, out _);
            }
            Zones(ZoneType.Fields, new Vector3(30f, 0f, -90f), 3);
            Zones(ZoneType.Housing, new Vector3(-90f, 0f, -80f), 3);
            Zones(ZoneType.Mining, new Vector3(60f, 0f, 70f), 2);
            Zones(ZoneType.Crafts, new Vector3(60f, 0f, 20f), 1);
            Zones(ZoneType.Forest, new Vector3(-100f, 0f, 30f), 2);

            var table = new StringBuilder("| hour | wood | ore | in mines | food | days of food | people | sites | at places | states |\n|---|---|---|---|---|---|---|---|---|---|\n");
            Time.timeScale = speed;
            TownHall hall = null;
            GameObject second = null;
            float foundingAt = -1f, hungryHours = 0f, minDays = float.MaxValue, nextRow = 0f, lastHour = 0f;
            bool hallPlaced = false, foundWood = false, tradeMarked = false;
            int level = 0;
            bool found = Env("SIM_FOUND", "1") != "0";
            float woodGained = 0f, oreGained = 0f, foodGained = 0f, farmerHours = 0f, sawyerHours = 0f, minerHours = 0f, peopleHours = 0f, hallOne = -1f, hallTwo = -1f, at15 = -1f, at25 = -1f;
            int lastWood = rm.Main.WoodCount, lastOre = rm.Main.OreCount;
            float lastFood = rm.Main.Food;
            while (WorldClock.GameHours < hours)
            {
                float now = (float)WorldClock.GameHours;
                float dt = now - lastHour;
                lastHour = now;
                SettlementStock stock = rm.Main;
                woodGained += Mathf.Max(0, stock.WoodCount - lastWood);
                oreGained += Mathf.Max(0, stock.OreCount - lastOre);
                foodGained += Mathf.Max(0f, stock.Food - lastFood);
                lastWood = stock.WoodCount; lastOre = stock.OreCount; lastFood = stock.Food;
                foreach (WorkPlace place in WorkPlace.All)
                {
                    if (place is FieldNode) farmerHours += place.Present * dt;
                    else if (place is SawmillNode) sawyerHours += place.Present * dt;
                }
                foreach (MineNode mine in MineNode.ActiveMines) if (mine != null) minerHours += mine.AssignedWorkerCount * dt;
                peopleHours += WorkerRegistry.WorkerCount * dt;
                if (at15 < 0f && WorkerRegistry.WorkerCount >= 15) at15 = now;
                if (at25 < 0f && WorkerRegistry.WorkerCount >= 25) at25 = now;
                if (hallOne < 0f && TownHall.Current != null) hallOne = now;
                if (hallTwo < 0f && TownHall.Current != null && TownHall.Current.Level >= 2) hallTwo = now;
                if (stock.Hungry) hungryHours += dt;
                float perDay = Mathf.Max(1, TownHall.Population) * GameBalance.FoodPerPersonPerDay;
                minDays = Mathf.Min(minDays, stock.Food / perDay);

                if (!foundWood && stock.WoodCount >= 20) { foundWood = true; Mark("20 wood"); }
                foreach (BuildingNode b in BuildingNode.Active)
                    if (b != null && b.GetComponent<ConstructionSite>() == null && seen.Add(b.Type)) Mark("first " + b.Type + " built");

                BuildingDefinition hallDef = BuildingData.GetDefinition(BuildingType.TownHall);
                if (!hallPlaced && stock.WoodCount >= hallDef.WoodCost && stock.OreCount >= hallDef.OreCost)
                {
                    hallPlaced = true;
                    stock.ConsumeResource(ResourceType.Wood, hallDef.WoodCost);
                    stock.ConsumeOreAtLeast(hallDef.OreTierRequired, hallDef.OreCost);
                    GameObject h = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
                    Vector3 p = hallAt;
                    p.y = RtsUnit.GetTerrainHeight(p);
                    h.transform.position = p;
                    ConstructionSite.Begin(h, BuildingType.TownHall);
                    Mark("hall I placed");
                }
                if (hall == null && TownHall.Current != null) { hall = TownHall.Current; level = hall.Level; Mark("hall I standing"); }
                if (hall != null && hall.Level < TownHall.MaxLevel && hall.CanUpgrade(out _))
                {
                    hall.TryUpgrade();
                    level = hall.Level;
                    Mark("hall " + level);
                }
                if (hall != null && hall.Level >= 2 && !tradeMarked) { tradeMarked = true; Zones(ZoneType.Trade, new Vector3(-60f, 0f, 80f), 2); }

                // A new settlement as soon as it can be paid for
                if (found && second == null && hall != null && hall.Level >= GameBalance.FoundingHallLevel && stock.WoodCount >= GameBalance.FoundingWood && stock.OreCount >= GameBalance.FoundingOre)
                {
                    Vector3 place = Vector3.zero;
                    for (float r = 650f; r < 2000f && place == Vector3.zero; r += 100f)
                        for (int i = 0; i < 16 && place == Vector3.zero; i++)
                        {
                            Vector3 p = hall.transform.position + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * r;
                            if (WorldSettings.Inside(p, 60f) && !Lakes.InWater(p.x, p.z, 40f) && TownHall.FoundingForbidden(p) == null) place = p;
                        }
                    stock.ConsumeResource(ResourceType.Wood, GameBalance.FoundingWood);
                    stock.ConsumeOreAtLeast(0, GameBalance.FoundingOre);
                    second = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
                    place.y = RtsUnit.GetTerrainHeight(place);
                    second.transform.position = place;
                    ConstructionSite site = ConstructionSite.Begin(second, BuildingType.TownHall);
                    int sent = 0;
                    foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
                        if (w != null && sent < builders && w.IsWorking && !w.IsStationed && w.AssignToSite(site)) sent++;
                    foundingAt = now;
                    Mark($"second hall placed {Vector3.Distance(place, hall.transform.position):0} m off, {sent} sent, {site.WorkSeconds:0} worker-seconds of work");
                }
                if (foundingAt >= 0f && second != null && second.GetComponent<ConstructionSite>() == null)
                {
                    Mark($"second hall built after {now - foundingAt:0.0} h");
                    foundingAt = -2f;
                }

                if (now >= nextRow)
                {
                    nextRow += 3f;
                    int atPlaces = 0, inMines = 0;
                    foreach (WorkPlace p in WorkPlace.All) atPlaces += p.Present;
                    foreach (MineNode m in MineNode.ActiveMines) if (m != null) inMines += m.AccumulatedOreCount;
                    var states = new Dictionary<GathererState, int>();
                    foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers) if (w != null) { states.TryGetValue(w.CurrentState, out int c); states[w.CurrentState] = c + 1; }
                    var parts = new List<string>();
                    foreach (var st in states) parts.Add(st.Key + " " + st.Value);
                    table.AppendLine($"| {now:0} | {stock.WoodCount} | {stock.OreCount} | {inMines} | {stock.FoodCount} | {stock.Food / perDay:0.0} | {WorkerRegistry.WorkerCount} | {ConstructionSite.All.Count} | {atPlaces} | {string.Join(", ", parts)} |");
                }
                yield return null;
            }
            Time.timeScale = 1f;

            report.AppendLine("## Milestones\n\n| game hour = real min | what | then |\n|---|---|---|");
            foreach (string m in marks) report.AppendLine(m);
            report.AppendLine($"\nLowest food: {minDays:0.0} days. Hungry for {hungryHours:0.0} h of {hours:0}.\n");
            report.AppendLine("## Over time\n");
            report.Append(table);
            File.WriteAllText(Path.Combine(dir, "pace_report.md"), report.ToString());

            var built = new Dictionary<BuildingType, int>();
            foreach (BuildingNode b in BuildingNode.Active)
                if (b != null && b.GetComponent<ConstructionSite>() == null) { built.TryGetValue(b.Type, out int n); built[b.Type] = n + 1; }
            var builtText = new List<string>();
            foreach (var pair in built) builtText.Add(pair.Key + " " + pair.Value);
            string ab = Path.Combine(dir, "zone_ab.md");
            if (!File.Exists(ab)) File.WriteAllText(ab, "| zones | seed | hall I h | hall II h | 15 ppl h | 25 ppl h | people | wood gained | ore gained | food gained | wood | ore | food | low food days | hungry h | farmer-h | sawyer-h | miner-h | people-h | built |\n|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            File.AppendAllText(ab, $"| {zoneList} | {seed} | {hallOne:0.0} | {hallTwo:0.0} | {at15:0.0} | {at25:0.0} | {WorkerRegistry.WorkerCount} | {woodGained:0} | {oreGained:0} | {foodGained:0} | {rm.Main.WoodCount} | {rm.Main.OreCount} | {rm.Main.FoodCount} | {minDays:0.0} | {hungryHours:0.0} | {farmerHours:0} | {sawyerHours:0} | {minerHours:0} | {peopleHours:0} | {string.Join(", ", builtText)} |\n");

            GameBalance.BanditFirstRaidHours = firstRaid;
            SaveSystem.FolderOverride = null;
            foreach (TownHall h in Object.FindObjectsByType<TownHall>(FindObjectsSortMode.None)) Object.Destroy(h.gameObject);
            GameObject hero = GameObject.Find("Player");
            if (hero != null) Object.Destroy(hero);
            NpcKingdom.RemoveAll(); KingdomRealm.Clear(); ZoneMap.Clear(); WorldSettings.Set(0, 0f);
        }
    }
}
