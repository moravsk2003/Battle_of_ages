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
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            WoodCount = initialWood;
            OreCount = initialOre;

            for (int i = 0; i < OreTierHelper.MAX_TIERS; i++)
            {
                oreTierCounts[i] = 0;
            }
        }

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
    }
}
