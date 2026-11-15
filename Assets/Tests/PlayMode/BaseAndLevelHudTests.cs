using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The player knows where its base is (BaseCompass), and is told to spend a new level's points (LevelUpBanner).</summary>
    public class BaseAndLevelHudTests
    {
        private static void Put(GameObject player, Vector3 at, Vector3 facing)
        {
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(facing));
            if (cc != null) cc.enabled = true;
            Camera cam = Utils.ViewCamera.Current;
            if (cam != null) cam.transform.rotation = Quaternion.LookRotation(facing); // (the camera's own pitch aside)
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TheBase_IsShownWhereItLies_AndANewLevel_SaysWhichKeySpendsIt()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            GameObject player = Managers.GameContext.Player;
            Assert.IsNotNull(player, "the scene's player");
            GameObject built = null;
            TownHall hall = TownHall.Current;
            if (hall == null)
            {
                // The start has no hall yet: one by the player, as the player raises it
                built = TownHall.CreatePrefab("TestHall");
                Vector3 spot = player.transform.position + new Vector3(0f, 0f, 12f);
                spot.y = RTS.RtsUnit.GetTerrainHeight(spot);
                built.transform.position = spot;
                yield return null;
                hall = TownHall.Current;
            }
            Assert.IsNotNull(hall, "a hall to call home");
            Vector3 home = hall.transform.position;
            Vector3 east = home + new Vector3(100f, 2f, 0f);

            // Facing north, the base lies to the left, out of the view: at the left edge, with an arrow
            Put(player, east, Vector3.forward);
            yield return null;
            yield return null;
            Assert.IsTrue(BaseCompass.Shown, "far from home, the base is marked");
            Assert.Less(BaseCompass.Place.x, -200f, "to the left");
            StringAssert.StartsWith("‹", BaseCompass.Said);
            StringAssert.Contains("100", BaseCompass.Said, "with how far it is");

            // Facing it: in the middle
            Put(player, east, Vector3.left);
            yield return null;
            yield return null;
            Assert.AreEqual(0f, BaseCompass.Place.x, 40f, "straight ahead");
            StringAssert.DoesNotContain("‹", BaseCompass.Said);

            // At home: no mark
            Put(player, home + new Vector3(8f, 2f, 0f), Vector3.forward);
            yield return null;
            yield return null;
            Assert.IsFalse(BaseCompass.Shown, "at home it needs no mark");

            // A new level: the banner with the key, gone once the hero window is open
            HeroProgress hero = player.GetComponent<HeroProgress>();
            Assert.IsNotNull(hero);
            hero.AddXp(hero.XpToNextLevel);
            yield return null;
            Assert.IsTrue(LevelUpBanner.Shown, "a new level is said");
            HeroUI.Instance.Open();
            yield return null;
            yield return null;
            Assert.IsFalse(LevelUpBanner.Shown, "and goes once its window is open");
            HeroUI.Instance.Close();
            if (built != null) Object.Destroy(built);
        }
    }
}
