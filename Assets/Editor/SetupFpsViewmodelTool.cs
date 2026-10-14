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

            GameObject arms = HumanoidModelBuilder.CreateViewmodelArms("ViewmodelArms", LoadBakedArm);
            SaveArmAssets(arms);
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

        /// <summary>Batch mode entry (-executeMethod): opens SampleScene, rebuilds the arms and saves the scene.</summary>
        public static void ConfigureInSampleScene()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            ConfigureFpsViewmodel();
        }

        // ------------------------------------------------------------------ baked arm meshes

        // The sculpted fists take a moment to build, and the arms are rebuilt on every script reload: the meshes are
        // kept as assets named after a hash of the code that shapes them, and only rebuilt when that code changes.
        private const string ArmFolder = "Assets/Models/Viewmodel";
        private static string signature;

        private static string Signature()
        {
            if (signature != null) return signature;
            var text = new System.Text.StringBuilder();
            foreach (string script in new[] { "ViewmodelArmsBuilder", "MeshKit", "SdfCreature" })
                foreach (string guid in AssetDatabase.FindAssets(script + " t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == script) text.Append(System.IO.File.ReadAllText(path).Replace("\r", ""));
                }
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
                signature = System.BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
            }
            return signature;
        }

        private static string ArmPath(string key) => $"{ArmFolder}/ViewmodelArm_{key}_{Signature()}.asset";

        private static Mesh LoadBakedArm(string key) => AssetDatabase.LoadAssetAtPath<Mesh>(ArmPath(key));

        private static void SaveArmAssets(GameObject arms)
        {
            System.IO.Directory.CreateDirectory(ArmFolder);
            string matPath = ArmFolder + "/ViewmodelArms.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            Material source = ViewmodelArmsBuilder.SharedMaterial();
            if (mat == null)
            {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source); // keep the asset in step with the code
                EditorUtility.SetDirty(mat);
            }
            foreach (MeshRenderer mr in arms.GetComponentsInChildren<MeshRenderer>()) mr.sharedMaterial = mat;

            foreach (MeshFilter mf in arms.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || AssetDatabase.Contains(mf.sharedMesh)) continue;
                string key = mf.name == "RightArm" ? "Right" : "Left";
                foreach (string old in System.IO.Directory.GetFiles(ArmFolder, $"ViewmodelArm_{key}_*.asset")) AssetDatabase.DeleteAsset(old.Replace('\\', '/'));
                AssetDatabase.CreateAsset(mf.sharedMesh, ArmPath(key));
            }
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
