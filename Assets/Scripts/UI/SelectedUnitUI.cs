using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.RTS;
using BattleOfAges.InputSystem;
using BattleOfAges.Crafting;
using BattleOfAges.Combat;
using TMPro;

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
        private const float CardSize = 38f, CardGap = 3f; // two rows under the title

        private GameObject panelObj;
        private TMP_Text titleText, statusText, statsText, hpText;
        private RectTransform single, grid, hpFill;
        private Image portrait;
        private TMP_Text overflowText;
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
            UnitProgress progress = unit.IsHero ? null : unit.GetComponent<UnitProgress>();
            int xp = progress != null ? progress.BattleXp : 0;
            int langBit = Loc.En ? 1 : 0;
            int heroKey = unit.IsHero ? HeroPortraitKey(unit) : 0;
            if (singleHead.Changed(Shown.Key(unit.GetInstanceID(), (int)rank * 2 + langBit, xp, heroKey)))
            {
                int need = UnitProgress.XpToPromote(rank);
                titleText.richText = true;
                titleText.text = DisplayName(unit) + (progress != null && need > 0
                    ? Loc.Pick($"  <size=14>до рангу «{HeroUI.RankName(rank + 1)}»: {xp}/{need} досвіду</size>", $"  <size=14>to «{HeroUI.RankName(rank + 1)}»: {xp}/{need} XP</size>")
                    : "");
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
            if (singleStats.Changed(c != null ? Shown.Key(c.AttackDamage, Mathf.RoundToInt(c.AttackCooldown * 10f), armour * 2 + langBit) : -1))
                statsText.text = c != null
                    ? Loc.Pick(
                        $"Шкода <color=#F5CC73>{c.AttackDamage}</color>  ·  удар раз на {c.AttackCooldown:0.0} с  ·  захист <color=#F5CC73>{armour}%</color>",
                        $"Damage <color=#F5CC73>{c.AttackDamage}</color>  ·  hit every {c.AttackCooldown:0.0} s  ·  armor <color=#F5CC73>{armour}%</color>")
                    : "";

            NpcResourceGatherer gatherer = singleGatherer;
            int jobKind = unit.WallBound ? (unit.IsPostedOnWall ? 1 : 2) : unit.HasAttackTarget ? 3 : gatherer == null ? (unit.IsHero ? 4 : 5) : gatherer.IsIdle ? 6 : 7;
            if (singleJob.Changed(jobKind == 7 ? Shown.Key(jobKind * 2 + langBit, (int)gatherer.CurrentDirective, (int)gatherer.CurrentState) : jobKind * 2 + langBit))
                statusText.text = jobKind == 1 ? Loc.Pick("На стіні, на посту", "On the wall, on post")
                                : jobKind == 2 ? Loc.Pick("Іде на стіну", "Heading to the wall")
                                : jobKind == 3 ? Loc.Pick("У бою", "In combat")
                                : jobKind == 4 ? Loc.Pick("Герой", "Hero")
                                : jobKind == 5 ? Loc.Pick("Боєць", "Fighter")
                                : jobKind == 6 ? Loc.Pick("<color=#DB5242>Без діла</color>", "<color=#DB5242>Idle</color>")
                                : $"{GetDirectiveName(gatherer.CurrentDirective)}  ·  {GetStateName(gatherer.CurrentState)}";
        }

        private void ShowGroup(IReadOnlyList<RtsUnit> selected)
        {
            int count = selected.Count;
            singleUnit = null; // the single view starts afresh next time
            titleText.color = UiTheme.Gold;

            int hash = count * 2 + (Loc.En ? 1 : 0);
            foreach (RtsUnit u in selected) hash = hash * 31 + (u != null ? u.GetInstanceID() + (u.IsHero ? HeroPortraitKey(u) * 97 : 0) : 0);
            int shown = Mathf.Min(count, CardColumns * CardRows);
            if (hash != shownHash)
            {
                shownHash = hash;
                titleText.richText = true;
                titleText.text = GroupTitle(selected);
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

        /// <summary>How many, and who: the cards show only the first of a big group.</summary>
        private static string GroupTitle(IReadOnlyList<RtsUnit> selected)
        {
            int people = 0, warriors = 0, knights = 0, generals = 0, heroes = 0;
            foreach (RtsUnit u in selected)
            {
                if (u == null) continue;
                if (u.IsHero) { heroes++; continue; }
                switch (RankOf(u))
                {
                    case UnitRank.General: generals++; break;
                    case UnitRank.Knight: knights++; break;
                    case UnitRank.Warrior: warriors++; break;
                    default: people++; break;
                }
            }
            var who = new List<string>();
            if (heroes > 0) who.Add(Loc.Pick("герой", "hero"));
            if (generals > 0) who.Add(Loc.Pick($"генерали {generals}", $"generals {generals}"));
            if (knights > 0) who.Add(Loc.Pick($"лицарі {knights}", $"knights {knights}"));
            if (warriors > 0) who.Add(Loc.Pick($"воїни {warriors}", $"warriors {warriors}"));
            if (people > 0) who.Add(Loc.Pick($"люди {people}", $"villagers {people}"));
            return Loc.Pick($"Обрано: {selected.Count}  <size=14>{string.Join(" · ", who)}</size>", $"Selected: {selected.Count}  <size=14>{string.Join(" · ", who)}</size>");
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

        private static int HeroPortraitKey(RtsUnit u)
        {
            if (u == null || !u.IsHero) return 0;
            Equipment gear = u.GetComponent<Equipment>();
            HeroProgress hp = u.GetComponent<HeroProgress>();
            int w = gear != null && !gear[EquipSlot.Weapon].IsEmpty ? ((int)gear[EquipSlot.Weapon].Type + 1) * 16 + gear[EquipSlot.Weapon].Level : 0;
            int c = hp != null ? (int)hp.Class : 0;
            return w * 8 + c + 1;
        }

        private static void SetPortrait(Image img, RtsUnit u, UnitRank rank)
        {
            if (u != null && u.IsHero)
            {
                Equipment gear = u.GetComponent<Equipment>();
                HeroProgress hp = u.GetComponent<HeroProgress>();
                ItemType weapon = gear != null && !gear[EquipSlot.Weapon].IsEmpty ? gear[EquipSlot.Weapon].Type
                                : hp != null && hp.Class == HeroClass.Archer ? ItemType.Bow
                                : hp != null && hp.Class == HeroClass.Mage ? ItemType.Staff
                                : hp != null && hp.Class == HeroClass.Tank ? ItemType.Shield : ItemType.Sword;
                int lvl = gear != null && !gear[EquipSlot.Weapon].IsEmpty ? Mathf.Clamp(gear[EquipSlot.Weapon].Level, 1, 5) : 5;
                img.sprite = UiTheme.ItemSprite(weapon, lvl);
            }
            else if (rank == UnitRank.General) img.sprite = UiTheme.ItemSprite(ItemType.Helmet, 5);
            else if (rank == UnitRank.Knight) img.sprite = UiTheme.ItemSprite(ItemType.Helmet, 3);
            else if (rank == UnitRank.Warrior) img.sprite = UiTheme.IconSprite(UiTheme.Icon.Army);
            else img.sprite = UiTheme.IconSprite(UiTheme.Icon.Worker);
        }

        public static string DisplayName(RtsUnit u)
        {
            if (u.IsHero) return Loc.Pick("Герой", "Hero");
            string n = u.gameObject.name;
            string rank = HeroUI.RankName(RankOf(u));
            rank = char.ToUpper(rank[0]) + rank.Substring(1);
            if (n.StartsWith("NPC_Villager_")) return (RankOf(u) == UnitRank.Person ? Loc.Pick("Робітник", "Worker") : rank) + " " + n.Substring("NPC_Villager_".Length);
            return rank;
        }

        /// <summary>The single unit's details for its tooltip: gear and how far it is from the next rank.</summary>
        private string SingleTip()
        {
            if (commander == null || commander.SelectedUnits.Count != 1 || commander.SelectedUnits[0] == null) return null;
            RtsUnit u = commander.SelectedUnits[0];
            var sb = new System.Text.StringBuilder(TipText.Title(DisplayName(u), RankColor(RankOf(u), u.IsHero)));
            sb.Append(Loc.Pick("\nРанг: ", "\nRank: ")).Append(HeroUI.RankName(RankOf(u)));
            UnitProgress p = u.GetComponent<UnitProgress>();
            if (p != null && !p.IsHero && RankOf(u) != UnitRank.General)
                sb.Append(Loc.Pick($"\nБойовий досвід {p.BattleXp} / {UnitProgress.XpToPromote(RankOf(u))} до підвищення", $"\nBattle XP {p.BattleXp} / {UnitProgress.XpToPromote(RankOf(u))} to promotion"));
            Equipment gear = u.GetComponent<Equipment>();
            if (gear != null)
            {
                var parts = new List<string>();
                foreach (var slot in ItemTypeNames.Slots) if (!gear[slot].IsEmpty) parts.Add(gear[slot].ToString());
                sb.Append("\n").Append(TipText.Dim(parts.Count == 0 ? Loc.Pick("Без спорядження", "No equipment") : string.Join(" · ", parts)));
            }
            return sb.ToString();
        }

        private void IssueDirectiveToSelected(GatherDirective directive)
        {
            if (commander == null) return;
            var ids = new List<int>();
            foreach (RtsUnit unit in commander.SelectedUnits) if (unit != null) ids.Add(Net.WorldEntity.IdOf(unit).Value);
            Net.CommandBus.Submit(new Net.SetDirectiveCommand { Units = ids.ToArray(), Directive = directive });
        }

        private void DeselectAll()
        {
            if (commander != null) commander.DeselectAll();
        }

        private static string GetDirectiveName(GatherDirective directive)
        {
            switch (directive)
            {
                case GatherDirective.WoodGatherBranchesOnly: return Loc.Pick("Збір гілок", "Gather branches");
                case GatherDirective.WoodChopTreesOnly: return Loc.Pick("Рубання дерев", "Chop trees");
                case GatherDirective.OrePebblesOnly: return Loc.Pick("Збір камінчиків", "Pick up ore");
                case GatherDirective.HaulOreFromMineToChest: return Loc.Pick("Носить руду з шахти", "Hauls ore from the mine");
                case GatherDirective.Idle: return Loc.Pick("Чекає наказу", "Awaiting orders");
                default: return Loc.Pick("Самостійно", "Autonomous");
            }
        }

        private static string GetStateName(GathererState state)
        {
            switch (state)
            {
                case GathererState.FindingResource: return Loc.Pick("шукає роботу", "looking for work");
                case GathererState.MovingToResource: return Loc.Pick("іде до ресурсу", "moving to resource");
                case GathererState.Harvesting: return Loc.Pick("добуває", "harvesting");
                case GathererState.DeliveringToChest: return Loc.Pick("несе в скриню", "delivering to chest");
                case GathererState.MovingToMine: return Loc.Pick("іде до шахти", "moving to mine");
                case GathererState.HaulingFromMine: return Loc.Pick("несе руду з шахти", "hauling ore from mine");
                case GathererState.MovingToMineToWork: return Loc.Pick("іде працювати в шахту", "heading to work in mine");
                case GathererState.WorkingInsideMine: return Loc.Pick("працює в шахті", "working in mine");
                case GathererState.WorkingAtBench: return Loc.Pick("працює за верстаком", "working at workbench");
                case GathererState.Building: return Loc.Pick("будує", "building");
                case GathererState.WorkingAtPlace: return Loc.Pick("працює на місці", "working at station");
                default: return Loc.Pick("рухається", "moving");
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
            LocText.Detach(titleText);

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
            LocText.Detach(statsText);
            RectTransform job = UiTheme.Rect(single, "Job", new Vector2(0f, 1f), new Vector2(84f, -52f), new Vector2(346f, 26f));
            statusText = UiTheme.Label(job, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.InkDim);
            LocText.Detach(statusText);

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
                RectTransform ic = UiTheme.Rect(card, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0f, 3f), new Vector2(26f, 26f));
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
            RectTransform orders = UiTheme.Rect(rect, "DirectivesBar", new Vector2(1f, 0.5f), new Vector2(-24f, -4f), new Vector2(650f, 88f));
            HorizontalLayoutGroup layout = orders.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            CreateDirectiveButton(orders, UiTheme.Icon.Wood, "Гілки", GatherDirective.WoodGatherBranchesOnly,
                "Збирати гілки", "Gather branches",
                "Обтрушують дерева, не рубаючи їх: ліс лишається, деревини менше.",
                "Shake branches without felling trees: forest stays intact, less wood.");
            CreateDirectiveButton(orders, UiTheme.Icon.Hammer, "Рубати", GatherDirective.WoodChopTreesOnly,
                "Рубати дерева", "Chop trees",
                "Валять дерева на дрова: більше деревини, ліс рідшає. Сокира рубає швидше (I — ×1,25); найкращу зі складу беруть самі.",
                "Fell trees for timber: more wood, forest thins out. Axes chop faster (I — ×1.25); workers take the best one from the armory.",
                UiTheme.ItemSprite(ItemType.Axe, 2));
            CreateDirectiveButton(orders, UiTheme.Icon.Ore, "Камінці", GatherDirective.OrePebblesOnly,
                "Збирати камінці руди", "Gather ore pebbles",
                "Підбирають камінці руди з землі й несуть у скриню.",
                "Pick up ore pebbles from the ground and carry them to the chest.");
            CreateDirectiveButton(orders, UiTheme.Icon.House, "Носій", GatherDirective.HaulOreFromMineToChest,
                "Носити руду з шахти", "Haul ore from the mine",
                "Носять уже видобуту руду з шахти в скриню; самі не копають. Щоб копати в шахті — ПКМ по шахті.",
                "Carry ore already mined from the mine to the chest; they do not dig. To dig inside a mine — RMB on the mine.",
                UiTheme.ItemSprite(ItemType.Pickaxe, 2));
            CreateDirectiveButton(orders, UiTheme.Icon.Worker, "Авто", GatherDirective.Auto,
                "Самостійно", "Autonomous",
                "Самі беруться за роботу, якої бракує: поки дерева менше, ніж на наступну ратушу, більшість іде по дерево, решта носить руду з шахти; потім навпаки.",
                "Automatically take on needed tasks: while wood is short for the next Town Hall upgrade, most gather wood while the rest haul ore from the mine.");
            CreateDistrictsButton(orders);
            CreateGearButton(orders);
            CreateStopButton(orders);

            RectTransform close = UiTheme.Rect(rect, "DeselectBtn", new Vector2(1f, 1f), new Vector2(14f, 14f), new Vector2(38f, 38f));
            UiTheme.PanelImage(close, UiTheme.ButtonSprite, new Color(1f, 0.72f, 0.68f));
            UiTheme.AddButton(close.gameObject).onClick.AddListener(DeselectAll);
            UiTheme.Label(close, "Text", "×", 26, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            Tooltip.Set(close.gameObject, () => Loc.Pick("Зняти виділення  ", "Clear selection  ") + TipText.Key("Esc"));
        }

        private static string CardTip(RtsUnit u)
        {
            HealthComponent hp = u.GetComponent<HealthComponent>();
            NpcResourceGatherer g = u.GetComponent<NpcResourceGatherer>();
            return TipText.Title(DisplayName(u), RankColor(RankOf(u), u.IsHero)) +
                   (hp != null ? Loc.Pick($"\nЗдоров'я {hp.CurrentHealth} / {hp.MaxHealth}", $"\nHealth {hp.CurrentHealth} / {hp.MaxHealth}") : "") +
                   (g != null ? "\n" + (g.IsIdle ? TipText.Bad(Loc.Pick("Без діла", "Idle")) : GetDirectiveName(g.CurrentDirective)) : "") +
                   "\n" + TipText.Dim(Loc.Pick("Клік — лише цей, Shift+клік — прибрати з обраних", "Click — select only this, Shift+click — remove from selection"));
        }

        /// <summary>To the districts: fields, sawmills, benches, markets that want hands (else any with a free place).</summary>
        private void CreateDistrictsButton(Transform parent)
        {
            var go = new GameObject("DistrictsBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiTheme.PanelImage((RectTransform)go.transform, UiTheme.ButtonSprite);
            Button btn = UiTheme.AddButton(go);
            btn.onClick.AddListener(() => { if (commander != null) commander.OrderDistricts(); });
            UiTheme.IconImage(go.transform, UiTheme.Icon.Food, new Vector2(0.5f, 1f), new Vector2(0f, -8f), 38f);
            RectTransform txt = UiTheme.Rect(go.transform, "Label", new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(120f, 26f));
            TMP_Text t = UiTheme.Label(txt, "Text", "Райони", 16, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            Tooltip.Set(go, () => TipText.Title(Loc.Pick("У райони", "To the districts"), UiTheme.Gold)
                + Loc.Pick("\nНа поля, лісопилки, верстаки й ринки районів: спершу туди, де бракує рук, інакше — на найближче вільне місце. Там і лишаються, поки не дасте іншого наказу.",
                           "\nTo the districts' fields, sawmills, benches and markets: first where hands are short, else the nearest free place. They stay until given another order."));
        }

        private void CreateStopButton(Transform parent)
        {
            var go = new GameObject("StopBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiTheme.PanelImage((RectTransform)go.transform, UiTheme.ButtonSprite, new Color(1f, 0.78f, 0.72f));
            Button btn = UiTheme.AddButton(go);
            btn.onClick.AddListener(() => { if (commander != null) commander.OrderStop(); });
            RectTransform ic = UiTheme.Rect(go.transform, "Icon", new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(38f, 38f));
            Image ii = ic.gameObject.AddComponent<Image>();
            ii.sprite = UiTheme.ItemSprite(ItemType.Shield, 2); ii.preserveAspect = true; ii.raycastTarget = false;
            RectTransform txt = UiTheme.Rect(go.transform, "Label", new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(120f, 26f));
            UiTheme.Label(txt, "Text", "Стій", 16, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            Tooltip.Set(go, () => TipText.Title(Loc.Pick("Стій", "Stop"), UiTheme.Gold)
                + Loc.Pick("\nКидають роботу й похід, стоять на місці. Ворога поруч однаково б'ють.  ", "\nDrop work and movement, hold position. Still fight nearby enemies.  ")
                + TipText.Key(HudTips.Key(KeyAction.StopUnits)));
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
            UiTheme.Label(txt, "Text", "Спорядж.", 14, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            Tooltip.Set(go, () => TipText.Title(Loc.Pick("Спорядження", "Equipment"), UiTheme.Gold)
                + Loc.Pick("\nЗброя й броня обраних зі спільного складу.  ", "\nWeapons and armor for selected units from the shared armory.  ")
                + TipText.Key(HudTips.Key(KeyAction.Equipment)));
        }

        private void CreateDirectiveButton(Transform parent, UiTheme.Icon icon, string label, GatherDirective directive, string titleUk, string titleEn, string tipUk, string tipEn, Sprite spriteOverride = null)
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
            if (spriteOverride != null)
            {
                RectTransform ic = UiTheme.Rect(go.transform, "Icon", new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(38f, 38f));
                Image ii = ic.gameObject.AddComponent<Image>();
                ii.sprite = spriteOverride; ii.preserveAspect = true; ii.raycastTarget = false;
            }
            else
            {
                UiTheme.IconImage(go.transform, icon, new Vector2(0.5f, 1f), new Vector2(0f, -8f), 38f);
            }
            RectTransform txt = UiTheme.Rect(go.transform, "Label", new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(120f, 26f));
            UiTheme.Label(txt, "Text", label, 16, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            Tooltip.Set(go, () => TipText.Title(Loc.Pick(titleUk, titleEn), UiTheme.Gold) + "\n" + Loc.Pick(tipUk, tipEn) + "\n" + TipText.Dim(Loc.Pick($"Зараз так працюють: {WorkerRegistry.GetDirectiveWorkerCount(directive)}", $"Currently assigned: {WorkerRegistry.GetDirectiveWorkerCount(directive)}")));
        }
    }
}
