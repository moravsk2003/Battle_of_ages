using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The help window (F1): the controls of both views with the player's own key bindings, what the game has and what it
    /// does not have yet (a player report: "the interface is unclear, it is unclear what there is"). Closes with F1, Esc
    /// or the cross; the top bar's mode hint tells about it.
    /// </summary>
    public sealed class HelpUI : MonoBehaviour
    {
        private static HelpUI instance;
        public static bool IsVisible => instance != null && instance.open;
        public static HelpUI Instance => instance;

        private GameObject window;
        private UnityEngine.UI.Text controls, features;
        private bool open;

        private static readonly Vector2 Size = new Vector2(1420f, 860f);

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
            controls.text = ControlsText();
            features.text = FeaturesText();
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

        private static string ControlsText()
        {
            return Head("Від першої особи") + "\n" +
                   $"{K("W A S D")} — рух,  {K("Пробіл")} — стрибок,  миша — огляд\n" +
                   $"{K("ЛКМ")} — удар або постріл\n" +
                   $"{Key(KeyAction.InteractGather)} — зібрати, відкрити скриню, верстак, шахту\n" +
                   $"{Key(KeyAction.ChopTree)} — рубати дерево\n" +
                   $"{Key(KeyAction.Ability)} / {Key(KeyAction.Ability2)} — уміння класу героя,  {Key(KeyAction.UseFullHeal)} / {Key(KeyAction.UseLightning)} — одноразові артефакти\n" +
                   $"{Key(KeyAction.Inventory)} — інвентар,  {Key(KeyAction.Equipment)} — спорядження,  {Key(KeyAction.Hero)} — герой\n" +
                   $"{Key(KeyAction.BuildMenu)} — будівництво,  {Key(KeyAction.WorkerCommandUI)} — розподіл робіт\n" +
                   $"{Key(KeyAction.Alarm)} — тривога: усі люди до зброї\n" +
                   $"{Key(KeyAction.ToggleGameMode)} — мапа (вид зверху)\n\n" +
                   Head("На мапі") + "\n" +
                   $"{K("ЛКМ")} — вибрати людину, рамкою — кількох\n" +
                   $"{K("ПКМ")} з вибраними — іти туди / бити ворога / працювати\n" +
                   $"{K("ПКМ")} без вибраних — тягти мапу,  {K("W A S D")} — рухати мапу\n" +
                   $"{K("Колесо")} — наближення,  {K("Q / E")} — повернути,  {K("R / F")} — нахил\n" +
                   $"{K("Середня кнопка")} — крутити камеру\n" +
                   $"{Key(KeyAction.IdleWorker)} — наступний робітник без діла,  {Key(KeyAction.AlertJump)} — до місця нападу\n" +
                   $"{K("Мінімапа")}: ЛКМ — камера туди, ПКМ — вибрані йдуть туди\n" +
                   $"{K("Esc")} — назад до виду від першої особи\n\n" +
                   Head("Завжди") + "\n" +
                   $"{K("F1")} — ця довідка,  {K("F3")} — FPS,  {K("Esc / P")} — пауза (спершу закриває вікна)\n" +
                   "Наведи курсор на будь-що — з'явиться підказка. Пауза → Налаштування: миша, зум, гучність, графіка, " +
                   "масштаб інтерфейсу, екран, VSync, ліміт кадрів, клавіші, поради.";
        }

        private static string FeaturesText()
        {
            return Head("Що є в грі") + "\n" +
                   $"• Збирай дерево й руду, неси в скриню. Люди з будинків працюють самі; {Key(KeyAction.WorkerCommandUI)} — хто що робить.\n" +
                   $"• Будівлі ({Key(KeyAction.BuildMenu)}): ратуша (рівні I–V — більше людей), будинок, скриня, шахта (люди працюють усередині, носії везуть руду в скриню), верстак (зброя й броня I–V), стіни й ворота (руда на вибір — Tab, Z / X — поворот, сегмент прилипає до краю стіни; рівень стін — під Ратушею). ПКМ по стіні — юніти йдуть сходами на пости й б'ють звідти сильніше (лучники — далі); тривога шле туди лучників.\n" +
                   $"• Герой ({Key(KeyAction.Hero)}): досвід за вбивства, рівні, очки сили й здоров'я; клас — танк, лучник, маг або ДД з уміннями на {Key(KeyAction.Ability)} / {Key(KeyAction.Ability2)}.\n" +
                   "• Люди і працюють, і б'ються: людина → воїн → лицар → генерал. Воїном стають у боях і зрідка від роботи, лицарем і генералом — лише в боях (удари, вбивства, пережиті бої). Зброя й броня — окремо, з верстака.\n" +
                   $"• Спорядження ({Key(KeyAction.Equipment)}): виділи людей на мапі — клік по речі на складі дає по одній кожному; «Найкраще зі складу» — усім кращу броню. Кайло шахтарі беруть самі.\n" +
                   "• Верстак: робітники біля нього (ПКМ по верстаку з виділеними) кують речі I–V; переплавка — 10 руди → 1 руда рівнем вище.\n" +
                   "• На мапі під кожним кільце: сині — свої, червоні — бандити, інші кольори — королівства; ромб над головою — ранг.\n" +
                   "• Сусіди: герої на своїх стоянках за кілька км, найближчі королі ростуть і ходять у набіги на тебе й один на одного; табори бандитів із луками й мечами; дикі звірі.\n" +
                   "• Бандити приходять на поселення від першого дня: щоразу більша ватага, напис скаже, звідки. Тривога кличе людей до зброї.\n" +
                   $"• Тривога ({Key(KeyAction.Alarm)}): люди кидають роботу й захищають дім; сама вмикається, коли ворог підходить до ратуші, і вимикається після бою.\n" +
                   "• Смерть героя: міжцарів'я, частина людей тікає, новим героєм стає найсильніший.\n" +
                   "• Збереження: Пауза → Зберегти / Завантажити.\n\n" +
                   Head("Чого ще немає") + "\n" +
                   "• Їжі й полів, торгівлі, мультиплеєра. Навчання — лише поради «Перші кроки».\n" +
                   "• Ресурсів і будівництва в горах.\n" +
                   "• Бігу на Shift: швидкість героя залежить від його сили й здоров'я.";
        }

        private void Build()
        {
            UnityEngine.Canvas canvas = UiTheme.CreateCanvas("HelpCanvas", 125);
            canvas.transform.SetParent(transform, false); // survives scene loads with this object
            window = canvas.gameObject;
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, Size);
            PopIn.On(panel);
            UiTheme.PanelImage(panel);

            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 48f));
            UiTheme.Divider(panel, -64f, 600f);
            UiTheme.Label(head, "Text", "Довідка", 32, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), Close, 28);

            RectTransform left = UiTheme.Rect(panel, "Controls", new Vector2(0f, 1f), new Vector2(34f, -80f), new Vector2(640f, 740f));
            controls = UiTheme.Label(left, "Text", "", 18, TextAnchor.UpperLeft, UiTheme.Ink);
            controls.lineSpacing = 1.15f;
            controls.horizontalOverflow = HorizontalWrapMode.Wrap; // lines wrap in the column, never out of the window
            RectTransform right = UiTheme.Rect(panel, "Features", new Vector2(1f, 1f), new Vector2(-34f, -80f), new Vector2(700f, 740f));
            features = UiTheme.Label(right, "Text", "", 18, TextAnchor.UpperLeft, UiTheme.Ink);
            features.lineSpacing = 1.15f;
            features.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
