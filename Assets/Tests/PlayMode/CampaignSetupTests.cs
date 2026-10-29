using System.Collections;
using System.IO;
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
    /// <summary>The choices of the new campaign screen (M1 step 6) make the campaign: its world, its heroes, the player's class and artifact.</summary>
    public class CampaignSetupTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_setup_test_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
            GameBalance.CampaignHeroes = 16;
            GameBalance.CampaignKingShare = 0.75f;
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator TheChosenWorldAndHero_AreTheCampaigns()
        {
            var setup = new CampaignSetup { Seed = 4242, Size = 8000f, Heroes = 10, KingShare = 0.5f, Class = HeroClass.Mage, HasArtifact = true, Artifact = ArtifactType.Vampiric };
            SaveSystem.NewGame(setup);
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(4242, WorldSettings.Seed, "the typed seed");
            Assert.AreEqual(8000f, WorldSettings.Size);
            Assert.AreEqual(9, NpcKingdom.All.Count + KingdomRealm.Far.Count, "nine other heroes");
            int free = 0;
            foreach (KingdomSave s in KingdomRealm.Far) if (s.Free) free++;
            foreach (NpcKingdom k in NpcKingdom.All) if (k.Free) free++;
            Assert.That(free, Is.InRange(4, 5), "half of them kings");

            PlayerCombatController player = Object.FindFirstObjectByType<PlayerCombatController>();
            Assert.AreEqual(HeroClass.Mage, player.GetComponent<HeroProgress>().Class, "the chosen class");
            HeroArtifacts bag = player.GetComponent<HeroArtifacts>();
            Assert.IsTrue(bag.StartChosen, "the starting artifact is taken: the hero window does not offer it");
            Assert.IsTrue(bag.Has(ArtifactType.Vampiric));
        }
    }
}
