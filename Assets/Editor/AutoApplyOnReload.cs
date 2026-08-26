#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BattleOfAges.EditorTools
{
    [InitializeOnLoad]
    public static class AutoApplyOnReload
    {
        static AutoApplyOnReload()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode) return;

                // Auto setup Pause Menu if missing in open scene
                if (GameObject.Find("PauseCanvas") == null)
                {
                    SetupPauseMenuTool.SetupPauseMenu();
                }

                // Auto setup 3D Hero and NPCs if missing in open scene
                GameObject player = GameObject.Find("Player");
                if (player != null && player.transform.Find("Hero3DModel") == null)
                {
                    SetupCharacterModelsTool.SetupCharacterModels();
                }

                if (player != null)
                {
                    SetupFpsViewmodelTool.ConfigureFpsViewmodel();
                }
            };
        }
    }
}
#endif
