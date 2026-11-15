using NUnit.Framework;
using BattleOfAges.Combat;
using BattleOfAges.Player;

namespace BattleOfAges.Tests
{
    /// <summary>Run or walk: auto-run on (the default) runs and the held key walks; off, the key runs.</summary>
    public class MoveSettingsTests
    {
        [Test]
        public void AutoRun_FlipsWhatTheKeyMeans()
        {
            bool was = MoveSettings.AutoRun;
            try
            {
                MoveSettings.SetAutoRun(true);
                Assert.IsTrue(MoveSettings.Running(false), "auto-run: runs");
                Assert.IsFalse(MoveSettings.Running(true), "and Shift walks");
                MoveSettings.SetAutoRun(false);
                Assert.IsFalse(MoveSettings.Running(false), "no auto-run: walks");
                Assert.IsTrue(MoveSettings.Running(true), "and Shift runs");
            }
            finally { MoveSettings.SetAutoRun(was); }
            Assert.That(GameBalance.WalkFactor, Is.InRange(0.3f, 0.7f), "a walk is about half the run");
        }
    }
}
