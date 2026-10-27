using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// Movement speed of every character (player, workers, knights, raiders; not wildlife), docs/GAME_DESIGN.md §11.1:
    /// speed = Global x SpeedModifier x sqrt(body / 2) + 1.5, body = (Strength + HP / 10) / 2. SpeedModifier is a
    /// per-character characteristic (person 7, warrior / knight / general 6, hero 5, see UnitRanks); Strength and HP are the
    /// body's own (CombatUnit.SpeedStrength / SpeedHealth: before the weapon and armour, so better gear does not change the
    /// speed). Every rank has 10 HP per strength point, so a plain unit runs as on its strength alone.
    /// </summary>
    public static class CharacterSpeed
    {
        /// <summary>Metres per second per (modifier x sqrt(strength)) point.</summary>
        public static float Global => GameBalance.SpeedGlobal;

        /// <summary>Characters without a CombatUnit move like a person.</summary>
        public static float DefaultModifier => GameBalance.SpeedModifierPerson;
        public static float HeroModifier => GameBalance.SpeedModifierHero;
        public static int DefaultStrength => GameBalance.RankStrength[(int)UnitRank.Person];

        public static float Of(float modifier, float strength, float health)
        {
            float body = (Mathf.Max(0f, strength) + Mathf.Max(0f, health) / GameBalance.SpeedHealthPerStrength) * 0.5f;
            return Global * modifier * Mathf.Sqrt(body / GameBalance.SpeedStrengthDivisor) + GameBalance.SpeedFlat;
        }

        /// <summary>A body with the usual 10 HP per strength point (every rank's base).</summary>
        public static float Of(float modifier, float strength) => Of(modifier, strength, strength * GameBalance.SpeedHealthPerStrength);

        public static float Of(CombatUnit combat) =>
            combat != null ? Of(combat.SpeedModifier, combat.SpeedStrength, combat.SpeedHealth) : Of(DefaultModifier, DefaultStrength);
    }
}
