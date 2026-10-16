namespace BattleOfAges.Combat
{
    /// <summary>
    /// Movement speed of every character (player, workers, knights, raiders; not wildlife):
    /// speed = Global x SpeedModifier x Strength, where SpeedModifier is a per-character characteristic (5 for
    /// everyone, 2 for the hero) and Strength is the character's strength characteristic (CombatUnit.Strength; its damage
    /// is strength x weapon modifier, so a better sword does not change the speed). Global is the one tuning knob.
    /// </summary>
    public static class CharacterSpeed
    {
        /// <summary>Metres per second per (modifier x strength) point.</summary>
        public static float Global = 0.1f;

        public const float DefaultModifier = 5f;
        public const float HeroModifier = 2f;
        /// <summary>Strength of characters without a CombatUnit (workers): the default attack damage.</summary>
        public const int DefaultStrength = 10;

        public static float Of(CombatUnit combat) =>
            Global * (combat != null ? combat.SpeedModifier : DefaultModifier) * (combat != null ? combat.Strength : DefaultStrength);
    }
}
