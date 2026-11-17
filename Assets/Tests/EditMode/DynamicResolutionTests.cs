using NUnit.Framework;
using BattleOfAges.Managers;

namespace BattleOfAges.Tests
{
    /// <summary>The dynamic resolution aims at 60 frames a second, and lowers the picture only where the graphics card holds the frame.</summary>
    public class DynamicResolutionTests
    {
        [Test]
        public void ASlowFrame_TheCardBusy_LowersTheResolution()
        {
            Assert.AreEqual(-1, GraphicsQuality.Verdict(25f, 22f), "40 frames a second, the card busy for 22 ms of 25");
            Assert.AreEqual(-1, GraphicsQuality.Verdict(20f, 0f), "the card's time not known: the frame's own decides");
        }

        [Test]
        public void ASlowFrame_TheProcessorBusy_LeavesThePictureAlone()
        {
            Assert.AreNotEqual(-1, GraphicsQuality.Verdict(25f, 8f), "fewer pixels do not help a slow processor");
        }

        [Test]
        public void SixtyFramesASecond_Stays_AndAnIdleCardLetsItUp()
        {
            Assert.AreEqual(0, GraphicsQuality.Verdict(16.7f, 14f), "60 frames a second with the card near its limit: as it is");
            Assert.AreEqual(1, GraphicsQuality.Verdict(16.7f, 6f), "held at 60 by the screen, the card idle: up again");
            Assert.AreEqual(1, GraphicsQuality.Verdict(9f, 0f));
        }

        [Test]
        public void ASlowProcessor_StepsTheShadowsDown_NotASlowCard()
        {
            Assert.AreEqual(1, GraphicsQuality.CpuVerdict(22f, 8f, 21f), "45 frames a second, the main thread busy for 21 ms of 22");
            Assert.AreEqual(0, GraphicsQuality.CpuVerdict(22f, 20f, 21f), "the card is what holds it: the resolution's affair");
            Assert.AreEqual(0, GraphicsQuality.CpuVerdict(16.7f, 5f, 12f), "60 frames a second: as it is");
            Assert.AreEqual(-1, GraphicsQuality.CpuVerdict(16.7f, 5f, 6f), "held at 60 by the screen with the main thread half idle: back up");
            Assert.AreEqual(0, GraphicsQuality.CpuVerdict(30f, 0f, 0f), "the main thread's time not known: nothing is touched");
        }
    }
}
