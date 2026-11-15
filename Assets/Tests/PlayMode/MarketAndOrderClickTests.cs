using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.UI;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The market's buttons answer a real click (through the EventSystem, as the mouse does), and a worker sent to a job goes at once.</summary>
    public class MarketAndOrderClickTests
    {
        private readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in made) if (go != null) Object.Destroy(go);
            made.Clear();
        }

        /// <summary>A click where the button is on the screen: whatever the EventSystem finds on top gets it.</summary>
        private static GameObject ClickAt(RectTransform target)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, target.TransformPoint(target.rect.center));
            var data = new PointerEventData(EventSystem.current) { position = screen, button = PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(data, hits);
            Assert.Greater(hits.Count, 0, $"something under {target.name} at {screen}");
            GameObject top = hits[0].gameObject;
            data.pointerCurrentRaycast = hits[0];
            data.pointerPress = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
            ExecuteEvents.ExecuteHierarchy(top, data, ExecuteEvents.pointerClickHandler);
            return top;
        }

        private static IEnumerator OpenMap()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            GameModeManager modes = Object.FindFirstObjectByType<GameModeManager>();
            Assert.IsNotNull(modes);
            modes.SetGameMode(GameMode.RtsMap);
            yield return new WaitForSeconds(0.5f);
            Assert.IsNotNull(EventSystem.current);
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator TheMarket_TradeButton_AndItsOffers_DoWhatTheySay()
        {
            yield return OpenMap();
            GameObject market = ZoneBuildingBuilder.CreateMarket("TestMarket");
            made.Add(market);
            Vector3 at = GameContext.Player.transform.position + new Vector3(14f, 0f, 0f);
            at.y = RtsUnit.GetTerrainHeight(at);
            market.transform.position = at;
            yield return null;
            GameContext.Commander.SelectBuilding(market.GetComponent<BuildingNode>());
            yield return new WaitForSeconds(0.4f);

            // The panel's own button opens the exchange (and opens it again once closed)
            Button action = null;
            foreach (Button b in SelectedBuildingUI.Instance.GetComponentsInChildren<Button>(false)) if (b.name == "Action") action = b;
            Assert.IsNotNull(action, "the building panel has its button");
            GameObject got = ClickAt((RectTransform)action.transform);
            yield return null;
            Assert.IsTrue(TradeUI.IsOpen, $"the trade button opens the market (the click went to '{got.name}')");

            // An offer: 10 wood for 4 ore, from the stock the window shows
            ResourceManager rm = ResourceManager.Instance;
            rm.AddResource(ResourceType.Wood, 50);
            yield return null;
            int wood = rm.WoodCount, ore = rm.GetOreTierCount(0);
            Button offer = null;
            foreach (Button b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None)) if (b.name == "Offer2" && b.gameObject.activeInHierarchy) offer = b;
            Assert.IsNotNull(offer, "the wood-for-ore offer");
            got = ClickAt((RectTransform)offer.transform);
            yield return null;
            Assert.AreEqual(wood - 10, rm.WoodCount, $"the wood is given (the click went to '{got.name}')");
            Assert.AreEqual(ore + 4, rm.GetOreTierCount(0), "and the ore taken");
            Assert.IsTrue(TradeUI.LastSaid.Contains("4"), "and it says what it got: " + TradeUI.LastSaid);
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator TheDistrictsButton_SendsTheChosen_ToAFreePlace_AndLetsThemGo()
        {
            yield return OpenMap();
            Vector3 at = GameContext.Player.transform.position + new Vector3(-20f, 0f, 20f);
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject worker = BuildingNode.CreateWorkerNpc(at, "TestDistrictWorker");
            made.Add(worker);
            GameObject sawmill = ZoneBuildingBuilder.CreateSawmill("TestDistrictSawmill");
            made.Add(sawmill);
            Vector3 millAt = at + new Vector3(-15f, 0f, 0f);
            millAt.y = RtsUnit.GetTerrainHeight(millAt);
            sawmill.transform.position = millAt;
            yield return null;

            RtsUnit unit = worker.GetComponent<RtsUnit>();
            NpcResourceGatherer job = worker.GetComponent<NpcResourceGatherer>();
            RtsUnitCommander commander = GameContext.Commander;
            commander.SelectOnly(unit);
            yield return new WaitForSeconds(0.6f); // the panel slides in and lays its buttons out
            Button districts = null;
            foreach (Button b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None)) if (b.name == "DistrictsBtn" && b.gameObject.activeInHierarchy) districts = b;
            Assert.IsNotNull(districts, "the unit panel has the districts button");
            GameObject got = ClickAt((RectTransform)districts.transform);
            yield return null;
            Assert.IsTrue(job.AtPlace, $"sent to a district's place (the click went to '{got.name}')");
            Assert.IsFalse(unit.IsSelected, "and let go to do it");
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator AWorker_SentToAJob_IsLetGo_AndGoesAtOnce()
        {
            yield return OpenMap();
            Vector3 at = GameContext.Player.transform.position + new Vector3(0f, 0f, 20f);
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject worker = BuildingNode.CreateWorkerNpc(at, "TestSentWorker");
            made.Add(worker);
            GameObject house = BuildingNode.CreateWorkerHousePrefab("TestSiteHouse");
            made.Add(house);
            Vector3 siteAt = at + new Vector3(12f, 0f, 0f);
            siteAt.y = RtsUnit.GetTerrainHeight(siteAt);
            house.transform.position = siteAt;
            ConstructionSite site = ConstructionSite.Begin(house, BuildingType.WorkerHouse);
            yield return null;

            RtsUnit unit = worker.GetComponent<RtsUnit>();
            RtsUnitCommander commander = GameContext.Commander;
            commander.SelectOnly(unit);
            Assert.IsTrue(unit.IsSelected);
            Assert.IsTrue(RtsUnitCommander.IsWorkOrder(Net.UnitOrderCommand.Kind.Site));
            Assert.IsFalse(RtsUnitCommander.IsWorkOrder(Net.UnitOrderCommand.Kind.Move), "a move keeps the group chosen");
            commander.OrderSelected(Net.UnitOrderCommand.Kind.Site, site, siteAt);
            yield return null;
            Assert.IsFalse(unit.IsSelected, "sent to work, it is let go");
            Assert.AreEqual(0, commander.SelectedUnits.Count);
            float start = (worker.transform.position - siteAt).magnitude;
            yield return new WaitForSeconds(2f);
            Assert.Less((worker.transform.position - siteAt).magnitude, start - 2f, "and it walks to the site straight away");
        }
    }
}
