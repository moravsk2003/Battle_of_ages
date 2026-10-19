using UnityEngine;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// The world-changing Central Empire artifact on the player's hero (docs/GAME_DESIGN.md §9.1.1): every unit wears
    /// gear by its rank (hero 5, warriors and up 4, people 3), nobody works, building and town hall upgrades are free.
    /// The bearer's death destroys the empire: a new world epoch begins (§10.4).
    /// </summary>
    public static class CentralEmpire
    {
        /// <summary>The player's kingdom is the Central Empire now.</summary>
        public static bool Active { get; private set; }
        /// <summary>World epochs opened so far (each fall of the empire opens one).</summary>
        public static int WorldEpoch { get; set; }

        public static void SetActive(bool on, GameObject hero)
        {
            if (on == Active) return;
            Active = on;
            if (on)
            {
                if (hero != null) Outfit(hero, GameBalance.EmpireHeroGear);
                foreach (RtsUnit u in UnitRegistry.ActiveUnits) if (u != null) ApplyTo(u.gameObject);
                UI.StrategyHud.Notify("Центральна імперія: усе військо в спорядженні, будівництво безкоштовне");
            }
            else
            {
                WorldEpoch++;
                UI.StrategyHud.Notify($"Центральну імперію знищено — настала нова епоха світу ({WorldEpoch})");
            }
        }

        /// <summary>A unit of the empire: gear by its rank, and it does not work.</summary>
        public static void ApplyTo(GameObject unit)
        {
            if (!Active || unit == null) return;
            UnitProgress p = unit.GetComponent<UnitProgress>();
            Outfit(unit, p != null && p.Rank != UnitRank.Person ? GameBalance.EmpireSoldierGear : GameBalance.EmpirePersonGear);
            NpcResourceGatherer job = unit.GetComponent<NpcResourceGatherer>();
            if (job != null) job.SetGatherDirective(GatherDirective.Idle);
        }

        private static void Outfit(GameObject unit, int level)
        {
            Equipment gear = unit.GetComponent<Equipment>();
            if (gear == null) return;
            gear.LoadSlots(new[]
            {
                new Item(ItemType.Sword, level), new Item(ItemType.Shield, level), new Item(ItemType.Helmet, level),
                new Item(ItemType.Armor, level), new Item(ItemType.Gloves, level), new Item(ItemType.Boots, level)
            });
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Active = false; WorldEpoch = 0; }
    }
}
