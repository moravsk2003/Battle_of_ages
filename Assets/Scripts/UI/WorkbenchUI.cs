using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.RTS;

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
            GameModeManager gmm = FindFirstObjectByType<GameModeManager>();
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
                string tierText = tier == 0 ? "руда" : $"руда {tier}+";
                c.cost.text = $"<color={(woodOk ? "#E8DFCB" : "#DB5242")}>дерево {wood}</color>  ·  <color={(oreOk ? "#E8DFCB" : "#DB5242")}>{tierText} ×{ore}</color>  ·  {ItemStatCalculator.CalculateCraftTime(c.type, selectedLevel):0.#} с";
                c.craft.interactable = woodOk && oreOk && bench != null;
            }

            int workers = bench != null ? bench.AssignedWorkerCount : 0;
            workersText.text = $"Робітники: <color=#F5CC73>{workers}/{CraftingQueue.MAX_WORKERS}</color>   ·   швидкість ×{CraftingQueue.CalculateSpeedMultiplier(workers):0.##}";
            warningText.gameObject.SetActive(workers == 0);
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
            currentText.text = $"Виготовляється: {new Item(active.Type, active.Level)}";
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
                img.sprite = UiTheme.ItemSprite(task.Type, task.Level); img.preserveAspect = true; img.raycastTarget = false;
                RectTransform lbl = UiTheme.Rect(row, "Label", new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(270f, 36f));
                UiTheme.Label(lbl, "Text", new Item(task.Type, task.Level).ToString(), 17, TextAnchor.MiddleLeft, UiTheme.LevelColor(task.Level));
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
            if (!bench.TryEnqueueItem(type, selectedLevel)) StrategyHud.Notify("Не вистачає ресурсів");
            RefreshAll();
        }

        private void CallWorker()
        {
            if (bench == null) return;
            RtsUnit u = bench.AssignNearestFreeWorker();
            if (u == null) StrategyHud.Notify(bench.IsWorkerFull ? "Усі місця біля верстака зайняті" : "Немає вільних робітників");
            RefreshAll();
        }

        private void ReleaseWorker()
        {
            if (bench == null) return;
            bench.UnassignLastWorker();
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

            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1260f, 720f));
            UiTheme.PanelImage(panel);
            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 44f));
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
            RectTransform recipes = UiTheme.Rect(panel, "Recipes", new Vector2(0f, 1f), new Vector2(24f, -198f), new Vector2(800f, 500f));
            for (int i = 0; i < ItemTypeNames.All.Length; i++) Card(recipes, ItemTypeNames.All[i], i);

            // Queue column
            RectTransform side = UiTheme.Rect(panel, "Queue", new Vector2(1f, 1f), new Vector2(-24f, -70f), new Vector2(408f, 628f));
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
            queueList = UiTheme.Rect(side, "List", new Vector2(0.5f, 1f), new Vector2(0f, -114f), new Vector2(380f, 350f));

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
        }
    }
}
