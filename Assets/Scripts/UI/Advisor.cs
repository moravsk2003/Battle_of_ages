using System;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.InputSystem;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// "First steps" (docs/GAME_DESIGN.md §21, stage 1.8): the next goal of the opening in the corner — what to do and
    /// how (keys) — with how many are done. Goals already met are skipped; the × hides it (settings bring it back).
    /// </summary>
    public sealed class Advisor : MonoBehaviour
    {
        private const string HiddenKey = "Settings_AdvisorHidden";

        public static bool Hidden
        {
            get => PlayerPrefs.GetInt(HiddenKey, 0) == 1;
            set { PlayerPrefs.SetInt(HiddenKey, value ? 1 : 0); if (instance != null) instance.Refresh(); }
        }

        private static Advisor instance;
        private RectTransform panel;
        private Text title, hint, count;
        private float timer;

        private static string K(KeyAction a) => TipText.Key(HudTips.Key(a));
        /// <summary>"M (мапа) → B (будівництво) →": where every building starts.</summary>
        private static string ToBuild() => $"{K(KeyAction.ToggleGameMode)} мапа → {K(KeyAction.BuildMenu)} будівництво →";
        private static bool Has(BuildingType t) { foreach (BuildingNode b in BuildingNode.Active) if (b != null && b.Type == t) return true; return false; }

        /// <summary>The goals in order: (what to do, how, done?).</summary>
        public static (string goal, Func<string> how, Func<bool> done)[] Goals => World.WorldSettings.Bounded ? CampaignGoals : FixedGoals;

        /// <summary>A campaign: most buildings grow from marked zones (M5).</summary>
        private static string ToZones() => $"{K(KeyAction.ToggleGameMode)} мапа → {K(KeyAction.BuildMenu)} → панель «Зони»:";

        private static readonly (string goal, Func<string> how, Func<bool> done)[] CampaignGoals =
        {
            ("Збудуйте скриню-склад", () => $"{ToBuild()} «Скриня-склад», ЛКМ — поставити. Робітники «Авто» самі її збудують; у скриню вони зносять дерево й руду.", () => StorageChestNode.ActiveChests.Count > 0),
            ("Назбирайте 20 деревини", () => $"Самі: {K(KeyAction.ChopTree)} біля дерева. Або на мапі: виділіть робітників і ПКМ по дереву.", () => ResourceManager.Instance != null && ResourceManager.Instance.WoodCount >= 20),
            ("Розмітьте рудню", () => $"{ToZones()} «Рудня», ЛКМ малює зону. Робітники зведуть шахту; ПКМ по ній — шахтарі, носії — наказ «Шахта» або «Авто».", () => MineNode.ActiveMines.Count > 0),
            ("Розмітьте поля", () => $"{ToZones()} «Поля». Їжа витрачається щодня; коли її мало, робітники «Авто» самі стають у поле.", () => Has(BuildingType.Field)),
            ("Викуйте першу річ", () => $"Верстак: на мапі виділіть робітника, ПКМ по верстаку; ЛКМ по ньому — вікно, рецепт → «Виготовити». Ще верстаки — зона «Ремесла».", () => Armory.Instance.Items.Count > 0),
            ("Озбройте робітника", () => $"На мапі виділіть людей, {K(KeyAction.Equipment)} — спорядження: клік по мечу чи броні на складі дає по одній кожному. Вище за воїна (лицар, генерал) піднімають лише бої.", () => ArmedVillager()),
            ("Збудуйте ратушу", () => $"{ToBuild()} «Ратуша»: без неї населення не росте. Стелю задає її рівень, приріст прискорюють хати.", () => TownHall.Current != null),
            ("Розмітьте житло", () => $"{ToZones()} «Житло». Кожна хата прискорює приріст населення.", () => Has(BuildingType.WorkerHouse)),
            ("Покращте ратушу до II", () => $"На мапі наведіть на ратушу — кнопка «Покращити». Треба {Combat.GameBalance.TownHallWood[2]} дерева й {Combat.GameBalance.TownHallOre[2]} руди I+: з шахти зрідка або переплавкою на верстаці (10 → 1).", () => TownHall.Current != null && TownHall.Current.Level >= 2),
            ("Збудуйте стіну з воротами", () => $"{ToBuild()} «Стіна» і «Ворота». Виділені робітники ПКМ по стіні стають на пости; {K(KeyAction.Alarm)} — тривога, усі до зброї.", () => Has(BuildingType.WallGate)),
        };

        private static readonly (string goal, Func<string> how, Func<bool> done)[] FixedGoals =
        {
            ("Збудуйте скриню-склад", () => $"{ToBuild()} «Скриня», ЛКМ — поставити. У скриню робітники зносять дерево й руду.", () => StorageChestNode.ActiveChests.Count > 0),
            ("Назбирайте 20 деревини", () => $"Самі: {K(KeyAction.ChopTree)} біля дерева — рубати, {K(KeyAction.InteractGather)} — підняти гілку. Або на мапі: виділіть робітників і ПКМ по дереву.", () => ResourceManager.Instance != null && ResourceManager.Instance.WoodCount >= 20),
            ("Збудуйте шахту", () => $"{ToBuild()} «Шахта». Виділіть робітників (ЛКМ або рамкою) і ПКМ по шахті — копатимуть усередині. Носять руду в скриню інші: наказ «Шахта» або «Авто».", () => MineNode.ActiveMines.Count > 0),
            ("Збудуйте верстак", () => $"{ToBuild()} «Верстак». На ньому робітник кує зброю й броню для всіх.", () => WorkbenchNode.Instance != null),
            ("Викуйте першу річ", () => $"На мапі: виділіть робітника, ПКМ по верстаку — він стане до роботи; ЛКМ по верстаку — вікно, рецепт → «Виготовити». (Від першої особи — {K(KeyAction.InteractGather)} біля верстака.)", () => Armory.Instance.Items.Count > 0),
            ("Озбройте робітника", () => $"На мапі виділіть людей, {K(KeyAction.Equipment)} — спорядження: клік по мечу чи броні на складі дає по одній кожному. Вище за воїна (лицар, генерал) піднімають лише бої.", () => ArmedVillager()),
            ("Збудуйте ратушу", () => $"{ToBuild()} «Ратуша». Вона піднімає межу населення; нові робітники народжуються самі.", () => TownHall.Current != null),
            ("Досягніть 10 жителів", () => $"{ToBuild()} «Будинок»: кожен дає ще одного робітника. Далі приріст іде сам, поки є місце.", () => TownHall.Population >= 10),
            ("Покращте ратушу до II", () => $"На мапі ({K(KeyAction.ToggleGameMode)}) наведіть на ратушу — кнопка «Покращити». Треба {Combat.GameBalance.TownHallWood[2]} дерева й {Combat.GameBalance.TownHallOre[2]} руди I+: з шахти зрідка або переплавкою на верстаці (10 → 1).", () => TownHall.Current != null && TownHall.Current.Level >= 2),
            ("Збудуйте стіну з воротами", () => $"{ToBuild()} «Стіна» і «Ворота». Виділені робітники ПКМ по стіні стають на пости; {K(KeyAction.Alarm)} — тривога, усі до зброї.", () => Has(BuildingType.WallGate)),
        };

        /// <summary>A villager (not a guard born armed) holds a sword, a bow or a staff.</summary>
        private static bool ArmedVillager()
        {
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
            {
                Equipment e = w != null ? w.GetComponent<Equipment>() : null;
                if (e == null) continue;
                Item held = e[EquipSlot.Weapon];
                if (!held.IsEmpty && !GearDispenser.IsTool(held.Type)) return true;
            }
            return false;
        }

        public static int DoneCount { get { int n = 0; foreach (var g in Goals) if (g.done()) n++; return n; } }

        /// <summary>The first goal not met yet, or -1 when all are.</summary>
        public static int Current
        {
            get
            {
                for (int i = 0; i < Goals.Length; i++) if (!Goals[i].done()) return i;
                return -1;
            }
        }

        public static Advisor Create(RectTransform hudRoot)
        {
            var go = new GameObject("Advisor", typeof(RectTransform));
            go.transform.SetParent(hudRoot, false);
            Advisor a = go.AddComponent<Advisor>();
            a.Build((RectTransform)go.transform);
            instance = a;
            return a;
        }

        private void Build(RectTransform root)
        {
            // The component lives on a full-screen holder so it keeps running while the panel is hidden
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            panel = UiTheme.Rect(root, "Panel", new Vector2(1f, 1f), new Vector2(-18f, -250f), new Vector2(380f, 166f));
            UiTheme.PanelImage(panel);
            RectTransform head = UiTheme.Rect(panel, "Head", new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(250f, 24f));
            UiTheme.Label(head, "Text", "Порада", 15, TextAnchor.MiddleLeft, UiTheme.InkDim, FontStyle.Bold);
            RectTransform cnt = UiTheme.Rect(panel, "Count", new Vector2(1f, 1f), new Vector2(-46f, -8f), new Vector2(80f, 24f));
            count = UiTheme.Label(cnt, "Text", "", 15, TextAnchor.MiddleRight, UiTheme.InkDim);
            RectTransform t = UiTheme.Rect(panel, "Goal", new Vector2(0f, 1f), new Vector2(16f, -32f), new Vector2(350f, 28f));
            title = UiTheme.Label(t, "Text", "", 20, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            RectTransform h = UiTheme.Rect(panel, "How", new Vector2(0f, 1f), new Vector2(16f, -62f), new Vector2(350f, 96f));
            hint = UiTheme.Label(h, "Text", "", 15, TextAnchor.UpperLeft, UiTheme.Ink);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.supportRichText = true;
            Button close = UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-6f, -6f), new Vector2(30f, 28f), () => Hidden = true, 20);
            Tooltip.Set(close.gameObject, "Сховати поради" + "\n" + TipText.Dim("Повернути — у налаштуваннях"));
            Refresh();
        }

        private void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.5f;
            Refresh();
        }

        private string shownGoal;
        private float bornAt;

        private void Start() => bornAt = Time.unscaledTime;

        private void Refresh()
        {
            int i = Current;
            bool show = !Hidden && i >= 0;
            if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
            if (!show) return;
            count.text = $"{DoneCount} / {Goals.Length}";
            // (not in the first seconds: a scene or a save fills in and several goals tick at once)
            if (shownGoal != Goals[i].goal && shownGoal != null && Time.unscaledTime - bornAt > 4f) { StrategyHud.Notify("Ціль виконано!"); UiSound.Play(UiSound.Kind.Success); }
            shownGoal = Goals[i].goal;
            title.text = Goals[i].goal;
            hint.text = Goals[i].how();
        }
    }
}
