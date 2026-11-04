using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.AI
{
    /// <summary>
    /// Every NPC hero of a campaign (M1 steps 4-5, docs/GAME_DESIGN.md §18.2): kings and free heroes on their start
    /// sites. A kingdom is real (NpcKingdom, people and a hall) when it is one of the few nearest the player's home or
    /// the hero comes near; otherwise it is only its state (the save's KingdomState), and it grows by formula once a game
    /// hour on the same numbers as a real one (work, births, promotions, the hall): the first small piece of the
    /// abstract layer (§6.2, M6). Coming near makes it real from its state; leaving folds it back. The endless seed-0
    /// world keeps M0's three kings at hand and does not use this.
    /// </summary>
    public sealed class KingdomRealm : MonoBehaviour
    {
        private static readonly List<KingdomState> far = new List<KingdomState>();
        private static KingdomRealm instance;
        private float hourClock, syncClock;
        /// <summary>People of a kingdom made real that come into the scene each frame.</summary>
        public const int ArrivalsPerFrame = 2;

        public static bool Active => WorldSettings.Bounded;
        /// <summary>The kingdoms that are only data now.</summary>
        public static IReadOnlyList<KingdomState> Far => far;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { far.Clear(); instance = null; }

        /// <summary>Forgets every far kingdom (a new campaign, back to the menu): the next campaign founds its own.</summary>
        public static void Clear() => far.Clear();

        private static void Ensure()
        {
            if (instance == null) instance = new GameObject("KingdomRealm").AddComponent<KingdomRealm>();
        }

        /// <summary>Is any kingdom against the player still standing out of sight (no victory yet)?</summary>
        public static bool AnyStandingFar()
        {
            foreach (KingdomState s in far) if (!s.Defeated && !s.Free) return true;
            return false;
        }

        /// <summary>
        /// The nearest kingdom against the player still standing, near or far (no free band): false when there is none.
        /// It is what the minimap points at until the world map of §5.4 is there: the world is kilometres wide and
        /// nothing else tells the player which way to go.
        /// </summary>
        public static bool NearestEnemy(Vector3 from, out string name, out Vector3 at)
        {
            string bestName = null;
            Vector3 bestAt = from;
            float best = float.MaxValue;
            void Consider(string n, Vector3 p)
            {
                float d = Flat(from, p);
                if (d < best) { best = d; bestName = n; bestAt = p; }
            }
            foreach (KingdomState s in far) if (!s.Defeated && !s.Free) Consider(s.Name, s.Home);
            foreach (NpcKingdom k in NpcKingdom.All) if (k != null && !k.IsDefeated && !k.Free && k.AgainstPlayer) Consider(k.KingdomName, k.Home);
            name = bestName;
            at = bestAt;
            return name != null;
        }

        // ------------------------------------------------------------------ a new campaign

        /// <summary>The campaign's heroes on their sites: heroes - 1 of them (the player is one), a share kings, the rest free.</summary>
        public static void FoundCampaign(int heroes = -1, float kingShare = -1f)
        {
            if (heroes < 0) heroes = GameBalance.CampaignHeroes;
            if (kingShare < 0f) kingShare = GameBalance.CampaignKingShare;
            NpcKingdom.RemoveAll();
            far.Clear();
            List<Vector3> sites = StartSites.Choose(Mathf.Max(0, heroes - 1));
            int kings = Mathf.RoundToInt(sites.Count * kingShare);
            var rng = new System.Random(WorldSettings.Seed ^ 0x4B49);
            int empire = CentralEmpire.Active ? -1 : EmpireSite(sites);
            for (int i = 0; i < sites.Count; i++)
            {
                bool free = i >= kings && i != empire;
                string name = NpcKingdom.CampaignNames[i % NpcKingdom.CampaignNames.Length];
                KingdomState state = NewState(sites[i], i == empire ? "Центральна імперія" : free ? "Ватага: " + name : name, free, RTS.UnitSpatialGrid.TeamKingdomFirst + i, rng);
                if (i == empire)
                {
                    state.Empire = true;
                    state.Hall = 1;
                    state.HallHealth = GameBalance.TownHallHealth[1];
                }
                far.Add(state);
            }
            Ensure();
            instance.Sync();
        }

        /// <summary>
        /// Every world has its Central Empire (§9.1.1), unless the player's hero is its bearer: on the site furthest from the
        /// player's start, the middle of the map, so that the strongest of all is not at the door at the start of the game.
        /// </summary>
        private static int EmpireSite(List<Vector3> sites)
        {
            int best = -1;
            float bestSqr = -1f;
            for (int i = 0; i < sites.Count; i++)
            {
                float d = sites[i].x * sites[i].x + sites[i].z * sites[i].z;
                if (d > bestSqr) { bestSqr = d; best = i; }
            }
            return best;
        }

        /// <summary>A hero's start (§7.1): the hero, KingdomStartPeople people and KingdomStartWood wood on level ground, no hall.</summary>
        public static KingdomState NewState(Vector3 site, string name, bool free, int team, System.Random rng)
        {
            BuildSite.FindLevel(site, 7f, GameBalance.KingdomSiteSearchRadius, GameBalance.KingdomSiteMaxRise, out site);
            var s = new KingdomState
            {
                Name = name, Home = site, Hall = 0, Wood = GameBalance.KingdomStartWood, Free = free, Team = team, HasKing = true,
                KingClass = rng.Next((int)HeroClass.Tank, (int)HeroClass.Dd + 1),
                King = new UnitState { Rank = (int)UnitRank.General, Position = Around(site, rng) }
            };
            for (int i = 0; i < GameBalance.KingdomStartPeople; i++) s.Subjects.Add(new UnitState { Rank = (int)UnitRank.Person, Position = Around(site, rng) });
            return s.EnsureIds();
        }

        private static Vector3 Around(Vector3 home, System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 5f + (float)rng.NextDouble() * 5f;
            Vector3 p = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
            return p;
        }

        // ------------------------------------------------------------------ saves

        /// <summary>Every kingdom for a save, real or far.</summary>
        public static List<KingdomState> AllStates()
        {
            var list = new List<KingdomState>(far);
            foreach (NpcKingdom k in NpcKingdom.All) if (k != null) list.Add(k.ToState());
            return list;
        }

        /// <summary>A loaded campaign: all of them far first, then the ones that should be real become real.</summary>
        public static void Load(List<KingdomState> states)
        {
            NpcKingdom.RemoveAll();
            far.Clear();
            far.AddRange(states);
            Ensure();
            instance.Sync();
        }

        // ------------------------------------------------------------------ near and far

        private void Update()
        {
            if (!Active) return;
            float hours = WorldClock.HoursOf(Time.deltaTime);
            hourClock += hours;
            if (hourClock >= 1f)
            {
                foreach (KingdomState s in far) s.PendingHours += hourClock;
                hourClock = 0f;
                tickCursor = 0;
            }
            TickFar();
            foreach (NpcKingdom k in NpcKingdom.All) if (k != null && k.Arriving > 0) k.ArriveMore(ArrivalsPerFrame);
            syncClock -= Time.deltaTime;
            if (syncClock <= 0f) { syncClock = 1f; Sync(); }
        }

        private int tickCursor;
        private readonly System.Diagnostics.Stopwatch tickWatch = new System.Diagnostics.Stopwatch();

        /// <summary>Lives the far kingdoms' hour a few at a time, within the frame's budget (§17: at most 2 ms); the rest next frame.</summary>
        private void TickFar()
        {
            if (tickCursor >= far.Count) return;
            tickWatch.Restart();
            Vector3 home = HomeOfPlayer();
            while (tickCursor < far.Count && tickWatch.Elapsed.TotalMilliseconds < GameBalance.FarTickBudgetMs)
            {
                KingdomState s = far[tickCursor++];
                if (s.PendingHours <= 0f) continue;
                float hours = s.PendingHours;
                s.PendingHours = 0f;
                Advance(s, hours);
                FarWorld.Hour(s, far, hours, new System.Random(KingdomTown.Seed(s.Name) ^ Mathf.FloorToInt(s.Age * 7f)), home);
            }
        }

        private static Vector3 HomeOfPlayer() => TownHall.Current != null ? TownHall.Current.transform.position : Vector3.zero;

        private static Vector3 Hero()
        {
            GameObject p = Managers.GameContext.Player;
            return p != null ? p.transform.position : HomeOfPlayer();
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>Makes real what should be (the nearest to home, those by the hero) and folds the rest into their states.</summary>
        public void Sync()
        {
            if (!Active) return;
            Vector3 home = HomeOfPlayer(), hero = Hero();
            var standing = new List<(float d, KingdomState s, NpcKingdom k)>();
            foreach (KingdomState s in far) if (!s.Defeated) standing.Add((Flat(s.Home, home), s, null));
            foreach (NpcKingdom k in NpcKingdom.All) if (k != null && !k.IsDefeated) standing.Add((Flat(k.Home, home), null, k));
            standing.Sort((a, b) => a.d.CompareTo(b.d));
            for (int i = 0; i < standing.Count; i++)
            {
                var (_, s, k) = standing[i];
                bool nearHome = i < GameBalance.RealKingdomsNearHome;
                if (s != null)
                {
                    if (nearHome || Flat(s.Home, hero) < GameBalance.MaterializeRadius) { far.Remove(s); NpcKingdom.FromState(s, gradual: true); }
                }
                else if (!nearHome && Flat(k.Home, hero) > GameBalance.DematerializeRadius && !k.Raiding && !k.UnderRaid())
                {
                    far.Add(k.ToState());
                    k.Dissolve();
                }
            }
        }

        /// <summary>
        /// A far kingdom over these game hours, by the rules a real one lives by: everyone works at home, births up to the
        /// hall's cap, people become warriors from work, the hall is raised when the room runs out (not a free hero's),
        /// an interregnum runs out into the strongest one's reign.
        /// </summary>
        public static void Advance(KingdomState s, float hours)
        {
            if (s.Defeated || hours <= 0f) return;
            s.Age += hours;
            if (!s.HasKing)
            {
                s.Interregnum -= hours * GameBalance.GameHourSeconds;
                if (s.Interregnum <= 0f) Crown(s);
                return;
            }
            var economy = new KingdomEconomy { Wood = s.Wood, Ore = s.Ore, HallLevel = s.Hall, BirthCarry = s.Births, Free = s.Empire };
            int born = economy.Tick(hours, s.Empire ? 0 : s.Subjects.Count, s.Subjects.Count);
            var rng = new System.Random(s.Name.GetHashCode() ^ Mathf.FloorToInt(s.Age * 7f)); // (the same hour's draws: births, work, training)
            for (int i = 0; i < born; i++) s.Subjects.Add(new UnitState { Rank = (int)UnitRank.Person, Position = Around(s.Home, rng) });
            double chance = 1.0 - System.Math.Pow(1.0 - GameBalance.WorkPromotionPerHour, hours);
            if (!s.Empire) foreach (UnitState u in s.Subjects) if (u.Rank == (int)UnitRank.Person && rng.NextDouble() < chance) u.Rank = (int)UnitRank.Warrior;
            // Training, as a real one's (NpcKingdom.PromoteWorkers)
            int hall = Mathf.Clamp(s.Hall, 0, GameBalance.KingdomKnightTraining.Length - 1);
            double knight = 1.0 - System.Math.Pow(1.0 - GameBalance.KingdomKnightTraining[hall], hours), general = 1.0 - System.Math.Pow(1.0 - GameBalance.KingdomGeneralTraining[hall], hours);
            foreach (UnitState u in s.Subjects)
            {
                if (u.Rank == (int)UnitRank.Warrior && rng.NextDouble() < knight) u.Rank = (int)UnitRank.Knight;
                else if (u.Rank == (int)UnitRank.Knight && rng.NextDouble() < general) u.Rank = (int)UnitRank.General;
            }
            if (!s.Free && (economy.HallLevel == 0 || s.Subjects.Count >= economy.Cap - GameBalance.KingdomHallRoom) && economy.TryUpgrade())
                s.HallHealth = GameBalance.TownHallHealth[economy.HallLevel];
            s.Wood = economy.Wood; s.Ore = economy.Ore; s.Hall = economy.HallLevel; s.Births = economy.BirthCarry;
        }

        /// <summary>The strongest one left reigns (§7.2); nobody left: the kingdom is gone.</summary>
        private static void Crown(KingdomState s)
        {
            if (s.Subjects.Count == 0) { s.Defeated = true; return; }
            UnitState best = s.Subjects[0];
            foreach (UnitState u in s.Subjects) if (u.Rank > best.Rank || (u.Rank == best.Rank && u.BattleXp > best.BattleXp)) best = u;
            s.Subjects.Remove(best);
            s.King = best;
            s.HasKing = true;
            s.Hero.Id = default; // another person
            s.KingClass = Random.Range((int)HeroClass.Tank, (int)HeroClass.Dd + 1);
            s.KingTotalXp = 0;
            s.Interregnum = 0f;
            s.EnsureIds();
        }
    }
}
