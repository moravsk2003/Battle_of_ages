using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>Zones on the map (M5 step 3): the brush marks and clears cells within the settlement's reach, on dry land only; a save restores them.</summary>
    [TestFixture]
    public class ZoneMapTests
    {
        [SetUp]
        public void SetUp()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            ZoneMap.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ZoneMap.Clear();
            WorldSettings.Set(0, 0f);
        }

        [Test]
        public void TheBrush_MarksAndClears()
        {
            Vector3 at = new Vector3(40f, 0f, 40f);
            int marked = ZoneMap.Paint(at, 14f, ZoneType.Fields, out string refused);
            Assert.Greater(marked, 3, "several cells under the brush");
            Assert.IsNull(refused);
            Assert.AreEqual(ZoneType.Fields, ZoneMap.At(at));
            Assert.AreEqual(marked, ZoneMap.Count(ZoneType.Fields));
            Assert.AreEqual(0, ZoneMap.Paint(at, 14f, ZoneType.Fields, out _), "marked already");
            int changed = ZoneMap.Paint(at, 14f, ZoneType.Housing, out _);
            Assert.AreEqual(marked, changed, "another zone takes the cells over");
            Assert.AreEqual(0, ZoneMap.Count(ZoneType.Fields));
            Assert.AreEqual(marked, ZoneMap.Erase(at, 14f));
            Assert.AreEqual(ZoneType.None, ZoneMap.At(at));
        }

        [Test]
        public void NotFarFromTheSettlement_NotOnWater_TradeFromHallTwo()
        {
            Assert.AreEqual(GameBalance.ZoneReachBase, ZoneMap.Reach(), 1e-3f);
            Assert.AreEqual(0, ZoneMap.Paint(new Vector3(ZoneMap.Reach() + 200f, 0f, 0f), 14f, ZoneType.Housing, out string far));
            StringAssert.Contains("далеко", far);
            Lakes.Lake lake = Lakes.All[0];
            Assert.IsNotNull(ZoneMap.Forbidden(ZoneMap.CellOf(new Vector3(lake.Center.x, 0f, lake.Center.y)), ZoneType.Fields), "not on a lake");
            Assert.AreEqual(0, ZoneMap.Paint(new Vector3(20f, 0f, 20f), 14f, ZoneType.Trade, out string trade), "no market without a hall II");
            StringAssert.Contains("ратуші II", trade);
        }

        [Test]
        public void MarkedLand_IsClearedOfTrees_ExceptAForest_AndHousesGrowWithTheEpoch()
        {
            Vector3 fields = new Vector3(40f, 0f, 40f), forest = new Vector3(-60f, 0f, 30f);
            ZoneMap.Paint(fields, 14f, ZoneType.Fields, out _);
            ZoneMap.Paint(forest, 14f, ZoneType.Forest, out _);
            Assert.IsTrue(ProceduralBiomeChunk.ZoneClearsTrees(fields));
            Assert.IsFalse(ProceduralBiomeChunk.ZoneClearsTrees(forest), "a forest keeps its trees");
            Assert.IsFalse(ProceduralBiomeChunk.ZoneClearsTrees(new Vector3(200f, 0f, 200f)), "unmarked land too");
            Assert.Less(Building.BuildingNode.HouseScale(1), Building.BuildingNode.HouseScale(3));
            Assert.Less(Building.BuildingNode.HouseScale(3), Building.BuildingNode.HouseScale(5));
        }

        [Test]
        public void NoNewSettlement_NextToARaidersCamp()
        {
            for (int x = 10; x < 200; x++)
                for (int z = 10; z < 200; z++)
                {
                    if (!ProceduralBiomeChunk.CampChunk(new Vector2Int(x, z), 64f)) continue;
                    StringAssert.Contains("табір", Building.TownHall.FoundingForbidden(new Vector3(x * 64f, 0f, z * 64f)));
                    return;
                }
            Assert.Fail("no camp in this part of the world");
        }

        [Test]
        public void ASave_RestoresTheZones()
        {
            ZoneMap.Paint(new Vector3(40f, 0f, 40f), 14f, ZoneType.Crafts, out _);
            ZoneMap.Paint(new Vector3(-60f, 0f, 30f), 14f, ZoneType.Fields, out _);
            int crafts = ZoneMap.Count(ZoneType.Crafts), fields = ZoneMap.Count(ZoneType.Fields);
            var saved = ZoneMap.Save();
            ZoneMap.Clear();
            Assert.AreEqual(0, ZoneMap.Count(ZoneType.Crafts));
            ZoneMap.Load(saved);
            Assert.AreEqual(crafts, ZoneMap.Count(ZoneType.Crafts));
            Assert.AreEqual(fields, ZoneMap.Count(ZoneType.Fields));
            Assert.AreEqual(ZoneType.Crafts, ZoneMap.At(new Vector3(40f, 0f, 40f)));
        }
    }
}
