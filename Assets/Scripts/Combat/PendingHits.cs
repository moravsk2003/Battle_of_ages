using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// Swings on their way (docs/GAME_DESIGN.md §11.1): an attack lands after its wind-up, not at once, so a unit that is
    /// walked up to has time to swing back. A started swing lands even if its striker falls meanwhile (a trade), as long
    /// as the target is still in reach. Kept here, not on the striker, because a fallen unit's components are switched
    /// off; WorldClockDriver ticks the list.
    /// </summary>
    public static class PendingHits
    {
        /// <summary>A target may step this much past the striker's reach during the wind-up and still be hit.</summary>
        public const float ReachSlack = 1.5f;

        private struct Hit
        {
            public CombatUnit Striker;
            public HealthComponent Target;
            public int Damage;
            public bool Bolts;
            public float At, Reach;
            public Vector3 From;
        }

        private static readonly List<Hit> hits = new List<Hit>(64);
        private static readonly List<(float at, System.Action act)> actions = new List<(float, System.Action)>(16);
        public static int Count => hits.Count;

        public static void Add(CombatUnit striker, HealthComponent target, int damage, bool bolts, float windup, float reach)
        {
            hits.Add(new Hit { Striker = striker, Target = target, Damage = damage, Bolts = bolts, At = Time.time + windup,
                               Reach = reach, From = striker.transform.position });
        }

        /// <summary>Runs an effect after its wind-up (a skill cast like an attack: the fan, the meteor).</summary>
        public static void AddAction(float delay, System.Action act) => actions.Add((Time.time + delay, act));

        /// <summary>Lands every swing whose wind-up is over.</summary>
        public static void Tick() => Tick(Time.time);

        public static void Tick(float now)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                if (actions[i].at > now) continue;
                System.Action act = actions[i].act;
                actions.RemoveAt(i--);
                act();
            }
            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i].At > now) continue;
                Hit h = hits[i];
                hits.RemoveAt(i--);
                Land(h);
            }
        }

        /// <summary>Lands every swing now (edit-mode tests, where time does not run).</summary>
        public static void FlushAll() => Tick(float.PositiveInfinity);

        private static void Land(Hit h)
        {
            if (h.Target == null || !h.Target.IsAlive) return;
            Vector3 from = h.Striker != null ? h.Striker.transform.position : h.From;
            if (Vector3.Distance(from, h.Target.transform.position) > h.Reach + 1f + ReachSlack) return; // it got away
            if (h.Striker == null) { h.Target.TakeDamage(h.Damage); return; }
            if (h.Bolts) { MageBolt.Volley(h.Striker, h.Target, h.Damage); return; }
            h.Striker.DeliverHit(h.Target, h.Damage, ignoreArmor: false);
            if (h.Striker.IsRanged && h.Striker.PierceShare > 0f) h.Striker.PierceBehind(h.Target, h.Damage); // the archer's Piercing arrow
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { hits.Clear(); actions.Clear(); }
    }
}
