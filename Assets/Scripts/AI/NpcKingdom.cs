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
        /// <summary>Its side in the unit grid: hostile to the player (an NPC king), or the player's (a kingdom on the player's side, the session sim).</summary>
        public int Team { get; private set; } = UnitSpatialGrid.TeamHostile;
        public int EnemyTeam => Team == UnitSpatialGrid.TeamHostile ? UnitSpatialGrid.TeamPlayer : UnitSpatialGrid.TeamHostile;
        public bool AgainstPlayer => Team == UnitSpatialGrid.TeamHostile;
        public readonly KingdomEconomy Economy = new KingdomEconomy();
        public HostileMobAI King { get; private set; }
        public bool IsDefeated { get; private set; }
        public bool Raiding => raiders.Count > 0;
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
        private static void ResetStatics() { all.Clear(); Defeated = null; RaidStarted = null; }

        // ------------------------------------------------------------------ founding

        /// <summary>A new kingdom here: a king (a general hero, like the player) and its starting people and wood, no hall.</summary>
        public static NpcKingdom Found(Vector3 centre, string name, int people = -1, int wood = -1, HeroClass kingClass = HeroClass.None, int team = -1)
        {
            centre.y = RtsUnit.GetTerrainHeight(centre);
            var root = new GameObject("NpcKingdom_" + name);
            root.transform.position = centre;
            NpcKingdom k = root.AddComponent<NpcKingdom>();
            if (!all.Contains(k)) all.Add(k); // (OnEnable does not run in edit mode)
            k.KingdomName = name;
            if (team >= 0) k.Team = team;
            k.FirstRaidAt = GameBalance.KingdomFirstRaidHours * Random.Range(1f, 2f);
            k.Economy.Wood = wood >= 0 ? wood : GameBalance.KingdomStartWood;
            k.BuildHall();
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
        public Item[] KitFor(UnitRank rank)
        {
            if (rank == UnitRank.Person) return new[] { new Item(ItemType.Axe, 1) };
            int l = Economy.GearLevel;
            return new[] { new Item(ItemType.Sword, l), new Item(ItemType.Armor, l), new Item(ItemType.Shield, l) };
        }

        private static HumanoidModelBuilder.HumanType ModelFor(UnitRank rank) => rank switch
        {
            UnitRank.Person => HumanoidModelBuilder.HumanType.Villager,
            UnitRank.Warrior => HumanoidModelBuilder.HumanType.Bandit,
            UnitRank.Knight => HumanoidModelBuilder.HumanType.KnightGuard,
            _ => HumanoidModelBuilder.HumanType.Warlord
        };

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
            mob.InitializeAsSubject(rank, KitFor(rank));
            mob.Team = Team;
            mob.Kingdom = this;
            mob.SetGuardOrigin(Home);
            if (withModel && Application.isPlaying)
            {
                GameObject visual = HumanoidModelBuilder.CreateHumanoidModel(ModelFor(rank), "VisualModel");
                visual.transform.SetParent(go.transform, false);
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
            if (gear != null && p.Rank != UnitRank.Person) foreach (Item it in KitFor(p.Rank)) gear.Equip(it);
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
                Hall.InitializeHealth(GameBalance.KingdomHallHealthPerLevel * Economy.HallLevel);
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

            int born = Economy.Tick(hours, Workers(), subjects.Count);
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
                if (p != null && p.Rank == UnitRank.Person && Random.value < GameBalance.WorkPromotionPerHour) p.Promote();
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
            GameObject player = GameObject.Find("Player");
            HealthComponent p = player != null ? player.GetComponent<HealthComponent>() : null;
            return p != null && p.IsAlive ? p : null;
        }

        /// <summary>
        /// What a raid would go after, the nearest of: an enemy kingdom's hall (its king or people while it has no hall),
        /// and against the player the player's hall, or else the player's hero.
        /// </summary>
        public HealthComponent RaidTarget()
        {
            HealthComponent best = null;
            float bestD = float.MaxValue;
            void Consider(HealthComponent h)
            {
                if (h == null || !h.IsAlive) return;
                float d = (h.transform.position - Home).sqrMagnitude;
                if (d < bestD) { best = h; bestD = d; }
            }
            foreach (NpcKingdom k in all)
            {
                if (k == null || k == this || k.IsDefeated || k.Team != EnemyTeam) continue;
                if (k.Economy.HallLevel > 0) Consider(k.Hall);
                else if (k.King != null && k.King.Health.IsAlive) Consider(k.King.Health);
                else if (k.Population > 0) Consider(k.subjects[0].Health);
            }
            if (AgainstPlayer)
            {
                TownHall hall = TownHall.Current;
                HealthComponent h = hall != null ? hall.GetComponent<HealthComponent>() : null;
                Consider(h != null && h.IsAlive ? h : PlayerHero());
            }
            return best;
        }

        /// <summary>What guards this place: the enemy's units (and the player's hero) near it.</summary>
        public List<HealthComponent> GuardsOf(Vector3 place)
        {
            var guards = new List<HealthComponent>();
            int n = UnitSpatialGrid.Query(place, GameBalance.KingdomGuardRadius, EnemyTeam, buffer);
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

        /// <summary>Sends a raid at the enemy when the party is strong enough for its guards; returns whether it went.</summary>
        public bool ConsiderRaid(HealthComponent target = null)
        {
            if (target == null) target = RaidTarget();
            if (target == null) return false;
            List<HostileMobAI> party = RaidParty();
            if (party.Count == 0) return false;
            float ours = PowerOf(party.ConvertAll(m => m.Health));
            float theirs = PowerOf(GuardsOf(target.transform.position));
            if (ours < GameBalance.KingdomRaidPowerRatio * theirs) return false;
            StartRaid(party, target);
            return true;
        }

        public void StartRaid(List<HostileMobAI> party, HealthComponent target)
        {
            raiders.Clear();
            raiders.AddRange(party);
            raidSize = party.Count;
            raidTarget = target;
            foreach (HostileMobAI m in raiders) m.OrderAttack(target);
            RaidStarted?.Invoke(this, target);
            StrategyHud.Notify($"{KingdomName} іде в набіг: {raidSize} бійців!");
        }

        /// <summary>Once a second on a raid: turn back after heavy losses, else keep every raider fighting something.</summary>
        private void LeadRaid()
        {
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
            foreach (HostileMobAI m in raiders) m.SpeedCap = marching ? slowest : float.MaxValue;
            bool anyFighting = false;
            foreach (HostileMobAI m in raiders)
            {
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
            int id = UnitSpatialGrid.FindNearest(from, radius, EnemyTeam);
            if (id >= 0 && UnitSpatialGrid.Health(id) != null && UnitSpatialGrid.Health(id).IsAlive) return UnitSpatialGrid.Health(id);
            HealthComponent p = AgainstPlayer ? PlayerHero() : null;
            return p != null && (p.transform.position - from).sqrMagnitude <= radius * radius ? p : null;
        }

        // ------------------------------------------------------------------ a new game and saves

        private static readonly string[] Names = { "Вовчий край", "Залізні пагорби", "Ясенова долина", "Кам'яний брід", "Сива рівнина", "Орлине гніздо" };

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
        }

        private static SaveLoad.SubjectSave SaveOf(HostileMobAI m)
        {
            UnitProgress p = m.GetComponent<UnitProgress>();
            return new SaveLoad.SubjectSave { Rank = p != null ? (int)p.Rank : 0, BattleXp = p != null ? p.BattleXp : 0, Health = m.Health.CurrentHealth, Position = m.transform.position };
        }

        public SaveLoad.KingdomSave ToSave()
        {
            Prune();
            var s = new SaveLoad.KingdomSave
            {
                Name = KingdomName, Home = Home, Hall = Economy.HallLevel, HallHealth = Hall != null ? Hall.CurrentHealth : 0,
                Wood = Economy.Wood, Ore = Economy.Ore, Births = Economy.BirthCarry, Age = Age, Interregnum = InterregnumLeft, Defeated = IsDefeated
            };
            if (IsDefeated) return s;
            foreach (HostileMobAI m in subjects) s.Subjects.Add(SaveOf(m));
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

        /// <summary>A kingdom back from a save (its king grows again to the saved level, spending the points its usual way).</summary>
        public static NpcKingdom FromSave(SaveLoad.KingdomSave s)
        {
            var root = new GameObject("NpcKingdom_" + s.Name);
            root.transform.position = s.Home;
            NpcKingdom k = root.AddComponent<NpcKingdom>();
            if (!all.Contains(k)) all.Add(k);
            k.KingdomName = s.Name;
            k.Economy.Wood = s.Wood; k.Economy.Ore = s.Ore; k.Economy.BirthCarry = s.Births; k.Economy.HallLevel = s.Hall;
            k.Age = s.Age;
            k.BuildHall();
            if (s.Hall > 0 && s.HallHealth > 0) k.Hall.SetCurrentHealth(s.HallHealth);
            if (s.Defeated) { k.IsDefeated = true; return k; }
            foreach (SaveLoad.SubjectSave u in s.Subjects) Restore(k.AddSubject((UnitRank)u.Rank, u.Position), u);
            if (s.HasKing)
            {
                HostileMobAI king = k.AddSubject((UnitRank)s.King.Rank, s.King.Position);
                k.Crown(king, (HeroClass)s.KingClass);
                HeroProgress hero = king.GetComponent<HeroProgress>();
                hero.AddXp(s.KingTotalXp);
                HeroAutoPilot.SpendPoints(king.gameObject);
                if (s.King.Health > 0) king.Health.SetCurrentHealth(Mathf.Min(s.King.Health, king.Health.MaxHealth));
            }
            else if (s.Interregnum > 0f) k.InterregnumLeft = s.Interregnum;
            return k;
        }

        private static void Restore(HostileMobAI m, SaveLoad.SubjectSave u)
        {
            UnitProgress p = m.GetComponent<UnitProgress>();
            if (p != null) p.AddBattleXp(u.BattleXp);
            if (u.Health > 0) m.Health.SetCurrentHealth(Mathf.Min(u.Health, m.Health.MaxHealth));
        }

        // ------------------------------------------------------------------ the king's death (§7.2) and the fall (§7.3)

        private void OnKingDied()
        {
            if (IsDefeated) return;
            if (Raiding) EndRaid(retreat: true);
            InterregnumLeft = GameBalance.InterregnumSeconds;
            StrategyHud.Notify($"Король {KingdomName} загинув! Міжцар'я");
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
            Crown(heir, RandomClass());
            StrategyHud.Notify($"{KingdomName}: новий король");
        }

        /// <summary>The kingdom falls: its people and king are a free band now (§7.3).</summary>
        public void Fall(string why)
        {
            if (IsDefeated) return;
            IsDefeated = true;
            if (Raiding) EndRaid(retreat: false);
            foreach (HostileMobAI m in subjects) if (m != null) m.Kingdom = null;
            if (King != null) King.Kingdom = null;
            subjects.Clear();
            if (hallGridId >= 0) { UnitSpatialGrid.Unregister(hallGridId); hallGridId = -1; }
            StrategyHud.Notify($"{KingdomName} впало: {why}");
            Defeated?.Invoke(this);
            if (!AgainstPlayer) StrategyHud.Notify("Поразка: королівство гравця впало");
            else if (all.TrueForAll(k => k == null || k.IsDefeated || !k.AgainstPlayer)) StrategyHud.Notify("Перемога! Усі сусідні королівства впали");
        }
    }
}
