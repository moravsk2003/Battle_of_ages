using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Player;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A new campaign's heroes (M1): the nearest kingdoms are real, the rest data that turn real as the hero comes and fold back as it leaves.</summary>
    public class CampaignKingdomsTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_realm_test_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        private static void Teleport(PlayerCombatController hero, Vector3 to)
        {
            to.y = InfiniteWorldManager.GetSampledTerrainHeight(to.x, to.z) + 1f;
            var cc = hero.GetComponent<CharacterController>();
            cc.enabled = false; hero.transform.position = to; cc.enabled = true;
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator ACampaign_HasItsHeroes_NearOnesReal_FarOnesData()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            int others = GameBalance.CampaignHeroes - 1;
            Assert.AreEqual(others, NpcKingdom.All.Count + KingdomRealm.Far.Count, "every hero of the campaign is there");
            Assert.AreEqual(GameBalance.RealKingdomsNearHome, NpcKingdom.All.Count, "the nearest ones are real");
            int free = 0;
            foreach (KingdomSave s in KingdomRealm.Far) if (s.Free) free++;
            foreach (NpcKingdom k in NpcKingdom.All) if (k.Free) free++;
            Assert.Greater(free, 0, "some heroes are free");

            // Go to a far one: it turns real; go home: it folds back into its state
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            KingdomSave target = KingdomRealm.Far[0];
            foreach (KingdomSave s in KingdomRealm.Far) if (s.Home.magnitude > target.Home.magnitude) target = s;
            string name = target.Name;
            Teleport(hero, target.Home + new Vector3(60f, 0f, 0f));
            yield return new WaitForSeconds(2.5f);
            NpcKingdom real = NpcKingdom.All.FirstOrDefault(k => k.KingdomName == name);
            Assert.IsNotNull(real, "it is real with the hero by it");
            Assert.AreEqual(GameBalance.KingdomStartPeople, real.Population, "with its people from the state");
            Assert.IsNotNull(real.King, "and its king");
            Teleport(hero, Vector3.zero + new Vector3(5f, 0f, 5f));
            yield return new WaitForSeconds(2.5f);
            Assert.IsFalse(NpcKingdom.All.Any(k => k.KingdomName == name), "far again: only its state");
            Assert.IsTrue(KingdomRealm.Far.Any(s => s.Name == name));

            Assert.IsTrue(SaveSystem.Save(1));
            Assert.IsTrue(SaveSystem.Load(1));
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(others, NpcKingdom.All.Count + KingdomRealm.Far.Count, "a save keeps them all");
        }
    }
}
