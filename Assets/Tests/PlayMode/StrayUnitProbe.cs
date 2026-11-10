using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Diagnostic tool (not a regular test): loads a save (PROBE_SAVE, a path to a .json) and writes every character
    /// body in the scene with what drives it (RtsUnit, AI, the grid team) to Logs/stray_units.txt. Finds bodies that
    /// stand without a unit behind them.
    /// </summary>
    [Explicit("Diagnostic tool, run on demand")]
    public class StrayUnitProbe
    {
        [UnityTest]
        public IEnumerator ListBodies()
        {
            string save = System.Environment.GetEnvironmentVariable("PROBE_SAVE");
            Assert.IsTrue(!string.IsNullOrEmpty(save) && File.Exists(save), "PROBE_SAVE");
            string folder = Path.Combine(Path.GetTempPath(), "boa_stray_probe");
            Directory.CreateDirectory(folder);
            File.Copy(save, Path.Combine(folder, "auto.json"), true);
            SaveSystem.FolderOverride = folder;
            try
            {
                Assert.IsTrue(SaveSystem.Load(SaveSystem.AutoSlot));
                for (int i = 0; i < 2; i++) { yield return Report(i == 0 ? "after 2 s" : "after 8 s"); }
            }
            finally { SaveSystem.FolderOverride = null; }
        }

        private static readonly StringBuilder log = new StringBuilder();

        private static IEnumerator Report(string when)
        {
            float end = Time.realtimeSinceStartup + (when == "after 2 s" ? 2f : 6f);
            while (Time.realtimeSinceStartup < end) yield return null;
            log.AppendLine("==== " + when);
            foreach (CharacterGear body in Object.FindObjectsByType<CharacterGear>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform t = body.transform;
                if ((t.position - new Vector3(10f, 0f, 30f)).sqrMagnitude > 120f * 120f) continue; // the player's town
                RtsUnit unit = t.GetComponentInParent<RtsUnit>(true);
                var ai = t.GetComponentInParent<AI.AIBaseController>(true);
                Renderer r = t.GetComponentInChildren<Renderer>(true);
                var gpu = t.GetComponentInChildren<GpuSkinnedCharacter>(true);
                string path = t.name;
                for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
                int grid = -1, team = -9;
                Component driver = (Component)unit ?? ai;
                for (int id = 0; driver != null && id < UnitSpatialGrid.Capacity; id++)
                    if (UnitSpatialGrid.IsAlive(id) && UnitSpatialGrid.Owner(id) == driver) { grid = id; team = UnitSpatialGrid.Team(id); }
                log.AppendLine($"{path} type={body.Type} pos={t.position} activeInHierarchy={t.gameObject.activeInHierarchy} " +
                               $"rts={(unit != null ? unit.name + (unit.enabled ? "" : "(off)") : "-")} ai={(ai != null ? ai.GetType().Name : "-")} grid={grid} team={team} " +
                               $"renderer={(r != null ? r.enabled.ToString() : "-")} gpu={(gpu != null ? (gpu.Hidden ? "hidden" : "on") : "-")}");
            }
            foreach (RtsUnit u in Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None))
                if (u.GetComponentInChildren<CharacterGear>(true) == null) log.AppendLine($"unit without body: {u.name} pos={u.transform.position}");
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Logs", "stray_units.txt"), log.ToString());
        }
    }
}
