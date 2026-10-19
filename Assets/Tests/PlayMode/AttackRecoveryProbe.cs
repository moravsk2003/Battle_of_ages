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
    /// Balance tool (not a regular test): how much hit-and-run a hero gets away with for each attack recovery and
    /// wind-up (docs/GAME_DESIGN.md §11.1). A hero general fights a pack on open ground (BattleSim): the archer and the
    /// mage shoot and back off, the swordsman strikes and backs off until its next swing, compared with standing and
    /// fighting. Writes attack_recovery.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Balance tool, run on demand")]
    public class AttackRecoveryProbe
    {
        private const float MaxSeconds = 120f;
        private readonly BattleSim sim = new BattleSim();
        private readonly StringBuilder report = new StringBuilder();

        [UnityTest]
        public IEnumerator PlayHitAndRun()
        {
            LogAssert.ignoreFailingMessages = true;
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            float rootWas = GameBalance.RangedRootSeconds, slowWas = GameBalance.MeleeSlowSeconds, windupWas = GameBalance.MeleeWindup;
            Time.timeScale = 10f;
            Time.maximumDeltaTime = 0.1f; // small steps at the higher speed

            report.AppendLine("# Відновлення після атаки — симуляція «вдарив — відійшов»\n");
            report.AppendLine("Герой-генерал (×2) з мечем I (Лучник — з луком I), без очок і артефактів; вороги без спорядження.\n");

            Header("Стрільці: зупинка після пострілу", "зупинка, с");
            foreach (HeroClass c in new[] { HeroClass.Archer, HeroClass.Mage })
                foreach (float root in new[] { 0f, 0.5f, 1f, 2f, 3f })
                {
                    GameBalance.RangedRootSeconds = root;
                    yield return Fight(c, true, UnitRank.General, 3, $"{HeroClassRules.Name(c)} проти 3 генералів", root);
                }
            GameBalance.RangedRootSeconds = rootWas;

            Header("Мечник: сповільнення 50% після удару", "сповільнення, с");
            foreach ((UnitRank rank, int count) in new[] { (UnitRank.General, 2), (UnitRank.Warrior, 6) })
            {
                string pack = Pack(rank, count);
                GameBalance.MeleeSlowSeconds = 0f;
                yield return Fight(HeroClass.None, false, rank, count, $"стоїть і рубається проти {pack}", 0f);
                foreach (float slow in new[] { 0f, 1f, 2f })
                {
                    GameBalance.MeleeSlowSeconds = slow;
                    yield return Fight(HeroClass.None, true, rank, count, $"вдарив — відійшов проти {pack}", slow);
                }
            }
            GameBalance.MeleeSlowSeconds = slowWas;

            Header("Замах перед ударом (мечник; сповільнення 1 с)", "замах, с");
            foreach ((UnitRank rank, int count, bool kite) in new[] { (UnitRank.Warrior, 6, false), (UnitRank.Warrior, 12, false), (UnitRank.General, 2, false), (UnitRank.General, 2, true) })
                foreach (float w in new[] { 0f, 0.2f, 0.35f, 0.5f })
                {
                    GameBalance.MeleeWindup = w;
                    yield return Fight(HeroClass.None, kite, rank, count, $"{(kite ? "вдарив — відійшов" : "стоїть і рубається")} проти {Pack(rank, count)}", w);
                }
            GameBalance.MeleeWindup = windupWas;

            Time.timeScale = 1f;
            Time.maximumDeltaTime = 1f / 3f;
            File.WriteAllText(Path.Combine(dir, "attack_recovery.md"), report.ToString(), new UTF8Encoding(true));
        }

        private void Header(string title, string column)
        {
            report.AppendLine($"\n## {title}\n");
            report.AppendLine($"| Сценарій | {column} | переміг | HP героя втрачено | убито | час, с |\n|---|---|---|---|---|---|");
        }

        private static string Pack(UnitRank rank, int count) => rank == UnitRank.General ? $"{count} генералів" : $"{count} воїнів";

        private IEnumerator Fight(HeroClass cls, bool kite, UnitRank enemyRank, int count, string name, float value)
        {
            sim.Add(0, UnitRank.General, hero: true, cls, kite);
            for (int i = 0; i < count; i++) sim.Add(1, enemyRank);
            BattleSim.Result r = default;
            yield return sim.Run(MaxSeconds, x => r = x);
            report.AppendLine($"| {name} | {value:0.##} | {(r.Winner == 0 ? "так" : "ні")} | {(1f - r.HpLeft[0]) * 100f:0}% | {r.Count[1] - r.Alive[1]}/{r.Count[1]} | {r.Seconds:0} |");
        }
    }
}
