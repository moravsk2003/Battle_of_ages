using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.CameraSystem;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.World;
using TMPro;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The buttons over the map (a campaign's): "Будівництво" opens the zones and the buildings, "Стерти" takes up the
    /// eraser, the view goes back to its usual angle or to the hero or the base. While a brush is up, a line says what the
    /// zone gives, what is marked and what comes next.
    /// </summary>
    public sealed class ZoneToolbar : MonoBehaviour
    {
        private const float TopY = -72f, RowH = 46f, RowGap = 6f, Step = 118f, ButtonW = 112f, SizeStep = 50f, MainStep = 166f, MainW = 160f;
        private static readonly string[] SizeNamesUk = { "М", "С", "В" };
        private static readonly string[] SizeNamesEn = { "S", "M", "L" };
        private static readonly string[] SizeTipsUk = { "малий", "середній", "великий" };
        private static readonly string[] SizeTipsEn = { "small", "medium", "large" };

        /// <summary>The toolbar on the screen: B opens its building row.</summary>
        public static ZoneToolbar Instance { get; private set; }

        /// <summary>Whether the building row is open (tests).</summary>
        public bool BuildOpen => open;

        private float nextCounts;
        private TMP_Text[] zoneLabels;
        private Button build, eraser;
        private Button[] zoneButtons;
        private Button[] sizes;
        private GameObject row, zonesRow;
        private RectTransform statusPanel;
        private TMP_Text status;
        private bool open;
        private float nextStatus;
        private int baseIndex;

        public static ZoneToolbar Create(Transform parent)
        {
            var go = new GameObject("ZoneToolbar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            ZoneToolbar bar = go.AddComponent<ZoneToolbar>();
            Instance = bar;
            bar.Build(rt);
            return bar;
        }

        private void Build(RectTransform root)
        {
            RectTransform rowRect = UiTheme.Child(root, "Row", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            row = rowRect.gameObject;

            // The first row: always there
            float x = -2.5f * MainStep;
            Button Main(string name, string text, UnityEngine.Events.UnityAction click, System.Func<string> tip)
            {
                Button b = UiTheme.TextButton(rowRect, name, text, new Vector2(0.5f, 1f), new Vector2(x, TopY), new Vector2(MainW, RowH), click, 16);
                Tooltip.Set(b.gameObject, tip);
                x += MainStep;
                return b;
            }
            build = Main("Build", "Будівництво", ToggleBuild, () => TipText.Title(Loc.Pick("Будівництво", "Build"), UiTheme.Gold) + Loc.Pick("\nЗони, які робітники забудовують самі, і споруди: ратуша, стіни, скриня.", "\nZones workers build automatically, plus structures: town hall, walls, chest."));
            eraser = Main("Eraser", "Стерти", ToggleEraser, () => TipText.Title(Loc.Pick("Стерти розмітку", "Erase Zones"), UiTheme.Gold) + Loc.Pick("\nТягніть ЛКМ по розмітці. Споруди, що вже стоять, лишаються.\n", "\nDrag LMB over painted zones. Built structures stay.\n") + TipText.Dim(Loc.Pick("Також клавіша Delete.", "Also press Delete.")));
            Button people = Main("People", "Люди", () => NpcGatherCommandUI.Instance?.ToggleMenu(), () => TipText.Title(Loc.Pick("Розподіл людей", "Worker Assignment"), UiTheme.Gold) + Loc.Pick("\nХто де працює: райони, робота, кожне поселення окремо.", "\nWho works where: districts, jobs, per settlement.") + "  " + TipText.Key(HudTips.Key(InputSystem.KeyAction.WorkerCommandUI)));
            Main("ResetView", "Скинути вигляд", ResetView, () => TipText.Title(Loc.Pick("Скинути вигляд", "Reset View"), UiTheme.Gold) + Loc.Pick("\nЗвичайний нахил, поворот і масштаб мапи.", "\nDefault map tilt, rotation, and zoom."));
            Main("ToHero", "До героя", ToHero, () => TipText.Title(Loc.Pick("До героя", "To Hero"), UiTheme.Gold) + Loc.Pick("\nНавести мапу на свого героя.", "\nCenter map on your hero."));
            Main("ToBase", "До бази", ToBase, () => TipText.Title(Loc.Pick("До бази", "To Base"), UiTheme.Gold) + Loc.Pick("\nНавести мапу на ратушу. Натискайте ще раз, щоб перейти до наступного поселення.", "\nCenter map on Town Hall. Press again to cycle settlements."));

            // The second row: opened by "Будівництво"
            RectTransform zonesRect = UiTheme.Child(rowRect, "Zones", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            zonesRow = zonesRect.gameObject;
            float zonesY = TopY - RowH - RowGap;
            ZoneType[] types = ZoneMap.Types;
            float width = (types.Length + 1) * Step + SizeNamesUk.Length * SizeStep + 14f;
            x = -width * 0.5f + ButtonW * 0.5f;
            zoneButtons = new Button[types.Length];
            zoneLabels = new TMP_Text[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                ZoneType type = types[i];
                zoneButtons[i] = UiTheme.TextButton(zonesRect, "Zone_" + type, ZoneMap.Name(type), new Vector2(0.5f, 1f), new Vector2(x, zonesY), new Vector2(ButtonW, RowH), () => Toggle(type), 16);
                zoneLabels[i] = zoneButtons[i].GetComponentInChildren<TMP_Text>();
                LocText.Detach(zoneLabels[i]);
                RectTransform stripe = UiTheme.Child(zoneButtons[i].transform, "Stripe", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(7f, 5f), new Vector2(-7f, 11f));
                Image img = stripe.gameObject.AddComponent<Image>();
                Color c = ZoneMap.Colour(type);
                c.a = 1f;
                img.color = c;
                img.raycastTarget = false;
                Tooltip.Set(zoneButtons[i].gameObject, () => TipText.Title(ZoneMap.Name(type), UiTheme.Gold) + "\n" + ZoneMap.Description(type));
                x += Step;
            }
            Button buildings = UiTheme.TextButton(zonesRect, "Buildings", "Споруди", new Vector2(0.5f, 1f), new Vector2(x, zonesY), new Vector2(ButtonW, RowH), OpenBuildings, 16);
            Tooltip.Set(buildings.gameObject, () => TipText.Title(Loc.Pick("Споруди", "Buildings"), UiTheme.Gold) + Loc.Pick("\nРатуша, стіни, ворота, скриня: їх ставите самі, зводять робітники.", "\nTown Hall, walls, gates, chest: placed manually, built by workers."));
            x += ButtonW * 0.5f + 14f + 22f;
            sizes = new Button[SizeNamesUk.Length];
            for (int i = 0; i < SizeNamesUk.Length; i++)
            {
                int index = i;
                sizes[i] = UiTheme.TextButton(zonesRect, "Size" + i, Loc.En ? SizeNamesEn[i] : SizeNamesUk[i], new Vector2(0.5f, 1f), new Vector2(x, zonesY), new Vector2(44f, RowH), () => ZonePainter.BrushIndex = index, 17);
                LocText.Detach(sizes[i].GetComponentInChildren<TMP_Text>());
                Tooltip.Set(sizes[i].gameObject, () => TipText.Title(Loc.Pick($"Пензель {SizeTipsUk[index]}", $"Brush: {SizeTipsEn[index]}"), UiTheme.Gold) + Loc.Pick($"\nРадіус {ZonePainter.Brushes[index]:0} м.", $"\nRadius {ZonePainter.Brushes[index]:0} m."));
                x += SizeStep;
            }

            statusPanel = UiTheme.Rect(rowRect, "Status", new Vector2(0.5f, 1f), new Vector2(0f, zonesY - RowH - RowGap), new Vector2(width - 6f, 84f));
            UiTheme.PanelImage(statusPanel);
            RectTransform text = UiTheme.Child(statusPanel, "Text", Vector2.zero, Vector2.one, new Vector2(16f, 6f), new Vector2(-148f, -6f));
            status = UiTheme.Label(text, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.Ink);
            LocText.Detach(status);
            status.richText = true;
            status.textWrappingMode = TextWrappingModes.Normal;
            UiTheme.TextButton(statusPanel, "Done", "Готово\n<size=12>Esc</size>", new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(120f, 56f), ZonePainter.Stop, 16)
                .GetComponentInChildren<TMP_Text>().richText = true;
            statusPanel.gameObject.SetActive(false);
            zonesRow.SetActive(false);
            Apply();
            Loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            Loc.Changed -= OnLanguageChanged;
            if (Instance == this) Instance = null;
        }

        private void OnLanguageChanged()
        {
            if (sizes != null)
            {
                string[] names = Loc.En ? SizeNamesEn : SizeNamesUk;
                for (int i = 0; i < sizes.Length && i < names.Length; i++)
                {
                    TMP_Text t = sizes[i] != null ? sizes[i].GetComponentInChildren<TMP_Text>() : null;
                    if (t != null) t.text = names[i];
                }
            }
            nextCounts = 0f;
            nextStatus = 0f;
            if (open) ShowCounts();
            if (statusPanel != null && statusPanel.gameObject.activeSelf && status != null) status.text = StatusText();
        }

        /// <summary>Opens or closes the row of zones and buildings.</summary>
        public void ToggleBuild()
        {
            open = !open;
            if (!open) ZonePainter.Stop();
            zonesRow.SetActive(open);
        }

        private static void ToggleEraser()
        {
            if (ZonePainter.Erasing) ZonePainter.Stop();
            else ZonePainter.BeginEraser();
        }

        private static void Toggle(ZoneType type)
        {
            if (ZonePainter.Tool == type) ZonePainter.Stop();
            else ZonePainter.Begin(type);
        }

        private static void OpenBuildings()
        {
            ZonePainter.Stop();
            BuildMenuUI.Instance?.ToggleMenu();
        }

        private static void ResetView() => GameContext.MapCamera?.ResetView();

        private static void ToHero()
        {
            GameObject hero = GameContext.Player;
            if (hero != null) GameContext.MapCamera?.LookAtGround(hero.transform.position);
        }

        /// <summary>The hall nearest the hero first, then each settlement in turn.</summary>
        private void ToBase()
        {
            RtsMapCameraController map = GameContext.MapCamera;
            if (map == null) return;
            var halls = TownHall.All;
            GameObject hero = GameContext.Player;
            if (halls.Count == 0)
            {
                if (hero != null) map.LookAtGround(StorageChestNode.GetNearestChestDepositPosition(hero.transform.position));
                return;
            }
            baseIndex = (baseIndex + 1) % halls.Count;
            map.LookAtGround(halls[baseIndex].transform.position);
        }

        private void Update()
        {
            bool campaign = WorldSettings.Bounded;
            if (row.activeSelf != campaign) row.SetActive(campaign);
            if (!campaign) return;
            if (ZonePainter.Painting && !open) { open = true; zonesRow.SetActive(true); } // the eraser, Delete: the sizes in reach
            Apply();
            if (open && Time.unscaledTime >= nextCounts) { nextCounts = Time.unscaledTime + 0.5f; ShowCounts(); }
            bool painting = ZonePainter.Painting;
            if (statusPanel.gameObject.activeSelf != painting) { statusPanel.gameObject.SetActive(painting); nextStatus = 0f; }
            if (!painting || Time.unscaledTime < nextStatus) return;
            nextStatus = Time.unscaledTime + 0.25f;
            status.text = StatusText();
        }

        /// <summary>On each zone's button how it stands: the buildings there of those its cells call for.</summary>
        private void ShowCounts()
        {
            ZoneType[] types = ZoneMap.Types;
            for (int i = 0; i < types.Length; i++) zoneLabels[i].text = LabelOf(types[i], TownHall.Viewed);
        }

        public static string LabelOf(ZoneType zone, TownHall hall)
        {
            ZoneGrower.Progress(zone, hall, out int cells, out int wanted, out int have, out int sites);
            return cells == 0 && have == 0 ? ZoneMap.Name(zone) : $"{ZoneMap.Name(zone)} {have - sites}/{wanted}";
        }

        /// <summary>What is in hand lit up; trade waits for a hall II.</summary>
        private void Apply()
        {
            ZoneType[] types = ZoneMap.Types;
            for (int i = 0; i < types.Length; i++)
            {
                Light(zoneButtons[i], ZonePainter.Tool == types[i]);
                if (types[i] == ZoneType.Trade)
                {
                    TownHall hall = TownHall.Viewed;
                    bool canTrade = hall != null && hall.Level >= 2;
                    if (zoneButtons[i].interactable != canTrade) zoneButtons[i].interactable = canTrade;
                    if (!canTrade && ZonePainter.Tool == ZoneType.Trade) ZonePainter.Stop();
                }
            }
            Light(build, open);
            Light(eraser, ZonePainter.Erasing);
            for (int i = 0; i < sizes.Length; i++) Light(sizes[i], ZonePainter.BrushIndex == i);
        }

        /// <summary>A gold frame and gold letters on what is in hand (a tint is lost on the dark button).</summary>
        private static void Light(Button b, bool on)
        {
            Outline frame = b.GetComponent<Outline>();
            if (frame == null)
            {
                frame = b.gameObject.AddComponent<Outline>();
                frame.effectColor = UiTheme.Gold;
                frame.effectDistance = new Vector2(3f, -3f);
                frame.enabled = false;
            }
            if (frame.enabled == on) return;
            frame.enabled = on;
            TMP_Text label = b.GetComponentInChildren<TMP_Text>();
            if (label != null) label.color = on ? UiTheme.Gold : UiTheme.Ink;
        }

        /// <summary>What the brush in hand does, what is marked and what comes next.</summary>
        public static string StatusText()
        {
            if (ZonePainter.Erasing) return Loc.Pick("<b>Стерти</b>: тягніть ЛКМ по розмітці. Споруди, що вже стоять, лишаються.", "<b>Erase</b>: drag LMB over painted zones. Built structures stay.");
            ZoneType zone = ZonePainter.Tool;
            if (zone == ZoneType.None) return "";
            TownHall hall = TownHall.Viewed;
            ZoneGrower.Progress(zone, hall, out int cells, out int wanted, out int have, out int sites);
            string head = $"<b>{ZoneMap.Description(zone)}</b>\n";
            if (cells == 0) return head + Loc.Pick("Тягніть ЛКМ по землі в межах золотого кола, ПКМ стирає. Робітники зведуть споруди самі.", "Drag LMB inside the golden circle, RMB erases. Workers will construct buildings automatically.");

            BuildingDefinition def = BuildingData.GetDefinition(ZoneGrower.BuildingOf(zone));
            int next = ZoneGrower.CellsPerBuilding(zone) * wanted + 1 - cells;
            string text = head + Loc.Pick(
                $"Розмічено {cells} кл.: споруд {wanted}, стоїть {have - sites}" + (sites > 0 ? $", будується {sites}" : "") + $". Ще одна — за {next} кл. Вільні люди йдуть самі; інших: виділити, ПКМ по споруді.",
                $"Marked {cells} cells: {wanted} buildings, {have - sites} built" + (sites > 0 ? $", {sites} under construction" : "") + $". Next in {next} cells. Idle people assign themselves; others: select, RMB building.");
            if (have >= wanted) return text;
            ResourceManager rm = ResourceManager.Instance;
            SettlementStock stock = rm == null ? null : hall != null ? hall.Stock : rm.Main;
            if (stock != null && (stock.WoodCount < def.WoodCost || stock.GetOreCountAtLeast(def.OreTierRequired) < def.OreCost))
                return text + Loc.Pick($" <color=#DB5242>Для «{def.DisplayName}» треба {def.WoodCost} дерева й {def.OreCost} руди.</color>", $" <color=#DB5242>{def.DisplayName} needs {def.WoodCost} wood and {def.OreCost} ore.</color>");
            return text;
        }
    }
}
