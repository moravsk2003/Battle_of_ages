using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>NPC kings in real time (docs/GAME_DESIGN.md §12, M0): a raid walks to its target and comes home; an enemy at the door is fought.</summary>
    public class NpcKingdomPlayTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        /// <summary>Far from the origin: a game scene left by an earlier test has the player and its guards there.</summary>
        private static readonly Vector3 Far = new Vector3(5000f, 0f, 5000f);

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 4f;
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.5f, 0f);
            box.size = new Vector3(20000f, 1f, 20000f);
            spawned.Add(ground);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            NpcKingdom.RemoveAll();
            foreach (HostileMobAI m in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None)) Object.Destroy(m.gameObject);
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        /// <summary>A unit of the player's side (no AI: it only stands and takes it).</summary>
        internal HealthComponent PlayerSide(Vector3 at, UnitRank rank, string name)
        {
            var go = new GameObject(name, typeof(HealthComponent), typeof(CombatUnit));
            at.y = RtsUnit.GetTerrainHeight(at); // (a world left by an earlier scene test may have hills)
            go.transform.position = at;
            go.AddComponent<Equipment>();
            UnitProgress.Setup(go, rank);
            HealthComponent hp = go.GetComponent<HealthComponent>();
            hp.InitializeHealth(hp.MaxHealth);
            int id = UnitSpatialGrid.Register(go.GetComponent<CombatUnit>(), hp, UnitSpatialGrid.TeamPlayer, 0.4f);
            hp.OnDeath += () => UnitSpatialGrid.Unregister(id);
            spawned.Add(go);
            return hp;
        }

        /// <summary>An absent player's kingdom kept by a bot (§14.4): real, it shows no town of its own, has no hall to storm,
        /// defends its home, never raids; given back, the guest's save loses whom it lost.</summary>
        [UnityTest]
        public IEnumerator AGuestsKingdom_KeptByABot_OnlyGuards_AndGivesItsLossesBack()
        {
            var save = new SaveLoad.SaveData();
            save.Player.Health = 400;
            save.Buildings.Add(new SaveLoad.BuildingSave { Type = (int)Building.BuildingType.TownHall, Position = Far, Level = 3, Health = 900 });
            for (int i = 0; i < 6; i++) save.Units.Add(new SaveLoad.UnitSave { Name = "W" + i, Rank = (int)UnitRank.Warrior, Health = 120, Position = Far + new Vector3(i, 0f, 4f) });
            var guest = new Net.GuestRecord { Token = "bot-test", Number = 3, Save = JsonUtility.ToJson(save) };
            SaveLoad.KingdomState s = Net.GuestBot.FromSave(save, guest);
            NpcKingdom k = NpcKingdom.FromState(s);
            Assert.IsTrue(k.Guardian);
            Assert.IsNull(k.Town, "the player's own buildings stand there, not a town of the bot's");
            Assert.IsFalse(k.GetComponent<BoxCollider>().enabled, "no hall of its own to storm");
            Assert.AreEqual(6, k.Population);
            Assert.IsNotNull(k.King, "its hero waits at home");

            k.Age = 500f;
            k.Think();
            Assert.IsFalse(k.Raiding, "a bot does not raid");

            HealthComponent enemy = PlayerSide(Far + new Vector3(8f, 0f, 8f), UnitRank.Warrior, "Intruder");
            yield return Until(() => !enemy.IsAlive, 30f);
            Assert.IsFalse(enemy.IsAlive, "its people go for an enemy at home");

            // Two of them fall
            int fallen = 0;
            foreach (HostileMobAI m in k.Subjects) if (fallen < 2 && m != null) { m.Health.TakeDamage(int.MaxValue / 2, ignoreArmor: true); fallen++; }
            yield return null;
            Assert.IsTrue(Net.GuestBot.Retire(guest));
            Assert.IsNull(Net.GuestBot.RealOf("bot-test"), "the bot has given it up");
            Assert.AreEqual(4, JsonUtility.FromJson<SaveLoad.SaveData>(guest.Save).Units.Count, "the fallen are gone from the player's save");
        }

        private static IEnumerator Until(System.Func<bool> done, float gameSeconds)
        {
            float end = Time.time + gameSeconds;
            while (Time.time < end && !done()) yield return null;
        }

        [Category("Slow")]
        [UnityTest, Timeout(300000)]
        public IEnumerator Raid_WalksToTheTarget_DestroysIt_AndComesHome()
        {
            NpcKingdom k = NpcKingdom.Found(Far, "Test", 0, 10, HeroClass.Dd);
            for (int i = 0; i < 6; i++) k.AddSubject(UnitRank.Warrior, k.Spot());
            yield return null;
            HealthComponent target = PlayerSide(Far + new Vector3(80f, 0f, 0f), UnitRank.Knight, "PlayerHall");
            k.StartRaid(k.RaidParty(), target);
            Assert.IsTrue(k.Raiding);

            yield return Until(() => !target.IsAlive, 90f);
            Assert.IsFalse(target.IsAlive, "the raid reached and destroyed its target");

            yield return Until(() => !k.Raiding, 30f);
            Assert.IsFalse(k.Raiding, "nothing left to fight: the raid is over");
            yield return Until(() => Vector3.Distance(k.King.transform.position, k.Home) < 15f, 60f);
            Assert.Less(Vector3.Distance(k.King.transform.position, k.Home), 15f, "the king came home");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EnemyAtTheDoor_EveryoneAtHomeFightsIt()
        {
            NpcKingdom k = NpcKingdom.Found(Far, "Test", 4, 10, HeroClass.Tank);
            yield return null;
            HealthComponent intruder = PlayerSide(k.Home + new Vector3(30f, 0f, 0f), UnitRank.General, "Intruder"); // (the kingdom settles on level ground near Far)
            // People think in turns (CrowdSim): at the first blow only one or two may have picked the intruder yet, so wait for
            // three as well, not only for the first blow (it failed a full run in three that way; KNOWN_ISSUES 36)
            int Fighting() { int n = 0; foreach (HostileMobAI m in k.Subjects) if (m.CurrentCombatTarget == intruder) n++; return n; }
            yield return Until(() => intruder.CurrentHealth < intruder.MaxHealth && Fighting() >= 3, 30f);
            var why = new System.Text.StringBuilder($"home {k.Home}, intruder at {intruder.transform.position}, world {World.WorldSettings.Size}, king {(k.King != null ? k.King.CurrentStateType.ToString() : "-")};");
            foreach (HostileMobAI m in k.Subjects) why.Append($" [{m.CurrentStateType} {intruder.DistanceFrom(m.transform.position):0.0} m, range {m.AttackRange:0.0}, target {(m.CurrentCombatTarget != null ? m.CurrentCombatTarget.name : "-")}, dest {m.HasDestination}, can {m.Combat.CanAttack}]");
            Assert.Less(intruder.CurrentHealth, intruder.MaxHealth, "the kingdom fought the intruder at 30 m: " + why);
            Assert.GreaterOrEqual(Fighting(), 3, "the people too: " + why);
        }
    }
}
