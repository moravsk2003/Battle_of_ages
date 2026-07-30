#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BattleOfAges.Player;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>Creates or refreshes the local first-person arms camera in the open scene.</summary>
    public static class SetupFpsViewmodelTool
    {
        [MenuItem("Battle of Ages/Configure FPS Viewmodel Camera")]
        public static void ConfigureFpsViewmodel()
        {
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                Debug.LogError("[Battle of Ages] Cannot configure the FPS viewmodel because Player is missing.");
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                mainCamera = player.GetComponentInChildren<Camera>(true);
            }

            if (mainCamera == null)
            {
                Debug.LogError("[Battle of Ages] Cannot configure the FPS viewmodel because Main Camera is missing.");
                return;
            }

            ConfigureForPlayer(player, mainCamera, true);
        }

        public static FpsViewmodelSetup ConfigureForPlayer(GameObject player, Camera mainCamera, bool saveScene)
        {
            Transform worldBody = player.transform.Find("Hero3DModel");
            if (worldBody == null)
            {
                GameObject hero = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.HeroPlayer, "Hero3DModel");
                hero.transform.SetParent(player.transform, false);
                hero.transform.localPosition = new Vector3(0f, -1f, 0f);
                worldBody = hero.transform;
                Undo.RegisterCreatedObjectUndo(hero, "Create FPS World Body");
            }

            Transform cameraTransform = mainCamera.transform.Find("FPS Viewmodel Camera");
            Camera viewmodelCamera;
            if (cameraTransform == null)
            {
                GameObject cameraObject = new GameObject("FPS Viewmodel Camera");
                cameraObject.transform.SetParent(mainCamera.transform, false);
                viewmodelCamera = cameraObject.AddComponent<Camera>();
                Undo.RegisterCreatedObjectUndo(cameraObject, "Create FPS Viewmodel Camera");
            }
            else
            {
                viewmodelCamera = cameraTransform.GetComponent<Camera>();
                if (viewmodelCamera == null)
                {
                    viewmodelCamera = cameraTransform.gameObject.AddComponent<Camera>();
                }
            }

            AudioListener listener = viewmodelCamera.GetComponent<AudioListener>();
            if (listener != null) Object.DestroyImmediate(listener);

            Transform viewmodelRoot = viewmodelCamera.transform.Find("ViewmodelArms");
            if (viewmodelRoot != null)
            {
                Undo.DestroyObjectImmediate(viewmodelRoot.gameObject);
            }

            GameObject arms = HumanoidModelBuilder.CreateViewmodelArms();
            arms.transform.SetParent(viewmodelCamera.transform, false);
            arms.AddComponent<FpsViewmodelAnimation>();
            viewmodelRoot = arms.transform;
            Undo.RegisterCreatedObjectUndo(arms, "Create FPS Viewmodel Arms");

            FpsViewmodelSetup setup = player.GetComponent<FpsViewmodelSetup>();
            if (setup == null)
            {
                setup = player.AddComponent<FpsViewmodelSetup>();
                Undo.RegisterCreatedObjectUndo(setup, "Add FPS Viewmodel Setup");
            }

            setup.SetReferences(mainCamera, viewmodelCamera, worldBody, viewmodelRoot);
            viewmodelCamera.enabled = mainCamera.enabled;

            if (saveScene)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }

            Debug.Log("[Battle of Ages] FPS viewmodel camera configured. The local body is hidden from the main FPS camera.");
            return setup;
        }
    }
}
#endif
