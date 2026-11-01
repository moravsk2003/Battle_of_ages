using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.RTS;
using BattleOfAges.InputSystem;
using BattleOfAges.Crafting;
using BattleOfAges.Combat;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The command panel at the bottom of the map while units are selected. One unit: rank, health, damage, armour and
    /// what it is doing. Several: a card per unit (rank icon and a health bar; click — just this one, Shift+click —
    /// drop it from the selection). On the right the orders, each with a tooltip.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectedUnitUI : MonoBehaviour
    {
        public static SelectedUnitUI Instance { get; private set; }

        private const int CardColumns = 9, CardRows = 2;
        private const float CardSize = 44f, CardGap = 4f;

        private GameObject panelObj;
        private Text titleText, statusText, statsText, hpText;
        private RectTransform single, grid, hpFill;
        private Image portrait;
        private Text overflowText;
        private RtsUnitCommander commander;
        private readonly List<(RectTransform rt, Image icon, RectTransform fill, RtsUnit unit)> cards = new List<(RectTransform, Image, RectTransform, RtsUnit)>();
        private int shownHash;
        private Shown groupCount;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildUI();
        }

        private void Start()
        {
            commander = Managers.GameContext.Commander;
            if (panelObj != null) panelObj.SetActive(false);
        }

        private void Update()
        {
            if (commander == null)
            {
                commander = Managers.GameContext.Commander;
                if (commander == null) return;
            }

            var selected = commander.SelectedUnits;
            if (selected == null || selected.Count == 0)
            {
                if (panelObj != null && panelObj.activeSelf) panelObj.SetActive(false);
                shownHash = 0;
                return;
            }
            if (panelObj != null && !panelObj.activeSelf) panelObj.SetActive(true);
            UpdateDisplay(selected);
        }

        // ------------------------------------------------------------------ show

        private void UpdateDisplay(IReadOnlyList<RtsUnit> selected)
        {
            int count = selected.Count;
            bool one = count == 1 && selected[0] != null;
            single.gameObject.SetActive(one);
            grid.gameObject.SetActive(!one);
            if (one) ShowSingle(selected[0]);
            else ShowGroup(selected);
        }

        private RtsUnit singleUnit;
        private HealthComponent singleHp;
        private CombatUnit singleCombat;
        private NpcResourceGatherer singleGatherer;
        private Shown singleHead, singleHpShown, singleStats, singleJob;

        /// <summary>The one selected unit; its texts are made again only when what they say changes.</summary>
        private void ShowSingle(RtsUnit unit)
        {
            groupCount.Reset(); // the group view writes its title again next time
            if (unit != singleUnit)
            {
                singleUnit = unit;
                singleHp = unit.GetComponent<HealthComponent>();
                singleCombat = unit.GetComponent<CombatUnit>();
                singleGatherer = unit.GetComponent<NpcResourceGatherer>();
                singleHead.Reset(); singleHpShown.Reset(); singleStats.Reset(); singleJob.Reset();
            }
            UnitRank rank = RankOf(unit);
            if (singleHead.Changed(Shown.Key(unit.GetInstanceID(), (int)rank)))
            {
                titleText.text = DisplayName(unit);
                titleText.color = RankColor(rank, unit.IsHero);
                SetPortrait(portrait, unit, rank);
            }

            HealthComponent hp = singleHp;
            float f = hp != null && hp.MaxHealth > 0 ? Mathf.Clamp01(hp.CurrentHealth / (float)hp.MaxHealth) : 1f;
            hpFill.anchorMax = new Vector2(f, 1f);
            if (singleHpShown.Changed(hp != null ? Shown.Key(hp.CurrentHealth, hp.MaxHealth) : -1))
            {
                hpFill.GetComponent<Image>().color = HealthColor(f);
                hpText.text = hp != null ? $"{hp.CurrentHealth} / {hp.MaxHealth}" : "";
            }

            CombatUnit c = singleCombat;
            int armour = Mathf.RoundToInt((hp != null ? hp.DamageReduction : 0f) * 100f);
            if (singleStats.Changed(c != null ? Shown.Key(c.AttackDamage, Mathf.RoundToInt(c.AttackCooldown * 10f), armour) : -1))
                statsText.text = c != null
                    ? $"Шкода <color=#F5CC73>{c.AttackDamage}</color>  ·  удар раз на {c.AttackCooldown:0.0} с  ·  захист <color=#F5CC73>{armour}%</color>"
                    : "";

            NpcResourceGatherer gatherer = singleGatherer;
            int jobKind = unit.WallBound ? (unit.IsPostedOnWall ? 1 : 2) : unit.HasAttackTarget ? 3 : gatherer == null ? (unit.IsHero ? 4 : 5) : gatherer.IsIdle ? 6 : 7;
            if (singleJob.Changed(jobKind == 7 ? Shown.Key(jobKind, (int)gatherer.CurrentDirective, (int)gatherer.CurrentState) : jobKind))
                statusText.text = jobKind == 1 ? "На стіні, на посту" : jobKind == 2 ? "Іде на стіну" : jobKind == 3 ? "У бою"
                                : jobKind == 4 ? "Герой" : jobKind == 5 ? "Боєць" : jobKind == 6 ? "<color=#DB5242>Без діла</color>"
                                : $"{GetDirectiveName(gatherer.CurrentDirective)}  ·  {GetStateName(gatherer.CurrentState)}";
        }

        private void ShowGroup(IReadOnlyList<RtsUnit> selected)
        {
            int count = selected.Count;
            singleUnit = null; // the single view starts afresh next time
            if (groupCount.Changed(count))
            {
                titleText.text = $"Обрано: {count}";
                titleText.color = UiTheme.Gold;
            }

            int hash = count;
            foreach (RtsUnit u in selected) hash = hash * 31 + (u != null ? u.GetInstanceID() : 0);
            int shown = Mathf.Min(count, CardColumns * CardRows);
            if (hash != shownHash)
            {
                shownHash = hash;
                for (int i = 0; i < cards.Count; i++)
                {
                    bool on = i < shown && selected[i] != null;
                    cards[i].rt.gameObject.SetActive(on);
                    if (!on) continue;
                    RtsUnit u = selected[i];
                    cards[i] = (cards[i].rt, cards[i].icon, cards[i].fill, u);
                    SetPortrait(cards[i].icon, u, RankOf(u));
                    cards[i].rt.GetComponent<Image>().color = RankColor(RankOf(u), u.IsHero);
                }
                overflowText.text = count > shown ? $"+{count - shown}" : "";
            }
            for (int i = 0; i < shown && i < cards.Count; i++)
            {
                RtsUnit u = cards[i].unit;
                HealthComponent hp = u != null ? u.GetComponent<HealthComponent>() : null;
                float f = hp != null && hp.MaxHealth > 0 ? Mathf.Clamp01(hp.CurrentHealth / (float)hp.MaxHealth) : 1f;
                cards[i].fill.anchorMax = new Vector2(f, 1f);
                cards[i].fill.GetComponent<Image>().color = HealthColor(f);
            }
        }

        private void OnCardClicked(int index)
        {
            if (commander == null || index >= cards.Count) return;
            RtsUnit u = cards[index].unit;
            if (u == null) return;
            bool shift = UniversalInput.IsShiftHeld();
            if (shift) commander.Deselect(u);
            else commander.SelectOnly(u);
            shownHash = 0;
        }

        // ------------------------------------------------------------------ helpers

        private static UnitRank RankOf(RtsUnit u)
        {
            UnitProgress p = u != null ? u.GetComponent<UnitProgress>() : null;
            return p != null ? p.Rank : UnitRank.Person;
        }

        public static Color RankColor(UnitRank rank, bool hero) =>
            hero ? UiTheme.LevelColor(5) : rank == UnitRank.General ? UiTheme.LevelColor(4) : rank == UnitRank.Knight ? UiTheme.LevelColor(3)
            : rank == UnitRank.Warrior ? UiTheme.LevelColor(2) : UiTheme.LevelColor(1);

        private static Color HealthColor(float f) => f > 0.5f ? new Color(0.36f, 0.72f, 0.30f) : f > 0.25f ? new Color(0.86f, 0.66f, 0.22f) : new Color(0.82f, 0.22f, 0.18f);

        private static void SetPortrait(Image img, RtsUnit u, UnitRank rank)
        {
            if (u != null && u.IsHero) img.sprite = UiTheme.ItemSprite(ItemType.Sword, 5);
            else if (rank == UnitRank.General) img.sprite = UiTheme.ItemSprite(ItemType.Helmet, 5);
            else if (rank == UnitRank.Knight) img.sprite = UiTheme.ItemSprite(ItemType.Helmet, 3);
            else if (rank == UnitRank.Warrior) img.sprite = UiTheme.IconSprite(UiTheme.Icon.Army);
            else img.sprite = UiTheme.IconSprite(UiTheme.Icon.Worker);
        }

        public static string DisplayName(RtsUnit u)
        {
            if (u.IsHero) return "Герой";
            string n = u.gameObject.name;
            string rank = HeroUI.RankName(RankOf(u));
            rank = char.ToUpper(rank[0]) + rank.Substring(1);
            if (n.StartsWith("NPC_Villager_")) return (RankOf(u) == UnitRank.Person ? "Робітник" : rank) + " " + n.Substring("NPC_Villager_".Length);
            return rank;
        }

        /// <summary>The single unit's details for its tooltip: gear and how far it is from the next rank.</summary>
        private string SingleTip()
        {
            if (commander == null || commander.SelectedUnits.Count != 1 || commander.SelectedUnits[0] == null) return null;
            RtsUnit u = commander.SelectedUnits[0];
            var sb = new System.Text.StringBuilder(TipText.Title(DisplayName(u), RankColor(RankOf(u), u.IsHero)));
            sb.Append("\nРанг: ").Append(HeroUI.RankName(RankOf(u)));
            UnitProgress p = u.GetComponent<UnitProgress>();
            if (p != null && !p.IsHero && RankOf(u) != UnitRank.General)
                sb.Append($"\nБойовий досвід {p.BattleXp} / {UnitProgress.XpToPromote(RankOf(u))} до підвищення");
            Equipment gear = u.GetComponent<Equipment>();
            if (gear != null)
            {
                var parts = new List<string>();
                foreach (var slot in ItemTypeNames.Slots) if (!gear[slot].IsEmpty) parts.Add(gear[slot].ToString());
                sb.Append("\n").Append(TipText.Dim(parts.Count == 0 ? "Без спорядження" : string.Join(" · ", parts)));
            }
            return sb.ToString();
        }

        private void IssueDirectiveToSelected(GatherDirective directive)
        {
            if (commander == null) return;
            int n = 0;
            foreach (RtsUnit unit in commander.SelectedUnits)
                if (unit != null && unit.TryGetComponent(out NpcResourceGatherer gatherer)) { gatherer.SetGatherDirective(directive); n++; }
            if (n == 0) StrategyHud.Notify("Серед обраних немає робітників");
        }

        private void DeselectAll()
        {
            if (commander != null) commander.DeselectAll();
        }

        private static string GetDirectiveName(GatherDirective directive)
        {
            switch (directive)
            {
                case GatherDirective.WoodGatherBranchesOnly: return "Збір гілок";
                case GatherDirective.WoodChopTreesOnly: return "Рубання дерев";
                case GatherDirective.OrePebblesOnly: return "Збір камінчиків";
                case GatherDirective.HaulOreFromMineToChest: return "Шахта";
                case GatherDirective.Idle: return "Чекає наказу";
                default: return "Самостійно";
            }
        }

        private static string GetStateName(GathererState state)
        {
            switch (state)
            {
                case GathererState.FindingResource: return "шукає роботу";
                case GathererState.MovingToResource: return "іде до ресурсу";
                case GathererState.Harvesting: return "добуває";
                case GathererState.DeliveringToChest: return "несе в скриню";
                case GathererState.MovingToMine: return "іде до шахти";
                case GathererState.HaulingFromMine: return "несе руду з шахти";
                case GathererState.MovingToMineToWork: return "іде працювати в шахту";
                case GathererState.WorkingInsideMine: return "працює в шахті";
                case GathererState.WorkingAtBench: return "працює за верстаком";
                case GathererState.Building: return "будує";
                default: return "рухається";
            }
        }

        // ------------------------------------------------------------------ build

        private void BuildUI()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (gameObject.GetComponent<UiThemed>() == null) gameObject.AddComponent<UiThemed>();

            // Command panel: the selection on the left, the order buttons on the right
            RectTransform rect = UiTheme.Rect(transform, "SelectedUnitPanel", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1120f, 136f));
            panelObj = rect.gameObject;
            UiTheme.PanelImage(rect);
            PopIn.On(rect, sound: false);

            RectTransform head = UiTheme.Rect(rect, "Title", new Vector2(0f, 1f), new Vector2(22f, -8f), new Vector2(430f, 34f));
            titleText = UiTheme.Label(head, "Text", "", 22, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);

            // One unit
            single = UiTheme.Rect(rect, "Single", new Vector2(0f, 0f), new Vector2(22f, 12f), new Vector2(430f, 82f));
            single.gameObject.AddComponent<Image>().color = Color.clear;
            Tooltip.Set(single.gameObject, SingleTip);
            RectTransform pf = UiTheme.Rect(single, "Portrait", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(72f, 72f));
            UiTheme.PanelImage(pf, UiTheme.SlotSprite).raycastTarget = false;
            RectTransform pi = UiTheme.Rect(pf, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f));
            portrait = pi.gameObject.AddComponent<Image>();
            portrait.preserveAspect = true; portrait.raycastTarget = false;
            RectTransform bar = UiTheme.Rect(single, "HpBar", new Vector2(0f, 1f), new Vector2(84f, -2f), new Vector2(340f, 22f));
            UiTheme.PanelImage(bar, UiTheme.SlotSprite).raycastTarget = false;
            hpFill = UiTheme.Child(bar, "Fill", Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
            UiTheme.PanelImage(hpFill, UiTheme.BarSprite).raycastTarget = false;
            hpText = UiTheme.Label(bar, "Value", "", 15, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            RectTransform st = UiTheme.Rect(single, "Stats", new Vector2(0f, 1f), new Vector2(84f, -26f), new Vector2(346f, 26f));
            statsText = UiTheme.Label(st, "Text", "", 15, TextAnchor.MiddleLeft, UiTheme.Ink);
            RectTransform job = UiTheme.Rect(single, "Job", new Vector2(0f, 1f), new Vector2(84f, -52f), new Vector2(346f, 26f));
            statusText = UiTheme.Label(job, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.InkDim);

            // Several: cards
            grid = UiTheme.Rect(rect, "Cards", new Vector2(0f, 0f), new Vector2(22f, 12f), new Vector2(CardColumns * (CardSize + CardGap), CardRows * (CardSize + CardGap)));
            for (int i = 0; i < CardColumns * CardRows; i++)
            {
                int index = i;
                int col = i % CardColumns, row = i / CardColumns;
                RectTransform card = UiTheme.Rect(grid, "Card" + i, new Vector2(0f, 1f), new Vector2(col * (CardSize + CardGap), -row * (CardSize + CardGap)), new Vector2(CardSize, CardSize));
                UiTheme.PanelImage(card, UiTheme.SlotSprite);
                UiTheme.AddButton(card.gameObject).onClick.AddListener(() => OnCardClicked(index));
                Tooltip.Set(card.gameObject, () => index < cards.Count && cards[index].unit != null ? CardTip(cards[index].unit) : null);
                RectTransform ic = UiTheme.Rect(card, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(32f, 32f));
                Image icon = ic.gameObject.AddComponent<Image>();
                icon.preserveAspect = true; icon.raycastTarget = false;
                RectTransform hb = UiTheme.Rect(card, "Hp", new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(CardSize - 10f, 5f));
                hb.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
                hb.GetComponent<Image>().raycastTarget = false;
                RectTransform fill = UiTheme.Child(hb, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                Image fi = fill.gameObject.AddComponent<Image>();
                fi.raycastTarget = false;
                cards.Add((card, icon, fill, null));
                card.gameObject.SetActive(false);
            }
            RectTransform more = UiTheme.Rect(rect, "More", new Vector2(0f, 0f), new Vector2(22f + CardColumns * (CardSize + CardGap) + 4f, 12f), new Vector2(50f, 40f));
            overflowText = UiTheme.Label(more, "Text", "", 18, TextAnchor.MiddleLeft, UiTheme.InkDim, FontStyle.Bold);

            // Orders
            RectTransform orders = UiTheme.Rect(rect, "DirectivesBar", new Vector2(1f, 0.5f), new Vector2(-24f, -4f), new Vector2(600f, 88f));
            HorizontalLayoutGroup layout = orders.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            CreateDirectiveButton(orders, UiTheme.Icon.Wood, "Гілки", GatherDirective.WoodGatherBranchesOnly,
                "Збирати гілки", "Обтрушують дерева, не рубаючи їх: ліс лишається, деревини менше.");
            CreateDirectiveButton(orders, UiTheme.Icon.Hammer, "Рубати", GatherDirective.WoodChopTreesOnly,
                "Рубати дерева", "Валять дерева на дрова: більше деревини, ліс рідшає. Сокира рубає швидше (I — ×1,25); найкращу зі складу беруть самі.");
            CreateDirectiveButton(orders, UiTheme.Icon.Ore, "Руда", GatherDirective.OrePebblesOnly,
                "Збирати руду", "Підбирають камінці руди з землі й несуть у скриню.");
            CreateDirectiveButton(orders, UiTheme.Icon.House, "Шахта", GatherDirective.HaulOreFromMineToChest,
                "Носити з шахти", "Носять видобуту руду з шахти в скриню. Щоб працювати в шахті — ПКМ по шахті.");
            CreateDirectiveButton(orders, UiTheme.Icon.Worker, "Авто", GatherDirective.Auto,
                "Самостійно", "Самі беруться за роботу, якої бракує: поки дерева менше, ніж на наступну ратушу, більшість іде по дерево, решта носить руду з шахти; потім навпаки.");
            CreateGearButton(orders);

            RectTransform close = UiTheme.Rect(rect, "DeselectBtn", new Vector2(1f, 1f), new Vector2(14f, 14f), new Vector2(38f, 38f));
            UiTheme.PanelImage(close, UiTheme.ButtonSprite, new Color(1f, 0.72f, 0.68f));
            UiTheme.AddButton(close.gameObject).onClick.AddListener(DeselectAll);
            UiTheme.Label(close, "Text", "×", 26, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            Tooltip.Set(close.gameObject, "Зняти виділення  " + TipText.Key("ПКМ по порожньому / Esc"));
        }

        private static string CardTip(RtsUnit u)
        {
            HealthComponent hp = u.GetComponent<HealthComponent>();
            NpcResourceGatherer g = u.GetComponent<NpcResourceGatherer>();
            return TipText.Title(DisplayName(u), RankColor(RankOf(u), u.IsHero)) +
                   (hp != null ? $"\nЗдоров'я {hp.CurrentHealth} / {hp.MaxHealth}" : "") +
                   (g != null ? "\n" + (g.IsIdle ? TipText.Bad("Без діла") : GetDirectiveName(g.CurrentDirective)) : "") +
                   "\n" + TipText.Dim("Клік — лише цей, Shift+клік — прибрати з обраних");
        }

        private void CreateGearButton(Transform parent)
        {
            var go = new GameObject("GearBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiTheme.PanelImage((RectTransform)go.transform, UiTheme.ButtonSprite, new Color(1f, 0.92f, 0.75f));
            Button btn = UiTheme.AddButton(go);
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.2f, 1.12f, 0.95f); cb.pressedColor = new Color(0.78f, 0.72f, 0.64f);
            cb.colorMultiplier = 1.25f;
            btn.colors = cb;
            btn.onClick.AddListener(() => EquipmentUI.Instance.OpenDefault());
            RectTransform icon = UiTheme.Rect(go.transform, "Icon", new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(42f, 42f));
            Image img = icon.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.ItemSprite(ItemType.Helmet, 3); img.preserveAspect = true; img.raycastTarget = false;
            RectTransform txt = UiTheme.Rect(go.transform, "Label", new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(120f, 26f));
            UiTheme.Label(txt, "Text", "Спорядж.", 16, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            Tooltip.Set(go, () => TipText.Title("Спорядження", UiTheme.Gold) + "\nЗброя й броня обраних зі спільного складу.  " + TipText.Key(HudTips.Key(KeyAction.Equipment)));
        }

        private void CreateDirectiveButton(Transform parent, UiTheme.Icon icon, string label, GatherDirective directive, string title, string tip)
        {
            var go = new GameObject("DirectiveBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiTheme.PanelImage((RectTransform)go.transform, UiTheme.ButtonSprite);
            Button btn = UiTheme.AddButton(go);
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.2f, 1.12f, 0.95f); cb.pressedColor = new Color(0.78f, 0.72f, 0.64f);
            cb.colorMultiplier = 1.25f;
            btn.colors = cb;
            btn.onClick.AddListener(() => IssueDirectiveToSelected(directive));
            UiTheme.IconImage(go.transform, icon, new Vector2(0.5f, 1f), new Vector2(0f, -8f), 38f);
            RectTransform txt = UiTheme.Rect(go.transform, "Label", new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(120f, 26f));
            UiTheme.Label(txt, "Text", label, 18, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            Tooltip.Set(go, () => TipText.Title(title, UiTheme.Gold) + "\n" + tip + "\n" + TipText.Dim($"Зараз так працюють: {WorkerRegistry.GetDirectiveWorkerCount(directive)}"));
        }
    }
}
