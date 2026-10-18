using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BattleOfAges.Crafting;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.Combat;
using BattleOfAges.Player;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Equipment window (key C): the six worn slots of the player or of the selected own units (arrows switch between
    /// them), their resulting stats, and the settlement's armory. Click an armory item to put it on (what was worn
    /// goes back to the armory), click a worn slot to take it off. Hovering shows the item's stats and the change
    /// against what is worn.
    /// </summary>
    public sealed class EquipmentUI : MonoBehaviour
    {
        private static EquipmentUI instance;
        public static EquipmentUI Instance
        {
            get
            {
                if (instance == null) instance = new GameObject("EquipmentUI").AddComponent<EquipmentUI>();
                return instance;
            }
        }
        public static bool IsVisible => instance != null && instance.open;

        private readonly List<Equipment> targets = new List<Equipment>();
        private int current;
        private bool open;
        private int filter = -1; // -1 all, else EquipSlot

        private GameObject window;
        private Text title, counter, stats, hint;
        private Button prevButton, nextButton;
        private readonly Dictionary<EquipSlot, (Image rim, Image icon, Text level, Text caption)> slotViews = new Dictionary<EquipSlot, (Image, Image, Text, Text)>();
        private RectTransform grid;
        private Text emptyStore;
        private readonly List<Button> filterButtons = new List<Button>();
        private RectTransform tooltip;
        private Text tooltipText;
        private bool dirty;

        private static readonly Vector2 Size = new Vector2(1180f, 680f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("EquipmentUI").AddComponent<EquipmentUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            window.SetActive(false);
            DontDestroyOnLoad(gameObject); // the game may start in the main menu scene
        }

        private void OnEnable()
        {
            Equipment.AnyChanged += OnEquipmentChanged;
            Armory.Changed += MarkDirty;
        }

        private void OnDisable()
        {
            Equipment.AnyChanged -= OnEquipmentChanged;
            Armory.Changed -= MarkDirty;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void OnEquipmentChanged(Equipment e) { if (open && targets.Contains(e)) dirty = true; }
        private void MarkDirty() { if (open) dirty = true; }

        // ------------------------------------------------------------------ open / close

        /// <summary>Opens for these units (first shown at index).</summary>
        public void Open(IList<Equipment> units, int index = 0)
        {
            targets.Clear();
            foreach (Equipment e in units) if (e != null) targets.Add(e);
            if (targets.Count == 0) return;
            current = Mathf.Clamp(index, 0, targets.Count - 1);
            open = true;
            window.SetActive(true);
            window.transform.SetAsLastSibling();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        /// <summary>Opens for the selected own units on the map, else for the player.</summary>
        public void OpenDefault()
        {
            var list = new List<Equipment>();
            GameModeManager modes = FindFirstObjectByType<GameModeManager>();
            RtsUnitCommander commander = FindFirstObjectByType<RtsUnitCommander>();
            if (modes != null && modes.CurrentMode == GameMode.RtsMap && commander != null)
                foreach (RtsUnit u in commander.SelectedUnits)
                    if (u != null) list.Add(GearOf(u.gameObject));
            if (list.Count == 0)
            {
                var player = FindFirstObjectByType<PlayerCombatController>();
                if (player != null) list.Add(player.Gear);
            }
            Open(list);
        }

        private static Equipment GearOf(GameObject go)
        {
            var player = go.GetComponent<PlayerCombatController>();
            if (player != null) return player.Gear;
            Equipment e = go.GetComponent<Equipment>();
            return e != null ? e : go.AddComponent<Equipment>();
        }

        public void Close()
        {
            open = false;
            window.SetActive(false);
            tooltip.gameObject.SetActive(false);
            GameModeManager modes = FindFirstObjectByType<GameModeManager>();
            bool otherModal = GameWindows.InventoryOpen || GameWindows.WorkbenchOpen
                              || GameWindows.Paused;
            if (!otherModal && (modes == null || modes.CurrentMode == GameMode.FirstPerson))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (Time.frameCount < 2) return;
            if (open)
            {
                targets.RemoveAll(t => t == null);
                if (targets.Count == 0) { Close(); return; }
                if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                if (UniversalInput.GetKeyDownEscape() || KeybindsManager.GetKeyDown(KeyAction.Equipment)) { Close(); return; }
                if (dirty) Refresh();
                if (tooltip.gameObject.activeSelf) PlaceTooltip();
                return;
            }
            if (GameWindows.Paused) return;
            if (GameWindows.InventoryOpen || GameWindows.BuildMenuOpen
                || GameWindows.WorkersOpen) return;
            if (KeybindsManager.GetKeyDown(KeyAction.Equipment)) OpenDefault();
        }

        // ------------------------------------------------------------------ actions

        private Equipment Target => targets.Count > 0 ? targets[Mathf.Clamp(current, 0, targets.Count - 1)] : null;

        private void Step(int d)
        {
            if (targets.Count == 0) return;
            current = (current + d + targets.Count) % targets.Count;
            Refresh();
        }

        private void OnSlotClicked(EquipSlot slot)
        {
            if (Armory.Instance.UnequipToStore(Target, slot)) Refresh();
        }

        private void OnStoreItemClicked(Item item)
        {
            if (Armory.Instance.EquipFromStore(Target, item))
            {
                tooltip.gameObject.SetActive(false);
                Refresh();
            }
        }

        private void SetFilter(int f)
        {
            filter = f;
            Refresh();
        }

        // ------------------------------------------------------------------ refresh

        private void Refresh()
        {
            dirty = false;
            Equipment t = Target;
            if (t == null) return;
            title.text = "Спорядження — " + DisplayName(t.gameObject);
            counter.text = targets.Count > 1 ? $"{current + 1} / {targets.Count}" : "";
            prevButton.gameObject.SetActive(targets.Count > 1);
            nextButton.gameObject.SetActive(targets.Count > 1);

            foreach (EquipSlot s in ItemTypeNames.Slots)
            {
                var v = slotViews[s];
                Item item = t[s];
                bool has = !item.IsEmpty;
                v.icon.enabled = has;
                if (has) v.icon.sprite = UiTheme.ItemSprite(item.Type, item.Level);
                v.level.text = has ? ItemTypeNames.Roman(item.Level) : "";
                v.level.color = has ? UiTheme.LevelColor(item.Level) : UiTheme.Ink;
                v.caption.text = has ? "" : ItemTypeNames.SlotName(s);
                v.rim.color = has ? UiTheme.LevelColor(item.Level) : new Color(1f, 1f, 1f, 0.55f);
            }

            stats.text =
                $"Здоров'я  <color=#F5CC73>{t.MaxHealth}</color>\n" +
                $"Урон  <color=#F5CC73>{t.Damage}</color>   ·   удар раз на {t.Cooldown:0.0} с\n" +
                $"Дальність  <color=#F5CC73>{t.Range:0.#} м</color>\n" +
                $"Поглинання урону  <color=#F5CC73>{Mathf.RoundToInt(t.DamageReduction * 100f)}%</color>\n" +
                $"Сила  <color=#F5CC73>{t.Strength:0.#}</color>   ·   швидкість {SpeedOf(t):0.#} м/с";

            for (int i = 0; i < filterButtons.Count; i++)
            {
                Image img = filterButtons[i].GetComponent<Image>();
                img.color = (i - 1) == filter ? new Color(1f, 0.85f, 0.5f) : Color.white;
            }
            RebuildStore();
        }

        private static float SpeedOf(Equipment t)
        {
            var combat = t.GetComponent<CombatUnit>();
            return CharacterSpeed.Of(combat);
        }

        private static string DisplayName(GameObject go)
        {
            if (go.GetComponent<PlayerCombatController>() != null) return "Герой";
            string n = go.name;
            if (n.StartsWith("NPC_Villager_")) return "Робітник " + n.Substring("NPC_Villager_".Length);
            if (n.Contains("Knight")) return "Лицар";
            return n.Replace("NPC_", "").Replace("_", " ");
        }

        private void RebuildStore()
        {
            for (int i = grid.childCount - 1; i >= 0; i--) Destroy(grid.GetChild(i).gameObject);
            var groups = new List<(Item item, int count)>();
            foreach (Item it in Armory.Instance.Items)
            {
                if (filter >= 0 && (int)it.Slot != filter) continue;
                if (groups.Count > 0 && groups[groups.Count - 1].item.Equals(it)) groups[groups.Count - 1] = (it, groups[groups.Count - 1].count + 1);
                else groups.Add((it, 1));
            }
            emptyStore.gameObject.SetActive(groups.Count == 0);
            foreach (var g in groups) StoreCell(g.item, g.count);
            grid.sizeDelta = new Vector2(grid.sizeDelta.x, Mathf.Ceil(groups.Count / 6f) * 100f + 10f);
        }

        private void StoreCell(Item item, int count)
        {
            var go = new GameObject("Item", typeof(RectTransform));
            go.transform.SetParent(grid, false);
            Image rim = go.AddComponent<Image>();
            rim.sprite = UiTheme.SlotSprite; rim.type = Image.Type.Sliced;
            rim.color = UiTheme.LevelColor(item.Level);
            Button b = go.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.25f, 1.2f, 1.1f); cb.pressedColor = new Color(0.75f, 0.7f, 0.65f); cb.colorMultiplier = 1.3f;
            b.colors = cb;
            b.onClick.AddListener(() => OnStoreItemClicked(item));
            RectTransform icon = UiTheme.Rect(go.transform, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(62f, 62f));
            Image ii = icon.gameObject.AddComponent<Image>();
            ii.sprite = UiTheme.ItemSprite(item.Type, item.Level); ii.preserveAspect = true; ii.raycastTarget = false;
            RectTransform lv = UiTheme.Rect(go.transform, "Level", new Vector2(0f, 0f), new Vector2(6f, 2f), new Vector2(40f, 24f));
            UiTheme.Label(lv, "Text", ItemTypeNames.Roman(item.Level), 17, TextAnchor.LowerLeft, UiTheme.LevelColor(item.Level), FontStyle.Bold);
            if (count > 1)
            {
                RectTransform cnt = UiTheme.Rect(go.transform, "Count", new Vector2(1f, 0f), new Vector2(-6f, 2f), new Vector2(44f, 24f));
                UiTheme.Label(cnt, "Text", "×" + count, 17, TextAnchor.LowerRight, UiTheme.Ink, FontStyle.Bold);
            }
            Hover(go, () => Describe(item, true));
        }

        private string Describe(Item item, bool compare)
        {
            string s = $"<b><color=#{ColorUtility.ToHtmlStringRGB(UiTheme.LevelColor(item.Level))}>{ItemTypeNames.MaterialName(item.Type, item.Level)} {ItemTypeNames.Display(item.Type).ToLowerInvariant()} {ItemTypeNames.Roman(item.Level)}</color></b>\n"
                     + ItemTypeNames.Describe(item);
            Equipment t = Target;
            if (compare && t != null)
            {
                Item worn = t[item.Slot];
                s += worn.IsEmpty ? "\n<color=#9C9080>Слот вільний</color>" : $"\n<color=#9C9080>Замість: {worn}</color>";
                s += "\n<color=#9C9080>Клік — надягти</color>";
            }
            return s;
        }

        // ------------------------------------------------------------------ tooltip

        private void Hover(GameObject go, System.Func<string> text)
        {
            var trigger = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { tooltipText.text = text(); tooltip.gameObject.SetActive(true); tooltip.SetAsLastSibling(); PlaceTooltip(); });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => tooltip.gameObject.SetActive(false));
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        private void PlaceTooltip()
        {
            var canvasRt = (RectTransform)tooltip.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, UniversalInput.GetMousePosition(), null, out Vector2 local);
            Vector2 half = canvasRt.rect.size * 0.5f;
            Vector2 size = tooltip.sizeDelta;
            float x = Mathf.Clamp(local.x + 18f, -half.x, half.x - size.x);
            float y = Mathf.Clamp(local.y - 18f, -half.y + size.y, half.y);
            tooltip.anchoredPosition = new Vector2(x, y);
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("EquipmentCanvas", 120);
            canvas.transform.SetParent(transform, false);
            window = canvas.gameObject;

            RectTransform dim = UiTheme.Rect(canvas.transform, "Dim", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.sizeDelta = Vector2.zero;
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, Size);
            UiTheme.PanelImage(panel);

            // Header
            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(800f, 44f));
            title = UiTheme.Label(head, "Text", "Спорядження", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            prevButton = UiTheme.TextButton(panel, "Prev", "‹", new Vector2(0f, 1f), new Vector2(24f, -14f), new Vector2(46f, 44f), () => Step(-1), 30);
            nextButton = UiTheme.TextButton(panel, "Next", "›", new Vector2(0f, 1f), new Vector2(78f, -14f), new Vector2(46f, 44f), () => Step(1), 30);
            RectTransform cnt = UiTheme.Rect(panel, "Counter", new Vector2(0f, 1f), new Vector2(132f, -14f), new Vector2(90f, 44f));
            counter = UiTheme.Label(cnt, "Text", "", 19, TextAnchor.MiddleLeft, UiTheme.InkDim);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), Close, 28)
                   .GetComponent<Image>().color = new Color(1f, 0.72f, 0.68f);

            // Paper doll
            RectTransform doll = UiTheme.Rect(panel, "Doll", new Vector2(0f, 1f), new Vector2(24f, -74f), new Vector2(430f, 590f));
            UiTheme.PanelImage(doll, UiTheme.SlotSprite, new Color(1f, 1f, 1f, 0.9f));
            DrawSilhouette(doll);
            Slot(doll, EquipSlot.Helmet, new Vector2(0f, 228f));
            Slot(doll, EquipSlot.Weapon, new Vector2(-140f, 102f));
            Slot(doll, EquipSlot.Chest, new Vector2(0f, 102f));
            Slot(doll, EquipSlot.Shield, new Vector2(140f, 102f));
            Slot(doll, EquipSlot.Gloves, new Vector2(-140f, -24f));
            Slot(doll, EquipSlot.Boots, new Vector2(0f, -24f));
            RectTransform st = UiTheme.Rect(doll, "Stats", new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(390f, 150f));
            UiTheme.PanelImage(st, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.85f));
            RectTransform stText = UiTheme.Rect(st, "Text", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 130f));
            stats = UiTheme.Label(stText, "Text", "", 17, TextAnchor.MiddleLeft, UiTheme.Ink);
            stats.supportRichText = true;
            stats.lineSpacing = 1.15f;

            // Armory
            RectTransform store = UiTheme.Rect(panel, "Store", new Vector2(1f, 1f), new Vector2(-24f, -74f), new Vector2(690f, 590f));
            UiTheme.PanelImage(store, UiTheme.SlotSprite, new Color(1f, 1f, 1f, 0.9f));
            RectTransform storeTitle = UiTheme.Rect(store, "Title", new Vector2(0f, 1f), new Vector2(18f, -8f), new Vector2(300f, 34f));
            UiTheme.Label(storeTitle, "Text", "Склад", 24, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);

            string[] names = { "Усе", "Зброя", "Щит", "Шолом", "Нагр.", "Рукав.", "Чоботи" };
            for (int i = 0; i < names.Length; i++)
            {
                int f = i - 1;
                filterButtons.Add(UiTheme.TextButton(store, "Filter" + i, names[i], new Vector2(0f, 1f), new Vector2(16f + i * 95f, -48f), new Vector2(89f, 36f), () => SetFilter(f), 15));
            }

            RectTransform view = UiTheme.Rect(store, "View", new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(654f, 450f));
            view.gameObject.AddComponent<RectMask2D>();
            grid = UiTheme.Rect(view, "Grid", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(654f, 450f));
            GridLayoutGroup gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(98f, 92f);
            gl.spacing = new Vector2(8f, 8f);
            gl.padding = new RectOffset(6, 6, 6, 6);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = 6;
            ScrollRect sr = view.gameObject.AddComponent<ScrollRect>();
            sr.content = grid; sr.horizontal = false; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 30f;
            view.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f); // catches the wheel

            RectTransform empty = UiTheme.Rect(store, "Empty", new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(600f, 80f));
            emptyStore = UiTheme.Label(empty, "Text", "Склад порожній — речі виготовляє верстак,\nтрофеї падають з переможених ворогів", 19, TextAnchor.MiddleCenter, UiTheme.InkDim);

            RectTransform hintRt = UiTheme.Rect(store, "Hint", new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(654f, 30f));
            hint = UiTheme.Label(hintRt, "Text", "Клік по речі — надягти  ·  клік по слоту — зняти на склад  ·  [C] закрити", 15, TextAnchor.MiddleCenter, UiTheme.InkDim);

            // Tooltip
            tooltip = UiTheme.Rect(canvas.transform, "Tooltip", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 118f));
            tooltip.pivot = new Vector2(0f, 1f);
            UiTheme.PanelImage(tooltip);
            tooltip.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            RectTransform tt = UiTheme.Rect(tooltip, "Text", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(330f, 100f));
            tooltipText = UiTheme.Label(tt, "Text", "", 17, TextAnchor.MiddleLeft, UiTheme.Ink);
            tooltipText.supportRichText = true;
            tooltipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltip.gameObject.SetActive(false);
        }

        private void Slot(RectTransform doll, EquipSlot slot, Vector2 offset)
        {
            RectTransform rt = UiTheme.Rect(doll, "Slot_" + slot, new Vector2(0.5f, 0.5f), offset, new Vector2(104f, 104f));
            Image rim = rt.gameObject.AddComponent<Image>();
            rim.sprite = UiTheme.SlotSprite; rim.type = Image.Type.Sliced;
            Button b = rt.gameObject.AddComponent<Button>();
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.25f, 1.2f, 1.1f); cb.pressedColor = new Color(0.75f, 0.7f, 0.65f); cb.colorMultiplier = 1.3f;
            b.colors = cb;
            b.onClick.AddListener(() => OnSlotClicked(slot));
            RectTransform iconRt = UiTheme.Rect(rt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(72f, 72f));
            Image icon = iconRt.gameObject.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            RectTransform lv = UiTheme.Rect(rt, "Level", new Vector2(1f, 0f), new Vector2(-8f, 4f), new Vector2(44f, 26f));
            Text level = UiTheme.Label(lv, "Text", "", 19, TextAnchor.LowerRight, UiTheme.Ink, FontStyle.Bold);
            RectTransform cap = UiTheme.Rect(rt, "Caption", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 30f));
            Text caption = UiTheme.Label(cap, "Text", ItemTypeNames.SlotName(slot), 15, TextAnchor.MiddleCenter, UiTheme.InkDim);
            slotViews[slot] = (rim, icon, level, caption);
            Hover(rt.gameObject, () =>
            {
                Item worn = Target != null ? Target[slot] : Item.None;
                return worn.IsEmpty ? $"<b>{ItemTypeNames.SlotName(slot)}</b>\n<color=#9C9080>Порожньо — оберіть річ на складі</color>" : Describe(worn, false) + "\n<color=#9C9080>Клік — зняти на склад</color>";
            });
        }

        /// <summary>A faint figure behind the slots.</summary>
        private static void DrawSilhouette(RectTransform doll)
        {
            Color c = new Color(0.9f, 0.8f, 0.6f, 0.06f);
            void Shape(Vector2 pos, Vector2 size, bool round)
            {
                RectTransform rt = UiTheme.Rect(doll, "Figure", new Vector2(0.5f, 0.5f), pos, size);
                Image img = rt.gameObject.AddComponent<Image>();
                img.sprite = round ? UiTheme.RoundSprite : UiTheme.BarSprite; img.type = Image.Type.Sliced; img.color = c; img.raycastTarget = false;
            }
            Shape(new Vector2(0f, 228f), new Vector2(80f, 90f), true);
            Shape(new Vector2(0f, 102f), new Vector2(170f, 200f), false);
            Shape(new Vector2(-104f, 80f), new Vector2(46f, 190f), false);
            Shape(new Vector2(104f, 80f), new Vector2(46f, 190f), false);
            Shape(new Vector2(-36f, -50f), new Vector2(56f, 150f), false);
            Shape(new Vector2(36f, -50f), new Vector2(56f, 150f), false);
        }
    }
}
