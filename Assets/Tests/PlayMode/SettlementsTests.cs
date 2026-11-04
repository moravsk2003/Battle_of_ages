using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Player;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.UI;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Several settlements (M5 step 7): a second hall on free land has its own stock, chest, people, zones and alarm; caravans carry goods between them; a save keeps it all.</summary>
    public class SettlementsTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_settlements_test_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            Time.timeScale = 1f;
            SaveSystem.FolderOverride = null;
            GameBalance.BanditFirstRaidHours = 6f;
            // The campaign's hero and halls would stand in for the next tests' own
            foreach (TownHall hall in Object.FindObjectsByType<TownHall>(FindObjectsSortMode.None)) Object.Destroy(hall.gameObject);
            GameObject hero = GameObject.Find("Player");
            if (hero != null) Object.Destroy(hero);
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            ZoneMap.Clear();
            WorldSettings.Set(0, 0f);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        private static TownHall Hall(Vector3 at)
        {
            GameObject go = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
            at.y = RtsUnit.GetTerrainHeight(at);
            go.transform.position = at;
            go.GetComponent<BuildingNode>().OnBuilt();
            return go.GetComponent<TownHall>();
        }

        /// <summary>Dry, level land where a new settlement may stand.</summary>
        private static Vector3 FreeLand(Vector3 from)
        {
            for (float r = GameBalance.FoundingFromOwn + 150f; r < 1500f; r += 100f)
                for (int a = 0; a < 16; a++)
                {
                    Vector3 p = from + Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward * r;
                    if (WorldSettings.Inside(p, 60f) && !Lakes.InWater(p.x, p.z, 40f) && TownHall.FoundingForbidden(p) == null) return p;
                }
            Assert.Fail("no free land near");
            return from;
        }

        /// <summary>A new campaign with the capital standing and a second settlement built by the rules.</summary>
        private static IEnumerator TwoSettlements(System.Action<TownHall, TownHall> done)
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 555, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            GameBalance.BanditFirstRaidHours = 1000f;
            BanditRaids.NextRaidHour = 1000.0;
            foreach (NpcResourceGatherer w in new List<NpcResourceGatherer>(WorkerRegistry.ActiveWorkers)) if (w != null) Object.Destroy(w.gameObject);
            yield return null;

            TownHall a = Hall(new Vector3(0f, 0f, -44f));
            a.Level = 2;
            ResourceManager rm = ResourceManager.Instance;
            rm.Main.SetState(1000, new[] { new KeyValuePair<int, int>(0, 200) });
            rm.Main.SetFood(500f);
            Vector3 at = FreeLand(a.transform.position);
            GameObject go = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
            at.y = RtsUnit.GetTerrainHeight(at);
            go.transform.position = at;
            ConstructionSite.Begin(go, BuildingType.TownHall);
            ConstructionSite site = go.GetComponent<ConstructionSite>();
            Assert.AreEqual(1, TownHall.All.Count, "a site is no settlement yet");
            Assert.AreEqual(GameBalance.BuildBaseSeconds + GameBalance.BuildSecondsPerResource * (GameBalance.FoundingWood + GameBalance.FoundingOre), site.WorkSeconds, 1e-3f, "a new settlement's is the long work");
            site.Work(site.WorkSeconds + 1f);
            yield return null;
            done(a, go.GetComponent<TownHall>());
        }

        [Category("Slow")]
        [UnityTest, Timeout(180000)]
        public IEnumerator ASecondHall_IsASettlementOfItsOwn_AndASaveKeepsIt()
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 555, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            GameBalance.BanditFirstRaidHours = 1000f;
            BanditRaids.NextRaidHour = 1000.0;
            Assert.AreEqual(GameBalance.TownHallWood[1], BuildingData.GetDefinition(BuildingType.TownHall).WoodCost, "the first hall costs as ever");
            GameObject firstSite = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
            firstSite.transform.position = new Vector3(40f, RtsUnit.GetTerrainHeight(new Vector3(40f, 0f, -44f)), -44f);
            ConstructionSite founding = ConstructionSite.Begin(firstSite, BuildingType.TownHall);
            Assert.AreEqual(GameBalance.BuildBaseSeconds + GameBalance.BuildSecondsPerResource * (GameBalance.TownHallWood[1] + GameBalance.TownHallOre[1]), founding.WorkSeconds, 1e-3f,
                "the first hall's site does not take itself for a second settlement");
            Object.Destroy(firstSite);
            yield return null;
            TownHall first = Hall(new Vector3(0f, 0f, -44f));
            Assert.IsFalse(BuildingPlacementSystem.IsAllowed(BuildingType.TownHall, out string early));
            StringAssert.Contains("ратуші II", early);
            first.Level = 2;
            Assert.IsTrue(BuildingPlacementSystem.IsAllowed(BuildingType.TownHall, out _), "from a hall II");
            Assert.AreEqual(GameBalance.FoundingWood, BuildingData.GetDefinition(BuildingType.TownHall).WoodCost, "another costs settlers' load");
            StringAssert.Contains("близько", TownHall.FoundingForbidden(first.transform.position + new Vector3(100f, 0f, 0f)));
            yield return null;
            Object.Destroy(first.gameObject);
            yield return null;

            TownHall a = null, b = null;
            yield return TwoSettlements((x, y) => { a = x; b = y; });
            ResourceManager rm = ResourceManager.Instance;
            Assert.AreEqual(2, TownHall.All.Count);
            Assert.AreSame(rm.Main, a.Stock, "the capital keeps the main stock");
            Assert.AreNotSame(a.Stock, b.Stock, "the new one has its own");
            Assert.AreEqual(GameBalance.FoundingStartWood, b.Stock.WoodCount, "what the settlers brought");
            Assert.AreEqual(GameBalance.FoundingStartFood, b.Stock.FoodCount);
            StorageChestNode chest = null;
            foreach (StorageChestNode c in StorageChestNode.ActiveChests)
                if (c != null && Vector3.Distance(c.transform.position, b.transform.position) < 15f) chest = c;
            Assert.IsNotNull(chest, "a chest of its own to bring things to");

            Vector3 nearB = b.transform.position + new Vector3(10f, 0f, 10f);
            Assert.AreSame(b.Stock, rm.For(nearB));
            rm.For(nearB).AddResource(ResourceType.Wood, 40);
            Assert.AreEqual(1000, a.Stock.WoodCount, "the capital's wood is another matter");
            Assert.AreEqual(GameBalance.FoundingStartWood + 40, b.Stock.WoodCount);

            int capBoth = a.Cap + b.Cap;
            Assert.AreEqual(capBoth, TownHall.PopulationCap, "the caps add up");
            nearB.y = RtsUnit.GetTerrainHeight(nearB);
            GameObject settler = BuildingNode.CreateWorkerNpc(nearB, "NPC_Settler");
            yield return null;
            yield return null;
            Assert.AreEqual(1, b.People, "people belong where they are");

            b.Stock.SetFood(0f);
            Assert.IsTrue(b.Stock.Hungry);
            Assert.IsFalse(a.Stock.Hungry, "hunger there is not hunger here");
            b.Stock.SetFood(9f);

            // Zones: by the nearest hall's level, reach and stock
            Vector3 fieldAt = b.transform.position + new Vector3(30f, 0f, 30f);
            Assert.Greater(ZoneMap.Paint(fieldAt, 14f, ZoneType.Fields, out _), 0, "reach counts from its own hall");
            Assert.AreEqual(0, ZoneMap.Paint(b.transform.position + new Vector3(-30f, 0f, 30f), 14f, ZoneType.Trade, out string noTrade), "its hall is I, whatever the capital's");
            StringAssert.Contains("ратуші II", noTrade);
            b.Stock.SetState(500, null);
            ZoneGrower grower = Object.FindFirstObjectByType<ZoneGrower>();
            if (grower == null) grower = new GameObject("ZoneGrower").AddComponent<ZoneGrower>();
            grower.Tick();
            yield return null;
            bool grew = false;
            foreach (ConstructionSite s in ConstructionSite.All) if (s != null && TownHall.Nearest(s.transform.position) == b) grew = true;
            Assert.IsTrue(grew, "the zone grows at its settlement");
            Assert.Less(b.Stock.WoodCount, 500, "by its stock");
            Assert.AreEqual(1000, a.Stock.WoodCount);

            // A cart on the road and the two stocks through a save
            Assert.IsTrue(Caravans.Send(a, b, CaravanGood.Food, 30));
            int bWood = b.Stock.WoodCount;
            float bFood = b.Stock.Food, aFood = a.Stock.Food;
            Assert.IsTrue(SaveSystem.Save(1));
            Assert.IsTrue(SaveSystem.Load(1));
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(2, TownHall.All.Count, "both halls back");
            TownHall capital = TownHall.Current, other = TownHall.All[1];
            if (capital.Stock.WoodCount < other.Stock.WoodCount) { capital = TownHall.All[1]; other = TownHall.All[0]; }
            Assert.AreEqual(1000, capital.Stock.WoodCount);
            Assert.AreEqual(bWood, other.Stock.WoodCount, 20, "each with its own (its zone may have set out another site meanwhile)");
            Assert.AreEqual(bFood, other.Stock.Food, 2f);
            Assert.AreEqual(aFood, capital.Stock.Food, 2f);
            Assert.AreEqual(1, Caravan.All.Count, "the cart still on the road");
            Assert.AreEqual(CaravanGood.Food, Caravan.All[0].Good);
            Assert.AreSame(other, Caravan.All[0].To);
        }

        [Category("Slow")]
        [UnityTest, Timeout(120000)]
        public IEnumerator OnTheMap_AnArrowShowsWhereTheHeroLooks()
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 555, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            Managers.GameContext.Modes?.SetGameMode(Managers.GameMode.RtsMap);
            yield return new WaitForSeconds(0.5f);
            HeroMapArrow holder = Object.FindFirstObjectByType<HeroMapArrow>();
            Assert.IsNotNull(holder, "it is there");
            Transform arrow = holder.transform.Find("Arrow");
            Camera cam = ViewCamera.Current;
            GameObject hero = Managers.GameContext.Player;
            Assert.IsNotNull(arrow, $"it was made: map {Managers.GameContext.MapOpen}, hero {hero != null}, camera {cam != null}");
            MeshRenderer r = arrow.GetComponent<MeshRenderer>();
            Mesh mesh = arrow.GetComponent<MeshFilter>().sharedMesh;
            Vector3 onScreen = cam.WorldToViewportPoint(arrow.position);
            string state = $"active {arrow.gameObject.activeInHierarchy}, enabled {r.enabled}, shader {r.sharedMaterial.shader.name}, at {arrow.position}, scale {arrow.lossyScale}, hero {hero.transform.position}, viewport {onScreen}, verts {mesh.vertexCount}, tris {mesh.triangles.Length}, bounds {r.bounds}, layer {arrow.gameObject.layer}, cull {cam.cullingMask}, near {cam.nearClipPlane} far {cam.farClipPlane}, ortho {cam.orthographicSize}, colour {mesh.colors[4]}";
            Assert.IsTrue(arrow.gameObject.activeInHierarchy, state);
            Assert.Greater(arrow.lossyScale.x, 0.3f, state);
            Vector3 off = arrow.position - hero.transform.position;
            off.y = 0f;
            Assert.Less(off.magnitude, 0.1f, state);
            hero.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return null;
            Assert.AreEqual(90f, arrow.eulerAngles.y, 1f, "it turns with the hero; " + state);
            UnityEngine.Debug.Log("[ArrowState] " + state);
        }

        [Category("Slow")]
        [UnityTest, Timeout(180000)]
        public IEnumerator Caravans_CarryGoodsWhereTheyRunShort_OneARouteAtATime()
        {
            TownHall a = null, b = null;
            yield return TwoSettlements((x, y) => { a = x; b = y; });
            Caravans.Tick();
            Assert.AreEqual(0, Caravan.All.Count, "no market, no caravans on their own");

            GameObject market = ZoneBuildingBuilder.CreateMarket();
            Vector3 marketAt = a.transform.position + new Vector3(20f, 0f, 0f);
            marketAt.y = RtsUnit.GetTerrainHeight(marketAt);
            market.transform.position = marketAt;
            yield return null;
            Assert.IsTrue(MarketNode.In(a));
            Assert.IsFalse(MarketNode.In(b));

            int wood = a.Stock.WoodCount, there = b.Stock.WoodCount;
            Caravans.Tick();
            Assert.AreEqual(1, Caravan.All.Count, "the capital sends what the other lacks");
            Caravan cart = Caravan.All[0];
            Assert.AreEqual(CaravanGood.Wood, cart.Good);
            Assert.AreEqual(GameBalance.CaravanLoadWood, cart.Amount);
            Assert.AreEqual(wood - cart.Amount, a.Stock.WoodCount, "off the stock at once");
            Assert.AreEqual(there, b.Stock.WoodCount, "and not there before it arrives");
            Caravans.Tick();
            Assert.AreEqual(1, Caravan.All.Count, "one on the route at a time");

            Vector3 start = cart.transform.position;
            yield return new WaitForSeconds(3f);
            Assert.Greater(Vector3.Distance(start, cart.transform.position), 5f, "it goes");
            Assert.Less(Vector3.Distance(cart.transform.position, b.transform.position), Vector3.Distance(start, b.transform.position), "that way");

            Vector3 almost = b.transform.position + new Vector3(15f, 0f, 0f);
            almost.y = RtsUnit.GetTerrainHeight(almost);
            cart.transform.position = almost;
            float end = Time.time + 20f;
            while (Time.time < end && cart != null) yield return null;
            Assert.IsTrue(cart == null, "it reached the hall");
            Assert.AreEqual(there + GameBalance.CaravanLoadWood, b.Stock.WoodCount, "with the goods");

            Assert.IsFalse(Caravans.Send(b, a, CaravanGood.Ore, 10), "nothing to send, nothing goes");
        }

        [Category("Slow")]
        [UnityTest, Timeout(300000)]
        public IEnumerator Settlers_RaiseTheHall_AndWorkForTheirOwnStock_WithTheHeroFarAway()
        {
            SaveSystem.NewGame(new CampaignSetup { Seed = 555, Class = HeroClass.Tank, HasArtifact = true, Artifact = ArtifactType.Vampiric });
            yield return new WaitForSeconds(2f);
            GameBalance.BanditFirstRaidHours = 1000f;
            BanditRaids.NextRaidHour = 1000.0;
            foreach (NpcResourceGatherer w in new List<NpcResourceGatherer>(WorkerRegistry.ActiveWorkers)) if (w != null) Object.Destroy(w.gameObject);
            yield return null;
            TownHall a = Hall(new Vector3(0f, 0f, -44f));
            a.Level = 2;
            Vector3 at = FreeLand(a.transform.position);
            at.y = RtsUnit.GetTerrainHeight(at);
            GameObject go = BuildingPlacementSystem.CreateBuilding(BuildingType.TownHall);
            go.transform.position = at;
            ConstructionSite.Begin(go, BuildingType.TownHall);
            var settlers = new List<NpcResourceGatherer>();
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = at + new Vector3(6f + i * 2f, 0f, 6f);
                p.y = RtsUnit.GetTerrainHeight(p);
                settlers.Add(BuildingNode.CreateWorkerNpc(p, "NPC_Settler_" + i).GetComponent<NpcResourceGatherer>());
            }
            Time.timeScale = 10f;
            float end = Time.time + 400f;
            while (Time.time < end && go.GetComponent<ConstructionSite>() != null) yield return null;
            Assert.IsNull(go.GetComponent<ConstructionSite>(), "those there raise it");
            TownHall b = go.GetComponent<TownHall>();
            Assert.AreEqual(2, TownHall.All.Count);
            int start = b.Stock.WoodCount, home = a.Stock.WoodCount;

            end = Time.time + 300f;
            while (Time.time < end && b.Stock.WoodCount <= start) yield return null;
            Time.timeScale = 1f;
            Assert.Greater(b.Stock.WoodCount, start, "and then work for it, the hero far away: the land round a hall stays");
            Assert.AreEqual(home, a.Stock.WoodCount, "into its own stock");
        }

        [Category("Slow")]
        [UnityTest, Timeout(180000)]
        public IEnumerator TheAlarm_CallsTheSettlementInDanger_TheOtherWorksOn_AndAFallenOneTakesItsStock()
        {
            TownHall a = null, b = null;
            yield return TwoSettlements((x, y) => { a = x; b = y; });
            GameObject bandit = HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "TestBandit");
            Vector3 at = b.transform.position + new Vector3(25f, 0f, 0f);
            at.y = RtsUnit.GetTerrainHeight(at);
            bandit.transform.position = at;
            bandit.GetComponent<HostileMobAI>().SetGuardOrigin(at);
            float end = Time.time + 6f;
            while (Time.time < end && !RTS.SettlementAlarm.Active) yield return null;
            Assert.IsTrue(RTS.SettlementAlarm.Active, "an enemy at the new settlement");
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(RTS.SettlementAlarm.ActiveAt(b.transform.position), "calls it");
            Assert.IsFalse(RTS.SettlementAlarm.ActiveAt(a.transform.position), "and not the capital far off");
            Object.Destroy(bandit);
            RTS.SettlementAlarm.StandDown();

            SettlementStock stock = b.Stock;
            Assert.Greater(stock.WoodCount, 0);
            HealthComponent hp = b.GetComponent<HealthComponent>();
            hp.TakeDamage(10000000, ignoreArmor: true);
            yield return new WaitForSeconds(0.5f);
            if (b != null) Object.Destroy(b.gameObject);
            yield return null;
            Assert.AreEqual(1, TownHall.All.Count);
            Assert.AreEqual(0, stock.WoodCount, "what it had went with it");
            Assert.AreEqual(1000, a.Stock.WoodCount, "the other stands as it was");
        }
    }
}
