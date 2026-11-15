using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Starts the game UI after a scene loads: the StrategyHud, and a frame later, when every window there has built itself,
    /// the interface's scale. Each window wears the theme from its own building (UiTheme's builders; the inventory and the
    /// pause menu from the scene restyle themselves once): there is no pass over every canvas any more (UiRestyler was one).
    /// </summary>
    public sealed class UiBoot : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<UiBoot>() != null) return;
            var go = new GameObject("UiBoot");
            DontDestroyOnLoad(go);
            go.AddComponent<UiBoot>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private void Start() => StartCoroutine(SetUp(SceneManager.GetActiveScene().name));
        private void OnSceneLoaded(Scene s, LoadSceneMode m) => StartCoroutine(SetUp(s.name));

        private static IEnumerator SetUp(string scene)
        {
            if (scene != SaveSystem.MenuScene && FindFirstObjectByType<StrategyHud>() == null) new GameObject("StrategyHud").AddComponent<StrategyHud>();
            yield return null; // the scene's windows build in Awake and Start
            Managers.DisplaySettings.ApplyScale();
        }
    }
}
