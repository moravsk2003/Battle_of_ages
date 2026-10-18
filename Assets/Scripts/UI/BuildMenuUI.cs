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

            BuildUI();
            KeybindsManager.OnKeybindsChanged += UpdateHeaderText;
        }

        private void OnDestroy()
        {
            KeybindsManager.OnKeybindsChanged -= UpdateHeaderText;
        }

        private void Start()
        {
            gameModeManager = FindFirstObjectByType<GameModeManager>();
            CloseMenu();
        }

        private void Update()
        {
            if (Time.frameCount < 2 || Time.time < 0.5f) return;

            // Only show in RTS mode
            if (gameModeManager != null && gameModeManager.CurrentMode != GameMode.RtsMap)
            {
                if (IsOpen) CloseMenu();
                return;
            }

            if (GameWindows.Paused) return;

            if (IsOpen)
            {
                if (UniversalInput.GetKeyDownEscape() || IsToggleKeyPressed())
                {
                    CloseMenu();
                    return;
                }
            }
            else
            {
                if (GameWindows.WorkersOpen || GameWindows.InventoryOpen) return;

                if (IsToggleKeyPressed())
                {
                    OpenMenu();
                }
            }
        }

        private bool IsToggleKeyPressed()
        {
            return KeybindsManager.GetKeyDown(KeyAction.BuildMenu);
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
            UpdateHeaderText();
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

            RectTransform panelRect = UiTheme.Child(transform, "BuildMenuPanel", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            menuPanelObj = panelRect.gameObject;
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 160f); // above the unit command panel
            panelRect.sizeDelta = new Vector2(580f, 590f); // six buildings

            Image panelBg = menuPanelObj.AddComponent<Image>();
            panelBg.color = new Color(0.04f, 0.07f, 0.14f, 0.96f);

            RectTransform headerRect = UiTheme.Child(menuPanelObj.transform, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f));
            GameObject headerObj = headerRect.gameObject;
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 50f);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.08f, 0.14f, 0.25f, 0.98f);

            RectTransform titleRect = UiTheme.Child(headerObj.transform, "Title", Vector2.zero, Vector2.one, offsetMin: new Vector2(15f, 0f));
            GameObject titleObj = titleRect.gameObject;

            titleText = titleObj.AddComponent<Text>();
            titleText.font = defaultFont;
            titleText.fontSize = 22;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleLeft;
            titleText.color = new Color(0.3f, 0.95f, 1.0f);
            UpdateHeaderText();

            RectTransform statusRect = UiTheme.Child(menuPanelObj.transform, "StatusText", new Vector2(0f, 0f), new Vector2(1f, 0f));
            GameObject statusObj = statusRect.gameObject;
            statusRect.pivot = new Vector2(0.5f, 0f);
            statusRect.sizeDelta = new Vector2(0f, 35f);
            statusRect.anchoredPosition = new Vector2(0f, 5f);

            statusText = statusObj.AddComponent<Text>();
            statusText.font = defaultFont;
            statusText.fontSize = 16;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = new Color(0.7f, 0.8f, 0.9f);
            statusText.text = "Оберіть споруду для будівництва";

            RectTransform containerRect = UiTheme.Child(menuPanelObj.transform, "ButtonsContainer", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(15f, 45f), new Vector2(-15f, -55f));
            GameObject btnContainer = containerRect.gameObject;

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

            if (menuPanelObj != null) menuPanelObj.SetActive(false);
        }

        private void UpdateHeaderText()
        {
            if (titleText != null)
            {
                KeyCode key = KeybindsManager.GetKey(KeyAction.BuildMenu);
                string keyName = KeybindsManager.GetKeyDisplayString(key);
                titleText.text = $"🏗 БУДІВНИЦТВО (Клавіша '{keyName}')";
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

            RectTransform iconRect = UiTheme.Child(btnObj.transform, "Icon", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 8f), new Vector2(62f, -8f));
            GameObject iconObj = iconRect.gameObject;

            Image iconBg = iconObj.AddComponent<Image>();
            iconBg.color = new Color(0.6f, 0.15f, 0.10f, 0.9f);

            RectTransform iconTxtRect = UiTheme.Child(iconObj.transform, "IconText", Vector2.zero, Vector2.one);
            GameObject iconTxt = iconTxtRect.gameObject;

            Text icoTxt = UiTheme.PlainText(iconTxt, font, 26, TextAnchor.MiddleCenter, Color.white);
            icoTxt.text = ""; // the font has no emoji: a themed icon instead
            UiTheme.Icon icon = def.Type == BuildingType.Mine ? UiTheme.Icon.Ore
                              : def.Type == BuildingType.StorageChest ? UiTheme.Icon.Wood
                              : def.Type == BuildingType.Workbench ? UiTheme.Icon.Hammer
                              : def.Type == BuildingType.CastleWall ? UiTheme.Icon.Army
                              : def.Type == BuildingType.TownHall ? UiTheme.Icon.House : UiTheme.Icon.Worker;
            UiTheme.IconImage(iconObj.transform, icon, new Vector2(0.5f, 0.5f), Vector2.zero, 42f);

            RectTransform nameRect = UiTheme.Child(btnObj.transform, "NameText", new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(72f, 0f), new Vector2(-10f, -4f));
            GameObject nameObj = nameRect.gameObject;

            Text nameTxt = UiTheme.PlainText(nameObj, font, 19, TextAnchor.MiddleLeft, Color.white, def.DisplayName, style: FontStyle.Bold);

            RectTransform costRect = UiTheme.Child(btnObj.transform, "CostText", new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(72f, 4f), new Vector2(-10f, 0f));
            GameObject costObj = costRect.gameObject;

            Text costTxt = UiTheme.PlainText(costObj, font, 16, TextAnchor.MiddleLeft, new Color(0.85f, 0.80f, 0.55f));

            string shortTierName = def.OreTierRequired == 0 ? "Руда рів. 0" : $"Руда рів. {def.OreTierRequired}";
            costTxt.text = $"Ціна: 🪵 {def.WoodCost} Дерево  +  🪨 {def.OreCost} {shortTierName}  |  {def.Description}";
        }
    }
}
