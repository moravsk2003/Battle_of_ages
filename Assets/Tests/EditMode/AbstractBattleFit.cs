using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.Tests
{
    /// <summary>
    /// Fits the abstract battle's coefficients (GameBalance.Abstract*) to the real battles of AbstractBattleData.csv (not a
    /// regular test): one coefficient at a time over its candidates, the others held, a few rounds (they hardly depend on each
    /// other: the gate only matters behind walls, the hero's skills only with heroes). Mostly the losses, a little the length.
    /// Prints the best; GameBalance's defaults are then set to it by hand.
    /// </summary>
    [Explicit("Fits coefficients, run on demand after AbstractBattleCalibration")]
    public class AbstractBattleFit
    {
        private static readonly (string field, float[] values)[] Candidates =
        {
            ("AbstractFrontBase", new[] { 0f, 1f, 2f, 4f, 6f }),
            ("AbstractFrontPerRoot", new[] { 2f, 3f, 4f, 5f, 6f, 7f, 8f, 10f }),
            ("AbstractDamageShare", new[] { 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f }),
            ("AbstractRangedShare", new[] { 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 1f }),
            ("AbstractApproachSeconds", new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 8f, 10f }),
            ("AbstractGateFront", new[] { 2f, 4f, 6f, 8f, 12f, 16f, 24f, 32f, 48f }),
            ("AbstractHeroSkillFactor", new[] { 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 1f, 1.2f, 1.5f }),
            ("AbstractLastUnits", new[] { 0.5f, 1f, 1.5f, 2f, 3f, 4f }),
        };

        private static float Cost(AbstractBattleTests.Score s) => s.LossError + 0.05f * s.LengthError + 0.02f * (s.Decided - s.Right);

        [Test]
        public void FitTheCoefficients()
        {
            List<AbstractBattleTests.Real> played = AbstractBattleTests.Played();
            var fields = new Dictionary<string, FieldInfo>();
            foreach (var c in Candidates) fields[c.field] = typeof(GameBalance).GetField(c.field, BindingFlags.Public | BindingFlags.Static);
            var start = new Dictionary<string, float>();
            foreach (var c in Candidates) start[c.field] = (float)fields[c.field].GetValue(null);
            float best = Cost(AbstractBattleTests.Measure(played));
            var report = new System.Text.StringBuilder($"start: {Describe(fields)} cost {best:0.0000}\n");
            try
            {
                for (int round = 0; round < 3; round++)
                    foreach (var c in Candidates)
                    {
                        FieldInfo f = fields[c.field];
                        float keep = (float)f.GetValue(null);
                        foreach (float v in c.values)
                        {
                            f.SetValue(null, v);
                            float cost = Cost(AbstractBattleTests.Measure(played));
                            if (cost < best - 1e-5f) { best = cost; keep = v; }
                        }
                        f.SetValue(null, keep);
                    }
                AbstractBattleTests.Score s = AbstractBattleTests.Measure(played);
                report.AppendLine($"best: {Describe(fields)}");
                report.AppendLine($"loss error {s.LossError:0.000}, winner {s.Right} of {s.Decided}, length error {s.LengthError:0.000}");
                Debug.Log("[AbstractBattleFit] " + report);
                TestContext.WriteLine(report.ToString());
                string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
                if (!string.IsNullOrEmpty(dir)) { System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "fit.txt"), report.ToString()); }
            }
            finally { foreach (var c in Candidates) fields[c.field].SetValue(null, start[c.field]); }
        }

        private static string Describe(Dictionary<string, FieldInfo> fields)
        {
            var parts = new List<string>();
            foreach (var pair in fields) parts.Add($"{pair.Key.Replace("Abstract", "")} {(float)pair.Value.GetValue(null):0.##}");
            return string.Join(", ", parts);
        }
    }
}
