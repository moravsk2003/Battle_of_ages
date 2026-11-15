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
        /// <summary>
        /// Its town's buildings in ruins (KingdomTown.RuinCode: kind and place), rebuilt one at a time every
        /// GameBalance.TownRepairHours for their wood. While they lie, the work they hold up is less: ruined sawmills cut its
        /// wood, ruined mines its ore (down to GameBalance.TownRuinFloor: by hand), ruined houses its room for people.
        /// </summary>
        public System.Collections.Generic.List<int> Ruins = new System.Collections.Generic.List<int>();
        public float RepairClock;
        /// <summary>0: no hall yet (a camp), 1..5: hall I..V.</summary>
        public int HallLevel;
        public float BirthCarry;
        /// <summary>The Central Empire: its hall rises for nothing (§9.1.1).</summary>
        public bool Free;

        public int Cap => TownHall.CapForLevel(HallLevel);
        public bool MaxedOut => HallLevel >= TownHall.MaxLevel;
        /// <summary>Weapon and armour level of its soldiers: what its smiths make with the ore of its hall.</summary>
        public int GearLevel => Mathf.Max(1, HallLevel - 1);

        /// <summary>Wood and ore for the next hall level (building hall I, then the upgrades).</summary>
        public void NextCost(out int wood, out int ore)
        {
            int to = Mathf.Clamp(HallLevel + 1, 1, TownHall.MaxLevel);
            wood = Free ? 0 : GameBalance.TownHallWood[to];
            ore = Free ? 0 : GameBalance.TownHallOre[to];
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
            Wood += workers * GameBalance.KingdomWoodPerWorkerHour * hours * Share(KingdomTown.Kind.Sawmill, population);
            Ore += workers * GameBalance.KingdomOrePerWorkerHour * hours * Share(KingdomTown.Kind.Mine, population);
            Repair(hours);
            return TownHall.Births(ref BirthCarry, hours, population, Room);
        }

        /// <summary>How many of a kind lie in ruins.</summary>
        public int Ruined(KingdomTown.Kind kind)
        {
            int n = 0;
            foreach (int code in Ruins) if (KingdomTown.KindOf(code) == kind) n++;
            return n;
        }

        /// <summary>Room for people: the hall's cap less the houses in ruins.</summary>
        public int Room => Mathf.Max(0, Cap - Ruined(KingdomTown.Kind.House) * GameBalance.TownPeoplePerHouse);

        /// <summary>Of the work a kind of building holds up, the share still done (all of it with none in ruins).</summary>
        public float Share(KingdomTown.Kind kind, int population)
        {
            int lost = Ruined(kind);
            if (lost == 0) return 1f;
            int wanted = KingdomTown.Wanted(kind, HallLevel, population);
            float standing = wanted <= 0 ? 1f : Mathf.Clamp01(1f - lost / (float)wanted);
            return Mathf.Lerp(GameBalance.TownRuinFloor, 1f, standing);
        }

        /// <summary>The oldest ruin rises again every TownRepairHours, if its wood is there.</summary>
        private void Repair(float hours)
        {
            if (Ruins.Count == 0) { RepairClock = 0f; return; }
            RepairClock += hours;
            while (Ruins.Count > 0 && RepairClock >= GameBalance.TownRepairHours)
            {
                int wood = Free ? 0 : KingdomTown.RepairWood(KingdomTown.KindOf(Ruins[0]));
                if (Wood < wood) break;
                Wood -= wood;
                Ruins.RemoveAt(0);
                RepairClock -= GameBalance.TownRepairHours;
            }
        }

        /// <summary>
        /// Strength of a group in a fight (Lanchester's square law): total HP x total damage per second. Two groups of
        /// equal strength wipe each other out; twice the units is four times the strength.
        /// </summary>
        public static float Power(float totalHealth, float totalDps) => totalHealth * totalDps;
    }
}
