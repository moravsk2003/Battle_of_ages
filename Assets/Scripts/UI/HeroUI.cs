using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Net;
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
        private readonly Button[] classButtons = new Button[4];
        private static readonly HeroClass[] ClassOrder = { HeroClass.Tank, HeroClass.Archer, HeroClass.Mage, HeroClass.Dd };
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
            Loc.Changed += OnLanguageChanged;
        }

        private void OnDestroy()
        {
            Loc.Changed -= OnLanguageChanged;
            if (instance == this) instance = null;
            Watch(null);
        }

        private void OnLanguageChanged()
        {
            dirty = true;
            RefreshClassPickerLabels();
            if (open) Refresh();
        }

        private void RefreshClassPickerLabels()
        {
            for (int i = 0; i < classButtons.Length; i++)
            {
                if (classButtons[i] == null) continue;
                Text t = classButtons[i].GetComponentInChildren<Text>();
                if (t != null) t.text = HeroClassRules.Name(ClassOrder[i]);
            }
        }

        public void Open()
        {
            var player = Managers.GameContext.PlayerCombat;
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
            GameModeManager modes = Managers.GameContext.Modes;
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

            header.text = Loc.Pick($"Герой · рівень {hero.Level}", $"Hero · Level {hero.Level}");
            xpFill.fillAmount = hero.XpToNextLevel > 0 ? (float)hero.Xp / hero.XpToNextLevel : 0f;
            body.text = Loc.Pick(
                $"Досвід: {hero.Xp} / {hero.XpToNextLevel}\n" +
                $"Тип: {RankName(rank != null ? rank.Rank : UnitRank.General)}   Клас: {HeroClassRules.Name(hero.Class)}\n" +
                $"HP: {(hp != null ? hp.CurrentHealth : 0)} / {(hp != null ? hp.MaxHealth : 0)}   (вкладено: {hero.HealthPoints} × 50)\n" +
                $"Сила: {(combat != null ? combat.Strength : 0f):0}   (вкладено: {hero.StrengthPoints} × 5)\n" +
                $"Шкода: {(combat != null ? combat.AttackDamage : 0)}   Швидкість: {(combat != null ? combat.MoveSpeed : 0f):0.0} м/с",
                $"XP: {hero.Xp} / {hero.XpToNextLevel}\n" +
                $"Rank: {RankName(rank != null ? rank.Rank : UnitRank.General)}   Class: {HeroClassRules.Name(hero.Class)}\n" +
                $"HP: {(hp != null ? hp.CurrentHealth : 0)} / {(hp != null ? hp.MaxHealth : 0)}   (invested: {hero.HealthPoints} × 50)\n" +
                $"Strength: {(combat != null ? combat.Strength : 0f):0}   (invested: {hero.StrengthPoints} × 5)\n" +
                $"Damage: {(combat != null ? combat.AttackDamage : 0)}   Speed: {(combat != null ? combat.MoveSpeed : 0f):0.0} m/s");
            points.text = Loc.Pick(
                $"Очки характеристик: {hero.AttributePoints}     Очки навиків: {hero.SkillPoints}",
                $"Attribute Points: {hero.AttributePoints}     Skill Points: {hero.SkillPoints}");
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
                        if (!s.RegenStart) SkillButton(ref used, Loc.Pick("Регенерація (1)", "Regeneration (1)"), "RegenStart",
                            Loc.Pick("Старт гілки: +1% макс. і +2% поточного HP за хвилину, і в бою.", "Branch start: +1% max and +2% current HP per minute, even in combat."));
                        else
                        {
                            SkillButton(ref used, Loc.Pick($"Реген +1% макс ({s.RegenMaxShare * 100f:0}%)", $"Regen +1% Max ({s.RegenMaxShare * 100f:0}%)"), "RegenMax",
                                Loc.Pick("Краще для тих, кого добивають до низу.", "Better when dropping low in HP."));
                            SkillButton(ref used, Loc.Pick($"Реген +2% пот. ({s.RegenCurrentShare * 100f:0}%)", $"Regen +2% Cur ({s.RegenCurrentShare * 100f:0}%)"), "RegenCurrent",
                                Loc.Pick("Краще для тих, хто тримається вище половини HP.", "Better when staying above half HP."));
                        }
                        if (s.HealLevel < GameBalance.HealMaxLevel && s.CanLearnHeal) SkillButton(ref used, Loc.Pick($"Відхіл {s.HealLevel + 1}/{GameBalance.HealMaxLevel}", $"Heal {s.HealLevel + 1}/{GameBalance.HealMaxLevel}"), "Heal",
                            Loc.Pick("Уміння Q: миттєво лікує частку макс. HP (5% і +4% за очко, до 25%).", "Skill Q: instantly heals a share of max HP (5% + 4% per point, up to 25%)."));
                        break;
                    case HeroClass.Archer:
                        if (!s.LongShot) SkillButton(ref used, Loc.Pick($"Далекий постріл ({GameBalance.LongShotCost})", $"Long Shot ({GameBalance.LongShotCost})"), "LongShot",
                            Loc.Pick("Пасивно: +5% дальності стрільби.", "Passive: +5% shooting range."));
                        else if (!s.PiercingArrow) SkillButton(ref used, Loc.Pick("Пробивна стріла (1)", "Piercing Arrow (1)"), "Pierce",
                            Loc.Pick("Уміння Q: наступна стріла б'є на 200% і летить наскрізь.", "Skill Q: next arrow deals 200% damage and pierces through."));
                        break;
                    case HeroClass.Mage:
                        if (!s.FireFan) SkillButton(ref used, Loc.Pick("Вогняне віяло (1)", "Fire Fan (1)"), "FireFan",
                            Loc.Pick("Уміння Q: віяло вогню по зоні перед собою, крізь броню.", "Skill Q: cone of fire ahead, ignoring armor."));
                        else if (!s.Meteor) SkillButton(ref used, Loc.Pick("Метеор (1)", "Meteor (1)"), "Meteor",
                            Loc.Pick("Уміння R: метеор на найближчого ворога, 5× шкода мага в радіусі 2,5 м.", "Skill R: drops a meteor on the nearest enemy for 5× mage damage in 2.5 m radius."));
                        break;
                    case HeroClass.Dd:
                        if (s.DashLevel < GameBalance.DashMaxLevel) SkillButton(ref used, Loc.Pick($"Ривок ×{s.DashFactor + 1f:0}", $"Dash ×{s.DashFactor + 1f:0}"), "Dash",
                            Loc.Pick("Уміння Q: на 1 с біг у кілька разів швидший; перезарядка 20 с.", "Skill Q: multiply movement speed for 1 s; 20 s cooldown."));
                        if (s.DashLevel > 0 && !s.CriticalHit) SkillButton(ref used, Loc.Pick("Критичний удар (1)", "Critical Strike (1)"), "CriticalHit",
                            Loc.Pick("Уміння R: наступний удар подвійний; перезарядка 40 с.", "Skill R: next hit deals double damage; 40 s cooldown."));
                        break;
                }
            }
            for (int i = used; i < skillButtons.Length; i++) skillButtons[i].gameObject.SetActive(false);
        }

        /// <summary>What the player does with its hero goes as an order (Net.HeroCommand).</summary>
        private static void Hero(HeroCommand.Act what, int value = 0, string skill = null) =>
            CommandBus.Submit(new HeroCommand { What = what, Value = value, Skill = skill });

        private void SkillButton(ref int index, string label, string skill, string tip)
        {
            if (index >= skillButtons.Length) return;
            Button b = skillButtons[index++];
            Tooltip.Set(b.gameObject, () => TipText.Title(label, UiTheme.Gold) + "\n" + tip + "\n" + TipText.Dim(hero != null && hero.SkillPoints > 0 ? Loc.Pick("Клік — вивчити за очко навику", "Click — learn for 1 skill point") : Loc.Pick("Немає очок навику: вони даються з рівнем", "No skill points: gained on level up")));
            b.gameObject.SetActive(true);
            b.GetComponentInChildren<Text>().text = label;
            b.interactable = hero.SkillPoints > 0;
            b.onClick.RemoveAllListeners();
            b.onClick.AddListener(() => Hero(HeroCommand.Act.LearnSkill, 0, skill));
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
                artifactText.text = Loc.Pick(
                    $"Стартовий артефакт: <b>{Artifacts.Name(a, hero != null ? hero.Class : HeroClass.None)}</b>\n{Artifacts.Description(a)}",
                    $"Starting Artifact: <b>{Artifacts.Name(a, hero != null ? hero.Class : HeroClass.None)}</b>\n{Artifacts.Description(a)}");
            }
            else
            {
                var names = new System.Text.StringBuilder();
                foreach (ArtifactType a in bag.Held) names.Append(names.Length > 0 ? ", " : "").Append(Artifacts.Name(a, hero != null ? hero.Class : HeroClass.None));
                artifactText.text = Loc.Pick(
                    $"Артефакти: {(names.Length > 0 ? names.ToString() : "немає")}\nОсколки: {bag.Shards.Count}",
                    $"Artifacts: {(names.Length > 0 ? names.ToString() : "none")}\nShards: {bag.Shards.Count}");
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
                case UnitRank.Person: return Loc.Pick("людина", "villager");
                case UnitRank.Warrior: return Loc.Pick("воїн", "warrior");
                case UnitRank.Knight: return Loc.Pick("лицар", "knight");
                default: return Loc.Pick("генерал", "general");
            }
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("HeroCanvas", 121);
            canvas.transform.SetParent(transform, false); // survives scene loads with this object
            window = canvas.gameObject;
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, Size);
            PopIn.On(panel);
            UiTheme.PanelImage(panel);

            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(460f, 44f));
            UiTheme.Divider(panel, -58f, 380f);
            header = UiTheme.Label(head, "Text", "Герой", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            LocText.Detach(header);
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
            LocText.Detach(body);

            RectTransform pts = UiTheme.Rect(panel, "Points", new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(500f, 36f));
            points = UiTheme.Label(pts, "Text", "", 19, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            LocText.Detach(points);

            strengthButton = UiTheme.TextButton(panel, "Strength", "+5 сили", new Vector2(0.5f, 0f), new Vector2(-130f, 28f), new Vector2(230f, 54f),
                () => Hero(HeroCommand.Act.Strength), 22);
            healthButton = UiTheme.TextButton(panel, "Health", "+50 HP", new Vector2(0.5f, 0f), new Vector2(130f, 28f), new Vector2(230f, 54f),
                () => Hero(HeroCommand.Act.Health), 22);
            Tooltip.Set(strengthButton.gameObject, () => TipText.Title(Loc.Pick("+5 сили", "+5 Strength"), UiTheme.Gold)
                + Loc.Pick("\nСила — шкода удару; сила разом зі здоров'ям — швидкість бігу.", "\nStrength increases hit damage; combined with health, it increases run speed.")
                + "\n" + TipText.Dim(Loc.Pick("За очко характеристик; очки даються з рівнем.", "Costs 1 attribute point; earned on level up.")));
            Tooltip.Set(healthButton.gameObject, () => TipText.Title("+50 HP", UiTheme.Gold)
                + Loc.Pick("\nДо найбільшого здоров'я героя; разом із силою — швидкість бігу.", "\nIncreases hero max health; combined with strength, increases run speed.")
                + "\n" + TipText.Dim(Loc.Pick("За очко характеристик; очки даються з рівнем.", "Costs 1 attribute point; earned on level up.")));
            // Artifacts: the starting pick (once), one-shots and shards
            RectTransform art = UiTheme.Rect(panel, "Artifacts", new Vector2(0.5f, 0f), new Vector2(0f, 262f), new Vector2(500f, 62f));
            artifactText = UiTheme.Label(art, "Text", "", 17, TextAnchor.MiddleLeft, UiTheme.Ink);
            LocText.Detach(artifactText);
            artifactText.supportRichText = true;
            pickPrev = UiTheme.TextButton(panel, "PickPrev", "‹", new Vector2(0f, 0f), new Vector2(30f, 206f), new Vector2(50f, 44f), () => StepPick(-1), 26);
            pickNext = UiTheme.TextButton(panel, "PickNext", "›", new Vector2(0f, 0f), new Vector2(90f, 206f), new Vector2(50f, 44f), () => StepPick(1), 26);
            pickTake = UiTheme.TextButton(panel, "PickTake", "Взяти", new Vector2(0f, 0f), new Vector2(150f, 206f), new Vector2(140f, 44f),
                () => Hero(HeroCommand.Act.ChooseArtifact, (int)Artifacts.All[pickIndex]), 19);
            useHeal = UiTheme.TextButton(panel, "UseHeal", "Зцілитися", new Vector2(0f, 0f), new Vector2(30f, 206f), new Vector2(150f, 44f),
                () => Hero(HeroCommand.Act.UseArtifact, (int)ArtifactType.FullHeal), 17);
            Tooltip.Set(useHeal.gameObject, () => TipText.Title(Artifacts.Name(ArtifactType.FullHeal), UiTheme.Gold) + "\n" + Artifacts.Description(ArtifactType.FullHeal));
            useLightning = UiTheme.TextButton(panel, "UseLightning", "Блискавка", new Vector2(0f, 0f), new Vector2(190f, 206f), new Vector2(150f, 44f),
                () => Hero(HeroCommand.Act.UseArtifact, (int)ArtifactType.Lightning), 17);
            Tooltip.Set(useLightning.gameObject, () => TipText.Title(Artifacts.Name(ArtifactType.Lightning), UiTheme.Gold) + "\n" + Artifacts.Description(ArtifactType.Lightning));
            combine = UiTheme.TextButton(panel, "Combine", "Об'єднати осколки", new Vector2(0f, 0f), new Vector2(350f, 206f), new Vector2(180f, 44f),
                () => Hero(HeroCommand.Act.CombineShards), 15);

            // Class skills share the row with the class picker (shown once the class is chosen)
            for (int i = 0; i < skillButtons.Length; i++)
            {
                skillButtons[i] = UiTheme.TextButton(panel, "Skill" + i, "", new Vector2(0f, 0f), new Vector2(20f + i * 130f, 150f), new Vector2(124f, 46f), null, 14);
                LocText.Detach(skillButtons[i].GetComponentInChildren<Text>());
            }

            // The class is picked once (docs/GAME_DESIGN.md §8.1)
            RectTransform pick = UiTheme.Rect(panel, "ClassPicker", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(520f, 50f));
            classPicker = pick.gameObject;
            for (int i = 0; i < ClassOrder.Length; i++)
            {
                HeroClass c = ClassOrder[i];
                Button cb = UiTheme.TextButton(pick, "Class" + c, HeroClassRules.Name(c), new Vector2(0f, 0.5f), new Vector2(10f + i * 128f, 0f), new Vector2(120f, 46f),
                    () => Hero(HeroCommand.Act.ChooseClass, (int)c), 19);
                LocText.Detach(cb.GetComponentInChildren<Text>());
                classButtons[i] = cb;
                Tooltip.Set(cb.gameObject, () => TipText.Title(HeroClassRules.Name(c), UiTheme.Gold) + "\n" + HeroClassRules.Description(c) + "\n" + TipText.Dim(Loc.Pick("Клас обирається один раз.", "Class is chosen once.")));
            }

        }
    }
}
