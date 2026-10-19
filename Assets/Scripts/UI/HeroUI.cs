using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Player;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The hero window (H): level, experience to the next level, rank and stats, and the free points
    /// (an attribute point buys +5 strength or +50 HP; docs/GAME_DESIGN.md §8).
    /// </summary>
    public sealed class HeroUI : MonoBehaviour
    {
        private static HeroUI instance;
        public static bool IsVisible => instance != null && instance.open;
        public static HeroUI Instance => instance;

        private GameObject window;
        private Text header, body, points;
        private Image xpFill;
        private Button strengthButton, healthButton;
        private GameObject classPicker;
        private HeroProgress hero;
        private bool open, dirty;

        private static readonly Vector2 Size = new Vector2(560f, 470f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("HeroUI").AddComponent<HeroUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            window.SetActive(false);
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy() { if (instance == this) instance = null; Watch(null); }

        public void Open()
        {
            var player = FindFirstObjectByType<PlayerCombatController>();
            Watch(player != null ? player.GetComponent<HeroProgress>() : null);
            if (hero == null) return;
            open = true;
            window.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }

        public void Close()
        {
            open = false;
            window.SetActive(false);
            GameModeManager modes = FindFirstObjectByType<GameModeManager>();
            if (!GameWindows.AnyModalOpen && !GameWindows.Paused && (modes == null || modes.CurrentMode == GameMode.FirstPerson))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Watch(HeroProgress h)
        {
            if (hero != null) hero.Changed -= OnHeroChanged;
            hero = h;
            if (hero != null) hero.Changed += OnHeroChanged;
        }

        private void OnHeroChanged(HeroProgress h) => dirty = true;

        private void Update()
        {
            if (Time.frameCount < 2) return;
            if (open)
            {
                if (hero == null) { Close(); return; }
                if (UniversalInput.GetKeyDownEscape() || KeybindsManager.GetKeyDown(KeyAction.Hero)) { Close(); return; }
                if (dirty) Refresh();
                return;
            }
            if (GameWindows.Paused || GameWindows.AnyModalOpen) return;
            if (KeybindsManager.GetKeyDown(KeyAction.Hero)) Open();
        }

        private void Refresh()
        {
            dirty = false;
            if (hero == null) return;
            UnitProgress rank = hero.GetComponent<UnitProgress>();
            HealthComponent hp = hero.GetComponent<HealthComponent>();
            CombatUnit combat = hero.GetComponent<CombatUnit>();

            header.text = $"Герой · рівень {hero.Level}";
            xpFill.fillAmount = hero.XpToNextLevel > 0 ? (float)hero.Xp / hero.XpToNextLevel : 0f;
            body.text =
                $"Досвід: {hero.Xp} / {hero.XpToNextLevel}\n" +
                $"Тип: {RankName(rank != null ? rank.Rank : UnitRank.General)}   Клас: {HeroClassRules.Name(hero.Class)}\n" +
                $"HP: {(hp != null ? hp.CurrentHealth : 0)} / {(hp != null ? hp.MaxHealth : 0)}   (вкладено: {hero.HealthPoints} × 50)\n" +
                $"Сила: {(combat != null ? combat.Strength : 0f):0}   (вкладено: {hero.StrengthPoints} × 5)\n" +
                $"Урон: {(combat != null ? combat.AttackDamage : 0)}   Швидкість: {(combat != null ? combat.MoveSpeed : 0f):0.0} м/с";
            points.text = $"Очки характеристик: {hero.AttributePoints}     Очки навиків: {hero.SkillPoints}";
            strengthButton.interactable = healthButton.interactable = hero.AttributePoints > 0;
            classPicker.SetActive(hero.Class == HeroClass.None);
        }

        public static string RankName(UnitRank rank)
        {
            switch (rank)
            {
                case UnitRank.Person: return "людина";
                case UnitRank.Warrior: return "воїн";
                case UnitRank.Knight: return "рицар";
                default: return "генерал";
            }
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("HeroCanvas", 121);
            window = canvas.gameObject;
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, Size);
            UiTheme.PanelImage(panel);

            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(460f, 44f));
            header = UiTheme.Label(head, "Text", "Герой", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), Close, 28);

            RectTransform bar = UiTheme.Rect(panel, "XpBar", new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(500f, 18f));
            UiTheme.PanelImage(bar, UiTheme.BarSprite, new Color(0f, 0f, 0f, 0.55f));
            RectTransform fill = UiTheme.Child(bar, "Fill", Vector2.zero, Vector2.one);
            xpFill = fill.gameObject.AddComponent<Image>();
            xpFill.sprite = UiTheme.BarSprite;
            xpFill.type = Image.Type.Filled;
            xpFill.fillMethod = Image.FillMethod.Horizontal;
            xpFill.color = UiTheme.Gold;
            xpFill.raycastTarget = false;

            RectTransform text = UiTheme.Rect(panel, "Body", new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(500f, 190f));
            body = UiTheme.Label(text, "Text", "", 19, TextAnchor.UpperLeft, UiTheme.Ink);

            RectTransform pts = UiTheme.Rect(panel, "Points", new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(500f, 36f));
            points = UiTheme.Label(pts, "Text", "", 19, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);

            strengthButton = UiTheme.TextButton(panel, "Strength", "+5 сили", new Vector2(0.5f, 0f), new Vector2(-130f, 28f), new Vector2(230f, 54f),
                () => { if (hero != null) hero.SpendOnStrength(); }, 22);
            healthButton = UiTheme.TextButton(panel, "Health", "+50 HP", new Vector2(0.5f, 0f), new Vector2(130f, 28f), new Vector2(230f, 54f),
                () => { if (hero != null) hero.SpendOnHealth(); }, 22);
            // The class is picked once (docs/GAME_DESIGN.md §8.1)
            RectTransform pick = UiTheme.Rect(panel, "ClassPicker", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(520f, 50f));
            classPicker = pick.gameObject;
            HeroClass[] classes = { HeroClass.Tank, HeroClass.Archer, HeroClass.Mage, HeroClass.Dd };
            for (int i = 0; i < classes.Length; i++)
            {
                HeroClass c = classes[i];
                UiTheme.TextButton(pick, "Class" + c, HeroClassRules.Name(c), new Vector2(0f, 0.5f), new Vector2(10f + i * 128f, 0f), new Vector2(120f, 46f),
                    () => { if (hero != null) hero.ChooseClass(c); }, 19);
            }

        }
    }
}
