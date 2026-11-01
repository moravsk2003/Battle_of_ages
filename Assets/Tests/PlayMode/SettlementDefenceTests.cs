using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The player's people defend themselves and their home (one kind of unit, docs/GAME_DESIGN.md §11.1).</summary>
    public class SettlementDefenceTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(-5000f, 0f, 5000f);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // A game scene left by another test has its own hall and player: they go, and the test plays far from them
            foreach (TownHall th in Object.FindObjectsByType<TownHall>(FindObjectsSortMode.None)) Object.Destroy(th.gameObject);
            GameObject player = GameObject.Find("Player");
            if (player != null) Object.Destroy(player);
            yield return null;
            Time.timeScale = 3f;
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(2000f, 1f, 2000f);
            spawned.Add(ground);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            SettlementAlarm.StandDown();
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        private List<RtsUnit> Workers(int n)
        {
            var list = new List<RtsUnit>();
            for (int i = 0; i < n; i++)
            {
                GameObject w = BuildingNode.CreateWorkerNpc(Far + new Vector3(3f + i * 1.5f, 0f, 3f), "NPC_Villager_Test_" + i);
                spawned.Add(w);
                list.Add(w.GetComponent<RtsUnit>());
            }
            return list;
        }

        private HostileMobAI Bandit(Vector3 at)
        {
            GameObject b = HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "TestBandit");
            b.transform.position = at;
            HostileMobAI mob = b.GetComponent<HostileMobAI>();
            mob.SetGuardOrigin(at);
            spawned.Add(b);
            return mob;
        }

        private static IEnumerator Until(System.Func<bool> done, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end && !done()) yield return null;
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EnemyNearTheHall_RaisesTheAlarm_PeopleFightIt_ThenGoBackToWork()
        {
            GameObject hall = TownHall.CreatePrefab("TestHall");
            hall.transform.position = Far;
            spawned.Add(hall);
            List<RtsUnit> people = Workers(4);
            yield return null;
            HostileMobAI bandit = Bandit(Far + new Vector3(0f, 0f, 30f));
            yield return Until(() => SettlementAlarm.Active, 10f);
            Assert.IsTrue(SettlementAlarm.Active, "an enemy within 40 m of the hall raises the alarm");
            int hits = 0, fighters = 0;
            bandit.Health.OnTakeDamage += d => hits++;
            float end = Time.time + 30f;
            while (Time.time < end && (hits == 0 || fighters < 3))
            {
                fighters = Mathf.Max(fighters, people.FindAll(p => p != null && p.HasAttackTarget).Count);
                yield return null;
            }
            Assert.GreaterOrEqual(fighters, 3, "the people dropped their work and went for the enemy");
            Assert.Greater(hits, 0, "and hit it");
            // (A geared warrior can beat four people with axes, as the ranks go; here it simply falls)
            if (bandit.Health.IsAlive) bandit.Health.TakeDamage(100000, ignoreArmor: true);
            yield return Until(() => !SettlementAlarm.Active, GameBalance.AlarmStandDownSeconds + 5f);
            Assert.IsFalse(SettlementAlarm.Active, "the alarm stands down when the enemy is gone");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator ARaidThatRuns_IsLetGo_AndThePeopleGoBack()
        {
            GameObject hall = TownHall.CreatePrefab("TestHall");
            hall.transform.position = Far;
            spawned.Add(hall);
            List<RtsUnit> people = Workers(4);
            yield return null;
            HostileMobAI bandit = Bandit(Far + new Vector3(0f, 0f, 30f));
            yield return Until(() => people.FindAll(p => p != null && p.HasAttackTarget).Count >= 3, 15f);
            Assert.GreaterOrEqual(people.FindAll(p => p != null && p.HasAttackTarget).Count, 3, "the alarm sent them at it");
            bandit.enabled = false;
            bandit.transform.position = Far + new Vector3(0f, 0f, 800f); // it ran
            yield return Until(() => !SettlementAlarm.Active && !people.Exists(p => p != null && p.HasAttackTarget), GameBalance.AlarmStandDownSeconds + 30f);
            Assert.IsFalse(SettlementAlarm.Active, "the alarm stood down");
            foreach (RtsUnit p in people)
            {
                if (p == null) continue;
                Assert.IsFalse(p.HasAttackTarget, "no one runs after it");
                Vector3 off = p.transform.position - Far;
                off.y = 0f;
                Assert.Less(off.magnitude, GameBalance.AlarmChaseRadius + 10f, "no further than the chase goes");
            }
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator HitWorker_FightsBack()
        {
            GameBalance.AutoAlarm = false; // only the worker's own reaction
            try
            {
                List<RtsUnit> people = Workers(1);
                yield return null;
                yield return null;
                HostileMobAI bandit = Bandit(Far + new Vector3(60f, 0f, 60f)); // far from the worker: it does not see it
                bandit.enabled = false;
                yield return null;
                HealthComponent worker = people[0].GetComponent<HealthComponent>();
                bandit.Combat.DeliverHit(worker, 2, ignoreArmor: true);
                yield return null;
                Assert.IsTrue(people[0].HasAttackTarget, "a hit worker goes for whoever hit it");
            }
            finally { GameBalance.AutoAlarm = true; }
        }
    }
}
