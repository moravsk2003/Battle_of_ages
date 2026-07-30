using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.Building;
using BattleOfAges.InputSystem;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Build menu toolbar shown in RTS map mode. Toggle with 'B' key.
    /// Displays available buildings with costs and handles placement initiation.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildMenuUI : MonoBehaviour
    {
        public static BuildMenuUI Instance { get; private set; }

        private GameObject menuPanelObj;
        private Text statusText;
        private GameModeManager gameModeManager;

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

            BuildUI();
        }

        private void Start()
        {
            gameModeManager = FindFirstObjectByType<GameModeManager>();
            CloseMenu();
        }

        private void Update()
        {
            // Only show in RTS mode
            if (gameModeManager != null && gameModeManager.CurrentMode != GameMode.RtsMap)
            {
                if (IsOpen) CloseMenu();
                return;
            }

            if (UniversalInput.GetKeyDownEscape() && IsOpen)
            {
                CloseMenu();
                return;
            }

            if (IsToggleKeyPressed())
            {
                ToggleMenu();
            }
        }

        private bool IsToggleKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame) return true;
#endif
            try { return Input.GetKeyDown(KeyCode.B); } catch { return false; }
        }

        public void ToggleMenu()
        {
            if (IsOpen) CloseMenu();
            else OpenMenu();
        }

        public void OpenMenu()
        {
            IsOpen = true;
            if (menuPanelObj != null) menuPanelObj.SetActive(true);
        }

        public void CloseMenu()
        {
            IsOpen = false;
            if (menuPanelObj != null) menuPanelObj.SetActive(false);
        }

        private void OnBuildButtonClicked(BuildingType type)
        {
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            if (placer == null)
            {
                placer = FindFirstObjectByType<BuildingPlacementSystem>();
            }

            if (placer != null)
            {
                placer.StartPlacement(type);

                BuildingDefinition def = BuildingData.GetDefinition(type);
                if (statusText != null)
                {
                    statusText.text = $"Розміщуємо: {def.DisplayName}\nЛКМ — побудувати  |  ПКМ — скасувати";
                }
            }

            CloseMenu();
        }

        private void BuildUI()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 92;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Font defaultFont = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            menuPanelObj = new GameObject("BuildMenuPanel", typeof(RectTransform));
            menuPanelObj.transform.SetParent(transform, false);

            RectTransform panelRect = menuPanelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 20f);
            panelRect.sizeDelta = new Vector2(580f, 380f);

            Image panelBg = menuPanelObj.AddComponent<Image>();
            panelBg.color = new Color(0.04f, 0.07f, 0.14f, 0.96f);

            GameObject headerObj = new GameObject("Header", typeof(RectTransform));
            headerObj.transform.SetParent(menuPanelObj.transform, false);
            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 50f);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.08f, 0.14f, 0.25f, 0.98f);

            GameObject titleObj = new GameObject("Title", typeof(RectTransform));
            titleObj.transform.SetParent(headerObj.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(15f, 0f);

            Text titleTxt = titleObj.AddComponent<Text>();
            titleTxt.font = defaultFont;
            titleTxt.fontSize = 22;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.color = new Color(0.3f, 0.95f, 1.0f);
            titleTxt.text = "🏗 БУДІВНИЦТВО (Клавіша 'B')";

            GameObject statusObj = new GameObject("StatusText", typeof(RectTransform));
            statusObj.transform.SetParent(menuPanelObj.transform, false);
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(1f, 0f);
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.sizeDelta = new Vector2(0f, 35f);
            statusRect.anchoredPosition = new Vector2(0f, 5f);

            statusText = statusObj.AddComponent<Text>();
            statusText.font = defaultFont;
            statusText.fontSize = 16;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = new Color(0.7f, 0.8f, 0.9f);
            statusText.text = "Оберіть споруду для будівництва";

            GameObject btnContainer = new GameObject("ButtonsContainer", typeof(RectTransform));
            btnContainer.transform.SetParent(menuPanelObj.transform, false);
            RectTransform containerRect = btnContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0f, 0f);
            containerRect.anchorMax = new Vector2(1f, 1f);
            containerRect.offsetMin = new Vector2(15f, 45f);
            containerRect.offsetMax = new Vector2(-15f, -55f);

            VerticalLayoutGroup layout = btnContainer.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.padding = new RectOffset(0, 0, 5, 5);

            BuildingDefinition[] defs = BuildingData.GetAllDefinitions();
            for (int i = 0; i < defs.Length; i++)
            {
                BuildingDefinition def = defs[i];
                CreateBuildingButton(btnContainer.transform, defaultFont, def);
            }
        }

        private void CreateBuildingButton(Transform parent, Font font, BuildingDefinition def)
        {
            GameObject btnObj = new GameObject($"Btn_{def.Type}", typeof(RectTransform));
            btnObj.transform.SetParent(parent, false);

            RectTransform rect = btnObj.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(480f, 70f);

            Image bg = btnObj.AddComponent<Image>();
            bg.color = new Color(0.10f, 0.16f, 0.26f, 0.95f);

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.10f, 0.16f, 0.26f, 0.95f);
            cb.highlightedColor = new Color(0.15f, 0.30f, 0.50f, 1.0f);
            cb.pressedColor = new Color(0.10f, 0.45f, 0.75f, 1.0f);
            cb.disabledColor = new Color(0.15f, 0.15f, 0.15f, 0.7f);
            btn.colors = cb;

            BuildingType capturedType = def.Type;
            btn.onClick.AddListener(() => OnBuildButtonClicked(capturedType));

            GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
            iconObj.transform.SetParent(btnObj.transform, false);
            RectTransform iconRect = iconObj.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0f, 0f);
            iconRect.anchorMax = new Vector2(0f, 1f);
            iconRect.offsetMin = new Vector2(8f, 8f);
            iconRect.offsetMax = new Vector2(62f, -8f);

            Image iconBg = iconObj.AddComponent<Image>();
            iconBg.color = new Color(0.6f, 0.15f, 0.10f, 0.9f);

            GameObject iconTxt = new GameObject("IconText", typeof(RectTransform));
            iconTxt.transform.SetParent(iconObj.transform, false);
            RectTransform iconTxtRect = iconTxt.GetComponent<RectTransform>();
            iconTxtRect.anchorMin = Vector2.zero;
            iconTxtRect.anchorMax = Vector2.one;

            Text icoTxt = iconTxt.AddComponent<Text>();
            icoTxt.font = font;
            icoTxt.fontSize = 26;
            icoTxt.alignment = TextAnchor.MiddleCenter;
            icoTxt.color = Color.white;
            icoTxt.text = (def.Type == BuildingType.Mine) ? "⛏️" : ((def.Type == BuildingType.StorageChest) ? "📦" : "🏠");

            GameObject nameObj = new GameObject("NameText", typeof(RectTransform));
            nameObj.transform.SetParent(btnObj.transform, false);
            RectTransform nameRect = nameObj.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 0.5f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(72f, 0f);
            nameRect.offsetMax = new Vector2(-10f, -4f);

            Text nameTxt = nameObj.AddComponent<Text>();
            nameTxt.font = font;
            nameTxt.fontSize = 19;
            nameTxt.fontStyle = FontStyle.Bold;
            nameTxt.alignment = TextAnchor.MiddleLeft;
            nameTxt.color = Color.white;
            nameTxt.text = def.DisplayName;

            GameObject costObj = new GameObject("CostText", typeof(RectTransform));
            costObj.transform.SetParent(btnObj.transform, false);
            RectTransform costRect = costObj.GetComponent<RectTransform>();
            costRect.anchorMin = new Vector2(0f, 0f);
            costRect.anchorMax = new Vector2(1f, 0.5f);
            costRect.offsetMin = new Vector2(72f, 4f);
            costRect.offsetMax = new Vector2(-10f, 0f);

            Text costTxt = costObj.AddComponent<Text>();
            costTxt.font = font;
            costTxt.fontSize = 16;
            costTxt.alignment = TextAnchor.MiddleLeft;
            costTxt.color = new Color(0.85f, 0.80f, 0.55f);

            string shortTierName = def.OreTierRequired == 0 ? "Руда Lvl 0" : $"Руда Lvl {def.OreTierRequired}";
            costTxt.text = $"Ціна: 🪵 {def.WoodCost} Дерево  +  🪨 {def.OreCost} {shortTierName}  |  {def.Description}";
        }
    }
}
