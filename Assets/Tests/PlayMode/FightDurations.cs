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
    /// <summary>
    /// Balance tool: how long typical fights last and how they end (a hero general against packs, BattleSim).
    /// "≥ 300" in the balance table can mean the hero only survived until the time ran out, so the end is shown here.
    /// Writes fight_durations.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Balance tool, run on demand")]
    public class FightDurations
    {
        [UnityTest, Timeout(3600000)]
        public IEnumerator Measure()
        {
            LogAssert.ignoreFailingMessages = true;
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Time.timeScale = 50f;
            Time.maximumDeltaTime = 0.1f;
            var report = new StringBuilder("# Скільки тривають бої (герой-генерал проти загону, без спорядження)\n\n");
            report.AppendLine("| Герой | проти | кінець | убито | HP героя лишилось | тривалість, ігрових с |\n|---|---|---|---|---|---|");
            var sim = new BattleSim();
            var cases = new (HeroClass cls, UnitRank rank, int count)[]
            {
                (HeroClass.None, UnitRank.Warrior, 10), (HeroClass.Tank, UnitRank.Warrior, 10), (HeroClass.Dd, UnitRank.Warrior, 10),
                (HeroClass.Archer, UnitRank.Warrior, 10), (HeroClass.Mage, UnitRank.Warrior, 10),
                (HeroClass.Archer, UnitRank.Person, 300), (HeroClass.Mage, UnitRank.Person, 300), (HeroClass.Mage, UnitRank.Warrior, 300),
                (HeroClass.Archer, UnitRank.Warrior, 100), (HeroClass.None, UnitRank.General, 2), (HeroClass.Archer, UnitRank.General, 3),
                (HeroClass.Mage, UnitRank.General, 3),
            };
            foreach (var c in cases)
            {
                sim.Add(0, UnitRank.General, hero: true, c.cls, kite: c.cls == HeroClass.Archer || c.cls == HeroClass.Mage);
                for (int i = 0; i < c.count; i++) sim.Add(1, c.rank);
                BattleSim.Result r = default;
                yield return sim.Run(1800f, x => r = x);
                string end = r.Winner == 0 ? "герой переміг" : r.Winner == 1 ? "герой загинув" : r.Alive[0] == 0 ? "обидва загинули" : "вийшов час (30 хв)";
                report.AppendLine($"| {HeroClassRules.Name(c.cls)} | {c.count} × {UI.HeroUI.RankName(c.rank)} | {end} | {r.Count[1] - r.Alive[1]}/{r.Count[1]} | {r.HpLeft[0] * 100f:0}% | {r.Seconds:0} |");
            }
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 1f / 3f;
            File.WriteAllText(Path.Combine(dir, "fight_durations.md"), report.ToString(), new UTF8Encoding(true));
        }
    }
}
