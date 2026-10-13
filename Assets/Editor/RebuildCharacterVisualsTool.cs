#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Replaces the character visuals baked into SampleScene (player hero body, base NPCs) with freshly built models
    /// (rigged + animated when the character library is set up). Keeps the units themselves, their components,
    /// selection rings and markers untouched.
    /// </summary>
    public static class RebuildCharacterVisualsTool
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Battle of Ages/Rebuild Character Visuals (keep units)")]
        public static void Rebuild()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            int rebuilt = 0;

            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                Transform old = player.transform.Find("Hero3DModel");
                if (old != null) Object.DestroyImmediate(old.gameObject);

                GameObject hero = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.HeroPlayer, "Hero3DModel");
                hero.transform.SetParent(player.transform, false);
                hero.transform.localPosition = new Vector3(0f, -1f, 0f); // player pivot is the capsule centre
                // First-person arms (viewmodel camera) are rebuilt too, then hidden-body layers re-applied.
                Camera mainCam = Camera.main != null ? Camera.main : player.GetComponentInChildren<Camera>(true);
                if (mainCam != null) SetupFpsViewmodelTool.ConfigureForPlayer(player, mainCam, false);

                FpsViewmodelSetup fps = player.GetComponent<FpsViewmodelSetup>();
                if (fps != null)
                {
                    fps.SetWorldBodyRoot(hero.transform);
                    fps.Configure();
                }
                rebuilt++;
            }

            foreach (RtsUnit unit in Object.FindObjectsByType<RtsUnit>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (unit.transform == player?.transform) continue;
                Transform old = unit.transform.Find("VisualModel") ?? unit.transform.Find("3DModel");
                if (old == null) continue;

                string n = unit.name;
                var type = (n.Contains("Knight") || n.Contains("Commander") || n.Contains("Guard"))
                    ? HumanoidModelBuilder.HumanType.KnightGuard
                    : HumanoidModelBuilder.HumanType.Villager;

                Object.DestroyImmediate(old.gameObject);
                GameObject model = HumanoidModelBuilder.CreateHumanoidModel(type, "VisualModel");
                model.transform.SetParent(unit.transform, false);
                model.transform.localPosition = Vector3.zero;
                rebuilt++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Battle of Ages] Rebuilt {rebuilt} character visuals.");
        }
    }
}
#endif
