using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for the four unit ranks (not a regular test): person, warrior, knight and general side by side,
    /// each with the kit the game gives it, up close, three-quarter, from behind and from high above like the map.
    /// CAPTURE_DIR = output folder, CAPTURE_GEAR = gear level of the warriors' kit (1).
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class RankCapture
    {
        [UnityTest]
        public IEnumerator CaptureRanks()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "ranks");
            int gear = int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_GEAR"), out int g) ? g : 1;
            GpuSkinnedCharacter.Enabled = false;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            Vector3 c = new Vector3(18f, 0f, -24f);
            c.y = InfiniteWorldManager.GetSampledTerrainHeight(c.x, c.z);
            ModelCapture.ClearStage(new[] { c }, 14f);
            Vector3 cam = c + new Vector3(0f, 1.5f, -6.5f);

            var row = new List<GameObject>();
            void Spawn(UnitRank rank, int i, Item[] kit)
            {
                Vector3 p = c + new Vector3((i - 1.5f) * 1.5f, 0f, 0f);
                p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
                GameObject go = HumanoidModelBuilder.CreateHumanoidModel(RankLooks.ModelFor(rank), "Rank_" + rank);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.back));
                Equipment e = go.AddComponent<Equipment>();
                foreach (Item item in kit) if (!item.IsEmpty) e.Equip(item);
                row.Add(go);
            }
            Spawn(UnitRank.Person, 0, UnitStats.Kit(UnitRank.Person, gear));
            Spawn(UnitRank.Warrior, 1, UnitStats.Kit(UnitRank.Warrior, gear));
            Spawn(UnitRank.Knight, 2, UnitStats.Kit(UnitRank.Knight, gear + 1));
            Spawn(UnitRank.General, 3, UnitStats.Kit(UnitRank.General, gear + 2));
            yield return new WaitForSeconds(0.6f);

            var capture = new CaptureRenderer(dir, 6, 40f, drawGrass: true);
            Vector3 mid = c + Vector3.up * 1.0f;
            capture.Camera.fieldOfView = 40f;
            capture.Shoot(0, "front", cam, mid);
            capture.Shoot(1, "three_quarter", c + new Vector3(-4.5f, 2.0f, -4.5f), mid);
            capture.Shoot(2, "back", c + new Vector3(0f, 1.6f, 6.5f), mid);
            capture.Shoot(3, "close_knight_general", c + new Vector3(1.9f, 1.55f, -3.0f), c + new Vector3(1.9f, 1.2f, 0f));
            capture.Camera.fieldOfView = 8f; // the map: high above, the units a few dozen pixels tall
            capture.Shoot(4, "map_zoom_40", c + new Vector3(0f, 60f, -40f), c);
            capture.Camera.fieldOfView = 4f;
            capture.Shoot(5, "map_zoom_20", c + new Vector3(0f, 60f, -40f), c);
            yield return null;
            capture.Finish();
            GpuSkinnedCharacter.Enabled = true;
        }
    }
}
