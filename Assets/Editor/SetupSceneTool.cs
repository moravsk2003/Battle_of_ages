#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using BattleOfAges.Player;
using BattleOfAges.CameraSystem;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.UI;

namespace BattleOfAges.EditorTools
{
    public class SetupSceneTool
    {
        [MenuItem("Battle of Ages/Auto Setup FPS & RTS Scene")]
        public static void SetupScene()
        {
            string scenePath = "Assets/Scenes/SampleScene.unity";
            Scene activeScene;

            if (System.IO.File.Exists(scenePath))
            {
                activeScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
            else
            {
                activeScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            }
            
            // 1. Create Ground Plane
            GameObject ground = GameObject.Find("Ground");
            if (ground == null)
            {
                ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.name = "Ground";
                ground.transform.position = Vector3.zero;
                ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100x100 world area
                Undo.RegisterCreatedObjectUndo(ground, "Create Ground");
            }

            // 2. Setup Player GameObject
            GameObject player = GameObject.Find("Player");
            if (player == null)
            {
                player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.name = "Player";
                player.transform.position = new Vector3(0f, 1.0f, 0f);
                Undo.RegisterCreatedObjectUndo(player, "Create Player");
            }

            // Ensure CharacterController component
            CharacterController charController = player.GetComponent<CharacterController>();
            if (charController == null)
            {
                charController = player.AddComponent<CharacterController>();
                charController.height = 2.0f;
                charController.center = new Vector3(0, 0, 0);
            }

            // Remove standard CapsuleCollider if CharacterController is added to prevent physics conflict
            CapsuleCollider capsuleCol = player.GetComponent<CapsuleCollider>();
            if (capsuleCol != null)
            {
                Object.DestroyImmediate(capsuleCol);
            }

            // Add FirstPersonController component
            FirstPersonController fpController = player.GetComponent<FirstPersonController>();
            if (fpController == null)
            {
                fpController = player.AddComponent<FirstPersonController>();
            }

            // 3. Main Camera (FPS Camera) Setup
            Camera mainCam = Camera.main;
            GameObject mainCamObj;
            if (mainCam != null)
            {
                mainCamObj = mainCam.gameObject;
            }
            else
            {
                mainCamObj = new GameObject("Main Camera");
                mainCam = mainCamObj.AddComponent<Camera>();
                mainCamObj.tag = "MainCamera";
                mainCamObj.AddComponent<AudioListener>();
            }

            mainCamObj.transform.SetParent(player.transform);
            mainCamObj.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            mainCamObj.transform.localRotation = Quaternion.identity;

            // Link camera in FP Controller
            SerializedObject fpSerialized = new SerializedObject(fpController);
            fpSerialized.FindProperty("cameraTransform").objectReferenceValue = mainCamObj.transform;
            fpSerialized.ApplyModifiedProperties();

            // Keep the full player body out of the local FPS camera and render hands separately.
            SetupFpsViewmodelTool.ConfigureForPlayer(player, mainCam, false);

            // 4. Create Map Marker for Player
            Transform mapMarkerTransform = player.transform.Find("MapMarker");
            if (mapMarkerTransform == null)
            {
                GameObject markerObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
                markerObj.name = "MapMarker";
                markerObj.transform.SetParent(player.transform);
                markerObj.transform.localPosition = new Vector3(0f, 15.0f, 0f);
                markerObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                markerObj.transform.localScale = new Vector3(3f, 3f, 3f);

                // Destroy mesh collider to avoid physics interaction
                MeshCollider markerCollider = markerObj.GetComponent<MeshCollider>();
                if (markerCollider != null) Object.DestroyImmediate(markerCollider);

                MapMarker markerComp = markerObj.AddComponent<MapMarker>();
                Undo.RegisterCreatedObjectUndo(markerObj, "Create Player Map Marker");
            }

            // 5. Create MapCamera GameObject
            GameObject mapCamObj = GameObject.Find("MapCamera");
            if (mapCamObj == null)
            {
                mapCamObj = new GameObject("MapCamera");
                Camera mapCam = mapCamObj.AddComponent<Camera>();
                mapCam.orthographic = true;
                mapCam.orthographicSize = 30.0f;
                mapCam.nearClipPlane = 0.3f;
                mapCam.farClipPlane = 1000.0f;

                mapCamObj.transform.position = new Vector3(0f, 100.0f, 0f);
                mapCamObj.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

                Undo.RegisterCreatedObjectUndo(mapCamObj, "Create Map Camera");
            }

            RtsMapCameraController mapCamController = mapCamObj.GetComponent<RtsMapCameraController>();
            if (mapCamController == null)
            {
                mapCamController = mapCamObj.AddComponent<RtsMapCameraController>();
            }

            // 6. Create GameManager GameObject
            GameObject gameManager = GameObject.Find("GameManager");
            if (gameManager == null)
            {
                gameManager = new GameObject("GameManager");
                Undo.RegisterCreatedObjectUndo(gameManager, "Create GameManager");
            }

            GameModeManager modeManager = gameManager.GetComponent<GameModeManager>();
            if (modeManager == null)
            {
                modeManager = gameManager.AddComponent<GameModeManager>();
            }

            SerializedObject modeSerialized = new SerializedObject(modeManager);
            modeSerialized.FindProperty("playerController").objectReferenceValue = fpController;
            modeSerialized.FindProperty("fpsCamera").objectReferenceValue = mainCam;
            modeSerialized.FindProperty("mapCameraController").objectReferenceValue = mapCamController;
            modeSerialized.ApplyModifiedProperties();

            RtsUnitCommander unitCommander = gameManager.GetComponent<RtsUnitCommander>();
            if (unitCommander == null)
            {
                unitCommander = gameManager.AddComponent<RtsUnitCommander>();
            }

            SerializedObject commanderSerialized = new SerializedObject(unitCommander);
            commanderSerialized.FindProperty("mapCamera").objectReferenceValue = mapCamObj.GetComponent<Camera>();
            commanderSerialized.FindProperty("gameModeManager").objectReferenceValue = modeManager;
            commanderSerialized.ApplyModifiedProperties();

            // 7. Create a Sample RTS Unit
            GameObject sampleUnit = GameObject.Find("SampleRtsUnit");
            if (sampleUnit == null)
            {
                sampleUnit = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                sampleUnit.name = "SampleRtsUnit";
                sampleUnit.transform.position = new Vector3(10f, 1.0f, 5f);

                RtsUnit unitComp = sampleUnit.AddComponent<RtsUnit>();

                // Add Selection Indicator child
                GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                indicator.name = "SelectionIndicator";
                indicator.transform.SetParent(sampleUnit.transform);
                indicator.transform.localPosition = new Vector3(0f, -0.9f, 0f);
                indicator.transform.localScale = new Vector3(1.5f, 0.05f, 1.5f);

                MeshCollider indCollider = indicator.GetComponent<MeshCollider>();
                if (indCollider != null) Object.DestroyImmediate(indCollider);

                SerializedObject unitSerialized = new SerializedObject(unitComp);
                unitSerialized.FindProperty("selectionIndicator").objectReferenceValue = indicator;
                unitSerialized.ApplyModifiedProperties();

                indicator.SetActive(false);

                Undo.RegisterCreatedObjectUndo(sampleUnit, "Create Sample RTS Unit");
            }

            // Save Scene
            EditorSceneManager.MarkSceneDirty(activeScene);
            bool saved = EditorSceneManager.SaveScene(activeScene, scenePath);

            Debug.Log($"[Battle of Ages] FPS & RTS Scene setup completed! Saved: {saved} to {scenePath}");
        }
    }
}
#endif
