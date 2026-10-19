using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>Hero classes (docs/GAME_DESIGN.md §8.1); only heroes have a class.</summary>
    public enum HeroClass { None, Tank, Archer, Mage, Dd }

    /// <summary>The class rules as plain numbers (Equipment asks a hero for them through IStatModifier).</summary>
    public static class HeroClassRules
    {
        /// <summary>Tank +20% HP, archer -20% HP.</summary>
        public static float HealthFactor(HeroClass c) =>
            c == HeroClass.Tank ? 1f + GameBalance.TankHealthBonus
            : c == HeroClass.Archer ? 1f - GameBalance.ArcherHealthPenalty
            : 1f;

        /// <summary>The archer's strength is doubled while a bow is in hand (more damage, and faster through sqrt).</summary>
        public static float StrengthFactor(HeroClass c, bool bow) => c == HeroClass.Archer && bow ? 2f : 1f;

        /// <summary>DD attacks twice as fast; an archer swings a sword twice as slowly.</summary>
        public static float CooldownFactor(HeroClass c, bool bow) =>
            c == HeroClass.Dd ? 0.5f
            : c == HeroClass.Archer && !bow ? 2f
            : 1f;

        /// <summary>Shoots from afar: the archer with a bow, and the mage.</summary>
        public static bool IsRanged(HeroClass c, bool bow) => c == HeroClass.Mage || (c == HeroClass.Archer && bow);

        /// <summary>Range of a ranged class: the distance it runs in 3 s, never less than a plain bow (12 m).</summary>
        public static float Range(float runSpeed, float bonus = 0f) =>
            Mathf.Max(GameBalance.MinClassRange, runSpeed * GameBalance.ClassRangeRunSeconds) * (1f + bonus);

        /// <summary>The tank ignores every 10th hit (a counter, no chance).</summary>
        public static int IgnoredHitEvery(HeroClass c) => c == HeroClass.Tank ? GameBalance.TankIgnoreEveryNthHit : 0;

        public static string Name(HeroClass c)
        {
            switch (c)
            {
                case HeroClass.Tank: return "Танк";
                case HeroClass.Archer: return "Лучник";
                case HeroClass.Mage: return "Маг";
                case HeroClass.Dd: return "ДД";
                default: return "без класу";
            }
        }
    }

    /// <summary>Something on a unit that changes what its gear gives (a hero's class).</summary>
    public interface IStatModifier
    {
        float HealthFactor { get; }
        float StrengthFactor(bool bow);
        float CooldownFactor(bool bow);
        /// <summary>Range for this strength (the run speed follows the strength), or the given range unchanged.</summary>
        float Range(float range, bool bow, float strength);
        /// <summary>The weapon multiplier (an artifact sword takes the weapon slot).</summary>
        float WeaponMultiplier(float worn);
        /// <summary>The share of a hit armour stops (artifact armour takes the armour slot).</summary>
        float DamageReduction(float worn);
    }
}
