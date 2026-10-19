using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;

namespace BattleOfAges.AI
{
    /// <summary>
    /// The stores and the hall of an NPC kingdom (docs/GAME_DESIGN.md §12), kept apart from the scene so its rules are
    /// tested on their own. Its people work at home without walking to the trees: each brings in a set amount per game
    /// hour (the abstract layer of §6.2 in small). Births and the hall's caps and costs are the player's (§10.3, §10.4).
    /// </summary>
    public sealed class KingdomEconomy
    {
        public float Wood, Ore;
        /// <summary>0: no hall yet (a camp), 1..5: hall I..V.</summary>
        public int HallLevel;
        public float BirthCarry;

        public int Cap => TownHall.CapForLevel(HallLevel);
        public bool MaxedOut => HallLevel >= TownHall.MaxLevel;
        /// <summary>Weapon and armour level of its soldiers: what its smiths make with the ore of its hall.</summary>
        public int GearLevel => Mathf.Max(1, HallLevel - 1);

        /// <summary>Wood and ore for the next hall level (building hall I, then the upgrades).</summary>
        public void NextCost(out int wood, out int ore)
        {
            int to = Mathf.Clamp(HallLevel + 1, 1, TownHall.MaxLevel);
            wood = GameBalance.TownHallWood[to];
            ore = GameBalance.TownHallOre[to];
        }

        public bool CanUpgrade
        {
            get
            {
                if (MaxedOut) return false;
                NextCost(out int wood, out int ore);
                return Wood >= wood && Ore >= ore;
            }
        }

        public bool TryUpgrade()
        {
            if (!CanUpgrade) return false;
            NextCost(out int wood, out int ore);
            Wood -= wood;
            Ore -= ore;
            HallLevel++;
            return true;
        }

        /// <summary>Work and births over these game hours; returns how many are born.</summary>
        public int Tick(float hours, int workers, int population)
        {
            Wood += workers * GameBalance.KingdomWoodPerWorkerHour * hours;
            Ore += workers * GameBalance.KingdomOrePerWorkerHour * hours;
            return TownHall.Births(ref BirthCarry, hours, population, Cap);
        }

        /// <summary>
        /// Strength of a group in a fight (Lanchester's square law): total HP x total damage per second. Two groups of
        /// equal strength wipe each other out; twice the units is four times the strength.
        /// </summary>
        public static float Power(float totalHealth, float totalDps) => totalHealth * totalDps;
    }
}
