using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Net;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Commanding units: who gets chosen, what a click means, the groups, the marching orders, the formation.</summary>
    public class UnitControlTests
    {
        private static readonly Vector3 Far = new Vector3(5300f, 0f, 5300f);
        private readonly List<GameObject> spawned = new List<GameObject>();
        private RtsUnitCommander commander;

        [SetUp]
        public void Setup()
        {
            WorldSettings.Set(0, 0f);
            GameObject go = new GameObject("TestCommander");
            spawned.Add(go);
            commander = go.AddComponent<RtsUnitCommander>();
        }

        [TearDown]
        public void Teardown()
        {
            commander.DeselectAll();
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private RtsUnit Unit(Vector3 offset, UnitRank rank = UnitRank.Person)
        {
            Vector3 at = Far + offset;
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject go = BuildingNode.CreateWorkerNpc(at, "NPC_Control_" + spawned.Count);
            spawned.Add(go);
            go.GetComponent<NpcResourceGatherer>().SetGatherDirective(GatherDirective.Idle);
            UnitProgress.Setup(go, rank);
            return go.GetComponent<RtsUnit>();
        }

        private static Camera LookingAt(Vector3 middle, List<GameObject> spawned)
        {
            var go = new GameObject("TestCamera");
            spawned.Add(go);
            Camera cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 30f;
            go.transform.SetPositionAndRotation(middle + Vector3.up * 100f, Quaternion.Euler(90f, 0f, 0f));
            return cam;
        }

        // ------------------------------------------------------------------ the formation

        [Test]
        public void TheFormation_IsCentred_AndAcrossTheWay()
        {
            var at = new List<Vector3>();
            var strength = new List<int>();
            for (int i = 0; i < 9; i++) { at.Add(new Vector3(i, 0f, -50f)); strength.Add(0); }
            Vector3 point = new Vector3(4f, 0f, 50f);
            Vector3[] slots = UnitFormation.Slots(at, strength, point);
            Vector3 middle = Vector3.zero;
            float widest = 0f, deepest = 0f;
            foreach (Vector3 s in slots) middle += s;
            middle /= slots.Length;
            Assert.Less((middle - point).magnitude, UnitFormation.Spacing, "round the point, not off to a side of it");
            foreach (Vector3 s in slots)
            {
                widest = Mathf.Max(widest, Mathf.Abs(s.x - point.x));
                deepest = Mathf.Max(deepest, Mathf.Abs(s.z - point.z));
            }
            Assert.Greater(widest, deepest, "a line across the way it walks: wider than deep");
            for (int a = 0; a < slots.Length; a++)
                for (int b = a + 1; b < slots.Length; b++)
                    Assert.GreaterOrEqual((slots[a] - slots[b]).magnitude, UnitFormation.Spacing - 0.01f, "nobody on another's place");
        }

        [Test]
        public void TheFormation_PutsTheStrongFirst_AndNobodyCrosses()
        {
            var at = new List<Vector3> { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f) };
            var strength = new List<int> { 0, 0, 3, -1 };
            Vector3[] slots = UnitFormation.Slots(at, strength, new Vector3(0f, 0f, 60f));
            Assert.Greater(slots[2].z, slots[3].z, "the knight before the archer");
            var line = new List<Vector3> { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            Vector3[] two = UnitFormation.Slots(line, new List<int> { 0, 0 }, new Vector3(0f, 0f, 60f));
            Assert.Less(two[0].x, two[1].x, "the one on the left stays on the left");
        }

        [Test]
        public void OnePlaceInATrunk_IsMovedOutOfIt()
        {
            var tree = new GameObject("Trunk");
            spawned.Add(tree);
            Vector3 trunk = Far + new Vector3(40f, 0f, 0f);
            SolidProps.Set(tree, trunk, 1f);
            Vector3 place = UnitFormation.Standable(trunk + new Vector3(0.2f, 0f, 0f), trunk + new Vector3(9f, 0f, 0f));
            Assert.IsFalse(SolidProps.Inside(place, 0.3f, out _));
            SolidProps.Remove(tree);
        }

        // ------------------------------------------------------------------ what a click means

        [UnityTest]
        public IEnumerator OneOfOurs_IsWalkedUpTo_NotStruck()
        {
            RtsUnit ours = Unit(Vector3.zero);
            yield return null;
            Assert.AreEqual(UnitOrderCommand.Kind.Move, RtsUnitCommander.KindFor(ours.GetComponent<CharacterController>(), out _), "a right-click on a friend");
        }

        [UnityTest]
        public IEnumerator AClick_MeansTheUnitNearOnTheScreen_AtAnyZoom()
        {
            RtsUnit u = Unit(Vector3.zero);
            yield return null;
            Camera cam = LookingAt(u.transform.position, spawned);
            Vector3 onScreen = cam.WorldToScreenPoint(u.transform.position);
            Assert.AreEqual(u, UnitRegistry.NearestOnScreen(onScreen + new Vector3(8f, 0f, 0f), cam, 26f), "close to it");
            Assert.IsNull(UnitRegistry.NearestOnScreen(onScreen + new Vector3(60f, 0f, 0f), cam, 26f), "far from it: nobody");
            cam.orthographicSize = 8f;
            Assert.IsNull(UnitRegistry.NearestOnScreen(cam.WorldToScreenPoint(u.transform.position + new Vector3(3f, 0f, 0f)), cam, 26f),
                          "zoomed in, 3 m is an empty spot of ground (metres took the unit from here)");
        }

        // ------------------------------------------------------------------ choosing many

        [UnityTest]
        public IEnumerator ADoubleClick_TakesThoseOfTheKindInView()
        {
            RtsUnit a = Unit(Vector3.zero, UnitRank.Warrior), b = Unit(new Vector3(4f, 0f, 0f), UnitRank.Warrior);
            RtsUnit person = Unit(new Vector3(8f, 0f, 0f)), farAway = Unit(new Vector3(400f, 0f, 0f), UnitRank.Warrior);
            yield return null;
            Camera cam = LookingAt(a.transform.position, spawned);
            yield return null;
            commander.SelectKindOnScreen(a, cam, false);
            CollectionAssert.Contains(commander.SelectedUnits, a);
            CollectionAssert.Contains(commander.SelectedUnits, b);
            CollectionAssert.DoesNotContain(commander.SelectedUnits, person, "another rank");
            CollectionAssert.DoesNotContain(commander.SelectedUnits, farAway, "out of view");
        }

        [UnityTest]
        public IEnumerator Fighters_AreChosen_WithoutTheWorkers()
        {
            RtsUnit worker = Unit(Vector3.zero), warrior = Unit(new Vector3(3f, 0f, 0f), UnitRank.Warrior), knight = Unit(new Vector3(6f, 0f, 0f), UnitRank.Knight);
            yield return null;
            commander.SelectFighters();
            CollectionAssert.Contains(commander.SelectedUnits, warrior);
            CollectionAssert.Contains(commander.SelectedUnits, knight);
            CollectionAssert.DoesNotContain(commander.SelectedUnits, worker);
            Assert.AreEqual(knight, commander.SelectedUnits[0], "the strongest first in the panel");
            commander.SelectEveryone();
            CollectionAssert.Contains(commander.SelectedUnits, worker, "Ctrl+A takes everyone");
        }

        [UnityTest]
        public IEnumerator AGroup_Remembers_AndForgetsTheFallen()
        {
            RtsUnit a = Unit(Vector3.zero), b = Unit(new Vector3(3f, 0f, 0f)), c = Unit(new Vector3(6f, 0f, 0f));
            yield return null;
            commander.SelectOnly(a);
            commander.SetGroup(1);
            commander.SelectOnly(b);
            commander.AddToGroup(1);
            commander.SelectOnly(c);
            Assert.IsTrue(commander.SelectGroup(1));
            Assert.AreEqual(2, commander.SelectedUnits.Count);
            CollectionAssert.DoesNotContain(commander.SelectedUnits, c, "the group alone");
            Assert.IsFalse(commander.SelectGroup(2), "an empty group changes nothing");
            Assert.AreEqual(2, commander.SelectedUnits.Count);

            a.GetComponent<HealthComponent>().TakeDamage(100000, true);
            yield return null;
            yield return null;
            CollectionAssert.DoesNotContain(commander.SelectedUnits, a, "the fallen leave the selection");
            Assert.AreEqual(1, commander.Group(1).Count, "and the group");
        }

        // ------------------------------------------------------------------ the orders

        [UnityTest, Timeout(60000)]
        public IEnumerator Stop_EndsTheWalk_AndTheWork()
        {
            RtsUnit u = Unit(Vector3.zero);
            yield return null;
            u.GetComponent<NpcResourceGatherer>().SetGatherDirective(GatherDirective.Auto);
            u.IssueMoveCommand(u.transform.position + new Vector3(50f, 0f, 0f));
            yield return null;
            Assert.IsTrue(u.HasMoveTarget);
            commander.SelectOnly(u);
            CommandBus.Run(new UnitOrderCommand { Order = UnitOrderCommand.Kind.Stop, Units = new[] { WorldEntity.IdOf(u).Value } });
            Assert.IsFalse(u.HasMoveTarget);
            Assert.AreEqual(GatherDirective.Idle, u.GetComponent<NpcResourceGatherer>().CurrentDirective);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator AWayOfPoints_IsWalked_InTurn_AndANewOrderForgetsIt()
        {
            RtsUnit u = Unit(Vector3.zero);
            yield return null;
            Vector3 first = u.transform.position + new Vector3(4f, 0f, 0f), second = u.transform.position + new Vector3(4f, 0f, 4f);
            u.IssueMoveCommand(first);
            u.QueueMove(second);
            Assert.AreEqual(1, u.WaypointsLeft);
            Time.timeScale = 4f;
            float end = Time.time + 10f;
            while (Time.time < end && (u.HasMoveTarget || u.WaypointsLeft > 0)) yield return null;
            Vector3 left = u.transform.position - second;
            left.y = 0f;
            Assert.Less(left.magnitude, 1.5f, "at the last of them");

            u.QueueMove(first);
            u.QueueMove(second);
            u.IssueMoveCommand(u.transform.position);
            Assert.AreEqual(0, u.WaypointsLeft);
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator AMarch_FightsWhatItMeets_ThenGoesOn()
        {
            NpcKingdom k = NpcKingdom.Found(Far + new Vector3(0f, 0f, 200f), "Похід", 0);
            k.Economy.HallLevel = 0;
            RtsUnit u = Unit(Vector3.zero, UnitRank.General);
            yield return null;
            HostileMobAI enemy = k.AddSubject(UnitRank.Person, u.transform.position + new Vector3(12f, 0f, 0f), false);
            enemy.SetGuardOrigin(enemy.transform.position); // it stays by the road (switched off, it would leave the grid: nothing to find)
            spawned.Add(k.gameObject);
            Vector3 goal = u.transform.position + new Vector3(30f, 0f, 0f);
            u.IssueAttackMove(goal);
            Assert.IsTrue(u.AttackMoving);
            Time.timeScale = 4f;
            float end = Time.time + 25f;
            while (Time.time < end && enemy.Health.IsAlive) yield return null;
            Assert.IsFalse(enemy.Health.IsAlive, $"it turned on the enemy by its road: at {u.transform.position - enemy.transform.position} from it, marching {u.AttackMoving}, target {u.HasAttackTarget}, moving {u.HasMoveTarget}, enemy health {enemy.Health.CurrentHealth}/{enemy.Health.MaxHealth}, team {enemy.Combat.Team}, damage {u.GetComponent<CombatUnit>().AttackDamage}, range {u.GetComponent<CombatUnit>().AttackRange}, in the grid nearby {UnitSpatialGrid.FindNearestEnemy(enemy.transform.position, 5f, UnitSpatialGrid.TeamPlayer)}");
            end = Time.time + 15f;
            while (Time.time < end && u.HasMoveTarget) yield return null;
            Vector3 left = u.transform.position - goal;
            left.y = 0f;
            Assert.Less(left.magnitude, 2f, "and went on to where it was going, not back");
            yield return null; // (the unit sees that it has arrived on its next frame)
            yield return null;
            Assert.IsFalse(u.AttackMoving, "and the march is over");
            NpcKingdom.RemoveAll();
        }
    }
}
