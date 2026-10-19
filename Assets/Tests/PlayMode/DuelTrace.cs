using System;
using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Combat;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Debug tool: one hero duel second by second (distance, HP, bolts in flight). Writes duel_trace.md.</summary>
    [Explicit("Debug tool, run on demand")]
    public class DuelTrace
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator MageAgainstTank()
        {
            LogAssert.ignoreFailingMessages = true;
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            var log = new StringBuilder("# Дуель з покроковим записом\n");
            foreach ((HeroClass a, HeroClass b) in new[] { (HeroClass.Mage, HeroClass.Tank), (HeroClass.Tank, HeroClass.Mage) })
            {
                log.AppendLine($"\n## {HeroClassRules.Name(a)} (сторона 0) проти {HeroClassRules.Name(b)} (сторона 1)\n");
                log.AppendLine("| с | відстань | HP 0 | HP 1 | снарядів |\n|---|---|---|---|---|");
                var sim = new BattleSim();
                BattleSim.Unit u0 = sim.Add(0, UnitRank.General, true, a, kite: a == HeroClass.Mage);
                BattleSim.Unit u1 = sim.Add(1, UnitRank.General, true, b, kite: b == HeroClass.Mage);
                Time.timeScale = 10f;
                Time.maximumDeltaTime = 0.1f;
                float next = 0f;
                BattleSim.Result r = default;
                IEnumerator run = sim.Run(120f, x => r = x);
                float t = 0f;
                while (run.MoveNext())
                {
                    if (t >= next && u0.Hp != null && u1.Hp != null)
                    {
                        next += 2f;
                        float d = Vector3.Distance(u0.Combat.transform.position, u1.Combat.transform.position);
                        int bolts = UnityEngine.Object.FindObjectsByType<MageBolt>(FindObjectsSortMode.None).Length;
                        log.AppendLine($"| {t:0} | {d:0.0} | {u0.Hp.CurrentHealth} | {u1.Hp.CurrentHealth} | {bolts} |");
                    }
                    t += Time.deltaTime;
                    yield return run.Current;
                }
                log.AppendLine($"\nПереможець: {(r.Winner < 0 ? "нічия" : "сторона " + r.Winner)}, {r.Seconds:0} с.");
            }
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 1f / 3f;
            File.WriteAllText(Path.Combine(dir, "duel_trace.md"), log.ToString(), new UTF8Encoding(true));
        }
    }
}
