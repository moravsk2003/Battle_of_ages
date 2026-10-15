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
    /// Professional UI modal for the Workbench crafting station and finished goods storage.
    /// Allows queuing items (levels 1-5), viewing recipes and stats, managing 1-4 workers,
    /// and claiming/equipping crafted items for player or nearest villager.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorkbenchUI : MonoBehaviour
    {
        public static WorkbenchUI Instance { get; private set; }

        public static WorkbenchUI EnsureInstance()
        {
            if (Instance != null) return Instance;
            Instance = FindFirstObjectByType<WorkbenchUI>();
            if (Instance != null) return Instance;

            GameObject uiObj = new GameObject("WorkbenchUI");
            Instance = uiObj.AddComponent<WorkbenchUI>();
            return Instance;
        }

        private GameObject panelObj;
        private WorkbenchNode activeWorkbench;

        private Text titleText;
        private Text workerCountText;
        private Text currentTaskText;
        private RectTransform progressBarFillRect;
        private Text progressPercentText;

        private readonly List<Text> recipeTexts = new List<Text>();
        private readonly List<Button> levelTabButtons = new List<Button>();
        private Transform outputItemsContainer;
        private Text emptyOutputText;

        private int selectedLevel = 1;
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BuildUI();
            CloseUI();
        }

        private void Update()
        {
            if (!IsOpen) return;

            if (UniversalInput.GetKeyDownEscape() || KeybindsManager.GetKeyDown(KeyAction.Inventory))
            {
                CloseUI();
                return;
            }

            UpdateProgressDisplay();
        }

        public void OpenUI(WorkbenchNode workbench)
        {
            if (activeWorkbench != null) activeWorkbench.OnOutputChanged -= RefreshOutputDisplay;

            activeWorkbench = workbench ?? WorkbenchNode.Instance;
            if (activeWorkbench != null) activeWorkbench.OnOutputChanged += RefreshOutputDisplay;

            IsOpen = true;
            if (panelObj != null) panelObj.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            UpdateWorkerCountDisplay();
            UpdateRecipeButtonLabels();
            UpdateProgressDisplay();
            RefreshOutputDisplay();
        }

        public void CloseUI()
        {
            if (activeWorkbench != null) activeWorkbench.OnOutputChanged -= RefreshOutputDisplay;

            IsOpen = false;
            if (panelObj != null) panelObj.SetActive(false);

            GameModeManager gmm = FindFirstObjectByType<GameModeManager>();
            if (gmm == null || gmm.CurrentMode == GameMode.FirstPerson)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void SetSelectedLevel(int level)
        {
            selectedLevel = Mathf.Clamp(level, 1, 5);
            UpdateRecipeButtonLabels();
            UpdateLevelTabStyles();
        }

        private void UpdateLevelTabStyles()
        {
            for (int i = 0; i < levelTabButtons.Count; i++)
            {
                if (levelTabButtons[i] == null) continue;
                int tabLevel = i + 1;
                Image btnImg = levelTabButtons[i].GetComponent<Image>();
                if (btnImg != null)
                {
                    btnImg.color = (tabLevel == selectedLevel)
                        ? new Color(0.85f, 0.65f, 0.15f, 1f)  // Golden active tab
                        : new Color(0.18f, 0.24f, 0.36f, 0.95f);
                }
            }
        }

        private void UpdateWorkerCountDisplay()
        {
            if (activeWorkbench == null || workerCountText == null) return;

            int workers = activeWorkbench.AssignedWorkerCount;
            float speed = CraftingQueue.CalculateSpeedMultiplier(workers);
            workerCountText.text = $"⚒️ Робітники на верстаку: <b>{workers}/4</b>  |  ⚡ Швидкість: <b>{speed:F2}x</b>";
        }

        private void UpdateProgressDisplay()
        {
            if (activeWorkbench == null) return;

            CraftingTask active = activeWorkbench.Queue.ActiveTask;
            if (active != null)
            {
                if (currentTaskText != null)
                {
                    currentTaskText.text = $"🔨 Виготовляється: <b>{BattleOfAges.Crafting.ItemTypeNames.Display(active.Type)} (рівень {active.Level})</b>\n" +
                                           $"Час: {active.ProgressSeconds:F1}s / {active.TargetDurationSeconds:F1}s | У черзі: {activeWorkbench.Queue.Queue.Count}";
                }

                float norm = active.NormalizedProgress;
                if (progressBarFillRect != null)
                {
                    progressBarFillRect.anchorMax = new Vector2(Mathf.Clamp01(norm), 1f);
                }

                if (progressPercentText != null)
                {
                    progressPercentText.text = $"{Mathf.RoundToInt(norm * 100f)}%";
                }
            }
            else
            {
                if (currentTaskText != null)
                {
                    currentTaskText.text = "Верстак вільний. Оберіть рівень та натисніть [Виготовити].";
                }
                if (progressBarFillRect != null) progressBarFillRect.anchorMax = new Vector2(0f, 1f);
                if (progressPercentText != null) progressPercentText.text = "0%";
            }
        }

        private void OnCraftButtonClicked(ItemType type)
        {
            if (activeWorkbench == null) activeWorkbench = WorkbenchNode.Instance;
            if (activeWorkbench == null) return;

            activeWorkbench.TryEnqueueItem(type, selectedLevel);
            UpdateProgressDisplay();
        }

        private void OnAssignWorkerClicked()
        {
            if (activeWorkbench == null) activeWorkbench = WorkbenchNode.Instance;
            if (activeWorkbench == null) return;

            RtsUnit[] units = FindObjectsByType<RtsUnit>(FindObjectsSortMode.None);
            for (int i = 0; i < units.Length; i++)
            {
                RtsUnit u = units[i];
                if (u != null && u.GetComponent<BattleOfAges.Player.FirstPersonController>() == null)
                {
                    if (activeWorkbench.AssignWorker(u))
                    {
                        UpdateWorkerCountDisplay();
                        break;
                    }
                }
            }
        }

        private void OnUnassignWorkerClicked()
        {
            if (activeWorkbench == null) activeWorkbench = WorkbenchNode.Instance;
            if (activeWorkbench == null) return;

            activeWorkbench.Queue.UnassignWorker();
            UpdateWorkerCountDisplay();
        }

        private void RefreshOutputDisplay()
        {
            if (outputItemsContainer == null) return;

            for (int i = outputItemsContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(outputItemsContainer.GetChild(i).gameObject);
            }

            if (activeWorkbench == null || activeWorkbench.CompletedOutput.Count == 0)
            {
                if (emptyOutputText != null) emptyOutputText.gameObject.SetActive(true);
                return;
            }

            if (emptyOutputText != null) emptyOutputText.gameObject.SetActive(false);

            Font font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            for (int i = 0; i < activeWorkbench.CompletedOutput.Count; i++)
            {
                int itemIndex = i;
                CraftingTask item = activeWorkbench.CompletedOutput[i];

                GameObject cardObj = new GameObject($"OutputCard_{i}", typeof(RectTransform));
                cardObj.transform.SetParent(outputItemsContainer, false);
                RectTransform cardRect = cardObj.GetComponent<RectTransform>();
                cardRect.sizeDelta = new Vector2(320f, 54f);

                Image cardBg = cardObj.AddComponent<Image>();
                cardBg.color = new Color(0.12f, 0.16f, 0.24f, 0.95f);

                // Label
                GameObject lblObj = new GameObject("Label", typeof(RectTransform));
                lblObj.transform.SetParent(cardObj.transform, false);
                RectTransform lblRect = lblObj.GetComponent<RectTransform>();
                lblRect.anchorMin = new Vector2(0f, 0f);
                lblRect.anchorMax = new Vector2(0.55f, 1f);
                lblRect.offsetMin = new Vector2(10f, 0);

                Text lbl = lblObj.AddComponent<Text>();
                lbl.font = font;
                lbl.fontSize = 14;
                lbl.fontStyle = FontStyle.Bold;
                lbl.alignment = TextAnchor.MiddleLeft;
                lbl.color = new Color(1.0f, 0.88f, 0.4f);
                lbl.text = $"✨ {BattleOfAges.Crafting.ItemTypeNames.Display(item.Type)}\n(рівень {item.Level})";

                // Claim Button
                GameObject claimBtnObj = new GameObject("ClaimBtn", typeof(RectTransform));
                claimBtnObj.transform.SetParent(cardObj.transform, false);
                RectTransform claimRect = claimBtnObj.GetComponent<RectTransform>();
                claimRect.anchorMin = new Vector2(0.56f, 0.12f);
                claimRect.anchorMax = new Vector2(0.77f, 0.88f);

                Image claimImg = claimBtnObj.AddComponent<Image>();
                claimImg.color = new Color(0.18f, 0.55f, 0.28f);
                Button claimBtn = claimBtnObj.AddComponent<Button>();
                claimBtn.onClick.AddListener(() =>
                {
                    if (activeWorkbench != null) activeWorkbench.ClaimItemToPlayer(itemIndex);
                });

                GameObject claimTxtObj = new GameObject("Text", typeof(RectTransform));
                claimTxtObj.transform.SetParent(claimBtnObj.transform, false);
                RectTransform cTxtRect = claimTxtObj.GetComponent<RectTransform>();
                cTxtRect.anchorMin = Vector2.zero;
                cTxtRect.anchorMax = Vector2.one;
                Text claimTxt = claimTxtObj.AddComponent<Text>();
                claimTxt.font = font;
                claimTxt.fontSize = 12;
                claimTxt.fontStyle = FontStyle.Bold;
                claimTxt.alignment = TextAnchor.MiddleCenter;
                claimTxt.color = Color.white;
                claimTxt.text = "✋ Мені";

                // Equip NPC Button
                GameObject npcBtnObj = new GameObject("EquipNpcBtn", typeof(RectTransform));
                npcBtnObj.transform.SetParent(cardObj.transform, false);
                RectTransform npcRect = npcBtnObj.GetComponent<RectTransform>();
                npcRect.anchorMin = new Vector2(0.79f, 0.12f);
                npcRect.anchorMax = new Vector2(0.98f, 0.88f);

                Image npcImg = npcBtnObj.AddComponent<Image>();
                npcImg.color = new Color(0.22f, 0.42f, 0.75f);
                Button npcBtn = npcBtnObj.AddComponent<Button>();
                npcBtn.onClick.AddListener(() =>
                {
                    if (activeWorkbench != null) activeWorkbench.EquipNearestVillager(itemIndex);
                });

                GameObject npcTxtObj = new GameObject("Text", typeof(RectTransform));
                npcTxtObj.transform.SetParent(npcBtnObj.transform, false);
                RectTransform nTxtRect = npcTxtObj.GetComponent<RectTransform>();
                nTxtRect.anchorMin = Vector2.zero;
                nTxtRect.anchorMax = Vector2.one;
                Text npcTxt = npcTxtObj.AddComponent<Text>();
                npcTxt.font = font;
                npcTxt.fontSize = 12;
                npcTxt.fontStyle = FontStyle.Bold;
                npcTxt.alignment = TextAnchor.MiddleCenter;
                npcTxt.color = Color.white;
                npcTxt.text = "🛡️ Селянину";
            }
        }

        private void UpdateRecipeButtonLabels()
        {
            if (titleText != null)
            {
                titleText.text = $"⚒️ ВЕРСТАК — РЕЦЕПТИ (Обрано Рівень {selectedLevel})";
            }

            ItemType[] items = new ItemType[]
            {
                ItemType.Sword,
                ItemType.Bow,
                ItemType.Armor,
                ItemType.Axe,
                ItemType.Pickaxe
            };

            for (int i = 0; i < items.Length && i < recipeTexts.Count; i++)
            {
                ItemType type = items[i];
                int wood = ItemStatCalculator.CalculateWoodCost(type, selectedLevel);
                int ore = ItemStatCalculator.CalculateOreCost(type, selectedLevel);
                int oreTier = ItemStatCalculator.CalculateRequiredOreTier(selectedLevel);

                string statDesc = "";
                switch (type)
                {
                    case ItemType.Sword:
                        statDesc = $"+{ItemStatCalculator.CalculateSwordDamage(selectedLevel):F1} Урону";
                        break;
                    case ItemType.Bow:
                        statDesc = $"+{20f + selectedLevel * 5f:F1} Дальнього урону";
                        break;
                    case ItemType.Armor:
                        statDesc = $"+{ItemStatCalculator.CalculateArmorDefense(selectedLevel):F1} Захисту";
                        break;
                    case ItemType.Axe:
                        statDesc = $"+{ItemStatCalculator.CalculateToolEfficiency(selectedLevel):F2}x Рубки дерева";
                        break;
                    case ItemType.Pickaxe:
                        statDesc = $"+{ItemStatCalculator.CalculateToolEfficiency(selectedLevel):F2}x Видобутку руди";
                        break;
                }

                recipeTexts[i].text = $"<b>{BattleOfAges.Crafting.ItemTypeNames.Display(type)} (рів. {selectedLevel})</b>  |  {statDesc}\n" +
                                      $"<color=#a0b0c8>Ціна: {wood} дерева  ·  {ore} руди рів. {oreTier}</color>";
            }
        }

        private void BuildUI()
        {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 300;
            }

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
            }

            if (GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Font defaultFont = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            panelObj = new GameObject("WorkbenchPanel", typeof(RectTransform));
            panelObj.transform.SetParent(transform, false);

            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(960f, 680f);

            Image panelBg = panelObj.AddComponent<Image>();
            panelBg.color = new Color(0.06f, 0.09f, 0.16f, 0.98f);

            // Header Bar
            GameObject headerObj = new GameObject("Header", typeof(RectTransform));
            headerObj.transform.SetParent(panelObj.transform, false);
            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 56f);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.10f, 0.15f, 0.28f, 1.0f);

            GameObject titleObj = new GameObject("TitleText", typeof(RectTransform));
            titleObj.transform.SetParent(headerObj.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = new Vector2(0.9f, 1f);
            titleRect.offsetMin = new Vector2(20f, 0);

            titleText = titleObj.AddComponent<Text>();
            titleText.font = defaultFont;
            titleText.fontSize = 22;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = new Color(0.3f, 0.92f, 1.0f);
            titleText.text = "⚒️ ВЕРСТАК — РЕЦЕПТИ ТА КРАФТ";

            // Close Button
            GameObject closeBtnObj = new GameObject("CloseBtn", typeof(RectTransform));
            closeBtnObj.transform.SetParent(headerObj.transform, false);
            RectTransform closeRect = closeBtnObj.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(0.92f, 0.15f);
            closeRect.anchorMax = new Vector2(0.98f, 0.85f);

            Image closeImg = closeBtnObj.AddComponent<Image>();
            closeImg.color = new Color(0.65f, 0.18f, 0.18f);
            Button closeBtn = closeBtnObj.AddComponent<Button>();
            closeBtn.onClick.AddListener(CloseUI);

            GameObject closeTxtObj = new GameObject("Txt", typeof(RectTransform));
            closeTxtObj.transform.SetParent(closeBtnObj.transform, false);
            RectTransform cTxtRect = closeTxtObj.GetComponent<RectTransform>();
            cTxtRect.anchorMin = Vector2.zero;
            cTxtRect.anchorMax = Vector2.one;
            Text closeTxt = closeTxtObj.AddComponent<Text>();
            closeTxt.font = defaultFont;
            closeTxt.fontSize = 18;
            closeTxt.fontStyle = FontStyle.Bold;
            closeTxt.alignment = TextAnchor.MiddleCenter;
            closeTxt.color = Color.white;
            closeTxt.text = "✕";

            // Workers Bar
            GameObject workerBar = new GameObject("WorkerControlBar", typeof(RectTransform));
            workerBar.transform.SetParent(panelObj.transform, false);
            RectTransform wBarRect = workerBar.GetComponent<RectTransform>();
            wBarRect.anchorMin = new Vector2(0f, 1f);
            wBarRect.anchorMax = new Vector2(1f, 1f);
            wBarRect.pivot = new Vector2(0.5f, 1f);
            wBarRect.anchoredPosition = new Vector2(0f, -62f);
            wBarRect.sizeDelta = new Vector2(-36f, 44f);

            Image wBarBg = workerBar.AddComponent<Image>();
            wBarBg.color = new Color(0.12f, 0.17f, 0.26f, 0.95f);

            GameObject wTxtObj = new GameObject("WorkerText", typeof(RectTransform));
            wTxtObj.transform.SetParent(workerBar.transform, false);
            RectTransform wTxtRect = wTxtObj.GetComponent<RectTransform>();
            wTxtRect.anchorMin = Vector2.zero;
            wTxtRect.anchorMax = new Vector2(0.65f, 1f);
            wTxtRect.offsetMin = new Vector2(15f, 0);

            workerCountText = wTxtObj.AddComponent<Text>();
            workerCountText.font = defaultFont;
            workerCountText.fontSize = 16;
            workerCountText.alignment = TextAnchor.MiddleLeft;
            workerCountText.color = new Color(1.0f, 0.88f, 0.4f);
            workerCountText.text = "⚒️ Робітники на верстаку: 0/4 (Швидкість: 0.00x)";

            CreateButton(workerBar.transform, defaultFont, "+ Призначити NPC", new Vector2(0.66f, 0.12f), new Vector2(0.82f, 0.88f), OnAssignWorkerClicked, new Color(0.22f, 0.48f, 0.72f));
            CreateButton(workerBar.transform, defaultFont, "- Зняти NPC", new Vector2(0.84f, 0.12f), new Vector2(0.98f, 0.88f), OnUnassignWorkerClicked, new Color(0.48f, 0.28f, 0.28f));

            // Crafting Progress Section (Height 58px)
            GameObject progBox = new GameObject("ProgressBox", typeof(RectTransform));
            progBox.transform.SetParent(panelObj.transform, false);
            RectTransform pBoxRect = progBox.GetComponent<RectTransform>();
            pBoxRect.anchorMin = new Vector2(0f, 1f);
            pBoxRect.anchorMax = new Vector2(1f, 1f);
            pBoxRect.pivot = new Vector2(0.5f, 1f);
            pBoxRect.anchoredPosition = new Vector2(0f, -112f);
            pBoxRect.sizeDelta = new Vector2(-36f, 58f);

            Image pBoxBg = progBox.AddComponent<Image>();
            pBoxBg.color = new Color(0.08f, 0.12f, 0.20f, 0.95f);

            GameObject curTxtObj = new GameObject("CurrentTaskText", typeof(RectTransform));
            curTxtObj.transform.SetParent(progBox.transform, false);
            RectTransform cTxtRect2 = curTxtObj.GetComponent<RectTransform>();
            cTxtRect2.anchorMin = new Vector2(0f, 0.48f);
            cTxtRect2.anchorMax = Vector2.one;
            cTxtRect2.offsetMin = new Vector2(15f, 0);

            currentTaskText = curTxtObj.AddComponent<Text>();
            currentTaskText.font = defaultFont;
            currentTaskText.fontSize = 14;
            currentTaskText.alignment = TextAnchor.MiddleLeft;
            currentTaskText.color = Color.white;
            currentTaskText.text = "Верстак вільний.";

            // Bar background & Fill (Using anchorMax.x for dynamic fill without sprite bugs)
            GameObject barBgObj = new GameObject("BarBg", typeof(RectTransform));
            barBgObj.transform.SetParent(progBox.transform, false);
            RectTransform barBgRect = barBgObj.GetComponent<RectTransform>();
            barBgRect.anchorMin = new Vector2(0.015f, 0.1f);
            barBgRect.anchorMax = new Vector2(0.985f, 0.44f);

            Image barBg = barBgObj.AddComponent<Image>();
            barBg.color = new Color(0.14f, 0.18f, 0.26f, 1f);

            GameObject fillObj = new GameObject("Fill", typeof(RectTransform));
            fillObj.transform.SetParent(barBgObj.transform, false);
            progressBarFillRect = fillObj.GetComponent<RectTransform>();
            progressBarFillRect.anchorMin = Vector2.zero;
            progressBarFillRect.anchorMax = new Vector2(0f, 1f); // Stretches horizontally via code
            progressBarFillRect.offsetMin = Vector2.zero;
            progressBarFillRect.offsetMax = Vector2.zero;

            Image fillImg = fillObj.AddComponent<Image>();
            fillImg.color = new Color(0.18f, 0.78f, 0.95f, 1f);

            GameObject pctObj = new GameObject("PercentText", typeof(RectTransform));
            pctObj.transform.SetParent(barBgObj.transform, false);
            RectTransform pctRect = pctObj.GetComponent<RectTransform>();
            pctRect.anchorMin = Vector2.zero;
            pctRect.anchorMax = Vector2.one;

            progressPercentText = pctObj.AddComponent<Text>();
            progressPercentText.font = defaultFont;
            progressPercentText.fontSize = 13;
            progressPercentText.fontStyle = FontStyle.Bold;
            progressPercentText.alignment = TextAnchor.MiddleCenter;
            progressPercentText.color = Color.white;
            progressPercentText.text = "0%";

            // Main Body: Split into Left Column (Crafting) and Right Column (Ready Storage)
            // Left Column: Level Tabs & Item Recipes (Width 580px)
            GameObject leftCol = new GameObject("LeftCraftingColumn", typeof(RectTransform));
            leftCol.transform.SetParent(panelObj.transform, false);
            RectTransform leftRect = leftCol.GetComponent<RectTransform>();
            leftRect.anchorMin = new Vector2(0f, 0f);
            leftRect.anchorMax = new Vector2(0.62f, 1f);
            leftRect.offsetMin = new Vector2(18f, 18f);
            leftRect.offsetMax = new Vector2(-10f, -178f);

            // Level Selection Tabs (1 to 5)
            GameObject tabsObj = new GameObject("LevelTabs", typeof(RectTransform));
            tabsObj.transform.SetParent(leftCol.transform, false);
            RectTransform tabsRect = tabsObj.GetComponent<RectTransform>();
            tabsRect.anchorMin = new Vector2(0f, 1f);
            tabsRect.anchorMax = new Vector2(1f, 1f);
            tabsRect.pivot = new Vector2(0.5f, 1f);
            tabsRect.anchoredPosition = Vector2.zero;
            tabsRect.sizeDelta = new Vector2(0f, 38f);

            levelTabButtons.Clear();
            for (int lvl = 1; lvl <= 5; lvl++)
            {
                int lvlIndex = lvl;
                float minX = (lvl - 1) * 0.20f;
                float maxX = lvl * 0.20f - 0.01f;

                Button tabBtn = CreateButton(tabsObj.transform, defaultFont, $"Рівень {lvl}",
                    new Vector2(minX, 0f), new Vector2(maxX, 1f),
                    () => SetSelectedLevel(lvlIndex),
                    (lvl == 1) ? new Color(0.85f, 0.65f, 0.15f, 1f) : new Color(0.18f, 0.24f, 0.36f, 0.95f));

                levelTabButtons.Add(tabBtn);
            }

            // Recipe Cards Container (5 items)
            ItemType[] itemTypes = new ItemType[]
            {
                ItemType.Sword,
                ItemType.Bow,
                ItemType.Armor,
                ItemType.Axe,
                ItemType.Pickaxe
            };

            recipeTexts.Clear();
            float cardHeight = 78f;
            for (int i = 0; i < itemTypes.Length; i++)
            {
                ItemType itype = itemTypes[i];
                GameObject itemCard = new GameObject($"Card_{itype}", typeof(RectTransform));
                itemCard.transform.SetParent(leftCol.transform, false);
                RectTransform cardRect = itemCard.GetComponent<RectTransform>();
                cardRect.anchorMin = new Vector2(0f, 1f);
                cardRect.anchorMax = new Vector2(1f, 1f);
                cardRect.pivot = new Vector2(0.5f, 1f);
                cardRect.anchoredPosition = new Vector2(0f, -44f - i * (cardHeight + 6f));
                cardRect.sizeDelta = new Vector2(0f, cardHeight);

                Image cardBg = itemCard.AddComponent<Image>();
                cardBg.color = new Color(0.10f, 0.14f, 0.22f, 0.95f);

                // Recipe text
                GameObject descObj = new GameObject("DescText", typeof(RectTransform));
                descObj.transform.SetParent(itemCard.transform, false);
                RectTransform descRect = descObj.GetComponent<RectTransform>();
                descRect.anchorMin = Vector2.zero;
                descRect.anchorMax = new Vector2(0.72f, 1f);
                descRect.offsetMin = new Vector2(12f, 0);

                Text desc = descObj.AddComponent<Text>();
                desc.font = defaultFont;
                desc.fontSize = 14;
                desc.alignment = TextAnchor.MiddleLeft;
                desc.color = Color.white;
                recipeTexts.Add(desc);

                // Craft Button
                CreateButton(itemCard.transform, defaultFont, "⚒️ Виготовити",
                    new Vector2(0.74f, 0.16f), new Vector2(0.98f, 0.84f),
                    () => OnCraftButtonClicked(itype),
                    new Color(0.18f, 0.45f, 0.75f));
            }

            // Right Column: Finished Goods Storage (Width 340px)
            GameObject rightCol = new GameObject("RightOutputColumn", typeof(RectTransform));
            rightCol.transform.SetParent(panelObj.transform, false);
            RectTransform rightRect = rightCol.GetComponent<RectTransform>();
            rightRect.anchorMin = new Vector2(0.63f, 0f);
            rightRect.anchorMax = new Vector2(1f, 1f);
            rightRect.offsetMin = new Vector2(10f, 18f);
            rightRect.offsetMax = new Vector2(-18f, -178f);

            Image rightBg = rightCol.AddComponent<Image>();
            rightBg.color = new Color(0.08f, 0.11f, 0.18f, 0.95f);

            // Storage Title
            GameObject sTitleObj = new GameObject("StorageHeader", typeof(RectTransform));
            sTitleObj.transform.SetParent(rightCol.transform, false);
            RectTransform sTitleRect = sTitleObj.GetComponent<RectTransform>();
            sTitleRect.anchorMin = new Vector2(0f, 1f);
            sTitleRect.anchorMax = new Vector2(1f, 1f);
            sTitleRect.pivot = new Vector2(0.5f, 1f);
            sTitleRect.sizeDelta = new Vector2(0f, 38f);

            Image sTitleBg = sTitleObj.AddComponent<Image>();
            sTitleBg.color = new Color(0.14f, 0.19f, 0.30f, 1.0f);

            GameObject stTxtObj = new GameObject("Txt", typeof(RectTransform));
            stTxtObj.transform.SetParent(sTitleObj.transform, false);
            RectTransform stTxtRect = stTxtObj.GetComponent<RectTransform>();
            stTxtRect.anchorMin = Vector2.zero;
            stTxtRect.anchorMax = Vector2.one;
            stTxtRect.offsetMin = new Vector2(12f, 0);

            Text stTxt = stTxtObj.AddComponent<Text>();
            stTxt.font = defaultFont;
            stTxt.fontSize = 15;
            stTxt.fontStyle = FontStyle.Bold;
            stTxt.alignment = TextAnchor.MiddleLeft;
            stTxt.color = new Color(1.0f, 0.85f, 0.4f);
            stTxt.text = "📦 Склад готових виробів";

            // Storage Items Container (Vertical Layout)
            GameObject outContainer = new GameObject("OutputItemsContainer", typeof(RectTransform));
            outContainer.transform.SetParent(rightCol.transform, false);
            RectTransform outRect = outContainer.GetComponent<RectTransform>();
            outRect.anchorMin = new Vector2(0f, 0f);
            outRect.anchorMax = new Vector2(1f, 1f);
            outRect.offsetMin = new Vector2(8f, 8f);
            outRect.offsetMax = new Vector2(-8f, -44f);

            VerticalLayoutGroup vlg = outContainer.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6f;
            vlg.childControlHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            outputItemsContainer = outContainer.transform;

            // Empty text placeholder
            GameObject emptyObj = new GameObject("EmptyPlaceholder", typeof(RectTransform));
            emptyObj.transform.SetParent(rightCol.transform, false);
            RectTransform empRect = emptyObj.GetComponent<RectTransform>();
            empRect.anchorMin = Vector2.zero;
            empRect.anchorMax = new Vector2(1f, 0.9f);

            emptyOutputText = emptyObj.AddComponent<Text>();
            emptyOutputText.font = defaultFont;
            emptyOutputText.fontSize = 14;
            emptyOutputText.alignment = TextAnchor.MiddleCenter;
            emptyOutputText.color = new Color(0.6f, 0.7f, 0.85f, 0.8f);
            emptyOutputText.text = "Склад порожній.\nСкрафтіть предмети зліва.";

            UpdateRecipeButtonLabels();
            UpdateLevelTabStyles();
        }

        private Button CreateButton(Transform parent, Font font, string text, Vector2 anchorMin, Vector2 anchorMax, UnityEngine.Events.UnityAction onClick, Color? bgColor = null)
        {
            GameObject btnObj = new GameObject($"Btn_{text}", typeof(RectTransform));
            btnObj.transform.SetParent(parent, false);

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            Image img = btnObj.AddComponent<Image>();
            img.color = bgColor ?? new Color(0.20f, 0.38f, 0.65f, 1.0f);

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.highlightedColor = (bgColor ?? new Color(0.20f, 0.38f, 0.65f)) * 1.25f;
            cb.pressedColor = (bgColor ?? new Color(0.20f, 0.38f, 0.65f)) * 0.85f;
            btn.colors = cb;
            btn.onClick.AddListener(onClick);

            GameObject txtObj = new GameObject("Text", typeof(RectTransform));
            txtObj.transform.SetParent(btnObj.transform, false);

            RectTransform txtRt = txtObj.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;

            Text txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 13;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = text;

            return btn;
        }
    }
}
