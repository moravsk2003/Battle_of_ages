using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// A whole M0 session played by bots (docs/GAME_DESIGN.md §18, §19 p.8): a kingdom on the player's side (the same rules
    /// and AI as the NPC kings, its king a general hero like the player) against KingdomCount NPC kings around it, for up to
    /// SIM_HOURS game hours (default 60 = a one-hour session). Writes session_report.md to CAPTURE_DIR: growth, raids, kings'
    /// deaths, falls. SIM_SPEED (default 20) is the time scale, SIM_CLASS the player king's class, SIM_SEED the random seed.
    /// </summary>
    [Explicit("Balance probe, run on demand")]
    public class SessionSim
    {
        private static readonly Vector3 Centre = new Vector3(5000f, 0f, 5000f);
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            NpcKingdom.RemoveAll();
            foreach (HostileMobAI m in UnityEngine.Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None)) UnityEngine.Object.Destroy(m.gameObject);
            foreach (GameObject go in spawned) if (go != null) UnityEngine.Object.Destroy(go);
        }

        private static string Env(string name, string fallback) { string v = Environment.GetEnvironmentVariable(name); return string.IsNullOrEmpty(v) ? fallback : v; }

        private static string Line(NpcKingdom k)
        {
            if (k.IsDefeated) return "впало";
            int soldiers = 0;
            foreach (HostileMobAI m in k.Subjects) if (m.GetComponent<UnitProgress>().Rank != UnitRank.Person) soldiers++;
            HeroProgress hero = k.King != null && k.King.Health.IsAlive ? k.King.GetComponent<HeroProgress>() : null;
            string king = hero != null ? $"король {hero.Class} рів. {hero.Level}" : k.InInterregnum ? "міжцар'я" : "без короля";
            return $"{k.Population} / {k.Economy.HallLevel} / {soldiers} ({king}{(k.Raiding ? ", у набігу" : "")})";
        }

        [UnityTest, Timeout(7200000)]
        public IEnumerator PlaySession()
        {
            float hours = float.Parse(Env("SIM_HOURS", "60"), System.Globalization.CultureInfo.InvariantCulture);
            float speed = float.Parse(Env("SIM_SPEED", "20"), System.Globalization.CultureInfo.InvariantCulture);
            var cls = (HeroClass)Enum.Parse(typeof(HeroClass), Env("SIM_CLASS", "Archer"));
            UnityEngine.Random.InitState(int.Parse(Env("SIM_SEED", "1")));
            string dir = Env("CAPTURE_DIR", System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "session"));
            System.IO.Directory.CreateDirectory(dir);

            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Centre.x, -0.5f, Centre.z);
            box.size = new Vector3(4000f, 1f, 4000f);
            spawned.Add(ground);

            NpcKingdom player = NpcKingdom.Found(Centre, "Гравець", kingClass: cls, team: UnitSpatialGrid.TeamPlayer);
            var kings = new List<NpcKingdom>();
            for (int i = 0; i < GameBalance.KingdomCount; i++)
            {
                float a = (i * 360f / GameBalance.KingdomCount + 30f) * Mathf.Deg2Rad;
                kings.Add(NpcKingdom.Found(Centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * GameBalance.KingdomDistance, $"Король {i + 1}"));
            }
            var all = new List<NpcKingdom>(kings) { player };

            var log = new StringBuilder();
            var events = new StringBuilder();
            float Now() => player.Age;
            Action<NpcKingdom, HealthComponent> onRaid = (k, t) => events.AppendLine($"- {Now():0.0} год: **{k.KingdomName}** іде в набіг ({k.Raiders.Count}) на {t.name}");
            Action<NpcKingdom> onFall = k => events.AppendLine($"- {Now():0.0} год: **{k.KingdomName} впало**");
            NpcKingdom.RaidStarted += onRaid;
            NpcKingdom.Defeated += onFall;
            var lastKing = new Dictionary<NpcKingdom, HostileMobAI>();
            var raidWas = new Dictionary<NpcKingdom, bool>();
            foreach (NpcKingdom k in all) { lastKing[k] = k.King; raidWas[k] = false; }

            Time.timeScale = speed;
            float nextSnapshot = 0f;
            float realStart = Time.realtimeSinceStartup;
            while (player.Age < hours && !player.IsDefeated && !kings.TrueForAll(k => k.IsDefeated))
            {
                foreach (NpcKingdom k in all)
                {
                    if (k.King != lastKing[k])
                    {
                        if (!ReferenceEquals(lastKing[k], null)) events.AppendLine($"- {Now():0.0} год: король **{k.KingdomName}** загинув");
                        lastKing[k] = k.King;
                    }
                    if (raidWas[k] && !k.Raiding) events.AppendLine($"- {Now():0.0} год: набіг {k.KingdomName} закінчився, вдома {k.Population}");
                    raidWas[k] = k.Raiding;
                }
                if (player.Age >= nextSnapshot)
                {
                    log.Append($"| {nextSnapshot:0} |");
                    foreach (NpcKingdom k in all) log.Append($" {Line(k)} |");
                    log.AppendLine();
                    nextSnapshot += 5f;
                }
                yield return null;
            }
            NpcKingdom.RaidStarted -= onRaid;
            NpcKingdom.Defeated -= onFall;

            var report = new StringBuilder();
            report.AppendLine("# Сесія M0 ботами");
            report.AppendLine();
            report.AppendLine($"Гравець — таке саме королівство під ботом (король-генерал {cls}), проти {kings.Count} NPC-королів за {GameBalance.KingdomDistance:0} м. " +
                              $"Зіграно {player.Age:0.0} ігрових год ({(Time.realtimeSinceStartup - realStart) / 60f:0.0} хв реального часу при ×{speed}).");
            string end = player.IsDefeated ? "**гравець програв**" : kings.TrueForAll(k => k.IsDefeated) ? "**гравець переміг усіх**"
                : $"нічия за часом, впало сусідів: {kings.FindAll(k => k.IsDefeated).Count}";
            report.AppendLine($"Підсумок: {end}.");
            report.AppendLine();
            report.AppendLine("Формат: люди / ратуша / солдати (король).");
            report.AppendLine();
            report.Append("| год |");
            foreach (NpcKingdom k in all) report.Append($" {k.KingdomName} |");
            report.AppendLine();
            report.Append("|---|");
            foreach (NpcKingdom k in all) report.Append("---|");
            report.AppendLine();
            report.Append(log);
            report.Append($"| кінець |");
            foreach (NpcKingdom k in all) report.Append($" {Line(k)} |");
            report.AppendLine();
            report.AppendLine();
            report.AppendLine("## Події");
            report.AppendLine();
            report.Append(events.Length > 0 ? events.ToString() : "- нічого\n");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "session_report.md"), report.ToString());
            Debug.Log(report.ToString());
            Assert.Pass(end);
        }
    }
}
