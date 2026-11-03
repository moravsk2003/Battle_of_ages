using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>How a walking unit deals with others in its way: time, how far aside it went, whether it arrived; rows are added to push_probe.md in CAPTURE_DIR (PROBE_LABEL names the run).</summary>
    [Explicit("Movement measurement, run on demand")]
    public class PushProbe
    {
        private static readonly Vector3 Far = new Vector3(4000f, 0f, 4000f);
        private readonly List<GameObject> spawned = new List<GameObject>();

        private NpcResourceGatherer Unit(Vector3 at, string name)
        {
            GameObject w = BuildingNode.CreateWorkerNpc(at, name);
            spawned.Add(w);
            NpcResourceGatherer g = w.GetComponent<NpcResourceGatherer>();
            g.SetGatherDirective(GatherDirective.Idle);
            return g;
        }

        private void Block(Vector3 at, Vector3 size)
        {
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "ProbeBlock";
            b.transform.position = at + Vector3.up * size.y * 0.5f;
            b.transform.localScale = size;
            spawned.Add(b);
        }

        private IEnumerator Walk(string scene, Vector3 from, Vector3 to, StreamWriter report, string label, List<NpcResourceGatherer> others)
        {
            NpcResourceGatherer walker = Unit(from, "NPC_Walker");
            yield return null;
            yield return null;
            var was = new List<Vector3>();
            foreach (NpcResourceGatherer o in others) was.Add(o.transform.position);
            walker.GetComponent<RtsUnit>().IssueMoveCommand(to);
            float start = Time.time, side = 0f, end = start + 25f;
            Vector3 line = (to - from).normalized;
            while (Time.time < end && walker.GetComponent<RtsUnit>().HasMoveTarget)
            {
                Vector3 off = walker.transform.position - from;
                off.y = 0f;
                side = Mathf.Max(side, (off - line * Vector3.Dot(off, line)).magnitude);
                yield return null;
            }
            Vector3 left = walker.transform.position - to;
            left.y = 0f;
            float shoved = 0f;
            for (int i = 0; i < others.Count; i++) shoved = Mathf.Max(shoved, Vector3.Distance(others[i].transform.position, was[i]));
            report.WriteLine($"| {label} | {scene} | {Time.time - start:0.0} | {side:0.0} | {left.magnitude:0.0} | {shoved:0.0} |");
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            yield return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator Measure()
        {
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            string label = System.Environment.GetEnvironmentVariable("PROBE_LABEL") ?? "run";
            if (label == "old")
            {
                GameBalance.WalkerSeparation = GameBalance.StandingSeparation = 6f;
                GameBalance.PressBeforeBypass = 0.25f;
            }
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(400f, 1f, 400f);
            spawned.Clear();
            using (var report = new StreamWriter(Path.Combine(dir, "push_probe.md"), true))
            {
                Vector3 from = Far, to = Far + new Vector3(14f, 0f, 0f);

                // A row of units across the way, too long to be worth going round
                var row = new List<NpcResourceGatherer>();
                for (int i = -6; i <= 6; i++) row.Add(Unit(Far + new Vector3(7f, 0f, i * 0.85f), "NPC_Row" + i));
                yield return Walk("row of 13", from, to, report, label, row);

                // A gap two metres wide between two long walls, a unit standing in it
                Block(Far + new Vector3(7f, 0f, 11f), new Vector3(2f, 3f, 20f));
                Block(Far + new Vector3(7f, 0f, -11f), new Vector3(2f, 3f, 20f));
                var gap = new List<NpcResourceGatherer> { Unit(Far + new Vector3(7f, 0f, 0f), "NPC_Gap") };
                yield return Walk("unit in a gap", from, to, report, label, gap);

                // Someone standing right on the place it is going to
                var spot = new List<NpcResourceGatherer> { Unit(to, "NPC_Spot") };
                yield return Walk("unit on its place", from, to, report, label, spot);

                // A knot of units round its place
                var knot = new List<NpcResourceGatherer>();
                for (int i = 0; i < 8; i++) knot.Add(Unit(to + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 1.1f, "NPC_Knot" + i));
                knot.Add(Unit(to, "NPC_KnotMid"));
                yield return Walk("knot on its place", from, to, report, label, knot);
            }
            Object.Destroy(ground);
            Assert.Pass("measured");
        }
    }
}
