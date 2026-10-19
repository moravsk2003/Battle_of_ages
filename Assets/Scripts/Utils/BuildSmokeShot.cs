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
            go.AddComponent<BuildSmokeShot>().StartCoroutine(Shoot(args[i + 1]));
        }

        private static IEnumerator Shoot(string path)
        {
            yield return SceneManager.LoadSceneAsync(SaveLoad.SaveSystem.GameScene);
            yield return new WaitForSecondsRealtime(8f);
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1.5f);
            Application.Quit();
        }
    }
}
