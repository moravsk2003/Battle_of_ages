#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BattleOfAges.UI;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Creates Assets/Scenes/MainMenu.unity (camera + MainMenuUI; the UI is built in code) and puts it first in the
    /// build, before SampleScene, so the game starts at the start screen.
    /// </summary>
    public static class CreateMainMenuSceneTool
    {
        private const string MenuPath = "Assets/Scenes/MainMenu.unity";
        private const string GamePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Battle of Ages/Create Main Menu Scene")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            Camera c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.06f, 0.05f, 0.05f);
            cam.AddComponent<AudioListener>();
            new GameObject("MainMenu").AddComponent<MainMenuUI>();
            EditorSceneManager.SaveScene(scene, MenuPath);

            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuPath, true) };
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
                if (s.path != MenuPath) scenes.Add(s);
            if (!scenes.Exists(s => s.path == GamePath)) scenes.Add(new EditorBuildSettingsScene(GamePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Battle of Ages] Main menu scene created and set first in the build.");
        }
    }
}
#endif
