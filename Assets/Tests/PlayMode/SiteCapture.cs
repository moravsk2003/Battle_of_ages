using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NUnit.Framework;
using BattleOfAges.Building;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Visual review tool (not a regular test): a house going up with four builders, shot through its stages. CAPTURE_DIR = output folder.</summary>
    [Explicit("Visual capture tool, run on demand")]
    public class SiteCapture
    {
        [UnityTest]
        public IEnumerator CaptureSite()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "site");
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            Vector3 at = new Vector3(20f, 0f, -34f);
            at.y = InfiniteWorldManager.GetSampledTerrainHeight(at.x, at.z);
            GameObject house = BuildingNode.CreateWorkerHousePrefab("Cap_SiteHouse");
            house.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 20f, 0f));
            ConstructionSite site = ConstructionSite.Begin(house, BuildingType.WorkerHouse);
            var workers = new List<NpcResourceGatherer>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = at + Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(10f, 0f, 0f);
                p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
                NpcResourceGatherer w = BuildingNode.CreateWorkerNpc(p, "Cap_Builder" + i).GetComponent<NpcResourceGatherer>();
                workers.Add(w);
            }
            yield return null;
            foreach (NpcResourceGatherer w in workers) w.AssignToSite(site);

            var capture = new CaptureRenderer(dir, 8, 50f, drawGrass: false);
            Vector3 look = at + Vector3.up * 1.5f;
            Vector3 far = at + new Vector3(-11f, 7f, -11f), near = at + new Vector3(-6f, 2.4f, -5f);
            int shot = 0;
            float[] stops = { 0.05f, 0.3f, 0.6f, 0.95f };
            Time.timeScale = 3f;
            foreach (float stop in stops)
            {
                float end = Time.time + 90f;
                while (site != null && site.Progress < stop && Time.time < end) yield return null;
                if (site == null) break;
                Debug.Log($"[SiteCapture] progress {site.Progress:0.00} present {site.PresentBuilders}");
                foreach (NpcResourceGatherer w in workers)
                    if (w != null) Debug.Log($"[SiteCapture]   {w.name} at {w.transform.position - at} facing {w.transform.forward}");
                capture.Shoot(shot++, $"site_{Mathf.RoundToInt(stop * 100f)}_far", far, look);
                capture.Shoot(shot++, $"site_{Mathf.RoundToInt(stop * 100f)}_near", near, look);
            }
            Time.timeScale = 1f;
            capture.Finish();
            UnityEngine.Object.Destroy(house);
            foreach (NpcResourceGatherer w in workers) if (w != null) UnityEngine.Object.Destroy(w.gameObject);
        }
    }
}
