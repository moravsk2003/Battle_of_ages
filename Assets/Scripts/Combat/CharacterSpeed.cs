using Unity.Mathematics;
using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// Movement speed of every character (player, workers, knights, raiders; not wildlife), docs/GAME_DESIGN.md §11.1:
    /// speed = Global x SpeedModifier x sqrt(body / 2 + 0.5) + 3, body = (Strength + HP / 20) / 2. SpeedModifier is a
    /// per-character characteristic (person 7, warrior / knight / general 6, hero 5, see UnitRanks); Strength and HP are the
    /// body's own (CombatUnit.SpeedStrength / SpeedHealth: before the weapon and armour, so better gear does not change the
    /// speed). Every rank has 10 HP per strength point: its body is 0.75 of its strength.
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
            float body = (Mathf.Max(0f, strength) + Mathf.Max(0f, health) / GameBalance.SpeedHealthPerStrength) / GameBalance.SpeedBodyDivisor;
            return Global * modifier * Mathf.Sqrt(body / GameBalance.SpeedStrengthDivisor + GameBalance.SpeedBodyFlat) + GameBalance.SpeedFlat;
        }

        /// <summary>HP per strength point in every rank's base (and in what an attribute point buys).</summary>
        public static float RankHealthPerStrength => (float)GameBalance.HealthPerPoint / GameBalance.StrengthPerPoint;

        /// <summary>A body with the usual 10 HP per strength point (every rank's base).</summary>
        public static float Of(float modifier, float strength) => Of(modifier, strength, strength * RankHealthPerStrength);

        /// <summary>Cached default speed for characters without a CombatUnit (moving like a person).</summary>
        public static float DefaultSpeed => Of(DefaultModifier, DefaultStrength);

        public static float Of(CombatUnit combat) =>
            combat != null ? combat.BaseMoveSpeed : DefaultSpeed;

        /// <summary>
        /// How fast one goes for where it faces (the author's word, 2026-11-15): straight ahead whole, ahead and aside 90%,
        /// aside 80%, back and aside 65%, straight back half. From the cosine of the angle between facing and going; between
        /// those the speed runs evenly with the angle. Plain arithmetic: the Burst crowd job calls it too.
        /// </summary>
        public static float DirectionFactor(float cosine)
        {
            float degrees = math.degrees(math.acos(math.clamp(cosine, -1f, 1f)));
            if (degrees <= 45f) return math.lerp(1f, 0.9f, degrees / 45f);
            if (degrees <= 90f) return math.lerp(0.9f, 0.8f, (degrees - 45f) / 45f);
            if (degrees <= 135f) return math.lerp(0.8f, 0.65f, (degrees - 90f) / 45f);
            return math.lerp(0.65f, 0.5f, (degrees - 135f) / 45f);
        }

        /// <summary>DirectionFactor for a body facing forward and going along dir (flat; standing still: whole).</summary>
        public static float DirectionFactor(Vector3 forward, Vector3 dir)
        {
            forward.y = 0f; dir.y = 0f;
            if (forward.sqrMagnitude < 1e-6f || dir.sqrMagnitude < 1e-6f) return 1f;
            return DirectionFactor(Vector3.Dot(forward.normalized, dir.normalized));
        }
    }
}
