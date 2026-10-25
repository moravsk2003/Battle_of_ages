using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The UI in play (docs/GAME_DESIGN.md §21, stage 1): tooltips, attack alerts, idle workers.</summary>
    public class UiPlayTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(6000f, 0f, 6000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            AttackAlerts.Reset();
        }

        private static Vector3 OnGround(Vector3 p) { p.y = RtsUnit.GetTerrainHeight(p); return p; }

        [UnityTest]
        public IEnumerator Tooltip_ShowsAfterTheDelay_WithLiveText_AndHidesOnLeave()
        {
            Canvas canvas = UiTheme.CreateCanvas("TipTestCanvas", 50);
            spawned.Add(canvas.gameObject);
            RectTransform button = UiTheme.Rect(canvas.transform, "Button", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 40f));
            button.gameObject.AddComponent<UnityEngine.UI.Image>();
            int n = 1;
            Tooltip tip = Tooltip.Set(button.gameObject, () => "Сказано " + n);
            yield return null;

            TooltipUI.Hover(tip);
            yield return null;
            Assert.IsFalse(TooltipUI.IsShowing, "not at once");
            yield return new WaitForSecondsRealtime(TooltipUI.Delay + 0.1f);
            Assert.IsTrue(TooltipUI.IsShowing, "after the delay");
            Assert.AreEqual("Сказано 1", TooltipUI.ShownText);

            n = 2;
            yield return null;
            Assert.AreEqual("Сказано 2", TooltipUI.ShownText, "live text follows the numbers");

            TooltipUI.Leave(tip);
            yield return null;
            Assert.IsFalse(TooltipUI.IsShowing, "gone when the cursor leaves");

            TooltipUI.Hover(tip);
            TooltipUI.ShowNow();
            button.gameObject.SetActive(false);
            yield return null;
            Assert.IsFalse(TooltipUI.IsShowing, "gone with its element");
        }

        [UnityTest]
        public IEnumerator EnemyHittingOurWorker_RaisesOneAlert_PerPlaceAndCooldown()
        {
            GameObject worker = BuildingNode.CreateWorkerNpc(OnGround(Far), "NPC_Villager_AlertTest");
            spawned.Add(worker);
            var enemyGo = new GameObject("AlertEnemy", typeof(HealthComponent), typeof(CombatUnit));
            spawned.Add(enemyGo);
            CombatUnit enemy = enemyGo.GetComponent<CombatUnit>();
            enemy.Team = UnitSpatialGrid.TeamHostile;
            yield return null;

            var raised = new List<string>();
            void OnRaised(string what, Vector3 where) => raised.Add(what);
            AttackAlerts.Raised += OnRaised;
            try
            {
                HealthComponent hp = worker.GetComponent<HealthComponent>();
                hp.LastAttacker = enemy;
                hp.TakeDamage(5);
                hp.TakeDamage(5);
                Assert.AreEqual(1, raised.Count, "one alert for the place, not one per hit");
                Assert.AreEqual("робітник", raised[0]);
                Assert.IsTrue(AttackAlerts.HasLast);
                Assert.Less((AttackAlerts.Last - worker.transform.position).magnitude, 0.01f, "Space goes there");

                // Somewhere else is a new place
                Assert.IsTrue(AttackAlerts.Report("Ратуша", Far + new Vector3(200f, 0f, 0f), Time.unscaledTime));
                // The same place again after the cooldown
                Assert.IsTrue(AttackAlerts.Report("робітник", worker.transform.position, Time.unscaledTime + AttackAlerts.Cooldown + 0.1f));

                // Our own side hitting it (training, friendly fire) is no attack
                raised.Clear();
                AttackAlerts.Reset();
                var friendGo = new GameObject("AlertFriend", typeof(HealthComponent), typeof(CombatUnit));
                spawned.Add(friendGo);
                hp.LastAttacker = friendGo.GetComponent<CombatUnit>();
                hp.TakeDamage(5);
                Assert.AreEqual(0, raised.Count);
            }
            finally { AttackAlerts.Raised -= OnRaised; }
        }

        [UnityTest]
        public IEnumerator IdleWorker_IsCounted_AndTheButtonSelectsIt()
        {
            GameObject worker = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(0f, 0f, 30f)), "NPC_Villager_IdleTest");
            spawned.Add(worker);
            NpcResourceGatherer g = worker.GetComponent<NpcResourceGatherer>();
            yield return null;
            g.SetGatherDirective(GatherDirective.Idle);
            yield return null;
            Assert.IsFalse(g.IsIdle, "told to stand by: a soldier waiting, not an idle worker");
            g.SetGatherDirective(GatherDirective.Auto);
            // as if it had searched for work and found none for longer than the idle time
            typeof(NpcResourceGatherer).GetField("searchingFor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(g, NpcResourceGatherer.IdleAfterSeconds + 1f);
            Assert.IsTrue(g.IsIdle, "searching for work and finding none: idle");
            Assert.GreaterOrEqual(IdleWorkers.Count, 1);

            var commanderGo = new GameObject("IdleTestCommander");
            spawned.Add(commanderGo);
            RtsUnitCommander commander = Object.FindFirstObjectByType<RtsUnitCommander>();
            if (commander == null) commander = commanderGo.AddComponent<RtsUnitCommander>();
            RtsUnit unit = worker.GetComponent<RtsUnit>();
            NpcResourceGatherer found = null;
            for (int k = 0; k < WorkerRegistry.WorkerCount && found != g; k++) found = IdleWorkers.Next();
            Assert.AreEqual(g, found, "the cycle reaches it");
            commander.SelectOnly(unit);
            Assert.AreEqual(1, commander.SelectedUnits.Count);
            Assert.AreEqual(unit, commander.SelectedUnits[0]);
            commander.DeselectAll();

            g.SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            Assert.IsFalse(g.IsIdle, "a new order: not idle at once");
            yield return null;
        }
    }
}
