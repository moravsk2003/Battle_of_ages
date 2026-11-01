using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.Utils;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.UI;

namespace BattleOfAges.Building
{
    /// <summary>
    /// The settlement's main building (one per settlement). Its level (I..V) is the hard population cap; each upgrade
    /// costs ore of the next tier (II needs tier I ore, ... V needs tier IV), and rare ore is what holds growth back.
    /// Without a town hall only a handful of villagers can live in the settlement.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TownHall : MonoBehaviour
    {
        public const int MaxLevel = 5;
        /// <summary>Population cap without a town hall.</summary>
        public static int CapWithoutHall => GameBalance.TownHallCaps[0];

        private static readonly List<TownHall> active = new List<TownHall>();
        /// <summary>The first hall (the capital), as the one settlement of the fixed world.</summary>
        public static TownHall Current
        {
            get
            {
                for (int i = active.Count - 1; i >= 0; i--) if (active[i] == null) active.RemoveAt(i);
                return active.Count > 0 ? active[0] : null;
            }
        }

        /// <summary>All the player's standing halls: one for each settlement.</summary>
        public static IReadOnlyList<TownHall> All { get { _ = Current; return active; } }

        /// <summary>The settlement a place belongs to: its nearest hall, null with none.</summary>
        public static TownHall Nearest(Vector3 place)
        {
            TownHall first = Current;
            if (active.Count <= 1) return first;
            float best = float.MaxValue;
            foreach (TownHall h in active)
            {
                Vector3 off = h.transform.position - place;
                off.y = 0f;
                if (off.sqrMagnitude < best) { best = off.sqrMagnitude; first = h; }
            }
            return first;
        }

        private static TownHall viewed;
        private static int viewedFrame = -1;

        /// <summary>The settlement the player is looking at: nearest the middle of the map view, or the hero.</summary>
        public static TownHall Viewed
        {
            get
            {
                TownHall first = Current;
                if (active.Count <= 1) return first;
                if (viewedFrame == Time.frameCount && viewed != null && viewed.isActiveAndEnabled) return viewed;
                viewedFrame = Time.frameCount;
                viewed = Nearest(ViewFocus(first.transform.position.y));
                return viewed;
            }
        }

        private static Vector3 ViewFocus(float ground)
        {
            Camera cam = ViewCamera.Current;
            GameObject player = Managers.GameContext.Player;
            if (cam != null && cam.orthographic && cam.transform.forward.y < -0.01f)
                return cam.transform.position + cam.transform.forward * ((cam.transform.position.y - ground) / -cam.transform.forward.y);
            return player != null ? player.transform.position : cam != null ? cam.transform.position : Vector3.zero;
        }

        /// <summary>Its name on the screen.</summary>
        public string Title { get { int i = active.IndexOf(this); return i <= 0 ? "Столиця" : "Поселення " + (i + 1); } }

        // ------------------------------------------------------------------ a settlement of its own (M5 step 7)

        private SettlementStock stock;

        /// <summary>What this settlement has: its own in a campaign (the first hall takes the main one); the fixed world has only that.</summary>
        public SettlementStock Stock
        {
            get
            {
                ResourceManager rm = ResourceManager.Instance;
                if (rm == null) return null;
                if (!World.WorldSettings.Bounded) return rm.Main;
                if (stock != null && stock.Owner == rm) return stock;
                TownHall first = Current;
                if (first != null && first != this) _ = first.Stock; // the capital chooses first
                bool taken = false;
                foreach (TownHall h in active) if (h != this && h.stock == rm.Main) taken = true;
                stock = taken ? new SettlementStock(rm) : rm.Main;
                return stock;
            }
        }

        /// <summary>A loaded game's own stock of this settlement (not the main one).</summary>
        public void AdoptStock(int wood, float food, IEnumerable<KeyValuePair<int, int>> ore)
        {
            if (ResourceManager.Instance == null) return;
            stock = new SettlementStock(ResourceManager.Instance);
            stock.SetState(wood, ore);
            stock.SetFood(food);
        }

        /// <summary>Holds a stock other than the main one (the save keeps it with the hall).</summary>
        public bool HasOwnStock => stock != null && ResourceManager.Instance != null && stock != ResourceManager.Instance.Main;

        private static int peopleFrame = -1;
        private static readonly Dictionary<TownHall, int> peopleOf = new Dictionary<TownHall, int>();

        /// <summary>People of this settlement: those nearest this hall (they belong where they are).</summary>
        public int People
        {
            get
            {
                if (active.Count <= 1) return Population;
                if (peopleFrame != Time.frameCount)
                {
                    peopleFrame = Time.frameCount;
                    peopleOf.Clear();
                    foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
                    {
                        if (w == null) continue;
                        TownHall h = Nearest(w.transform.position);
                        peopleOf.TryGetValue(h, out int n);
                        peopleOf[h] = n + 1;
                    }
                }
                return peopleOf.TryGetValue(this, out int count) ? count : 0;
            }
        }

        public static int PeopleAt(Vector3 place)
        {
            TownHall hall = Nearest(place);
            return hall != null ? hall.People : Population;
        }

        /// <summary>Why a new settlement cannot stand here, or null: too near a hall of the player's or a king's.</summary>
        public static string FoundingForbidden(Vector3 at)
        {
            foreach (TownHall h in All)
            {
                Vector3 off = h.transform.position - at;
                off.y = 0f;
                if (off.magnitude < GameBalance.FoundingFromOwn) return $"Надто близько до поселення: треба щонайменше {GameBalance.FoundingFromOwn:0} м від ратуші";
            }
            foreach (AI.NpcKingdom k in AI.NpcKingdom.All)
            {
                if (k == null) continue;
                Vector3 off = k.Home - at;
                off.y = 0f;
                if (off.magnitude < GameBalance.FoundingFromKing) return "Чужа земля: тут живуть люди короля";
            }
            return null;
        }

        /// <summary>Built: another hall is a new settlement, with a chest beside it and what the settlers brought.</summary>
        public void Founded()
        {
            if (!World.WorldSettings.Bounded || active.Count <= 1 || Current == this) return;
            SettlementStock own = Stock;
            if (own != null)
            {
                own.AddResource(ResourceType.Wood, GameBalance.FoundingStartWood);
                own.AddFood(GameBalance.FoundingStartFood);
            }
            GameObject chest = BuildingPlacementSystem.CreateBuilding(BuildingType.StorageChest);
            if (chest == null) return;
            Vector3 at = transform.position + new Vector3(7f, 0f, 3f);
            at.y = RtsUnit.GetTerrainHeight(at);
            chest.transform.position = at;
            BuildingNode node = chest.GetComponent<BuildingNode>();
            if (node != null) { node.SetType(BuildingType.StorageChest); node.OnBuilt(); }
            StrategyHud.Notify("Нове поселення! Робітників сюди посилайте наказом; склад у нього свій");
        }

        [SerializeField, Range(1, MaxLevel)] private int level = 1;
        public int Level { get => level; set { level = Mathf.Clamp(value, 1, MaxLevel); ApplyLevelHealth(); } }

        /// <summary>The hall's HP follows its level (GameBalance.TownHallHealth); the same share of it is kept.</summary>
        private void ApplyLevelHealth()
        {
            HealthComponent hp = GetComponent<HealthComponent>();
            if (hp == null || !hp.IsAlive) return;
            float share = hp.MaxHealth > 0 ? (float)hp.CurrentHealth / hp.MaxHealth : 1f;
            hp.InitializeHealth(GameBalance.TownHallHealth[level]);
            hp.SetCurrentHealth(Mathf.Max(1, Mathf.RoundToInt(share * hp.MaxHealth)));
        }

        public static int CapForLevel(int lvl) => GameBalance.TownHallCaps[Mathf.Clamp(lvl, 0, MaxLevel)];
        /// <summary>All the halls' caps together: each settlement is limited by its own (Cap).</summary>
        public static int PopulationCap
        {
            get
            {
                if (Current == null) return CapWithoutHall;
                int cap = 0;
                foreach (TownHall h in active) cap += CapForLevel(h.level);
                return cap;
            }
        }
        public int Cap => CapForLevel(level);
        public static int Population => WorkerRegistry.WorkerCount;
        public static bool HasRoom(int extra = 1) => Population + extra <= PopulationCap;

        /// <summary>Wood and ore (count, minimum tier) for raising the hall to toLevel.</summary>
        public static void UpgradeCost(int toLevel, out int wood, out int ore, out int tier)
        {
            int l = Mathf.Clamp(toLevel, 2, MaxLevel);
            wood = GameBalance.TownHallWood[l];
            ore = GameBalance.TownHallOre[l];
            tier = l - 1;
            if (CentralEmpire.Active) wood = ore = 0; // free for the Central Empire
        }

        public bool CanUpgrade(out string reason)
        {
            reason = null;
            if (level >= MaxLevel) { reason = "Найвищий рівень"; return false; }
            UpgradeCost(level + 1, out int wood, out int ore, out int tier);
            SettlementStock rm = Stock;
            if (rm == null) return true;
            if (rm.WoodCount < wood) { reason = $"Потрібно {wood} деревини"; return false; }
            if (rm.GetOreCountAtLeast(tier) < ore) { reason = $"Потрібно {ore} руди {ItemTypeNames.Roman(tier)}+ (або переплавте нижчу на верстаці)"; return false; }
            return true;
        }

        public bool TryUpgrade()
        {
            if (!CanUpgrade(out string reason))
            {
                StrategyHud.Notify(reason);
                return false;
            }
            UpgradeCost(level + 1, out int wood, out int ore, out int tier);
            SettlementStock rm = Stock;
            if (rm != null)
            {
                rm.ConsumeResource(ResourceType.Wood, wood);
                rm.ConsumeOreAtLeast(tier, ore);
            }
            level++;
            ApplyLevelHealth();
            StrategyHud.Notify($"Ратуша {ItemTypeNames.Roman(level)}: населення до {CapForLevel(level)}");
            return true;
        }

        // ------------------------------------------------------------------ population growth (§10.3)

        private float birthCarry;

        /// <summary>Births per game hour for this population: base + per-capita share.</summary>
        public static float GrowthPerHour(int population, TownHall hall = null) =>
            GameBalance.GrowthBasePerHour + GameBalance.GrowthPerCapitaPerHour * population + GameBalance.HouseBirthsPerHour * (hall != null ? hall.OwnHouses() : Houses());

        private static int housesFrame = -1, housesCount;

        /// <summary>A campaign's finished houses (each quickens births, §18.3; the fixed world's houses give a worker instead).</summary>
        public static int Houses()
        {
            if (!World.WorldSettings.Bounded) return 0;
            if (housesFrame == Time.frameCount) return housesCount;
            housesFrame = Time.frameCount;
            housesCount = 0;
            foreach (BuildingNode b in BuildingNode.Active)
                if (b != null && b.Type == BuildingType.WorkerHouse && b.GetComponent<ConstructionSite>() == null) housesCount++;
            return housesCount;
        }

        /// <summary>This settlement's finished houses.</summary>
        private int OwnHouses()
        {
            if (active.Count <= 1) return Houses();
            int n = 0;
            foreach (BuildingNode b in BuildingNode.Active)
                if (b != null && b.Type == BuildingType.WorkerHouse && b.GetComponent<ConstructionSite>() == null && Nearest(b.transform.position) == this) n++;
            return n;
        }

        /// <summary>
        /// Accumulates births over the elapsed game hours and returns how many people are born now (never above the
        /// room under the cap; with no room the growth waits instead of piling up).
        /// </summary>
        public static int Births(ref float carry, float gameHours, int population, int cap, TownHall hall = null)
        {
            int room = cap - population;
            if (room <= 0) { carry = Mathf.Min(carry, 1f); return 0; }
            carry += GrowthPerHour(population, hall) * gameHours;
            int born = Mathf.Min((int)carry, room);
            carry -= born;
            return born;
        }

        private float promotionClock;

        /// <summary>
        /// Once per game hour every working person may become a warrior (docs/GAME_DESIGN.md §11.1): the same as
        /// Binomial(working people, p) promotions, one cheap roll per worker per hour, never per frame.
        /// </summary>
        public static int PromoteWorkers(float chance)
        {
            int promoted = 0;
            var workers = WorkerRegistry.ActiveWorkers;
            for (int i = workers.Count - 1; i >= 0; i--)
            {
                NpcResourceGatherer w = workers[i];
                if (w == null || !w.IsWorking) continue;
                UnitProgress p = w.GetComponent<UnitProgress>();
                if (p == null || p.Rank != UnitRank.Person || Random.value >= chance) continue;
                p.Promote();
                promoted++;
            }
            return promoted;
        }

        private void Update()
        {
            float hours = WorldClock.HoursOf(Time.deltaTime);
            promotionClock += hours;
            if (promotionClock >= 1f && Current != this) promotionClock = 0f; // the rolls go over everyone: one hall makes them
            if (promotionClock >= 1f)
            {
                promotionClock -= 1f;
                if (PromoteWorkers(GameBalance.WorkPromotionPerHour) > 0) StrategyHud.Notify("Робота загартувала: новий воїн");
            }

            SettlementStock food = Stock;
            int born = food != null && food.Hungry ? 0 : Births(ref birthCarry, hours, People, Cap, this); // no births on an empty larder (§10.3)
            for (int i = 0; i < born; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * 6f;
                Vector3 pos = transform.position + new Vector3(ring.x, 0f, ring.y);
                pos.y = RtsUnit.GetTerrainHeight(pos);
                BuildingNode.CreateWorkerNpc(pos, BuildingNode.NextVillagerName());
            }
        }

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable()
        {
            active.Remove(this);
            HealthComponent hp = GetComponent<HealthComponent>();
            if (active.Count > 0 && hp != null && !hp.IsAlive) stock?.Clear(); // it fell, and what it had with it; the last one's stays for the next hall
            stock = null;
        }

        /// <summary>A large timber hall with a banner (the cottage mesh, enlarged).</summary>
        public static GameObject CreatePrefab(string name = "TownHall")
        {
            GameObject root = new GameObject(name);
            TownHall th = root.AddComponent<TownHall>();
            if (!active.Contains(th)) active.Add(th); // (OnEnable does not run in edit mode)
            root.AddComponent<BuildingNode>().SetType(BuildingType.TownHall);
            root.AddComponent<HealthComponent>().InitializeHealth(BuildingData.GetDefinition(BuildingType.TownHall).MaxHealth);

            GameObject hall = WorkerHouseBuilder.Attach(root.transform);
            hall.transform.localScale = new Vector3(1.6f, 1.45f, 1.6f);

            // Banner pole by the door
            var kit = new MeshKit();
            Vector3 woodS = new Vector3(4f, 0f, 0.25f), clothS = new Vector3(1f, 0f, 0.2f), goldS = new Vector3(3f, 0.95f, 0.7f);
            kit.Box(Matrix4x4.Translate(new Vector3(3.2f, 3.4f, 2.6f)), new Vector3(0.16f, 6.8f, 0.16f), 0.03f, woodS, _ => new Color(0.36f, 0.23f, 0.12f));
            kit.Box(Matrix4x4.Translate(new Vector3(3.2f, 6.85f, 2.6f)), new Vector3(0.24f, 0.24f, 0.24f), 0.05f, goldS, _ => new Color(0.9f, 0.7f, 0.3f));
            kit.Box(Matrix4x4.Translate(new Vector3(3.2f, 5.7f, 3.25f)), new Vector3(0.05f, 1.6f, 1.1f), 0.01f, clothS, q => q.y > 0.6f ? new Color(0.9f, 0.72f, 0.3f) : new Color(0.12f, 0.24f, 0.55f));
            var banner = new GameObject("Banner");
            banner.transform.SetParent(root.transform, false);
            banner.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("TownHallBanner");
            banner.AddComponent<MeshRenderer>().sharedMaterial = MeshKit.PropMaterial();

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 3.0f, 0f);
            col.size = new Vector3(5.4f, 6.0f, 5.4f);
            return root;
        }
    }
}
