using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Resources;
using BattleOfAges.World;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The zones on the map, always at hand (a campaign's): one click takes up a zone's brush, another puts it down; the
    /// eraser and the brush's size beside them; while a brush is up, a line says what the zone gives, what is marked and
    /// what comes next.
    /// </summary>
    public sealed class ZoneToolbar : MonoBehaviour
    {
        private const float Y = -72f, Step = 118f, ButtonW = 112f, ButtonH = 46f, SizeStep = 50f;
        private static readonly string[] SizeNames = { "М", "С", "В" };
        private static readonly string[] SizeTips = { "малий", "середній", "великий" };

        private Button[] zoneButtons;
        private Button eraser;
        private Button[] sizes;
        private RectTransform statusPanel;
        private Text status;
        private GameObject row;
        private float nextStatus;

        public static ZoneToolbar Create(Transform parent)
        {
            var go = new GameObject("ZoneToolbar", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            ZoneToolbar bar = go.AddComponent<ZoneToolbar>();
            bar.Build(rt);
            return bar;
        }

        private void Build(RectTransform root)
        {
            var rowRect = UiTheme.Child(root, "Row", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            row = rowRect.gameObject;
            ZoneType[] types = ZoneMap.Types;
            float width = (types.Length + 1) * Step + SizeNames.Length * SizeStep + 14f;
            float x = -width * 0.5f + ButtonW * 0.5f;
            zoneButtons = new Button[types.Length];
            for (int i = 0; i < types.Length; i++)
            {
                ZoneType type = types[i];
                zoneButtons[i] = UiTheme.TextButton(rowRect, "Zone_" + type, ZoneMap.Name(type), new Vector2(0.5f, 1f), new Vector2(x, Y), new Vector2(ButtonW, ButtonH), () => Toggle(type), 16);
                RectTransform stripe = UiTheme.Child(zoneButtons[i].transform, "Stripe", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(7f, 5f), new Vector2(-7f, 11f));
                Image img = stripe.gameObject.AddComponent<Image>();
                Color c = ZoneMap.Colour(type);
                c.a = 1f;
                img.color = c;
                img.raycastTarget = false;
                Tooltip.Set(zoneButtons[i].gameObject, () => TipText.Title(ZoneMap.Name(type), UiTheme.Gold) + "\n" + ZoneMap.Description(type));
                x += Step;
            }
            eraser = UiTheme.TextButton(rowRect, "Eraser", "Стерти", new Vector2(0.5f, 1f), new Vector2(x, Y), new Vector2(ButtonW, ButtonH), () => { if (ZonePainter.Erasing) ZonePainter.Stop(); else ZonePainter.BeginEraser(); }, 16);
            Tooltip.Set(eraser.gameObject, () => TipText.Title("Стерти розмітку", UiTheme.Gold) + "\nСпоруди, що вже стоять, лишаються.");
            x += ButtonW * 0.5f + 14f + 22f;
            sizes = new Button[SizeNames.Length];
            for (int i = 0; i < SizeNames.Length; i++)
            {
                int index = i;
                sizes[i] = UiTheme.TextButton(rowRect, "Size" + i, SizeNames[i], new Vector2(0.5f, 1f), new Vector2(x, Y), new Vector2(44f, ButtonH), () => ZonePainter.BrushIndex = index, 17);
                Tooltip.Set(sizes[i].gameObject, () => TipText.Title($"Пензель {SizeTips[index]}", UiTheme.Gold) + $"\nРадіус {ZonePainter.Brushes[index]:0} м.");
                x += SizeStep;
            }

            statusPanel = UiTheme.Rect(rowRect, "Status", new Vector2(0.5f, 1f), new Vector2(0f, Y - ButtonH - 6f), new Vector2(width - 6f, 84f));
            UiTheme.PanelImage(statusPanel);
            RectTransform text = UiTheme.Child(statusPanel, "Text", Vector2.zero, Vector2.one, new Vector2(16f, 6f), new Vector2(-148f, -6f));
            status = UiTheme.Label(text, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.Ink);
            status.supportRichText = true;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiTheme.TextButton(statusPanel, "Done", "Готово\n<size=12>Esc</size>", new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(120f, 56f), ZonePainter.Stop, 16)
                .GetComponentInChildren<Text>().supportRichText = true;
            statusPanel.gameObject.SetActive(false);
            Apply();
        }

        private static void Toggle(ZoneType type)
        {
            if (ZonePainter.Tool == type) ZonePainter.Stop();
            else ZonePainter.Begin(type);
        }

        private void Update()
        {
            bool campaign = WorldSettings.Bounded;
            if (row.activeSelf != campaign) row.SetActive(campaign);
            if (!campaign) return;
            Apply();
            bool painting = ZonePainter.Painting;
            if (statusPanel.gameObject.activeSelf != painting) { statusPanel.gameObject.SetActive(painting); nextStatus = 0f; }
            if (!painting || Time.unscaledTime < nextStatus) return;
            nextStatus = Time.unscaledTime + 0.25f;
            status.text = StatusText();
        }

        /// <summary>The brush in hand and the size lit up; trade waits for a hall II.</summary>
        private void Apply()
        {
            ZoneType[] types = ZoneMap.Types;
            for (int i = 0; i < types.Length; i++)
            {
                Light(zoneButtons[i], ZonePainter.Tool == types[i]);
                if (types[i] == ZoneType.Trade)
                {
                    TownHall hall = TownHall.Viewed;
                    bool open = hall != null && hall.Level >= 2;
                    if (zoneButtons[i].interactable != open) zoneButtons[i].interactable = open;
                    if (!open && ZonePainter.Tool == ZoneType.Trade) ZonePainter.Stop();
                }
            }
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
            Text label = b.GetComponentInChildren<Text>();
            if (label != null) label.color = on ? UiTheme.Gold : UiTheme.Ink;
        }

        /// <summary>What the brush in hand does, what is marked and what comes next.</summary>
        public static string StatusText()
        {
            if (ZonePainter.Erasing) return "<b>Стерти</b>: тягніть ЛКМ по розмітці. Споруди, що вже стоять, лишаються.";
            ZoneType zone = ZonePainter.Tool;
            if (zone == ZoneType.None) return "";
            TownHall hall = TownHall.Viewed;
            ZoneGrower.Progress(zone, hall, out int cells, out int wanted, out int have, out int sites);
            string head = $"<b>{ZoneMap.Description(zone)}</b>\n";
            if (cells == 0) return head + "Тягніть ЛКМ по землі в межах золотого кола, ПКМ стирає. Робітники зведуть споруди самі.";

            BuildingDefinition def = BuildingData.GetDefinition(ZoneGrower.BuildingOf(zone));
            int next = ZoneGrower.CellsPerBuilding(zone) * wanted + 1 - cells;
            string text = head + $"Розмічено {cells} кл.: споруд {wanted}, стоїть {have - sites}" + (sites > 0 ? $", будується {sites}" : "") + $". Ще одна — за {next} кл.";
            if (have >= wanted) return text;
            ResourceManager rm = ResourceManager.Instance;
            SettlementStock stock = rm == null ? null : hall != null ? hall.Stock : rm.Main;
            if (stock != null && (stock.WoodCount < def.WoodCost || stock.GetOreCountAtLeast(def.OreTierRequired) < def.OreCost))
                return text + $" <color=#DB5242>Для «{def.DisplayName}» треба {def.WoodCost} дерева й {def.OreCost} руди.</color>";
            return text;
        }
    }
}
