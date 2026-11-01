using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Resources
{
    /// <summary>
    /// The stocks of the game. Asked directly it answers for the settlement the player is looking at (Viewed), so the
    /// HUD, the menus and whatever the player does there need nothing more; work done in a place goes through For(place).
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceManager : MonoBehaviour
    {
        public static ResourceManager Instance { get; private set; }

        [Header("Initial Resources")]
        [SerializeField] private int initialWood = 0;
        [SerializeField] private int initialOre = 0;

        /// <summary>The first settlement's stock: the only one in the fixed world, and the one a settlement starts with before its hall.</summary>
        public SettlementStock Main => main ?? (main = new SettlementStock(this));
        private SettlementStock main;

        /// <summary>The stock of the settlement the player is looking at.</summary>
        public SettlementStock Viewed
        {
            get
            {
                if (!World.WorldSettings.Bounded) return Main;
                Building.TownHall hall = Building.TownHall.Viewed;
                return hall != null && hall.Stock != null ? hall.Stock : Main;
            }
        }

        /// <summary>The stock of the settlement this place belongs to (its nearest hall).</summary>
        public SettlementStock For(Vector3 place)
        {
            if (!World.WorldSettings.Bounded) return Main;
            Building.TownHall hall = Building.TownHall.Nearest(place);
            return hall != null && hall.Stock != null ? hall.Stock : Main;
        }

        public int WoodCount => Viewed.WoodCount;
        public float Food => Viewed.Food;
        public int FoodCount => Viewed.FoodCount;
        /// <summary>Food matters in a campaign only: the seed-0 world keeps M0's rules.</summary>
        public static bool FoodRules => World.WorldSettings.Bounded;
        public bool Hungry => Viewed.Hungry;
        public int OreCount => Viewed.OreCount;
        public int HighestOreTier => Viewed.HighestOreTier;

        public event Action<ResourceType, int> OnResourceChanged;
        public event Action<int, int> OnOreTierAdded;

        private SettlementStock lastViewed;

        private void Awake()
        {
            // One stock per game scene. Not DontDestroyOnLoad: that moved the whole manager object (with the
            // GameModeManager on it) out of the scene, so after a reload the old mode manager held dead cameras.
            // Saves carry the stock across loads now.
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Main.SetState(initialWood, initialOre > 0 ? new[] { new KeyValuePair<int, int>(0, initialOre) } : null);
        }

        /// <summary>A stock changed: what shows the numbers hears of the one on the screen.</summary>
        internal void RaiseChanged(SettlementStock stock, ResourceType type, int value) { if (stock == Viewed) OnResourceChanged?.Invoke(type, value); }
        internal void RaiseOreAdded(SettlementStock stock, int tier, int amount) { if (stock == Viewed) OnOreTierAdded?.Invoke(tier, amount); }

        public List<KeyValuePair<int, int>> OreTiers() => Viewed.OreTiers();
        public void SetState(int wood, IEnumerable<KeyValuePair<int, int>> ore) => Viewed.SetState(wood, ore);

        public void ResetToInitial()
        {
            Main.SetState(initialWood, initialOre > 0 ? new[] { new KeyValuePair<int, int>(0, initialOre) } : null);
            Main.SetFood(Combat.GameBalance.FoodStart);
        }

        public void SetFood(float amount) => Viewed.SetFood(amount);
        public void AddFood(float amount) => Viewed.AddFood(amount);

        /// <summary>How much this many people eat over these game hours.</summary>
        public static float Eaten(int people, float hours) => people * Combat.GameBalance.FoodPerPersonPerDay * hours / 24f;

        public void Eat(int people, float hours) => Viewed.Eat(people, hours);

        private void Update()
        {
            SettlementStock viewed = Viewed;
            if (viewed != lastViewed)
            {
                if (lastViewed != null && Building.TownHall.All.Count > 1) UI.StrategyHud.Notify("Склад: " + Building.TownHall.Viewed.Title);
                lastViewed = viewed;
                OnResourceChanged?.Invoke(ResourceType.Wood, viewed.WoodCount);
                OnResourceChanged?.Invoke(ResourceType.Ore, viewed.OreCount);
                OnResourceChanged?.Invoke(ResourceType.Food, viewed.FoodCount);
            }
            if (!FoodRules || !SaveLoad.SaveSystem.InGame) return;
            float hours = Combat.WorldClock.HoursOf(Time.deltaTime);
            var halls = Building.TownHall.All;
            if (halls.Count == 0) Main.Eat(Building.TownHall.Population, hours);
            foreach (Building.TownHall hall in halls) hall.Stock?.Eat(hall.People, hours);
        }

        public void AddResource(ResourceType type, int amount) => Viewed.AddResource(type, amount);
        public void AddOre(int tierLevel, int amount) => Viewed.AddOre(tierLevel, amount);
        public int GetOreTierCount(int tierLevel) => Viewed.GetOreTierCount(tierLevel);
        public bool HasResource(ResourceType type, int amount) => Viewed.HasResource(type, amount);
        public bool ConsumeResource(ResourceType type, int amount) => Viewed.ConsumeResource(type, amount);
        public bool ConsumeOre(int tierLevel, int amount) => Viewed.ConsumeOre(tierLevel, amount);
        public int GetOreCountAtLeast(int minTier) => Viewed.GetOreCountAtLeast(minTier);
        public bool ConsumeOreAtLeast(int minTier, int amount) => Viewed.ConsumeOreAtLeast(minTier, amount);
    }
}
