using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Starts the game UI after a scene loads: the StrategyHud, and — once, a frame later, when every window there has
    /// built itself — the UiTheme look for the older windows built with default looks (inventory, worker orders, the
    /// pause menu from the scene). Their rows made later restyle themselves (UiTheme.RestyleTree). It used to poll every
    /// canvas five times a second for new graphics; now nothing here runs per frame.
    /// </summary>
    public sealed class UiRestyler : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<UiRestyler>() != null) return;
            var go = new GameObject("UiRestyler");
            DontDestroyOnLoad(go);
            go.AddComponent<UiRestyler>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private void Start() => StartCoroutine(SetUp(SceneManager.GetActiveScene().name));
        private void OnSceneLoaded(Scene s, LoadSceneMode m) => StartCoroutine(SetUp(s.name));

        private static IEnumerator SetUp(string scene)
        {
            if (scene != SaveSystem.MenuScene && FindFirstObjectByType<StrategyHud>() == null) new GameObject("StrategyHud").AddComponent<StrategyHud>();
            yield return null; // the scene's windows build in Awake and Start
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace || c.GetComponent<UiThemed>() != null) continue;
                UiTheme.RestyleTree(c.transform);
                c.gameObject.AddComponent<UiThemed>();
            }
            Managers.DisplaySettings.ApplyScale();
        }
    }
}
