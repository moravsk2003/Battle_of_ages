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
        public static TownHall Current
        {
            get
            {
                for (int i = active.Count - 1; i >= 0; i--) if (active[i] == null) active.RemoveAt(i);
                return active.Count > 0 ? active[0] : null;
            }
        }

        [SerializeField, Range(1, MaxLevel)] private int level = 1;
        public int Level { get => level; set => level = Mathf.Clamp(value, 1, MaxLevel); }

        public static int CapForLevel(int lvl) => GameBalance.TownHallCaps[Mathf.Clamp(lvl, 0, MaxLevel)];
        public static int PopulationCap => Current != null ? CapForLevel(Current.level) : CapWithoutHall;
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
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return true;
            if (rm.WoodCount < wood) { reason = $"Потрібно {wood} деревини"; return false; }
            if (rm.GetOreCountAtLeast(tier) < ore) { reason = $"Потрібно {ore} руди рів. {tier}+"; return false; }
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
            ResourceManager rm = ResourceManager.Instance;
            if (rm != null)
            {
                rm.ConsumeResource(ResourceType.Wood, wood);
                rm.ConsumeOreAtLeast(tier, ore);
            }
            level++;
            StrategyHud.Notify($"Ратуша {ItemTypeNames.Roman(level)}: населення до {CapForLevel(level)}");
            return true;
        }

        // ------------------------------------------------------------------ population growth (§10.3)

        private float birthCarry;

        /// <summary>Births per game hour for this population: base + per-capita share.</summary>
        public static float GrowthPerHour(int population) =>
            GameBalance.GrowthBasePerHour + GameBalance.GrowthPerCapitaPerHour * population;

        /// <summary>
        /// Accumulates births over the elapsed game hours and returns how many people are born now (never above the
        /// room under the cap; with no room the growth waits instead of piling up).
        /// </summary>
        public static int Births(ref float carry, float gameHours, int population, int cap)
        {
            int room = cap - population;
            if (room <= 0) { carry = Mathf.Min(carry, 1f); return 0; }
            carry += GrowthPerHour(population) * gameHours;
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
            if (promotionClock >= 1f)
            {
                promotionClock -= 1f;
                if (PromoteWorkers(GameBalance.WorkPromotionPerHour) > 0) StrategyHud.Notify("Робота загартувала: новий воїн");
            }

            int born = Births(ref birthCarry, hours, Population, PopulationCap);
            for (int i = 0; i < born; i++)
            {
                Vector2 ring = Random.insideUnitCircle.normalized * 6f;
                Vector3 pos = transform.position + new Vector3(ring.x, 0f, ring.y);
                pos.y = RtsUnit.GetTerrainHeight(pos);
                BuildingNode.CreateWorkerNpc(pos, $"NPC_Villager_{WorkerRegistry.WorkerCount + 1}");
            }
        }

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

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
