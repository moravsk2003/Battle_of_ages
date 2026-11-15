#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// TextMeshPro's essential resources (its settings, the SDF shaders, a fallback font) into Assets/TextMesh Pro, once:
    /// the interface's text is TextMeshPro (UiTheme.TmpFont). Batch: -executeMethod BattleOfAges.EditorTools.TmpSetupTool.ImportEssentials
    /// </summary>
    public static class TmpSetupTool
    {
        [MenuItem("Battle of Ages/Import TextMeshPro Essentials")]
        public static void ImportEssentials()
        {
            if (File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset")) { Debug.Log("[TmpSetup] already there"); return; }
            string package = null;
            foreach (string dir in Directory.GetDirectories("Library/PackageCache", "com.unity.ugui*"))
            {
                string p = Path.Combine(dir, "Package Resources", "TMP Essential Resources.unitypackage");
                if (File.Exists(p)) package = p;
            }
            if (package == null) { Debug.LogError("[TmpSetup] TMP Essential Resources.unitypackage not found"); return; }
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
            Debug.Log("[TmpSetup] imported " + package);
        }

        /// <summary>
        /// Every uGUI Text left in the game's scenes (the pause menu and labels made by older set-up tools) becomes a
        /// TextMeshPro text with its words, size, colour, alignment and wrapping, in the theme's look. Batch:
        /// -executeMethod BattleOfAges.EditorTools.TmpSetupTool.ConvertScenes
        /// </summary>
        [MenuItem("Battle of Ages/Convert Scene Texts To TextMeshPro")]
        public static void ConvertScenes()
        {
            foreach (string path in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/MainMenu.unity" })
            {
                var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Single);
                int n = 0;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (UnityEngine.UI.Text old in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                    {
                        GameObject go = old.gameObject;
                        string text = old.text;
                        int size = old.fontSize;
                        Color color = old.color;
                        TextAnchor align = old.alignment;
                        FontStyle style = old.fontStyle;
                        bool wrap = old.horizontalOverflow == HorizontalWrapMode.Wrap, raycast = old.raycastTarget, rich = old.supportRichText;
                        foreach (UnityEngine.UI.Shadow fx in go.GetComponents<UnityEngine.UI.Shadow>()) Object.DestroyImmediate(fx, true);
                        Object.DestroyImmediate(old, true);
                        var t = go.AddComponent<TMPro.TextMeshProUGUI>();
                        BattleOfAges.UI.UiTheme.Dress(t, size, align, color, style);
                        // The theme's face and shadow are made at run time: a scene keeps TextMeshPro's own face, and the game
                        // dresses the text when it starts (UiTheme.Restyle)
                        t.font = TMPro.TMP_Settings.defaultFontAsset;
                        t.fontSharedMaterial = t.font != null ? t.font.material : null;
                        t.richText = rich;
                        t.text = text;
                        t.textWrappingMode = wrap ? TMPro.TextWrappingModes.Normal : TMPro.TextWrappingModes.NoWrap;
                        t.raycastTarget = raycast;
                        n++;
                    }
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                Debug.Log($"[TmpSetup] {path}: {n} texts converted");
            }
        }
    }
}
#endif
