using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Zones grow (M5 step 4): a marked zone gets its buildings by workers, a field feeds, Auto workers go to it when food runs short and leave when there is enough.</summary>
    public class ZoneGrowthTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            ZoneMap.Clear();
            yield return null; // (what the last test destroyed is gone only now)
            if (ResourceManager.Instance == null) spawned.Add(new GameObject("Resources", typeof(ResourceManager)));
            ResourceManager.Instance.SetState(200, new[] { new KeyValuePair<int, int>(0, 100) });
            ResourceManager.Instance.SetFood(100f);
            foreach (BuildingNode b in new List<BuildingNode>(BuildingNode.Active)) if (b != null) Object.Destroy(b.gameObject);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (ConstructionSite s in new List<ConstructionSite>(ConstructionSite.All)) if (s != null) Object.Destroy(s.gameObject);
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            foreach (BuildingNode b in new List<BuildingNode>(BuildingNode.Active)) if (b != null) Object.Destroy(b.gameObject);
            ZoneMap.Clear();
            WorldSettings.Set(0, 0f);
        }

        private NpcResourceGatherer Worker(Vector3 pos, string name)
        {
            pos.y = RtsUnit.GetTerrainHeight(pos);
            GameObject w = BuildingNode.CreateWorkerNpc(pos, name);
            spawned.Add(w);
            return w.GetComponent<NpcResourceGatherer>();
        }

        private static ZoneGrower Grower()
        {
            ZoneGrower g = Object.FindFirstObjectByType<ZoneGrower>();
            return g != null ? g : new GameObject("ZoneGrower").AddComponent<ZoneGrower>();
        }

        [Test]
        public void TheCampaignMenu_OffersKeyBuildingsOnly()
        {
            foreach (BuildingDefinition d in BuildingData.MenuDefinitions()) Assert.IsFalse(BuildingData.GrowsFromZone(d.Type), d.Type + " grows from a zone");
            Assert.AreEqual(1, ZoneGrower.Wanted(ZoneType.Fields, 1), "the first with the first cells");
            Assert.AreEqual(2, ZoneGrower.Wanted(ZoneType.Fields, GameBalance.ZoneCellsField + 1));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AMarkedZone_GetsItsBuildings_OnFreeGround_AndThePays()
        {
            Vector3 at = new Vector3(60f, 0f, 60f);
            Assert.Greater(ZoneMap.Paint(at, 14f, ZoneType.Fields, out _), 0);
            int wood = ResourceManager.Instance.WoodCount;
            BuildingDefinition def = BuildingData.GetDefinition(BuildingType.Field);
            Assert.IsTrue(ZoneGrower.TryFindSpot(ZoneType.Fields, def, out Vector3 spot), "a free spot in the zone; cells " + ZoneMap.Count(ZoneType.Fields));
            Assert.AreEqual(ZoneType.Fields, ZoneMap.At(spot));
            Grower().Tick();
            yield return null;
            Assert.AreEqual(1, ConstructionSite.All.Count, "one field's site set out");
            ConstructionSite site = ConstructionSite.All[0];
            Assert.AreEqual(BuildingType.Field, site.Type);
            Assert.AreEqual(ZoneType.Fields, ZoneMap.At(site.transform.position), "inside the zone");
            Assert.AreEqual(wood - BuildingData.GetDefinition(BuildingType.Field).WoodCost, ResourceManager.Instance.WoodCount, "paid");
            int wanted = ZoneGrower.Wanted(ZoneType.Fields, ZoneMap.Count(ZoneType.Fields));
            for (int i = 0; i < 6; i++) { Grower().Tick(); yield return null; }
            Assert.LessOrEqual(ConstructionSite.All.Count, wanted, "no more than the zone's size calls for");
            Assert.LessOrEqual(ConstructionSite.All.Count, GameBalance.ZoneSitesAtOnce, "and a few at a time");
            foreach (ConstructionSite s in new List<ConstructionSite>(ConstructionSite.All)) s.Work(s.WorkSeconds + 1f);
            yield return null;
            Assert.AreEqual(0, ConstructionSite.All.Count, "built");
            Assert.Greater(WorkPlace.All.Count, 0, "the field works now");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AField_Feeds_AutoWorkersComeWhenShort_LeaveWhenEnough()
        {
            Vector3 at = new Vector3(-60f, 0f, 60f);
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject fieldObj = BattleOfAges.Utils.ZoneBuildingBuilder.CreateField();
            fieldObj.transform.position = at;
            spawned.Add(fieldObj);
            FieldNode field = fieldObj.GetComponent<FieldNode>();
            yield return null;

            float before = ResourceManager.Instance.Food;
            field.Produce(2, 1f);
            Assert.AreEqual(before + 2f * GameBalance.FarmerFoodPerHour, ResourceManager.Instance.Food, 1e-3f, "two farmers, an hour");
            Assert.Greater(12f * GameBalance.FoodPerPersonPerDay / 24f, GameBalance.FarmerFoodPerHour * 0.9f, "a farmer feeds about a dozen");

            ResourceManager.Instance.SetFood(0.5f);
            NpcResourceGatherer auto = Worker(at + new Vector3(10f, 0f, 0f), "NPC_Farmer");
            Assert.AreEqual(GatherDirective.Auto, auto.CurrentDirective);
            Assert.IsTrue(field.WantsHands, "food is short");
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(GathererState.WorkingAtPlace, auto.CurrentState, "an Auto worker goes to the field");
            Assert.AreSame(field, auto.AssignedPlace);

            ResourceManager.Instance.SetFood(10000f);
            Assert.IsFalse(field.Wanted, "plenty now");
            yield return null;
            yield return null;
            Assert.IsFalse(auto.AtPlace, "and it goes back to other work");

            ResourceManager.Instance.SetFood(10000f);
            NpcResourceGatherer sent = Worker(at + new Vector3(-10f, 0f, 0f), "NPC_Sent");
            Assert.IsTrue(sent.AssignToPlace(field, true));
            yield return null;
            yield return null;
            Assert.IsTrue(sent.AtPlace, "one sent there stays, plenty or not");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AMine_GetsAutoMinersWhenOreIsShort_NoMoreThanItsShare_TheyComeOutAtPlenty()
        {
            foreach (MineNode left in new List<MineNode>(MineNode.ActiveMines)) if (left != null) Object.Destroy(left.gameObject); // (other tests' ore counts too)
            foreach (NpcResourceGatherer left in new List<NpcResourceGatherer>(WorkerRegistry.ActiveWorkers)) if (left != null) Object.Destroy(left.gameObject); // (and their free workers would take the places)
            yield return null;
            Vector3 at = new Vector3(-90f, 0f, -60f);
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject mineObj = MineNode.CreateMinePrefab("ShortMine");
            mineObj.transform.position = at;
            spawned.Add(mineObj);
            MineNode mine = mineObj.GetComponent<MineNode>();
            ResourceManager.Instance.SetState(5000, new[] { new KeyValuePair<int, int>(0, 0) });
            yield return null;
            Assert.IsTrue(mine.OreShort);

            var people = new List<NpcResourceGatherer>();
            for (int i = 0; i < GameBalance.MineAutoMiners + 2; i++) people.Add(Worker(at + new Vector3(6f + i, 0f, 4f), "NPC_Miner_" + i));
            NpcResourceGatherer sent = Worker(at + new Vector3(4f, 0f, 8f), "NPC_SentMiner");
            sent.AssignToWorkInMine(mine);
            Time.timeScale = 4f;
            float end = Time.time + 40f;
            while (Time.time < end && people.FindAll(p => p.IsInsideMine).Count < GameBalance.MineAutoMiners - 1) yield return null;
            Time.timeScale = 1f;
            int auto = people.FindAll(p => p.AutoMiner).Count;
            Assert.GreaterOrEqual(auto, GameBalance.MineAutoMiners - 1, "workers go down on their own");
            Assert.LessOrEqual(auto + 1, GameBalance.MineAutoMiners, "no more than a mine's share, the one sent counted");

            ResourceManager.Instance.SetState(5000, new[] { new KeyValuePair<int, int>(0, GameBalance.MineEnoughOre) });
            yield return null;
            yield return null;
            Assert.AreEqual(0, people.FindAll(p => p.IsMiner).Count, "those who went on their own come out");
            Assert.IsTrue(sent.IsMiner, "one sent stays");
            Assert.IsNull(MineNode.NearestWantingHands(at, 100f), "and no one goes while there is plenty");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator AHouse_QuickensBirths_AMineFollowsTheVein()
        {
            float without = TownHall.GrowthPerHour(10);
            Vector3 at = new Vector3(80f, 0f, -60f);
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject house = BuildingPlacementSystem.CreateBuilding(BuildingType.WorkerHouse);
            house.transform.position = at;
            spawned.Add(house);
            house.GetComponent<BuildingNode>().SetType(BuildingType.WorkerHouse);
            yield return null;
            Assert.AreEqual(without + GameBalance.HouseBirthsPerHour, TownHall.GrowthPerHour(10), 1e-3f, "a house adds to the births");
            int workers = WorkerRegistry.WorkerCount;
            house.GetComponent<BuildingNode>().OnBuilt();
            Assert.AreEqual(workers, WorkerRegistry.WorkerCount, "and gives no worker in a campaign");

            GameObject mine = MineNode.CreateMinePrefab("VeinMine");
            mine.transform.position = at + new Vector3(30f, 0f, 0f);
            spawned.Add(mine);
            Assert.That(mine.GetComponent<MineNode>().Richness, Is.InRange(0.7f, 1.6f));
        }
    }
}
