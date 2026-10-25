using System;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.InputSystem;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// "First steps" (docs/GAME_DESIGN.md §21, stage 1.8): the next goal of the opening in the corner — what to do and
    /// how (keys) — with how many are done. Goals already met are skipped; the × hides it (settings bring it back).
    /// </summary>
    public sealed class Advisor : MonoBehaviour
    {
        private const string HiddenKey = "Settings_AdvisorHidden";

        public static bool Hidden
        {
            get => PlayerPrefs.GetInt(HiddenKey, 0) == 1;
            set { PlayerPrefs.SetInt(HiddenKey, value ? 1 : 0); if (instance != null) instance.Refresh(); }
        }

        private static Advisor instance;
        private RectTransform panel;
        private Text title, hint, count;
        private float timer;

        private static string K(KeyAction a) => TipText.Key(HudTips.Key(a));
        private static bool Has(BuildingType t) { foreach (BuildingNode b in BuildingNode.Active) if (b != null && b.Type == t) return true; return false; }

        /// <summary>The goals in order: (what to do, how, done?).</summary>
        public static readonly (string goal, Func<string> how, Func<bool> done)[] Goals =
        {
            ("Збудуйте скриню-склад", () => $"Мапа {K(KeyAction.ToggleGameMode)}, будівництво {K(KeyAction.BuildMenu)}: сюди робітники несуть ресурси.", () => StorageChestNode.ActiveChests.Count > 0),
            ("Назбирайте 20 деревини", () => $"Самі: гілки {K(KeyAction.InteractGather)}, рубка {K(KeyAction.ChopTree)}. Робітникам — наказ «Рубати».", () => ResourceManager.Instance != null && ResourceManager.Instance.WoodCount >= 20),
            ("Збудуйте шахту", () => "Шахта дає руду; ПКМ робітниками по шахті — працювати в ній.", () => MineNode.ActiveMines.Count > 0),
            ("Збудуйте верстак", () => "На верстаку робітник кує зброю й броню для всіх.", () => WorkbenchNode.Instance != null),
            ("Викуйте першу річ", () => $"Верстак ({K(KeyAction.InteractGather)} біля нього): покличте робітника й оберіть рецепт.", () => Armory.Instance.Items.Count > 0),
            ("Збудуйте ратушу", () => "Ратуша піднімає стелю населення; робітники народжуються самі, поки є місце.", () => TownHall.Current != null),
            ("Досягніть 10 жителів", () => "Будинок робітника дає ще одного; і приріст іде сам.", () => TownHall.Population >= 10),
            ("Покращте ратушу до II", () => "На мапі над ратушею — кнопка покращення (потрібна руда вищого рівня).", () => TownHall.Current != null && TownHall.Current.Level >= 2),
            ("Збудуйте стіну з воротами", () => "Стіни тримають набіги; лучники на стіні б'ють далі. Тривога " + K(KeyAction.Alarm) + " ставить їх на пости.", () => Has(BuildingType.WallGate)),
        };

        public static int DoneCount { get { int n = 0; foreach (var g in Goals) if (g.done()) n++; return n; } }

        /// <summary>The first goal not met yet, or -1 when all are.</summary>
        public static int Current
        {
            get
            {
                for (int i = 0; i < Goals.Length; i++) if (!Goals[i].done()) return i;
                return -1;
            }
        }

        public static Advisor Create(RectTransform hudRoot)
        {
            var go = new GameObject("Advisor", typeof(RectTransform));
            go.transform.SetParent(hudRoot, false);
            Advisor a = go.AddComponent<Advisor>();
            a.Build((RectTransform)go.transform);
            instance = a;
            return a;
        }

        private void Build(RectTransform root)
        {
            // The component lives on a full-screen holder so it keeps running while the panel is hidden
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            panel = UiTheme.Rect(root, "Panel", new Vector2(1f, 1f), new Vector2(-18f, -250f), new Vector2(380f, 118f));
            UiTheme.PanelImage(panel);
            RectTransform head = UiTheme.Rect(panel, "Head", new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(250f, 24f));
            UiTheme.Label(head, "Text", "Порада", 15, TextAnchor.MiddleLeft, UiTheme.InkDim, FontStyle.Bold);
            RectTransform cnt = UiTheme.Rect(panel, "Count", new Vector2(1f, 1f), new Vector2(-46f, -8f), new Vector2(80f, 24f));
            count = UiTheme.Label(cnt, "Text", "", 15, TextAnchor.MiddleRight, UiTheme.InkDim);
            RectTransform t = UiTheme.Rect(panel, "Goal", new Vector2(0f, 1f), new Vector2(16f, -32f), new Vector2(350f, 28f));
            title = UiTheme.Label(t, "Text", "", 20, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            RectTransform h = UiTheme.Rect(panel, "How", new Vector2(0f, 1f), new Vector2(16f, -62f), new Vector2(350f, 48f));
            hint = UiTheme.Label(h, "Text", "", 15, TextAnchor.UpperLeft, UiTheme.Ink);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.supportRichText = true;
            Button close = UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(30f, 28f), () => Hidden = true, 20);
            Tooltip.Set(close.gameObject, "Сховати поради" + "\n" + TipText.Dim("Повернути — у налаштуваннях"));
            Refresh();
        }

        private void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            Refresh();
        }

        private string shownGoal;

        private void Refresh()
        {
            int i = Current;
            bool show = !Hidden && i >= 0;
            if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
            if (!show) return;
            count.text = $"{DoneCount} / {Goals.Length}";
            if (shownGoal != Goals[i].goal && shownGoal != null) StrategyHud.Notify("Ціль виконано!");
            shownGoal = Goals[i].goal;
            title.text = Goals[i].goal;
            hint.text = Goals[i].how();
        }
    }
}
