using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The early threat in a real campaign: a band appears at its hour, comes for the settlement, the next is bigger, a save keeps the count.</summary>
    public class BanditRaidTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_raid_test_" + System.Guid.NewGuid().ToString("N"));
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

        [Category("Slow")]
        [UnityTest, Timeout(180000)]
        public IEnumerator ABand_ComesAtItsHour_ForTheSettlement_AndTheNextIsBigger()
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 555, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            BanditRaids raids = Object.FindFirstObjectByType<BanditRaids>();
            Assert.IsNotNull(raids);
            Assert.AreEqual(0, raids.BandAlive, "not at once");
            Assert.AreEqual(0, BanditRaids.RaidCount);

            WorldClock.GameHours = GameBalance.BanditFirstRaidHours + 0.01;
            yield return null;
            yield return null;
            Assert.AreEqual(BanditRaids.BandSize(0), raids.BandAlive, "the first band at its hour");
            Assert.AreEqual(1, BanditRaids.RaidCount);
            foreach (HostileMobAI m in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None))
                if (m.name.StartsWith("RaidBandit"))
                {
                    Assert.IsNotNull(m.CurrentCombatTarget, "it comes for the settlement");
                    Assert.IsFalse(Lakes.InWater(m.transform.position.x, m.transform.position.z), "from dry land");
                    float d = Vector3.Distance(m.transform.position, Object.FindFirstObjectByType<PlayerCombatController>().transform.position);
                    Assert.Greater(d, 100f, "from some way off");
                }

            Assert.IsTrue(SaveSystem.Save(1));
            int saved = BanditRaids.RaidCount; double next = BanditRaids.NextRaidHour;
            BanditRaids.Reset();
            Assert.IsTrue(SaveSystem.Load(1));
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(saved, BanditRaids.RaidCount, "a save keeps the count");
            Assert.AreEqual(next, BanditRaids.NextRaidHour, 1e-3, "and the next hour");

            foreach (HostileMobAI m in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None)) if (m.name.StartsWith("RaidBandit")) Object.Destroy(m.gameObject);
            yield return null;
            raids = Object.FindFirstObjectByType<BanditRaids>();
            WorldClock.GameHours = BanditRaids.NextRaidHour + 0.01;
            yield return null;
            yield return null;
            Assert.AreEqual(BanditRaids.BandSize(1), raids.BandAlive, "the next band is bigger");
        }
    }

    /// <summary>
    /// How hard the raids are on a settlement left to itself (no player input, the hero standing idle): SIM_HOURS game hours
    /// (default 48) at SIM_SPEED (default 15); writes raid_report.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Balance probe, run on demand")]
    public class BanditRaidSim
    {
        [UnityTest, Timeout(1200000)]
        public IEnumerator ASettlementLeftToItself()
        {
            string Env(string n, string d) { string v = System.Environment.GetEnvironmentVariable(n); return string.IsNullOrEmpty(v) ? d : v; }
            float hours = float.Parse(Env("SIM_HOURS", "48")), speed = float.Parse(Env("SIM_SPEED", "15"));
            string dir = Env("CAPTURE_DIR", Directory.GetCurrentDirectory());
            Directory.CreateDirectory(dir);
            SaveSystem.FolderOverride = Path.Combine(Path.GetTempPath(), "boa_raid_sim");
            LogAssert.ignoreFailingMessages = true;
            SaveSystem.NewGame(new CampaignSetup { Seed = int.Parse(Env("SIM_SEED", "777")), Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            Time.timeScale = speed;
            BanditRaids raids = Object.FindFirstObjectByType<BanditRaids>();
            HealthComponent hero = Object.FindFirstObjectByType<PlayerCombatController>().GetComponent<HealthComponent>();
            var report = new StringBuilder("# Raids on a settlement left to itself\n\n| game hour | clock | raid no | band alive | own units | hero HP | alarm |\n|---|---|---|---|---|---|---|\n");
            float nextRow = 0f;
            int ownAtStart = UnitRegistry.ActiveUnits.Count;
            while (WorldClock.GameHours < hours && hero != null && hero.IsAlive)
            {
                if (WorldClock.GameHours >= nextRow)
                {
                    nextRow += 2f;
                    report.AppendLine($"| {WorldClock.GameHours:0} | День {WorldClock.Day} {WorldClock.HourOfDay:00.0} | {BanditRaids.RaidCount} | {raids.BandAlive} | {UnitRegistry.ActiveUnits.Count} | {hero.CurrentHealth}/{hero.MaxHealth} | {SettlementAlarm.Active} |");
                }
                yield return null;
            }
            report.AppendLine().AppendLine($"End: game hour {WorldClock.GameHours:0.0}, raids {BanditRaids.RaidCount}, own units {UnitRegistry.ActiveUnits.Count} (started {ownAtStart}), hero {(hero != null && hero.IsAlive ? hero.CurrentHealth + " HP" : "dead")}");
            File.WriteAllText(Path.Combine(dir, "raid_report.md"), report.ToString());
            Time.timeScale = 1f;
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
        }
    }
}
