using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UInput = UnityEngine.InputSystem.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.UI;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// The M0 checklist in the real game scene (not a regular test): every implemented rule of docs/GAME_DESIGN.md is
    /// exercised in SampleScene, keys go through the Input System like a player's. Writes m0_checklist.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Integration checklist, run on demand")]
    public class M0Checklist
    {
        private readonly StringBuilder report = new StringBuilder();
        private int passed, failed;
        private Keyboard kb;

        private static PlayerCombatController Player => UnityEngine.Object.FindFirstObjectByType<PlayerCombatController>();
        private static T Of<T>() where T : Component => Player.GetComponent<T>();

        [UnityTest]
        public IEnumerator RunChecklist()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            LogAssert.ignoreFailingMessages = true;
            string saves = Path.Combine(dir, "checklist_saves");
            SaveSystem.FolderOverride = saves;

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);
            var behaviour = UInput.settings.editorInputBehaviorInPlayMode; var background = UInput.settings.backgroundBehavior;
            UInput.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            UInput.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            kb = Keyboard.current ?? UInput.AddDevice<Keyboard>();
            kb.MakeCurrent();

            report.AppendLine("# M0 — чек-лист у SampleScene").AppendLine();
            report.AppendLine("| # | Пункт | Результат | Деталі |").AppendLine("|---|---|---|---|");

            yield return Check("1", "Ранги: герой-генерал 500 HP / сила 50 / 7,75 м/с; рицарі, селяни, моби — база свого рангу", Ranks());
            yield return Check("2", "Один тип юніта: у всіх HP, бій, робота, ранг; рицарі стоять, селяни працюють; стеля рахує всіх", OneKindOfUnit());
            yield return Check("3", "Ратуша: стелі 6/15/25/40/60/85, побудова 10 дерева, апгрейди 30/60/100/160", HallTable());
            yield return Check("4", "Ігровий годинник: 1 хв = 1 ігрова година", Clock());
            yield return Check("5", "Приріст населення біля ратуші (прискорений годинник)", Growth());
            yield return Check("6", "Природний реген пораненого (прискорений годинник)", Regen());
            yield return Check("7", "Підвищення від роботи: людина → воїн", WorkPromotion());
            yield return Check("8", "Підвищення від бою: воїн 100 досвіду → рицар", BattlePromotion());
            yield return Check("9", "Досвід героя за вбивство (бандит-воїн = 4)", HeroKillXp());
            yield return Check("10", "Вікно героя відкривається і закривається клавішею H", HeroWindow());
            yield return Check("11", "Клас: Танк +20% HP; очки +5 сили / +50 HP", ClassAndPoints());
            yield return Check("12", "Навик: Миттєвий відхіл Танка на Q", SkillOnQ());
            yield return Check("13", "Артефакти: стартовий вибір, Меч ×5, Мудрості +20%", ArtifactsCheck());
            yield return Check("14", "Сейв і завантаження зберігають рівень, клас, навики, артефакти, ранг юніта", SaveLoad());
            yield return Check("15", "Анімація за швидкістю: LocoRate у аніматорі", LocoRate());
            yield return Check("16", "Смерть героя: міжцарів'я, половина дезертирує, найсильніший стає героєм, артефакти на землі", HeroDeath());
            yield return Check("17", "Кінець гри, коли юнітів не лишилось", GameOver());
            yield return Check("18", "Пробіл не натискає виділену кнопку UI (Submit) і не гальмує", SpaceAndUi());

            report.AppendLine().AppendLine($"Пройдено {passed}, не пройдено {failed}.");
            File.WriteAllText(Path.Combine(dir, "m0_checklist.md"), report.ToString(), new UTF8Encoding(true));
            UInput.settings.editorInputBehaviorInPlayMode = behaviour;
            UInput.settings.backgroundBehavior = background;
            GameBalance.GameHourSeconds = 60f;
            SaveSystem.FolderOverride = null;
            if (Directory.Exists(saves)) Directory.Delete(saves, true);
        }

        // ------------------------------------------------------------------ runner

        private IEnumerator Check(string n, string title, IEnumerator body)
        {
            string error = null, details = "";
            while (true)
            {
                object current;
                try
                {
                    if (!body.MoveNext()) break;
                    current = body.Current;
                }
                catch (Exception e) { error = e.Message.Replace("\n", " ").Replace("|", "/"); break; }
                if (current is string note) { details = note; continue; }
                yield return current;
            }
            GameBalance.GameHourSeconds = 60f;
            if (error == null) passed++; else failed++;
            report.AppendLine($"| {n} | {title} | {(error == null ? "✅" : "❌")} | {(error ?? details)} |");
        }

        private IEnumerator Press(Key key)
        {
            UInput.QueueStateEvent(kb, new KeyboardState(key));
            yield return null;
            UInput.QueueStateEvent(kb, new KeyboardState());
            yield return null;
        }

        private static List<RtsUnit> OwnUnits() => HeroSuccession.OwnUnits();

        private static bool IsKnight(RtsUnit u)
        {
            var look = u.GetComponentInChildren<CharacterGear>(true);
            return look != null ? look.Type == HumanoidModelBuilder.HumanType.KnightGuard : u.name.Contains("Knight");
        }

        // ------------------------------------------------------------------ checks

        private IEnumerator Ranks()
        {
            UnitProgress p = Of<UnitProgress>();
            Assert.AreEqual(UnitRank.General, p.Rank); Assert.IsTrue(p.IsHero);
            Assert.AreEqual(500, Of<Equipment>().BaseHealth);
            Assert.AreEqual(7.75f, Of<CombatUnit>().MoveSpeed, 0.05f, "hero speed");
            int knights = 0, people = 0;
            foreach (RtsUnit u in OwnUnits())
            {
                UnitProgress r = u.GetComponent<UnitProgress>();
                if (IsKnight(u)) { knights++; Assert.AreEqual(UnitRank.Knight, r.Rank, u.name); Assert.AreEqual(100, u.GetComponent<Equipment>().BaseHealth); }
                else { people++; Assert.AreEqual(UnitRank.Person, r.Rank, u.name); Assert.AreEqual(20, u.GetComponent<Equipment>().BaseHealth); }
            }
            int warriors = 0, generals = 0;
            foreach (HostileMobAI m in UnityEngine.Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None))
            {
                UnitProgress r = m.GetComponent<UnitProgress>();
                Assert.IsNotNull(r, m.name);
                if (r.Rank == UnitRank.General) generals++; else { warriors++; Assert.AreEqual(40, m.GetComponent<Equipment>().BaseHealth, m.name); }
            }
            yield return $"рицарів {knights}, людей {people}, мобів-воїнів {warriors}, генералів {generals}";
        }

        private IEnumerator OneKindOfUnit()
        {
            var units = OwnUnits();
            Assert.Greater(units.Count, 0, "no own units in the scene");
            foreach (RtsUnit u in units)
            {
                Assert.IsNotNull(u.GetComponent<HealthComponent>(), u.name + " HP");
                Assert.IsNotNull(u.GetComponent<CombatUnit>(), u.name + " combat");
                var job = u.GetComponent<NpcResourceGatherer>();
                Assert.IsNotNull(job, u.name + " work");
                if (IsKnight(u)) Assert.IsFalse(job.IsWorking, u.name + " should stand by");
                else Assert.IsTrue(job.IsWorking, u.name + " should work");
            }
            Assert.AreEqual(units.Count, WorkerRegistry.WorkerCount, "the cap counts every unit");
            yield return $"юнітів {units.Count}, працюють {WorkerRegistry.WorkingCount}, стеля {TownHall.PopulationCap}";
        }

        private IEnumerator HallTable()
        {
            int[] caps = { 6, 15, 25, 40, 60, 85 };
            for (int l = 0; l <= 5; l++) Assert.AreEqual(caps[l], TownHall.CapForLevel(l));
            Assert.AreEqual(10, BuildingData.GetDefinition(BuildingType.TownHall).WoodCost);
            int[] wood = { 0, 0, 30, 60, 100, 160 };
            for (int to = 2; to <= 5; to++) { TownHall.UpgradeCost(to, out int w, out _, out _); Assert.AreEqual(wood[to], w); }
            yield return "таблиця як у документі";
        }

        private IEnumerator Clock()
        {
            double h0 = WorldClock.GameHours;
            float t0 = Time.time;
            yield return new WaitForSeconds(3f);
            double hours = WorldClock.GameHours - h0, expected = (Time.time - t0) / 60.0;
            Assert.AreEqual(expected, hours, 0.01, "game hours");
            yield return $"за {Time.time - t0:0.0} с минуло {hours:0.000} ігрових год";
        }

        private IEnumerator Growth()
        {
            TownHall hall = UnityEngine.Object.FindFirstObjectByType<TownHall>();
            if (hall == null)
            {
                hall = TownHall.CreatePrefab("ChecklistHall").GetComponent<TownHall>();
                Vector3 p = Player.transform.position + Player.transform.forward * 25f;
                p.y = RtsUnit.GetTerrainHeight(p);
                hall.transform.position = p;
            }
            int before = WorkerRegistry.WorkerCount;
            Assert.Less(before, TownHall.PopulationCap, "no room to grow");
            GameBalance.GameHourSeconds = 0.05f; // an hour every 0.05 s
            yield return new WaitForSeconds(1.5f);
            GameBalance.GameHourSeconds = 60f;
            yield return null;
            int after = WorkerRegistry.WorkerCount;
            Assert.Greater(after, before, "nobody was born");
            Assert.LessOrEqual(after, TownHall.PopulationCap, "growth went over the cap");
            yield return $"населення {before} → {after} (стеля {TownHall.PopulationCap})";
        }

        private IEnumerator Regen()
        {
            RtsUnit knight = OwnUnits().Find(IsKnight);
            Assert.IsNotNull(knight, "no knight");
            HealthComponent hp = knight.GetComponent<HealthComponent>();
            hp.SetCurrentHealth(hp.MaxHealth / 2);
            int before = hp.CurrentHealth;
            GameBalance.GameHourSeconds = 0.05f;
            yield return new WaitForSeconds(1.5f);
            GameBalance.GameHourSeconds = 60f;
            Assert.Greater(hp.CurrentHealth, before, "no healing");
            yield return $"HP рицаря {before} → {hp.CurrentHealth} за ~30 ігрових год";
        }

        private IEnumerator WorkPromotion()
        {
            int promoted = TownHall.PromoteWorkers(1f);
            Assert.Greater(promoted, 0, "no working person");
            foreach (RtsUnit u in OwnUnits())
                if (IsKnight(u)) Assert.AreEqual(UnitRank.Knight, u.GetComponent<UnitProgress>().Rank, "idle knights are not promoted by work");
            yield return null;
            yield return $"воїнами стали {promoted} працюючих людей";
        }

        private IEnumerator BattlePromotion()
        {
            RtsUnit warrior = OwnUnits().Find(u => u.GetComponent<UnitProgress>().Rank == UnitRank.Warrior);
            Assert.IsNotNull(warrior, "no warrior (check 7 makes them)");
            UnitProgress p = warrior.GetComponent<UnitProgress>();
            for (int i = 0; i < 10; i++) p.OnKill();
            Assert.AreEqual(UnitRank.Knight, p.Rank);
            Assert.AreEqual(100, warrior.GetComponent<Equipment>().BaseHealth);
            yield return null;
            yield return $"{warrior.name}: 10 вбивств → рицар";
        }

        private IEnumerator HeroKillXp()
        {
            HeroProgress hero = Of<HeroProgress>();
            int xp0 = hero.Xp, level0 = hero.Level;
            GameObject mob = HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "ChecklistBandit");
            mob.GetComponent<HostileMobAI>().enabled = false;
            CharacterController cc = mob.GetComponent<CharacterController>();
            cc.enabled = false;
            mob.transform.position = Player.transform.position + Player.transform.forward * 1.5f;
            cc.enabled = true;
            yield return null;
            mob.GetComponent<HealthComponent>().SetCurrentHealth(1);
            yield return new WaitForSeconds(2.1f); // let the attack cooldown pass
            Assert.IsTrue(Of<CombatUnit>().TryAttack(mob.GetComponent<HealthComponent>()), "attack did not land");
            yield return new WaitForSeconds(0.5f); // wind-up
            Assert.AreEqual(4, hero.Xp - xp0 + (hero.Level - level0) * 100, "kill XP");
            yield return $"вбивство бандита: +{hero.Xp - xp0} досвіду";
        }

        private IEnumerator HeroWindow()
        {
            Assert.IsFalse(HeroUI.IsVisible);
            yield return Press(Key.H);
            Assert.IsTrue(HeroUI.IsVisible, "H did not open the window");
            yield return Press(Key.H);
            Assert.IsFalse(HeroUI.IsVisible, "H did not close it");
            yield return "H відкриває і закриває";
        }

        private IEnumerator ClassAndPoints()
        {
            HeroProgress hero = Of<HeroProgress>();
            Assert.IsTrue(hero.ChooseClass(HeroClass.Tank));
            Assert.AreEqual(600, Player.GetComponent<HealthComponent>().MaxHealth, "tank HP");
            hero.AddXp(100 + 200 + 300);
            float str = Of<CombatUnit>().Strength;
            Assert.IsTrue(hero.SpendOnStrength());
            Assert.AreEqual(str + 5f, Of<CombatUnit>().Strength, 0.01f);
            Assert.IsTrue(hero.SpendOnHealth());
            Assert.AreEqual(660, Player.GetComponent<HealthComponent>().MaxHealth, "+50 HP x 1.2");
            yield return null;
            yield return $"рівень {hero.Level}, сила {Of<CombatUnit>().Strength:0}, HP {Player.GetComponent<HealthComponent>().MaxHealth}";
        }

        private IEnumerator SkillOnQ()
        {
            HeroSkills skills = Of<HeroSkills>();
            Assert.IsTrue(skills.LearnRegenStart() && skills.LearnRegenMax(), "Regeneration first (the branch rule)");
            Assert.IsTrue(skills.LearnHeal(), "no skill point");
            HealthComponent hp = Player.GetComponent<HealthComponent>();
            hp.SetCurrentHealth(100);
            yield return Press(Key.Q);
            Assert.AreEqual(100 + Mathf.RoundToInt(hp.MaxHealth * 0.05f), hp.CurrentHealth, 1, "Q did not heal");
            yield return $"Q: HP 100 → {hp.CurrentHealth}";
        }

        private IEnumerator ArtifactsCheck()
        {
            HeroArtifacts bag = Of<HeroArtifacts>();
            Assert.IsTrue(bag.ChooseStart(ArtifactType.Wisdom));
            Assert.IsFalse(bag.ChooseStart(ArtifactType.Vampiric), "start pick twice");
            bag.Add(ArtifactType.SwordX5);
            Assert.AreEqual(Mathf.RoundToInt(Of<CombatUnit>().Strength * 5f), Of<CombatUnit>().AttackDamage, "sword x5");
            int xp = Of<HeroProgress>().Xp;
            Of<HeroProgress>().AddXp(10);
            Assert.AreEqual(xp + 12, Of<HeroProgress>().Xp, "wisdom +20%");
            yield return null;
            yield return $"урон {Of<CombatUnit>().AttackDamage}, артефактів {bag.Held.Count}";
        }

        private IEnumerator SaveLoad()
        {
            int level = Of<HeroProgress>().Level;
            int warriorsOrBetter = 0;
            foreach (RtsUnit u in OwnUnits()) if (!IsKnight(u) && u.GetComponent<UnitProgress>().Rank != UnitRank.Person) warriorsOrBetter++;
            Assert.IsTrue(SaveSystem.Save(9), "save failed");
            yield return null;
            Assert.IsTrue(SaveSystem.Load(9), "load failed");
            yield return new WaitForSeconds(2.5f);
            Assert.AreEqual(level, Of<HeroProgress>().Level, "level");
            Assert.AreEqual(HeroClass.Tank, Of<HeroProgress>().Class, "class");
            Assert.AreEqual(1, Of<HeroSkills>().HealLevel, "skill");
            Assert.IsTrue(Of<HeroArtifacts>().Has(ArtifactType.SwordX5) && Of<HeroArtifacts>().Has(ArtifactType.Wisdom), "artifacts");
            int after = 0;
            foreach (RtsUnit u in OwnUnits()) if (!IsKnight(u) && u.GetComponent<UnitProgress>().Rank != UnitRank.Person) after++;
            Assert.AreEqual(warriorsOrBetter, after, "promoted villagers");
            yield return $"рівень {level}, Танк, відхіл 1, 2 артефакти, підвищених селян {after}";
        }

        private IEnumerator LocoRate()
        {
            CharacterAnimationDriver driver = null;
            foreach (CharacterAnimationDriver d in UnityEngine.Object.FindObjectsByType<CharacterAnimationDriver>(FindObjectsSortMode.None))
            {
                Animator a = d.GetComponentInChildren<Animator>();
                if (a != null && a.enabled && a.runtimeAnimatorController != null) { driver = d; break; }
            }
            Assert.IsNotNull(driver, "no animated character with an Animator on");
            Animator anim = driver.GetComponentInChildren<Animator>();
            bool has = false;
            foreach (AnimatorControllerParameter p in anim.parameters) if (p.name == "LocoRate") has = true;
            Assert.IsTrue(has, "the controller has no LocoRate");
            driver.SpeedOverride = 7.5f;
            yield return new WaitForSeconds(0.3f);
            float rate = anim.GetFloat("LocoRate");
            driver.SpeedOverride = null;
            Assert.AreEqual(7.5f / 4.2f, rate, 0.05f);
            yield return $"{driver.name}: 7,5 м/с → LocoRate {rate:0.00}";
        }

        private IEnumerator HeroDeath()
        {
            HeroSuccession hero = Of<HeroSuccession>();
            int units = OwnUnits().Count;
            Assert.GreaterOrEqual(units, 2, "too few units");
            Player.GetComponent<HealthComponent>().TakeDamage(int.MaxValue / 2, ignoreArmor: true);
            Assert.IsTrue(hero.InInterregnum, "no interregnum");
            yield return null;
            int left = OwnUnits().Count;
            Assert.AreEqual(units - units / 2, left, "half should desert");
            int pickups = ArtifactPickup.Active.Count;
            Assert.AreEqual(2, pickups, "artifacts on the ground (sword, wisdom)");
            RtsUnit strongest = HeroSuccession.Strongest(OwnUnits());
            UnitRank expected = strongest.GetComponent<UnitProgress>().Rank;
            hero.EndInterregnum();
            yield return null;
            Assert.AreEqual(expected, Of<UnitProgress>().Rank, "rank of the new hero");
            Assert.AreEqual(1, Of<HeroProgress>().Level);
            Assert.AreEqual(HeroClass.None, Of<HeroProgress>().Class);
            Assert.IsTrue(Player.GetComponent<HealthComponent>().IsAlive);
            foreach (ArtifactPickup p in ArtifactPickup.Active) Assert.IsTrue(p.MayTake(hero), "the new hero may take them");
            yield return $"юнітів {units} → {left}, на землі {pickups} артефакти, новий герой — {HeroUI.RankName(expected)}";
        }

        private IEnumerator SpaceAndUi()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            Assert.IsNotNull(es, "no EventSystem");
            Canvas canvas = UiTheme.CreateCanvas("ChecklistCanvas", 50);
            int clicks = 0;
            var button = UiTheme.TextButton(canvas.transform, "B", "B", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 40f), () => clicks++);
            es.SetSelectedGameObject(button.gameObject);
            yield return null;
            float t0 = Time.realtimeSinceStartup; int f0 = Time.frameCount;
            for (int i = 0; i < 20; i++) yield return Press(Key.Space);
            float ms = (Time.realtimeSinceStartup - t0) * 1000f / (Time.frameCount - f0);
            UnityEngine.Object.Destroy(canvas.gameObject);
            Assert.AreEqual(0, clicks, "Space clicked the selected button");
            yield return $"20 натискань: кнопка не натиснута, {ms:0.0} мс/кадр";
        }

        private IEnumerator GameOver()
        {
            HeroSuccession hero = Of<HeroSuccession>();
            foreach (RtsUnit u in OwnUnits()) UnityEngine.Object.Destroy(u.gameObject);
            yield return null;
            Player.GetComponent<HealthComponent>().TakeDamage(int.MaxValue / 2, ignoreArmor: true);
            hero.EndInterregnum();
            Assert.IsTrue(hero.IsGameOver);
            yield return null;
            yield return "без юнітів — кінець гри";
        }
    }
}
