using System;
using UnityEngine;

namespace BattleOfAges.Crafting
{
    public enum ItemType
    {
        Sword,    // Меч
        Bow,      // Лук
        Armor,    // Броня
        Axe,      // Сокира
        Pickaxe   // Кайло
    }

    [System.Serializable]
    public struct ItemDefinition
    {
        public ItemType Type;
        public string DisplayName;
        public string Description;
        public float BaseDamage;
        public float BaseDefense;
        public float BaseEfficiency;
        public float BaseCraftTimeSeconds;
        public int BaseWoodCost;
        public int BaseOreCost;
    }

    /// <summary>
    /// Pure mathematical calculation engine for Item Scaling (Levels 1 to 5) and Combat Defense.
    /// Fully covered by EditMode Unit Tests.
    /// </summary>
    public static class ItemStatCalculator
    {
        public const int MIN_LEVEL = 1;
        public const int MAX_LEVEL = 5;

        // Sword: +20% damage per level (+0.20 * level)
        public const float SWORD_BASE_DAMAGE = 25.0f;
        public const float SWORD_SCALING_PER_LEVEL = 0.20f;

        // Armor: 10% damage reduction per level (10% * level)
        public const float ARMOR_BASE_DEFENSE = 10.0f;
        public const float ARMOR_SCALING_PER_LEVEL = 0.10f;

        // Bow: +20% damage per level (+0.20 * level)
        public const float BOW_BASE_DAMAGE = 20.0f;
        public const float BOW_SCALING_PER_LEVEL = 0.20f;

        // Tools: +25% efficiency per level (+0.25 * level)
        public const float TOOL_BASE_EFFICIENCY = 1.0f;
        public const float TOOL_SCALING_PER_LEVEL = 0.25f;

        public static int ClampLevel(int level)
        {
            return Mathf.Clamp(level, MIN_LEVEL, MAX_LEVEL);
        }

        public static float CalculateSwordDamage(int level, float baseDamage = SWORD_BASE_DAMAGE)
        {
            int lvl = ClampLevel(level);
            return baseDamage * (1.0f + SWORD_SCALING_PER_LEVEL * lvl);
        }

        public static float CalculateArmorDefense(int level, float baseDefense = ARMOR_BASE_DEFENSE)
        {
            int lvl = ClampLevel(level);
            return baseDefense * (1.0f + ARMOR_SCALING_PER_LEVEL * lvl);
        }

        public static float CalculateArmorReductionPercent(int level)
        {
            int lvl = ClampLevel(level);
            return Mathf.Clamp(ARMOR_SCALING_PER_LEVEL * lvl, 0.0f, 0.80f);
        }

        public static float CalculateBowDamage(int level, float baseDamage = BOW_BASE_DAMAGE)
        {
            int lvl = ClampLevel(level);
            return baseDamage * (1.0f + BOW_SCALING_PER_LEVEL * lvl);
        }

        public static float CalculateToolEfficiency(int level, float baseEfficiency = TOOL_BASE_EFFICIENCY)
        {
            int lvl = ClampLevel(level);
            return baseEfficiency * (1.0f + TOOL_SCALING_PER_LEVEL * lvl);
        }

        public static int CalculateWoodCost(ItemType type, int level)
        {
            int lvl = ClampLevel(level);
            int baseCost = (type == ItemType.Sword || type == ItemType.Armor) ? 3 : 5;
            return baseCost + (lvl - 1) * 2;
        }

        public static int CalculateOreCost(ItemType type, int level)
        {
            int lvl = ClampLevel(level);
            int baseCost = (type == ItemType.Sword || type == ItemType.Armor) ? 2 : 1;
            return baseCost + (lvl - 1) * 2;
        }

        public static int CalculateRequiredOreTier(int level)
        {
            int lvl = ClampLevel(level);
            return (lvl - 1) * 5; // Lvl 1: Tier 0, Lvl 2: Tier 5, Lvl 3: Tier 10, etc.
        }

        public static float CalculateCraftTime(ItemType type, int level)
        {
            int lvl = ClampLevel(level);
            float baseTime = 3.0f;
            return baseTime + (lvl - 1) * 1.5f;
        }

        /// <summary>
        /// Calculates damage received after armor defense reduction:
        /// DamageReduction = 10% * ArmorLevel (e.g. Lvl 1 = 10%, Lvl 2 = 20%, ..., Lvl 5 = 50%)
        /// EffectiveDamage = max(1, RawDamage * (1 - 0.10 * ArmorLevel))
        /// </summary>
        public static int CalculateEffectiveDamageAgainstArmorLevel(int rawDamage, int armorLevel)
        {
            if (rawDamage <= 0) return 0;
            if (armorLevel <= 0) return rawDamage;

            float reduction = CalculateArmorReductionPercent(armorLevel);
            float effective = rawDamage * (1.0f - reduction);

            return Mathf.Max(1, Mathf.RoundToInt(effective));
        }

        public static int CalculateEffectiveDamage(int rawDamage, float defense)
        {
            if (rawDamage <= 0) return 0;
            if (defense <= 0f) return rawDamage;

            float reduction = defense / (defense + 50.0f);
            float effective = rawDamage * (1.0f - reduction);

            return Mathf.Max(1, Mathf.RoundToInt(effective));
        }

        public const float HIGH_GROUND_THRESHOLD_Y = 1.2f;
        public const float HIGH_GROUND_DAMAGE_MULTIPLIER = 1.10f; // +10% damage bonus from high ground / walls

        public static int CalculateDamageWithHighGround(int baseDamage, float attackerY, float targetY)
        {
            if (baseDamage <= 0) return 0;
            if (attackerY > targetY + HIGH_GROUND_THRESHOLD_Y)
            {
                return Mathf.RoundToInt(baseDamage * HIGH_GROUND_DAMAGE_MULTIPLIER);
            }
            return baseDamage;
        }
    }
}
