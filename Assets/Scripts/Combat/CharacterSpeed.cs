using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// Movement speed of every character (player, workers, knights, raiders; not wildlife), docs/GAME_DESIGN.md §11.1:
    /// speed = Global x SpeedModifier x sqrt(Strength). SpeedModifier is a per-character characteristic (person 7,
    /// warrior / knight / general 6, hero 5, see UnitRanks) and Strength is the character's strength characteristic
    /// (CombatUnit.Strength; its damage is strength x weapon modifier, so a better sword does not change the speed).
    /// </summary>
    public static class CharacterSpeed
    {
        /// <summary>Metres per second per (modifier x sqrt(strength)) point.</summary>
        public static float Global => GameBalance.SpeedGlobal;

        /// <summary>Characters without a CombatUnit move like a person.</summary>
        public static float DefaultModifier => GameBalance.SpeedModifierPerson;
        public static float HeroModifier => GameBalance.SpeedModifierHero;
        public static int DefaultStrength => GameBalance.RankStrength[(int)UnitRank.Person];

        public static float Of(float modifier, float strength) => Global * modifier * Mathf.Sqrt(Mathf.Max(0f, strength));

        public static float Of(CombatUnit combat) =>
            combat != null ? Of(combat.SpeedModifier, combat.SpeedStrength) : Of(DefaultModifier, DefaultStrength);
    }
}
