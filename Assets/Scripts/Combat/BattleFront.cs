using UnityEngine;
using BattleOfAges.RTS;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// The pull of a battle (docs/GAME_DESIGN.md §11.1): every few seconds each side that is in a fight (any of its units
    /// within BattleRadius of an enemy) gets one direction, the average way to the enemies its units face. While the
    /// fight lasts, a unit of that side moving that way runs 5% faster, one moving away 5% slower (sideways: no change).
    /// Counted per side, not per unit, and only while there is a battle: a few hundred grid lookups every 10 s, then
    /// one dot product per moving unit.
    /// </summary>
    public static class BattleFront
    {
        private const int Teams = 64; // the player, camps, wildlife and the NPC kingdoms' sides (UnitSpatialGrid.TeamKingdomFirst..)
        private static readonly bool[] inBattle = new bool[Teams];
        private static readonly Vector3[] front = new Vector3[Teams];
        private static float nextCheck;

        public static bool InBattle(int team) => team >= 0 && team < Teams && inBattle[team];
        public static Vector3 Front(int team) => team >= 0 && team < Teams ? front[team] : Vector3.zero;

        /// <summary>Speed factor for a unit of the team moving this way: 1 ± BattlePull by how much it heads to the front.</summary>
        public static float Factor(int team, Vector3 moveDir)
        {
            if (!InBattle(team)) return 1f;
            moveDir.y = 0f;
            if (moveDir.sqrMagnitude < 1e-6f) return 1f;
            return 1f + GameBalance.BattlePull * Vector3.Dot(moveDir.normalized, front[team]);
        }

        /// <summary>Called every frame (WorldClockDriver): refreshes the fronts every BattleFrontSeconds.</summary>
        public static void Tick()
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + GameBalance.BattleFrontSeconds;
            Recompute();
        }

        /// <summary>Which sides fight and which way their enemies are (now).</summary>
        public static void Recompute()
        {
            var sum = new Vector3[Teams];
            for (int t = 0; t < Teams; t++) inBattle[t] = false;
            UnitSpatialGrid.Refresh();
            int capacity = UnitSpatialGrid.Capacity;
            for (int id = 0; id < capacity; id++)
            {
                if (!UnitSpatialGrid.IsAlive(id)) continue;
                int team = UnitSpatialGrid.Team(id);
                if (team == UnitSpatialGrid.TeamNeutral || team < 0 || team >= Teams) continue;
                Vector3 p = UnitSpatialGrid.Position(id);
                int enemy = UnitSpatialGrid.FindNearestEnemy(p, GameBalance.BattleRadius, team);
                if (enemy < 0) continue;
                Vector3 to = UnitSpatialGrid.Position(enemy) - p;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f) sum[team] += to.normalized;
                inBattle[team] = true;
            }
            for (int t = 0; t < Teams; t++)
            {
                front[t] = sum[t].sqrMagnitude > 1e-6f ? sum[t].normalized : Vector3.zero;
                if (front[t] == Vector3.zero) inBattle[t] = false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            for (int t = 0; t < Teams; t++) { inBattle[t] = false; front[t] = Vector3.zero; }
            nextCheck = 0f;
        }
    }
}
