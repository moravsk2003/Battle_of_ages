using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;
using BattleOfAges.UI;
using BattleOfAges.World;
using Debug = UnityEngine.Debug;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// What one guest save costs (not a regular test; NetSession.GuestSaveSeconds is chosen from it): the guest's frame
    /// stops for SaveSystem.Capture + JSON + UTF-8 (median and worst of 15), the bytes that go to the host, the garbage
    /// made, and the host's decoding, at the start of a game and in a mid and a late settlement (people, houses, zone
    /// cells, felled trees). CAPTURE_DIR = folder for report.txt.
    /// </summary>
    [Explicit("Measurement, run on demand")]
    public class GuestSaveProbe
    {
        private readonly StringBuilder report = new StringBuilder();

        [UnityTest]
        public IEnumerator MeasureGuestSave()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(2f);
            Vector3 home = Managers.GameContext.Player != null ? Managers.GameContext.Player.transform.position : Vector3.zero;

            Measure("start");
            yield return Grow(home, people: 40, houses: 10, cells: 200, trees: 500);
            Measure("mid (40 more people, 10 houses, 200 zone cells, 500 felled trees)");
            yield return Grow(home, people: 60, houses: 20, cells: 400, trees: 2500);
            Measure("late (100 more people, 30 houses, 600 zone cells, 3000 felled trees)");

            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (!string.IsNullOrEmpty(dir)) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString()); }
            Debug.Log("[GuestSaveProbe]\n" + report);
        }

        private int grown;

        private IEnumerator Grow(Vector3 home, int people, int houses, int cells, int trees)
        {
            for (int i = 0; i < people; i++)
            {
                float a = (grown + i) * 0.61f, r = 6f + (grown + i) * 0.15f;
                Vector3 at = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                at.y = RTS.RtsUnit.GetTerrainHeight(at);
                UI.DevTools.SpawnOwn(i % 4 == 0 ? UnitRank.Warrior : UnitRank.Person, at);
            }
            for (int i = 0; i < houses; i++)
            {
                GameObject house = BuildingNode.CreateWorkerHousePrefab("ProbeHouse");
                Vector3 at = home + new Vector3(30f + (grown / 4 + i) % 10 * 9f, 0f, 30f + (grown / 4 + i) / 10 * 9f);
                at.y = RTS.RtsUnit.GetTerrainHeight(at);
                house.transform.position = at;
            }
            Vector2Int c0 = ZoneMap.CellOf(home);
            int placed = 0;
            for (int ring = 1; placed < cells && ring < 60; ring++)
                for (int x = -ring; x <= ring && placed < cells; x++)
                    for (int z = -ring; z <= ring && placed < cells; z++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring) continue;
                        Vector2Int c = new Vector2Int(c0.x + x, c0.y + z);
                        if (ZoneMap.At(ZoneMap.CenterOf(c)) != ZoneType.None) continue;
                        ZoneMap.SetRaw(c, (ZoneType)(1 + placed % 4));
                        placed++;
                    }
            for (int i = 0; i < trees; i++)
                WorldPersistence.ApplyTree(new TreeSave { Key = $"probe_{grown}_{i}_{(i * 7919) % 100000}", State = (int)Resources.TreeState.Depleted, Wood = 0 });
            grown += people;
            for (int i = 0; i < 30; i++) yield return null; // the new people settle in
        }

        private void Measure(string label)
        {
            var times = new List<double>();
            var captures = new List<double>();
            var jsons = new List<double>();
            double first = -1;
            int bytes = 0;
            long garbage = 0;
            double decode = 0;
            SaveData last = null;
            for (int i = 0; i < 15; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                SaveData own = SaveSystem.Capture(); // as NetSession.SendOwnSave
                own.Guests.Clear();
                own.KingdomStates.Clear();
                double captured = sw.Elapsed.TotalMilliseconds;
                byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(own));
                sw.Stop();
                captures.Add(captured);
                jsons.Add(sw.Elapsed.TotalMilliseconds - captured);
                garbage = GC.GetAllocatedBytesForCurrentThread() - before;
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (first < 0) first = sw.Elapsed.TotalMilliseconds;
                bytes = data.Length;
                last = own;
                var dw = Stopwatch.StartNew();
                string text = Encoding.UTF8.GetString(data); // the host keeps the text
                dw.Stop();
                decode = Math.Max(decode, dw.Elapsed.TotalMilliseconds + (text.Length > 0 ? 0 : 1));
            }
            times.Sort();
            captures.Sort();
            jsons.Sort();
            report.AppendLine($"{label}: units {last.Units.Count}, buildings {last.Buildings.Count}, zone values {last.Zones.Count}, trees {last.World?.Trees.Count ?? 0}");
            report.AppendLine($"  guest frame: median {times[times.Count / 2]:0.00} ms, worst {times[times.Count - 1]:0.00} ms (first {first:0.00}, best {times[0]:0.00}); {bytes / 1024f:0.0} KB; garbage {garbage / 1024f:0} KB; host decode {decode:0.00} ms");
            report.AppendLine($"  of it: capture median {captures[captures.Count / 2]:0.00} ms (worst {captures[captures.Count - 1]:0.00}), JSON + UTF-8 median {jsons[jsons.Count / 2]:0.00} ms (worst {jsons[jsons.Count - 1]:0.00})");
        }
    }
}
