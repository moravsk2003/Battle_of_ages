using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;

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
        private UnityEngine.UI.Text page;
        private UnityEngine.UI.Text[] tabs;
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
        }

        private void OnDestroy() { if (instance == this) instance = null; }

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

        /// <summary>A tab's text, with the player's own keys.</summary>
        public static string TabText(int index)
        {
            switch (index)
            {
                case 0: return Basics();
                case 1: return MapControls();
                case 2: return Building();
                case 3: return People();
                case 4: return Fight();
                case 5: return TheWorld();
                default: return Network();
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
            $"{K("F1")} — ця довідка,  {K("F3")} — FPS,  {K("Esc / P")} — пауза (спершу закриває вікна)\n\n" +
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
            "• Шахта: люди копають усередині, носії несуть руду в скриню. Повна шахта стоїть.\n\n" +
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
            "1. Хост починає гру, потім Пауза → «Відкрити гру в мережі» й каже іншим адресу.\n" +
            "2. Інші: головне меню → «Приєднатися до гри», вводять адресу.\n" +
            "3. Версія гри в усіх має бути одна.\n\n" +
            Head("У грі") + "\n" +
            $"{Key(KeyAction.PlayerList)} (утримувати) — хто в грі.\n" +
            "• Зберігає гру хост. Гра гостя зберігається в нього й повертається при вході.\n" +
            "• Усі гравці вороги, кожен зі своїм королівством.";

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
            tabs = new UnityEngine.UI.Text[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                UnityEngine.UI.Button tab = UiTheme.TextButton(panel, "Tab_" + TabNames[i], TabNames[i], new Vector2(0f, 1f), new Vector2(x, -60f), new Vector2(tabW, 42f), () => ShowTab(index), 16);
                tabs[i] = tab.GetComponentInChildren<UnityEngine.UI.Text>();
                x += tabW + gap;
            }

            RectTransform box = UiTheme.Rect(panel, "Text", new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(TextWidth, TextHeight));
            page = UiTheme.Label(box, "Text", "", 19, TextAnchor.UpperLeft, UiTheme.Ink);
            page.lineSpacing = 1.12f;
            page.supportRichText = true;
            page.horizontalOverflow = HorizontalWrapMode.Wrap; // lines wrap in the column, never out of the window
            page.verticalOverflow = VerticalWrapMode.Overflow;
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
