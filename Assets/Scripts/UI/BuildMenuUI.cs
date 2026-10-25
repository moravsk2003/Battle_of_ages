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
                if (Time.frameCount % 15 == 0) RefreshRows();
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
            RefreshRows();
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
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }
            if (gameObject.GetComponent<UiThemed>() == null) gameObject.AddComponent<UiThemed>();

            BuildingDefinition[] defs = BuildingData.GetAllDefinitions();
            const float rowH = 74f, gap = 6f;
            float height = 58f + defs.Length * (rowH + gap) + 44f;
            RectTransform panelRect = UiTheme.Rect(transform, "BuildMenuPanel", new Vector2(0.5f, 0f), new Vector2(0f, 160f), new Vector2(640f, height)); // above the unit command panel
            menuPanelObj = panelRect.gameObject;
            UiTheme.PanelImage(panelRect);

            titleText = UiTheme.Label(UiTheme.Child(panelRect, "Header", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(22f, -52f), new Vector2(-22f, -6f)), "Title", "", 24, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            UpdateHeaderText();
            UiTheme.Divider(panelRect, -54f, 600f);

            statusText = UiTheme.Label(UiTheme.Child(panelRect, "Status", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(16f, 8f), new Vector2(-16f, 42f)), "StatusText", "Оберіть споруду; ЛКМ ставить, ПКМ скасовує", 16, TextAnchor.MiddleCenter, UiTheme.InkDim);

            for (int i = 0; i < defs.Length; i++)
            {
                RectTransform row = UiTheme.Child(panelRect, $"Btn_{defs[i].Type}", new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(16f, -58f - (i + 1) * (rowH + gap) + gap), new Vector2(-16f, -58f - i * (rowH + gap)));
                rows[i] = CreateBuildingButton(row, defs[i]);
            }

            if (menuPanelObj != null) menuPanelObj.SetActive(false);
        }

        private void UpdateHeaderText()
        {
            if (titleText != null)
            {
                KeyCode key = KeybindsManager.GetKey(KeyAction.BuildMenu);
                string keyName = KeybindsManager.GetKeyDisplayString(key);
                titleText.text = $"Будівництво  <size=17><color=#B8A88E>[{keyName}]</color></size>";
            }
        }

        private struct Row { public BuildingType Type; public Button Button; public Text Cost, Description; public CanvasGroup Group; }
        private readonly Row[] rows = new Row[BuildingData.GetAllDefinitions().Length];

        private Row CreateBuildingButton(RectTransform rect, BuildingDefinition def)
        {
            UiTheme.PanelImage(rect, UiTheme.ButtonSprite);
            Button btn = rect.gameObject.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.2f, 1.12f, 0.95f); cb.pressedColor = new Color(0.78f, 0.72f, 0.64f);
            cb.disabledColor = new Color(0.7f, 0.7f, 0.7f, 1f); cb.colorMultiplier = 1.25f;
            btn.colors = cb;
            BuildingType capturedType = def.Type;
            btn.onClick.AddListener(() => OnBuildButtonClicked(capturedType));

            RectTransform iconRect = UiTheme.Child(rect, "Icon", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(9f, 9f), new Vector2(65f, -9f));
            UiTheme.PanelImage(iconRect, UiTheme.SlotSprite).raycastTarget = false;
            UiTheme.Icon icon = def.Type == BuildingType.Mine ? UiTheme.Icon.Ore
                              : def.Type == BuildingType.StorageChest ? UiTheme.Icon.Wood
                              : def.Type == BuildingType.Workbench ? UiTheme.Icon.Hammer
                              : WallSegment.IsWall(def.Type) ? UiTheme.Icon.Wall
                              : def.Type == BuildingType.TownHall ? UiTheme.Icon.House : UiTheme.Icon.Worker;
            UiTheme.IconImage(iconRect, icon, new Vector2(0.5f, 0.5f), Vector2.zero, 42f);

            UiTheme.Label(UiTheme.Child(rect, "Name", new Vector2(0f, 0.5f), new Vector2(0.55f, 1f), new Vector2(78f, 0f), new Vector2(0f, -4f)), "NameText", def.DisplayName, 19, TextAnchor.MiddleLeft, UiTheme.Ink, FontStyle.Bold);
            Text cost = UiTheme.Label(UiTheme.Child(rect, "Cost", new Vector2(0.55f, 0.5f), new Vector2(1f, 1f), Vector2.zero, new Vector2(-14f, -4f)), "CostText", "", 16, TextAnchor.MiddleRight, UiTheme.Gold);
            Text desc = UiTheme.Label(UiTheme.Child(rect, "Description", new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(78f, 5f), new Vector2(-14f, 0f)), "DescriptionText", def.Description, 15, TextAnchor.MiddleLeft, UiTheme.InkDim);
            desc.horizontalOverflow = HorizontalWrapMode.Wrap;
            desc.resizeTextForBestFit = true;
            desc.resizeTextMinSize = 12;
            desc.resizeTextMaxSize = 15;

            Tooltip.Set(rect.gameObject, () => BuildTip(capturedType));
            return new Row { Type = def.Type, Button = btn, Cost = cost, Description = desc, Group = rect.gameObject.AddComponent<CanvasGroup>() };
        }

        /// <summary>What the building is, costs against the stock, strength, and the placing keys.</summary>
        private static string BuildTip(BuildingType type)
        {
            BuildingDefinition def = BuildingData.GetDefinition(type);
            ResourceManager rm = ResourceManager.Instance;
            bool wall = WallSegment.IsWall(type);
            int wood = rm != null ? rm.WoodCount : 0;
            int ore = rm == null ? 0 : wall ? rm.GetOreCountAtLeast(0) : rm.GetOreTierCount(def.OreTierRequired);
            string Have(int have, int need) => have >= need ? TipText.Good($"є {have}") : TipText.Bad($"є {have}");
            var sb = new System.Text.StringBuilder(TipText.Title(def.DisplayName, UiTheme.Gold));
            sb.Append("\n").Append(def.Description);
            sb.Append($"\nДеревина {def.WoodCost}  ({Have(wood, def.WoodCost)})");
            sb.Append(wall ? $"\nРуда {def.OreCost} одного рівня  ({Have(ore, def.OreCost)} усього)" : $"\nРуда рів. {def.OreTierRequired}: {def.OreCost}  ({Have(ore, def.OreCost)})");
            sb.Append(wall ? $"\nМіцність {def.MaxHealth} на руді рів. 0, +{Mathf.RoundToInt(Combat.GameBalance.WallHealthPerOreTier * 100f)}% за кожен рівень руди" : $"\nМіцність {def.MaxHealth}");
            if (def.NpcSpawnCount > 0) sb.Append($"\nДає {def.NpcSpawnCount} робітника (потрібне місце в населенні)");
            sb.Append("\n").Append(TipText.Dim(wall ? "Z / X — повернути, Tab — руда, ЛКМ — поставити, ПКМ — скасувати" : "Z / X — повернути, ЛКМ — поставити, ПКМ — скасувати"));
            return sb.ToString();
        }

        /// <summary>Costs in red and the row dimmed when the stock is short; a building not allowed (a second hall) says why.</summary>
        private void RefreshRows()
        {
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            foreach (Row r in rows)
            {
                if (r.Button == null) continue;
                BuildingDefinition def = BuildingData.GetDefinition(r.Type);
                bool affordable = placer == null || placer.CanAfford(r.Type);
                bool allowed = BuildingPlacementSystem.IsAllowed(r.Type, out string reason);
                string ore = $"руда {def.OreCost} <size=13>({(WallSegment.IsWall(r.Type) ? "рівень — Tab" : "рів. " + def.OreTierRequired)})</size>";
                r.Cost.text = $"дерево {def.WoodCost}  ·  {ore}";
                r.Cost.color = affordable ? UiTheme.Gold : UiTheme.Bad;
                r.Description.text = allowed ? def.Description : UiTheme.Sanitize(reason);
                r.Description.color = allowed ? UiTheme.InkDim : UiTheme.Bad;
                r.Button.interactable = allowed;
                r.Group.alpha = affordable && allowed ? 1f : 0.7f;
            }
        }
    }
}
