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
    /// campaign (seed 777) instead of opening the fixed scene, "-smokelake" puts the hero by its nearest lake.
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
                                                                   Array.IndexOf(args, "-smokecampaign") >= 0, Array.IndexOf(args, "-smokelake") >= 0));
        }

        private static IEnumerator Shoot(string path, bool effects, bool map, bool campaign, bool lake)
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
            if (effects) yield return Effects();
            if (map) { Managers.GameContext.Modes?.SetGameMode(Managers.GameMode.RtsMap); yield return new WaitForSecondsRealtime(1.5f); }
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1.5f);
            Application.Quit();
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
