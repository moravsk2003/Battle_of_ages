using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A worker told to cross a lake does not wade: it goes along the shore; and every lake has its water drawn.</summary>
    public class LakeWalkTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            WorldSettings.Set(0, 0f);
        }

        [Category("Slow")]
        [UnityTest, Timeout(120000)]
        public IEnumerator AWorkerSentAcrossALake_KeepsToTheShore()
        {
            WorldSettings.Set(31337, WorldSettings.DefaultSize);
            yield return null;
            yield return null;
            LakeWater water = Object.FindFirstObjectByType<LakeWater>();
            if (water == null) { var go = new GameObject("LakeWater"); spawned.Add(go); water = go.AddComponent<LakeWater>(); yield return null; }
            Assert.AreEqual(Lakes.All.Count, water.SurfaceCount, "every lake has its water");

            Lakes.Lake lake = Lakes.All[0];
            float r = lake.ShoreRadius(0f) + 6f;
            Vector3 start = new Vector3(lake.Center.x + r, 0f, lake.Center.y);
            start.y = RtsUnit.GetTerrainHeight(start);
            GameObject w = BuildingNode.CreateWorkerNpc(start, "NPC_Wader");
            spawned.Add(w);
            yield return null;
            w.GetComponent<NpcResourceGatherer>().SetGatherDirective(GatherDirective.Idle);
            RtsUnit unit = w.GetComponent<RtsUnit>();
            Vector3 across = new Vector3(lake.Center.x - lake.ShoreRadius(Mathf.PI) - 6f, 0f, lake.Center.y + 1f);
            unit.IssueMoveCommand(across);

            Time.timeScale = 3f;
            bool wet = false;
            float closest = float.MaxValue;
            for (float t = 0f; t < 25f; t += Time.deltaTime)
            {
                Vector3 p = w.transform.position;
                wet |= Lakes.InWater(p.x, p.z, -0.5f); // well inside the water, not brushing the shore
                closest = Mathf.Min(closest, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(across.x, across.z)));
                yield return null;
            }
            Assert.IsFalse(wet, "it never waded in");
            Assert.Less(closest, Vector2.Distance(new Vector2(start.x, start.z), new Vector2(across.x, across.z)) - 10f, "it went on along the shore");
        }
    }
}
