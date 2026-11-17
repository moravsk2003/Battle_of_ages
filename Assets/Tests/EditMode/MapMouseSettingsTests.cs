using NUnit.Framework;
using BattleOfAges.CameraSystem;

namespace BattleOfAges.Tests
{
    /// <summary>The map is dragged with the right button (turned with the middle one), or with the middle one (turned with Shift+middle).</summary>
    public class MapMouseSettingsTests
    {
        private bool before;

        [SetUp] public void SetUp() => before = MapMouseSettings.PanWithMiddle;
        [TearDown] public void TearDown() => MapMouseSettings.Set(before);

        [Test]
        public void RightButton_DragsTheMap_MiddleTurns()
        {
            MapMouseSettings.Set(false);
            Assert.IsTrue(MapMouseSettings.Panning(rightHeld: true, middleHeld: false, shiftHeld: false));
            Assert.IsFalse(MapMouseSettings.Panning(rightHeld: false, middleHeld: true, shiftHeld: false), "the middle one turns, not drags");
            Assert.IsTrue(MapMouseSettings.Turning(middleHeld: true, shiftHeld: false));
            Assert.AreEqual(1, MapMouseSettings.PanButton);
        }

        [Test]
        public void MiddleButton_DragsTheMap_ShiftMiddleTurns_RightIsLeftToOrders()
        {
            MapMouseSettings.Set(true);
            Assert.IsTrue(MapMouseSettings.Panning(rightHeld: false, middleHeld: true, shiftHeld: false));
            Assert.IsFalse(MapMouseSettings.Turning(middleHeld: true, shiftHeld: false), "dragging, not turning");
            Assert.IsTrue(MapMouseSettings.Turning(middleHeld: true, shiftHeld: true));
            Assert.IsFalse(MapMouseSettings.Panning(rightHeld: false, middleHeld: true, shiftHeld: true), "Shift+middle turns only");
            Assert.IsFalse(MapMouseSettings.Panning(rightHeld: true, middleHeld: false, shiftHeld: false), "the right button only gives orders");
            Assert.AreEqual(2, MapMouseSettings.PanButton);
        }
    }
}
