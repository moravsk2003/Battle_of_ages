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
            if (tier == 0) return "Камінна руда (рів. 0)";
            if (tier == 1) return "Мідно-залізна руда (рів. 1)";
            if (tier == 2) return "Срібно-золота руда (рів. 2)";
            if (tier == 3) return "Платинова руда (рів. 3)";
            if (tier == 4) return "Мітрилова руда (рів. 4)";
            if (tier == 5) return "Адамантитова руда (рів. 5)";
            if (tier >= 90) return $"Небесна руда (рів. {tier})";
            if (tier >= 50) return $"Драконова руда (рів. {tier})";
            if (tier >= 20) return $"Давня реліктова руда (рів. {tier})";
            return $"Рідкісна руда (рів. {tier})";
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
