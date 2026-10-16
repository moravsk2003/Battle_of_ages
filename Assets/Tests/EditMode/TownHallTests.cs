using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    [TestFixture]
    public class TownHallTests
    {
        [Test]
        public void Levels_RaiseThePopulationCap()
        {
            Assert.AreEqual(TownHall.CapWithoutHall, TownHall.CapForLevel(0));
            int last = TownHall.CapForLevel(0);
            for (int l = 1; l <= TownHall.MaxLevel; l++)
            {
                Assert.Greater(TownHall.CapForLevel(l), last, $"Level {l} must allow more people.");
                last = TownHall.CapForLevel(l);
            }
        }

        [Test]
        public void EachUpgrade_NeedsOreOfTheNextTier()
        {
            for (int to = 2; to <= TownHall.MaxLevel; to++)
            {
                TownHall.UpgradeCost(to, out int wood, out int ore, out int tier);
                Assert.AreEqual(to - 1, tier, $"Level {to} needs tier {to - 1} ore.");
                Assert.Greater(wood, 0);
                Assert.Greater(ore, 0);
            }
        }

        [Test]
        public void OnlyOneTownHall_AndHousesStopAtTheCap()
        {
            GameObject hall = TownHall.CreatePrefab("TestHall");
            try
            {
                Assert.IsFalse(BuildingPlacementSystem.IsAllowed(BuildingType.TownHall, out string why), "A second town hall is not allowed.");
                Assert.IsNotNull(why);
                Assert.AreEqual(TownHall.CapForLevel(1), TownHall.PopulationCap);
                Assert.AreEqual(TownHall.HasRoom(), BuildingPlacementSystem.IsAllowed(BuildingType.WorkerHouse, out _));
                Assert.AreEqual(BuildingType.TownHall, hall.GetComponent<BuildingNode>().Type);
            }
            finally { Object.DestroyImmediate(hall); }
        }

        [Test]
        public void Pickaxe_RaisesMineOutput()
        {
            var mineObj = new GameObject("TestMine");
            MineNode mine = mineObj.AddComponent<MineNode>();
            var worker = new GameObject("Miner", typeof(CharacterController)).AddComponent<NpcResourceGatherer>();
            try
            {
                mine.AssignWorker(worker);
                Assert.AreEqual(1f, mine.OrePerSecond, 1e-4f, "A bare-handed miner digs 1 ore a second.");
                Equipment gear = worker.gameObject.AddComponent<Equipment>();
                gear.Equip(new Item(ItemType.Pickaxe, 4));
                Assert.AreEqual(ItemStatCalculator.CalculateToolEfficiency(4), mine.OrePerSecond, 1e-4f, "A pickaxe IV digs twice as fast.");
            }
            finally
            {
                Object.DestroyImmediate(worker.gameObject);
                Object.DestroyImmediate(mineObj);
            }
        }
    }
}
