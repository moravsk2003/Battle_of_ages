using UnityEngine;

namespace BattleOfAges.Resources
{
    public static class OreTierHelper
    {
        public const int MAX_TIERS = 100; // 100 levels of Ore (0 to 99)

        /// <summary>
        /// Rolls ore tier across 100 levels according to exact rule:
        /// Loop up to Tier 99:
        /// Roll 1..10. If != 1 (90% remaining chance) -> return current tier.
        /// If 1 (10%), advance to next tier!
        /// </summary>
        public static int DeterminePebbleOreTier()
        {
            int currentTier = 0;
            while (currentTier < MAX_TIERS - 1)
            {
                int roll = Random.Range(1, 11); // Roll 1 to 10
                if (roll != 1)
                {
                    return currentTier;
                }
                currentTier++;
            }
            return currentTier;
        }

        public static string GetTierName(int tier)
        {
            if (tier == 0) return "Common Stone Ore (Lvl 0)";
            if (tier == 1) return "Copper/Iron Ore (Lvl 1)";
            if (tier == 2) return "Silver/Gold Ore (Lvl 2)";
            if (tier == 3) return "Platinum Ore (Lvl 3)";
            if (tier == 4) return "Mithril Ore (Lvl 4)";
            if (tier == 5) return "Adamantite Ore (Lvl 5)";
            if (tier >= 90) return $"Godly Celestial Ore (Lvl {tier})";
            if (tier >= 50) return $"Mythic Dragon Ore (Lvl {tier})";
            if (tier >= 20) return $"Ancient Relic Ore (Lvl {tier})";
            return $"Rare Ore (Lvl {tier})";
        }

        public static Color GetTierColor(int tier)
        {
            if (tier == 0) return new Color(0.75f, 0.75f, 0.8f);
            if (tier == 1) return new Color(0.3f, 0.85f, 0.4f);
            if (tier == 2) return new Color(0.2f, 0.6f, 1.0f);
            if (tier == 3) return new Color(0.85f, 0.35f, 0.95f);
            if (tier >= 4) return Color.HSVToRGB((tier * 0.07f) % 1.0f, 0.85f, 1.0f);
            return Color.white;
        }
    }
}
