using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Resources
{
    /// <summary>
    /// One settlement's wood, food and ore (M5 step 7). A campaign has one for each town hall (TownHall.Stock); the
    /// fixed world and a settlement before its hall have ResourceManager.Main.
    /// </summary>
    public sealed class SettlementStock
    {
        public ResourceManager Owner { get; }

        public int WoodCount { get; private set; }
        /// <summary>Food in stock (a campaign, §10.3): whole pieces shown, eaten in fractions over the hours.</summary>
        public float Food { get; private set; }
        public int FoodCount => Mathf.FloorToInt(Food);
        /// <summary>Births wait while nothing is left to eat.</summary>
        public bool Hungry => ResourceManager.FoodRules && Food <= 0f;
        private bool warnedLow, warnedOut;
        public int OreCount { get; private set; }
        public int HighestOreTier { get; private set; }

        private readonly Dictionary<int, int> oreTierCounts = new Dictionary<int, int>();

        public SettlementStock(ResourceManager owner)
        {
            Owner = owner;
            for (int i = 0; i < OreTierHelper.MAX_TIERS; i++) oreTierCounts[i] = 0;
        }

        private void Changed(ResourceType type, int value) { if (Owner != null) Owner.RaiseChanged(this, type, value); }

        /// <summary>Ore stock per tier (only tiers that have ore), for saving.</summary>
        public List<KeyValuePair<int, int>> OreTiers()
        {
            var list = new List<KeyValuePair<int, int>>();
            foreach (var pair in oreTierCounts) if (pair.Value > 0) list.Add(pair);
            return list;
        }

        /// <summary>Replaces the whole stock (a new game: initial values; a loaded game: the saved stock).</summary>
        public void SetState(int wood, IEnumerable<KeyValuePair<int, int>> ore)
        {
            Fill(wood, ore);
            Changed(ResourceType.Wood, WoodCount);
            Changed(ResourceType.Ore, OreCount);
        }

        private void Fill(int wood, IEnumerable<KeyValuePair<int, int>> ore)
        {
            WoodCount = Mathf.Max(0, wood);
            OreCount = 0;
            HighestOreTier = 0;
            for (int i = 0; i < OreTierHelper.MAX_TIERS; i++) oreTierCounts[i] = 0;
            if (ore == null) return;
            foreach (var pair in ore)
            {
                int t = Mathf.Clamp(pair.Key, 0, OreTierHelper.MAX_TIERS - 1);
                if (pair.Value <= 0) continue;
                oreTierCounts[t] += pair.Value;
                OreCount += pair.Value;
                if (t > HighestOreTier) HighestOreTier = t;
            }
        }

        /// <summary>The settlement fell: what it had is gone (no one is told: its hall is going down with the scene or the enemy).</summary>
        public void Clear()
        {
            Fill(0, null);
            Food = 0f;
            warnedLow = warnedOut = false;
        }

        public void SetFood(float amount)
        {
            Food = Mathf.Max(0f, amount);
            warnedLow = warnedOut = false;
            Changed(ResourceType.Food, FoodCount);
        }

        public void AddFood(float amount)
        {
            if (amount <= 0f) return;
            Food += amount;
            warnedLow = warnedOut = false;
            Changed(ResourceType.Food, FoodCount);
        }

        /// <summary>Its people eat over these game hours (saying so when it runs low and when it runs out).</summary>
        public void Eat(int people, float hours)
        {
            if (Food <= 0f || people <= 0) return;
            int before = FoodCount;
            float eaten = ResourceManager.Eaten(people, hours);
            Food = Mathf.Max(0f, Food - eaten);
            if (FoodCount != before) Changed(ResourceType.Food, FoodCount);
            if (!warnedLow && Food > 0f && Food < ResourceManager.Eaten(people, 24f * Combat.GameBalance.FoodLowDays))
            {
                warnedLow = true;
                Say("Їжі лишилось менш ніж на день");
            }
            if (!warnedOut && Food <= 0f)
            {
                warnedOut = true;
                Say("Їжа скінчилась: приріст населення зупинився");
            }
        }

        /// <summary>A message about this stock, naming the settlement once there are several.</summary>
        private void Say(string text)
        {
            var halls = Building.TownHall.All;
            if (halls.Count > 1)
                foreach (Building.TownHall h in halls)
                    if (h.Stock == this) { text = h.Title + ": " + text; break; }
            UI.StrategyHud.Notify(text);
        }

        public void AddResource(ResourceType type, int amount)
        {
            if (amount <= 0) return;

            if (type == ResourceType.Wood)
            {
                WoodCount += amount;
                Changed(ResourceType.Wood, WoodCount);
            }
            else if (type == ResourceType.Ore)
            {
                AddOre(0, amount);
            }
        }

        public void AddOre(int tierLevel, int amount)
        {
            if (amount <= 0) return;

            tierLevel = Mathf.Clamp(tierLevel, 0, OreTierHelper.MAX_TIERS - 1);
            OreCount += amount;

            if (!oreTierCounts.ContainsKey(tierLevel)) oreTierCounts[tierLevel] = 0;
            oreTierCounts[tierLevel] += amount;

            if (tierLevel > HighestOreTier && oreTierCounts[tierLevel] > 0)
            {
                HighestOreTier = tierLevel;
            }

            Changed(ResourceType.Ore, OreCount);
            if (Owner != null) Owner.RaiseOreAdded(this, tierLevel, amount);
        }

        public int GetOreTierCount(int tierLevel)
        {
            return oreTierCounts.TryGetValue(tierLevel, out int count) ? count : 0;
        }

        public bool HasResource(ResourceType type, int amount)
        {
            if (type == ResourceType.Wood) return WoodCount >= amount;
            if (type == ResourceType.Ore) return OreCount >= amount;
            return false;
        }

        public bool ConsumeResource(ResourceType type, int amount)
        {
            if (!HasResource(type, amount)) return false;

            if (type == ResourceType.Wood)
            {
                WoodCount -= amount;
                Changed(ResourceType.Wood, WoodCount);
            }
            else if (type == ResourceType.Ore)
            {
                OreCount -= amount;
                Changed(ResourceType.Ore, OreCount);
            }
            return true;
        }

        /// <summary>
        /// Consumes ore of a specific tier level.
        /// Returns true if successful, false if not enough ore of that tier.
        /// </summary>
        public bool ConsumeOre(int tierLevel, int amount)
        {
            tierLevel = Mathf.Clamp(tierLevel, 0, OreTierHelper.MAX_TIERS - 1);

            int currentCount = GetOreTierCount(tierLevel);
            if (currentCount < amount) return false;

            oreTierCounts[tierLevel] -= amount;
            OreCount -= amount;

            Changed(ResourceType.Ore, OreCount);
            return true;
        }

        /// <summary>Ore of this tier or any higher one.</summary>
        public int GetOreCountAtLeast(int minTier)
        {
            int n = 0;
            foreach (var pair in oreTierCounts) if (pair.Key >= minTier) n += pair.Value;
            return n;
        }

        /// <summary>Consumes ore of at least minTier, the lowest qualifying tiers first (better ore is kept).</summary>
        public bool ConsumeOreAtLeast(int minTier, int amount)
        {
            if (GetOreCountAtLeast(minTier) < amount) return false;
            for (int t = Mathf.Max(0, minTier); t < OreTierHelper.MAX_TIERS && amount > 0; t++)
            {
                int take = Mathf.Min(amount, GetOreTierCount(t));
                if (take > 0) { ConsumeOre(t, take); amount -= take; }
            }
            return true;
        }
    }
}
