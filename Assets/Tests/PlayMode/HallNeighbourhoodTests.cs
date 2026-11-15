using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Managers;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Land (§5.3): round one's own hall the ground is free to build on; round a king's home no one else builds; a king's
    /// town stays inside its own land and never reaches a hall.
    /// </summary>
    public class HallNeighbourhoodTests
    {
        [Test]
        public void TwoLands_NeverOverlap_AndAKingsTown_StaysInItsOwn()
        {
            Assert.LessOrEqual(GameBalance.TerritoryRadius * 2f, GameBalance.FoundingFromKing, "a hall is at least two lands' radii from a king");
            Assert.Less(KingdomTown.WallRingRadius + 15f, GameBalance.TerritoryRadius, "a king's walls and what is round them stay on its land");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator RoundOwnHall_AHouseFits_RoundAKing_NothingDoes()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            Assert.IsNotNull(placer);
            MethodInfo check = typeof(BuildingPlacementSystem).GetMethod("CheckLocationValid", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(check);
            BuildingDefinition house = BuildingData.GetDefinition(BuildingType.WorkerHouse);
            bool Fits(Vector3 p) { p.y = RtsUnit.GetTerrainHeight(p); return (bool)check.Invoke(placer, new object[] { p, Quaternion.identity, house }); }

            Vector3 at = GameContext.Player.transform.position + new Vector3(60f, 0f, 60f);
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject hall = TownHall.CreatePrefab("ProbeHall");
            hall.transform.position = at;
            yield return null;
            yield return new WaitForFixedUpdate();

            // Past the hall's own walls only what stands there (a stone, a tree) may be in the way, never the hall
            var refused = new List<string>();
            for (float d = 9f; d <= 30f; d += 3f)
                for (int k = 0; k < 8; k++)
                {
                    Vector3 p = at + Quaternion.Euler(0f, k * 45f, 0f) * new Vector3(d, 0f, 0f);
                    if (Fits(p)) continue;
                    p.y = RtsUnit.GetTerrainHeight(p);
                    foreach (Collider c in Physics.OverlapBox(p + Vector3.up * 1.5f, new Vector3(2.1f, 1.3f, 2.1f)))
                        if (!c.isTrigger && c.GetComponentInParent<TownHall>() != null) refused.Add($"{d} m at {k * 45}°");
                }
            Object.Destroy(hall);
            Assert.IsEmpty(refused, "the hall itself keeps a house off:\n" + string.Join("\n", refused));

            // A king's land: nothing of ours on it, just past it as before
            Vector3 home = at + new Vector3(400f, 0f, 0f);
            NpcKingdom.RemoveAll(); // (the scene's own kings stand elsewhere)
            NpcKingdom king = NpcKingdom.Found(home, "Сусід", 0);
            yield return null;
            home = king.Home; // (a king settles on the nearest level ground)
            try
            {
                Assert.IsNotNull(TownHall.ForeignLand(home + new Vector3(GameBalance.TerritoryRadius - 5f, 0f, 0f)), "inside a king's land");
                Assert.IsNull(TownHall.ForeignLand(home + new Vector3(GameBalance.TerritoryRadius + 5f, 0f, 0f)), "outside it");
                Assert.IsFalse(Fits(home + new Vector3(0f, 0f, GameBalance.TerritoryRadius - 10f)), "a house on a king's land does not fit");
            }
            finally { NpcKingdom.RemoveAll(); }
        }
    }
}
