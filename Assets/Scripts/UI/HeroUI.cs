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
        private readonly Button[] skillButtons = new Button[4];
        private Text artifactText;
        private Button pickPrev, pickNext, pickTake, useHeal, useLightning, combine;
        private int pickIndex;
        private HeroArtifacts bag;
        private HeroProgress hero;
        private bool open, dirty;

        private static readonly Vector2 Size = new Vector2(560f, 610f);

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
            if (bag != null) bag.Changed -= OnBagChanged;
            bag = hero != null ? hero.GetComponent<HeroArtifacts>() : null;
            if (bag != null) bag.Changed += OnBagChanged;
        }

        private void OnHeroChanged(HeroProgress h) => dirty = true;
        private void OnBagChanged(HeroArtifacts b) => dirty = true;

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
            RefreshSkills();
            RefreshArtifacts();
        }


        /// <summary>The class skills that can be learnt now, one button each (docs/GAME_DESIGN.md §8.1).</summary>
        private void RefreshSkills()
        {
            HeroSkills s = hero.GetComponent<HeroSkills>();
            int used = 0;
            if (s != null)
            {
                switch (hero.Class)
                {
                    case HeroClass.Tank:
                        if (!s.RegenStart) SkillButton(ref used, "Регенерація (1)", s.LearnRegenStart);
                        else
                        {
                            SkillButton(ref used, $"Реген +1% макс ({s.RegenMaxShare * 100f:0}%)", s.LearnRegenMax);
                            SkillButton(ref used, $"Реген +2% пот. ({s.RegenCurrentShare * 100f:0}%)", s.LearnRegenCurrent);
                        }
                        if (s.HealLevel < GameBalance.HealMaxLevel) SkillButton(ref used, $"Відхіл {s.HealLevel + 1}/{GameBalance.HealMaxLevel}", s.LearnHeal);
                        break;
                    case HeroClass.Archer:
                        if (!s.LongShot) SkillButton(ref used, $"Далекий постріл ({GameBalance.LongShotCost})", s.LearnLongShot);
                        break;
                    case HeroClass.Mage:
                        if (!s.FireFan) SkillButton(ref used, "Вогняне віяло (1)", s.LearnFireFan);
                        break;
                    case HeroClass.Dd:
                        if (s.DashLevel < GameBalance.DashMaxLevel) SkillButton(ref used, $"Ривок ×{s.DashFactor + 1f:0}", s.LearnDash);
                        break;
                }
            }
            for (int i = used; i < skillButtons.Length; i++) skillButtons[i].gameObject.SetActive(false);
        }

        private void SkillButton(ref int index, string label, System.Func<bool> learn)
        {
            if (index >= skillButtons.Length) return;
            Button b = skillButtons[index++];
            b.gameObject.SetActive(true);
            b.GetComponentInChildren<Text>().text = label;
            b.interactable = hero.SkillPoints > 0;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => learn());
        }

        /// <summary>Artifacts (docs/GAME_DESIGN.md §9): the starting pick, what is held, one-shots, shards.</summary>
        private void RefreshArtifacts()
        {
            bool has = bag != null;
            bool picking = has && !bag.StartChosen;
            pickPrev.gameObject.SetActive(picking); pickNext.gameObject.SetActive(picking); pickTake.gameObject.SetActive(picking);
            if (!has) { artifactText.text = ""; return; }
            if (picking)
            {
                ArtifactType a = Artifacts.All[pickIndex];
                artifactText.text = $"Стартовий артефакт: <b>{Artifacts.Name(a)}</b>\n{Artifacts.Description(a)}";
            }
            else
            {
                var names = new System.Text.StringBuilder();
                foreach (ArtifactType a in bag.Held) names.Append(names.Length > 0 ? ", " : "").Append(Artifacts.Name(a));
                artifactText.text = $"Артефакти: {(names.Length > 0 ? names.ToString() : "немає")}\nОсколки: {bag.Shards.Count}";
            }
            useHeal.gameObject.SetActive(!picking && bag.Has(ArtifactType.FullHeal));
            useLightning.gameObject.SetActive(!picking && bag.Has(ArtifactType.Lightning));
            combine.gameObject.SetActive(!picking && bag.Shards.Count >= 2);
        }

        private void StepPick(int d)
        {
            pickIndex = (pickIndex + d + Artifacts.All.Length) % Artifacts.All.Length;
            dirty = true;
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
            canvas.transform.SetParent(transform, false); // survives scene loads with this object
            window = canvas.gameObject;
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, Size);
            UiTheme.PanelImage(panel);

            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(460f, 44f));
            header = UiTheme.Label(head, "Text", "Герой", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), Close, 28);

            RectTransform bar = UiTheme.Rect(panel, "XpBar", new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(500f, 18f));
            UiTheme.PanelImage(bar, UiTheme.BarSprite, new Color(0f, 0f, 0f, 0.55f));
            RectTransform fill = UiTheme.Child(bar, "Fill", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
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
            // Artifacts: the starting pick (once), one-shots and shards
            RectTransform art = UiTheme.Rect(panel, "Artifacts", new Vector2(0.5f, 0f), new Vector2(0f, 262f), new Vector2(500f, 62f));
            artifactText = UiTheme.Label(art, "Text", "", 17, TextAnchor.MiddleLeft, UiTheme.Ink);
            artifactText.supportRichText = true;
            pickPrev = UiTheme.TextButton(panel, "PickPrev", "‹", new Vector2(0f, 0f), new Vector2(30f, 206f), new Vector2(50f, 44f), () => StepPick(-1), 26);
            pickNext = UiTheme.TextButton(panel, "PickNext", "›", new Vector2(0f, 0f), new Vector2(90f, 206f), new Vector2(50f, 44f), () => StepPick(1), 26);
            pickTake = UiTheme.TextButton(panel, "PickTake", "Взяти", new Vector2(0f, 0f), new Vector2(150f, 206f), new Vector2(140f, 44f),
                () => { if (bag != null) bag.ChooseStart(Artifacts.All[pickIndex]); }, 19);
            useHeal = UiTheme.TextButton(panel, "UseHeal", "Зцілитися", new Vector2(0f, 0f), new Vector2(30f, 206f), new Vector2(150f, 44f),
                () => { if (bag != null) bag.Use(ArtifactType.FullHeal); }, 17);
            useLightning = UiTheme.TextButton(panel, "UseLightning", "Блискавка", new Vector2(0f, 0f), new Vector2(190f, 206f), new Vector2(150f, 44f),
                () => { if (bag != null) bag.Use(ArtifactType.Lightning); }, 17);
            combine = UiTheme.TextButton(panel, "Combine", "Об'єднати осколки", new Vector2(0f, 0f), new Vector2(350f, 206f), new Vector2(180f, 44f),
                () => { if (bag != null) bag.CombineShards(); }, 15);

            // Class skills share the row with the class picker (shown once the class is chosen)
            for (int i = 0; i < skillButtons.Length; i++)
                skillButtons[i] = UiTheme.TextButton(panel, "Skill" + i, "", new Vector2(0f, 0f), new Vector2(20f + i * 130f, 150f), new Vector2(124f, 46f), null, 14);

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
