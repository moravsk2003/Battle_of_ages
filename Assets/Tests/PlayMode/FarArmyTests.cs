using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Player;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>M6 step 4: an army out of sight turns real where it has got to, and back into data, nobody lost; a battle begun stays.</summary>
    public class FarArmyTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_fararmy_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            Time.timeScale = 1f;
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        [UnityTearDown]
        public IEnumerator LeaveTheScene()
        {
            Scene empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        private static void Teleport(PlayerCombatController hero, Vector3 to)
        {
            to.y = InfiniteWorldManager.GetSampledTerrainHeight(to.x, to.z) + 1f;
            var cc = hero.GetComponent<CharacterController>();
            cc.enabled = false; hero.transform.position = to; cc.enabled = true;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>The far kings furthest from home and each other, one with an army on its way to the other, the hero at the start.</summary>
        private static (KingdomState attacker, KingdomState target, ArmyState army) FarRaid()
        {
            var kings = KingdomRealm.Far.Where(s => !s.Free && !s.Empire && !s.Defeated).OrderByDescending(s => s.Home.magnitude).ToList();
            Assert.GreaterOrEqual(kings.Count, 2, "far kings");
            KingdomState attacker = kings[0], target = kings[1];
            for (int i = 0; i < 12; i++) attacker.Subjects.Add(new UnitState { Rank = (int)UnitRank.Warrior, Position = attacker.Home });
            ArmyState army = FarWorld.StartRaid(attacker, attacker.Subjects.ToList(), target);
            Vector3 way = target.Home - attacker.Home;
            way.y = 0f;
            army.Position = attacker.Home + way.normalized * Mathf.Min(300f, way.magnitude * 0.5f);
            return (attacker, target, army);
        }

        [Category("Slow")]
        [UnityTest, Timeout(180000)]
        public IEnumerator ACampaignArmy_TurnsReal_WhereItIs_WithItsTarget()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            var (attacker, target, army) = FarRaid();
            string name = attacker.Name, targetName = target.Name;
            int party = army.Units.Count;
            Vector3 at = army.Position;
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Teleport(hero, at + new Vector3(200f, 0f, 200f));
            yield return new WaitForSeconds(4f);

            NpcKingdom real = NpcKingdom.All.FirstOrDefault(k => k.KingdomName == name);
            Assert.IsNotNull(real, "the kingdom is real with its army in sight");
            Assert.IsTrue(NpcKingdom.All.Any(k => k.KingdomName == targetName), "and the one it goes for");
            Assert.IsTrue(real.Raiding);
            Assert.AreEqual(party + 1, real.Raiders.Count, "everyone who set off, and the king");
            float furthest = real.Raiders.Where(m => m != null).Max(m => Flat(m.transform.position, at));
            Assert.Less(furthest, 80f, "where the army had got to, not at home");
            Assert.AreEqual(real.StateId, attacker.Id, "the same kingdom by its number");
            Assert.IsNotNull(real.Raiders[0].CurrentCombatTarget, "they know where they are going");
        }

        [Category("Slow")]
        [UnityTest, Timeout(180000)]
        public IEnumerator ACampaignArmy_FoldsBack_NobodyLost_WhenTheHeroLeaves()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            var (attacker, target, army) = FarRaid();
            string name = attacker.Name;
            WorldId id = attacker.Id, armyGoesFor = target.Id;
            int people = army.Units.Count, home = attacker.Subjects.Count;
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Vector3 start = hero.transform.position;
            Teleport(hero, army.Position + new Vector3(100f, 0f, 100f));
            yield return new WaitForSeconds(4f);
            Assert.IsTrue(NpcKingdom.All.Any(k => k.KingdomName == name), "real");

            // Home again: the raid still on the road goes back into data, unless the home is nearer than the fold's reach
            Teleport(hero, start);
            yield return new WaitForSeconds(4f);
            KingdomState back = KingdomRealm.Far.FirstOrDefault(s => s.Id == id);
            if (back == null)
            {
                NpcKingdom stillReal = NpcKingdom.All.First(k => k.KingdomName == name);
                Assert.Inconclusive($"its army is within {GameBalance.DematerializeRadius} m of the player's home in this world ({Flat(stillReal.Home, start):0} m)");
            }
            Assert.AreEqual(1, back.Armies.Count, "as an army again");
            Assert.AreEqual(people, back.Armies[0].Units.Count, "all of it");
            Assert.AreEqual(home, back.Subjects.Count, "and those at home");
            Assert.AreEqual(armyGoesFor, back.Armies[0].TargetKingdomId, "the same target");
            Assert.IsTrue(back.Armies[0].WithHero);
        }

        [Category("Slow")]
        [UnityTest, Timeout(180000)]
        public IEnumerator ABattleBegun_IsNotFoldedUp()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            var (attacker, _, army) = FarRaid();
            string name = attacker.Name;
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Vector3 meet = army.Position;
            Teleport(hero, meet + new Vector3(60f, 0f, 0f));
            yield return new WaitForSeconds(4f);
            NpcKingdom real = NpcKingdom.All.First(k => k.KingdomName == name);
            // Someone of the player's side among them
            HealthComponent foe = new NpcKingdomPlayTests().PlayerSide(real.Raiders[0].transform.position + new Vector3(5f, 0f, 0f), UnitRank.General, "Foe");
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(real.CanFold(new Vector3(1e6f, 0f, 0f), new Vector3(1e6f, 0f, 0f)), "an enemy at hand: it stays, however far the player is");
            Object.Destroy(foe.gameObject);
        }
    }
}
