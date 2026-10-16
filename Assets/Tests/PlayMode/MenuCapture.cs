using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.SaveLoad;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Visual review tool (not a regular test): the start screen, its load / settings views, and the pause menu with the save window.</summary>
    [Explicit("Visual capture tool, run on demand")]
    public class MenuCapture
    {
        [UnityTest]
        public IEnumerator CaptureMenus()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "menu");
            var capture = new CaptureRenderer(dir, 5, 60f, drawGrass: true);
            UnityEngine.Object.DontDestroyOnLoad(capture.Camera.transform.root.gameObject); // survives the switch to the game scene

            yield return SceneManager.LoadSceneAsync(SaveSystem.MenuScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(0.5f);
            Shoot(capture, 0, "start_screen");
            SaveSlotsUI.Open(false);
            yield return null;
            Shoot(capture, 1, "load_window");
            SaveSlotsUI.Close();
            GameObject.Find("MainMenuCanvas").transform.Find("Settings").gameObject.SetActive(true);
            GameObject.Find("MainMenuCanvas").transform.Find("Menu").gameObject.SetActive(false);
            yield return null;
            Shoot(capture, 2, "settings");

            yield return SceneManager.LoadSceneAsync(SaveSystem.GameScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);
            PauseMenuController.Instance?.PauseGame();
            yield return null;
            Shoot(capture, 3, "pause_menu");
            SaveSlotsUI.Open(true);
            yield return null;
            Shoot(capture, 4, "save_window");
            SaveSlotsUI.Close();
            PauseMenuController.Instance?.ResumeGame();
            Time.timeScale = 1f;
            capture.Finish();
        }

        private static void Shoot(CaptureRenderer capture, int index, string name)
        {
            foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                c.renderMode = RenderMode.ScreenSpaceCamera;
                c.worldCamera = capture.Camera;
                c.planeDistance = 0.2f + 0.001f * (1000 - Mathf.Min(999, c.sortingOrder));
            }
            Canvas.ForceUpdateCanvases();
            Camera main = Camera.main;
            Vector3 pos = main != null ? main.transform.position : Vector3.up * 30f;
            Vector3 target = main != null ? pos + main.transform.forward : Vector3.zero;
            capture.Shoot(index, name, pos, target);
        }
    }
}
