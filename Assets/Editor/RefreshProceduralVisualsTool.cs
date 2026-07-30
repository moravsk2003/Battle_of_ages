#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace BattleOfAges.EditorTools
{
    /// <summary>Regenerates scene-owned procedural meshes after art generator changes.</summary>
    public static class RefreshProceduralVisualsTool
    {
        [MenuItem("Battle of Ages/Refresh Improved Models & Textures")]
        public static void RefreshVisuals()
        {
            const string scenePath = "Assets/Scenes/SampleScene.unity";
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            // Characters own their model hierarchies; the terrain owns trees, ore and the chest.
            SetupCharacterModelsTool.SetupCharacterModels();
            SetupSteppeEnvironmentTool.SetupSteppeEnvironment();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath);
        }
    }
}
#endif
