using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;

namespace BattleOfAges.Tests
{
    /// <summary>The abstract battle against the real ones (docs/GAME_DESIGN.md §6.2): AbstractBattleData.csv is written by AbstractBattleCalibration; play it again after a change of the combat balance.</summary>
    [TestFixture]
    public class AbstractBattleTests
    {
        private struct Real { public string A, B; public float LossA, LossB; }

        /// <summary>The played battles, repeats averaged.</summary>
        private static List<Real> Played()
        {
            var sums = new Dictionary<(string, string), (float a, float b, int n)>();
            string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "Tests/EditMode/AbstractBattleData.csv"));
            for (int i = 1; i < lines.Length; i++)
            {
                string[] c = lines[i].Split(';');
                if (c.Length < 4) continue;
                sums.TryGetValue((c[0], c[1]), out var s);
                sums[(c[0], c[1])] = (s.a + float.Parse(c[2], CultureInfo.InvariantCulture), s.b + float.Parse(c[3], CultureInfo.InvariantCulture), s.n + 1);
            }
            var list = new List<Real>();
            foreach (var pair in sums) list.Add(new Real { A = pair.Key.Item1, B = pair.Key.Item2, LossA = pair.Value.a / pair.Value.n, LossB = pair.Value.b / pair.Value.n });
            return list;
        }

        [Test]
        public void ItsLosses_AreWithinTenPercentOfRealBattles_AndTheWinnerTheSame()
        {
            List<Real> played = Played();
            Assert.Greater(played.Count, 20, "the battles are there");
            float error = 0f;
            int decided = 0, right = 0;
            var report = new System.Text.StringBuilder();
            foreach (Real r in played)
            {
                AbstractBattle.Outcome o = AbstractBattle.Resolve(AbstractBattle.Parse(r.A), AbstractBattle.Parse(r.B));
                error += Mathf.Abs(o.LossA - r.LossA) + Mathf.Abs(o.LossB - r.LossB);
                report.AppendLine($"{r.A} : {r.B}  real {r.LossA:0.00} {r.LossB:0.00}, calc {o.LossA:0.00} {o.LossB:0.00}");
                if (Mathf.Abs(r.LossA - r.LossB) <= 0.15f) continue; // (near-even fights go either way in the real ones too)
                decided++;
                if ((o.LossA > o.LossB) == (r.LossA > r.LossB)) right++;
            }
            error /= played.Count * 2;
            Assert.LessOrEqual(error, 0.10f, "the mean error of losses (§6.2)\n" + report);
            Assert.GreaterOrEqual(right, Mathf.CeilToInt(decided * 0.9f), $"the winner: {right} of {decided}\n" + report);
        }

        [Test]
        public void MoreUnits_BetterGear_HigherRank_Win()
        {
            AbstractBattle.Outcome more = AbstractBattle.Resolve(AbstractBattle.Parse("Warrior.1x30"), AbstractBattle.Parse("Warrior.1x20"));
            Assert.AreEqual(0, more.Winner);
            Assert.Less(more.LossA, 0.6f, "and at a price under the square law");
            Assert.AreEqual(0, AbstractBattle.Resolve(AbstractBattle.Parse("Warrior.4x20"), AbstractBattle.Parse("Warrior.1x20")).Winner, "gear");
            Assert.AreEqual(0, AbstractBattle.Resolve(AbstractBattle.Parse("Knight.1x10"), AbstractBattle.Parse("Warrior.1x10")).Winner, "rank");
            Assert.AreEqual(1, AbstractBattle.Resolve(AbstractBattle.Parse("Person.1x10"), AbstractBattle.Parse("Knight.1x10")).Winner);
        }

        [Test]
        public void HurtUnits_FightWorse_AndNothingIsChanged()
        {
            var whole = new List<UnitGroup> { new UnitGroup(UnitRank.Warrior, 1, 20) };
            var hurt = new List<UnitGroup> { new UnitGroup(UnitRank.Warrior, 1, 20, 0.5f) };
            Assert.AreEqual(0, AbstractBattle.Resolve(whole, hurt).Winner);
            Assert.AreEqual(20, whole[0].Count, "the groups as they were");
        }

        [Test]
        public void TheNumbers_FollowRankAndGear()
        {
            Assert.AreEqual(GameBalance.RankHealth[(int)UnitRank.Person], UnitStats.Of(UnitRank.Person, 3).Health, 1e-3f, "a person's axe gives no health");
            Assert.Greater(UnitStats.Of(UnitRank.Warrior, 4).Health, UnitStats.Of(UnitRank.Warrior, 1).Health, "armour gives health");
            Assert.Greater(UnitStats.Of(UnitRank.Knight, 1).Dps, UnitStats.Of(UnitRank.Warrior, 1).Dps);
            Assert.Greater(UnitStats.Of(UnitRank.Warrior, 4).Reduction, UnitStats.Of(UnitRank.Warrior, 1).Reduction);
        }
    }
}
