using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.AI
{
    /// <summary>
    /// The world out of sight (M6 step 3, docs/GAME_DESIGN.md §6.2): once a game hour kings send raids on the same terms as
    /// real ones (NpcKingdom.Think), armies march at the pace of their slowest, battles are decided by AbstractBattle, a
    /// lost hall ends a kingdom, a dead king starts an interregnum and half the people leave; free heroes walk the world.
    /// Plain data: no object in the scene is touched; an army coming into the player's sight turns real where it is
    /// (KingdomRealm.Sync, NpcKingdom.FromState). Not modelled yet: free bands fighting on the road, heroes' levels and classes.
    /// </summary>
    public static class FarWorld
    {
        /// <summary>The player's settlement as a far king sees it: where, and how strong what guards it is (KingdomEconomy.Power).</summary>
        public struct PlayerTarget
        {
            public Vector3 Position;
            public float Guards;
        }

        /// <summary>
        /// One game hour (or several) of this kingdom against the others that are only data. player: the settlement a raid may go
        /// for; realHome: where a kingdom that is real now stands, by its id (an army on its way to one goes on).
        /// </summary>
        public static void Hour(KingdomState s, IReadOnlyList<KingdomState> world, float hours, System.Random rng, Vector3? keepOutOf = null,
                                PlayerTarget? player = null, System.Func<WorldId, Vector3?> realHome = null)
        {
            if (s.Defeated || hours <= 0f) return;
            for (int i = s.Armies.Count - 1; i >= 0; i--) MoveArmy(s, s.Armies[i], world, hours, rng, player, realHome);
            if (s.Defeated) return;
            if (s.Free) Wander(s, hours, rng, keepOutOf);
            else ConsiderRaid(s, world, player);
        }

        private static float FirstRaidAt(KingdomState s) => GameBalance.KingdomFirstRaidHours * (1f + Mathf.Repeat(KingdomTown.Seed(s.Name) * 0.618034f, 1f));

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ------------------------------------------------------------------ strength

        private static int Gear(KingdomState s, UnitRank rank)
        {
            if (s.Empire) return rank == UnitRank.General ? GameBalance.EmpireHeroGear : rank == UnitRank.Person ? GameBalance.EmpirePersonGear : GameBalance.EmpireSoldierGear;
            return Mathf.Max(1, s.Hall - 1);
        }

        private static List<UnitGroup> Groups(KingdomState s, List<UnitState> units, bool hero)
        {
            var counts = new int[GameBalance.RankHealth.Length];
            foreach (UnitState u in units) counts[Mathf.Clamp(u.Rank, 0, counts.Length - 1)]++;
            if (hero) counts[Mathf.Clamp(s.King.Rank, 0, counts.Length - 1)]++;
            var groups = new List<UnitGroup>();
            for (int r = 0; r < counts.Length; r++)
                if (counts[r] > 0) groups.Add(new UnitGroup((UnitRank)r, Gear(s, (UnitRank)r), counts[r]));
            return groups;
        }

        /// <summary>Total health times total damage per second (KingdomEconomy.Power), as a real king weighs a raid.</summary>
        public static float Power(KingdomState s, List<UnitState> units, bool hero)
        {
            float health = 0f, dps = 0f;
            foreach (UnitGroup g in Groups(s, units, hero))
            {
                UnitStats st = UnitStats.Of((UnitRank)g.Rank, g.Gear);
                health += st.Health * g.Count;
                dps += st.Dps * g.Count;
            }
            return KingdomEconomy.Power(health, dps);
        }

        private static float DpsOf(KingdomState s, List<UnitState> units, bool hero)
        {
            float dps = 0f;
            foreach (UnitGroup g in Groups(s, units, hero)) dps += UnitStats.Of((UnitRank)g.Rank, g.Gear).Dps * g.Count;
            return dps;
        }

        private static bool Attacked(KingdomState s, IReadOnlyList<KingdomState> world)
        {
            foreach (KingdomState k in world)
            {
                if (k == s || k.Defeated) continue;
                foreach (ArmyState a in k.Armies) if (!a.Returning && a.TargetKingdomId == s.Id) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ raids

        /// <summary>The terms of NpcKingdom.Think: not before its first hour, one at a time, after the cooldown, not with an enemy on its way; at the nearest it is strong enough for.</summary>
        public static bool ConsiderRaid(KingdomState s, IReadOnlyList<KingdomState> world, PlayerTarget? player = null)
        {
            if (s.Free || !s.HasKing || s.Armies.Count > 0 || s.Age < FirstRaidAt(s)) return false;
            if (s.Age < s.LastRaidEnd + GameBalance.KingdomRaidCooldownHours || Attacked(s, world)) return false;

            int stay = Mathf.RoundToInt(s.Subjects.Count * GameBalance.KingdomGarrisonShare);
            var everyone = new List<UnitState>(s.Subjects);
            everyone.Sort((a, b) => b.Rank.CompareTo(a.Rank)); // the weakest stay
            List<UnitState> party = everyone.GetRange(0, everyone.Count - stay);
            float ours = Power(s, party, true);

            KingdomState target = null;
            float nearest = float.MaxValue;
            foreach (KingdomState t in world)
            {
                if (t == s || t.Defeated || TakenByAnother(t, s, world)) continue;
                float d = FlatDistance(t.Home, s.Home);
                if (d >= nearest || ours < GameBalance.KingdomRaidPowerRatio * Power(t, t.Subjects, t.HasKing)) continue;
                nearest = d;
                target = t;
            }
            // The player as if nearer, as a real king weighs it (GameBalance.KingdomPlayerTargetFactor)
            if (player.HasValue && ours >= GameBalance.KingdomRaidPowerRatio * player.Value.Guards
                && FlatDistance(player.Value.Position, s.Home) * GameBalance.KingdomPlayerTargetFactor < nearest)
            {
                StartRaid(s, party, null).TargetsPlayer = true;
                return true;
            }
            if (target == null) return false;
            StartRaid(s, party, target);
            return true;
        }

        private static bool TakenByAnother(KingdomState target, KingdomState self, IReadOnlyList<KingdomState> world)
        {
            foreach (KingdomState k in world)
            {
                if (k == self || k.Defeated) continue;
                foreach (ArmyState a in k.Armies) if (!a.Returning && a.TargetKingdomId == target.Id) return true;
            }
            return false;
        }

        public static ArmyState StartRaid(KingdomState s, List<UnitState> party, KingdomState target)
        {
            var army = new ArmyState { Position = s.Home, TargetKingdomId = target != null ? target.Id : default, WithHero = s.HasKing, StartCount = party.Count };
            foreach (UnitState u in party) s.Subjects.Remove(u);
            army.Units.AddRange(party);
            s.Armies.Add(army);
            s.EnsureIds();
            return army;
        }

        /// <summary>The pace of its slowest (metres in an hour of the game).</summary>
        private static float MetresPerHour(KingdomState s, ArmyState army)
        {
            float slowest = float.MaxValue;
            foreach (UnitState u in army.Units) slowest = Mathf.Min(slowest, Speed(u.Rank));
            if (army.WithHero) slowest = Mathf.Min(slowest, CharacterSpeed.Of(GameBalance.SpeedModifierHero, GameBalance.RankStrength[(int)UnitRank.General]));
            return (slowest == float.MaxValue ? Speed((int)UnitRank.Person) : slowest) * GameBalance.GameHourSeconds;
        }

        private static float Speed(int rank)
        {
            int r = Mathf.Clamp(rank, 0, GameBalance.RankStrength.Length - 1);
            return CharacterSpeed.Of(r == (int)UnitRank.Person ? GameBalance.SpeedModifierPerson : GameBalance.SpeedModifierSoldier, GameBalance.RankStrength[r]);
        }

        private static KingdomState Find(IReadOnlyList<KingdomState> world, WorldId id)
        {
            foreach (KingdomState k in world) if (k.Id == id) return k;
            return null;
        }

        private static void MoveArmy(KingdomState s, ArmyState army, IReadOnlyList<KingdomState> world, float hours, System.Random rng, PlayerTarget? player, System.Func<WorldId, Vector3?> realHome)
        {
            KingdomState target = army.Returning || army.TargetsPlayer ? null : Find(world, army.TargetKingdomId);
            // Its target real now, or the player's: it marches on, and turns real itself on coming near (KingdomRealm.Sync)
            Vector3? beyond = army.Returning ? null : army.TargetsPlayer ? player?.Position : target == null ? realHome?.Invoke(army.TargetKingdomId) : null;
            if (!army.Returning && !beyond.HasValue && (target == null || target.Defeated)) army.Returning = true;
            // Still on the way and someone is coming for the home: back to defend it (as NpcKingdom.LeadRaid)
            if (!army.Returning && !army.AtTarget && Attacked(s, world)) army.Returning = true;

            Vector3 to = army.Returning ? s.Home : beyond ?? target.Home;
            float left = FlatDistance(army.Position, to), step = MetresPerHour(s, army) * hours;
            if (left > step + GameBalance.KingdomGuardRadius)
            {
                Vector3 way = to - army.Position;
                way.y = 0f;
                army.Position += way.normalized * step;
                return;
            }
            if (!army.Returning && beyond.HasValue) return; // at the door: only real units fight there
            army.Position = to;
            if (army.Returning) ComeHome(s, army, rng);
            else
            {
                army.AtTarget = true;
                Battle(s, army, target, hours, rng);
            }
        }

        private static void ComeHome(KingdomState s, ArmyState army, System.Random rng)
        {
            foreach (UnitState u in army.Units)
            {
                u.Position = Around(s.Home, rng);
                s.Subjects.Add(u);
            }
            s.Armies.Remove(army);
            s.LastRaidEnd = s.Age;
        }

        private static Vector3 Around(Vector3 home, System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 5f + (float)rng.NextDouble() * 5f;
            Vector3 p = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
            return p;
        }

        // ------------------------------------------------------------------ battle

        private static void Battle(KingdomState s, ArmyState army, KingdomState target, float hours, System.Random rng)
        {
            bool attackerHero = army.WithHero && s.HasKing, defenderHero = target.HasKing;
            List<UnitGroup> a = Groups(s, army.Units, attackerHero), b = Groups(target, target.Subjects, defenderHero);
            bool defended = b.Count > 0;
            if (defended && a.Count > 0)
            {
                AbstractBattle.Outcome o = AbstractBattle.Resolve(a, b);
                Kill(army.Units, o.LossA, rng);
                Kill(target.Subjects, o.LossB, rng);
                if (attackerHero && HeroFalls(o.LossA, rng)) { army.WithHero = false; KingDied(s, rng); }
                if (defenderHero && HeroFalls(o.LossB, rng)) KingDied(target, rng);
                if (o.Winner == 1 || s.Defeated || target.Defeated) army.Returning = true;
            }
            if (s.Defeated) return;

            // Nobody left to stop them: they break the hall, a hall lost ends the kingdom (NpcKingdom.OnHallDestroyed)
            bool clear = target.Subjects.Count == 0 && !target.HasKing;
            if (!army.Returning && clear && target.Hall > 0)
            {
                float damage = DpsOf(s, army.Units, army.WithHero && s.HasKing) * hours * GameBalance.GameHourSeconds;
                target.HallHealth = Mathf.Max(0, target.HallHealth - Mathf.RoundToInt(damage));
                if (target.HallHealth <= 0) Fall(target);
            }
            else if (clear && target.Hall <= 0 && !target.Defeated) Fall(target); // a camp with no one in it

            if (target.Defeated || (army.StartCount > 0 && army.Units.Count <= army.StartCount * (1f - GameBalance.KingdomRaidRetreatLosses))) army.Returning = true;
            if (army.Units.Count == 0 && !army.WithHero) { s.Armies.Remove(army); s.LastRaidEnd = s.Age; }
        }

        /// <summary>The side's lost share of its health, in dead: with the group's health spread evenly the same share of people fall.</summary>
        private static void Kill(List<UnitState> units, float share, System.Random rng)
        {
            int dead = Mathf.Clamp(Mathf.RoundToInt(units.Count * share), 0, units.Count);
            for (int i = 0; i < dead; i++) units.RemoveAt(rng.Next(units.Count));
        }

        /// <summary>A hero stands in the front line, but not alone: the more of its side fell, the likelier it is among them; a side wiped out loses it for sure.</summary>
        private static bool HeroFalls(float loss, System.Random rng) => loss >= 0.999f || rng.NextDouble() < loss * loss;

        private static void KingDied(KingdomState s, System.Random rng)
        {
            s.HasKing = false;
            if (s.Empire) { Fall(s); return; }
            s.Interregnum = GameBalance.InterregnumSeconds;
            // About half the people leave (NpcKingdom.Desert)
            int leave = Mathf.FloorToInt(s.Subjects.Count * GameBalance.DesertionShare);
            for (int i = 0; i < leave && s.Subjects.Count > 0; i++) s.Subjects.RemoveAt(rng.Next(s.Subjects.Count));
        }

        private static void Fall(KingdomState s)
        {
            s.Defeated = true;
            s.Subjects.Clear();
            s.Armies.Clear();
            if (!Application.isPlaying) return;
            if (s.Empire) CentralEmpire.NpcEmpireFell();
            UI.StrategyHud.Notify($"{s.Name} впало далеко звідси");
        }

        // ------------------------------------------------------------------ free heroes

        /// <summary>A free hero's band walks the world (§7.4): a while at a camp, then on to another, clear of lakes, the world's edge and the player's base.</summary>
        private static void Wander(KingdomState s, float hours, System.Random rng, Vector3? keepOutOf)
        {
            if (!s.HasKing) return;
            if (!s.Walking)
            {
                s.WaitHours -= hours;
                if (s.WaitHours > 0f) return;
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = GameBalance.FreeHeroWalkMin + (float)rng.NextDouble() * (GameBalance.FreeHeroWalkMax - GameBalance.FreeHeroWalkMin);
                Vector3 to = s.Home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (Lakes.InWater(to.x, to.z, 30f) || (WorldSettings.Bounded && !WorldSettings.Inside(to, 200f))) return; // another try next hour
                if (keepOutOf.HasValue && FlatDistance(to, keepOutOf.Value) < GameBalance.FreeHeroKeepOut) return;
                s.WalkingTo = to;
                s.Walking = true;
            }
            Vector3 way = s.WalkingTo - s.Home;
            way.y = 0f;
            float step = Speed((int)UnitRank.Person) * GameBalance.GameHourSeconds * hours;
            bool there = way.magnitude <= step;
            Vector3 next = there ? s.WalkingTo : s.Home + way.normalized * step;
            next.y = InfiniteWorldManager.GetSampledTerrainHeight(next.x, next.z);
            Vector3 moved = next - s.Home;
            s.Home = next;
            s.King.Position += moved;
            foreach (UnitState u in s.Subjects) u.Position += moved;
            if (!there) return;
            s.Walking = false;
            s.WaitHours = GameBalance.FreeHeroCampHoursMin + (float)rng.NextDouble() * (GameBalance.FreeHeroCampHoursMax - GameBalance.FreeHeroCampHoursMin);
        }
    }
}
