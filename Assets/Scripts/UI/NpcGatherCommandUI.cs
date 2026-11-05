using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Net;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The people's window (K): who of a settlement works where, by district and by job, each settlement on its own tab. Every button
    /// works at once: one more takes the nearest free person, one less sends the person back to working on their own (SettlementLabor).
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcGatherCommandUI : MonoBehaviour
    {
        public static NpcGatherCommandUI Instance { get; private set; }

        private const float Width = 860f, RowH = 42f, RowGap = 4f, RefreshEvery = 0.25f;
        private const int MaxTabs = 6;

        private sealed class Row
        {
            public LaborJob Job;
            public Text Name, Detail, Value;
            public Button Minus, Plus, Free;
        }

        private GameObject window;
        private Text title, summary, said;
        private readonly List<Row> rows = new List<Row>();
        private readonly List<Button> tabs = new List<Button>();
        private TownHall shownHall;
        private float refresh;

        public bool IsOpen { get; private set; }
        /// <summary>The settlement on show (null: the one there is, with no hall yet).</summary>
        public TownHall ShownHall => shownHall;
        /// <summary>The line at the top, for tests.</summary>
        public string SummaryText => summary != null ? summary.text : "";
        /// <summary>What a row's value says now (tests).</summary>
        public string ValueOf(LaborJob job)
        {
            foreach (Row r in rows) if (r.Job == job) return r.Value.text;
            return "";
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            IsOpen = false;
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject); // the scene's old window
            Build();
            KeybindsManager.OnKeybindsChanged += ShowTitle;
        }

        private void OnDestroy()
        {
            KeybindsManager.OnKeybindsChanged -= ShowTitle;
            if (Instance == this) Instance = null;
        }

        private void Start() => CloseMenu();

        private void Update()
        {
            if (Time.frameCount < 2 || Time.time < 0.5f) return;
            if (GameWindows.Paused) return;

            if (IsOpen)
            {
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                if (UniversalInput.GetKeyDownEscape() || KeybindsManager.GetKeyDown(KeyAction.WorkerCommandUI))
                {
                    CloseMenu();
                    return;
                }
                refresh -= Time.unscaledDeltaTime;
                if (refresh <= 0f) Refresh();
                return;
            }
            if (GameWindows.InventoryOpen || GameWindows.BuildMenuOpen) return;
            if (KeybindsManager.GetKeyDown(KeyAction.WorkerCommandUI)) OpenMenu();
        }

        public void ToggleMenu()
        {
            if (IsOpen) CloseMenu();
            else OpenMenu();
        }

        public void OpenMenu()
        {
            IsOpen = true;
            if (window != null) window.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            shownHall = TownHall.Viewed != null ? TownHall.Viewed : TownHall.Current;
            said.text = "";
            ShowTitle();
            Refresh();
        }

        public void CloseMenu()
        {
            IsOpen = false;
            if (window != null) window.SetActive(false);
            if (GameWindows.InventoryOpen || GameWindows.BuildMenuOpen || GameWindows.Paused) return;
            GameModeManager modes = GameContext.Modes;
            if (modes == null || modes.CurrentMode == GameMode.FirstPerson)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // ------------------------------------------------------------------ what it shows

        private void ShowTitle()
        {
            if (title == null) return;
            string key = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.WorkerCommandUI));
            title.text = $"Розподіл людей  <size=17><color=#B8A88E>[{key}]</color></size>";
        }

        private void Refresh()
        {
            refresh = RefreshEvery;
            if (shownHall == null && TownHall.All.Count > 0) shownHall = TownHall.All[0];
            ShowTabs();
            int people = SettlementLabor.PeopleCount(shownHall), free = SettlementLabor.FreeCount(shownHall);
            summary.text = $"Людей: <color=#F5CC73>{people}</color>{(shownHall != null ? $" із {shownHall.Cap}" : "")}   ·   самостійно: <color=#F5CC73>{free}</color>   ·   без діла: <color={(HudIdle(shownHall) > 0 ? "#DB5242" : "#F5CC73")}>{HudIdle(shownHall)}</color>";
            foreach (Row row in rows) ShowRow(row);
        }

        private static int HudIdle(TownHall hall)
        {
            int n = 0;
            foreach (RTS.NpcResourceGatherer w in RTS.WorkerRegistry.ActiveWorkers)
                if (w != null && w.IsIdle && (hall == null || TownHall.All.Count <= 1 || TownHall.Nearest(w.transform.position) == hall)) n++;
            return n;
        }

        private void ShowRow(Row row)
        {
            LaborRow r = SettlementLabor.RowOf(row.Job, shownHall);
            row.Value.text = r.Places >= 0 ? $"{r.Hands} / {r.Places}" : r.Hands.ToString();
            bool building = r.Places >= 0;
            string detail = "";
            if (building) detail = r.Buildings == 0 ? (row.Job == LaborJob.Building ? "нічого не будується" : "ще не збудовано") : $"споруд: {r.Buildings}";
            if (r.Limit >= 0) detail += (detail.Length > 0 ? "  ·  " : "") + $"самі не більше {r.Limit}";
            row.Detail.text = detail;
            row.Minus.interactable = r.Hands > 0;
            row.Plus.interactable = !building || r.Hands < r.Places;
            if (row.Free != null && row.Free.gameObject.activeSelf != (r.Limit >= 0)) row.Free.gameObject.SetActive(r.Limit >= 0);
        }

        private void ShowTabs()
        {
            IReadOnlyList<TownHall> halls = TownHall.All;
            bool several = halls.Count > 1;
            for (int i = 0; i < tabs.Count; i++)
            {
                bool on = several && i < halls.Count;
                if (tabs[i].gameObject.activeSelf != on) tabs[i].gameObject.SetActive(on);
                if (!on) continue;
                Text label = tabs[i].GetComponentInChildren<Text>();
                label.text = $"{halls[i].Title} {Crafting.ItemTypeNames.Roman(halls[i].Level)}";
                label.color = halls[i] == shownHall ? UiTheme.Gold : UiTheme.Ink;
            }
        }

        private void Order(LaborJob job, LaborCommand.Kind kind)
        {
            said.text = "";
            CommandBus.Submit(new LaborCommand { Order = kind, Job = job, Hall = WorldEntity.IdOf(shownHall).Value });
            Refresh();
        }

        /// <summary>A refusal in the window itself, where the player is looking.</summary>
        public void Say(string text)
        {
            if (IsOpen && said != null) said.text = text;
            else StrategyHud.Notify(text);
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;
            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (GetComponent<UiThemed>() == null) gameObject.AddComponent<UiThemed>();

            int count = SettlementLabor.Districts.Length + SettlementLabor.Jobs.Length;
            float top = 148f, height = top + count * (RowH + RowGap) + 2f * 30f + 74f;
            RectTransform panel = UiTheme.Rect(transform, "PeoplePanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, height));
            window = panel.gameObject;
            UiTheme.PanelImage(panel);
            PopIn.On(panel);

            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(600f, 44f));
            title = UiTheme.Label(head, "Text", "", 28, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            title.supportRichText = true;
            ShowTitle();
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-14f, -10f), new Vector2(44f, 42f), CloseMenu, 26);

            // One tab per settlement (shown only with more than one)
            const float tabW = 132f;
            for (int i = 0; i < MaxTabs; i++)
            {
                int index = i;
                Button tab = UiTheme.TextButton(panel, "Tab" + i, "", new Vector2(0f, 1f), new Vector2(22f + i * (tabW + 4f), -58f), new Vector2(tabW, 34f), () =>
                {
                    IReadOnlyList<TownHall> halls = TownHall.All;
                    if (index < halls.Count) { shownHall = halls[index]; said.text = ""; Refresh(); }
                }, 14);
                tab.gameObject.SetActive(false);
                tabs.Add(tab);
            }

            RectTransform sumRect = UiTheme.Rect(panel, "Summary", new Vector2(0f, 1f), new Vector2(24f, -96f), new Vector2(Width - 48f, 26f));
            summary = UiTheme.Label(sumRect, "Text", "", 17, TextAnchor.MiddleLeft, UiTheme.Ink);
            summary.supportRichText = true;
            RectTransform saidRect = UiTheme.Rect(panel, "Said", new Vector2(0f, 1f), new Vector2(24f, -120f), new Vector2(Width - 48f, 24f));
            said = UiTheme.Label(saidRect, "Text", "", 15, TextAnchor.MiddleLeft, UiTheme.Bad);

            float y = -top;
            y = Section(panel, "Райони: люди при спорудах", y);
            foreach (LaborJob job in SettlementLabor.Districts) y = MakeRow(panel, job, y);
            y = Section(panel, "Робота без споруд", y);
            foreach (LaborJob job in SettlementLabor.Jobs) y = MakeRow(panel, job, y);

            Button allFree = UiTheme.TextButton(panel, "AllFree", "Усім: самостійно", new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(250f, 46f), () => Order(LaborJob.Standing, LaborCommand.Kind.AllOnTheirOwn), 16);
            Tooltip.Set(allFree.gameObject, () => TipText.Title("Усім: самостійно", UiTheme.Gold) + "\nУсі, хто не при споруді, самі беруться за те, чого бракує. Хто стоїть, теж іде працювати.");
            Button clear = UiTheme.TextButton(panel, "ClearLimits", "Зняти обмеження", new Vector2(0f, 0f), new Vector2(284f, 16f), new Vector2(250f, 46f), () => Order(LaborJob.Fields, LaborCommand.Kind.ClearLimits), 16);
            Tooltip.Set(clear.gameObject, () => TipText.Title("Зняти обмеження", UiTheme.Gold) + "\nРайони знову беруть вільних людей, скільки є місць.");

            window.SetActive(false);
        }

        private static float Section(RectTransform panel, string text, float y)
        {
            RectTransform r = UiTheme.Rect(panel, "Section", new Vector2(0f, 1f), new Vector2(24f, y), new Vector2(Width - 48f, 26f));
            UiTheme.Label(r, "Text", text, 16, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            return y - 30f;
        }

        private float MakeRow(RectTransform panel, LaborJob job, float y)
        {
            var row = new Row { Job = job };
            RectTransform r = UiTheme.Rect(panel, "Row_" + job, new Vector2(0f, 1f), new Vector2(18f, y), new Vector2(Width - 36f, RowH));
            UiTheme.PanelImage(r, UiTheme.ButtonSprite, new Color(0.72f, 0.7f, 0.68f));
            Tooltip.Set(r.gameObject, () => TipText.Title(SettlementLabor.NameOf(job), UiTheme.Gold) + "\n" + SettlementLabor.TipOf(job));

            RectTransform name = UiTheme.Rect(r, "Name", new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(240f, RowH));
            row.Name = UiTheme.Label(name, "Text", SettlementLabor.NameOf(job), 17, TextAnchor.MiddleLeft, UiTheme.Ink, FontStyle.Bold);
            RectTransform detail = UiTheme.Rect(r, "Detail", new Vector2(0f, 0.5f), new Vector2(254f, 0f), new Vector2(290f, RowH));
            row.Detail = UiTheme.Label(detail, "Text", "", 14, TextAnchor.MiddleLeft, UiTheme.InkDim);

            row.Free = UiTheme.TextButton(r, "Free", "×", new Vector2(1f, 0.5f), new Vector2(-192f, 0f), new Vector2(30f, 30f), () => Order(job, LaborCommand.Kind.ClearLimits), 16);
            Tooltip.Set(row.Free.gameObject, () => TipText.Title("Зняти обмеження", UiTheme.Gold) + "\nРайон знову бере вільних людей, скільки є місць.");
            row.Free.gameObject.SetActive(false);

            row.Minus = UiTheme.TextButton(r, "Minus", "−", new Vector2(1f, 0.5f), new Vector2(-140f, 0f), new Vector2(44f, 34f), () => Order(job, LaborCommand.Kind.Remove), 22);
            RectTransform value = UiTheme.Rect(r, "Value", new Vector2(1f, 0.5f), new Vector2(-62f, 0f), new Vector2(72f, 34f));
            UiTheme.PanelImage(value, UiTheme.SlotSprite).raycastTarget = false;
            row.Value = UiTheme.Label(value, "Text", "0", 17, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            row.Plus = UiTheme.TextButton(r, "Plus", "+", new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(44f, 34f), () => Order(job, LaborCommand.Kind.Add), 22);

            rows.Add(row);
            return y - RowH - RowGap;
        }
    }
}
