using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Resources;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;

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

        private Text woodQuantityText;
        private GameObject woodSlotObj;

        private readonly Dictionary<int, Text> oreQuantityTextMap = new Dictionary<int, Text>();
        private readonly Dictionary<int, GameObject> oreSlotObjMap = new Dictionary<int, GameObject>();

        private Text titleText;

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
            KeybindsManager.OnKeybindsChanged += UpdateHeaderText;
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

            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.OnResourceChanged -= OnResourceChangedHandler;
                ResourceManager.Instance.OnOreTierAdded -= OnOreTierAddedHandler;
            }
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
                GameModeManager gmm = FindFirstObjectByType<GameModeManager>();
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

            int activeItemsCount = (woodCount > 0) ? 1 : 0;

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

                Text bTxt = UiTheme.PlainText(badgeTxtObj, font, 17, TextAnchor.MiddleCenter, tierCol, $"Рів. {tierLevel}", style: FontStyle.Bold);

                RectTransform nameRect = UiTheme.Child(slotObj.transform, "NameText", Vector2.zero, Vector2.one, new Vector2(115f, 0f), new Vector2(-120f, 0f));
                GameObject nameObj = nameRect.gameObject;

                Text nameTxt = UiTheme.PlainText(nameObj, font, 20, TextAnchor.MiddleLeft, Color.white, OreTierHelper.GetTierName(tierLevel), style: FontStyle.Bold);

                RectTransform qtyRect = UiTheme.Child(slotObj.transform, "QtyText", new Vector2(1f, 0f), new Vector2(1f, 1f));
                GameObject qtyObj = qtyRect.gameObject;
                qtyRect.pivot = new Vector2(1f, 0.5f);
                qtyRect.offsetMin = new Vector2(-110f, 0f);
                qtyRect.offsetMax = new Vector2(-15f, 0f);

                Text qtyTxt = UiTheme.PlainText(qtyObj, font, 22, TextAnchor.MiddleRight, new Color(0.3f, 0.95f, 0.95f), style: FontStyle.Bold);

                oreSlotObjMap[tierLevel] = slotObj;
                oreQuantityTextMap[tierLevel] = qtyTxt;
            }

            if (oreSlotObjMap.TryGetValue(tierLevel, out GameObject slot))
            {
                slot.SetActive(true);
            }

            if (oreQuantityTextMap.TryGetValue(tierLevel, out Text qtyText))
            {
                qtyText.text = $"x{count}";
            }
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

            RectTransform titleRect = UiTheme.Child(headerObj.transform, "TitleText", Vector2.zero, Vector2.one, new Vector2(25f, 0), new Vector2(-25f, 0));
            GameObject titleObj = titleRect.gameObject;

            titleText = titleObj.AddComponent<Text>();
            titleText.font = defaultFont;
            titleText.fontSize = 28;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = new Color(0.3f, 0.9f, 1.0f);
            UpdateHeaderText();

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

            Text wBTxt = UiTheme.PlainText(wBadgeTxtObj, defaultFont, 17, TextAnchor.MiddleCenter, new Color(1.0f, 0.92f, 0.55f), "Дерево", style: FontStyle.Bold);

            RectTransform wNameRect = UiTheme.Child(woodSlotObj.transform, "NameText", Vector2.zero, Vector2.one, new Vector2(115f, 0f), new Vector2(-120f, 0f));
            GameObject wNameObj = wNameRect.gameObject;

            Text woodNameText = UiTheme.PlainText(wNameObj, defaultFont, 20, TextAnchor.MiddleLeft, new Color(0.98f, 0.88f, 0.45f), "Колоди", style: FontStyle.Bold);

            RectTransform wQtyRect = UiTheme.Child(woodSlotObj.transform, "QtyText", new Vector2(1f, 0f), new Vector2(1f, 1f));
            GameObject wQtyObj = wQtyRect.gameObject;
            wQtyRect.pivot = new Vector2(1f, 0.5f);
            wQtyRect.offsetMin = new Vector2(-110f, 0f);
            wQtyRect.offsetMax = new Vector2(-15f, 0f);

            woodQuantityText = wQtyObj.AddComponent<Text>();
            woodQuantityText.font = defaultFont;
            woodQuantityText.fontSize = 22;
            woodQuantityText.fontStyle = FontStyle.Bold;
            woodQuantityText.alignment = TextAnchor.MiddleRight;
            woodQuantityText.color = new Color(0.3f, 0.95f, 0.95f);
            woodQuantityText.text = "x0";

            emptyPlaceholderObj = new GameObject("EmptyPlaceholder", typeof(RectTransform));
            emptyPlaceholderObj.transform.SetParent(contentObj.transform, false);
            RectTransform emptyRect = emptyPlaceholderObj.GetComponent<RectTransform>();
            emptyRect.sizeDelta = new Vector2(740f, 120f);

            Text emptyTxt = UiTheme.PlainText(emptyPlaceholderObj, defaultFont, 22, TextAnchor.MiddleCenter, new Color(0.6f, 0.68f, 0.78f), "Inventory is empty!\nHarvest trees or pick up pebbles in the steppe.");

            if (inventoryPanelObj != null) inventoryPanelObj.SetActive(false);
        }

        private void UpdateHeaderText()
        {
            if (titleText != null)
            {
                KeyCode key = KeybindsManager.GetKey(KeyAction.Inventory);
                string keyName = KeybindsManager.GetKeyDisplayString(key);
                titleText.text = $"🎒 ІНВЕНТАР ГРАВЦЯ (Клавіша '{keyName}')";
            }
        }
    }
}
