using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Build check: started with "-smokeshot path.png", the player opens the game scene, waits for streaming, saves a
    /// screenshot and quits. Shows what a build really renders (shaders stripped from builds work in the Editor).
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
            go.AddComponent<BuildSmokeShot>().StartCoroutine(Shoot(args[i + 1], Array.IndexOf(args, "-smokefx") >= 0));
        }

        private static IEnumerator Shoot(string path, bool effects)
        {
            yield return SceneManager.LoadSceneAsync(SaveLoad.SaveSystem.GameScene);
            yield return new WaitForSecondsRealtime(8f);
            if (effects) yield return Effects();
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1.5f);
            Application.Quit();
        }

        /// <summary>With "-smokefx": runtime-made looks in front of the camera (mage bolts, fire fan, an artifact).</summary>
        private static IEnumerator Effects()
        {
            var player = UnityEngine.Object.FindFirstObjectByType<Player.PlayerCombatController>();
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
