using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A look at a kingdom's town (KingdomTown) in the real scene: from above, at a way through the ring, among the houses. CAPTURE_DIR = output folder.</summary>
    [Explicit("Visual capture tool, run on demand")]
    public class TownCapture
    {
        [UnityTest]
        public IEnumerator CaptureTown()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            NpcKingdom k = NpcKingdom.Found(new Vector3(110f, 0f, 60f), "Вигляд міста", 0, -1, HeroClass.Tank);
            k.Economy.HallLevel = 3;
            for (int i = k.Population; i < 36; i++) k.AddSubject(UnitRank.Warrior, k.Spot());
            k.Town.RaiseAllNow();
            yield return new WaitForSeconds(1f);
            Vector3 home = k.Home;
            Vector3 Ground(float x, float z) => new Vector3(home.x + x, InfiniteWorldManager.GetSampledTerrainHeight(home.x + x, home.z + z), home.z + z);
            float r = KingdomTown.WallRingRadius;
            var views = new (string name, Vector3 from, Vector3 to)[]
            {
                ("town_above", Ground(0f, -r - 30f) + Vector3.up * 70f, home),
                ("town_way", Ground(r + 22f, 6f) + Vector3.up * 5f, Ground(r - 10f, 0f) + Vector3.up * 2f),
                ("town_ring", Ground(r * 0.72f + 16f, r * 0.72f + 16f) + Vector3.up * 9f, Ground(r * 0.72f, r * 0.72f) + Vector3.up * 2f),
                ("town_street", Ground(18f, -22f) + Vector3.up * 3f, home + Vector3.up * 3f),
            };
            var capture = new CaptureRenderer(dir, views.Length, 50f, drawGrass: false);
            for (int j = 0; j < views.Length; j++)
            {
                capture.Camera.transform.SetPositionAndRotation(views[j].from, Quaternion.LookRotation(views[j].to - views[j].from));
                yield return null;
                capture.Shoot(j, views[j].name, views[j].from, views[j].to);
                yield return null;
            }
            capture.Finish();
            NpcKingdom.RemoveAll();
        }
    }
}
