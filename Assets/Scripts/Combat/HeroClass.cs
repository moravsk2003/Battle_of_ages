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

        /// <summary>What the class does, in a line or two (the class picker's tooltip; docs/GAME_DESIGN.md §8.1).</summary>
        public static string Description(HeroClass c)
        {
            if (UI.Loc.En)
            {
                switch (c)
                {
                    case HeroClass.Tank: return "+20% HP; ignores every 10th hit. Skills: Regeneration, Instant Heal.";
                    case HeroClass.Archer: return "−20% HP; with a bow ×2 strength (damage & run speed), range from speed (min 12 m). Skills: Long Shot, Piercing Arrow.";
                    case HeroClass.Mage: return "With a staff: 5 homing bolts at 20% damage each, pierces armor, never misses; cannot attack without a staff. Skills: Fire Fan, Meteor.";
                    case HeroClass.Dd: return "Attacks twice as fast. Skills: Dash, Critical Hit.";
                    default: return "";
                }
            }
            switch (c)
            {
                case HeroClass.Tank: return "+20% HP; кожен 10-й удар по ньому не проходить. Навики: регенерація, миттєвий відхіл.";
                case HeroClass.Archer: return "−20% HP; з луком сила ×2 (шкода і біг), дальність від швидкості, не менше 12 м. Навики: далекий постріл, пробивна стріла.";
                case HeroClass.Mage: return "З посохом: 5 самонавідних куль по 20% шкоди, крізь броню, без промахів; без посоха не атакує. Навики: вогняне віяло, метеор.";
                case HeroClass.Dd: return "Б'є вдвічі частіше. Навики: ривок, критичний удар.";
                default: return "";
            }
        }

        public static string Name(HeroClass c)
        {
            switch (c)
            {
                case HeroClass.Tank: return UI.Loc.Pick("Танк", "Tank");
                case HeroClass.Archer: return UI.Loc.Pick("Лучник", "Archer");
                case HeroClass.Mage: return UI.Loc.Pick("Маг", "Mage");
                case HeroClass.Dd: return UI.Loc.Pick("ДД", "DD");
                default: return UI.Loc.Pick("без класу", "No Class");
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
        /// <summary>The strength the run speed follows (normally the strength itself).</summary>
        float SpeedStrength(float strength);
        /// <summary>The HP the run speed follows (normally the body's HP itself).</summary>
        float SpeedHealth(float health);
        /// <summary>Attacks with bolts, and only with a staff in hand (the mage).</summary>
        bool CastsWithStaff { get; }
    }
}
