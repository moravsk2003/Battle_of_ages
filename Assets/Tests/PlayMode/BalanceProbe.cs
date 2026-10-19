using System;
using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Combat;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Balance tool (not a regular test), docs/GAME_DESIGN.md §8.1, §11.1. In real fights (BattleSim):
    /// 1. ranks: how many of a weaker rank it takes to beat one of a stronger one (no gear);
    /// 2. hero classes one on one: every class against every class (hero generals, sword I / archer bow I);
    /// 3. a hero of each class against every rank: the biggest pack it still beats.
    /// Writes balance_report.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Balance tool, run on demand")]
    public class BalanceProbe
    {
        private const float MaxSeconds = 600f;
        private const int MaxPack = 40;
        private const int MaxHorde = 300; // a hero against weak ranks
        private static readonly UnitRank[] Ranks = { UnitRank.Person, UnitRank.Warrior, UnitRank.Knight, UnitRank.General };
        private static readonly HeroClass[] Classes = { HeroClass.None, HeroClass.Tank, HeroClass.Archer, HeroClass.Mage, HeroClass.Dd };
        private readonly BattleSim sim = new BattleSim();
        private readonly StringBuilder report = new StringBuilder();

        [UnityTest, Timeout(3600000)] // a long run of fights
        public IEnumerator PlayBalance()
        {
            LogAssert.ignoreFailingMessages = true;
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            Time.timeScale = 50f; // steps stay small: maximumDeltaTime
            Time.maximumDeltaTime = 0.1f;

            report.AppendLine("# Баланс: ранги й класи в бою\n");
            report.AppendLine($"Замах {GameBalance.MeleeWindup} с (лук {GameBalance.BowWindup}, маг {GameBalance.MageWindup}), " +
                              $"сповільнення {GameBalance.MeleeSlowSeconds} с, зупинка стрільців {GameBalance.RangedRootSeconds} с. " +
                              "Відкрите поле, усі можуть дістати ціль (без строю й тісноти).\n");

            // 1. ranks ---------------------------------------------------------------------------------------------
            report.AppendLine("## 1. Скільки слабших перемагають одного сильнішого (без спорядження)\n");
            report.AppendLine("| Слабші | проти 1 | потрібно, шт | HP переможців лишилось | для порівняння: бойова вага (HP × урон) |\n|---|---|---|---|---|");
            for (int s = 1; s < Ranks.Length; s++)
                for (int w = 0; w < s; w++)
                {
                    int need = -1; float left = 0f;
                    UnitRank weak = Ranks[w], strong = Ranks[s];
                    yield return Smallest(n => Fight(n, weak, 1, strong), (n, r) => { need = n; left = r.HpLeft[0]; });
                    float weight = (float)(UnitRanks.Health(Ranks[s]) * UnitRanks.Strength(Ranks[s])) /
                                   (UnitRanks.Health(Ranks[w]) * UnitRanks.Strength(Ranks[w]));
                    report.AppendLine($"| {HeroUI.RankName(Ranks[w])} | {HeroUI.RankName(Ranks[s])} | {(need > 0 ? need.ToString() : $"> {MaxPack}")} | {left * 100f:0}% | ×{weight:0.#} (Ланчестер √: {Mathf.Sqrt(weight):0.#}) |");
                }

            // 2. classes one on one ---------------------------------------------------------------------------------
            report.AppendLine("\n## 2. Класи героїв 1 на 1 (герої-генерали)\n");
            var header = new StringBuilder("| Рядок проти стовпця |");
            var line = new StringBuilder("|---|");
            foreach (HeroClass c in Classes) { header.Append($" {HeroClassRules.Name(c)} |"); line.Append("---|"); }
            report.AppendLine(header.ToString()).AppendLine(line.ToString());
            foreach (HeroClass a in Classes)
            {
                var row = new StringBuilder($"| **{HeroClassRules.Name(a)}** |");
                foreach (HeroClass b in Classes)
                {
                    if (a == b) { row.Append(" — |"); continue; }
                    AddHero(0, a);
                    AddHero(1, b);
                    BattleSim.Result r = default;
                    yield return sim.Run(MaxSeconds, x => r = x);
                    row.Append(r.Winner == 0 ? $" ✅ {r.HpLeft[0] * 100f:0}% |" : r.Winner == 1 ? $" ❌ |" : " нічия |");
                }
                report.AppendLine(row.ToString());
            }
            report.AppendLine("\n✅ N% — рядок переміг, у нього лишилось N% HP; ❌ — програв.");

            // 3. a hero against every rank --------------------------------------------------------------------------
            report.AppendLine("\n## 3. Скількох юнітів кожного рангу (без спорядження) перемагає герой-генерал\n");
            var head3 = new StringBuilder("| Клас |");
            var line3 = new StringBuilder("|---|");
            foreach (UnitRank rank in Ranks) { head3.Append($" {HeroUI.RankName(rank)} |"); line3.Append("---|"); }
            report.AppendLine(head3.ToString()).AppendLine(line3.ToString());
            foreach (HeroClass c in Classes)
            {
                var row = new StringBuilder($"| {HeroClassRules.Name(c)} |");
                foreach (UnitRank rank in Ranks)
                {
                    HeroClass cls = c; UnitRank enemy = rank;
                    // The smallest pack that beats the hero; the hero beats one fewer
                    int beaten = -1;
                    yield return Smallest(k => HeroFight(cls, k, enemy), (k, r) => beaten = k, sideWins: 1, max: MaxHorde);
                    int best = beaten > 0 ? beaten - 1 : MaxHorde;
                    float left = 0f;
                    if (best > 0) { BattleSim.Result r = default; yield return HeroFight(cls, best, enemy, x => r = x); left = r.HpLeft[0]; }
                    row.Append(beaten > 0 ? $" {best} ({left * 100f:0}% HP) |" : $" ≥ {MaxHorde} |");
                }
                report.AppendLine(row.ToString());
            }
            report.AppendLine("\nN (M% HP) — герой перемагає N юнітів цього рангу, у нього лишається M% HP; N + 1 його вбивають.");

            Time.timeScale = 1f;
            Time.maximumDeltaTime = 1f / 3f;
            File.WriteAllText(Path.Combine(dir, "balance_report.md"), report.ToString(), new UTF8Encoding(true));
        }

        // ------------------------------------------------------------------ fights and the search

        /// <summary>n units of rank a (side 0) against m of rank b (side 1).</summary>
        private IEnumerator Fight(int n, UnitRank a, int m, UnitRank b, Action<BattleSim.Result> done = null)
        {
            for (int i = 0; i < n; i++) sim.Add(0, a);
            for (int i = 0; i < m; i++) sim.Add(1, b);
            BattleSim.Result r = default;
            yield return sim.Run(MaxSeconds, x => r = x);
            last = r;
            done?.Invoke(r);
        }

        /// <summary>A hero of the class (side 0) against n units of a rank (side 1).</summary>
        private IEnumerator HeroFight(HeroClass c, int n, UnitRank enemy, Action<BattleSim.Result> done = null)
        {
            AddHero(0, c);
            for (int i = 0; i < n; i++) sim.Add(1, enemy);
            BattleSim.Result r = default;
            yield return sim.Run(MaxSeconds, x => r = x);
            last = r;
            done?.Invoke(r);
        }

        private BattleSim.Result last;

        /// <summary>
        /// The smallest n (1..MaxPack) for which the fight is won by sideWins: doubling, then halving the gap
        /// (a few fights instead of one per n). Calls found(n, result of that fight), or nothing if none wins.
        /// </summary>
        private IEnumerator Smallest(Func<int, IEnumerator> fight, Action<int, BattleSim.Result> found, int sideWins = 0, int max = MaxPack)
        {
            int lo = 0, hi = 1;
            BattleSim.Result hiResult = default;
            while (true)
            {
                yield return fight(hi);
                if (last.Winner == sideWins) { hiResult = last; break; }
                lo = hi;
                if (hi >= max) yield break;
                hi = Mathf.Min(hi * 2, max);
            }
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                yield return fight(mid);
                if (last.Winner == sideWins) { hi = mid; hiResult = last; } else lo = mid;
            }
            found(hi, hiResult);
        }

        /// <summary>Hero general of the class; the archer and the mage kite, the rest stand and fight.</summary>
        private void AddHero(int side, HeroClass c) =>
            sim.Add(side, UnitRank.General, hero: true, c, kite: c == HeroClass.Archer || c == HeroClass.Mage);
    }
}
