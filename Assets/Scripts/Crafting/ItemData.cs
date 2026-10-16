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
        Pickaxe,  // Кайло
        Shield,   // Щит
        Helmet,   // Шолом
        Gloves,   // Рукавиці
        Boots     // Чоботи
    }

    /// <summary>Where an item is worn. Weapon holds the sword, bow, axe or pickaxe; Armor is the chest piece.</summary>
    public enum EquipSlot { Weapon, Shield, Helmet, Chest, Gloves, Boots }

    /// <summary>An item: type + level (0..5; level 0 is the hero's plain starting sword). default(Item) = an empty slot.</summary>
    [Serializable]
    public struct Item : IEquatable<Item>
    {
        public ItemType Type;
        public int Level;
        public bool Present;

        public Item(ItemType type, int level) { Type = type; Level = Mathf.Clamp(level, 0, ItemStatCalculator.MAX_LEVEL); Present = true; }
        public static readonly Item None = default;
        public bool IsEmpty => !Present;
        public EquipSlot Slot => ItemTypeNames.SlotOf(Type);
        public bool Equals(Item o) => Present == o.Present && (!Present || (Type == o.Type && Level == o.Level));
        public override bool Equals(object o) => o is Item i && Equals(i);
        public override int GetHashCode() => Present ? (int)Type * 16 + Level + 1 : 0;
        public override string ToString() => IsEmpty ? "—" : ItemTypeNames.Display(Type) + " " + ItemTypeNames.Roman(Level);
    }

    public static class ItemTypeNames
    {
        public static readonly ItemType[] All =
            { ItemType.Sword, ItemType.Bow, ItemType.Axe, ItemType.Pickaxe, ItemType.Shield, ItemType.Helmet, ItemType.Armor, ItemType.Gloves, ItemType.Boots };
        public static readonly EquipSlot[] Slots =
            { EquipSlot.Weapon, EquipSlot.Shield, EquipSlot.Helmet, EquipSlot.Chest, EquipSlot.Gloves, EquipSlot.Boots };

        /// <summary>Ukrainian display name of an item type.</summary>
        public static string Display(ItemType t)
        {
            switch (t)
            {
                case ItemType.Sword: return "Меч";
                case ItemType.Bow: return "Лук";
                case ItemType.Armor: return "Нагрудник";
                case ItemType.Axe: return "Сокира";
                case ItemType.Pickaxe: return "Кайло";
                case ItemType.Shield: return "Щит";
                case ItemType.Helmet: return "Шолом";
                case ItemType.Gloves: return "Рукавиці";
                default: return "Чоботи";
            }
        }

        public static string SlotName(EquipSlot s)
        {
            switch (s)
            {
                case EquipSlot.Weapon: return "Зброя";
                case EquipSlot.Shield: return "Щит";
                case EquipSlot.Helmet: return "Шолом";
                case EquipSlot.Chest: return "Нагрудник";
                case EquipSlot.Gloves: return "Рукавиці";
                default: return "Чоботи";
            }
        }

        public static EquipSlot SlotOf(ItemType t)
        {
            switch (t)
            {
                case ItemType.Shield: return EquipSlot.Shield;
                case ItemType.Helmet: return EquipSlot.Helmet;
                case ItemType.Armor: return EquipSlot.Chest;
                case ItemType.Gloves: return EquipSlot.Gloves;
                case ItemType.Boots: return EquipSlot.Boots;
                default: return EquipSlot.Weapon;
            }
        }

        public static bool IsWeapon(ItemType t) => SlotOf(t) == EquipSlot.Weapon;

        public static string Roman(int level)
        {
            switch (level) { case 0: return "0"; case 1: return "I"; case 2: return "II"; case 3: return "III"; case 4: return "IV"; case 5: return "V"; default: return ""; }
        }

        /// <summary>What a level is made of (the look on the model follows it).</summary>
        public static string MaterialName(ItemType t, int level)
        {
            bool wood = t == ItemType.Bow || t == ItemType.Shield;
            string stem;
            switch (Mathf.Clamp(level, 0, ItemStatCalculator.MAX_LEVEL))
            {
                case 0: stem = "Звичайн"; break; // the hero's plain iron sword
                case 1: stem = wood ? "Прост" : t == ItemType.Armor || t == ItemType.Helmet || t == ItemType.Gloves || t == ItemType.Boots ? "Шкірян" : "Бронзов"; break;
                case 2: stem = "Залізн"; break;
                case 3: stem = "Сталев"; break;
                case 4: stem = "Воронован"; break;
                default: stem = "Позолочен"; break;
            }
            // agrees with the noun: сокира (f), кайло (n), рукавиці / чоботи (pl), the rest (m)
            string ending = t == ItemType.Axe ? "а" : t == ItemType.Pickaxe ? "е" : t == ItemType.Gloves || t == ItemType.Boots ? "і" : "ий";
            return stem + ending;
        }

        /// <summary>One line of what the item gives, for tooltips and recipe cards.</summary>
        public static string Describe(Item item)
        {
            int l = ItemStatCalculator.ClampLevel(item.Level);
            switch (item.Type)
            {
                case ItemType.Sword: return $"+{Pct(ItemStatCalculator.WeaponDamageMultiplier(item) - 1f)}% урону, швидкі удари";
                case ItemType.Bow: return $"+{Pct(ItemStatCalculator.WeaponDamageMultiplier(item) - 1f)}% урону, дальність {ItemStatCalculator.BOW_RANGE:0} м";
                case ItemType.Axe: return $"+{Pct(ItemStatCalculator.WeaponDamageMultiplier(item) - 1f)}% урону, рубка ×{ItemStatCalculator.CalculateToolEfficiency(l):0.##}";
                case ItemType.Pickaxe: return $"+{Pct(ItemStatCalculator.WeaponDamageMultiplier(item) - 1f)}% урону, видобуток ×{ItemStatCalculator.CalculateToolEfficiency(l):0.##}";
                case ItemType.Armor: return $"-{Pct(ItemStatCalculator.DamageReduction(item))}% урону, +{Pct(ItemStatCalculator.HealthBonus(item))}% здоров'я";
                case ItemType.Gloves: return $"-{Pct(ItemStatCalculator.DamageReduction(item))}% урону, удари швидші на {Pct(1f - ItemStatCalculator.CooldownMultiplier(item))}%";
                default: return $"-{Pct(ItemStatCalculator.DamageReduction(item))}% урону";
            }
        }

        private static int Pct(float f) => Mathf.RoundToInt(f * 100f);
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
            int baseCost;
            switch (type)
            {
                case ItemType.Sword: case ItemType.Armor: baseCost = 3; break;
                case ItemType.Helmet: case ItemType.Gloves: case ItemType.Boots: baseCost = 2; break;
                case ItemType.Shield: baseCost = 6; break;
                default: baseCost = 5; break; // bow, axe, pickaxe
            }
            return baseCost + (lvl - 1) * 2;
        }

        public static int CalculateOreCost(ItemType type, int level)
        {
            int lvl = ClampLevel(level);
            int baseCost;
            switch (type)
            {
                case ItemType.Sword: case ItemType.Armor: case ItemType.Helmet: baseCost = 2; break;
                default: baseCost = 1; break;
            }
            return baseCost + (lvl - 1) * 2;
        }

        // ------------------------------------------------------------------ equipment effects (Equipment component)

        public const float SWORD_DAMAGE_PER_LEVEL = 0.2f;
        public const float BOW_RANGE = 12f;
        public const float MAX_DAMAGE_REDUCTION = 0.6f;

        /// <summary>Attack damage multiplier of the item in the weapon slot (1 for anything else).</summary>
        public static float WeaponDamageMultiplier(Item item)
        {
            if (item.IsEmpty) return 1f;
            int l = ClampLevel(item.Level);
            switch (item.Type)
            {
                case ItemType.Sword: return 1f + SWORD_DAMAGE_PER_LEVEL * Mathf.Clamp(item.Level, 0, MAX_LEVEL); // +20% per level: 0 = x1, V = x2
                case ItemType.Bow: return 1.1f + 0.1f * l;      // +20% .. +60%, at range
                case ItemType.Axe: return 1.15f + 0.05f * l;    // +20% .. +40%
                case ItemType.Pickaxe: return 1.1f + 0.05f * l; // +15% .. +35%
                default: return 1f;
            }
        }

        /// <summary>Attack cooldown multiplier: light weapons and gloves speed the swings up.</summary>
        public static float CooldownMultiplier(Item item)
        {
            if (item.IsEmpty) return 1f;
            int l = ClampLevel(item.Level);
            switch (item.Type)
            {
                case ItemType.Sword: return 0.85f;
                case ItemType.Bow: return 0.75f;
                case ItemType.Gloves: return 1f - 0.03f * l;
                default: return 1f;
            }
        }

        /// <summary>Share of every incoming hit an armour piece absorbs; pieces add up (capped at MAX_DAMAGE_REDUCTION).</summary>
        public static float DamageReduction(Item item)
        {
            if (item.IsEmpty) return 0f;
            int l = ClampLevel(item.Level);
            switch (item.Type)
            {
                case ItemType.Armor: return 0.04f * l;  // up to 20%
                case ItemType.Shield: return 0.03f * l; // up to 15%
                case ItemType.Helmet: return 0.02f * l; // up to 10%
                case ItemType.Gloves: return 0.01f * l;
                case ItemType.Boots: return 0.01f * l;
                default: return 0f;
            }
        }

        /// <summary>Extra max health (share of the base) from the chest piece.</summary>
        public static float HealthBonus(Item item) => item.IsEmpty || item.Type != ItemType.Armor ? 0f : 0.06f * ClampLevel(item.Level);

        public static int CalculateRequiredOreTier(int level)
        {
            int lvl = ClampLevel(level);
            return lvl - 1; // Lvl 1: Tier 0, Lvl 2: Tier 1 ... Lvl 5: Tier 4 (any higher ore also counts)
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
