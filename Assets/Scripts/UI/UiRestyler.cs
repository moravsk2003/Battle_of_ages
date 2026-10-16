using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Brings every window the older UI scripts build (build menu, inventory, workbench, worker orders, pause, prompts)
    /// to the UiTheme look without rewriting them: new graphics are restyled once as they appear, texts are kept free
    /// of emoji the font cannot draw. Also starts the StrategyHud. Created automatically after a scene loads.
    /// </summary>
    public sealed class UiRestyler : MonoBehaviour
    {
        private readonly HashSet<int> styled = new HashSet<int>();
        private readonly Dictionary<int, string> lastText = new Dictionary<int, string>();
        private readonly List<Graphic> buffer = new List<Graphic>();
        private float timer;

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
        private void Start() => EnsureHud();
        private void OnSceneLoaded(Scene s, LoadSceneMode m) { styled.Clear(); lastText.Clear(); EnsureHud(s.name); }

        private static void EnsureHud(string scene = null)
        {
            if ((scene ?? SceneManager.GetActiveScene().name) == BattleOfAges.SaveLoad.SaveSystem.MenuScene) return;
            if (FindFirstObjectByType<StrategyHud>() == null) new GameObject("StrategyHud").AddComponent<StrategyHud>();
        }

        private void LateUpdate()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.2f;
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.GetComponent<UiThemed>() != null || c.renderMode == RenderMode.WorldSpace) continue;
                c.GetComponentsInChildren(true, buffer);
                foreach (Graphic g in buffer)
                {
                    int id = g.GetInstanceID();
                    if (styled.Add(id))
                    {
                        UiTheme.FixDefaultOffsets(g.rectTransform);
                        if (g.transform.parent is RectTransform parent && styled.Add(parent.GetInstanceID())) UiTheme.FixDefaultOffsets(parent);
                        UiTheme.Restyle(g);
                    }
                    else if (g is Text t && (!lastText.TryGetValue(id, out string last) || !ReferenceEquals(last, t.text)))
                    {
                        string clean = UiTheme.Sanitize(t.text);
                        if (!ReferenceEquals(clean, t.text)) t.text = clean;
                        lastText[id] = t.text;
                    }
                }
            }
        }
    }
}
