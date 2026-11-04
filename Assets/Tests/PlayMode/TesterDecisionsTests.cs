using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Player;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>What testers asked for and the author decided (docs/KNOWN_ISSUES.md): raiders, the class's weapon, hunger, a raid's target.</summary>
    public class TesterDecisionsTests
    {
        private string folder;
        private float hourSeconds;

        [SetUp]
        public void Setup()
        {
            hourSeconds = GameBalance.GameHourSeconds;
            folder = Path.Combine(Path.GetTempPath(), "boa_decisions_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            GameBalance.GameHourSeconds = hourSeconds;
            Time.timeScale = 1f;
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        /// <summary>
        /// A campaign's scene goes with its test: a hero left standing is found by name by the tests after this one (a gate
        /// opened for it, not for theirs) and, kilometres from their fights, slowed their mobs' thinking to once in 2 s.
        /// </summary>
        [UnityTearDown]
        public IEnumerator LeaveTheScene()
        {
            UnityEngine.SceneManagement.Scene empty = UnityEngine.SceneManagement.SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(empty);
            for (int i = UnityEngine.SceneManagement.SceneManager.sceneCount - 1; i >= 0; i--)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ARaider_TurnsOnAnEnemyByItsRoad_AndLeavesOneFarOff()
        {
            Vector3 at = new Vector3(5600f, 0f, 5600f);
            NpcKingdom k = NpcKingdom.Found(at, "Набіг", 0);
            NpcKingdom other = NpcKingdom.Found(at + new Vector3(200f, 0f, 0f), "Ціль", 0);
            HostileMobAI raider = k.AddSubject(UnitRank.Warrior, at + new Vector3(5f, 0f, 0f));
            HostileMobAI near = other.AddSubject(UnitRank.Person, raider.transform.position + new Vector3(10f, 0f, 0f));
            HostileMobAI farOff = other.AddSubject(UnitRank.Person, raider.transform.position + new Vector3(30f, 0f, 0f));
            yield return null;
            yield return null;
            raider.OrderAttack(farOff.Health);
            yield return null;

            Assert.IsTrue(raider.EngageNear(GameBalance.RaidEngageRadius), "one 10 m off comes first");
            Assert.AreEqual(near.Health, raider.CurrentCombatTarget);
            Assert.IsFalse(raider.EngageNear(GameBalance.RaidEngageRadius), "it stays in the fight at hand");

            raider.OrderAttack(farOff.Health);
            near.Health.TakeDamage(int.MaxValue / 2, ignoreArmor: true);
            yield return null;
            yield return null;
            Assert.IsFalse(raider.EngageNear(5f), "nothing this near: it goes on");
            Assert.AreEqual(farOff.Health, raider.CurrentCombatTarget);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AnArchersCampaign_StartsWithABow_AndTheArtifactIsABow()
        {
            var setup = new CampaignSetup { Size = 8000f, Seed = 41, Heroes = 2, KingShare = 0f, Class = HeroClass.Archer, HasArtifact = true, Artifact = ArtifactType.SwordX5 };
            SaveSystem.NewGame(setup);
            yield return new WaitForSeconds(3f);
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Equipment gear = hero.GetComponent<Equipment>();
            Assert.IsTrue(gear.HasBow, "a bow in hand from the start");
            Assert.IsTrue(gear.IsLocked(EquipSlot.Weapon), "the artifact holds the slot");
            Assert.AreEqual("Лук ×5", Artifacts.Name(ArtifactType.SwordX5, HeroClass.Archer));
            Assert.AreEqual("Меч ×5", Artifacts.Name(ArtifactType.SwordX5, HeroClass.Tank));
            Assert.AreEqual("Посох ×5", Artifacts.Name(ArtifactType.SwordX5, HeroClass.Mage));
            Assert.IsTrue(hero.GetComponent<CombatUnit>().IsRanged, "and it shoots");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator WithAnEmptyLarder_PeopleDie_OneByOne()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            TownHall hall = TownHall.Current;
            GameObject built = null;
            if (hall == null)
            {
                PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
                built = TownHall.CreatePrefab("Starve_Hall");
                built.transform.position = hero.transform.position + new Vector3(30f, 0f, 30f);
                yield return null;
                hall = built.GetComponent<TownHall>();
            }
            int before = WorkerRegistry.ActiveWorkers.Count(w => w != null);
            Assert.Greater(before, 1, "the settlement has people");
            GameBalance.GameHourSeconds = 0.2f; // one dies every 0.6 s
            float end = Time.time + 2.5f;
            while (Time.time < end)
            {
                hall.Stock.SetFood(0f);
                yield return null;
            }
            GameBalance.GameHourSeconds = hourSeconds;
            hall.Stock.SetFood(GameBalance.FoodStart);
            int after = WorkerRegistry.ActiveWorkers.Count(w => w != null && w.GetComponent<HealthComponent>().IsAlive);
            if (built != null) Object.Destroy(built);
            TestContext.WriteLine($"{before} before, {after} after 2.5 s");
            Assert.Less(after, before, "hunger took some");
            Assert.Greater(after, before - 7, "one by one, not all at once");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator InAForestZone_AStumpGrowsBack_ElsewhereNot()
        {
            Vector3 forest = new Vector3(5200f, 0f, 5200f), bare = new Vector3(5400f, 0f, 5200f);
            GameObject a = Utils.SteppeTreeBuilder.CreateSteppeTree("Regrow_A", 1), b = Utils.SteppeTreeBuilder.CreateSteppeTree("Regrow_B", 1);
            a.transform.position = forest;
            b.transform.position = bare;
            ResourceNodeTree inForest = a.GetComponent<ResourceNodeTree>(), outside = b.GetComponent<ResourceNodeTree>();
            yield return null;
            inForest.ApplySavedState(TreeState.Depleted, 0);
            outside.ApplySavedState(TreeState.Depleted, 0);
            Vector2Int cell = ZoneMap.CellOf(forest);
            ZoneMap.SetRaw(cell, ZoneType.Forest);
            try
            {
                for (int h = 0; h < GameBalance.ForestRegrowHours - 1; h++) ZoneWork.Hour();
                Assert.AreEqual(TreeState.Depleted, inForest.CurrentState, "not yet");
                ZoneWork.Hour();
                Assert.AreEqual(TreeState.WholeTree, inForest.CurrentState, "grown back");
                Assert.AreEqual(TreeState.Depleted, outside.CurrentState, "out of the zone it stays a stump");
            }
            finally
            {
                ZoneMap.SetRaw(cell, ZoneType.None);
                Object.Destroy(a);
                Object.Destroy(b);
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator InACraftsZone_AMasterMakesGoods_WhileTheStockCanSpareThem()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            GameObject built = WorkbenchNode.CreateWorkbenchPrefab("Crafts_Bench");
            built.transform.position = hero.transform.position + new Vector3(20f, 0f, 5f);
            WorkbenchNode bench = built.GetComponent<WorkbenchNode>();
            ZoneMap.SetRaw(ZoneMap.CellOf(built.transform.position), ZoneType.Crafts);
            ResourceManager rm = ResourceManager.Instance;
            yield return null;

            rm.ConsumeResource(ResourceType.Wood, rm.WoodCount);
            ZoneWork.Hour();
            Assert.IsFalse(bench.Queue.HasWork, "with nothing to spare it makes nothing");
            Assert.AreEqual(0, bench.CalledWorkerCount);

            rm.AddResource(ResourceType.Wood, 1000);
            rm.AddOre(0, 200);
            ZoneWork.Hour();
            Assert.IsTrue(bench.Queue.HasWork, "with plenty a good is under way");
            Assert.AreEqual(1, bench.CalledWorkerCount, "and a master is called");
            bench.UnassignLastWorker();
            ZoneMap.SetRaw(ZoneMap.CellOf(built.transform.position), ZoneType.None);
            Object.Destroy(built);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ASave_KeepsItsName_ThroughResaves()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            Assert.IsTrue(SaveSystem.Save(2, "  Перед набігом "));
            Assert.AreEqual("Перед набігом", SaveSystem.Meta(2).Title);
            Assert.IsTrue(SaveSystem.Save(2), "saved again without a new name");
            Assert.AreEqual("Перед набігом", SaveSystem.Meta(2).Title);
            Assert.IsTrue(SaveSystem.Save(2, "Після"));
            Assert.AreEqual("Після", SaveSystem.Meta(2).Title);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AutoWalk_CarriesTheHeroForward_AndTheMapEndsIt()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            FirstPersonController fp = Object.FindFirstObjectByType<FirstPersonController>();
            Vector3 was = fp.transform.position;
            fp.AutoWalking = true;
            yield return new WaitForSeconds(1.5f);
            fp.AutoWalking = false; // (a hero left walking takes the viewer away from the tests after this one)
            Vector3 moved = fp.transform.position - was;
            moved.y = 0f;
            CharacterController cc = fp.GetComponent<CharacterController>();
            cc.enabled = false;
            fp.transform.position = was;
            cc.enabled = true;
            Assert.Greater(moved.magnitude, 3f, "it walks by itself");
            Assert.Greater(Vector3.Dot(moved.normalized, fp.transform.forward), 0.9f, "forward");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Settings_ShowOneCategoryAtATime()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            UI.PauseMenuController menu = Object.FindFirstObjectByType<UI.PauseMenuController>(FindObjectsInactive.Include);
            Assert.IsNotNull(menu);
            menu.OpenSettings();
            yield return null;
            string[] pages = { "Керування", "Звук", "Графіка", "Клавіші", "Гра" };
            for (int i = 0; i < pages.Length; i++)
            {
                menu.ShowSettingsTab(i);
                yield return null;
                foreach (Transform t in menu.GetComponentsInChildren<Transform>(true))
                    for (int p = 0; p < pages.Length; p++)
                        if (t.name == "Page_" + pages[p]) Assert.AreEqual(p == i, t.gameObject.activeSelf, t.name + " with the tab " + pages[i]);
            }
            Transform keys = menu.GetComponentsInChildren<Transform>(true).First(t => t.name == "Row_AutoWalk");
            Assert.IsNotNull(keys, "the auto-walk key can be set");
            menu.CloseSettings();
            menu.ResumeGame();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AKing_GoesForThePlayer_ThoughAnotherIsAlittleNearer()
        {
            Vector3 at = new Vector3(4000f, 0f, -4000f);
            NpcKingdom k = NpcKingdom.Found(at, "Загарбник", 1);
            GameObject built = TownHall.CreatePrefab("Target_Hall");
            built.transform.position = k.Home + new Vector3(1000f, 0f, 0f);
            NpcKingdom neighbour = NpcKingdom.Found(k.Home + new Vector3(-800f, 0f, 0f), "Сусід", 1);
            neighbour.Economy.HallLevel = 1;
            yield return null;
            var targets = k.RaidTargets();
            Object.Destroy(built);
            Assert.Greater(targets.Count, 1);
            Assert.IsNotNull(targets[0].GetComponent<TownHall>(), "the player's hall at 1000 m before a king at 800 m");
        }
    }
}
