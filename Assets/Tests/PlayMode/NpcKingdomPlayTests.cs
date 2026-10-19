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
        private HealthComponent PlayerSide(Vector3 at, UnitRank rank, string name)
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

        private static IEnumerator Until(System.Func<bool> done, float gameSeconds)
        {
            float end = Time.time + gameSeconds;
            while (Time.time < end && !done()) yield return null;
        }

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
            HealthComponent intruder = PlayerSide(Far + new Vector3(30f, 0f, 0f), UnitRank.General, "Intruder");
            yield return Until(() => intruder.CurrentHealth < intruder.MaxHealth, 30f);
            Assert.Less(intruder.CurrentHealth, intruder.MaxHealth, "the kingdom fought the intruder at 30 m");
            int fighting = 0;
            foreach (HostileMobAI m in k.Subjects) if (m.CurrentCombatTarget == intruder) fighting++;
            Assert.GreaterOrEqual(fighting, 3, "the people too");
        }
    }
}
