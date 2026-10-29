using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Player;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A look at a lake of a new game: from its shore and from above (CAPTURE_DIR).</summary>
    [Explicit("Visual capture tool, run on demand")]
    public class LakeCapture
    {
        [UnityTest, Timeout(240000)]
        public IEnumerator CaptureLake()
        {
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "lake");
            string saves = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boa_lake_capture");
            SaveSystem.FolderOverride = saves;
            SaveSystem.NewGame();
            yield return new WaitForSeconds(2f);

            Lakes.Lake lake = Lakes.All[0];
            foreach (Lakes.Lake l in Lakes.All) if (l.Center.magnitude < lake.Center.magnitude && l.Radius > 90f) lake = l;
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Vector3 shore = new Vector3(lake.Center.x + lake.ShoreRadius(0f) + 12f, 0f, lake.Center.y);
            shore.y = InfiniteWorldManager.GetSampledTerrainHeight(shore.x, shore.z) + 1f;
            var cc = hero.GetComponent<CharacterController>();
            cc.enabled = false; hero.transform.position = shore; cc.enabled = true;
            yield return new WaitForSeconds(8f); // the chunks round it stream in

            var cap = new CaptureRenderer(dir, 3);
            Vector3 eye = shore + Vector3.up * 1.6f;
            Vector3 centre = new Vector3(lake.Center.x, lake.Level, lake.Center.y);
            cap.Shoot(0, "shore", eye, centre);
            cap.Shoot(1, "high", shore + new Vector3(40f, 45f, 0f), centre);
            cap.Camera.orthographic = true;
            cap.Camera.orthographicSize = lake.Radius * 1.6f;
            cap.Shoot(2, "map", centre + new Vector3(0f, 200f, -115f), centre);
            cap.Finish();
            SaveSystem.FolderOverride = null;
            WorldSettings.Set(0, 0f);
        }
    }
}
