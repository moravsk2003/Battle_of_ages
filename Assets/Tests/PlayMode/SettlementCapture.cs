using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Managers;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A second settlement seen with the hero far away (CAPTURE_DIR): its held land from the map and the ground, a caravan on the road.</summary>
    [Explicit("Visual capture tool, run on demand")]
    public class SettlementCapture
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator CaptureSecondSettlement()
        {
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "settlement");
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.FolderOverride = Path.Combine(Path.GetTempPath(), "boa_settlement_capture");
            SaveSystem.NewGame(new CampaignSetup { Seed = 777, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            GameBalance.BanditFirstRaidHours = 1000f;
            BanditRaids.NextRaidHour = 1000.0;

            TownHall Hall(Vector3 at)
            {
                GameObject go = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
                at.y = RtsUnit.GetTerrainHeight(at);
                go.transform.position = at;
                go.GetComponent<BuildingNode>().OnBuilt();
                return go.GetComponent<TownHall>();
            }
            TownHall a = Hall(new Vector3(0f, 0f, -44f));
            a.Level = 2;
            Vector3 place = Vector3.zero;
            for (float r = 650f; r < 1500f && place == Vector3.zero; r += 100f)
                for (int i = 0; i < 16 && place == Vector3.zero; i++)
                {
                    Vector3 p = a.transform.position + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * r;
                    if (!Lakes.InWater(p.x, p.z, 40f) && TownHall.FoundingForbidden(p) == null) place = p;
                }
            TownHall b = Hall(place);
            b.Founded();
            ResourcesFill(a);
            Caravans.Send(a, b, CaravanGood.Wood, 100);
            Vector3 cartAt = b.transform.position + new Vector3(25f, 0f, 25f);
            cartAt.y = RtsUnit.GetTerrainHeight(cartAt);
            Caravan.All[0].transform.position = cartAt;
            yield return new WaitForSeconds(4f); // the land held round the hall streams in

            Vector3 mid = b.transform.position;
            var cap = new CaptureRenderer(dir, 2);
            cap.Camera.orthographic = true;
            cap.Camera.orthographicSize = 60f;
            cap.Shoot(0, "second_map", mid + new Vector3(0f, 170f, -98f), mid);
            cap.Camera.orthographic = false;
            cap.Camera.fieldOfView = 60f;
            cap.Shoot(1, "second_ground", mid + new Vector3(-30f, 12f, -30f), mid + new Vector3(10f, 2f, 10f));
            cap.Finish();
            GameBalance.BanditFirstRaidHours = 6f;
            SaveSystem.FolderOverride = null;
            foreach (TownHall h in Object.FindObjectsByType<TownHall>(FindObjectsSortMode.None)) Object.Destroy(h.gameObject);
            GameObject hero = GameObject.Find("Player");
            if (hero != null) Object.Destroy(hero);
            NpcKingdom.RemoveAll(); KingdomRealm.Clear(); ZoneMap.Clear(); WorldSettings.Set(0, 0f);
        }

        private static void ResourcesFill(TownHall hall)
        {
            hall.Stock.SetState(1000, null);
            hall.Stock.SetFood(500f);
        }
    }
}
