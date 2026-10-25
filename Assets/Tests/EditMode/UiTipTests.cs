using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.UI;

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
        public void TearDown() => Object.DestroyImmediate(unit);

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
            StringAssert.Contains("Урон  10 → 14", text);
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
    }
}
