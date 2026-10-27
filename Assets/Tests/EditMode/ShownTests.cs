using NUnit.Framework;
using BattleOfAges.UI;

namespace BattleOfAges.Tests
{
    /// <summary>The change-only label helper: a label is rebuilt the first time and whenever its number changes, never otherwise.</summary>
    [TestFixture]
    public class ShownTests
    {
        [Test]
        public void Changed_OnlyWhenTheValueChanges()
        {
            var s = new Shown();
            Assert.IsTrue(s.Changed(5), "the first time it must be written");
            Assert.IsFalse(s.Changed(5));
            Assert.IsTrue(s.Changed(6));
            Assert.IsTrue(s.Changed(5));
            s.Reset();
            Assert.IsTrue(s.Changed(5), "after a reset it is written again");
        }

        [Test]
        public void Key_TellsApartOrderAndValues()
        {
            Assert.AreNotEqual(Shown.Key(1, 2), Shown.Key(2, 1));
            Assert.AreNotEqual(Shown.Key(10, 0), Shown.Key(0, 10));
            Assert.AreEqual(Shown.Key(3, 4, 5), Shown.Key(3, 4, 5));
            Assert.AreNotEqual(Shown.Key(500, 499, 2, 1), Shown.Key(500, 500, 2, 1));
        }
    }
}
