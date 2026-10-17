using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Resources
{
    [DisallowMultipleComponent]
    public class ResourceManager : MonoBehaviour
    {
        public static ResourceManager Instance { get; private set; }

        [Header("Initial Resources")]
        [SerializeField] private int initialWood = 0;
        [SerializeField] private int initialOre = 0;

        public int WoodCount { get; private set; }
        public int OreCount { get; private set; }
        public int HighestOreTier { get; private set; } = 0;

        private readonly Dictionary<int, int> oreTierCounts = new Dictionary<int, int>();

        public event Action<ResourceType, int> OnResourceChanged;
        public event Action<int, int> OnOreTierAdded;

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

            WoodCount = initialWood;
            OreCount = initialOre;

            for (int i = 0; i < OreTierHelper.MAX_TIERS; i++)
            {
                oreTierCounts[i] = 0;
            }
        }

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
            WoodCount = Mathf.Max(0, wood);
            OreCount = 0;
            HighestOreTier = 0;
            for (int i = 0; i < OreTierHelper.MAX_TIERS; i++) oreTierCounts[i] = 0;
            if (ore != null)
                foreach (var pair in ore)
                {
                    int t = Mathf.Clamp(pair.Key, 0, OreTierHelper.MAX_TIERS - 1);
                    if (pair.Value <= 0) continue;
                    oreTierCounts[t] += pair.Value;
                    OreCount += pair.Value;
                    if (t > HighestOreTier) HighestOreTier = t;
                }
            OnResourceChanged?.Invoke(ResourceType.Wood, WoodCount);
            OnResourceChanged?.Invoke(ResourceType.Ore, OreCount);
        }

        public void ResetToInitial() => SetState(initialWood, initialOre > 0 ? new[] { new KeyValuePair<int, int>(0, initialOre) } : null);

        public void AddResource(ResourceType type, int amount)
        {
            if (amount <= 0) return;

            if (type == ResourceType.Wood)
            {
                WoodCount += amount;
                OnResourceChanged?.Invoke(ResourceType.Wood, WoodCount);
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

            OnResourceChanged?.Invoke(ResourceType.Ore, OreCount);
            OnOreTierAdded?.Invoke(tierLevel, amount);
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
                OnResourceChanged?.Invoke(ResourceType.Wood, WoodCount);
            }
            else if (type == ResourceType.Ore)
            {
                OreCount -= amount;
                OnResourceChanged?.Invoke(ResourceType.Ore, OreCount);
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

            OnResourceChanged?.Invoke(ResourceType.Ore, OreCount);
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
