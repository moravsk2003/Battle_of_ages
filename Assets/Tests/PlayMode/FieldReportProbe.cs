using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Measurements in a real new campaign for what testers report: how far the kingdoms are and when their raids set off and
    /// arrive (ReportsOfRaids), and whether units sent past the first house and the things by it get there (UnitsRoundTheHouse).
    /// Both write to CAPTURE_DIR, or the temp folder.
    /// </summary>
    [Explicit("Measurement in a real campaign, run on demand")]
    public class FieldReportProbe
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_field_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            Time.timeScale = 1f;
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        private static string Out(string name)
        {
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR");
            return Path.Combine(string.IsNullOrEmpty(dir) ? Path.GetTempPath() : dir, name);
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        [UnityTest, Timeout(1500000)]
        public IEnumerator ReportsOfRaids()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            var report = new StringBuilder();
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Vector3 home = TownHall.Current != null ? TownHall.Current.transform.position : hero.transform.position;
            report.AppendLine($"seed {WorldSettings.Seed}, size {WorldSettings.Size}, hero speed {hero.GetComponent<CombatUnit>().MoveSpeed:0.0} m/s, hall at home {TownHall.Current != null}");
            var rows = new List<(float d, string line)>();
            foreach (NpcKingdom k in NpcKingdom.All) rows.Add((Flat(k.Home, home), $"real {k.KingdomName}{(k.Free ? " (вільний)" : "")}{(k.IsEmpire ? " (імперія)" : "")}, first raid at {k.FirstRaidAt:0.0} h"));
            foreach (KingdomSave s in KingdomRealm.Far) rows.Add((Flat(s.Home, home), $"far {s.Name}{(s.Free ? " (вільний)" : "")}{(s.Empire ? " (імперія)" : "")}"));
            foreach (var r in rows.OrderBy(r => r.d)) report.AppendLine($"{r.d:0} m: {r.line}");

            double started = WorldClock.GameHours;
            float Minute() => (float)(WorldClock.GameHours - started) * GameBalance.GameHourSeconds / 60f;
            float? firstAtPlayer = null;
            NpcKingdom.RaidStarted += (k, target) =>
            {
                bool atPlayer = target != null && (target.GetComponent<TownHall>() != null || target.GetComponent<PlayerCombatController>() != null);
                report.AppendLine($"minute {Minute():0.0}: {k.KingdomName} sets off, {k.Raiders.Count} fighters, at {(target != null ? target.name : "?")}{(atPlayer ? " (the player)" : "")}, {Flat(k.Home, target != null ? target.transform.position : k.Home):0} m");
            };
            Time.timeScale = 10f;
            float end = Time.realtimeSinceStartup + 1300f;
            float nextRow = 0f;
            while (Minute() < 45f && Time.realtimeSinceStartup < end)
            {
                float nearest = float.MaxValue;
                foreach (NpcKingdom k in NpcKingdom.All)
                    foreach (HostileMobAI m in k.Raiders)
                        if (m != null) nearest = Mathf.Min(nearest, Flat(m.transform.position, home));
                if (nearest < 40f && firstAtPlayer == null)
                {
                    firstAtPlayer = Minute();
                    report.AppendLine($"minute {Minute():0.0}: FIRST KING'S RAIDERS AT THE PLAYER'S BASE");
                }
                if (Minute() >= nextRow)
                {
                    nextRow += 10f;
                    string state = string.Join("; ", NpcKingdom.All.Where(k => k != null).Select(k =>
                    {
                        var party = k.RaidParty();
                        float ours = NpcKingdom.PowerOf(party.ConvertAll(m => m.Health)), guards = NpcKingdom.PowerOf(k.GuardsOf(TownHall.Current != null ? home : hero.transform.position));
                        return $"{k.KingdomName}: {k.Population} people, hall {k.Economy.HallLevel}, power {ours:0} vs base {guards:0} x{GameBalance.KingdomRaidPowerRatio}, {(k.Raiding ? "raiding" : "home")}";
                    }));
                    report.AppendLine($"minute {Minute():0}: {state}; nearest raider {(nearest < float.MaxValue ? nearest.ToString("0") + " m" : "none")}");
                }
                yield return null;
            }
            report.AppendLine(firstAtPlayer.HasValue ? $"first at the player: minute {firstAtPlayer:0.0}" : $"no king's raid reached the player in {Minute():0} minutes");
            File.WriteAllText(Out("raids_report.md"), report.ToString());
            TestContext.WriteLine(report.ToString());
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator UnitsRoundTheHouse()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            var report = new StringBuilder();
            foreach (NpcResourceGatherer g in Object.FindObjectsByType<NpcResourceGatherer>(FindObjectsSortMode.None)) g.SetGatherDirective(GatherDirective.Idle);
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Vector3 place = hero.transform.position + new Vector3(24f, 0f, 14f);
            place.y = RtsUnit.GetTerrainHeight(place);
            GameObject builtHouse = BuildingNode.CreateWorkerHousePrefab("Probe_House");
            builtHouse.transform.SetPositionAndRotation(place, Quaternion.Euler(0f, 30f, 0f));
            BuildingNode house = builtHouse.GetComponent<BuildingNode>();
            yield return new WaitForSeconds(1f);
            RtsUnit knight = Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None).FirstOrDefault(u => u.GetComponent<NpcResourceGatherer>() != null && u.GetComponent<PlayerCombatController>() == null);
            Assert.IsNotNull(house, "a house at the start");
            Assert.IsNotNull(knight, "a unit at the start");
            Vector3 c = house.transform.position;
            report.AppendLine($"house at {c}, yaw {house.transform.eulerAngles.y:0}; solids within 8 m:");
            foreach (Collider col in Physics.OverlapSphere(c, 8f))
                if (!col.isTrigger && !(col is CharacterController) && col.GetComponent<ProceduralBiomeChunk>() == null)
                    report.AppendLine($"  {col.name} ({col.GetType().Name}) at {Flat(col.bounds.center, c):0.0} m, size {col.bounds.size}");

            // From all round the house to all round it: arrived, or how far short and for how long it stood
            Time.timeScale = 4f;
            int runs = 0, short_ = 0;
            for (int from = 0; from < 16; from++)
                for (int across = 4; across <= 12; across += 4)
                    foreach (float radiusTo in new[] { 3.2f, 8f })
                    {
                        float a = from * 22.5f * Mathf.Deg2Rad, b = a + across * 22.5f * Mathf.Deg2Rad;
                        Vector3 start = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 8f, to = c + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * radiusTo;
                        start.y = RtsUnit.GetTerrainHeight(start);
                        to.y = RtsUnit.GetTerrainHeight(to);
                        knight.transform.position = start;
                        knight.IssueMoveCommand(to);
                        float t0 = Time.time;
                        while (Time.time < t0 + 16f && knight.HasMoveTarget) yield return null;
                        runs++;
                        float left = Flat(knight.transform.position, to);
                        bool timedOut = knight.HasMoveTarget;
                        if (timedOut || left > 2.5f)
                        {
                            short_++;
                            report.AppendLine($"SHORT from {from * 22.5f:0} deg to {b * Mathf.Rad2Deg % 360f:0} deg r{radiusTo}: stands {Flat(knight.transform.position, c):0.0} m from the house centre, {left:0.0} m from the goal, still going {timedOut}");

                            // Can it leave at all?
                            Vector3 away = knight.transform.position + (knight.transform.position - c).normalized * 12f;
                            away.y = RtsUnit.GetTerrainHeight(away);
                            knight.IssueMoveCommand(away);
                            float t1 = Time.time;
                            while (Time.time < t1 + 8f && knight.HasMoveTarget) yield return null;
                            report.AppendLine($"  told to walk away: {Flat(knight.transform.position, away):0.0} m left, still going {knight.HasMoveTarget}");
                        }
                    }
            report.AppendLine($"{runs} walks, {short_} short");
            File.WriteAllText(Out("house_report.md"), report.ToString());
            TestContext.WriteLine(report.ToString());
        }
    }
}
