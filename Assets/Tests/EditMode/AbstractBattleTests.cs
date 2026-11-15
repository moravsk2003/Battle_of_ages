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
        public struct Real { public string A, B; public float LossA, LossB, Seconds; public bool Walled; }

        /// <summary>The played battles, repeats averaged (a third field, "walls": B behind a town's wall).</summary>
        public static List<Real> Played()
        {
            var sums = new Dictionary<(string, string, bool), (float a, float b, float t, int n)>();
            string[] lines = File.ReadAllLines(Path.Combine(Application.dataPath, "Tests/EditMode/AbstractBattleData.csv"));
            for (int i = 1; i < lines.Length; i++)
            {
                string[] c = lines[i].Split(';');
                if (c.Length < 7) continue;
                bool walled = c.Length > 7 && c[7] == "walls";
                sums.TryGetValue((c[0], c[1], walled), out var s);
                sums[(c[0], c[1], walled)] = (s.a + float.Parse(c[2], CultureInfo.InvariantCulture), s.b + float.Parse(c[3], CultureInfo.InvariantCulture),
                                              s.t + float.Parse(c[6], CultureInfo.InvariantCulture), s.n + 1);
            }
            var list = new List<Real>();
            foreach (var pair in sums)
                list.Add(new Real { A = pair.Key.Item1, B = pair.Key.Item2, Walled = pair.Key.Item3, LossA = pair.Value.a / pair.Value.n, LossB = pair.Value.b / pair.Value.n, Seconds = pair.Value.t / pair.Value.n });
            return list;
        }

        /// <summary>How far the abstract battle is from the real ones: mean error of losses, the winners right of those decided, mean relative error of the length.</summary>
        public struct Score { public float LossError, LengthError; public int Right, Decided; }

        public static Score Measure(List<Real> played, System.Text.StringBuilder report = null)
        {
            var score = new Score();
            foreach (Real r in played)
            {
                AbstractBattle.Outcome o = AbstractBattle.Resolve(AbstractBattle.Parse(r.A), AbstractBattle.Parse(r.B), new AbstractBattle.Terms { BWalled = r.Walled, RetreatA = 1f, RetreatB = 1f });
                score.LossError += Mathf.Abs(o.LossA - r.LossA) + Mathf.Abs(o.LossB - r.LossB);
                score.LengthError += Mathf.Abs(o.Seconds - r.Seconds) / Mathf.Max(1f, r.Seconds);
                report?.AppendLine($"{r.A} : {r.B}{(r.Walled ? " (walls)" : "")}  real {r.LossA:0.00} {r.LossB:0.00} {r.Seconds:0} s, calc {o.LossA:0.00} {o.LossB:0.00} {o.Seconds:0} s");
                if (Mathf.Abs(r.LossA - r.LossB) <= 0.15f) continue; // (near-even fights go either way in the real ones too)
                score.Decided++;
                if ((o.LossA > o.LossB) == (r.LossA > r.LossB)) score.Right++;
            }
            score.LossError /= played.Count * 2;
            score.LengthError /= played.Count;
            return score;
        }

        [Test]
        public void ItsLosses_AreWithinTenPercentOfRealBattles_AndTheWinnerTheSame()
        {
            List<Real> played = Played();
            Assert.Greater(played.Count, 30, "the battles are there (open, archers, heroes, walls)");
            var report = new System.Text.StringBuilder();
            Score s = Measure(played, report);
            TestContext.WriteLine($"loss error {s.LossError:0.000}, winner {s.Right} of {s.Decided}, length error {s.LengthError:0.000}\n{report}");
            Assert.LessOrEqual(s.LossError, 0.10f, "the mean error of losses (§6.2)\n" + report);
            Assert.GreaterOrEqual(s.Right, Mathf.CeilToInt(s.Decided * 0.9f), $"the winner: {s.Right} of {s.Decided}\n" + report);
            // The length: 33% off before the closing in, archers and heroes were counted, 28% now. Even fights are most of what is
            // left: the real ones tip over by chance and end sooner, the abstract one, even to the end, takes both down slowly
            Assert.LessOrEqual(s.LengthError, 0.30f, "the length of a battle\n" + report);
        }

        [Test]
        public void ASideBehindAWall_FightsAtItsWays_AndAnArmyGivesUpAtItsLosses()
        {
            List<UnitGroup> many = AbstractBattle.Parse("Warrior.1x60"), few = AbstractBattle.Parse("Warrior.1x20");
            AbstractBattle.Outcome open = AbstractBattle.Resolve(many, few);
            AbstractBattle.Outcome walled = AbstractBattle.Resolve(many, few, new AbstractBattle.Terms { BWalled = true, RetreatA = 1f, RetreatB = 1f });
            Assert.Greater(walled.LossA, open.LossA, "storming a wall costs more");
            AbstractBattle.Outcome retreat = AbstractBattle.Resolve(few, many, new AbstractBattle.Terms { RetreatA = 0.5f, RetreatB = 1f });
            Assert.AreEqual(1, retreat.Winner);
            Assert.IsTrue(retreat.Retreated, "it gave up");
            Assert.Less(retreat.LossA, 0.9f, "and kept some of its people");
        }

        [Test]
        public void ArchersShoot_FromTheStart_AndAHeroCountsWithItsClass()
        {
            // Archers strike while the sides close in: in a quick look at the first seconds they have done harm already
            float before = GameBalance.AbstractApproachSeconds;
            try
            {
                GameBalance.AbstractApproachSeconds = 600f; // the swords never meet
                AbstractBattle.Outcome bows = AbstractBattle.Resolve(AbstractBattle.Parse("Warrior.1x20@bow"), AbstractBattle.Parse("Warrior.1x20"), 30f);
                Assert.Greater(bows.LossB, 0.05f, "the arrows land");
                Assert.AreEqual(0f, bows.LossA, 1e-4f, "swords do not reach");
            }
            finally { GameBalance.AbstractApproachSeconds = before; }
            UnitStats tank = UnitStats.Of(UnitGroup.Hero(UnitRank.General, 2, HeroClass.Tank)), plain = UnitStats.Of(UnitRank.General, 2);
            Assert.Greater(tank.Health, plain.Health * 2f, "a hero's doubled health, and a tank's more");
            Assert.IsTrue(UnitStats.Of(UnitGroup.Hero(UnitRank.General, 2, HeroClass.Mage)).PiercesArmour);
            Assert.Less(UnitStats.Of(UnitGroup.Hero(UnitRank.General, 2, HeroClass.Dd)).Cooldown, plain.Cooldown, "a DD strikes more often");
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
