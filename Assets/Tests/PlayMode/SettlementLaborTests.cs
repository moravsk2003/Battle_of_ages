using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Net;
using BattleOfAges.RTS;
using BattleOfAges.UI;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Who works where, by district; what can be done with a building once it stands.</summary>
    public class SettlementLaborTests
    {
        private static readonly Vector3 Far = new Vector3(-5300f, 0f, 5300f);
        private readonly List<GameObject> spawned = new List<GameObject>();

        [SetUp]
        public void Setup()
        {
            WorldSettings.Set(0, 0f);
            SettlementLabor.Clear();
        }

        [TearDown]
        public void Teardown()
        {
            SettlementLabor.Clear();
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        private NpcResourceGatherer Worker(Vector3 offset, GatherDirective directive = GatherDirective.Auto)
        {
            Vector3 at = Far + offset;
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject go = BuildingNode.CreateWorkerNpc(at, "NPC_Labor_" + spawned.Count);
            spawned.Add(go);
            NpcResourceGatherer g = go.GetComponent<NpcResourceGatherer>();
            g.SetGatherDirective(directive);
            return g;
        }

        private GameObject Built(BuildingType type, Vector3 offset)
        {
            GameObject go = BuildingPlacementSystem.CreateBuilding(type);
            Vector3 at = Far + offset;
            at.y = RtsUnit.GetTerrainHeight(at);
            go.transform.position = at;
            spawned.Add(go);
            return go;
        }

        [UnityTest]
        public IEnumerator OneMore_ComesToTheField_OneLess_GoesBackToWork()
        {
            NpcResourceGatherer w = Worker(Vector3.zero);
            GameObject field = Built(BuildingType.Field, new Vector3(20f, 0f, 0f));
            yield return null;
            WorkPlace place = field.GetComponent<WorkPlace>();
            int before = SettlementLabor.RowOf(LaborJob.Fields, null).Hands;

            Assert.IsNull(SettlementLabor.AddTo(field, null, LaborJob.Fields));
            Assert.AreEqual(place, w.AssignedPlace, "the free one goes there");
            Assert.AreEqual(before + 1, SettlementLabor.RowOf(LaborJob.Fields, null).Hands);
            Assert.IsTrue(w.IsStationed, "and stays until told otherwise");

            Assert.IsNull(SettlementLabor.RemoveFrom(field, null, LaborJob.Fields));
            Assert.IsNull(w.AssignedPlace);
            Assert.AreEqual(GatherDirective.Auto, w.CurrentDirective);
            Assert.AreEqual(before, SettlementLabor.RowOf(LaborJob.Fields, null).Hands);
        }

        [UnityTest]
        public IEnumerator OneLess_IsALimit_OnThoseWhoComeByThemselves_TillTaken0ff()
        {
            Worker(Vector3.zero);
            GameObject field = Built(BuildingType.Field, new Vector3(20f, 0f, 0f));
            yield return null;
            WorkPlace place = field.GetComponent<WorkPlace>();
            Assert.IsTrue(SettlementLabor.RoomFor(place), "no limit to start with");
            SettlementLabor.AddTo(field, null, LaborJob.Fields);
            SettlementLabor.RemoveFrom(field, null, LaborJob.Fields);
            Assert.GreaterOrEqual(SettlementLabor.LimitOf(LaborJob.Fields, null), 0, "the player has said how many");
            Assert.IsFalse(SettlementLabor.RoomFor(place), "or the field would call back whoever was just sent away");

            List<LaborLimitSave> saved = SettlementLabor.Save();
            SettlementLabor.Clear();
            Assert.IsTrue(SettlementLabor.RoomFor(place));
            SettlementLabor.Load(saved);
            Assert.IsFalse(SettlementLabor.RoomFor(place), "a save keeps it");

            SettlementLabor.ClearLimits(null);
            Assert.AreEqual(-1, SettlementLabor.LimitOf(LaborJob.Fields, null));
            Assert.IsTrue(SettlementLabor.RoomFor(place));
        }

        [UnityTest]
        public IEnumerator TheFreeOneGoes_BeforeOneOnAJob_EvenIfNearer()
        {
            NpcResourceGatherer busy = Worker(new Vector3(18f, 0f, 0f), GatherDirective.WoodChopTreesOnly);
            NpcResourceGatherer free = Worker(new Vector3(-30f, 0f, 0f));
            GameObject field = Built(BuildingType.Field, new Vector3(20f, 0f, 0f));
            yield return null;
            Assert.IsNull(SettlementLabor.AddTo(field, null, LaborJob.Fields));
            Assert.AreEqual(field.GetComponent<WorkPlace>(), free.AssignedPlace);
            Assert.IsNull(busy.AssignedPlace);
            Assert.AreEqual(GatherDirective.WoodChopTreesOnly, busy.CurrentDirective);
            // With no one free, one on a job with no building
            Assert.IsNull(SettlementLabor.AddTo(field, null, LaborJob.Fields));
            Assert.AreEqual(field.GetComponent<WorkPlace>(), busy.AssignedPlace);
        }

        [UnityTest]
        public IEnumerator AJobRow_MovesPeopleBetweenJobs_ThroughACommand()
        {
            NpcResourceGatherer w = Worker(Vector3.zero);
            yield return null;
            int chop = SettlementLabor.RowOf(LaborJob.Chop, null).Hands;
            CommandBus.Run(new LaborCommand { Order = LaborCommand.Kind.Add, Job = LaborJob.Chop });
            Assert.AreEqual(chop + 1, SettlementLabor.RowOf(LaborJob.Chop, null).Hands);
            CommandBus.Run(new LaborCommand { Order = LaborCommand.Kind.Remove, Job = LaborJob.Chop });
            Assert.AreEqual(chop, SettlementLabor.RowOf(LaborJob.Chop, null).Hands);
            Assert.AreEqual(GatherDirective.Auto, w.CurrentDirective);
        }

        [UnityTest]
        public IEnumerator ABuildingsHands_GoThroughACommand_ToThatBuilding()
        {
            NpcResourceGatherer w = Worker(Vector3.zero);
            GameObject field = Built(BuildingType.Field, new Vector3(20f, 0f, 0f));
            yield return null;
            int id = WorldEntity.IdOf(field.GetComponent<BuildingNode>()).Value;
            CommandBus.Run(new LaborCommand { Order = LaborCommand.Kind.Add, Job = LaborJob.Fields, Building = id });
            Assert.AreEqual(field.GetComponent<WorkPlace>(), w.AssignedPlace);
            Assert.IsTrue(SettlementLabor.HandsAt(field, out int hands, out int places));
            Assert.AreEqual(1, hands);
            Assert.Greater(places, 0);
        }

        [UnityTest]
        public IEnumerator ASiteGivenUp_GivesBackWhatWasNotBuilt_ABuildingPulledDown_Half()
        {
            GameObject house = Built(BuildingType.WorkerHouse, new Vector3(40f, 0f, 0f));
            GameObject site = Built(BuildingType.WorkerHouse, new Vector3(60f, 0f, 0f));
            ConstructionSite.Begin(site, BuildingType.WorkerHouse, 0.25f);
            yield return null;
            BuildingDefinition def = BuildingData.GetDefinition(BuildingType.WorkerHouse);

            BuildingDemolition.Refund(house.GetComponent<BuildingNode>(), out int wood, out int ore, out _);
            Assert.AreEqual(Mathf.FloorToInt(def.WoodCost * Combat.GameBalance.DemolishRefund), wood);
            Assert.AreEqual(Mathf.FloorToInt(def.OreCost * Combat.GameBalance.DemolishRefund), ore);
            BuildingDemolition.Refund(site.GetComponent<BuildingNode>(), out wood, out _, out _);
            Assert.AreEqual(Mathf.FloorToInt(def.WoodCost * 0.75f), wood, "a quarter built, three quarters back");

            NpcResourceGatherer builder = Worker(new Vector3(58f, 0f, 0f));
            Assert.IsTrue(builder.AssignToSite(site.GetComponent<ConstructionSite>()));
            CommandBus.Run(new DemolishCommand { Building = WorldEntity.IdOf(site.GetComponent<BuildingNode>()).Value });
            Assert.IsFalse(builder.AtSite, "whoever built it goes back to work");
            yield return null;
            Assert.IsTrue(site == null, "and it is gone");
        }

        [UnityTest]
        public IEnumerator AHall_CannotBePulledDown()
        {
            GameObject hall = Built(BuildingType.TownHall, new Vector3(80f, 0f, 0f));
            yield return null;
            Assert.IsFalse(BuildingDemolition.CanDemolish(hall.GetComponent<BuildingNode>(), out string why));
            StringAssert.Contains("Ратушу", why);
            BuildingDemolition.Run(hall.GetComponent<BuildingNode>());
            yield return null;
            Assert.IsTrue(hall != null, "it stands");
        }

        [UnityTest]
        public IEnumerator UnitsOrABuilding_AreChosen_NotBoth()
        {
            var go = new GameObject("TestCommander");
            spawned.Add(go);
            RtsUnitCommander commander = go.AddComponent<RtsUnitCommander>();
            RtsUnit unit = Worker(Vector3.zero).GetComponent<RtsUnit>();
            BuildingNode house = Built(BuildingType.WorkerHouse, new Vector3(40f, 0f, 0f)).GetComponent<BuildingNode>();
            yield return null;
            commander.SelectOnly(unit);
            commander.SelectBuilding(house);
            Assert.AreEqual(house, commander.SelectedBuilding);
            Assert.AreEqual(0, commander.SelectedUnits.Count, "the units let go");
            commander.SelectOnly(unit);
            Assert.IsNull(commander.SelectedBuilding, "and the other way round");
        }

        [UnityTest]
        public IEnumerator ThePeoplesWindow_Shows_ThePlacesAndTheHands()
        {
            NpcGatherCommandUI window = NpcGatherCommandUI.Instance; // (a scene left by another test keeps its own: there is one)
            if (window == null)
            {
                var go = new GameObject("TestPeople");
                spawned.Add(go);
                window = go.AddComponent<NpcGatherCommandUI>();
            }
            Worker(Vector3.zero);
            Built(BuildingType.Field, new Vector3(20f, 0f, 0f));
            yield return null;
            window.OpenMenu();
            StringAssert.Contains("Людей", window.SummaryText);
            LaborRow fields = SettlementLabor.RowOf(LaborJob.Fields, null);
            Assert.AreEqual($"{fields.Hands} / {fields.Places}", window.ValueOf(LaborJob.Fields));
            window.CloseMenu();
        }

        [Test]
        public void AZonesButton_SaysWhatStands_OfWhatItCallsFor()
        {
            ZoneMap.Clear();
            Assert.AreEqual(ZoneMap.Name(ZoneType.Fields), ZoneToolbar.LabelOf(ZoneType.Fields, null), "nothing marked: the name alone");
            ZoneMap.SetRaw(ZoneMap.CellOf(Far), ZoneType.Fields);
            StringAssert.EndsWith("0/1", ZoneToolbar.LabelOf(ZoneType.Fields, null));
            ZoneMap.Clear();
        }
    }
}
