using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.Combat
{
    /// <summary>The four unit ranks (docs/GAME_DESIGN.md §11.1); every unit both fights and works, the rank is only stats.</summary>
    public enum UnitRank { Person, Warrior, Knight, General }

    /// <summary>The model a unit of each rank wears (one place: kingdoms, other players' units, the review shots).</summary>
    public static class RankLooks
    {
        public static Utils.HumanoidModelBuilder.HumanType ModelFor(UnitRank rank) => rank switch
        {
            UnitRank.Person => Utils.HumanoidModelBuilder.HumanType.Villager,
            UnitRank.Warrior => Utils.HumanoidModelBuilder.HumanType.Soldier, // a kingdom's man in his gambeson, not one of the bandits
            UnitRank.Knight => Utils.HumanoidModelBuilder.HumanType.KnightGuard,
            _ => Utils.HumanoidModelBuilder.HumanType.General
        };

        /// <summary>The rank a model stands for (a unit built with it starts there).</summary>
        public static UnitRank RankOf(Utils.HumanoidModelBuilder.HumanType type) => type switch
        {
            Utils.HumanoidModelBuilder.HumanType.Soldier => UnitRank.Warrior,
            Utils.HumanoidModelBuilder.HumanType.KnightGuard => UnitRank.Knight,
            Utils.HumanoidModelBuilder.HumanType.General => UnitRank.General,
            _ => UnitRank.Person
        };
    }

    public static class UnitRanks
    {
        public static int Health(UnitRank rank, bool hero = false) => Scaled(GameBalance.RankHealth[(int)rank], hero);
        public static int Strength(UnitRank rank, bool hero = false) => Scaled(GameBalance.RankStrength[(int)rank], hero);
        public static int KillXp(UnitRank rank) => GameBalance.RankKillXp[(int)rank];

        public static float SpeedModifier(UnitRank rank, bool hero = false) =>
            hero ? GameBalance.SpeedModifierHero
            : rank == UnitRank.Person ? GameBalance.SpeedModifierPerson
            : GameBalance.SpeedModifierSoldier;

        private static int Scaled(int value, bool hero) => hero ? Mathf.RoundToInt(value * GameBalance.HeroStatMultiplier) : value;

        /// <summary>
        /// Gives the unit the base stats of its rank (gear is added on top, as Equipment does for any base) and the
        /// speed modifier; the worn kit stays.
        /// </summary>
        public static void Apply(GameObject unit, UnitRank rank, bool hero = false, float cooldown = -1f, float range = 3f)
        {
            if (cooldown < 0f) cooldown = GameBalance.BaseAttackCooldown;
            CombatUnit combat = unit.GetComponent<CombatUnit>();
            if (combat != null) combat.SpeedModifier = SpeedModifier(rank, hero);
            Equipment gear = unit.GetComponent<Equipment>();
            if (gear != null) { gear.SetBaseStats(Health(rank, hero), Strength(rank, hero), cooldown, range); return; }
            HealthComponent health = unit.GetComponent<HealthComponent>();
            if (health != null) health.InitializeHealth(Health(rank, hero));
            if (combat != null) { combat.Strength = Strength(rank, hero); combat.InitializeCombatStats(Strength(rank, hero), cooldown, range); }
        }
    }
}
