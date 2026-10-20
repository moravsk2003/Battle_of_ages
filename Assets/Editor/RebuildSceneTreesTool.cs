#if UNITY_EDITOR
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.Utils;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Replaces the trees baked into SampleScene with freshly built ones (SteppeTreeBuilder), each in the same place,
    /// parent, rotation, scale, name and kind (a spruce if its name says "Pine", else broadleaf; the variant from the
    /// number at the end of its name). Batch: -executeMethod BattleOfAges.EditorTools.RebuildSceneTreesTool.Rebuild
    /// </summary>
    public static class RebuildSceneTreesTool
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Battle of Ages/Rebuild Scene Trees")]
        public static void Rebuild()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int rebuilt = 0;
            foreach (ResourceNodeTree old in Object.FindObjectsByType<ResourceNodeTree>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (PrefabUtility.IsPartOfPrefabAsset(old)) continue;
                GameObject o = old.gameObject;
                Match m = Regex.Match(o.name, @"(\d+)\s*$");
                int variant = m.Success ? int.Parse(m.Groups[1].Value) : rebuilt;
                bool pine = o.name.Contains("Pine");
                GameObject fresh = pine ? SteppeTreeBuilder.CreatePineTree(o.name, variant) : SteppeTreeBuilder.CreateSteppeTree(o.name, variant);
                fresh.transform.SetParent(o.transform.parent, false);
                fresh.transform.SetLocalPositionAndRotation(o.transform.localPosition, o.transform.localRotation);
                fresh.transform.localScale = o.transform.localScale;
                fresh.transform.SetSiblingIndex(o.transform.GetSiblingIndex());
                fresh.layer = o.layer;
                fresh.tag = o.tag;
                fresh.SetActive(o.activeSelf);
                Object.DestroyImmediate(o);
                rebuilt++;
            }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[RebuildSceneTrees] Rebuilt {rebuilt} trees in {ScenePath}");
        }
    }
}
#endif
