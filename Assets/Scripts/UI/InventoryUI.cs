using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Resources;
using BattleOfAges.InputSystem;

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

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            BuildInventoryUI();
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
            if (ResourceManager.Instance != null)
            {
                ResourceManager.Instance.OnResourceChanged -= OnResourceChangedHandler;
                ResourceManager.Instance.OnOreTierAdded -= OnOreTierAddedHandler;
            }
        }

        private void Update()
        {
            if (UniversalInput.GetKeyDownEscape() && IsOpen)
            {
                CloseInventory();
                return;
            }

            if (IsToggleInventoryKeyPressed())
            {
                ToggleInventory();
            }
        }

        private bool IsToggleInventoryKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.iKey.wasPressedThisFrame) return true;
#endif
            try { return Input.GetKeyDown(KeyCode.I); } catch { return false; }
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
        }

        public void CloseInventory()
        {
            IsOpen = false;
            if (inventoryPanelObj != null) inventoryPanelObj.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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

                GameObject badgeObj = new GameObject("Badge", typeof(RectTransform));
                badgeObj.transform.SetParent(slotObj.transform, false);
                RectTransform badgeRect = badgeObj.GetComponent<RectTransform>();
                badgeRect.anchorMin = new Vector2(0f, 0f);
                badgeRect.anchorMax = new Vector2(0f, 1f);
                badgeRect.offsetMin = new Vector2(10f, 8f);
                badgeRect.offsetMax = new Vector2(100f, -8f);

                Image badgeBg = badgeObj.AddComponent<Image>();
                Color tierCol = OreTierHelper.GetTierColor(tierLevel);
                badgeBg.color = new Color(tierCol.r * 0.35f, tierCol.g * 0.35f, tierCol.b * 0.35f, 0.95f);

                GameObject badgeTxtObj = new GameObject("BadgeText", typeof(RectTransform));
                badgeTxtObj.transform.SetParent(badgeObj.transform, false);
                RectTransform bTxtRect = badgeTxtObj.GetComponent<RectTransform>();
                bTxtRect.anchorMin = Vector2.zero;
                bTxtRect.anchorMax = Vector2.one;

                Text bTxt = badgeTxtObj.AddComponent<Text>();
                bTxt.font = font;
                bTxt.fontSize = 17;
                bTxt.fontStyle = FontStyle.Bold;
                bTxt.alignment = TextAnchor.MiddleCenter;
                bTxt.color = tierCol;
                bTxt.text = $"Lvl {tierLevel}";

                GameObject nameObj = new GameObject("NameText", typeof(RectTransform));
                nameObj.transform.SetParent(slotObj.transform, false);
                RectTransform nameRect = nameObj.GetComponent<RectTransform>();
                nameRect.anchorMin = Vector2.zero;
                nameRect.anchorMax = Vector2.one;
                nameRect.offsetMin = new Vector2(115f, 0f);
                nameRect.offsetMax = new Vector2(-120f, 0f);

                Text nameTxt = nameObj.AddComponent<Text>();
                nameTxt.font = font;
                nameTxt.fontSize = 20;
                nameTxt.fontStyle = FontStyle.Bold;
                nameTxt.alignment = TextAnchor.MiddleLeft;
                nameTxt.color = Color.white;
                nameTxt.text = OreTierHelper.GetTierName(tierLevel);

                GameObject qtyObj = new GameObject("QtyText", typeof(RectTransform));
                qtyObj.transform.SetParent(slotObj.transform, false);
                RectTransform qtyRect = qtyObj.GetComponent<RectTransform>();
                qtyRect.anchorMin = new Vector2(1f, 0f);
                qtyRect.anchorMax = new Vector2(1f, 1f);
                qtyRect.pivot = new Vector2(1f, 0.5f);
                qtyRect.offsetMin = new Vector2(-110f, 0f);
                qtyRect.offsetMax = new Vector2(-15f, 0f);

                Text qtyTxt = qtyObj.AddComponent<Text>();
                qtyTxt.font = font;
                qtyTxt.fontSize = 22;
                qtyTxt.fontStyle = FontStyle.Bold;
                qtyTxt.alignment = TextAnchor.MiddleRight;
                qtyTxt.color = new Color(0.3f, 0.95f, 0.95f);

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

            inventoryPanelObj = new GameObject("InventoryPanel", typeof(RectTransform));
            inventoryPanelObj.transform.SetParent(transform, false);

            RectTransform panelRect = inventoryPanelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(840f, 680f);

            Image panelBg = inventoryPanelObj.AddComponent<Image>();
            panelBg.color = new Color(0.04f, 0.07f, 0.12f, 0.97f);

            GameObject headerObj = new GameObject("HeaderBar", typeof(RectTransform));
            headerObj.transform.SetParent(inventoryPanelObj.transform, false);
            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0, 70f);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.08f, 0.13f, 0.22f, 0.98f);

            GameObject titleObj = new GameObject("TitleText", typeof(RectTransform));
            titleObj.transform.SetParent(headerObj.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(25f, 0);
            titleRect.offsetMax = new Vector2(-25f, 0);

            Text titleTxt = titleObj.AddComponent<Text>();
            titleTxt.font = defaultFont;
            titleTxt.fontSize = 28;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.color = new Color(0.3f, 0.9f, 1.0f);
            titleTxt.text = "PLAYER INVENTORY (Press 'I' to Close)";

            GameObject scrollObj = new GameObject("ScrollView", typeof(RectTransform));
            scrollObj.transform.SetParent(inventoryPanelObj.transform, false);
            RectTransform scrollRect = scrollObj.GetComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(20f, 20f);
            scrollRect.offsetMax = new Vector2(-20f, -85f);

            ScrollRect scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;

            GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform));
            viewportObj.transform.SetParent(scrollObj.transform, false);
            RectTransform viewportRect = viewportObj.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;

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

            GameObject woodBadge = new GameObject("Badge", typeof(RectTransform));
            woodBadge.transform.SetParent(woodSlotObj.transform, false);
            RectTransform wBadgeRect = woodBadge.GetComponent<RectTransform>();
            wBadgeRect.anchorMin = new Vector2(0f, 0f);
            wBadgeRect.anchorMax = new Vector2(0f, 1f);
            wBadgeRect.offsetMin = new Vector2(10f, 8f);
            wBadgeRect.offsetMax = new Vector2(100f, -8f);

            Image wBadgeBg = woodBadge.AddComponent<Image>();
            wBadgeBg.color = new Color(0.55f, 0.40f, 0.15f, 0.95f);

            GameObject wBadgeTxtObj = new GameObject("BadgeText", typeof(RectTransform));
            wBadgeTxtObj.transform.SetParent(woodBadge.transform, false);
            RectTransform wBTxtRect = wBadgeTxtObj.GetComponent<RectTransform>();
            wBTxtRect.anchorMin = Vector2.zero;
            wBTxtRect.anchorMax = Vector2.one;

            Text wBTxt = wBadgeTxtObj.AddComponent<Text>();
            wBTxt.font = defaultFont;
            wBTxt.fontSize = 17;
            wBTxt.fontStyle = FontStyle.Bold;
            wBTxt.alignment = TextAnchor.MiddleCenter;
            wBTxt.color = new Color(1.0f, 0.92f, 0.55f);
            wBTxt.text = "WOOD";

            GameObject wNameObj = new GameObject("NameText", typeof(RectTransform));
            wNameObj.transform.SetParent(woodSlotObj.transform, false);
            RectTransform wNameRect = wNameObj.GetComponent<RectTransform>();
            wNameRect.anchorMin = Vector2.zero;
            wNameRect.anchorMax = Vector2.one;
            wNameRect.offsetMin = new Vector2(115f, 0f);
            wNameRect.offsetMax = new Vector2(-120f, 0f);

            Text woodNameText = wNameObj.AddComponent<Text>();
            woodNameText.font = defaultFont;
            woodNameText.fontSize = 20;
            woodNameText.fontStyle = FontStyle.Bold;
            woodNameText.alignment = TextAnchor.MiddleLeft;
            woodNameText.color = new Color(0.98f, 0.88f, 0.45f);
            woodNameText.text = "Wood Logs";

            GameObject wQtyObj = new GameObject("QtyText", typeof(RectTransform));
            wQtyObj.transform.SetParent(woodSlotObj.transform, false);
            RectTransform wQtyRect = wQtyObj.GetComponent<RectTransform>();
            wQtyRect.anchorMin = new Vector2(1f, 0f);
            wQtyRect.anchorMax = new Vector2(1f, 1f);
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

            Text emptyTxt = emptyPlaceholderObj.AddComponent<Text>();
            emptyTxt.font = defaultFont;
            emptyTxt.fontSize = 22;
            emptyTxt.alignment = TextAnchor.MiddleCenter;
            emptyTxt.color = new Color(0.6f, 0.68f, 0.78f);
            emptyTxt.text = "Inventory is empty!\nHarvest trees or pick up pebbles in the steppe.";
        }
    }
}
