using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Player;
using BattleOfAges.Resources;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A new game is a world of its own (M1): a seed and a size, the scene's people and trees on its ground, kept by a save.</summary>
    public class NewWorldTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_world_test_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            SaveSystem.FolderOverride = null;
            WorldSettings.Set(0, 0f); // the fixed world for the tests after
            BattleOfAges.AI.NpcKingdom.RemoveAll();
            BattleOfAges.AI.KingdomRealm.Clear();
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        private static float Ground(Vector3 p) => InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);

        [UnityTest, Timeout(180000)]
        public IEnumerator ANewGame_HasItsOwnWorld_WithEveryoneOnTheGround_AndASaveKeepsIt()
        {
            SaveSystem.NewGame();
            yield return null;
            yield return new WaitForSeconds(3f);
            int seed = WorldSettings.Seed;
            Assert.AreNotEqual(0, seed, "a new game gets a seed");
            Assert.AreEqual(WorldSettings.DefaultSize, WorldSettings.Size, "and a bounded world");

            PlayerCombatController hero = Object.FindFirstObjectByType<PlayerCombatController>();
            Assert.IsNotNull(hero);
            Assert.AreEqual(Ground(hero.transform.position), hero.transform.position.y, 1.5f, "the hero stands on this world's ground");
            int trees = 0, onGround = 0;
            foreach (ResourceNodeTree t in Object.FindObjectsByType<ResourceNodeTree>(FindObjectsSortMode.None))
            {
                if (t.transform.root.GetComponent<ProceduralBiomeChunk>() != null || t.GetComponentInParent<ProceduralBiomeChunk>() != null) continue; // chunk trees are made on it anyway
                trees++;
                if (Mathf.Abs(t.transform.position.y - Ground(t.transform.position)) < 1.5f) onGround++;
            }
            Assert.Greater(trees, 0, "the scene has its own trees");
            Assert.AreEqual(trees, onGround, "every scene tree stands on the new ground");

            GameObject baseGround = GameObject.Find(WorldSettings.BaseGroundName);
            if (baseGround != null)
                Assert.IsTrue(baseGround.GetComponent<Collider>().Raycast(new Ray(new Vector3(10f, 500f, 10f), Vector3.down), out RaycastHit g, 1000f)
                              && Mathf.Abs(g.point.y - Ground(g.point)) < 0.5f, "the base's own ground is remade on this world's relief");
            string dir = System.Environment.GetEnvironmentVariable("CAPTURE_DIR"); // a look at the new world
            if (!string.IsNullOrEmpty(dir))
            {
                var cap = new CaptureRenderer(dir, 2);
                Transform eye = hero.GetComponentInChildren<Camera>().transform;
                cap.Shoot(0, "first_person", eye.position, eye.position + eye.forward * 10f);
                cap.Camera.orthographic = true; cap.Camera.orthographicSize = 90f;
                cap.Shoot(1, "map", hero.transform.position + new Vector3(0f, 150f, -86f), hero.transform.position);
                cap.Finish();
            }

            Vector3 probe = hero.transform.position + new Vector3(200f, 0f, 150f);
            float h = Ground(probe);
            Assert.IsTrue(SaveSystem.Save(1));
            WorldSettings.Set(0, 0f); // another world in between
            Assert.IsTrue(SaveSystem.Load(1));
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(seed, WorldSettings.Seed, "the save brings its world back");
            Assert.AreEqual(h, Ground(probe), 1e-3f);
        }
    }
}
