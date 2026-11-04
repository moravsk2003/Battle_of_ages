using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.Player;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Workbench window: workers at the bench (speed), a level tab (I..V) over the nine recipes with their stats, cost
    /// and time, the crafting queue with progress and cancel (refund), and the way to the armory where finished items
    /// go. Built with UiTheme.
    /// </summary>
    public class WorkbenchUI : MonoBehaviour
    {
        public static WorkbenchUI Instance { get; private set; }

        public static WorkbenchUI EnsureInstance()
        {
            if (Instance != null) return Instance;
            Instance = FindFirstObjectByType<WorkbenchUI>();
            if (Instance != null) return Instance;
            Instance = new GameObject("WorkbenchUI").AddComponent<WorkbenchUI>();
            return Instance;
        }

        public bool IsOpen { get; private set; }

        private GameObject window;
        private WorkbenchNode bench;
        private int selectedLevel = 1;
        private float refreshTimer;

        private Text workersText, warningText, currentText, percentText, storeText;
        private RectTransform progressFill;
        private readonly List<Button> levelTabs = new List<Button>();
        private readonly List<(ItemType type, Image icon, Text name, Text desc, Text cost, Button craft)> cards = new List<(ItemType, Image, Text, Text, Text, Button)>();
        private RectTransform queueList;
        private int smeltTier;
        private Text smeltTierText, smeltInfoText;
        private Button smeltTen, smeltAll;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            window.SetActive(false);
        }

        private void OnEnable() => Armory.Changed += RefreshStore;
        private void OnDisable() => Armory.Changed -= RefreshStore;

        private void Update()
        {
            if (!IsOpen) return;
            if (UniversalInput.GetKeyDownEscape() || KeybindsManager.GetKeyDown(KeyAction.Inventory)) { CloseUI(); return; }
            if (Cursor.lockState != CursorLockMode.None) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            UpdateProgress();
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer <= 0f) { refreshTimer = 0.25f; RefreshAll(); }
        }

        public void OpenUI(WorkbenchNode workbench)
        {
            bench = workbench != null ? workbench : WorkbenchNode.Instance;
            IsOpen = true;
            window.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            RefreshAll();
        }

        public void CloseUI()
        {
            IsOpen = false;
            if (window != null) window.SetActive(false);
            GameModeManager gmm = Managers.GameContext.Modes;
            if (!EquipmentUI.IsVisible && (gmm == null || gmm.CurrentMode == GameMode.FirstPerson))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void SetSelectedLevel(int level)
        {
            selectedLevel = Mathf.Clamp(level, 1, 5);
            RefreshAll();
        }

        // ------------------------------------------------------------------ refresh

        private void RefreshAll()
        {
            if (bench == null) bench = WorkbenchNode.Instance;
            for (int i = 0; i < levelTabs.Count; i++)
                levelTabs[i].GetComponent<Image>().color = i + 1 == selectedLevel ? Color.Lerp(Color.white, UiTheme.LevelColor(i + 1), 0.65f) : new Color(0.75f, 0.72f, 0.68f);

            foreach (var c in cards)
            {
                Item item = new Item(c.type, selectedLevel);
                c.icon.sprite = UiTheme.ItemSprite(c.type, selectedLevel);
                c.name.text = $"{ItemTypeNames.MaterialName(c.type, selectedLevel)} {ItemTypeNames.Display(c.type).ToLowerInvariant()} {ItemTypeNames.Roman(selectedLevel)}";
                c.name.color = UiTheme.LevelColor(selectedLevel);
                c.desc.text = ItemTypeNames.Describe(item);
                int wood = ItemStatCalculator.CalculateWoodCost(c.type, selectedLevel), ore = ItemStatCalculator.CalculateOreCost(c.type, selectedLevel);
                int tier = ItemStatCalculator.CalculateRequiredOreTier(selectedLevel);
                ResourceManager rm = ResourceManager.Instance;
                bool woodOk = rm == null || rm.WoodCount >= wood, oreOk = rm == null || rm.GetOreCountAtLeast(tier) >= ore;
                string tierText = tier == 0 ? "руда" : $"руда {ItemTypeNames.Roman(tier)}+";
                c.cost.text = $"<color={(woodOk ? "#E8DFCB" : "#DB5242")}>дерево {wood}</color>  ·  <color={(oreOk ? "#E8DFCB" : "#DB5242")}>{tierText} ×{ore}</color>  ·  {ItemStatCalculator.CalculateCraftTime(c.type, selectedLevel):0.#} с";
                c.craft.interactable = woodOk && oreOk && bench != null;
            }

            int workers = bench != null ? bench.AssignedWorkerCount : 0, called = bench != null ? bench.CalledWorkerCount : 0;
            workersText.text = $"Робітники: <color=#F5CC73>{workers}/{CraftingQueue.MAX_WORKERS}</color>" + (called > workers ? $" (ще йдуть: {called - workers})" : "")
                               + $"   ·   швидкість ×{CraftingQueue.CalculateSpeedMultiplier(workers):0.##}";
            warningText.gameObject.SetActive(called == 0);
            RefreshSmelt();
            RefreshQueue();
            RefreshStore();
        }

        private void UpdateProgress()
        {
            CraftingTask active = bench != null ? bench.Queue.ActiveTask : null;
            if (active == null)
            {
                currentText.text = "Верстак вільний";
                progressFill.anchorMax = new Vector2(0f, 1f);
                percentText.text = "";
                return;
            }
            currentText.text = active.IsSmelt ? active.ToString() : $"Виготовляється: {new Item(active.Type, active.Level)}";
            progressFill.anchorMax = new Vector2(active.NormalizedProgress, 1f);
            percentText.text = Mathf.RoundToInt(active.NormalizedProgress * 100f) + "%";
        }

        private void RefreshQueue()
        {
            for (int i = queueList.childCount - 1; i >= 0; i--) Destroy(queueList.GetChild(i).gameObject);
            if (bench == null) return;
            var q = bench.Queue.Queue;
            for (int i = 1; i < q.Count && i <= 7; i++)
            {
                CraftingTask task = q[i];
                RectTransform row = UiTheme.Rect(queueList, "Task", new Vector2(0.5f, 1f), new Vector2(0f, -(i - 1) * 44f), new Vector2(380f, 40f));
                UiTheme.PanelImage(row, UiTheme.SlotSprite);
                RectTransform ic = UiTheme.Rect(row, "Icon", new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(32f, 32f));
                Image img = ic.gameObject.AddComponent<Image>();
                img.sprite = task.IsSmelt ? UiTheme.IconSprite(UiTheme.Icon.Ore) : UiTheme.ItemSprite(task.Type, task.Level); img.preserveAspect = true; img.raycastTarget = false;
                RectTransform lbl = UiTheme.Rect(row, "Label", new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(290f, 36f));
                Text label = UiTheme.Label(lbl, "Text", task.ToString(), task.IsSmelt ? 14 : 17, TextAnchor.MiddleLeft, task.IsSmelt ? UiTheme.Ink : UiTheme.LevelColor(task.Level));
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                string id = task.TaskId;
                UiTheme.TextButton(row, "Cancel", "×", new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(34f, 32f), () => { bench.CancelTask(id); RefreshAll(); }, 20)
                       .GetComponent<Image>().color = new Color(1f, 0.72f, 0.68f);
            }
            if (q.Count > 8)
            {
                RectTransform more = UiTheme.Rect(queueList, "More", new Vector2(0.5f, 1f), new Vector2(0f, -7 * 44f), new Vector2(380f, 30f));
                UiTheme.Label(more, "Text", $"ще {q.Count - 8} у черзі", 16, TextAnchor.MiddleCenter, UiTheme.InkDim);
            }
            else if (q.Count <= 1)
            {
                RectTransform none = UiTheme.Rect(queueList, "Empty", new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(380f, 30f));
                UiTheme.Label(none, "Text", "Черга порожня", 16, TextAnchor.MiddleCenter, UiTheme.InkDim);
            }
        }

        /// <summary>The smelting panel: the chosen tier, what ten of it make, how much the stock melts into.</summary>
        private void RefreshSmelt()
        {
            if (smeltTierText == null) return;
            int ratio = Combat.GameBalance.SmeltRatio, can = WorkbenchNode.SmeltableCount(smeltTier);
            ResourceManager rm = ResourceManager.Instance;
            int have = rm != null ? rm.GetOreTierCount(smeltTier) : 0;
            smeltTierText.text = $"руда {CraftingTask.Tier(smeltTier)}  →  руда {CraftingTask.Tier(smeltTier + 1)}";
            smeltInfoText.text = $"{ratio} шт. → 1 шт., {Combat.GameBalance.SmeltSecondsPerPiece:0.#} с за шматок\n"
                                 + $"на складі цієї руди: <color=#F5CC73>{have}</color>" + (can > 0 ? $" — вийде {can}" : "");
            smeltTen.interactable = bench != null && can >= 1;
            smeltAll.interactable = bench != null && can >= 2;
            smeltAll.GetComponentInChildren<Text>().text = can >= 2 ? $"Усе ({can})" : "Усе";
        }

        private void Smelt(bool all)
        {
            if (bench == null) bench = WorkbenchNode.Instance;
            if (bench == null) return;
            int count = all ? WorkbenchNode.SmeltableCount(smeltTier) : 1;
            Net.CommandBus.Submit(new Net.SmeltCommand { Bench = Net.WorldEntity.IdOf(bench).Value, Tier = smeltTier, Count = count });
            RefreshAll();
        }

        private void ShiftSmeltTier(int by)
        {
            smeltTier = Mathf.Clamp(smeltTier + by, 0, 8);
            RefreshAll();
        }

        private static string SmeltTip() =>
            TipText.Title("Переплавка руди", UiTheme.Gold)
            + $"\n{Combat.GameBalance.SmeltRatio} руди одного рівня → 1 руда наступного. З шахти кожен рівень вдесятеро рідший, тож переплавка — певний шлях до рідкої руди."
            + "\nРуду забирають зі скрині одразу, в черзі можна скасувати (руда повернеться). Йде як інші замовлення: потрібні робітники біля верстака.";

        private void RefreshStore()
        {
            if (storeText == null) return;
            int n = Armory.Instance.Items.Count;
            storeText.text = n == 0 ? "На складі поки порожньо" : $"На складі: <color=#F5CC73>{n}</color> {(n % 10 == 1 && n % 100 != 11 ? "річ" : n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 12 || n % 100 > 14) ? "речі" : "речей")}";
        }

        // ------------------------------------------------------------------ actions

        private void Craft(ItemType type)
        {
            if (bench == null) bench = WorkbenchNode.Instance;
            if (bench == null) return;
            Net.CommandBus.Submit(new Net.CraftCommand { Bench = Net.WorldEntity.IdOf(bench).Value, Item = type, Level = selectedLevel });
            RefreshAll();
        }

        private void CallWorker()
        {
            if (bench == null) return;
            Net.CommandBus.Submit(new Net.BenchWorkerCommand { Bench = Net.WorldEntity.IdOf(bench).Value, Call = true });
            RefreshAll();
        }

        private void ReleaseWorker()
        {
            if (bench == null) return;
            Net.CommandBus.Submit(new Net.BenchWorkerCommand { Bench = Net.WorldEntity.IdOf(bench).Value, Call = false });
            RefreshAll();
        }

        private void OpenArmory()
        {
            CloseUI();
            EquipmentUI.Instance.OpenDefault();
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("WorkbenchCanvas", 110);
            canvas.transform.SetParent(transform, false);
            window = canvas.gameObject;
            RectTransform dim = UiTheme.Rect(canvas.transform, "Dim", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.sizeDelta = Vector2.zero;
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1260f, 900f));
            PopIn.On(panel);
            UiTheme.PanelImage(panel);
            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 44f));
            UiTheme.Divider(panel, -60f, 520f);
            UiTheme.Label(head, "Text", "Верстак", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), CloseUI, 28)
                   .GetComponent<Image>().color = new Color(1f, 0.72f, 0.68f);

            // Workers bar
            RectTransform wbar = UiTheme.Rect(panel, "Workers", new Vector2(0f, 1f), new Vector2(24f, -70f), new Vector2(800f, 44f));
            UiTheme.IconImage(wbar, UiTheme.Icon.Worker, new Vector2(0f, 0.5f), new Vector2(0f, 0f), 38f);
            RectTransform wt = UiTheme.Rect(wbar, "Text", new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(420f, 40f));
            workersText = UiTheme.Label(wt, "Text", "", 19, TextAnchor.MiddleLeft, UiTheme.Ink);
            workersText.supportRichText = true;
            UiTheme.TextButton(wbar, "Call", "+ Покликати", new Vector2(0f, 0.5f), new Vector2(470f, 0f), new Vector2(150f, 40f), CallWorker, 17);
            UiTheme.TextButton(wbar, "Release", "− Відпустити", new Vector2(0f, 0.5f), new Vector2(628f, 0f), new Vector2(150f, 40f), ReleaseWorker, 17);
            RectTransform warn = UiTheme.Rect(panel, "Warning", new Vector2(0f, 1f), new Vector2(24f, -112f), new Vector2(800f, 26f));
            warningText = UiTheme.Label(warn, "Text", "Без робітника верстак стоїть — покличте когось", 16, TextAnchor.MiddleLeft, UiTheme.Bad);

            // Level tabs + recipe cards
            for (int l = 1; l <= 5; l++)
            {
                int lv = l;
                levelTabs.Add(UiTheme.TextButton(panel, "Level" + l, "Рівень " + ItemTypeNames.Roman(l), new Vector2(0f, 1f), new Vector2(24f + (l - 1) * 160f, -146f), new Vector2(152f, 42f), () => SetSelectedLevel(lv), 18));
            }
            RectTransform recipes = UiTheme.Rect(panel, "Recipes", new Vector2(0f, 1f), new Vector2(24f, -198f), new Vector2(800f, 680f));
            for (int i = 0; i < ItemTypeNames.All.Length; i++) Card(recipes, ItemTypeNames.All[i], i);

            // Queue column
            RectTransform side = UiTheme.Rect(panel, "Queue", new Vector2(1f, 1f), new Vector2(-24f, -70f), new Vector2(408f, 808f));
            UiTheme.PanelImage(side, UiTheme.SlotSprite, new Color(1f, 1f, 1f, 0.9f));
            RectTransform cur = UiTheme.Rect(side, "Current", new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(380f, 30f));
            currentText = UiTheme.Label(cur, "Text", "", 18, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            RectTransform barBg = UiTheme.Rect(side, "Bar", new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(380f, 22f));
            UiTheme.PanelImage(barBg, UiTheme.BarSprite, new Color(0.1f, 0.08f, 0.06f));
            progressFill = UiTheme.Rect(barBg, "Fill", new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            progressFill.anchorMin = Vector2.zero; progressFill.anchorMax = new Vector2(0f, 1f); progressFill.pivot = new Vector2(0f, 0.5f); progressFill.sizeDelta = Vector2.zero;
            UiTheme.PanelImage(progressFill, UiTheme.BarSprite, UiTheme.Gold);
            RectTransform pct = UiTheme.Rect(barBg, "Pct", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 22f));
            percentText = UiTheme.Label(pct, "Text", "", 14, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            RectTransform qt = UiTheme.Rect(side, "QueueTitle", new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(380f, 28f));
            UiTheme.Label(qt, "Text", "Далі в черзі", 17, TextAnchor.MiddleLeft, UiTheme.InkDim);
            queueList = UiTheme.Rect(side, "List", new Vector2(0.5f, 1f), new Vector2(0f, -114f), new Vector2(380f, 340f));

            // Smelting: ten of a tier into one of the next (GameBalance.SmeltRatio)
            RectTransform smelt = UiTheme.Rect(side, "Smelt", new Vector2(0.5f, 1f), new Vector2(0f, -462f), new Vector2(380f, 206f));
            UiTheme.PanelImage(smelt, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.9f));
            Tooltip.Set(smelt.gameObject, SmeltTip);
            RectTransform smt = UiTheme.Rect(smelt, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(350f, 28f));
            UiTheme.Label(smt, "Text", "Переплавка руди", 18, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(smelt, "Down", "‹", new Vector2(0f, 1f), new Vector2(14f, -40f), new Vector2(40f, 36f), () => ShiftSmeltTier(-1), 22);
            UiTheme.TextButton(smelt, "Up", "›", new Vector2(1f, 1f), new Vector2(-14f, -40f), new Vector2(40f, 36f), () => ShiftSmeltTier(1), 22);
            RectTransform stier = UiTheme.Rect(smelt, "Tier", new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(260f, 36f));
            smeltTierText = UiTheme.Label(stier, "Text", "", 18, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            RectTransform sinfo = UiTheme.Rect(smelt, "Info", new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(350f, 56f));
            smeltInfoText = UiTheme.Label(sinfo, "Text", "", 15, TextAnchor.MiddleCenter, UiTheme.InkDim);
            smeltInfoText.supportRichText = true;
            smeltInfoText.horizontalOverflow = HorizontalWrapMode.Wrap;
            smeltTen = UiTheme.TextButton(smelt, "One", $"Переплавити {Combat.GameBalance.SmeltRatio}", new Vector2(0f, 0f), new Vector2(14f, 12f), new Vector2(170f, 42f), () => Smelt(false), 16);
            smeltAll = UiTheme.TextButton(smelt, "All", "Усе", new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(170f, 42f), () => Smelt(true), 16);

            RectTransform store = UiTheme.Rect(side, "Store", new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(380f, 112f));
            UiTheme.PanelImage(store, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.9f));
            RectTransform stt = UiTheme.Rect(store, "Text", new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(350f, 36f));
            storeText = UiTheme.Label(stt, "Text", "", 18, TextAnchor.MiddleCenter, UiTheme.Ink);
            storeText.supportRichText = true;
            UiTheme.TextButton(store, "Armory", "Склад і спорядження  [C]", new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(330f, 48f), OpenArmory, 18);
        }

        private void Card(RectTransform parent, ItemType type, int index)
        {
            const float W = 258f, H = 160f;
            int col = index % 3, row = index / 3;
            RectTransform card = UiTheme.Rect(parent, "Recipe_" + type, new Vector2(0f, 1f), new Vector2(col * (W + 13f), -row * (H + 10f)), new Vector2(W, H));
            UiTheme.PanelImage(card, UiTheme.SlotSprite);
            RectTransform ic = UiTheme.Rect(card, "Icon", new Vector2(0f, 1f), new Vector2(10f, -10f), new Vector2(60f, 60f));
            Image icon = ic.gameObject.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            RectTransform nm = UiTheme.Rect(card, "Name", new Vector2(0f, 1f), new Vector2(78f, -8f), new Vector2(172f, 40f));
            Text name = UiTheme.Label(nm, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            name.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform ds = UiTheme.Rect(card, "Desc", new Vector2(0f, 1f), new Vector2(78f, -46f), new Vector2(172f, 40f));
            Text desc = UiTheme.Label(ds, "Text", "", 13, TextAnchor.UpperLeft, UiTheme.InkDim);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            RectTransform cs = UiTheme.Rect(card, "Cost", new Vector2(0f, 0f), new Vector2(10f, 50f), new Vector2(240f, 24f));
            Text cost = UiTheme.Label(cs, "Text", "", 14, TextAnchor.MiddleLeft, UiTheme.Ink);
            cost.supportRichText = true;
            Button craft = UiTheme.TextButton(card, "Craft", "Виготовити", new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(W - 20f, 36f), () => Craft(type), 16);
            cards.Add((type, icon, name, desc, cost, craft));
            Tooltip.Set(card.gameObject, () => RecipeTip(type));
        }

        /// <summary>The recipe at the chosen level: what it gives, what it costs and takes, and how it compares with the hero's piece.</summary>
        private string RecipeTip(ItemType type)
        {
            Item item = new Item(type, selectedLevel);
            int tier = ItemStatCalculator.CalculateRequiredOreTier(selectedLevel);
            string s = TipText.Title($"{ItemTypeNames.MaterialName(type, selectedLevel)} {ItemTypeNames.Display(type).ToLowerInvariant()} {ItemTypeNames.Roman(selectedLevel)}", UiTheme.LevelColor(selectedLevel))
                     + "\n" + ItemTypeNames.Describe(item)
                     + $"\nДеревина {ItemStatCalculator.CalculateWoodCost(type, selectedLevel)}, руда {(tier == 0 ? "будь-якого рівня" : ItemTypeNames.Roman(tier) + " і вище")} ×{ItemStatCalculator.CalculateOreCost(type, selectedLevel)}"
                     + $"\nЧас {ItemStatCalculator.CalculateCraftTime(type, selectedLevel):0.#} с з одним робітником; більше робітників — швидше";
            int stored = 0;
            foreach (Item it in Armory.Instance.Items) if (it.Equals(item)) stored++;
            if (stored > 0) s += "\n" + TipText.Dim($"На складі вже є: {stored}");
            PlayerCombatController player = Managers.GameContext.PlayerCombat;
            if (player != null && player.Gear != null)
            {
                Equipment gear = player.Gear;
                Item worn = gear[item.Slot];
                if (gear.IsLocked(item.Slot)) s += "\n" + TipText.Dim("У героя цей слот тримає артефакт");
                else s += "\n" + TipText.Dim(worn.IsEmpty ? "У героя слот вільний" : "У героя зараз: " + worn)
                          + TipText.Compare(gear.StatsWith(worn), gear.StatsWith(item), "Герою, якщо надягти:");
            }
            return s;
        }
    }
}
