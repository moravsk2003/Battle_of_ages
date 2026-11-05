using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;
using BattleOfAges.UI;
using BattleOfAges.Utils;
using Random = UnityEngine.Random;

namespace BattleOfAges.AI
{
    /// <summary>
    /// An NPC king (docs/GAME_DESIGN.md §12, M0): a hero with its people around a hall, on the same rules as the player.
    /// Its people work at home (KingdomEconomy), are born under the hall's cap and become warriors from work and fights;
    /// the king raises the hall when it can, sends a raid when its army is stronger than what guards the player's base,
    /// and everyone at home fights an enemy that comes close. When the king falls: an interregnum, half the people
    /// desert, then the strongest one left is the new king (level 1, a random class) - as for the player (§7.2). The
    /// kingdom falls when its hall is destroyed or nobody is left (§7.3).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcKingdom : MonoBehaviour
    {
        private static readonly List<NpcKingdom> all = new List<NpcKingdom>();
        public static IReadOnlyList<NpcKingdom> All => all;
        /// <summary>A kingdom fell (its hall is destroyed or nobody is left).</summary>
        public static event Action<NpcKingdom> Defeated;
        /// <summary>A raid set off: the kingdom and its target.</summary>
        public static event Action<NpcKingdom, HealthComponent> RaidStarted;

        public string KingdomName = "Королівство";
        /// <summary>
        /// Its side in the unit grid: an NPC kingdom is its own side (UnitSpatialGrid.TeamKingdomFirst..), at war with the
        /// player and every other kingdom (docs/GAME_DESIGN.md §12.1); a kingdom on the player's side (the session sim) is TeamPlayer.
        /// </summary>
        public int Team { get; private set; } = UnitSpatialGrid.TeamKingdomFirst;
        public bool AgainstPlayer => Team != UnitSpatialGrid.TeamPlayer;
        /// <summary>A free hero's band (§7.4): it camps, it grows to a camp's cap, it builds no hall and sends no raids.</summary>
        public bool Free { get; private set; }
        private static int nextTeam = UnitSpatialGrid.TeamKingdomFirst;
        /// <summary>A side of its own for a new NPC kingdom.</summary>
        private static int NewSide() => nextTeam++;
        public readonly KingdomEconomy Economy = new KingdomEconomy();
        public HostileMobAI King { get; private set; }
        public bool IsDefeated { get; private set; }
        public bool Raiding => raiders.Count > 0 || arrivingArmy.Count > 0;
        public float InterregnumLeft { get; private set; }
        public bool InInterregnum => InterregnumLeft > 0f;
        /// <summary>Game hours since the kingdom was founded.</summary>
        public float Age { get; set; }
        /// <summary>Age of its first raid: kings do not all set off at once (KingdomFirstRaidHours .. twice that).</summary>
        public float FirstRaidAt { get; set; } = GameBalance.KingdomFirstRaidHours;
        /// <summary>Age when the last raid came home (the next waits KingdomRaidCooldownHours).</summary>
        public float LastRaidEnd { get; set; } = float.NegativeInfinity;
        public HealthComponent Hall { get; private set; }
        public Vector3 Home => transform.position;

        private readonly List<HostileMobAI> subjects = new List<HostileMobAI>();
        private readonly List<HostileMobAI> raiders = new List<HostileMobAI>();
        public IReadOnlyList<HostileMobAI> Subjects => subjects;
        public IReadOnlyList<HostileMobAI> Raiders => raiders;
        /// <summary>People (the king not counted): what the hall's cap limits.</summary>
        public int Population { get { Prune(); return subjects.Count; } }

        private int raidSize;
        private HealthComponent raidTarget;
        private float hourClock, promotionClock, secondClock;
        private GameObject hallModel;
        private int hallGridId = -1;
        private static readonly int[] buffer = new int[64];

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() => all.Remove(this);
        private void OnDestroy() { if (hallGridId >= 0) UnitSpatialGrid.Unregister(hallGridId); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { all.Clear(); nextTeam = UnitSpatialGrid.TeamKingdomFirst; Defeated = null; RaidStarted = null; }

        // ------------------------------------------------------------------ founding

        /// <summary>A new kingdom here: a king (a general hero, like the player) and its starting people and wood, no hall.</summary>
        public static NpcKingdom Found(Vector3 centre, string name, int people = -1, int wood = -1, HeroClass kingClass = HeroClass.None, int team = -1, bool free = false)
        {
            // On level ground near the spot, not on a hillside or a ridge (the most level one about if there is none)
            Building.BuildSite.FindLevel(centre, 7f, GameBalance.KingdomSiteSearchRadius, GameBalance.KingdomSiteMaxRise, out centre);
            var root = new GameObject("NpcKingdom_" + name);
            root.transform.position = centre;
            NpcKingdom k = root.AddComponent<NpcKingdom>();
            if (!all.Contains(k)) all.Add(k); // (OnEnable does not run in edit mode)
            k.KingdomName = name;
            k.Free = free;
            k.Team = team >= 0 ? team : NewSide();
            k.FirstRaidAt = GameBalance.KingdomFirstRaidHours * Random.Range(1f, 2f);
            k.Economy.Wood = wood >= 0 ? wood : GameBalance.KingdomStartWood;
            k.BuildHall();
            if (Application.isPlaying && !free) k.Town = KingdomTown.Attach(k);
            k.Crown(k.AddSubject(UnitRank.General, k.Spot()), kingClass == HeroClass.None ? RandomClass() : kingClass);
            for (int i = 0, n = people >= 0 ? people : GameBalance.KingdomStartPeople; i < n; i++) k.AddSubject(UnitRank.Person, k.Spot());
            return k;
        }

        public static HeroClass RandomClass() => (HeroClass)Random.Range((int)HeroClass.Tank, (int)HeroClass.Dd + 1);

        /// <summary>A place near the hall for a unit.</summary>
        public Vector3 Spot()
        {
            Vector2 r = Random.insideUnitCircle * 10f;
            Vector3 p = Home + new Vector3(r.x, 0f, r.y) + (r.sqrMagnitude < 25f ? new Vector3(r.x, 0f, r.y).normalized * 5f : Vector3.zero);
            p.y = RtsUnit.GetTerrainHeight(p);
            return p;
        }

        /// <summary>The kit of a unit of this rank: people an axe, soldiers a sword and armour from the kingdom's smiths.</summary>
        public Item[] KitFor(UnitRank rank) => IsEmpire ? UnitStats.EmpireKit(rank) : UnitStats.Kit(rank, Economy.GearLevel);

        /// <summary>The Central Empire of this world (§9.1.1): everyone armed by rank, no one works, the hall rises for nothing; its emperor's death ends it and opens an epoch.</summary>
        public bool IsEmpire { get; private set; }

        /// <summary>The buildings round its hall (not a free hero's band).</summary>
        public KingdomTown Town { get; private set; }

        /// <summary>Its side, set directly (the calibration's two armies).</summary>
        public int TeamForTests { set => Team = value; }

        private int warriorsMade;

        private static HumanoidModelBuilder.HumanType ModelFor(UnitRank rank) => RankLooks.ModelFor(rank);

        /// <summary>A new unit of the kingdom at this place.</summary>
        public HostileMobAI AddSubject(UnitRank rank, Vector3 at, bool withModel = true)
        {
            var go = new GameObject($"{KingdomName}_{rank}_{subjects.Count + 1}");
            go.transform.position = at;
            CharacterController cc = go.AddComponent<CharacterController>();
            cc.height = 2f; cc.radius = 0.5f; cc.center = new Vector3(0f, 1f, 0f);
            go.AddComponent<HealthComponent>();
            go.AddComponent<CombatUnit>();
            HostileMobAI mob = go.AddComponent<HostileMobAI>();
            Item[] kit = KitFor(rank);
            if (rank == UnitRank.Warrior && GameBalance.KingdomArcherEvery > 0 && ++warriorsMade % GameBalance.KingdomArcherEvery == 0) kit = UnitStats.ArcherKit(kit);
            mob.InitializeAsSubject(rank, kit);
            mob.Team = Team;
            mob.Kingdom = this;
            mob.SetGuardOrigin(Home);
            if (withModel && Application.isPlaying)
            {
                GameObject visual = HumanoidModelBuilder.CreateHumanoidModel(ModelFor(rank), "VisualModel");
                visual.transform.SetParent(go.transform, false);
                go.AddComponent<RankModel>(); // a promotion shows
            }
            UnitProgress p = go.GetComponent<UnitProgress>();
            if (p != null) p.Promoted += OnPromoted;
            subjects.Add(mob);
            return mob;
        }

        private void OnPromoted(UnitProgress p)
        {
            // A new soldier gets its kit from the kingdom's smiths
            Equipment gear = p.GetComponent<Equipment>();
            if (gear == null || p.Rank == UnitRank.Person) return;
            Item[] kit = gear.HasBow ? UnitStats.ArcherKit(KitFor(p.Rank)) : KitFor(p.Rank); // an archer stays one
            foreach (Item it in kit) if (!it.IsEmpty) gear.Equip(it);
        }

        /// <summary>Makes this unit the king: a hero of its rank (x2), level 1, this class.</summary>
        public void Crown(HostileMobAI unit, HeroClass cls)
        {
            if (unit == null) return;
            subjects.Remove(unit);
            raiders.Remove(unit);
            UnitProgress rank = unit.GetComponent<UnitProgress>();
            UnitRank r = rank != null ? rank.Rank : UnitRank.Person;
            unit.InitializeAsSubject(r, KitFor(r == UnitRank.Person ? UnitRank.Warrior : r), hero: true);
            HeroProgress hero = unit.GetComponent<HeroProgress>();
            if (hero == null) hero = unit.gameObject.AddComponent<HeroProgress>();
            else hero.ResetForNewHero();
            if (unit.GetComponent<HeroSkills>() == null) unit.gameObject.AddComponent<HeroSkills>();
            hero.ChooseClass(cls);
            Equipment kingGear = unit.GetComponent<Equipment>();
            if (cls == HeroClass.Archer && kingGear != null) kingGear.Equip(new Item(ItemType.Bow, Mathf.Max(1, kingGear.LevelOf(ItemType.Sword))));
            if (cls == HeroClass.Mage && kingGear != null) // the mage strikes only with a staff: the kingdom's smiths make one
                kingGear.Equip(new Item(ItemType.Staff, Mathf.Max(1, kingGear.LevelOf(ItemType.Sword))));
            hero.Changed -= OnHeroChanged;
            hero.Changed += OnHeroChanged;
            HealthComponent hp = unit.Health;
            hp.InitializeHealth(hp.MaxHealth);
            hp.OnDeath -= OnKingDied;
            hp.OnDeath += OnKingDied;
            unit.name = $"{KingdomName}_King";
            King = unit;
        }

        private bool spending;
        private void OnHeroChanged(HeroProgress hero)
        {
            if (spending) return; // (spending a point raises Changed too)
            spending = true;
            HeroAutoPilot.SpendPoints(hero.gameObject); // a level-up: spend it at once
            spending = false;
        }

        // ------------------------------------------------------------------ the hall

        private void BuildHall()
        {
            if (hallModel != null) Kill(hallModel);
            hallModel = new GameObject("Hall");
            hallModel.transform.SetParent(transform, false);
            if (Application.isPlaying)
            {
                GameObject house = WorkerHouseBuilder.Attach(hallModel.transform);
                float s = 0.8f + 0.2f * Economy.HallLevel; // a chief's hut that grows into a great hall
                house.transform.localScale = new Vector3(s, s * 0.9f, s);
            }
            if (Hall == null)
            {
                Hall = gameObject.AddComponent<HealthComponent>();
                Hall.OnDeath += OnHallDestroyed;
                BoxCollider col = gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 3f, 0f);
                col.size = new Vector3(5f, 6f, 5f);
            }
            GetComponent<BoxCollider>().enabled = Economy.HallLevel > 0; // a camp has nothing to storm
            if (Economy.HallLevel > 0)
            {
                float share = Hall.MaxHealth > 0 && Hall.CurrentHealth > 0 ? (float)Hall.CurrentHealth / Hall.MaxHealth : 1f;
                Hall.InitializeHealth(GameBalance.TownHallHealth[Economy.HallLevel]);
                Hall.SetCurrentHealth(Mathf.Max(1, Mathf.RoundToInt(share * Hall.MaxHealth)));
                if (hallGridId < 0) hallGridId = UnitSpatialGrid.Register(this, Hall, Team, 2.5f); // its enemies can storm it
            }
            else Hall.InitializeHealth(1); // a camp: nothing to destroy (it is not in the grid)
        }

        /// <summary>Raises the hall a level when the stores allow it.</summary>
        public bool TryUpgradeHall()
        {
            if (!Economy.TryUpgrade()) return false;
            BuildHall();
            return true;
        }

        public void SetHallLevel(int level)
        {
            Economy.HallLevel = Mathf.Clamp(level, 0, TownHall.MaxLevel);
            BuildHall();
        }

        private void OnHallDestroyed()
        {
            if (Economy.HallLevel <= 0) return;
            Fall("ратушу зруйновано");
        }

        // ------------------------------------------------------------------ the clock

        private void Update() => Advance(WorldClock.HoursOf(Time.deltaTime), Time.deltaTime);

        /// <summary>Runs the kingdom on for this much game time (tests call it directly).</summary>
        public void Advance(float hours, float seconds)
        {
            if (IsDefeated) return;
            Prune();
            Age += hours;

            if (InInterregnum)
            {
                InterregnumLeft -= seconds;
                if (InterregnumLeft <= 0f) EndInterregnum();
            }
            else if (King == null || !King.Health.IsAlive) EndInterregnum(); // no king and no interregnum (an old save): the heir now, or the fall

            if (!InInterregnum && King != null && King.Health.IsAlive) HeroAutoPilot.UseSkills(King.Combat, King.Health, King.CurrentCombatTarget);

            int born = Economy.Tick(hours, IsEmpire ? 0 : Workers(), subjects.Count);
            for (int i = 0; i < born; i++) AddSubject(UnitRank.Person, Spot());

            promotionClock += hours;
            while (promotionClock >= 1f)
            {
                promotionClock -= 1f;
                PromoteWorkers();
            }

            secondClock += seconds;
            if (secondClock >= 1f)
            {
                secondClock = 0f;
                Defend();
                if (Raiding) LeadRaid();
            }

            hourClock += hours;
            if (hourClock >= 1f)
            {
                hourClock = 0f;
                Think();
            }
        }

        /// <summary>The once-a-game-hour decisions: raise the hall, send a raid.</summary>
        public void Think()
        {
            if (IsDefeated) return;
            if (Free) return; // a free hero camps: no hall, no raids
            if (Economy.HallLevel == 0 || Population >= Economy.Cap - GameBalance.KingdomHallRoom) TryUpgradeHall(); // only when it needs the room
            if (!Raiding && !InInterregnum && Age >= FirstRaidAt && !UnderRaid() && Age >= LastRaidEnd + GameBalance.KingdomRaidCooldownHours) ConsiderRaid();
        }

        private bool AtHome(HostileMobAI m) => (m.transform.position - Home).sqrMagnitude <= GameBalance.KingdomHomeRadius * GameBalance.KingdomHomeRadius;

        /// <summary>People at home and not fighting: they work.</summary>
        public int Workers()
        {
            int n = 0;
            foreach (HostileMobAI m in subjects)
                if (m.CurrentCombatTarget == null && !raiders.Contains(m) && AtHome(m)) n++;
            return n;
        }

        private void PromoteWorkers()
        {
            foreach (HostileMobAI m in subjects.ToArray())
            {
                if (m.CurrentCombatTarget != null || raiders.Contains(m) || !AtHome(m)) continue;
                UnitProgress p = m.GetComponent<UnitProgress>();
                if (p != null && p.Rank == UnitRank.Person && !IsEmpire && Random.value < GameBalance.WorkPromotionPerHour) p.Promote();
                else if (p != null && p.Rank == UnitRank.Warrior && Random.value < GameBalance.KingdomKnightTraining[Economy.HallLevel]) p.Promote();
                else if (p != null && p.Rank == UnitRank.Knight && Random.value < GameBalance.KingdomGeneralTraining[Economy.HallLevel]) p.Promote();
            }
        }

        private void Prune()
        {
            subjects.RemoveAll(m => m == null || m.Health == null || !m.Health.IsAlive);
            raiders.RemoveAll(m => m == null || m.Health == null || !m.Health.IsAlive);
        }

        // ------------------------------------------------------------------ war

        /// <summary>Strength of these units in a fight (KingdomEconomy.Power).</summary>
        public static float PowerOf(IEnumerable<HealthComponent> units)
        {
            float hp = 0f, dps = 0f;
            foreach (HealthComponent h in units)
            {
                if (h == null || !h.IsAlive) continue;
                CombatUnit c = h.GetComponent<CombatUnit>();
                if (c == null) continue;
                hp += h.CurrentHealth;
                dps += c.AttackDamage / Mathf.Max(0.1f, c.AttackCooldown);
            }
            return KingdomEconomy.Power(hp, dps);
        }

        /// <summary>What would go on a raid: everyone but a garrison of the weakest, and the king.</summary>
        public List<HostileMobAI> RaidParty()
        {
            Prune();
            var everyone = new List<HostileMobAI>(subjects);
            int stay = Mathf.RoundToInt(everyone.Count * GameBalance.KingdomGarrisonShare);
            everyone.Sort((a, b) => b.Health.MaxHealth.CompareTo(a.Health.MaxHealth)); // the weakest stay
            var party = everyone.GetRange(0, everyone.Count - stay);
            if (King != null && King.Health.IsAlive && !InInterregnum) party.Add(King);
            return party;
        }

        /// <summary>The player's hero, if alive.</summary>
        private static HealthComponent PlayerHero()
        {
            GameObject player = Managers.GameContext.Player;
            HealthComponent p = player != null ? player.GetComponent<HealthComponent>() : null;
            return p != null && p.IsAlive ? p : null;
        }

        /// <summary>
        /// What a raid could go after, nearest first: every enemy kingdom's hall (its king or people while it has no hall),
        /// and against the player the player's hall, or else the player's hero.
        /// </summary>
        public List<HealthComponent> RaidTargets()
        {
            var targets = new List<HealthComponent>();
            void Consider(HealthComponent h) { if (h != null && h.IsAlive) targets.Add(h); }
            foreach (NpcKingdom k in all)
            {
                if (k == null || k == this || k.IsDefeated || !UnitSpatialGrid.Hostile(k.Team, Team)) continue;
                if (k.Economy.HallLevel > 0) Consider(k.Hall);
                else if (k.King != null && k.King.Health.IsAlive) Consider(k.King.Health);
                else if (k.Population > 0) Consider(k.subjects[0].Health);
            }
            if (AgainstPlayer)
            {
                TownHall hall = TownHall.Current;
                HealthComponent h = hall != null ? hall.GetComponent<HealthComponent>() : null;
                Consider(h != null && h.IsAlive ? h : PlayerHero());
                // In a game with others: its other players' heroes too (the bodies here stand for them)
                if (Net.NetSession.IsHost) Net.NetSession.Current.Remote.ForEachHero(body => Consider(body.Health));
            }
            // Not one that another king is already raiding: the player sits in the middle, the nearest for every king, and
            // the kings took turns on it (the session sim: 1 against 3 again)
            targets.RemoveAll(TakenByAnotherRaid);
            // Nearest first, the player as if half as far: kings went on fighting each other and never came (FieldReportProbe)
            float Way(HealthComponent t)
            {
                float d = (t.transform.position - Home).magnitude;
                return t.GetComponent<TownHall>() != null || t.gameObject == Managers.GameContext.Player || t.GetComponent<Net.RemoteBody>() != null ? d * GameBalance.KingdomPlayerTargetFactor : d;
            }
            targets.Sort((a, b) => Way(a).CompareTo(Way(b)));
            return targets;
        }

        private bool TakenByAnotherRaid(HealthComponent target)
        {
            foreach (NpcKingdom k in all)
            {
                if (k == null || k == this || !k.Raiding || k.raidTarget == null) continue;
                if ((k.raidTarget.transform.position - target.transform.position).sqrMagnitude <= GameBalance.KingdomGuardRadius * GameBalance.KingdomGuardRadius) return true;
            }
            return false;
        }

        /// <summary>What guards this place against this kingdom: its enemies' units (and the player's hero) near it.</summary>
        public List<HealthComponent> GuardsOf(Vector3 place)
        {
            var guards = new List<HealthComponent>();
            int n = UnitSpatialGrid.QueryEnemies(place, GameBalance.KingdomGuardRadius, Team, buffer);
            for (int i = 0; i < n; i++)
            {
                HealthComponent h = UnitSpatialGrid.Health(buffer[i]);
                if (h != null && h.IsAlive && !guards.Contains(h)) guards.Add(h);
            }
            HealthComponent p = AgainstPlayer ? PlayerHero() : null;
            if (p != null && !guards.Contains(p) && (p.transform.position - place).sqrMagnitude <= GameBalance.KingdomGuardRadius * GameBalance.KingdomGuardRadius)
                guards.Add(p);
            return guards;
        }

        /// <summary>
        /// Sends a raid at this target, or else at the nearest enemy its party is strong enough for (KingdomRaidPowerRatio
        /// times its guards); returns whether it went.
        /// </summary>
        public bool ConsiderRaid(HealthComponent target = null)
        {
            List<HostileMobAI> party = RaidParty();
            if (party.Count == 0) return false;
            float ours = PowerOf(party.ConvertAll(m => m.Health));
            foreach (HealthComponent t in target != null ? new List<HealthComponent> { target } : RaidTargets())
            {
                if (ours < GameBalance.KingdomRaidPowerRatio * PowerOf(GuardsOf(t.transform.position))) continue;
                StartRaid(party, t);
                return true;
            }
            return false;
        }

        public void StartRaid(List<HostileMobAI> party, HealthComponent target)
        {
            raiders.Clear();
            raiders.AddRange(party);
            raidSize = party.Count;
            raidTarget = target;
            raidAtPlayer = target.GetComponent<TownHall>() != null || target.gameObject == Managers.GameContext.Player;
            foreach (HostileMobAI m in raiders) m.OrderAttack(target);
            RaidStarted?.Invoke(this, target);
            StrategyHud.Notify($"{KingdomName} іде в набіг{Whom(target)}: {raidSize} бійців!");
        }

        /// <summary>A raid that turned real finds its target among those in the scene (it goes home if there is none), and everyone sets on it.</summary>
        private void ResolveRaidTarget()
        {
            HealthComponent target = null;
            if (raidAtPlayer)
            {
                TownHall hall = TownHall.Current;
                HealthComponent h = hall != null ? hall.GetComponent<HealthComponent>() : null;
                target = h != null && h.IsAlive ? h : PlayerHero();
            }
            else
                foreach (NpcKingdom k in all)
                {
                    if (k == null || k == this || k.IsDefeated || k.stateIds.Id != raidPending) continue;
                    target = k.Economy.HallLevel > 0 ? k.Hall : k.King != null ? k.King.Health : null;
                }
            raidPending = default;
            if (target == null || !target.IsAlive) { raidAtPlayer = false; EndRaid(retreat: false); return; }
            raidTarget = target;
            foreach (HostileMobAI m in raiders) if (m != null) m.OrderAttack(target);
            if (raidAtPlayer) StrategyHud.Notify($"{KingdomName} іде в набіг на вас: {raidSize} бійців!");
        }

        /// <summary>Who a raid goes at, for the message: the same words looked as if every raid were coming for the player.</summary>
        private static string Whom(HealthComponent target)
        {
            if (target == null) return "";
            if (target.GetComponent<TownHall>() != null || target.gameObject == Managers.GameContext.Player) return " на вас";
            foreach (NpcKingdom k in all)
            {
                if (k == null) continue;
                if (k.Hall == target || (k.King != null && k.King.Health == target)) return $" на «{k.KingdomName}»";
                foreach (HostileMobAI m in k.subjects) if (m != null && m.Health == target) return $" на «{k.KingdomName}»";
            }
            return "";
        }

        /// <summary>Once a second on a raid: turn back after heavy losses, else keep every raider fighting something.</summary>
        private void LeadRaid()
        {
            if (arrivingArmy.Count > 0) return; // still coming into the scene
            if (raidTarget == null && (raidAtPlayer || !raidPending.IsNone)) ResolveRaidTarget();
            if (raidSize > 0 && raiders.Count <= raidSize * (1f - GameBalance.KingdomRaidRetreatLosses)) { EndRaid(retreat: true); return; }
            // On the march the raid keeps together at the pace of its slowest one; near the enemy everyone runs
            bool marching = true;
            float slowest = float.MaxValue;
            foreach (HostileMobAI m in raiders)
            {
                slowest = Mathf.Min(slowest, m.Combat != null ? m.Combat.MoveSpeed : slowest);
                if (NearestEnemyUnit(m.transform.position, GameBalance.KingdomRaidSearchRadius) != null ||
                    (raidTarget != null && (raidTarget.transform.position - m.transform.position).sqrMagnitude <= GameBalance.KingdomRaidSearchRadius * GameBalance.KingdomRaidSearchRadius))
                    marching = false;
            }
            // Still on the way and someone is coming for the home: back to defend it (the session sim: a king out on a raid
            // lost its empty hall to another king)
            if (marching && UnderRaid()) { EndRaid(retreat: true); return; }
            foreach (HostileMobAI m in raiders) m.SpeedCap = marching ? slowest : float.MaxValue;
            bool anyFighting = false;
            foreach (HostileMobAI m in raiders)
            {
                m.EngageNear(GameBalance.RaidEngageRadius);
                HealthComponent t = m.CurrentCombatTarget;
                if (t != null && t.IsAlive) { anyFighting = true; continue; }
                HealthComponent next = NearestEnemy(m.transform.position, GameBalance.KingdomRaidSearchRadius);
                if (next == null && raidTarget != null && raidTarget.IsAlive) next = raidTarget;
                if (next != null) { m.OrderAttack(next); anyFighting = true; }
            }
            if (!anyFighting) EndRaid(retreat: false);
        }

        /// <summary>An enemy raid is on its way to this kingdom: the king keeps everyone home.</summary>
        public bool UnderRaid()
        {
            foreach (NpcKingdom k in all)
            {
                if (k == null || k == this || !k.Raiding || k.raidTarget == null) continue;
                if (k.raidTarget == Hall || (King != null && k.raidTarget == King.Health)) return true;
                foreach (HostileMobAI m in subjects) if (k.raidTarget == m.Health) return true;
            }
            return false;
        }

        /// <summary>The raid is over: everyone goes home.</summary>
        public void EndRaid(bool retreat)
        {
            foreach (HostileMobAI m in raiders)
            {
                if (m == null || !m.Health.IsAlive) continue;
                m.SetTarget(null);
                m.SpeedCap = float.MaxValue;
                m.ChangeState(AIStateType.Retreat); // walks home and gets its breath back
            }
            if (retreat) StrategyHud.Notify($"{KingdomName} відступає з набігу");
            LastRaidEnd = Age;
            raiders.Clear();
            raidSize = 0;
            raidTarget = null;
            raidPending = default;
            raidAtPlayer = false;
        }

        /// <summary>The nearest enemy unit, enemy hall (and against the player: the player's hero and buildings) within this radius.</summary>
        public HealthComponent NearestEnemy(Vector3 from, float radius)
        {
            HealthComponent unit = NearestEnemyUnit(from, radius);
            if (unit != null) return unit;
            if (!AgainstPlayer) return null;
            HealthComponent best = null;
            float bestD = radius * radius;
            foreach (BuildingNode b in BuildingNode.Active)
            {
                if (b == null) continue;
                float d = (b.transform.position - from).sqrMagnitude;
                if (d > bestD) continue;
                HealthComponent h = b.GetComponent<HealthComponent>();
                if (h != null && h.IsAlive) { best = h; bestD = d; }
            }
            return best;
        }

        /// <summary>An enemy close to home: everyone at home goes for it.</summary>
        private void Defend()
        {
            HealthComponent enemy = NearestEnemyUnit(Home, GameBalance.KingdomDefendRadius);
            if (enemy == null) return;
            foreach (HostileMobAI m in subjects)
                if (!raiders.Contains(m) && m.CurrentCombatTarget == null && m.CurrentStateType != AIStateType.Retreat) m.OrderAttack(enemy);
            if (King != null && King.Health.IsAlive && !raiders.Contains(King) && King.CurrentCombatTarget == null) King.OrderAttack(enemy);
        }

        private HealthComponent NearestEnemyUnit(Vector3 from, float radius)
        {
            int id = UnitSpatialGrid.FindNearestEnemy(from, radius, Team);
            if (id >= 0 && UnitSpatialGrid.Health(id) != null && UnitSpatialGrid.Health(id).IsAlive) return UnitSpatialGrid.Health(id);
            HealthComponent p = AgainstPlayer ? PlayerHero() : null;
            return p != null && (p.transform.position - from).sqrMagnitude <= radius * radius ? p : null;
        }

        // ------------------------------------------------------------------ a new game and saves

        private static readonly string[] Names = { "Вовчий край", "Залізні пагорби", "Ясенова долина", "Кам'яний брід", "Сива рівнина", "Орлине гніздо" };

        /// <summary>The names of a campaign's kingdoms and free heroes' bands (the first six are M0's).</summary>
        public static readonly string[] CampaignNames =
        {
            "Вовчий край", "Залізні пагорби", "Ясенова долина", "Кам'яний брід", "Сива рівнина", "Орлине гніздо",
            "Дубовий гай", "Червоний яр", "Мідна гора", "Тихі заводі", "Соколиний бір", "Вітряний кряж",
            "Срібний потік", "Ведмежий кут", "Золоте поле", "Туманна гать", "Кленова балка", "Високий вал",
            "Березовий луг", "Чорна скеля", "Лисячі нори", "Білий камінь", "Зелена діброва", "Старий шлях",
            "Грозова вершина", "Медова пасіка", "Кремінний брід", "Озерний край", "Північна застава", "Сонячний схил"
        };

        /// <summary>The NPC kings of a new game: KingdomCount of them on a ring KingdomDistance around this place.</summary>
        public static void FoundAround(Vector3 centre, int count = -1, float distance = -1f)
        {
            if (count < 0) count = GameBalance.KingdomCount;
            if (distance < 0f) distance = GameBalance.KingdomDistance;
            float start = Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                float a = (start + i * 360f / Mathf.Max(1, count) + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
                float d = distance * Random.Range(0.85f, 1.15f);
                Found(centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d, Names[i % Names.Length]);
            }
        }

        private static void Kill(GameObject go) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }

        /// <summary>Takes the kingdom out of the scene without a fall (KingdomRealm folds it into its state): its people, king and hall go.</summary>
        public void Dissolve()
        {
            foreach (HostileMobAI m in subjects) if (m != null) Kill(m.gameObject);
            if (King != null) Kill(King.gameObject);
            all.Remove(this);
            Kill(gameObject);
        }

        public static void RemoveAll()
        {
            foreach (NpcKingdom k in new List<NpcKingdom>(all))
            {
                if (k == null) continue;
                foreach (HostileMobAI m in k.subjects) if (m != null) Kill(m.gameObject);
                if (k.King != null) Kill(k.King.gameObject);
                Kill(k.gameObject);
            }
            all.Clear();
            nextTeam = UnitSpatialGrid.TeamKingdomFirst;
        }

        private static SaveLoad.UnitState SaveOf(HostileMobAI m)
        {
            UnitProgress p = m.GetComponent<UnitProgress>();
            return new SaveLoad.UnitState { Rank = p != null ? (int)p.Rank : 0, BattleXp = p != null ? p.BattleXp : 0, Health = m.Health.CurrentHealth, Position = m.transform.position };
        }

        /// <summary>Its number in the world's state.</summary>
        public SaveLoad.WorldId StateId { get { EnsureStateIds(); return stateIds.Id; } }

        private void EnsureStateIds()
        {
            if (stateIds.Id.IsNone) stateIds.Id = SaveLoad.WorldIds.New();
        }

        /// <summary>The kingdom its raid goes for (none: no raid, or at the player).</summary>
        public SaveLoad.WorldId RaidTargetId
        {
            get
            {
                if (!raidPending.IsNone) return raidPending;
                NpcKingdom owner = raidTarget != null ? OwnerOf(raidTarget) : null;
                return owner != null ? owner.StateId : default;
            }
        }

        private static NpcKingdom OwnerOf(HealthComponent target)
        {
            foreach (NpcKingdom k in all)
            {
                if (k == null) continue;
                if (k.Hall == target || (k.King != null && k.King.Health == target)) return k;
                foreach (HostileMobAI m in k.subjects) if (m != null && m.Health == target) return k;
            }
            return null;
        }

        /// <summary>
        /// It can turn back into data: a raid only while no one of it is in sight of the player or at its fight. A battle that
        /// has begun is never folded up (docs/GAME_DESIGN.md §6.3).
        /// </summary>
        public bool CanFold(Vector3 playerHome, Vector3 hero)
        {
            if (!Raiding) return true;
            if (arrivingArmy.Count > 0) return false;
            float reach = GameBalance.DematerializeRadius;
            foreach (HostileMobAI m in raiders)
            {
                if (m == null) continue;
                Vector3 p = m.transform.position;
                if (KingdomRealm.AnyPlayerWithin(p, reach) || FlatDistance(p, playerHome) < reach) return false;
                if (NearestEnemyUnit(p, GameBalance.KingdomRaidSearchRadius) != null) return false;
                if (raidTarget != null && FlatDistance(p, raidTarget.transform.position) <= GameBalance.KingdomRaidSearchRadius) return false;
            }
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        public SaveLoad.KingdomState ToState()
        {
            Prune();
            var s = new SaveLoad.KingdomState
            {
                Id = stateIds.Id, Name = KingdomName, Home = Home, Hall = Economy.HallLevel, HallHealth = Hall != null ? Hall.CurrentHealth : 0,
                Wood = Economy.Wood, Ore = Economy.Ore, Births = Economy.BirthCarry, Age = Age, Interregnum = InterregnumLeft, Defeated = IsDefeated,
                Free = Free, Team = Team, Empire = IsEmpire, LastRaidEnd = float.IsInfinity(LastRaidEnd) ? -1000f : LastRaidEnd
            };
            s.Hero.Id = stateIds.Hero;
            s.Capital.Id = stateIds.Settlement;
            s.Capital.Garrison.Id = stateIds.Garrison;
            s.EnsureIds();
            stateIds = (s.Id, s.Hero.Id, s.Capital.Id, s.Capital.Garrison.Id); // (a kingdom founded here gets its numbers the first time)
            if (IsDefeated) return s;
            foreach (HostileMobAI m in subjects) if (!raiders.Contains(m)) s.Subjects.Add(SaveOf(m));
            s.Subjects.AddRange(arriving); // not in the scene yet: still its people
            if (Raiding) s.Armies.Add(RaidAsArmy());
            s.EnsureIds();
            if (King != null && King.Health.IsAlive)
            {
                s.HasKing = true;
                s.King = SaveOf(King);
                HeroProgress hero = King.GetComponent<HeroProgress>();
                if (hero != null)
                {
                    s.KingClass = (int)hero.Class;
                    s.KingTotalXp = hero.Xp;
                    for (int l = 1; l < hero.Level; l++) s.KingTotalXp += HeroProgress.XpForLevelUp(l);
                }
            }
            return s;
        }

        /// <summary>The raid under way as data: who, where they have got to, at whom.</summary>
        private SaveLoad.ArmyState RaidAsArmy()
        {
            var army = new SaveLoad.ArmyState { StartCount = raidSize, WithHero = King != null && raiders.Contains(King), TargetKingdomId = RaidTargetId, TargetsPlayer = raidAtPlayer };
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (HostileMobAI m in raiders)
            {
                if (m == null) continue;
                sum += m.transform.position;
                n++;
                if (m != King) army.Units.Add(SaveOf(m));
            }
            foreach (SaveLoad.UnitState u in arrivingArmy) { sum += u.Position; n++; army.Units.Add(u); }
            army.Position = n > 0 ? sum / n : Home;
            if (raidTarget != null && !raidAtPlayer && army.TargetKingdomId.IsNone) army.Returning = true; // a band of its own, no kingdom: home
            return army;
        }

        /// <summary>A kingdom back from a save (its king grows again to the saved level, spending the points its usual way).</summary>
        public static NpcKingdom FromState(SaveLoad.KingdomState s, bool gradual = false)
        {
            var root = new GameObject("NpcKingdom_" + s.Name);
            root.transform.position = s.Home;
            NpcKingdom k = root.AddComponent<NpcKingdom>();
            if (!all.Contains(k)) all.Add(k);
            k.KingdomName = s.Name;
            s.EnsureIds();
            k.stateIds = (s.Id, s.Hero.Id, s.Capital.Id, s.Capital.Garrison.Id);
            k.Free = s.Free;
            k.IsEmpire = s.Empire;
            k.Economy.Free = s.Empire;
            k.Team = s.Team >= UnitSpatialGrid.TeamKingdomFirst ? s.Team : NewSide();
            if (k.Team >= nextTeam) nextTeam = k.Team + 1;
            k.Economy.Wood = s.Wood; k.Economy.Ore = s.Ore; k.Economy.BirthCarry = s.Births; k.Economy.HallLevel = s.Hall;
            k.Age = s.Age;
            k.LastRaidEnd = s.LastRaidEnd;
            // Kings do not all set off at once, as a new one (a state does not keep it: the same hour for the same name)
            k.FirstRaidAt = GameBalance.KingdomFirstRaidHours * (1f + Mathf.Repeat(KingdomTown.Seed(s.Name) * 0.618034f, 1f));
            k.BuildHall();
            if (s.Hall > 0 && s.HallHealth > 0) k.Hall.SetCurrentHealth(s.HallHealth);
            if (Application.isPlaying && !s.Free) k.Town = KingdomTown.Attach(k);
            if (s.Defeated) { k.IsDefeated = true; return k; }
            k.RaiseArmies(s, gradual);
            if (gradual) k.arriving.AddRange(s.Subjects); // a few a frame (ArriveMore): a grown kingdom was a hitch of hundreds of ms
            else foreach (SaveLoad.UnitState u in s.Subjects) Restore(k.AddSubject((UnitRank)u.Rank, u.Position), u);
            if (s.HasKing)
            {
                bool leadsArmy = s.Armies.Count > 0 && s.Armies[0].WithHero;
                HostileMobAI king = k.AddSubject((UnitRank)s.King.Rank, leadsArmy ? s.Armies[0].Position : s.King.Position);
                k.Crown(king, (HeroClass)s.KingClass);
                if (leadsArmy && !s.Armies[0].Returning) k.raiders.Add(king);
                HeroProgress hero = king.GetComponent<HeroProgress>();
                hero.AddXp(s.KingTotalXp);
                HeroAutoPilot.SpendPoints(king.gameObject);
                if (s.King.Health > 0) king.Health.SetCurrentHealth(Mathf.Min(s.King.Health, king.Health.MaxHealth));
            }
            else if (s.Interregnum > 0f) k.InterregnumLeft = s.Interregnum;
            return k;
        }

        private readonly List<SaveLoad.UnitState> arriving = new List<SaveLoad.UnitState>();
        /// <summary>Its army's people still to come into the scene, where the army has got to.</summary>
        private readonly List<SaveLoad.UnitState> arrivingArmy = new List<SaveLoad.UnitState>();
        /// <summary>A raid that turned real: the kingdom it goes for, until that one is there too; whether it goes for the player; whether it is on its way home.</summary>
        private SaveLoad.WorldId raidPending;
        private bool raidAtPlayer, armyReturning;
        /// <summary>The numbers of its state (SaveLoad.KingdomState): they stay through turning far and real.</summary>
        private (SaveLoad.WorldId Id, SaveLoad.WorldId Hero, SaveLoad.WorldId Settlement, SaveLoad.WorldId Garrison) stateIds;

        /// <summary>People of a kingdom made real gradually, still to come into the scene.</summary>
        public int Arriving => arriving.Count + arrivingArmy.Count;

        /// <summary>Its armies from the far roads turn real where they are (M6 step 4), marching on or going home.</summary>
        private void RaiseArmies(SaveLoad.KingdomState s, bool gradual)
        {
            if (s.Armies.Count == 0) return;
            SaveLoad.ArmyState army = s.Armies[0];
            for (int i = 1; i < s.Armies.Count; i++) s.Subjects.AddRange(s.Armies[i].Units); // (one at a time so far)
            armyReturning = army.Returning;
            raidAtPlayer = army.TargetsPlayer && !army.Returning;
            raidPending = army.Returning ? default : army.TargetKingdomId;
            raidSize = army.StartCount > 0 ? army.StartCount : army.Units.Count;
            foreach (SaveLoad.UnitState u in army.Units)
            {
                Vector2 r = Random.insideUnitCircle * Mathf.Min(3f + Mathf.Sqrt(army.Units.Count), 18f);
                u.Position = army.Position + new Vector3(r.x, 0f, r.y);
                u.Position.y = RtsUnit.GetTerrainHeight(u.Position);
                arrivingArmy.Add(u);
            }
            if (!gradual) ArriveMore(int.MaxValue);
        }

        /// <summary>Brings up to this many of its people into the scene (KingdomRealm, each frame); false when none are left to come.</summary>
        public bool ArriveMore(int max)
        {
            while (max > 0 && arriving.Count > 0)
            {
                SaveLoad.UnitState u = arriving[0];
                arriving.RemoveAt(0);
                Restore(AddSubject((UnitRank)u.Rank, u.Position), u);
                max--;
            }
            while (max > 0 && arrivingArmy.Count > 0)
            {
                max--;
                SaveLoad.UnitState u = arrivingArmy[arrivingArmy.Count - 1];
                arrivingArmy.RemoveAt(arrivingArmy.Count - 1);
                HostileMobAI m = AddSubject((UnitRank)u.Rank, u.Position);
                Restore(m, u);
                if (armyReturning) m.ChangeState(AIStateType.Retreat); // on its way home, from where it had got to
                else raiders.Add(m);
            }
            return Arriving > 0;
        }

        private static void Restore(HostileMobAI m, SaveLoad.UnitState u)
        {
            UnitProgress p = m.GetComponent<UnitProgress>();
            if (p != null) p.AddBattleXp(u.BattleXp);
            if (u.Health > 0) m.Health.SetCurrentHealth(Mathf.Min(u.Health, m.Health.MaxHealth));
        }

        // ------------------------------------------------------------------ the king's death (§7.2) and the fall (§7.3)

        private void OnKingDied()
        {
            if (IsDefeated) return;
            if (IsEmpire)
            {
                // No succession: the empire is its emperor's (§9.1.1, §9.3), and the artifact breaks into a shard where he fell
                if (King != null) ArtifactPickup.Spawn(ArtifactType.CentralEmpire, King.transform.position, shard: true, owner: null);
                Fall("імператор загинув");
                return;
            }
            if (Raiding) EndRaid(retreat: true);
            InterregnumLeft = GameBalance.InterregnumSeconds;
            StrategyHud.Notify($"Король {KingdomName} загинув! Міжцарів'я");
            Desert();
        }

        /// <summary>About half the people leave: they wander off as a band of their own, no longer the kingdom's.</summary>
        private void Desert()
        {
            Prune();
            int leave = Mathf.FloorToInt(subjects.Count * GameBalance.DesertionShare + Random.value * 0.999f * (subjects.Count % 2));
            for (int i = 0; i < leave && subjects.Count > 0; i++)
            {
                HostileMobAI m = subjects[Random.Range(0, subjects.Count)];
                subjects.Remove(m);
                m.Kingdom = null;
                Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(80f, 150f);
                Vector3 camp = m.transform.position + new Vector3(dir.x, 0f, dir.y);
                camp.y = RtsUnit.GetTerrainHeight(camp);
                m.SetGuardOrigin(camp);
                m.name = $"Deserter_{KingdomName}_{i + 1}";
            }
        }

        /// <summary>The strongest one left: the highest rank, then more battle experience, then more HP (§7.2).</summary>
        public HostileMobAI Strongest()
        {
            Prune();
            HostileMobAI best = null;
            foreach (HostileMobAI m in subjects)
            {
                if (best == null || Compare(m, best) > 0) best = m;
            }
            return best;
        }

        private static int Compare(HostileMobAI a, HostileMobAI b)
        {
            UnitProgress pa = a.GetComponent<UnitProgress>(), pb = b.GetComponent<UnitProgress>();
            int c = (pa != null ? (int)pa.Rank : 0).CompareTo(pb != null ? (int)pb.Rank : 0);
            if (c != 0) return c;
            c = (pa != null ? pa.BattleXp : 0).CompareTo(pb != null ? pb.BattleXp : 0);
            return c != 0 ? c : a.Health.CurrentHealth.CompareTo(b.Health.CurrentHealth);
        }

        public void EndInterregnum()
        {
            InterregnumLeft = 0f;
            HostileMobAI heir = Strongest();
            if (heir == null) { Fall("нікого не лишилось"); return; }
            stateIds.Hero = default; // another person: a new number at the next ToState
            Crown(heir, RandomClass());
            StrategyHud.Notify($"{KingdomName}: новий король");
        }

        /// <summary>The kingdom falls: its people and king are a free band now (§7.3).</summary>
        public void Fall(string why)
        {
            if (IsDefeated) return;
            IsDefeated = true;
            if (IsEmpire) CentralEmpire.NpcEmpireFell();
            if (Raiding) EndRaid(retreat: false);
            foreach (HostileMobAI m in subjects) if (m != null) m.Kingdom = null;
            if (King != null) King.Kingdom = null;
            subjects.Clear();
            if (hallGridId >= 0) { UnitSpatialGrid.Unregister(hallGridId); hallGridId = -1; }
            StrategyHud.Notify($"{KingdomName} впало: {why}");
            Defeated?.Invoke(this);
            if (!AgainstPlayer) StrategyHud.Notify("Поразка: королівство гравця впало");
            else if (all.TrueForAll(k => k == null || k.IsDefeated || !k.AgainstPlayer) && !KingdomRealm.AnyStandingFar()) StrategyHud.Notify("Перемога! Усі сусідні королівства впали");
        }
    }
}
