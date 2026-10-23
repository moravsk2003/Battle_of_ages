#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Replaces the props baked into SampleScene (the base workbench) with freshly built ones, each in the same place,
    /// parent, rotation, scale and name, after their builder changed (as RebuildSceneTreesTool does for the trees).
    /// Batch: -executeMethod BattleOfAges.EditorTools.RebuildScenePropsTool.Rebuild
    /// </summary>
    public static class RebuildScenePropsTool
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        private static readonly (string name, Func<string, GameObject> build)[] Props =
        {
            ("BaseWorkbench", WorkbenchNode.CreateWorkbenchPrefab),
        };

        [MenuItem("Battle of Ages/Rebuild Scene Props")]
        public static void Rebuild()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int rebuilt = 0;
            foreach (var (name, build) in Props)
            {
                GameObject o = GameObject.Find(name);
                if (o == null) { Debug.LogWarning($"[RebuildSceneProps] {name} not found"); continue; }
                GameObject fresh = build(name);
                fresh.transform.SetParent(o.transform.parent, false);
                fresh.transform.SetLocalPositionAndRotation(o.transform.localPosition, o.transform.localRotation);
                fresh.transform.localScale = o.transform.localScale;
                fresh.transform.SetSiblingIndex(o.transform.GetSiblingIndex());
                fresh.layer = o.layer;
                fresh.tag = o.tag;
                UnityEngine.Object.DestroyImmediate(o);
                rebuilt++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[RebuildSceneProps] Rebuilt {rebuilt} props in {ScenePath}");
        }
    }
}
#endif
