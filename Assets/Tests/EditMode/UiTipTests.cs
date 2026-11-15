using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.UI;
using TMPro;

namespace BattleOfAges.Tests
{
    /// <summary>Tooltip texts (docs/GAME_DESIGN.md §21, stage 1.1): the gear comparison and the preview behind it.</summary>
    [TestFixture]
    public class UiTipTests
    {
        private GameObject unit;
        private Equipment gear;

        [SetUp]
        public void SetUp()
        {
            unit = new GameObject("Unit", typeof(HealthComponent), typeof(CombatUnit));
            unit.GetComponent<HealthComponent>().InitializeHealth(100);
            unit.GetComponent<CombatUnit>().InitializeCombatStats(10, 2f, 3f);
            gear = unit.AddComponent<Equipment>();
            gear.SetBaseStats(100, 10, 2f, 3f);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(unit);
            Loc.Set(Loc.Lang.Uk, save: false);
            PlayerPrefs.DeleteKey(Loc.PrefKey);
        }

        [Test]
        public void StatsWith_PreviewsTheItem_WithoutPuttingItOn()
        {
            gear.Equip(new Item(ItemType.Sword, 1));
            int damage = gear.Damage;
            var with = gear.StatsWith(new Item(ItemType.Sword, 3));
            Assert.Greater(with.damage, damage, "a better sword gives more damage");
            Assert.AreEqual(damage, gear.Damage, "nothing changed on the unit");
            Assert.AreEqual(1, gear[EquipSlot.Weapon].Level, "the worn sword is still the old one");

            var without = gear.StatsWithout(EquipSlot.Weapon);
            Assert.Less(without.damage, damage, "bare hands hit weaker");
            Assert.AreEqual(1, gear[EquipSlot.Weapon].Level);
        }

        [Test]
        public void Compare_ShowsOnlyWhatChanges_BetterGreen_WorseRed()
        {
            var a = (health: 100, damage: 10, cooldown: 2f, reduction: 0.1f, range: 3f);
            var b = (health: 100, damage: 14, cooldown: 2.5f, reduction: 0.1f, range: 3f);
            string text = TipText.Compare(a, b);
            StringAssert.Contains("Шкода  10 → 14", text);
            StringAssert.Contains(ColorUtility.ToHtmlStringRGB(UiTheme.Good) + ">+4", text, "more damage is better");
            StringAssert.Contains(ColorUtility.ToHtmlStringRGB(UiTheme.Bad) + ">+0.5 с", text.Replace(",", "."), "a slower hit is worse");
            StringAssert.DoesNotContain("Здоров'я", text, "health did not change");
            StringAssert.DoesNotContain("Дальність", text);
            Assert.AreEqual("", TipText.Compare(a, a), "nothing to show when nothing changes");
        }

        [Test]
        public void Compare_OfARealArmourSwap_ShowsTheReduction()
        {
            gear.Equip(new Item(ItemType.Helmet, 1));
            string text = TipText.Compare(gear.StatsWith(gear[EquipSlot.Helmet]), gear.StatsWith(new Item(ItemType.Helmet, 4)));
            StringAssert.Contains("Поглинання", text);
            StringAssert.Contains(ColorUtility.ToHtmlStringRGB(UiTheme.Good), text);
        }

        [Test]
        public void Loc_DefaultsToUk_SwitchesAndCyclesCleanly()
        {
            PlayerPrefs.DeleteKey(Loc.PrefKey);
            Loc.Set(Loc.Lang.Uk, save: false);
            Assert.AreEqual(Loc.Lang.Uk, Loc.Current);
            Assert.IsFalse(Loc.En);
            Assert.IsFalse(Loc.HasChosen);

            Loc.Set(Loc.Lang.En, save: true);
            Assert.AreEqual(Loc.Lang.En, Loc.Current);
            Assert.IsTrue(Loc.En);
            Assert.IsTrue(Loc.HasChosen);

            Loc.Cycle();
            Assert.AreEqual(Loc.Lang.Uk, Loc.Current);
            Assert.IsFalse(Loc.En);
        }

        [Test]
        public void Loc_TranslatesCoreGameplayAndUiLabels()
        {
            Loc.Set(Loc.Lang.Uk, save: false);
            Assert.AreEqual("Будинок", BuildingData.GetDefinition(BuildingType.WorkerHouse).DisplayName);
            Assert.AreEqual("Меч", ItemTypeNames.Display(ItemType.Sword));
            Assert.AreEqual("Танк", HeroClassRules.Name(HeroClass.Tank));
            Assert.AreEqual("Блискавка", Artifacts.Name(ArtifactType.Lightning));
            Assert.AreEqual("Пробіл", KeybindsManager.GetKeyDisplayString(KeyCode.Space));
            Assert.AreEqual("Меню будівництва", KeybindsManager.GetActionDisplayName(KeyAction.BuildMenu));
            Assert.AreEqual("Низька", GraphicsQuality.GetName(GraphicsQuality.Level.Low));
            Assert.AreEqual("Автозбереження", SaveSystem.SlotName(SaveSystem.AutoSlot));
            Assert.AreEqual("Слот 1", SaveSystem.SlotName(1));
            Assert.AreEqual(DisplaySettings.FrameCap == 0 ? "без обмеження" : DisplaySettings.FrameCap + " FPS", DisplaySettings.FrameCapName);
            Assert.AreEqual("Основи", HelpUI.TabName(0));
            StringAssert.Contains("Від першої особи", HelpUI.TabText(0));

            Loc.Set(Loc.Lang.En, save: false);
            Assert.AreEqual("House", BuildingData.GetDefinition(BuildingType.WorkerHouse).DisplayName);
            Assert.AreEqual("Sword", ItemTypeNames.Display(ItemType.Sword));
            Assert.AreEqual("Tank", HeroClassRules.Name(HeroClass.Tank));
            Assert.AreEqual("Lightning", Artifacts.Name(ArtifactType.Lightning));
            Assert.AreEqual("Space", KeybindsManager.GetKeyDisplayString(KeyCode.Space));
            Assert.AreEqual("Build Menu", KeybindsManager.GetActionDisplayName(KeyAction.BuildMenu));
            Assert.AreEqual("Low", GraphicsQuality.GetName(GraphicsQuality.Level.Low));
            Assert.AreEqual("Autosave", SaveSystem.SlotName(SaveSystem.AutoSlot));
            Assert.AreEqual("Slot 1", SaveSystem.SlotName(1));
            Assert.AreEqual(DisplaySettings.FrameCap == 0 ? "Unlimited" : DisplaySettings.FrameCap + " FPS", DisplaySettings.FrameCapName);
            StringAssert.Contains("First Person", HelpUI.TabText(0));
            Assert.AreEqual("Game saved (auto)", Loc.Tr("Гру збережено (авто)"));
            Assert.AreEqual("Wolf Reach has fallen: town hall destroyed", Loc.Tr("Вовчий край впало: ратушу зруйновано"));

            var a = (health: 100, damage: 10, cooldown: 2f, reduction: 0.1f, range: 3f);
            var b = (health: 100, damage: 14, cooldown: 2.5f, reduction: 0.1f, range: 3f);
            string cmpEn = TipText.Compare(a, b);
            StringAssert.Contains("Damage  10 → 14", cmpEn);
        }

        [Test]
        public void LocText_UpdatesLiveOnLanguageChange()
        {
            Loc.Set(Loc.Lang.Uk, save: false);
            var go = new GameObject("LocTextProbe", typeof(RectTransform), typeof(TextMeshProUGUI));
            try
            {
                TMP_Text label = go.GetComponent<TMP_Text>();
                label.text = "Налаштування";
                LocText.Attach(label);
                Assert.AreEqual("Налаштування", label.text);

                Loc.Set(Loc.Lang.En, save: false);
                Assert.AreEqual("Settings", label.text);

                Loc.Set(Loc.Lang.Uk, save: false);
                Assert.AreEqual("Налаштування", label.text);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PickaxeDescription_MentionsSurfacePebbleBonusFromLevelThree()
        {
            var p2 = new Item(ItemType.Pickaxe, 2);
            var p3 = new Item(ItemType.Pickaxe, 3);
            Loc.Set(Loc.Lang.Uk, save: false);
            Assert.IsFalse(ItemTypeNames.Describe(p2).Contains("камінці"));
            StringAssert.Contains("×2", ItemTypeNames.Describe(p3));
            StringAssert.Contains("камінці", ItemTypeNames.Describe(p3));

            Loc.Set(Loc.Lang.En, save: false);
            Assert.IsFalse(ItemTypeNames.Describe(p2).Contains("pebbles"));
            StringAssert.Contains("×2", ItemTypeNames.Describe(p3));
            StringAssert.Contains("pebbles", ItemTypeNames.Describe(p3));
        }

        [Test]
        public void ArcherDescription_MentionsMeleePenaltyWithoutBow()
        {
            Loc.Set(Loc.Lang.Uk, save: false);
            StringAssert.Contains("вдвічі повільніше", HeroClassRules.Description(HeroClass.Archer));

            Loc.Set(Loc.Lang.En, save: false);
            StringAssert.Contains("twice as slowly", HeroClassRules.Description(HeroClass.Archer));
        }

        [Test]
        public void HeroSkills_ActiveDescription_CoversAllClassesAndSlots()
        {
            foreach (HeroClass cls in new[] { HeroClass.Tank, HeroClass.Archer, HeroClass.Mage, HeroClass.Dd })
            {
                var h = new GameObject("HeroProbe_" + cls, typeof(HealthComponent), typeof(CombatUnit), typeof(Equipment));
                try
                {
                    h.GetComponent<HealthComponent>().InitializeHealth(500);
                    h.GetComponent<CombatUnit>().InitializeCombatStats(10, 2f, 3f);
                    UnitProgress.Setup(h, UnitRank.General, hero: true);
                    HeroProgress hp = h.AddComponent<HeroProgress>();
                    HeroSkills hs = h.AddComponent<HeroSkills>();
                    hp.AddXp(100 * (1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9));
                    Assert.IsTrue(hp.ChooseClass(cls));

                    if (cls == HeroClass.Tank)
                    {
                        Assert.IsTrue(hs.LearnRegenStart());
                        Assert.IsTrue(hs.LearnRegenMax());
                        Assert.IsTrue(hs.LearnHeal());
                    }
                    else if (cls == HeroClass.Archer)
                    {
                        Assert.IsTrue(hs.LearnLongShot());
                        Assert.IsTrue(hs.LearnPierce());
                    }
                    else if (cls == HeroClass.Mage)
                    {
                        Assert.IsTrue(hs.LearnFireFan());
                        Assert.IsTrue(hs.LearnMeteor());
                    }
                    else if (cls == HeroClass.Dd)
                    {
                        Assert.IsTrue(hs.LearnDash());
                        Assert.IsTrue(hs.LearnCriticalHit());
                    }

                    foreach (Loc.Lang lang in new[] { Loc.Lang.Uk, Loc.Lang.En })
                    {
                        Loc.Set(lang, save: false);
                        string cdWord = lang == Loc.Lang.Uk ? "Перезарядка" : "Cooldown";
                        Assert.IsTrue(hs.HasActiveIn(0));
                        string d0 = hs.ActiveDescription(0);
                        Assert.IsFalse(string.IsNullOrEmpty(d0));
                        StringAssert.Contains(cdWord, d0);
                        if (cls == HeroClass.Mage || cls == HeroClass.Dd)
                        {
                            Assert.IsTrue(hs.HasActiveIn(1));
                            string d1 = hs.ActiveDescription(1);
                            Assert.IsFalse(string.IsNullOrEmpty(d1));
                            StringAssert.Contains(cdWord, d1);
                        }
                        else
                        {
                            Assert.IsFalse(hs.HasActiveIn(1));
                            Assert.IsNull(hs.ActiveDescription(1));
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(h);
                }
            }
        }
    }
}
