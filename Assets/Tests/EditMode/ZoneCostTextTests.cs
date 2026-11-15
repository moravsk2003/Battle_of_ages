using NUnit.Framework;
using BattleOfAges.Building;
using BattleOfAges.UI;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>Every zone says what its building costs and how many cells call for one (the zone button's tip and the brush's line).</summary>
    public class ZoneCostTextTests
    {
        [Test]
        public void EveryZone_SaysItsBuildingsPrice_AndCells()
        {
            foreach (ZoneType zone in new[] { ZoneType.Housing, ZoneType.Fields, ZoneType.Forest, ZoneType.Crafts, ZoneType.Mining, ZoneType.Trade })
            {
                BuildingDefinition def = BuildingData.GetDefinition(ZoneGrower.BuildingOf(zone));
                string said = ZoneToolbar.CostText(zone);
                StringAssert.Contains(def.WoodCost.ToString(), said, zone + ": the wood");
                if (def.OreCost > 0) StringAssert.Contains(def.OreCost.ToString(), said, zone + ": the ore");
                StringAssert.Contains(ZoneGrower.CellsPerBuilding(zone).ToString(), said, zone + ": the cells for one");
            }
            Assert.AreEqual("", ZoneToolbar.CostText(ZoneType.None));
        }
    }
}
