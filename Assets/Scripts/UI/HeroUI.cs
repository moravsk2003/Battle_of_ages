using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Net;
using BattleOfAges.Player;
using TMPro;

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
        private TMP_Text header, body, points;
        private Image xpFill;
        private Button strengthButton, healthButton;
        private GameObject classPicker;
        private readonly Button[] classButtons = new Button[4];
        private static readonly HeroClass[] ClassOrder = { HeroClass.Tank, HeroClass.Archer, HeroClass.Mage, HeroClass.Dd };
        private readonly Button[] skillButtons = new Button[4];
        private TMP_Text artifactText;
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
                TMP_Text t = classButtons[i].GetComponentInChildren<TMP_Text>();
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
                $"Шкода: {(combat != null ? combat.AttackDamage : 0)}   Швидкість: {(combat != null ? combat.MoveSpeed : 0f):0.0} м/с\n" +
                $"<size=14><color=#B8A88E>Кожен рівень після 1-го додає +{GameBalance.HeroHealthPerLevel} HP і +{GameBalance.HeroStrengthPerLevel} сили</color></size>",
                $"XP: {hero.Xp} / {hero.XpToNextLevel}\n" +
                $"Rank: {RankName(rank != null ? rank.Rank : UnitRank.General)}   Class: {HeroClassRules.Name(hero.Class)}\n" +
                $"HP: {(hp != null ? hp.CurrentHealth : 0)} / {(hp != null ? hp.MaxHealth : 0)}   (invested: {hero.HealthPoints} × 50)\n" +
                $"Strength: {(combat != null ? combat.Strength : 0f):0}   (invested: {hero.StrengthPoints} × 5)\n" +
                $"Damage: {(combat != null ? combat.AttackDamage : 0)}   Speed: {(combat != null ? combat.MoveSpeed : 0f):0.0} m/s\n" +
                $"<size=14><color=#B8A88E>Each level after 1st grants +{GameBalance.HeroHealthPerLevel} HP and +{GameBalance.HeroStrengthPerLevel} strength</color></size>");
            points.text = Loc.Pick(
                $"Очки характеристик: {hero.AttributePoints}     Очки навиків: {hero.SkillPoints}",
                $"Attribute Points: {hero.AttributePoints}     Skill Points: {hero.SkillPoints}");
            strengthButton.interactable = healthButton.interactable = hero.AttributePoints > 0;
            classPicker.SetActive(hero.Class == HeroClass.None);
            RefreshSkills();
            RefreshArtifacts();
        }


        /// <summary>The class skills (both available and already learned/locked so tooltips stay readable; docs/GAME_DESIGN.md §8.1).</summary>
        private void RefreshSkills()
        {
            HeroSkills s = hero.GetComponent<HeroSkills>();
            int used = 0;
            if (s != null)
            {
                string keyQ = HudTips.Key(KeyAction.Ability);
                string keyR = HudTips.Key(KeyAction.Ability2);
                switch (hero.Class)
                {
                    case HeroClass.Tank:
                        if (!s.RegenStart) SkillButton(ref used, Loc.Pick("Регенерація (1)", "Regeneration (1)"), "RegenStart",
                            Loc.Pick("Старт гілки: +1% макс. і +2% поточного HP за хвилину, і в бою.", "Branch start: +1% max and +2% current HP per minute, even in combat."));
                        else
                        {
                            SkillButton(ref used, Loc.Pick($"Реген +1% макс ({s.RegenMaxShare * 100f:0}%)", $"Regen +1% Max ({s.RegenMaxShare * 100f:0}%)"), "RegenMax",
                                Loc.Pick($"Краще для тих, кого добивають до низу. Зараз: +{s.RegenMaxShare * 100f:0}% макс. HP/год.", $"Better when dropping low in HP. Current: +{s.RegenMaxShare * 100f:0}% max HP/h."));
                            SkillButton(ref used, Loc.Pick($"Реген +2% пот. ({s.RegenCurrentShare * 100f:0}%)", $"Regen +2% Cur ({s.RegenCurrentShare * 100f:0}%)"), "RegenCurrent",
                                Loc.Pick($"Краще для тих, хто тримається вище половини HP. Зараз: +{s.RegenCurrentShare * 100f:0}% пот. HP/год.", $"Better when staying above half HP. Current: +{s.RegenCurrentShare * 100f:0}% cur HP/h."));
                        }
                        bool healMax = s.HealLevel >= GameBalance.HealMaxLevel;
                        SkillButton(ref used,
                            healMax ? Loc.Pick($"Відхіл {s.HealLevel}/{GameBalance.HealMaxLevel} (макс.)", $"Heal {s.HealLevel}/{GameBalance.HealMaxLevel} (max)") : Loc.Pick($"Відхіл {s.HealLevel + 1}/{GameBalance.HealMaxLevel}", $"Heal {s.HealLevel + 1}/{GameBalance.HealMaxLevel}"),
                            "Heal",
                            Loc.Pick($"Уміння {keyQ}: миттєво лікує частку макс. HP (5% і +4% за очко, до 25%). Зараз: {Mathf.RoundToInt(s.HealShare * 100f)}%. Перезарядка {GameBalance.HealCooldown / 60f:0} хв.", $"Skill {keyQ}: instantly heals a share of max HP (5% + 4% per point, up to 25%). Current: {Mathf.RoundToInt(s.HealShare * 100f)}%. Cooldown {GameBalance.HealCooldown / 60f:0} min."),
                            learned: healMax,
                            lockedReason: !s.CanLearnHeal ? Loc.Pick("Спочатку вивчіть Регенерацію і вкладіть 1 очко в її гілку", "Requires Regeneration and 1 upgrade point in its branch") : null);
                        break;
                    case HeroClass.Archer:
                        SkillButton(ref used,
                            s.LongShot ? Loc.Pick("Далекий постріл (вивч.)", "Long Shot (learned)") : Loc.Pick($"Далекий постріл ({GameBalance.LongShotCost})", $"Long Shot ({GameBalance.LongShotCost})"),
                            "LongShot",
                            Loc.Pick($"Пасивно: +{Mathf.RoundToInt(GameBalance.LongShotRangeBonus * 100f)}% дальності стрільби.", $"Passive: +{Mathf.RoundToInt(GameBalance.LongShotRangeBonus * 100f)}% shooting range."),
                            learned: s.LongShot, cost: GameBalance.LongShotCost);
                        SkillButton(ref used,
                            s.PiercingArrow ? Loc.Pick("Пробивна стріла (вивч.)", "Piercing Arrow (learned)") : Loc.Pick("Пробивна стріла (1)", "Piercing Arrow (1)"),
                            "Pierce",
                            Loc.Pick($"Уміння {keyQ}: наступна стріла б'є на 200% і летить наскрізь (100% і 50%). Перезарядка {GameBalance.PierceCooldown:0} с.", $"Skill {keyQ}: next arrow deals 200% damage and pierces through (100% and 50%). Cooldown {GameBalance.PierceCooldown:0}s."),
                            learned: s.PiercingArrow,
                            lockedReason: !s.LongShot ? Loc.Pick("Спочатку вивчіть Далекий постріл", "Requires Long Shot first") : null);
                        break;
                    case HeroClass.Mage:
                        SkillButton(ref used,
                            s.FireFan ? Loc.Pick("Вогняне віяло (вивч.)", "Fire Fan (learned)") : Loc.Pick("Вогняне віяло (1)", "Fire Fan (1)"),
                            "FireFan",
                            Loc.Pick($"Уміння {keyQ}: віяло вогню ({GameBalance.FireFanAngle:0}°) перед собою — 60% шкоди мага (до 30% на краю), крізь броню. Перезарядка {GameBalance.FireFanCooldown:0} с.", $"Skill {keyQ}: {GameBalance.FireFanAngle:0}° cone of fire ahead — 60% mage damage (down to 30% at edge), ignores armor. Cooldown {GameBalance.FireFanCooldown:0}s."),
                            learned: s.FireFan);
                        SkillButton(ref used,
                            s.Meteor ? Loc.Pick("Метеор (вивч.)", "Meteor (learned)") : Loc.Pick("Метеор (1)", "Meteor (1)"),
                            "Meteor",
                            Loc.Pick($"Уміння {keyR}: метеор на найближчого ворога через 1 с, 5× шкода мага в радіусі 2,5 м, крізь броню. Перезарядка {GameBalance.MeteorCooldown:0} с.", $"Skill {keyR}: drops a meteor on the nearest enemy after 1s for 5× mage damage in 2.5 m radius, ignores armor. Cooldown {GameBalance.MeteorCooldown:0}s."),
                            learned: s.Meteor,
                            lockedReason: !s.FireFan ? Loc.Pick("Спочатку вивчіть Вогняне віяло", "Requires Fire Fan first") : null);
                        break;
                    case HeroClass.Dd:
                        bool dashMax = s.DashLevel >= GameBalance.DashMaxLevel;
                        SkillButton(ref used,
                            dashMax ? Loc.Pick($"Ривок ×{s.DashFactor:0} (макс.)", $"Dash ×{s.DashFactor:0} (max)") : Loc.Pick($"Ривок ×{s.DashFactor + 1f:0} ({s.DashLevel}/{GameBalance.DashMaxLevel})", $"Dash ×{s.DashFactor + 1f:0} ({s.DashLevel}/{GameBalance.DashMaxLevel})"),
                            "Dash",
                            Loc.Pick($"Уміння {keyQ}: на {GameBalance.DashSeconds:0.#} с прискорює біг до ×{GameBalance.DashMaxLevel + 1} (зараз ×{s.DashFactor:0}); перезарядка {GameBalance.DashCooldown:0} с.", $"Skill {keyQ}: boosts run speed for {GameBalance.DashSeconds:0.#}s up to ×{GameBalance.DashMaxLevel + 1} (current ×{s.DashFactor:0}); {GameBalance.DashCooldown:0}s cooldown."),
                            learned: dashMax);
                        SkillButton(ref used,
                            s.CriticalHit ? Loc.Pick("Критичний удар (вивч.)", "Critical Strike (learned)") : Loc.Pick("Критичний удар (1)", "Critical Strike (1)"),
                            "CriticalHit",
                            Loc.Pick($"Уміння {keyR}: наступний удар завдає ×{GameBalance.CritMultiplier:0} шкоди; перезарядка {GameBalance.CritCooldown:0} с.", $"Skill {keyR}: next hit deals ×{GameBalance.CritMultiplier:0} damage; {GameBalance.CritCooldown:0}s cooldown."),
                            learned: s.CriticalHit,
                            lockedReason: s.DashLevel <= 0 ? Loc.Pick("Спочатку вивчіть Ривок", "Requires Dash first") : null);
                        break;
                }
            }
            for (int i = used; i < skillButtons.Length; i++) skillButtons[i].gameObject.SetActive(false);
        }

        /// <summary>What the player does with its hero goes as an order (Net.HeroCommand).</summary>
        private static void Hero(HeroCommand.Act what, int value = 0, string skill = null) =>
            CommandBus.Submit(new HeroCommand { What = what, Value = value, Skill = skill });

        private void SkillButton(ref int index, string label, string skill, string tip, bool learned = false, int cost = 1, string lockedReason = null)
        {
            if (index >= skillButtons.Length) return;
            Button b = skillButtons[index++];
            Tooltip.Set(b.gameObject, () => TipText.Title(label, UiTheme.Gold) + "\n" + tip + "\n" +
                (learned ? TipText.Good(Loc.Pick("Вивчено", "Learned"))
                : lockedReason != null ? TipText.Dim(lockedReason)
                : TipText.Dim(hero != null && hero.SkillPoints >= cost
                    ? Loc.Pick($"Клік — вивчити за {cost} очк. навику", $"Click — learn for {cost} skill point(s)")
                    : Loc.Pick($"Потрібно очок навику: {cost} (даються з рівнем)", $"Requires {cost} skill point(s) (gained on level up)"))));
            b.gameObject.SetActive(true);
            b.GetComponentInChildren<TMP_Text>().text = label;
            b.interactable = !learned && lockedReason == null && hero != null && hero.SkillPoints >= cost;
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
            Tooltip.Set(strengthButton.gameObject, () =>
            {
                bool mage = hero != null && hero.Class == HeroClass.Mage;
                bool archer = hero != null && hero.Class == HeroClass.Archer;
                return TipText.Title(Loc.Pick("+5 сили", "+5 Strength"), UiTheme.Gold)
                    + (mage
                        ? Loc.Pick("\nДля мага сила підвищує шкоду заклять і куль (на швидкість бігу й дальність мага не впливає).", "\nFor a mage, strength increases spell and bolt damage (does not affect mage run speed or range).")
                        : archer
                            ? Loc.Pick("\nСила підвищує шкоду ударів, швидкість бігу та дальність стрільби з лука.", "\nStrength increases hit damage, run speed, and bow range.")
                            : Loc.Pick("\nСила — шкода удару; разом зі здоров'ям — швидкість бігу.", "\nStrength increases hit damage; combined with health, it increases run speed."))
                    + "\n" + TipText.Dim(Loc.Pick($"За 1 очко характеристик (дається з рівнем; кожен рівень сам дає +{GameBalance.HeroStrengthPerLevel} сили і +{GameBalance.HeroHealthPerLevel} HP).", $"Costs 1 attribute point (earned on level up; each level also grants +{GameBalance.HeroStrengthPerLevel} strength and +{GameBalance.HeroHealthPerLevel} HP)."));
            });
            Tooltip.Set(healthButton.gameObject, () =>
            {
                bool mage = hero != null && hero.Class == HeroClass.Mage;
                return TipText.Title("+50 HP", UiTheme.Gold)
                    + (mage
                        ? Loc.Pick("\nДодає +50 до макс. здоров'я героя (на швидкість бігу мага не впливає).", "\nIncreases hero max health by +50 (does not affect mage run speed).")
                        : Loc.Pick("\nДо найбільшого здоров'я героя; разом із силою — швидкість бігу.", "\nIncreases hero max health; combined with strength, increases run speed."))
                    + "\n" + TipText.Dim(Loc.Pick($"За 1 очко характеристик (дається з рівнем; кожен рівень сам дає +{GameBalance.HeroHealthPerLevel} HP і +{GameBalance.HeroStrengthPerLevel} сили).", $"Costs 1 attribute point (earned on level up; each level also grants +{GameBalance.HeroHealthPerLevel} HP and +{GameBalance.HeroStrengthPerLevel} strength)."));
            });
            // Artifacts: the starting pick (once), one-shots and shards
            RectTransform art = UiTheme.Rect(panel, "Artifacts", new Vector2(0.5f, 0f), new Vector2(0f, 262f), new Vector2(500f, 62f));
            artifactText = UiTheme.Label(art, "Text", "", 17, TextAnchor.MiddleLeft, UiTheme.Ink);
            LocText.Detach(artifactText);
            artifactText.richText = true;
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
            Tooltip.Set(combine.gameObject, () => TipText.Title(Loc.Pick("Об'єднати осколки", "Combine Shards"), UiTheme.Gold)
                + Loc.Pick("\nКує цілий артефакт із 2 осколків (шанс 50/50 отримати один із двох видів осколків).", "\nForges a complete artifact from 2 shards (50/50 chance between the two shard types).")
                + "\n" + TipText.Bad(Loc.Pick("Увага: другий світозмінний артефакт (зброя ×5, броня 80%, вампіризм або мудрість) миттєво вб'є героя!", "Warning: a second world-changing artifact (weapon ×5, 80% armor, vampirism, or wisdom) will instantly kill the hero!")));

            // Class skills share the row with the class picker (shown once the class is chosen)
            for (int i = 0; i < skillButtons.Length; i++)
            {
                skillButtons[i] = UiTheme.TextButton(panel, "Skill" + i, "", new Vector2(0f, 0f), new Vector2(20f + i * 130f, 150f), new Vector2(124f, 46f), null, 14);
                TMP_Text st = skillButtons[i].GetComponentInChildren<TMP_Text>();
                LocText.Detach(st);
                st.textWrappingMode = TextWrappingModes.Normal;
                st.enableAutoSizing = true;
                st.fontSizeMin = 10;
                st.fontSizeMax = 14;
            }

            // The class is picked once (docs/GAME_DESIGN.md §8.1)
            RectTransform pick = UiTheme.Rect(panel, "ClassPicker", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(520f, 50f));
            classPicker = pick.gameObject;
            for (int i = 0; i < ClassOrder.Length; i++)
            {
                HeroClass c = ClassOrder[i];
                Button cb = UiTheme.TextButton(pick, "Class" + c, HeroClassRules.Name(c), new Vector2(0f, 0.5f), new Vector2(10f + i * 128f, 0f), new Vector2(120f, 46f),
                    () => Hero(HeroCommand.Act.ChooseClass, (int)c), 19);
                LocText.Detach(cb.GetComponentInChildren<TMP_Text>());
                classButtons[i] = cb;
                Tooltip.Set(cb.gameObject, () => TipText.Title(HeroClassRules.Name(c), UiTheme.Gold) + "\n" + HeroClassRules.Description(c) + "\n" + TipText.Dim(Loc.Pick("Клас обирається один раз.", "Class is chosen once.")));
            }

        }
    }
}
