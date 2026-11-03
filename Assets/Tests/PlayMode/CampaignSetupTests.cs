using System.Collections;
using System.Collections.Generic;
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

        private static List<KingdomSave> Empires()
        {
            var list = new List<KingdomSave>();
            foreach (KingdomSave s in KingdomRealm.AllStates()) if (s.Empire) list.Add(s);
            return list;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator EveryWorld_HasItsCentralEmpire_FarFromThePlayer()
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 4242, Size = 8000f, Heroes = 10, KingShare = 0.5f, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(3f);
            List<KingdomSave> empires = Empires();
            Assert.AreEqual(1, empires.Count, "one, from the start");
            KingdomSave empire = empires[0];
            Assert.AreEqual("Центральна імперія", empire.Name);
            Assert.IsFalse(empire.Free);
            Assert.IsTrue(empire.HasKing, "with its emperor");
            foreach (KingdomSave s in KingdomRealm.AllStates()) Assert.LessOrEqual(s.Home.sqrMagnitude, empire.Home.sqrMagnitude + 1f, "no one further from the player's start: " + s.Name);

            // A far empire: no work, the hall for nothing
            float wood = empire.Wood;
            KingdomSave far = null;
            foreach (KingdomSave s in KingdomRealm.Far) if (s.Empire) far = s;
            Assert.IsNotNull(far, "far at the start");
            for (int h = 0; h < 80; h++) KingdomRealm.Advance(far, 1f);
            Assert.AreEqual(wood, far.Wood, 1e-3f, "no one works");
            Assert.Greater(far.Hall, 1, "and the hall rises anyway");
            foreach (SubjectSave u in far.Subjects) Assert.AreEqual((int)UnitRank.Person, u.Rank, "work makes no warriors there");

            // Real: armed by rank; the emperor's death is the end, and a new epoch
            int epoch = CentralEmpire.WorldEpoch;
            KingdomRealm.Load(KingdomRealm.AllStates());
            NpcKingdom real = NpcKingdom.FromSave(JsonUtility.FromJson<KingdomSave>(JsonUtility.ToJson(far)));
            Assert.IsTrue(real.IsEmpire, "a save keeps it");
            Assert.AreEqual(GameBalance.EmpireHeroGear, real.King.GetComponent<BattleOfAges.Crafting.Equipment>().LevelOf(BattleOfAges.Crafting.ItemType.Sword), "the emperor's sword");
            HostileMobAI person = real.Subjects[0];
            Assert.AreEqual(GameBalance.EmpirePersonGear, person.GetComponent<BattleOfAges.Crafting.Equipment>().LevelOf(BattleOfAges.Crafting.ItemType.Sword), "a person's sword");
            real.King.Health.TakeDamage(100000000, ignoreArmor: true);
            yield return null;
            Assert.IsTrue(real.IsDefeated, "no succession");
            Assert.AreEqual(epoch + 1, CentralEmpire.WorldEpoch, "a new epoch");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ALoad_LooksWhereTheSaveLooked()
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 4242, Size = 8000f, Heroes = 10, KingShare = 0.5f, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            GameObject hero = BattleOfAges.Managers.GameContext.Player;
            hero.transform.rotation = Quaternion.Euler(0f, 137f, 0f);
            hero.GetComponent<FirstPersonController>().Pitch = 33f;
            Assert.IsTrue(SaveSystem.Save(1));
            hero.transform.rotation = Quaternion.identity;
            hero.GetComponent<FirstPersonController>().Pitch = -10f;
            Assert.IsTrue(SaveSystem.Load(1));
            yield return new WaitForSeconds(2f);
            hero = BattleOfAges.Managers.GameContext.Player;
            Assert.AreEqual(137f, hero.transform.eulerAngles.y, 1f, "the way it faced");
            Assert.AreEqual(33f, hero.GetComponent<FirstPersonController>().Pitch, 0.5f, "and the tilt of the view");
            Assert.AreEqual(33f, Mathf.DeltaAngle(0f, Camera.main.transform.localEulerAngles.x), 0.5f, "on the camera itself");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator ThePlayerBearingTheArtifact_IsTheOnlyEmpire()
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 4242, Size = 8000f, Heroes = 10, KingShare = 0.5f, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.CentralEmpire });
            yield return new WaitForSeconds(3f);
            Assert.IsTrue(CentralEmpire.Active, "the player's");
            Assert.AreEqual(0, Empires().Count, "and no other");
            GameObject hero = BattleOfAges.Managers.GameContext.Player;
            if (hero != null) Object.Destroy(hero);
            int epoch = CentralEmpire.WorldEpoch;
            CentralEmpire.SetActive(false, null); // (not for the tests after this one)
            CentralEmpire.WorldEpoch = epoch;
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
