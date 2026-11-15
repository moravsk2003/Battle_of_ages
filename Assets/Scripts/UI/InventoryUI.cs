using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Resources;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using TMPro;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Player Inventory modal UI displaying harvested wood and 100-level ore tiers. Toggle with 'I' key.
    /// Safely unsubscribes from event channels on destroy.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI Instance { get; private set; }

        private GameObject inventoryPanelObj;
        private Transform contentTransform;
        private GameObject emptyPlaceholderObj;

        private TMP_Text woodQuantityText, woodBadgeText, woodNameLabel;
        private GameObject woodSlotObj;
        private GameObject foodSlotObj;
        private TMP_Text foodQuantityText, foodBadgeText, foodNameLabel;
        private int lastFoodCount = -1;

        private readonly Dictionary<int, TMP_Text> oreQuantityTextMap = new Dictionary<int, TMP_Text>();
        private readonly Dictionary<int, TMP_Text> oreBadgeTextMap = new Dictionary<int, TMP_Text>();
        private readonly Dictionary<int, TMP_Text> oreNameTextMap = new Dictionary<int, TMP_Text>();
        private readonly Dictionary<int, GameObject> oreSlotObjMap = new Dictionary<int, GameObject>();

        private TMP_Text titleText, emptyText;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            IsOpen = false;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            BuildInventoryUI();
            UiTheme.RestyleTree(transform); // its parts made with default looks take the theme now, not from a pass over every canvas
            if (GetComponent<UiThemed>() == null) gameObject.AddComponent<UiThemed>();
            KeybindsManager.OnKeybindsChanged += UpdateHeaderText;
            Loc.Changed += OnLanguageChanged;
        }

        private void Start()
        {
            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.OnResourceChanged += OnResourceChangedHandler;
                ResourceManager.Instance.OnOreTierAdded += OnOreTierAddedHandler;
            }

            CloseInventory();
        }

        private void OnDestroy()
        {
            KeybindsManager.OnKeybindsChanged -= UpdateHeaderText;
            Loc.Changed -= OnLanguageChanged;

            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.OnResourceChanged -= OnResourceChangedHandler;
                ResourceManager.Instance.OnOreTierAdded -= OnOreTierAddedHandler;
            }
        }

        private void OnLanguageChanged()
        {
            UpdateHeaderText();
            if (woodBadgeText != null) woodBadgeText.text = Loc.Pick("Дерево", "Wood");
            if (woodNameLabel != null) woodNameLabel.text = Loc.Pick("Колоди", "Logs");
            if (foodBadgeText != null) foodBadgeText.text = Loc.Pick("Їжа", "Food");
            if (foodNameLabel != null) foodNameLabel.text = Loc.Pick("Провізія", "Provisions");
            foreach (var kv in oreBadgeTextMap)
                if (kv.Value != null) kv.Value.text = Loc.Pick($"Рів. {kv.Key}", $"Lvl {kv.Key}");
            foreach (var kv in oreNameTextMap)
                if (kv.Value != null) kv.Value.text = OreTierHelper.GetTierName(kv.Key);
            if (emptyText != null)
                emptyText.text = Loc.Pick("Інвентар порожній!\nЗбирайте дерева або підбирайте камінці руди в степу.", "Inventory is empty!\nHarvest trees or pick up ore pebbles in the steppe.");
        }

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

                if (UniversalInput.GetKeyDownEscape() || IsToggleInventoryKeyPressed())
                {
                    CloseInventory();
                    return;
                }

                if (ResourceManager.Instance != null)
                {
                    int fc = ResourceManager.FoodRules ? ResourceManager.Instance.FoodCount : -1;
                    if (fc != lastFoodCount) RefreshInventoryDisplay();
                }
            }
            else
            {
                if (GameWindows.WorkersOpen || GameWindows.BuildMenuOpen) return;

                if (IsToggleInventoryKeyPressed())
                {
                    OpenInventory();
                }
            }
        }

        private bool IsToggleInventoryKeyPressed()
        {
            return KeybindsManager.GetKeyDown(KeyAction.Inventory);
        }

        public void ToggleInventory()
        {
            if (IsOpen) CloseInventory();
            else OpenInventory();
        }

        public void OpenInventory()
        {
            IsOpen = true;
            if (inventoryPanelObj != null) inventoryPanelObj.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            RefreshInventoryDisplay();
            UpdateHeaderText();
        }

        public void CloseInventory()
        {
            IsOpen = false;
            if (inventoryPanelObj != null) inventoryPanelObj.SetActive(false);

            if ((NpcGatherCommandUI.Instance == null || !NpcGatherCommandUI.Instance.IsOpen) &&
                (BuildMenuUI.Instance == null || !BuildMenuUI.Instance.IsOpen) &&
                !GameWindows.Paused)
            {
                GameModeManager gmm = Managers.GameContext.Modes;
                if (gmm == null || gmm.CurrentMode == GameMode.FirstPerson)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        private void OnResourceChangedHandler(ResourceType type, int amount)
        {
            if (IsOpen) RefreshInventoryDisplay();
        }

        private void OnOreTierAddedHandler(int tierLevel, int amount)
        {
            if (IsOpen) RefreshInventoryDisplay();
        }

        public void RefreshInventoryDisplay()
        {
            if (ResourceManager.Instance == null) return;

            int woodCount = ResourceManager.Instance.WoodCount;
            if (woodSlotObj != null)
            {
                woodSlotObj.SetActive(woodCount > 0);
            }
            if (woodQuantityText != null)
            {
                woodQuantityText.text = $"x{woodCount}";
            }

            int foodCount = ResourceManager.FoodRules ? ResourceManager.Instance.FoodCount : -1;
            lastFoodCount = foodCount;
            bool showFood = ResourceManager.FoodRules && foodCount > 0;
            if (foodSlotObj != null)
            {
                foodSlotObj.SetActive(showFood);
            }
            if (foodQuantityText != null)
            {
                foodQuantityText.text = $"x{Mathf.Max(0, foodCount)}";
            }

            int activeItemsCount = (woodCount > 0 ? 1 : 0) + (showFood ? 1 : 0);

            for (int i = 0; i < OreTierHelper.MAX_TIERS; i++)
            {
                int count = ResourceManager.Instance.GetOreTierCount(i);
                if (count > 0)
                {
                    activeItemsCount++;
                    UpdateOreSlot(i, count);
                }
                else if (oreSlotObjMap.TryGetValue(i, out GameObject slotObj))
                {
                    slotObj.SetActive(false);
                }
            }

            if (emptyPlaceholderObj != null)
            {
                emptyPlaceholderObj.SetActive(activeItemsCount == 0);
                if (activeItemsCount == 0)
                {
                    emptyPlaceholderObj.transform.SetAsLastSibling();
                }
            }
        }

        private void UpdateOreSlot(int tierLevel, int count)
        {
            if (contentTransform == null) return;

            Font font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (!oreSlotObjMap.ContainsKey(tierLevel))
            {
                GameObject slotObj = new GameObject($"OreSlot_Lvl{tierLevel}", typeof(RectTransform));
                slotObj.transform.SetParent(contentTransform, false);

                RectTransform slotRect = slotObj.GetComponent<RectTransform>();
                slotRect.sizeDelta = new Vector2(740f, 54f);

                Image bg = slotObj.AddComponent<Image>();
                bg.color = new Color(0.09f, 0.13f, 0.20f, 0.94f);

                RectTransform badgeRect = UiTheme.Child(slotObj.transform, "Badge", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 8f), new Vector2(100f, -8f));
                GameObject badgeObj = badgeRect.gameObject;

                Image badgeBg = badgeObj.AddComponent<Image>();
                Color tierCol = OreTierHelper.GetTierColor(tierLevel);
                badgeBg.color = new Color(tierCol.r * 0.35f, tierCol.g * 0.35f, tierCol.b * 0.35f, 0.95f);

                RectTransform bTxtRect = UiTheme.Child(badgeObj.transform, "BadgeText", Vector2.zero, Vector2.one);
                GameObject badgeTxtObj = bTxtRect.gameObject;

                TMP_Text bTxt = UiTheme.PlainText(badgeTxtObj, font, 17, TextAnchor.MiddleCenter, tierCol, Loc.Pick($"Рів. {tierLevel}", $"Lvl {tierLevel}"), style: FontStyle.Bold);
                LocText.Detach(bTxt);

                RectTransform nameRect = UiTheme.Child(slotObj.transform, "NameText", Vector2.zero, Vector2.one, new Vector2(115f, 0f), new Vector2(-120f, 0f));
                GameObject nameObj = nameRect.gameObject;

                TMP_Text nameTxt = UiTheme.PlainText(nameObj, font, 20, TextAnchor.MiddleLeft, Color.white, OreTierHelper.GetTierName(tierLevel), style: FontStyle.Bold);
                LocText.Detach(nameTxt);

                RectTransform qtyRect = UiTheme.Child(slotObj.transform, "QtyText", new Vector2(1f, 0f), new Vector2(1f, 1f));
                GameObject qtyObj = qtyRect.gameObject;
                qtyRect.pivot = new Vector2(1f, 0.5f);
                qtyRect.offsetMin = new Vector2(-110f, 0f);
                qtyRect.offsetMax = new Vector2(-15f, 0f);

                TMP_Text qtyTxt = UiTheme.PlainText(qtyObj, font, 22, TextAnchor.MiddleRight, new Color(0.3f, 0.95f, 0.95f), style: FontStyle.Bold);
                LocText.Detach(qtyTxt);

                UiTheme.RestyleTree(slotObj.transform); // a row made after the window: themed now
                Tooltip.Set(slotObj, () => OreSlotTip(tierLevel));
                oreSlotObjMap[tierLevel] = slotObj;
                oreBadgeTextMap[tierLevel] = bTxt;
                oreNameTextMap[tierLevel] = nameTxt;
                oreQuantityTextMap[tierLevel] = qtyTxt;
            }

            if (oreSlotObjMap.TryGetValue(tierLevel, out GameObject slot))
            {
                slot.SetActive(true);
            }

            if (oreQuantityTextMap.TryGetValue(tierLevel, out TMP_Text qtyText))
            {
                qtyText.text = $"x{count}";
            }
        }

        private static string OreSlotTip(int tierLevel)
        {
            ResourceManager rm = ResourceManager.Instance;
            int count = rm != null ? rm.GetOreTierCount(tierLevel) : 0;
            string roman = tierLevel == 0 ? "0" : Crafting.ItemTypeNames.Roman(tierLevel);
            string tierName = OreTierHelper.GetTierName(tierLevel);
            return TipText.Title(Loc.Pick($"{tierName} (ранг {roman})", $"{tierName} (tier {roman})"), OreTierHelper.GetTierColor(tierLevel))
                + "\n" + Loc.Pick(
                    $"У запасах: {count}. Іде на кування спорядження, будівництво, покращення ратуші й стін та на переплавку ({Combat.GameBalance.SmeltRatio} → 1 вищого рангу).",
                    $"In stock: {count}. Used for crafting gear, building, upgrading the Town Hall and walls, and smelting ({Combat.GameBalance.SmeltRatio} → 1 higher tier).");
        }

        private void BuildInventoryUI()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Font defaultFont = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            RectTransform panelRect = UiTheme.Child(transform, "InventoryPanel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            PopIn.On(panelRect);
            inventoryPanelObj = panelRect.gameObject;
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(840f, 680f);

            Image panelBg = inventoryPanelObj.AddComponent<Image>();
            panelBg.color = new Color(0.04f, 0.07f, 0.12f, 0.97f);

            RectTransform headerRect = UiTheme.Child(inventoryPanelObj.transform, "HeaderBar", new Vector2(0f, 1f), new Vector2(1f, 1f));
            GameObject headerObj = headerRect.gameObject;
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0, 70f);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.08f, 0.13f, 0.22f, 0.98f);

            RectTransform titleRect = UiTheme.Child(headerObj.transform, "TitleText", Vector2.zero, Vector2.one, new Vector2(25f, 0), new Vector2(-75f, 0));
            GameObject titleObj = titleRect.gameObject;

            titleText = titleObj.AddComponent<TextMeshProUGUI>();
            titleText.font = UiTheme.TmpFont;
            titleText.fontSize = 28;
            titleText.fontStyle = UiTheme.Style(FontStyle.Bold);
            titleText.alignment = UiTheme.Align(TextAnchor.MiddleLeft);
            titleText.color = new Color(0.3f, 0.9f, 1.0f);
            UpdateHeaderText();

            Button closeBtn = UiTheme.TextButton(headerRect, "Close", "×", new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(44f, 44f), CloseInventory, 22);
            closeBtn.GetComponent<Image>().color = new Color(1f, 0.72f, 0.68f);
            Tooltip.Set(closeBtn.gameObject, () => Loc.Pick("Закрити інвентар", "Close inventory"));

            RectTransform scrollRect = UiTheme.Child(inventoryPanelObj.transform, "ScrollView", Vector2.zero, Vector2.one, new Vector2(20f, 20f), new Vector2(-20f, -85f));
            GameObject scrollObj = scrollRect.gameObject;

            ScrollRect scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            RectTransform viewportRect = UiTheme.Child(scrollObj.transform, "Viewport", Vector2.zero, Vector2.one);
            GameObject viewportObj = viewportRect.gameObject;

            Image viewportMask = viewportObj.AddComponent<Image>();
            viewportMask.color = Color.white;
            Mask mask = viewportObj.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            scroll.viewport = viewportRect;

            GameObject contentObj = new GameObject("Content", typeof(RectTransform));
            contentObj.transform.SetParent(viewportObj.transform, false);
            contentTransform = contentObj.transform;

            RectTransform contentRect = contentObj.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup contentLayout = contentObj.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(15, 15, 15, 15);
            contentLayout.spacing = 10f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;

            ContentSizeFitter contentFitter = contentObj.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRect;

            woodSlotObj = new GameObject("WoodSlot", typeof(RectTransform));
            woodSlotObj.transform.SetParent(contentObj.transform, false);
            RectTransform woodSlotRect = woodSlotObj.GetComponent<RectTransform>();
            woodSlotRect.sizeDelta = new Vector2(740f, 54f);

            Image woodBg = woodSlotObj.AddComponent<Image>();
            woodBg.color = new Color(0.16f, 0.12f, 0.06f, 0.95f);

            RectTransform wBadgeRect = UiTheme.Child(woodSlotObj.transform, "Badge", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 8f), new Vector2(100f, -8f));
            GameObject woodBadge = wBadgeRect.gameObject;

            Image wBadgeBg = woodBadge.AddComponent<Image>();
            wBadgeBg.color = new Color(0.55f, 0.40f, 0.15f, 0.95f);

            RectTransform wBTxtRect = UiTheme.Child(woodBadge.transform, "BadgeText", Vector2.zero, Vector2.one);
            GameObject wBadgeTxtObj = wBTxtRect.gameObject;

            woodBadgeText = UiTheme.PlainText(wBadgeTxtObj, defaultFont, 17, TextAnchor.MiddleCenter, new Color(1.0f, 0.92f, 0.55f), Loc.Pick("Дерево", "Wood"), style: FontStyle.Bold);
            LocText.Detach(woodBadgeText);

            RectTransform wNameRect = UiTheme.Child(woodSlotObj.transform, "NameText", Vector2.zero, Vector2.one, new Vector2(115f, 0f), new Vector2(-120f, 0f));
            GameObject wNameObj = wNameRect.gameObject;

            woodNameLabel = UiTheme.PlainText(wNameObj, defaultFont, 20, TextAnchor.MiddleLeft, new Color(0.98f, 0.88f, 0.45f), Loc.Pick("Колоди", "Logs"), style: FontStyle.Bold);
            LocText.Detach(woodNameLabel);

            RectTransform wQtyRect = UiTheme.Child(woodSlotObj.transform, "QtyText", new Vector2(1f, 0f), new Vector2(1f, 1f));
            GameObject wQtyObj = wQtyRect.gameObject;
            wQtyRect.pivot = new Vector2(1f, 0.5f);
            wQtyRect.offsetMin = new Vector2(-110f, 0f);
            wQtyRect.offsetMax = new Vector2(-15f, 0f);

            woodQuantityText = wQtyObj.AddComponent<TextMeshProUGUI>();
            woodQuantityText.font = UiTheme.TmpFont;
            woodQuantityText.fontSize = 22;
            woodQuantityText.fontStyle = UiTheme.Style(FontStyle.Bold);
            woodQuantityText.alignment = UiTheme.Align(TextAnchor.MiddleRight);
            woodQuantityText.color = new Color(0.3f, 0.95f, 0.95f);
            woodQuantityText.text = "x0";
            Tooltip.Set(woodSlotObj, HudTips.Wood);

            foodSlotObj = new GameObject("FoodSlot", typeof(RectTransform));
            foodSlotObj.transform.SetParent(contentObj.transform, false);
            RectTransform foodSlotRect = foodSlotObj.GetComponent<RectTransform>();
            foodSlotRect.sizeDelta = new Vector2(740f, 54f);

            Image foodBg = foodSlotObj.AddComponent<Image>();
            foodBg.color = new Color(0.10f, 0.16f, 0.08f, 0.95f);

            RectTransform fBadgeRect = UiTheme.Child(foodSlotObj.transform, "Badge", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(10f, 8f), new Vector2(100f, -8f));
            GameObject foodBadge = fBadgeRect.gameObject;

            Image fBadgeBg = foodBadge.AddComponent<Image>();
            fBadgeBg.color = new Color(0.28f, 0.52f, 0.20f, 0.95f);

            RectTransform fBTxtRect = UiTheme.Child(foodBadge.transform, "BadgeText", Vector2.zero, Vector2.one);
            GameObject fBadgeTxtObj = fBTxtRect.gameObject;

            foodBadgeText = UiTheme.PlainText(fBadgeTxtObj, defaultFont, 17, TextAnchor.MiddleCenter, new Color(0.75f, 1.0f, 0.65f), Loc.Pick("Їжа", "Food"), style: FontStyle.Bold);
            LocText.Detach(foodBadgeText);

            RectTransform fNameRect = UiTheme.Child(foodSlotObj.transform, "NameText", Vector2.zero, Vector2.one, new Vector2(115f, 0f), new Vector2(-120f, 0f));
            GameObject fNameObj = fNameRect.gameObject;

            foodNameLabel = UiTheme.PlainText(fNameObj, defaultFont, 20, TextAnchor.MiddleLeft, new Color(0.78f, 0.98f, 0.62f), Loc.Pick("Провізія", "Provisions"), style: FontStyle.Bold);
            LocText.Detach(foodNameLabel);

            RectTransform fQtyRect = UiTheme.Child(foodSlotObj.transform, "QtyText", new Vector2(1f, 0f), new Vector2(1f, 1f));
            GameObject fQtyObj = fQtyRect.gameObject;
            fQtyRect.pivot = new Vector2(1f, 0.5f);
            fQtyRect.offsetMin = new Vector2(-110f, 0f);
            fQtyRect.offsetMax = new Vector2(-15f, 0f);

            foodQuantityText = fQtyObj.AddComponent<TextMeshProUGUI>();
            foodQuantityText.font = UiTheme.TmpFont;
            foodQuantityText.fontSize = 22;
            foodQuantityText.fontStyle = UiTheme.Style(FontStyle.Bold);
            foodQuantityText.alignment = UiTheme.Align(TextAnchor.MiddleRight);
            foodQuantityText.color = new Color(0.65f, 0.98f, 0.55f);
            foodQuantityText.text = "x0";
            Tooltip.Set(foodSlotObj, HudTips.Food);
            foodSlotObj.SetActive(false);

            emptyPlaceholderObj = new GameObject("EmptyPlaceholder", typeof(RectTransform));
            emptyPlaceholderObj.transform.SetParent(contentObj.transform, false);
            RectTransform emptyRect = emptyPlaceholderObj.GetComponent<RectTransform>();
            emptyRect.sizeDelta = new Vector2(740f, 120f);

            emptyText = UiTheme.PlainText(emptyPlaceholderObj, defaultFont, 22, TextAnchor.MiddleCenter, new Color(0.6f, 0.68f, 0.78f),
                Loc.Pick("Інвентар порожній!\nЗбирайте дерева або підбирайте камінці руди в степу.", "Inventory is empty!\nHarvest trees or pick up ore pebbles in the steppe."));
            LocText.Detach(emptyText);

            if (inventoryPanelObj != null) inventoryPanelObj.SetActive(false);
        }

        private void UpdateHeaderText()
        {
            if (titleText != null)
            {
                KeyCode key = KeybindsManager.GetKey(KeyAction.Inventory);
                string keyName = KeybindsManager.GetKeyDisplayString(key);
                titleText.text = Loc.Pick($"Інвентар  <size=17><color=#B8A88E>[{keyName}]</color></size>", $"Inventory  <size=17><color=#B8A88E>[{keyName}]</color></size>");
            }
        }
    }
}
