using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>World chunks: what stands in them does not depend on the LOD (a tester saw trees and a camp pop up 1 m on).</summary>
    public class WorldChunkTests
    {
        private GameObject chunkObj;

        [TearDown]
        public void TearDown()
        {
            if (chunkObj != null) Object.Destroy(chunkObj);
        }

        private static Dictionary<int, (string name, Vector3 at)> Content(GameObject chunk)
        {
            var d = new Dictionary<int, (string, Vector3)>();
            foreach (Transform c in chunk.transform) d[c.GetInstanceID()] = (c.name, c.position);
            return d;
        }

        [UnityTest]
        public IEnumerator ALodChange_KeepsTheTreesStonesAndTents_InPlace_AndLifeComesOnceNear()
        {
            // A chunk where a camp may stand (seed % 5 == 0, 2+ chunks out): its tents are checked too when the site is level
            Vector2Int coord = Vector2Int.zero;
            for (int x = 2; x < 60 && coord == Vector2Int.zero; x++)
                if (Mathf.Abs((x * 73856093) ^ (5 * 19349663)) % 5 == 0) coord = new Vector2Int(x, 5);
            chunkObj = new GameObject("TestChunk", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            ProceduralBiomeChunk chunk = chunkObj.AddComponent<ProceduralBiomeChunk>();

            chunk.GenerateChunk(coord, 64f, 16, true, null, null, null, null); // far off: low LOD
            yield return null;
            var far = Content(chunkObj);
            Assert.Greater(far.Count, 0, "the chunk has content");
            foreach (var c in far.Values) Assert.IsFalse(c.name.StartsWith("Wild_"), "no animals far off");
            foreach (AI.HostileMobAI mob in chunkObj.GetComponentsInChildren<AI.HostileMobAI>(true)) Assert.Fail("no raiders far off: " + mob.name);

            chunk.GenerateChunk(coord, 64f, 16, false, null, null, null, null); // the viewer came closer: full LOD
            yield return null;
            Assert.IsFalse(chunk.IsLowLOD);
            var near = Content(chunkObj);
            foreach (var kv in far)
            {
                Assert.IsTrue(near.ContainsKey(kv.Key), $"{kv.Value.name} stays");
                Assert.Less((near[kv.Key].at - kv.Value.at).magnitude, 0.01f, $"{kv.Value.name} in place");
            }
            foreach (var kv in near) if (!far.ContainsKey(kv.Key)) Assert.IsTrue(kv.Value.name.StartsWith("Wild_"), "only animals come: " + kv.Value.name);
            bool camp = false;
            foreach (var c in far.Values) camp |= c.name.StartsWith("HostileCamp_");
            if (camp) Assert.Greater(chunkObj.GetComponentsInChildren<AI.HostileMobAI>(true).Length, 0, "the camp's raiders are out");

            chunk.GenerateChunk(coord, 64f, 16, true, null, null, null, null); // away again: everything stays
            yield return null;
            Assert.AreEqual(near.Count, Content(chunkObj).Count, "nothing goes at a LOD change");
        }
    }
}
