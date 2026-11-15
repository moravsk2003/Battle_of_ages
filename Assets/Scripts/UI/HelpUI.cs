using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using TMPro;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The help window (F1): what there is and how to do it, a tab per subject, with the player's own key bindings (a player report:
    /// "the interface is unclear, it is unclear what there is"; then "the help is overloaded"). It opens on what the player is looking
    /// at: the map's controls on the map. Closes with F1, Esc or the cross; the top bar's mode hint tells about it.
    /// </summary>
    public sealed class HelpUI : MonoBehaviour
    {
        private static HelpUI instance;
        public static bool IsVisible => instance != null && instance.open;
        public static HelpUI Instance => instance;

        private GameObject window;
        private TMP_Text page;
        private TMP_Text[] tabs;
        /// <summary>The tab on show.</summary>
        public int CurrentTab { get; private set; }
        private bool open;

        private static readonly Vector2 Size = new Vector2(1120f, 780f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("HelpUI").AddComponent<HelpUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            window.SetActive(false);
            DontDestroyOnLoad(gameObject);
            Loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            Loc.Changed -= OnLanguageChanged;
            if (instance == this) instance = null;
        }

        private void OnLanguageChanged()
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Length; i++)
                if (tabs[i] != null) tabs[i].text = TabName(i);
            if (page != null) page.text = TabText(CurrentTab);
        }

        public void Open()
        {
            open = true;
            ShowTab(GameContext.MapOpen ? 1 : 0);
            window.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            open = false;
            window.SetActive(false);
            GameModeManager modes = Managers.GameContext.Modes;
            if (!GameWindows.AnyModalOpen && !GameWindows.Paused && (modes == null || modes.CurrentMode == GameMode.FirstPerson))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            if (Time.frameCount < 2) return;
            bool f1 = UniversalInput.IsKeyDown(KeyCode.F1);
            if (open)
            {
                if (f1) Close(); // (Esc: the pause menu closes every window)
                return;
            }
            if (f1 && !GameWindows.Paused)
            {
                GameWindows.CloseAllModals();
                Open();
            }
        }

        private static string Key(KeyAction a) => "<color=#f5cc73><b>" + KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(a)) + "</b></color>";
        private static string K(string key) => "<color=#f5cc73><b>" + key + "</b></color>";
        private static string Head(string text) => "<size=22><color=#f5cc73><b>" + text + "</b></color></size>";

        // ------------------------------------------------------------------ the tabs

        /// <summary>The tabs' names, in order.</summary>
        public static readonly string[] TabNames = { "Основи", "Мапа", "Будівництво", "Люди", "Бій і герой", "Світ", "Гра в мережі" };
        public static readonly string[] TabNamesEn = { "Basics", "Map", "Building", "People", "Hero & Combat", "World", "Multiplayer" };

        public static string TabName(int index) => (Loc.En ? TabNamesEn : TabNames)[Mathf.Clamp(index, 0, TabNames.Length - 1)];

        /// <summary>A tab's text, with the player's own keys.</summary>
        public static string TabText(int index)
        {
            switch (index)
            {
                case 0: return Loc.En ? BasicsEn() : Basics();
                case 1: return Loc.En ? MapControlsEn() : MapControls();
                case 2: return Loc.En ? BuildingEn() : Building();
                case 3: return Loc.En ? PeopleEn() : People();
                case 4: return Loc.En ? FightEn() : Fight();
                case 5: return Loc.En ? TheWorldEn() : TheWorld();
                default: return Loc.En ? NetworkEn() : Network();
            }
        }

        private static string Basics() =>
            Head("Від першої особи") + "\n" +
            $"{K("W A S D")} — рух,  {K("Пробіл")} — стрибок,  миша — огляд,  {K(HudTips.Key(KeyAction.AutoWalk))} — автоходьба\n" +
            $"{K("ЛКМ")} — удар або постріл\n" +
            $"{Key(KeyAction.InteractGather)} — зібрати, відкрити скриню, верстак, шахту,  {Key(KeyAction.ChopTree)} — рубати дерево\n" +
            $"{Key(KeyAction.ToggleGameMode)} — мапа, вид згори\n\n" +
            Head("Вікна") + "\n" +
            $"{Key(KeyAction.Inventory)} — інвентар,  {Key(KeyAction.Equipment)} — спорядження,  {Key(KeyAction.Hero)} — герой\n" +
            $"{Key(KeyAction.BuildMenu)} — будівництво,  {Key(KeyAction.WorkerCommandUI)} — розподіл людей,  {Key(KeyAction.WorldMap)} — мапа світу\n" +
            $"{K("F1")} — ця довідка,  {K("F3")} — FPS,  {K("F8")} — редактор для перевірок,  {K("Esc / P")} — пауза (спершу закриває вікна)\n\n" +
            Head("З чого почати") + "\n" +
            "1. Збирайте дерево й руду, несіть у скриню. Люди теж працюють самі.\n" +
            "2. Поставте ратушу: без неї населення не росте.\n" +
            "3. Розмітьте поля, житло й рудню: робітники зведуть усе самі.\n" +
            "4. Поради вгорі праворуч ведуть крок за кроком.\n\n" +
            Head("Ще") + "\n" +
            "• Наведіть курсор на будь-що, і з'явиться підказка.\n" +
            "• Пауза → Зберегти / Завантажити, Налаштування: миша, гучність, графіка, клавіші, поради.";

        private static string MapControls() =>
            Head("Вибрати") + "\n" +
            $"{K("ЛКМ")} — людину,  рамкою — кількох,  {K("Shift")} — додати або прибрати\n" +
            $"{K("Подвійний клік")} або {K("Ctrl+клік")} — усіх такого рангу на екрані\n" +
            $"{K("Ctrl+A")} — усіх людей,  {Key(KeyAction.SelectFighters)} — усіх бійців,  {Key(KeyAction.IdleWorker)} — наступного без діла\n" +
            $"{K("Ctrl+1…0")} — запам'ятати групу,  {K("Shift+1…0")} — додати до неї,  {K("1…0")} — обрати, двічі — камера до неї\n" +
            $"{K("Esc")} — зняти виділення, ще раз — назад до виду від першої особи\n\n" +
            Head("Наказати") + "\n" +
            $"{K("ПКМ")} — іти туди, бити ворога, працювати там, куди клацнули\n" +
            $"{K("Ctrl+ПКМ")} — іти й бити всіх по дорозі,  {K("Shift+ПКМ")} — ще одна точка шляху\n" +
            $"{Key(KeyAction.StopUnits)} — стій: кинути роботу й похід\n" +
            $"{K("Мінімапа")}: ЛКМ — камера туди, ПКМ — обрані йдуть туди\n\n" +
            Head("Камера") + "\n" +
            $"{K("ПКМ")} (тягнути) або {K("W A S D")} — рухати,  {K("Колесо")} — наближення\n" +
            $"{K("Q / E")} — повернути,  {K("R / F")} — нахил,  {K("Середня кнопка")} — крутити\n" +
            $"{Key(KeyAction.CenterSelection)} — до обраних,  {Key(KeyAction.AlertJump)} — до місця нападу\n\n" +
            Head("Позначки") + "\n" +
            "• Кільце під юнітом: сині — свої, червоні — бандити, інші кольори — королівства. Ромб над головою — ранг.";

        private static string Building() =>
            Head("Зони: робітники будують самі") + "\n" +
            $"{Key(KeyAction.BuildMenu)} — зони й споруди. Оберіть зону й тягніть {K("ЛКМ")} у межах золотого кола, {K("ПКМ")} стирає, {K("Esc")} — готово.\n" +
            "• Житло прискорює приріст, поля дають їжу, ліс — лісопилки, ремесла — верстаки, рудня — шахти, торгівля — ринок (від ратуші II).\n" +
            $"• «Стерти» або {K("Delete")} — гумка. На кнопках видно, скільки споруд стоїть зі скількох потрібних.\n\n" +
            Head("Споруди: ставите самі") + "\n" +
            $"«Споруди»: ратуша, скриня, стіни, ворота. {K("Z / X")} — поворот, {K("ЛКМ")} — поставити, {K("ПКМ")} — скасувати.\n" +
            $"• Стіни: {K("Tab")} — руда, сегмент прилипає до краю стіни. Рівень стін — на панелі ратуші чи стіни.\n" +
            "• Без робітників будівництво стоїть.\n\n" +
            Head("Панель будівлі") + "\n" +
            $"{K("ЛКМ")} по будівлі: здоров'я, люди, покращення, відкрити, знести. Снос повертає половину ціни, скасування ділянки — усе, що ще не збудовано.\n\n" +
            Head("Нове поселення") + "\n" +
            "Від ратуші II поставте ще одну ратушу на нічиїй землі й пошліть туди робітників. У кожного поселення свій склад і свої зони; угорі видно склад того, на яке ви дивитесь. Ринок шле каравани туди, де бракує.";

        private static string People() =>
            Head("Розподіл людей") + "\n" +
            $"{Key(KeyAction.WorkerCommandUI)} — хто де працює: райони й робота, кожне поселення окремо.\n" +
            "• «+» кличе найближчу вільну людину, «−» відпускає її. Район після цього сам більше не бере: межу знімає хрестик.\n" +
            "• На панелі обраних: кнопки роботи. «Авто» — самі беруться за те, чого бракує.\n\n" +
            Head("Ранги") + "\n" +
            "Людина → воїн → лицар → генерал. Воїном стають у боях і зрідка від роботи, лицарем і генералом — лише в боях. Люди і працюють, і б'ються.\n\n" +
            Head("Спорядження") + "\n" +
            $"{Key(KeyAction.Equipment)}: виділіть людей на мапі, клік по речі на складі дає по одній кожному. «Найкраще зі складу» — усім кращу броню. Кайло шахтарі беруть самі.\n\n" +
            Head("Верстак і шахта") + "\n" +
            "• Верстак: робітники кують речі I–V, переплавка — 10 руди в 1 руду рівнем вище.\n" +
            "• Шахта: двоє копають усередині, носії несуть руду в скриню. Повна шахта стоїть.\n" +
            "• Інструменти — самі: копач бере зі складу найкраще кайло, лісоруб — сокиру; хто перейшов на іншу роботу, здає свій назад.\n\n" +
            Head("Їжа") + "\n" +
            "1 на людину на день. Без неї населення не росте, а люди по одному гинуть. Робітники «Авто» йдуть у поле, коли їжі менше ніж на 3 дні.";

        private static string Fight() =>
            Head("Герой") + "\n" +
            $"{Key(KeyAction.Hero)}: досвід за вбивства, рівні, очки сили й здоров'я. Клас: танк, лучник, маг або ДД.\n" +
            $"{Key(KeyAction.Ability)} / {Key(KeyAction.Ability2)} — уміння класу,  {Key(KeyAction.UseFullHeal)} / {Key(KeyAction.UseLightning)} — одноразові артефакти\n\n" +
            Head("Тривога") + "\n" +
            $"{Key(KeyAction.Alarm)}: люди кидають роботу й захищають дім. Сама вмикається, коли ворог підходить до ратуші, і вимикається після бою.\n\n" +
            Head("Стіни") + "\n" +
            $"{K("ПКМ")} по стіні: юніти йдуть сходами на пости й б'ють звідти сильніше, лучники ще й далі. Тривога шле туди лучників.\n\n" +
            Head("Смерть героя") + "\n" +
            "Міжцарів'я: частина людей тікає, новим героєм стає найсильніший.";

        private static string TheWorld() =>
            Head("Мапа світу") + "\n" +
            $"{Key(KeyAction.WorldMap)}: рельєф і всі королівства, скільки до них і скільки в них людей.\n\n" +
            Head("Сусіди") + "\n" +
            "• Герої на своїх стоянках за кілька кілометрів. Найближчі королі ростуть і ходять у набіги на вас і один на одного.\n" +
            "• Табори бандитів із луками й мечами, дикі звірі.\n" +
            "• Бандити приходять на поселення від першого дня: щоразу більша ватага, напис скаже, звідки.\n\n" +
            Head("Чого ще немає") + "\n" +
            "• Наказів на мапі світу: поки вона лише показує.\n" +
            "• Ресурсів і будівництва в горах.\n" +
            "• Бігу на Shift: швидкість героя залежить від його сили й здоров'я.";

        private static string Network() =>
            Head("Як зіграти разом") + "\n" +
            "1. Хост: головне меню → «Гра в мережі» → нова кампанія або збереження. Відкривається лобі з адресою, яку він каже іншим.\n" +
            "2. Інші: головне меню → «Гра в мережі», вводять адресу й потрапляють у лобі.\n" +
            "3. У лобі новий гравець обирає клас і артефакт; хто вже має королівство в збереженні, повертається до нього.\n" +
            "4. Хост натискає «Почати», коли всі готові. Нові гравці заходять лише через лобі; ті, хто має королівство, — будь-коли.\n" +
            "5. Версія гри в усіх має бути одна.\n\n" +
            Head("У грі") + "\n" +
            $"{Key(KeyAction.PlayerList)} (утримувати) — хто в грі.\n" +
            $"{K("F6")} — пауза для всіх (3 на гру; зняти може той, хто поставив, через 30 с — будь-хто, хост — завжди).\n" +
            "• Гравець вийшов: гра на паузі, поки хост не вирішить — бот стереже його королівство, усі в лобі або чекати на нього.\n" +
            "• Зберігає гру хост. Гра гостя зберігається в нього й повертається при вході.\n" +
            "• Усі гравці вороги, кожен зі своїм королівством.";

        private static string BasicsEn() =>
            Head("First Person") + "\n" +
            $"{K("W A S D")} — move,  {K("Space")} — jump,  mouse — look,  {K(HudTips.Key(KeyAction.AutoWalk))} — auto-walk\n" +
            $"{K("LMB")} — strike or shoot\n" +
            $"{Key(KeyAction.InteractGather)} — gather, open chest, workbench, mine,  {Key(KeyAction.ChopTree)} — chop tree\n" +
            $"{Key(KeyAction.ToggleGameMode)} — switch to map view\n\n" +
            Head("Windows") + "\n" +
            $"{Key(KeyAction.Inventory)} — inventory,  {Key(KeyAction.Equipment)} — equipment,  {Key(KeyAction.Hero)} — hero\n" +
            $"{Key(KeyAction.BuildMenu)} — build menu,  {Key(KeyAction.WorkerCommandUI)} — worker assignment,  {Key(KeyAction.WorldMap)} — world map\n" +
            $"{K("F1")} — this help,  {K("F3")} — FPS,  {K("F8")} — dev editor,  {K("Esc / P")} — pause (closes windows first)\n\n" +
            Head("Getting Started") + "\n" +
            "1. Gather wood and ore and store them in a chest. Villagers also gather on their own.\n" +
            "2. Build a Town Hall: population does not grow without one.\n" +
            "3. Paint fields, housing, and mining zones: workers construct them automatically.\n" +
            "4. The advisor panel at the top right guides you step by step.\n\n" +
            Head("More") + "\n" +
            "• Hover over any button, icon, or item for a detailed tooltip.\n" +
            "• Pause → Save / Load, Settings: sensitivity, audio, graphics, keybinds, language, advisor.";

        private static string MapControlsEn() =>
            Head("Selection") + "\n" +
            $"{K("LMB")} — select a unit,  box drag — select several,  {K("Shift")} — add or remove\n" +
            $"{K("Double-click")} or {K("Ctrl+click")} — select all of the same rank on screen\n" +
            $"{K("Ctrl+A")} — all units,  {Key(KeyAction.SelectFighters)} — all fighters,  {Key(KeyAction.IdleWorker)} — next idle worker\n" +
            $"{K("Ctrl+1…0")} — assign control group,  {K("Shift+1…0")} — add to group,  {K("1…0")} — select, double-tap — focus camera\n" +
            $"{K("Esc")} — clear selection, press again — return to first person\n\n" +
            Head("Orders") + "\n" +
            $"{K("RMB")} — move, attack enemy, or work at target\n" +
            $"{K("Ctrl+RMB")} — attack-move along the way,  {K("Shift+RMB")} — queue waypoint\n" +
            $"{Key(KeyAction.StopUnits)} — stop current job or march\n" +
            $"{K("Minimap")}: LMB — move camera, RMB — move selected units\n\n" +
            Head("Camera") + "\n" +
            $"{K("RMB")} (drag) or {K("W A S D")} — pan,  {K("Wheel")} — zoom\n" +
            $"{K("Q / E")} — rotate,  {K("R / F")} — tilt,  {K("Middle mouse")} — orbit\n" +
            $"{Key(KeyAction.CenterSelection)} — center on selected,  {Key(KeyAction.AlertJump)} — jump to last attack\n\n" +
            Head("Markers") + "\n" +
            "• Ground ring: blue — allies, red — bandits, other colors — kingdoms. Diamond above head shows rank.";

        private static string BuildingEn() =>
            Head("Zones: Auto-Built by Workers") + "\n" +
            $"{Key(KeyAction.BuildMenu)} — zones and buildings. Pick a zone and drag {K("LMB")} inside the golden radius, {K("RMB")} erases, {K("Esc")} finishes.\n" +
            "• Housing boosts population growth, Fields produce food, Forest builds sawmills, Crafts builds workbenches, Mining builds mines, Trade builds markets (Town Hall II+).\n" +
            $"• \"Erase\" or {K("Delete")} — zone eraser. Buttons show completed vs planned buildings.\n\n" +
            Head("Structures: Placed Manually") + "\n" +
            $"\"Buildings\": Town Hall, Storage Chest, Walls, Gate. {K("Z / X")} — rotate, {K("LMB")} — place, {K("RMB")} — cancel.\n" +
            $"• Walls: {K("Tab")} cycles ore tier; segments snap to adjacent walls. Upgrade walls via Town Hall or wall panel.\n" +
            "• Construction stalls unless workers are assigned or in Auto mode.\n\n" +
            Head("Building Panel") + "\n" +
            $"{K("LMB")} on a building: health, workers, upgrade, open, demolish. Demolishing refunds half cost; canceling a site refunds everything unbuilt.\n\n" +
            Head("New Settlement") + "\n" +
            "From Town Hall II, place another Town Hall in unclaimed land and send workers there. Each settlement has its own stock and zones; Markets send caravans where needed.";

        private static string PeopleEn() =>
            Head("Worker Assignment") + "\n" +
            $"{Key(KeyAction.WorkerCommandUI)} — assign workers by district and job for each settlement.\n" +
            "• \"+\" calls the nearest free worker, \"−\" releases them and caps auto-assignment; click × to clear the cap.\n" +
            "• On the selection bar: job orders. \"Auto\" lets workers choose what is needed most.\n\n" +
            Head("Ranks") + "\n" +
            "Villager → Warrior → Knight → General. Villagers promote to Warrior in combat or rarely from labor; Knight & General only in combat. Everyone can work and fight.\n\n" +
            Head("Equipment") + "\n" +
            $"{Key(KeyAction.Equipment)}: select people on the map, click an item in the armory to issue one each. \"Best from Armory\" equips better armor automatically. Miners take pickaxes automatically.\n\n" +
            Head("Workbench & Mine") + "\n" +
            "• Workbench: crafters forge tier I–V gear and smelt 10 ore into 1 higher-tier ore.\n" +
            "• Mine: two dig inside, haulers carry ore to a chest. A full mine stops producing.\n" +
            "• Tools go by themselves: diggers take the best pickaxe from the store, woodcutters the axe; one moving to other work puts its tool back.\n\n" +
            Head("Food") + "\n" +
            "1 food per person per day. Without food, growth stops and villagers starve one by one. Auto workers farm when reserves drop below 3 days.";

        private static string FightEn() =>
            Head("Hero") + "\n" +
            $"{Key(KeyAction.Hero)}: XP from kills, level ups, strength & health points. Class: Tank, Archer, Mage, or DD.\n" +
            $"{Key(KeyAction.Ability)} / {Key(KeyAction.Ability2)} — class skills,  {Key(KeyAction.UseFullHeal)} / {Key(KeyAction.UseLightning)} — consumable artifacts\n\n" +
            Head("Alarm") + "\n" +
            $"{Key(KeyAction.Alarm)}: everyone drops work and arms up. Triggers automatically when enemies approach the Town Hall and stands down after battle.\n\n" +
            Head("Walls") + "\n" +
            $"{K("RMB")} on a wall: units climb stairs onto posts to strike harder (and archers shoot farther). Alarm sends archers to walls.\n\n" +
            Head("Hero Death") + "\n" +
            "Interregnum: some forces desert, then the strongest ally rises as the new hero.";

        private static string TheWorldEn() =>
            Head("World Map") + "\n" +
            $"{Key(KeyAction.WorldMap)}: terrain overview, rival kingdoms, distance, and population.\n\n" +
            Head("Neighbors") + "\n" +
            "• Rival heroes camp kilometers away. Nearby kings expand and launch raids on you and each other.\n" +
            "• Bandit camps with bows and swords, plus wild animals.\n" +
            "• Bandit raids arrive starting day 1 in growing bands; alerts tell you which direction they come from.\n\n" +
            Head("Notes") + "\n" +
            "• The global World Map is currently view-only (no direct map orders there).\n" +
            "• Resources and buildings cannot be placed on steep mountains.\n" +
            "• No Shift-sprint: hero movement speed scales with Strength and Health.";

        private static string NetworkEn() =>
            Head("How to Play Together") + "\n" +
            "1. Host: Main Menu → Multiplayer → New Campaign or From Save. Share the lobby address shown.\n" +
            "2. Guests: Main Menu → Multiplayer, enter the IP address to join the lobby.\n" +
            "3. New players pick a class and starting artifact in the lobby; returning players resume their kingdom.\n" +
            "4. Host clicks Start when ready. New players join only via lobby; returning kingdom owners can rejoin anytime.\n" +
            "5. All players must run the same game version.\n\n" +
            Head("In-Game") + "\n" +
            $"{Key(KeyAction.PlayerList)} (hold) — show player list.\n" +
            $"{K("F6")} — global pause (3 per player per match; unpaused by whoever paused, anyone after 30 s, or host anytime).\n" +
            "• On disconnect: game pauses for the host to assign a bot, return to lobby, or wait.\n" +
            "• Host saves the world. Guest hero progress saves on the host.\n" +
            "• All players are rivals, each leading their own kingdom.";

        // ------------------------------------------------------------------ the window

        private const float TextWidth = 1060f, TextHeight = 640f;

        private void Build()
        {
            UnityEngine.Canvas canvas = UiTheme.CreateCanvas("HelpCanvas", 125);
            canvas.transform.SetParent(transform, false); // survives scene loads with this object
            window = canvas.gameObject;
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, Size);
            PopIn.On(panel);
            UiTheme.PanelImage(panel);

            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(600f, 44f));
            UiTheme.Label(head, "Text", "Довідка", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-14f, -10f), new Vector2(44f, 42f), Close, 26);

            const float gap = 6f;
            float tabW = (TextWidth - gap * (TabNames.Length - 1)) / TabNames.Length, x = (Size.x - TextWidth) * 0.5f;
            tabs = new TMP_Text[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                UnityEngine.UI.Button tab = UiTheme.TextButton(panel, "Tab_" + TabNames[i], TabName(i), new Vector2(0f, 1f), new Vector2(x, -60f), new Vector2(tabW, 42f), () => ShowTab(index), 16);
                tabs[i] = tab.GetComponentInChildren<TMP_Text>();
                LocText.Detach(tabs[i]);
                x += tabW + gap;
            }

            RectTransform box = UiTheme.Rect(panel, "Text", new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(TextWidth, TextHeight));
            page = UiTheme.Label(box, "Text", "", 19, TextAnchor.UpperLeft, UiTheme.Ink);
            page.lineSpacing = 12f; // (TextMeshPro: extra, in hundredths of an em)
            page.richText = true;
            page.textWrappingMode = TextWrappingModes.Normal; // lines wrap in the column, never out of the window
            page.overflowMode = TextOverflowModes.Overflow;
        }

        /// <summary>Shows this tab, with the player's keys as they are now.</summary>
        public void ShowTab(int index)
        {
            CurrentTab = Mathf.Clamp(index, 0, TabNames.Length - 1);
            page.text = TabText(CurrentTab);
            for (int i = 0; i < tabs.Length; i++) tabs[i].color = i == CurrentTab ? UiTheme.Gold : UiTheme.Ink;
        }

        /// <summary>How high the tab on show is against the room for it (tests).</summary>
        public float PageHeight => page.preferredHeight;
        public string PageText => page.text;
        public static float PageRoom => TextHeight;
    }
}
