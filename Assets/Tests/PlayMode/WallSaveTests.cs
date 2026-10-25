using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A save keeps the walls (§10.5): each segment's ore (its strength), its damage and turn, and the walls' level.</summary>
    public class WallSaveTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_wall_save_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            SaveSystem.FolderOverride = null;
            Fortifications.Level = 1;
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        [UnityTest]
        public IEnumerator SaveThenLoad_KeepsTheWallsOreDamageTurnAndLevel()
        {
            yield return SceneManager.LoadSceneAsync(SaveSystem.GameScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);
            GameObject player = GameObject.Find("Player");
            Assert.IsNotNull(player);

            GameObject gate = BuildingPlacementSystem.CreateBuilding(BuildingType.WallGate);
            Vector3 at = player.transform.position + new Vector3(12f, 0f, 9f);
            at.y = RTS.RtsUnit.GetTerrainHeight(at);
            gate.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 30f, 0f));
            gate.GetComponent<WallSegment>().OreTier = 2;
            gate.GetComponent<BuildingNode>().OnBuilt();
            HealthComponent hp = gate.GetComponent<HealthComponent>();
            Assert.AreEqual(WallSegment.MaxHealthFor(BuildingType.WallGate, 2), hp.MaxHealth, "ore tier 2: stronger");
            hp.TakeDamage(700);
            int left = hp.CurrentHealth;
            Fortifications.Level = 3;
            yield return null;

            Assert.IsTrue(SaveSystem.Save(1));
            Fortifications.Level = 1;
            Assert.IsTrue(SaveSystem.Load(1));
            yield return new WaitForSeconds(2.0f);

            WallSegment loaded = null;
            foreach (WallSegment s in WallSegment.Active)
                if (s != null && s.Kind == Utils.WallBuilder.Kind.Gate && Vector3.Distance(s.transform.position, at) < 0.5f) loaded = s;
            Assert.IsNotNull(loaded, "the gate is back where it stood");
            Assert.AreEqual(2, loaded.OreTier, "built of the same ore");
            HealthComponent loadedHp = loaded.GetComponent<HealthComponent>();
            Assert.AreEqual(WallSegment.MaxHealthFor(BuildingType.WallGate, 2), loadedHp.MaxHealth, "as strong");
            Assert.AreEqual(left, loadedHp.CurrentHealth, "as damaged");
            Assert.AreEqual(30f, Mathf.DeltaAngle(0f, loaded.transform.eulerAngles.y), 0.5f, "turned the same way");
            Assert.IsNotNull(loaded.GetComponent<WallGateDoors>(), "still a gate");
            Assert.AreEqual(3, Fortifications.Level, "the walls' level");
            Assert.IsNotNull(WallPier.At(loaded.EndA), "its piers are made again");
        }
    }
}
