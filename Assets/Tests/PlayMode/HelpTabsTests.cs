using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.InputSystem;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The help in tabs: each fits its window, says the player's own keys, and has no code left in its words.</summary>
    public class HelpTabsTests
    {
        [TearDown]
        public void Teardown()
        {
            if (HelpUI.IsVisible) HelpUI.Instance.Close();
        }

        [Test]
        public void EveryTab_HasWords_AndNoCodeInThem()
        {
            for (int i = 0; i < HelpUI.TabNames.Length; i++)
            {
                string text = HelpUI.TabText(i);
                Assert.Greater(text.Length, 50, HelpUI.TabNames[i]);
                StringAssert.DoesNotContain("{", text, $"{HelpUI.TabNames[i]}: a key not filled in");
                StringAssert.DoesNotContain("KeyAction", text, HelpUI.TabNames[i]);
            }
        }

        [Test]
        public void TheTabs_SayThePlayersOwnKeys()
        {
            string key = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.WorkerCommandUI));
            StringAssert.Contains(key, HelpUI.TabText(3), "the people's window, on the people's tab");
            key = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.PlayerList));
            StringAssert.Contains(key, HelpUI.TabText(6), "who is in the game, on the network's");
        }

        [UnityTest]
        public IEnumerator EveryTab_FitsItsWindow()
        {
            HelpUI help = HelpUI.Instance;
            Assert.IsNotNull(help, "it starts by itself");
            help.Open();
            yield return null;
            Assert.IsTrue(GameWindows.HelpOpen);
            for (int i = 0; i < HelpUI.TabNames.Length; i++)
            {
                help.ShowTab(i);
                yield return null;
                Assert.AreEqual(i, help.CurrentTab);
                Assert.LessOrEqual(help.PageHeight, HelpUI.PageRoom, $"«{HelpUI.TabNames[i]}» runs out of the window");
            }
            help.Close();
            Assert.IsFalse(GameWindows.HelpOpen);
        }
    }
}
