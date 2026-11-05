using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Managers;
using BattleOfAges.Net;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The panel of a building chosen with a click on the map, where the units' panel stands: what it is, its health, who works there
    /// (one more, one less), and what can be done with it: open it, raise it, pull it down, give up a site.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SelectedBuildingUI : MonoBehaviour
    {
        public static SelectedBuildingUI Instance { get; private set; }

        private const float RefreshEvery = 0.2f, ConfirmSeconds = 3f;

        private GameObject panel;
        private Text title, status, hpText, handsText, demolishText, actionText;
        private RectTransform hpFill, handsGroup;
        private Button minus, plus, demolish, action;
        private BuildingNode shown;
        private float refresh, confirmUntil;

        /// <summary>Whether it is showing, and its lines (tests).</summary>
        public bool Showing => panel != null && panel.activeSelf;
        public string TitleText => title != null ? title.text : "";
        public string HandsText => handsText != null ? handsText.text : "";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Build();
            panel.SetActive(false);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            RtsUnitCommander commander = GameContext.Commander;
            BuildingNode node = commander != null ? commander.SelectedBuilding : null;
            bool show = node != null && GameContext.MapOpen;
            if (panel.activeSelf != show) panel.SetActive(show);
            if (!show) { shown = null; return; }
            if (node != shown) { shown = node; confirmUntil = 0f; refresh = 0f; }
            refresh -= Time.unscaledDeltaTime;
            if (refresh <= 0f) Refresh();
        }

        // ------------------------------------------------------------------ what it shows

        private static TownHall HallOf(BuildingNode node) => TownHall.Nearest(node.transform.position);

        private void Refresh()
        {
            refresh = RefreshEvery;
            BuildingDefinition def = BuildingData.GetDefinition(shown.Type);
            ConstructionSite site = shown.GetComponent<ConstructionSite>();
            TownHall hall = shown.GetComponent<TownHall>();

            title.text = hall != null ? $"{hall.Title}: ратуша {ItemTypeNames.Roman(hall.Level)}" : def.DisplayName;

            HealthComponent hp = shown.GetComponent<HealthComponent>();
            float f = hp != null && hp.MaxHealth > 0 ? Mathf.Clamp01(hp.CurrentHealth / (float)hp.MaxHealth) : 1f;
            hpFill.anchorMax = new Vector2(f, 1f);
            hpFill.GetComponent<Image>().color = Color.Lerp(UiTheme.Bad, UiTheme.Good, f);
            hpText.text = hp != null ? $"{hp.CurrentHealth} / {hp.MaxHealth}" : "";

            status.text = StatusOf(shown, def, site, hall);

            bool people = SettlementLabor.HandsAt(shown.gameObject, out int hands, out int places);
            if (handsGroup.gameObject.activeSelf != people) handsGroup.gameObject.SetActive(people);
            if (people)
            {
                handsText.text = $"Людей: {hands} / {places}";
                minus.interactable = hands > 0;
                plus.interactable = hands < places;
            }

            bool hasAction = ActionOf(hall, site, out string label, out bool can);
            if (action.gameObject.activeSelf != hasAction) action.gameObject.SetActive(hasAction);
            if (hasAction)
            {
                actionText.text = label;
                action.interactable = can;
            }

            bool canGo = BuildingDemolition.CanDemolish(shown, out _);
            demolish.interactable = canGo;
            bool confirming = Time.unscaledTime < confirmUntil;
            demolishText.text = confirming ? "Точно?" : site != null ? "Скасувати" : "Знести";
        }

        private static string StatusOf(BuildingNode node, BuildingDefinition def, ConstructionSite site, TownHall hall)
        {
            if (site != null) return $"Будується: {Mathf.FloorToInt(site.Progress * 100f)}%, працюють {site.PresentBuilders}";
            if (hall != null) return $"Населення {hall.People} із {hall.Cap}";
            if (node.TryGetComponent(out MineNode mine)) return $"Руда {mine.AccumulatedOreCount} / {mine.MaxAccumulatedOre}, {mine.OrePerSecond:0.#} за секунду";
            if (node.TryGetComponent(out WorkPlace place)) return place.Present > 0 ? $"Працюють {place.Present}" : "Без людей стоїть";
            if (node.TryGetComponent(out WorkbenchNode bench)) return bench.Queue.HasWork ? "Кує замовлене" : "Замовлень немає";
            if (node.GetComponent<WallSegment>() != null) return $"Стіни {ItemTypeNames.Roman(Fortifications.Level)}, шкода зі стін +{Mathf.RoundToInt(Fortifications.DamageBonus * 100f)}%";
            return def.Description;
        }

        /// <summary>The building's own button: open a bench or a market, raise a hall or the walls.</summary>
        private bool ActionOf(TownHall hall, ConstructionSite site, out string label, out bool can)
        {
            label = "";
            can = true;
            if (site != null) return false;
            if (hall != null)
            {
                if (hall.Level >= TownHall.MaxLevel) return false;
                TownHall.UpgradeCost(hall.Level + 1, out int wood, out int ore, out int tier);
                label = $"До рівня {ItemTypeNames.Roman(hall.Level + 1)}\n<size=12>{wood} дер., {ore} руди {ItemTypeNames.Roman(tier)}+</size>";
                can = hall.CanUpgrade(out _);
                return true;
            }
            if (shown.GetComponent<WorkbenchNode>() != null) { label = "Відкрити"; return true; }
            if (shown.TryGetComponent(out MarketNode market) && market.enabled) { label = "Торгівля"; return true; }
            if (shown.GetComponent<WallSegment>() != null && Fortifications.Level < Fortifications.MaxLevel)
            {
                Fortifications.UpgradeCost(Fortifications.Level + 1, out int wood, out int ore, out int tier);
                label = $"Стіни до {ItemTypeNames.Roman(Fortifications.Level + 1)}\n<size=12>{wood} дер., {ore} руди {ItemTypeNames.Roman(tier)}+</size>";
                can = Fortifications.CanUpgrade(out _);
                return true;
            }
            return false;
        }

        private void DoAction()
        {
            if (shown == null) return;
            TownHall hall = shown.GetComponent<TownHall>();
            if (hall != null) CommandBus.Submit(new UpgradeHallCommand { Hall = WorldEntity.IdOf(hall).Value });
            else if (shown.TryGetComponent(out WorkbenchNode bench)) WorkbenchUI.EnsureInstance().OpenUI(bench);
            else if (shown.GetComponent<MarketNode>() != null) TradeUI.Open();
            else if (shown.GetComponent<WallSegment>() != null) CommandBus.Submit(new UpgradeWallsCommand());
            refresh = 0f;
        }

        private void Hands(LaborCommand.Kind kind)
        {
            if (shown == null) return;
            SettlementLabor.DistrictOf(shown.gameObject, out LaborJob job);
            CommandBus.Submit(new LaborCommand { Order = kind, Job = job, Hall = WorldEntity.IdOf(HallOf(shown)).Value, Building = WorldEntity.IdOf(shown).Value });
            refresh = 0f;
        }

        /// <summary>The first press asks, the second does (a building is not pulled down by a slip of the mouse).</summary>
        public void PressDemolish()
        {
            if (shown == null) return;
            if (Time.unscaledTime >= confirmUntil)
            {
                confirmUntil = Time.unscaledTime + ConfirmSeconds;
                refresh = 0f;
                return;
            }
            confirmUntil = 0f;
            CommandBus.Submit(new DemolishCommand { Building = WorldEntity.IdOf(shown).Value });
        }

        private string DemolishTip()
        {
            if (shown == null) return null;
            if (!BuildingDemolition.CanDemolish(shown, out string why)) return TipText.Title("Знести", UiTheme.Gold) + "\n" + TipText.Bad(why);
            BuildingDemolition.Refund(shown, out int wood, out int ore, out _);
            bool site = shown.GetComponent<ConstructionSite>() != null;
            return TipText.Title(site ? "Скасувати будівництво" : "Знести", UiTheme.Gold) + $"\nПовернеться {wood} дерева й {ore} руди. Люди повернуться до роботи." + "\n" + TipText.Dim("Натисніть двічі.");
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            gameObject.AddComponent<UiThemed>();

            RectTransform rect = UiTheme.Rect(transform, "SelectedBuildingPanel", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1120f, 136f));
            panel = rect.gameObject;
            UiTheme.PanelImage(rect);
            PopIn.On(rect, sound: false);

            RectTransform head = UiTheme.Rect(rect, "Title", new Vector2(0f, 1f), new Vector2(22f, -8f), new Vector2(520f, 34f));
            title = UiTheme.Label(head, "Text", "", 22, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);

            RectTransform bar = UiTheme.Rect(rect, "HpBar", new Vector2(0f, 1f), new Vector2(22f, -46f), new Vector2(340f, 22f));
            UiTheme.PanelImage(bar, UiTheme.SlotSprite).raycastTarget = false;
            hpFill = UiTheme.Child(bar, "Fill", Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            UiTheme.PanelImage(hpFill, UiTheme.BarSprite).raycastTarget = false;
            hpText = UiTheme.Label(bar, "Value", "", 15, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);

            RectTransform st = UiTheme.Rect(rect, "Status", new Vector2(0f, 1f), new Vector2(22f, -76f), new Vector2(520f, 44f));
            status = UiTheme.Label(st, "Text", "", 16, TextAnchor.UpperLeft, UiTheme.InkDim);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;

            handsGroup = UiTheme.Rect(rect, "Hands", new Vector2(1f, 0.5f), new Vector2(-340f, -4f), new Vector2(236f, 88f));
            RectTransform handsLabel = UiTheme.Rect(handsGroup, "Label", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(236f, 30f));
            handsText = UiTheme.Label(handsLabel, "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            minus = UiTheme.TextButton(handsGroup, "Minus", "−", new Vector2(0.5f, 0f), new Vector2(-58f, 4f), new Vector2(104f, 46f), () => Hands(LaborCommand.Kind.Remove), 24);
            plus = UiTheme.TextButton(handsGroup, "Plus", "+", new Vector2(0.5f, 0f), new Vector2(58f, 4f), new Vector2(104f, 46f), () => Hands(LaborCommand.Kind.Add), 24);
            Tooltip.Set(minus.gameObject, () => TipText.Title("Одним менше", UiTheme.Gold) + "\nЛюдина повертається до роботи самостійно.");
            Tooltip.Set(plus.gameObject, () => TipText.Title("Одним більше", UiTheme.Gold) + "\nПрийде найближча вільна людина.");

            action = UiTheme.TextButton(rect, "Action", "", new Vector2(1f, 0.5f), new Vector2(-178f, -4f), new Vector2(152f, 88f), DoAction, 16);
            actionText = action.GetComponentInChildren<Text>();
            actionText.supportRichText = true;
            actionText.horizontalOverflow = HorizontalWrapMode.Wrap;

            demolish = UiTheme.TextButton(rect, "Demolish", "Знести", new Vector2(1f, 0.5f), new Vector2(-18f, -4f), new Vector2(152f, 88f), PressDemolish, 18);
            demolishText = demolish.GetComponentInChildren<Text>();
            Tooltip.Set(demolish.gameObject, DemolishTip);

            RectTransform close = UiTheme.Rect(rect, "Deselect", new Vector2(1f, 1f), new Vector2(14f, 14f), new Vector2(38f, 38f));
            UiTheme.PanelImage(close, UiTheme.ButtonSprite, new Color(1f, 0.72f, 0.68f));
            UiTheme.AddButton(close.gameObject).onClick.AddListener(() => GameContext.Commander?.SelectBuilding(null));
            UiTheme.Label(close, "Text", "×", 26, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            Tooltip.Set(close.gameObject, "Зняти виділення  " + TipText.Key("Esc"));
        }
    }
}
