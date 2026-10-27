using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Own units behave as players expect: a fight a unit picks itself ends at a leash and it goes back; the job window
    /// does not reshuffle workers; workers spread over trees instead of crowding one.
    /// </summary>
    public class WorkerBehaviourTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(-6000f, 0f, -6000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private static Vector3 OnGround(Vector3 p) { p.y = RtsUnit.GetTerrainHeight(p); return p; }
        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        private NpcResourceGatherer Worker(Vector3 at, string name)
        {
            GameObject w = BuildingNode.CreateWorkerNpc(OnGround(at), name);
            spawned.Add(w);
            w.GetComponent<HealthComponent>().InitializeHealth(100000); // the tests are about where it walks, not who wins
            return w.GetComponent<NpcResourceGatherer>();
        }

        /// <summary>A raider that does not walk on its own (the test moves it) and does not die.</summary>
        private HostileMobAI Runner(Vector3 at)
        {
            GameObject m = Utils.HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "LeashRunner");
            HostileMobAI ai = m.GetComponent<HostileMobAI>();
            ai.SpeedCap = 0f;
            CharacterController cc = m.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            m.transform.position = OnGround(at);
            m.GetComponent<HealthComponent>().InitializeHealth(1000000);
            spawned.Add(m);
            return ai;
        }

        /// <summary>The raider runs off along +x faster than anyone; returns how far the soldier ever got from its start.</summary>
        private IEnumerator RunAway(HostileMobAI runner, RtsUnit soldier, Vector3 start, float seconds, System.Action<float> farthest)
        {
            float best = 0f;
            float t0 = Time.time;
            while (Time.time - t0 < seconds)
            {
                Vector3 p = runner.transform.position + new Vector3(14f * Time.deltaTime, 0f, 0f);
                if (p.x < start.x + 70f) runner.transform.position = OnGround(p);
                best = Mathf.Max(best, Flat(soldier.transform.position, start));
                yield return null;
            }
            farthest(best);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SoldierChasingOnItsOwn_StopsAtTheLeash_AndGoesBack()
        {
            NpcResourceGatherer g = Worker(Far, "NPC_Villager_LeashSoldier");
            g.SetGatherDirective(GatherDirective.Idle); // a soldier standing by
            RtsUnit soldier = g.GetComponent<RtsUnit>();
            Vector3 start = soldier.transform.position;
            HostileMobAI runner = Runner(Far + new Vector3(6f, 0f, 0f));
            yield return new WaitForSeconds(1f);
            Assert.IsTrue(soldier.HasAttackTarget, "it goes for the raider near it on its own");
            Assert.IsTrue(soldier.Leashed, "a fight it picked itself: on a leash");

            Time.timeScale = 3f;
            float farthest = 0f;
            yield return RunAway(runner, soldier, start, 12f, f => farthest = f);
            Assert.LessOrEqual(farthest, GameBalance.UnitLeashRadius + 2f, "it does not chase past the leash");
            Assert.Greater(farthest, 8f, "it did chase");

            yield return new WaitForSeconds(8f);
            Assert.IsFalse(soldier.HasAttackTarget, "it let the raider run");
            Assert.Less(Flat(soldier.transform.position, start), 4f, "and walked back to where it stood");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator OrderedAttack_HasNoLeash()
        {
            NpcResourceGatherer g = Worker(Far + new Vector3(0f, 0f, 40f), "NPC_Villager_LeashOrdered");
            g.SetGatherDirective(GatherDirective.Idle);
            RtsUnit soldier = g.GetComponent<RtsUnit>();
            Vector3 start = soldier.transform.position;
            HostileMobAI runner = Runner(Far + new Vector3(6f, 0f, 40f));
            yield return null;
            soldier.IssueAttackCommand(runner.GetComponent<HealthComponent>()); // the player's order
            Assert.IsFalse(soldier.Leashed);

            Time.timeScale = 3f;
            float farthest = 0f;
            yield return RunAway(runner, soldier, start, 14f, f => farthest = f);
            Assert.Greater(farthest, GameBalance.UnitLeashRadius + 5f, "an order is followed past the leash");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator JobWindow_KeepsWorkersOnTheirJobs_AndSendsTheNearestToTheMine()
        {
            Vector3 c = Far + new Vector3(0f, 0f, -60f);
            GameObject mineGo = MineNode.CreateMinePrefab("JobsMine");
            mineGo.transform.position = OnGround(c + new Vector3(40f, 0f, 0f));
            spawned.Add(mineGo);
            NpcResourceGatherer chopA = Worker(c, "NPC_Villager_ChopA");
            NpcResourceGatherer chopB = Worker(c + new Vector3(0f, 0f, 3f), "NPC_Villager_ChopB");
            NpcResourceGatherer near = Worker(c + new Vector3(34f, 0f, 0f), "NPC_Villager_NearMine");
            NpcResourceGatherer carrying = Worker(c + new Vector3(36f, 0f, 2f), "NPC_Villager_Carrying");
            NpcResourceGatherer farAway = Worker(c + new Vector3(-30f, 0f, 0f), "NPC_Villager_FarFromMine");
            yield return null;
            chopA.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            chopB.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            typeof(NpcResourceGatherer).GetField("carriedWood", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(carrying, 2);
            Assert.IsTrue(carrying.IsCarrying);

            // Only the five test workers take part: the others in the scene are set aside for the call
            var others = new List<NpcResourceGatherer>();
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
                if (w != null && w != chopA && w != chopB && w != near && w != carrying && w != farAway && w.IsWorking) others.Add(w);
            foreach (NpcResourceGatherer w in others) w.SetGatherDirective(GatherDirective.Idle);
            try
            {
                WorkerRegistry.ApplyJobDistribution(0, 2, 0, 1, 0);
                Assert.AreEqual(GatherDirective.WoodChopTreesOnly, chopA.CurrentDirective, "a chopper keeps chopping");
                Assert.AreEqual(GatherDirective.WoodChopTreesOnly, chopB.CurrentDirective);
                Assert.AreEqual(GatherDirective.HaulOreFromMineToChest, near.CurrentDirective, "the free worker nearest the mine hauls");
                Assert.AreEqual(GatherDirective.Auto, carrying.CurrentDirective, "one with wood in hand is not moved first");
                Assert.AreEqual(GatherDirective.Auto, farAway.CurrentDirective, "the rest work on their own");
            }
            finally { foreach (NpcResourceGatherer w in others) if (w != null) w.SetGatherDirective(GatherDirective.Auto); }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator Workers_SpreadOverTrees_NotMoreThanTheCapAtOne()
        {
            Vector3 c = Far + new Vector3(0f, 0f, 90f);
            for (int i = 0; i < 3; i++)
            {
                GameObject t = Utils.SteppeTreeBuilder.CreateSteppeTree("CrowdTree" + i);
                t.transform.position = OnGround(c + new Vector3(10f + i * 6f, 0f, 0f));
                spawned.Add(t);
            }
            var workers = new List<NpcResourceGatherer>();
            for (int i = 0; i < 5; i++) workers.Add(Worker(c + new Vector3(0f, 0f, i * 1.5f), "NPC_Villager_Crowd" + i));
            yield return null;
            foreach (NpcResourceGatherer w in workers) w.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            yield return new WaitForSeconds(2.5f); // a search or two each

            var perTree = new Dictionary<ResourceNodeTree, int>();
            foreach (NpcResourceGatherer w in workers)
            {
                if (w.TargetTree == null) continue;
                perTree.TryGetValue(w.TargetTree, out int k);
                perTree[w.TargetTree] = k + 1;
            }
            Assert.GreaterOrEqual(perTree.Count, 3, "five workers use all three trees");
            foreach (var kv in perTree) Assert.LessOrEqual(kv.Value, GameBalance.WorkersPerTree, "no crowd at one tree");
        }
    }
}
