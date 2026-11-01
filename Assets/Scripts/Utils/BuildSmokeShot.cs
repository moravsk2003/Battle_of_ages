using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Build check: started with "-smokeshot path.png", the player opens the game scene, waits for streaming, saves a
    /// screenshot and quits. Shows what a build really renders (shaders stripped from builds work in the Editor).
    /// "-smokemap" shoots the map view (team rings, plates) instead of first person; "-smokecampaign" starts a new
    /// campaign (seed 777) instead of opening the fixed scene, "-smokelake" puts the hero by its nearest lake;
    /// "-smokezones" (with a campaign): zones, a field, a market, a site, a second settlement and a caravan by the base,
    /// and a second shot of the market's window beside path (path + ".market.png").
    /// </summary>
    public sealed class BuildSmokeShot : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-smokeshot");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("BuildSmokeShot");
            DontDestroyOnLoad(go);
            go.AddComponent<BuildSmokeShot>().StartCoroutine(Shoot(args[i + 1], Array.IndexOf(args, "-smokefx") >= 0, Array.IndexOf(args, "-smokemap") >= 0,
                                                                   Array.IndexOf(args, "-smokecampaign") >= 0, Array.IndexOf(args, "-smokelake") >= 0, Array.IndexOf(args, "-smokezones") >= 0));
        }

        private static IEnumerator Shoot(string path, bool effects, bool map, bool campaign, bool lake, bool zones)
        {
            if (campaign)
            {
                SaveLoad.SaveSystem.NewGame(new SaveLoad.CampaignSetup { Seed = 777, Class = Combat.HeroClass.Tank, HasArtifact = true, Artifact = Combat.ArtifactType.Vampiric });
                while (SceneManager.GetActiveScene().name != SaveLoad.SaveSystem.GameScene) yield return null;
            }
            else yield return SceneManager.LoadSceneAsync(SaveLoad.SaveSystem.GameScene);
            if (lake && World.Lakes.All.Count > 0)
            {
                yield return new WaitForSecondsRealtime(2f);
                World.Lakes.Lake l = World.Lakes.All[0];
                foreach (World.Lakes.Lake o in World.Lakes.All) if (o.Center.magnitude < l.Center.magnitude && o.Radius > 90f) l = o;
                GameObject player = Managers.GameContext.Player;
                Vector3 at = new Vector3(l.Center.x + l.ShoreRadius(0f) + 14f, 0f, l.Center.y);
                at.y = World.InfiniteWorldManager.GetSampledTerrainHeight(at.x, at.z) + 1f;
                var cc = player.GetComponent<CharacterController>();
                cc.enabled = false; player.transform.position = at; player.transform.rotation = Quaternion.LookRotation(Vector3.left); cc.enabled = true;
            }
            yield return new WaitForSecondsRealtime(8f);
            if (zones) Settlements();
            if (zones) yield return new WaitForSecondsRealtime(3f);
            if (effects) yield return Effects();
            if (map) { Managers.GameContext.Modes?.SetGameMode(Managers.GameMode.RtsMap); yield return new WaitForSecondsRealtime(1.5f); }
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1.5f);
            if (zones)
            {
                UI.TradeUI.Open();
                yield return new WaitForSecondsRealtime(1f);
                ScreenCapture.CaptureScreenshot(path + ".market.png");
                yield return new WaitForSecondsRealtime(1.5f);
            }
            Application.Quit();
        }

        /// <summary>With "-smokezones": what M5 makes at run time, in view of the map camera over the base.</summary>
        private static void Settlements()
        {
            GameObject Build(Building.BuildingType type, Vector3 at)
            {
                GameObject go = Building.BuildingPlacementSystem.CreateBuilding(type);
                at.y = RTS.RtsUnit.GetTerrainHeight(at);
                go.transform.position = at;
                go.GetComponent<Building.BuildingNode>().OnBuilt();
                return go;
            }
            Building.TownHall capital = Build(Building.BuildingType.TownHall, new Vector3(0f, 0f, -14f)).GetComponent<Building.TownHall>();
            capital.Level = 2;
            capital.Stock.SetState(1000, new[] { new System.Collections.Generic.KeyValuePair<int, int>(0, 200) });
            capital.Stock.SetFood(500f);
            World.ZoneMap.Paint(new Vector3(-30f, 0f, 20f), 14f, World.ZoneType.Fields, out _);
            World.ZoneMap.Paint(new Vector3(30f, 0f, 20f), 14f, World.ZoneType.Housing, out _);
            World.ZoneMap.Paint(new Vector3(-30f, 0f, -30f), 14f, World.ZoneType.Trade, out _);
            Build(Building.BuildingType.Field, new Vector3(-30f, 0f, 20f));
            Build(Building.BuildingType.Market, new Vector3(-30f, 0f, -30f));
            GameObject site = Building.BuildingPlacementSystem.CreateBuilding(Building.BuildingType.Mine);
            Vector3 siteAt = new Vector3(30f, 0f, -30f);
            siteAt.y = RTS.RtsUnit.GetTerrainHeight(siteAt);
            site.transform.position = siteAt;
            Building.ConstructionSite.Begin(site, Building.BuildingType.Mine, 0.5f);
            Vector3 place = Vector3.zero;
            for (float r = 650f; r < 2000f && place == Vector3.zero; r += 100f)
                for (int i = 0; i < 16 && place == Vector3.zero; i++)
                {
                    Vector3 p = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * r;
                    if (!World.Lakes.InWater(p.x, p.z, 40f) && Building.TownHall.FoundingForbidden(p) == null) place = p;
                }
            Building.TownHall second = Build(Building.BuildingType.TownHall, place).GetComponent<Building.TownHall>();
            second.Founded();
            Building.Caravans.Send(capital, second, Building.CaravanGood.Wood, 100);
            Debug.Log($"[Smoke] settlements {Building.TownHall.All.Count}, caravans {Building.Caravan.All.Count}, second at {place}, zones {World.ZoneMap.Count(World.ZoneType.Fields)} field cells");
        }

        /// <summary>With "-smokefx": runtime-made looks in front of the camera (mage bolts, fire fan, an artifact).</summary>
        private static IEnumerator Effects()
        {
            var player = Managers.GameContext.PlayerCombat;
            if (player == null) yield break;
            var hero = player.GetComponent<Combat.HeroProgress>();
            hero.ChooseClass(Combat.HeroClass.Mage);
            hero.AddXp(100);
            player.GetComponent<Combat.HeroSkills>().LearnFireFan();
            Transform t = player.transform;
            GameObject mob = HostileCampBuilder.CreateHostileMob(AI.HostileMobType.Bandit, "SmokeBandit");
            mob.GetComponent<AI.HostileMobAI>().enabled = false;
            var cc = mob.GetComponent<CharacterController>();
            cc.enabled = false;
            Vector3 p = t.position + t.forward * 9f;
            p.y = RTS.RtsUnit.GetTerrainHeight(p);
            mob.transform.position = p;
            cc.enabled = true;
            Combat.ArtifactPickup.Spawn(Combat.ArtifactType.SwordX5, t.position + t.forward * 4f + t.right * 1.5f, false, null);
            Combat.ArtifactPickup.Spawn(Combat.ArtifactType.Wisdom, t.position + t.forward * 4f - t.right * 1.5f, true, null);
            yield return null;
            player.GetComponent<Combat.CombatUnit>().TryAttack(mob.GetComponent<Combat.HealthComponent>());
            yield return new WaitForSecondsRealtime(0.15f);
            player.GetComponent<Combat.HeroSkills>().CastFireFan(t.forward);
            yield return new WaitForSecondsRealtime(0.12f);
        }
    }
}
