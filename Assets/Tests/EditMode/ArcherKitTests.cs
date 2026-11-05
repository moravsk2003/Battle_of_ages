using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    /// <summary>Enemies with bows: some of every camp and kingdom shoot, a bow takes both hands, an archer stays one when promoted.</summary>
    public class ArcherKitTests
    {
        [SetUp]
        public void SetUp() { UnitSpatialGrid.Clear(); Random.InitState(7); }

        [TearDown]
        public void TearDown()
        {
            NpcKingdom.RemoveAll();
            foreach (HostileMobAI m in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None)) Object.DestroyImmediate(m.gameObject);
            UnitSpatialGrid.Clear();
        }

        private static bool HasBow(Item[] kit) => kit.Any(i => !i.IsEmpty && i.Type == ItemType.Bow);

        [Test]
        public void ArcherKit_ABowOfTheSwordsLevel_NoShield_TheArmourStays()
        {
            Item[] archer = UnitStats.ArcherKit(UnitStats.Kit(UnitRank.Warrior, 3));
            Assert.IsTrue(archer.Contains(new Item(ItemType.Bow, 3)));
            Assert.IsFalse(archer.Any(i => !i.IsEmpty && (i.Type == ItemType.Sword || i.Type == ItemType.Shield)), "a bow takes both hands");
            Assert.IsTrue(archer.Contains(new Item(ItemType.Armor, 3)));
            Assert.IsTrue(UnitStats.ArcherKit(new[] { new Item(ItemType.Axe, 0) }).Contains(new Item(ItemType.Bow, 1)), "at least a level I bow");
        }

        [Test]
        public void Camps_EveryFewthBanditOrGoblinShoots_TheWarlordsGuardDoesNot()
        {
            foreach (HostileMobType type in new[] { HostileMobType.Bandit, HostileMobType.Goblin })
            {
                int archers = 0, n = GameBalance.CampArcherEvery * 5;
                for (int i = 0; i < n; i++) if (HasBow(StartingKits.ForCamp(type))) archers++;
                Assert.AreEqual(5, archers, type.ToString());
            }
            for (int i = 0; i < GameBalance.CampArcherEvery * 2; i++) Assert.IsFalse(HasBow(StartingKits.ForCamp(HostileMobType.WarlordGuard)));
        }

        [Test]
        public void Kingdoms_EveryFewthWarriorShoots_AndOutranges()
        {
            NpcKingdom k = NpcKingdom.Found(Vector3.zero, "Test", 0, 10);
            var warriors = new List<HostileMobAI>();
            for (int i = 0; i < GameBalance.KingdomArcherEvery * 3; i++) warriors.Add(k.AddSubject(UnitRank.Warrior, k.Spot(), withModel: false));
            List<HostileMobAI> archers = warriors.Where(w => w.GetComponent<Equipment>().HasBow).ToList();
            Assert.AreEqual(3, archers.Count);
            foreach (HostileMobAI a in archers)
            {
                Assert.IsTrue(a.Combat.IsRanged, "a bow shoots");
                Assert.IsTrue(a.GetComponent<Equipment>()[EquipSlot.Shield].IsEmpty);
            }
            Assert.IsFalse(k.AddSubject(UnitRank.Person, k.Spot(), withModel: false).GetComponent<Equipment>().HasBow, "people work, they do not shoot");
        }
    }
}
