using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Crafting;

namespace BattleOfAges.AI
{
    public enum HostileMobType
    {
        Bandit,
        Goblin,
        WarlordGuard
    }

    /// <summary>
    /// AI Controller for hostile mobs & raiders.
    /// Features aggro detection, pursuit, melee combat tactics, and tactical retreat on low HP.
    /// </summary>
    [DisallowMultipleComponent]
    public class HostileMobAI : AIBaseController
    {
        [Header("Hostile Mob Settings")]
        [SerializeField] private HostileMobType mobType = HostileMobType.Bandit;
        [SerializeField] private float aggroRadius = 18.0f;
        [SerializeField] private float attackRange = 2.5f;
        [SerializeField] private float retreatHealthPercent = 0.20f; // Retreat at <= 20% HP
        [SerializeField] private float retreatHealPerSecond = 12.0f;

        private const float AllyAlertRadius = 22.0f;
        private static readonly int[] gridBuffer = new int[64];

        private Vector3 guardOrigin;
        private bool guardOriginLocked;
        private HealthComponent currentCombatTarget;
        private HealthComponent cachedPlayerHealth;
        private bool isOnAssaultOrder;

        public HostileMobType MobType => mobType;
        public float AggroRadius => aggroRadius;
        public float AttackRange => Combat != null ? Mathf.Max(attackRange, Combat.AttackRange) : attackRange; // a bow or a class shoots further
        public float RetreatHealthPercent => retreatHealthPercent;
        public float RetreatHealPerSecond => retreatHealPerSecond;
        public Vector3 GuardOrigin => guardOriginLocked ? guardOrigin : transform.position;
        public HealthComponent CurrentCombatTarget => currentCombatTarget;
        public bool IsOnAssaultOrder => isOnAssaultOrder;

        private float provokedUntil = float.NegativeInfinity;
        /// <summary>Hit lately: it goes after whoever hit it and does not give up at the leash (docs/GAME_DESIGN.md §11.1).</summary>
        public bool IsProvoked => Time.time < provokedUntil;

        private void OnHit(int damage)
        {
            CombatUnit attacker = Health != null ? Health.LastAttacker : null;
            HealthComponent attackerHp = attacker != null ? attacker.GetComponent<HealthComponent>() : null;
            if (attackerHp == null || !attackerHp.IsAlive || !Health.IsAlive) return;
            provokedUntil = Time.time + GameBalance.ProvokedSeconds;
            if (CurrentStateType == AIStateType.Retreat) return;
            // On its way to something else (a raid's hall, far off) it turns on whoever hit it; in a fight at hand it stays in it
            bool busy = currentCombatTarget != null && currentCombatTarget.IsAlive && currentCombatTarget.BodyRadius <= 0f
                        && currentCombatTarget.DistanceFrom(transform.position) <= aggroRadius;
            if (busy) return;
            SetTarget(attackerHp); // also calls the allies nearby
            ChangeState(AIStateType.Chase);
        }

        protected override void Awake()
        {
            base.Awake();

            if (combatUnit == null) combatUnit = gameObject.AddComponent<CombatUnit>();
            if (Health != null) Health.OnTakeDamage += OnHit;

            ApplyMobStats();
        }

        protected override void Start()
        {
            base.Start();

            // Spawners create the mob at the world origin and move it afterwards,
            // so the guard post is captured here rather than in Awake.
            if (!guardOriginLocked)
            {
                SetGuardOrigin(transform.position);
            }
        }

        /// <summary>A raid marches at the pace of its slowest member (NpcKingdom): no faster than this.</summary>
        public float SpeedCap { get => speedCap; set { if (speedCap == value) return; speedCap = value; CrowdChanged(); } }
        private float speedCap = float.MaxValue;
        protected override bool CrowdCanGlide => !climbing;

        protected override float CurrentMoveSpeed => Mathf.Min(SpeedCap, Combat != null ? Combat.MoveSpeed : moveSpeed);

        public void InitializeMob(HostileMobType type)
        {
            mobType = type;
            ApplyMobStats();
        }

        private UnitRank? subjectRank;
        private Item[] subjectKit;
        private bool subjectHero;
        private int team = UnitSpatialGrid.TeamHostile;
        /// <summary>Its side: camps and raiders (TeamHostile), an NPC kingdom's own side, or the player's (a kingdom on the player's side).</summary>
        public int Team
        {
            get => team;
            set { if (team == value) return; team = value; RefreshGridTeam(); }
        }
        protected override int GridTeam => team;
        private bool AgainstPlayer => team != UnitSpatialGrid.TeamPlayer; // camps and NPC kingdoms

        /// <summary>The NPC kingdom this unit lives in (null: a mob of a camp or a deserter).</summary>
        public NpcKingdom Kingdom { get; set; }

        /// <summary>A unit of an NPC kingdom (docs/GAME_DESIGN.md §12): its own rank and kit instead of the mob type's.</summary>
        public void InitializeAsSubject(UnitRank rank, Item[] kit, bool hero = false)
        {
            subjectRank = rank;
            subjectKit = kit;
            subjectHero = hero;
            ApplyMobStats();
        }

        public void SetGuardOrigin(Vector3 origin)
        {
            guardOrigin = origin;
            guardOriginLocked = true;

            // Re-pick the patrol point that may have been chosen around the old (pre-spawn) position.
            if (CurrentStateType == AIStateType.Wander) ChangeState(AIStateType.Wander);
        }

        /// <summary>Rank of each mob type (docs/GAME_DESIGN.md §11.1): bandits and goblins are warriors, the warlord a general.</summary>
        public static UnitRank RankOf(HostileMobType type) => type == HostileMobType.WarlordGuard ? UnitRank.General : UnitRank.Warrior;

        /// <summary>Base stats of the mob's rank with its kit worn on top; trophies can drop from the kit.</summary>
        private void ApplyMobStats()
        {
            Equipment gear = GetComponent<Equipment>();
            if (gear == null) gear = gameObject.AddComponent<Equipment>();
            gear.DropsLoot = true;
            gear.LoadSlots(subjectKit ?? StartingKits.ForCamp(mobType));
            UnitRank rank = subjectRank ?? RankOf(mobType);
            UnitRanks.Apply(gameObject, rank, subjectHero, range: attackRange);
            UnitProgress.Setup(gameObject, rank, subjectHero);
            if (Health != null) Health.InitializeHealth(Health.MaxHealth); // spawn at full health
        }

        protected override void RegisterStates()
        {
            states[AIStateType.Idle] = new MobPatrolState();
            states[AIStateType.Wander] = new MobPatrolState();
            states[AIStateType.Chase] = new MobChaseState();
            states[AIStateType.Attack] = new MobAttackState();
            states[AIStateType.Retreat] = new MobRetreatState();

            ChangeState(AIStateType.Wander);
        }

        public void SetTarget(HealthComponent target)
        {
            currentCombatTarget = target;
            if (target != null)
            {
                AlertNearbyAllies(target);
            }
            else
            {
                isOnAssaultOrder = false;
            }
        }

        /// <summary>
        /// Ordered attack (e.g. from a settlement leader): pursues the target beyond the normal leash range.
        /// </summary>
        public void OrderAttack(HealthComponent target)
        {
            if (target == null || !target.IsAlive || !Health.IsAlive) return;
            currentCombatTarget = target;
            isOnAssaultOrder = true;
            ChangeState(AIStateType.Chase);
        }

        /// <summary>
        /// A raider on its way: any enemy within this radius comes first, the unit or the hero, unless it already fights one at
        /// hand (they marched past the player's hero, and it could cut them all down on the road). True when it turned.
        /// </summary>
        public bool EngageNear(float radius)
        {
            HealthComponent now = currentCombatTarget;
            if (now != null && now.IsAlive && now.BodyRadius <= 0f && now.DistanceFrom(transform.position) <= radius) return false;
            HealthComponent best = null;
            float bestDistance = radius;
            int unit = UnitSpatialGrid.FindNearestEnemy(transform.position, radius, team);
            if (unit >= 0)
            {
                HealthComponent h = UnitSpatialGrid.Health(unit);
                if (h != null && h.IsAlive) { best = h; bestDistance = Vector3.Distance(transform.position, h.transform.position); }
            }
            if (AgainstPlayer)
            {
                HealthComponent hero = GetPlayerHealth();
                if (hero != null && hero.IsAlive && Vector3.Distance(transform.position, hero.transform.position) <= bestDistance) best = hero;
            }
            if (best == null || best == now) return false;
            OrderAttack(best);
            return true;
        }

        public void AlertNearbyAllies(HealthComponent target)
        {
            int count = UnitSpatialGrid.Query(transform.position, AllyAlertRadius, team, gridBuffer, GridId);
            for (int i = 0; i < count; i++)
            {
                HostileMobAI allyMob = UnitSpatialGrid.Owner(gridBuffer[i]) as HostileMobAI;
                if (allyMob == null || allyMob == this || allyMob.CurrentCombatTarget != null) continue;
                if (!allyMob.Health.IsAlive || allyMob.CurrentStateType == AIStateType.Retreat) continue;

                allyMob.currentCombatTarget = target;
                allyMob.ChangeState(AIStateType.Chase);
            }
        }

        // ------------------------------------------------------------------ walls in the way (§10.5)

        private bool climbing;
        private float nextSiegeCheck;
        private bool wallInWay;
        private Building.WallSegment siegeWall;
        private Vector3 siegePoint, siegeSide;
        private Building.SiegeLadder ladder;

        /// <summary>On a ladder or the wall: moved by the climb, not by the motor.</summary>
        public bool IsClimbing => climbing;

        protected override void ExecuteMovement(float dt)
        {
            if (climbing) return;
            base.ExecuteMovement(dt);
        }

        /// <summary>
        /// While going for a target: if a wall stands in the way, the wall takes over (true). The strong (strength at
        /// least GameBalance.WallBreachStrength) break it where it blocks them; the rest go to a ladder there (raising
        /// one if there is none), wait their turn at its foot and climb over.
        /// </summary>
        public bool HandleWallInWay(Vector3 targetPos)
        {
            if (climbing) return true;
            if (!AgainstPlayer) return false; // the player's side: the gates are open to it
            if (Time.time >= nextSiegeCheck)
            {
                nextSiegeCheck = Time.time + 0.5f;
                wallInWay = Building.WallBarriers.Blocked(transform.position, targetPos, false)
                            && Building.WallBarriers.FirstCrossing(transform.position, targetPos, out siegeWall, out siegePoint, out siegeSide);
            }
            if (!wallInWay || siegeWall == null) { ladder = null; return false; }

            float half = Utils.WallBuilder.Thickness * 0.5f;
            if (Combat != null && Combat.CanHarmWalls)
            {
                Vector3 stand = siegePoint + siegeSide * (half + 0.6f);
                if (FlatDistance(transform.position, stand) > stoppingDistance + 0.3f) { SetMoveDestination(stand); return true; } // (it stops within stoppingDistance)
                StopMovement();
                Face(siegePoint);
                HealthComponent wallHp = siegeWall.GetComponent<HealthComponent>();
                if (wallHp != null && wallHp.IsAlive && Combat.CanAttack) Combat.TryAttack(wallHp);
                return true;
            }

            if (ladder == null || ladder.Wall != siegeWall) ladder = Building.SiegeLadder.Near(siegeWall, siegePoint, siegeSide);
            Vector3 foot = ladder != null ? ladder.Foot : siegePoint + siegeSide * (half + Building.SiegeLadder.FootOut);
            float toFoot = FlatDistance(transform.position, foot);
            if (ladder == null)
            {
                if (toFoot > stoppingDistance + 0.3f) { SetMoveDestination(foot); return true; }
                ladder = Building.SiegeLadder.Raise(siegeWall, siegePoint, siegeSide); // the first one there raises it
            }
            if (toFoot <= stoppingDistance + 0.6f && ladder.TryStartClimb(Health))
            {
                StartCoroutine(Climb(ladder));
                return true;
            }
            if (ladder.Ready && ladder.Free) { SetMoveDestination(foot); return true; } // its turn: to the foot
            // Wait for the turn round the foot, a little out from the wall
            int id = Mathf.Abs(GetInstanceID());
            Vector3 along = Vector3.Cross(Vector3.up, ladder.Side);
            Vector3 wait = ladder.Foot + ladder.Side * (0.9f + (id % 3) * 0.6f) + along * (((id / 3) % 5) - 2) * 0.7f;
            if (FlatDistance(transform.position, wait) > stoppingDistance + 0.2f) SetMoveDestination(wait); else StopMovement();
            return true;
        }

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>Up on the walkway it fights the defenders within this, for at most this long, before going down inside.</summary>
        private const float WalkwayFightRadius = 7f, WalkwayFightSeconds = 30f;

        /// <summary>The nearest living unit of the player's on the walls within radius.</summary>
        private RtsUnit NearestDefender(float radius)
        {
            RtsUnit best = null;
            float bestD = radius * radius;
            var units = UnitRegistry.ActiveUnits;
            for (int i = 0; i < units.Count; i++)
            {
                RtsUnit u = units[i];
                if (u == null || !u.OnWall || !u.IsAlive) continue;
                float d = (u.transform.position - transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        private void Face(Vector3 p)
        {
            Vector3 d = p - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
        }

        /// <summary>Up the ladder, over the breastwork, across the walkway and down inside (vulnerable all the way).</summary>
        private System.Collections.IEnumerator Climb(Building.SiegeLadder l)
        {
            climbing = true;
            CrowdChanged();
            StopMovement();
            Vector3[] path = { l.Foot, l.Top, l.Walk, l.Inside };
            float[] speed = { GameBalance.LadderClimbSpeed, 1.4f, 2.5f };
            Face(l.Walk);
            for (int leg = 0; leg < 3; leg++)
            {
                Vector3 a = transform.position, b = path[leg + 1];
                float time = Mathf.Max(0.2f, Vector3.Distance(a, b) / speed[leg]);
                for (float t = 0f; t < time; t += Time.deltaTime)
                {
                    if (Health == null || !Health.IsAlive) { if (l != null) l.EndClimb(Health); DropDown(); yield break; }
                    if (l == null || l.Wall == null) { DropDown(); yield break; } // the wall fell under it: it falls too
                    transform.position = Vector3.Lerp(a, b, t / time);
                    yield return null;
                }
                transform.position = b;
                if (leg == 0) l.EndClimb(Health); // up: the next may start
                if (leg == 1)
                {
                    yield return new WaitForSeconds(0.3f);
                    // On the walkway: fight the defenders up here first, moving along to them
                    for (float fight = 0f; fight < WalkwayFightSeconds; fight += Time.deltaTime)
                    {
                        if (Health == null || !Health.IsAlive) { DropDown(); yield break; }
                        if (l == null || l.Wall == null) { DropDown(); yield break; }
                        RtsUnit defender = NearestDefender(WalkwayFightRadius);
                        if (defender == null) break;
                        Vector3 to = defender.transform.position - transform.position;
                        float reach = Combat != null ? Combat.AttackRange : 2f;
                        if (to.magnitude > reach * 0.85f) transform.position = Vector3.MoveTowards(transform.position, defender.transform.position, 2f * Time.deltaTime);
                        Face(defender.transform.position);
                        HealthComponent dHp = defender.GetComponent<HealthComponent>();
                        if (Combat != null && Combat.CanAttack && dHp != null) Combat.TryAttack(dHp);
                        yield return null;
                    }
                    path[3] = transform.position - l.Side * (Utils.WallBuilder.Thickness * 0.5f + 1.2f); // down from where it is
                    path[3].y = RtsUnit.GetTerrainHeight(path[3]);
                }
            }
            climbing = false;
            CrowdChanged();
            ladder = null;
            wallInWay = false;
            nextSiegeCheck = Time.time + 0.5f;
        }

        /// <summary>Off the ladder or the walkway at once (the wall fell, or it died up there): onto the ground below, the siege over.</summary>
        private void DropDown()
        {
            Vector3 p = transform.position;
            p.y = RtsUnit.GetTerrainHeight(p);
            transform.position = p;
            climbing = false;
            CrowdChanged();
            ladder = null;
            wallInWay = false;
            nextSiegeCheck = Time.time + 0.5f;
        }

        /// <summary>
        /// How near its place round a target is: near enough that it can strike from where it stops, stoppingDistance short of the
        /// place (at 90% of its reach a swordsman stood at 3.4 m with a reach of 2.5, and stayed there for good).
        /// </summary>
        public float ApproachRadius => Mathf.Max(MinApproach, AttackRange * 0.9f - stoppingDistance);
        private const float MinApproach = 0.8f;

        /// <summary>
        /// Its own place round the target, at this reach (from the wall of a building). circling: the place turns round the
        /// target, a swordsman's footwork between blows; a place always turning made anyone with a long reach run round its
        /// target at 20 degrees a second, 9 m/s at a mage's range, and no one could catch it.
        /// </summary>
        public Vector3 GetFlankingDestination(HealthComponent target, float desiredRadius, bool circling = false)
        {
            int id = Mathf.Abs(gameObject.GetInstanceID());
            float angle = ((id % 8) * 45.0f + (circling ? Time.time * 20.0f : 0f)) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (desiredRadius + target.BodyRadius);
            Vector3 flankPos = target.transform.position + offset;
            flankPos.y = RtsUnit.GetTerrainHeight(flankPos);
            return flankPos;
        }

        public HealthComponent FindNearestTarget()
        {
            // 1. High Priority: Vulnerable Player Villagers / Gatherers / soldiers (spatial grid: constant cost per query)
            int nearest = UnitSpatialGrid.FindNearestEnemy(transform.position, aggroRadius, team);
            if (nearest >= 0)
            {
                HealthComponent unitHp = UnitSpatialGrid.Health(nearest);
                if (unitHp != null && unitHp.IsAlive)
                {
                    return unitHp;
                }
            }

            if (!AgainstPlayer) return null; // on the player's side: only enemy units

            // 2. Medium Priority: Player Character
            HealthComponent playerHp = GetPlayerHealth();
            if (playerHp != null && playerHp.IsAlive)
            {
                float dist = Vector3.Distance(transform.position, playerHp.transform.position);
                if (dist <= aggroRadius)
                {
                    return playerHp;
                }
            }

            // 3. Low Priority: Player Walls / Fortifications blocking path
            float wallRange = aggroRadius * 0.75f;
            var buildings = Building.BuildingNode.Active;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building.BuildingNode bNode = buildings[i];
                if (bNode == null || (bNode.transform.position - transform.position).sqrMagnitude > wallRange * wallRange) continue;
                if (Combat != null && !Combat.CanHarmWalls && bNode.GetComponent<Building.WallSegment>() != null) continue; // a wall is too much for it
                HealthComponent bHp = bNode.GetComponent<HealthComponent>();
                if (bHp != null && bHp.IsAlive)
                {
                    return bHp;
                }
            }

            return null;
        }

        /// <summary>Cached lookup of the player's health (avoids GameObject.Find on every AI tick).</summary>
        public HealthComponent GetPlayerHealth()
        {
            if (cachedPlayerHealth == null)
            {
                GameObject player = Managers.GameContext.Player;
                if (player != null) cachedPlayerHealth = player.GetComponent<HealthComponent>();
            }
            return cachedPlayerHealth;
        }
    }

    public class MobPatrolState : IAIState
    {
        public AIStateType StateType => AIStateType.Wander;
        private float patrolTimer = 0f;

        public void Enter(AIBaseController controller)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;

            Vector2 randCircle = Random.insideUnitCircle * 12.0f;
            Vector3 targetPos = mob.GuardOrigin + new Vector3(randCircle.x, 0, randCircle.y);
            mob.SetMoveDestination(targetPos);
            patrolTimer = 0f;
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;

            // Look for targets
            HealthComponent target = mob.FindNearestTarget();
            if (target != null)
            {
                mob.SetTarget(target);
                mob.ChangeState(AIStateType.Chase);
                return;
            }

            patrolTimer += deltaTime;
            if (!mob.HasDestination || patrolTimer > 6.0f)
            {
                Enter(controller);
            }
        }

        public void Exit(AIBaseController controller) { }
    }

    public class MobChaseState : IAIState
    {
        public AIStateType StateType => AIStateType.Chase;

        public void Enter(AIBaseController controller)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null || mob.CurrentCombatTarget == null) return;
            Vector3 flankTarget = mob.GetFlankingDestination(mob.CurrentCombatTarget, mob.ApproachRadius);
            mob.SetMoveDestination(flankTarget);
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;

            // Check retreat condition (<= RetreatHealthPercent HP)
            if (mob.Health.CurrentHealth <= mob.Health.MaxHealth * mob.RetreatHealthPercent)
            {
                mob.ChangeState(AIStateType.Retreat);
                return;
            }

            if (mob.CurrentCombatTarget == null || !mob.CurrentCombatTarget.IsAlive)
            {
                mob.SetTarget(null);
                mob.ChangeState(AIStateType.Wander);
                return;
            }

            float dist = mob.CurrentCombatTarget.DistanceFrom(mob.transform.position);
            if (!mob.IsOnAssaultOrder && !mob.IsProvoked && dist > mob.AggroRadius * 1.6f)
            {
                // Lost target
                mob.SetTarget(null);
                mob.ChangeState(AIStateType.Wander);
                return;
            }

            bool shootsOver = mob.Combat != null && mob.Combat.IsRanged; // arrows and bolts fly over walls
            if (dist <= mob.AttackRange && (shootsOver || !mob.IsClimbing))
            {
                if (shootsOver || !Building.WallBarriers.Blocked(mob.transform.position, mob.CurrentCombatTarget.transform.position, false))
                {
                    mob.ChangeState(AIStateType.Attack);
                    return;
                }
            }
            if (mob.HandleWallInWay(mob.CurrentCombatTarget.transform.position)) return;
            if (dist > mob.AttackRange)
            {
                Vector3 flankTarget = mob.GetFlankingDestination(mob.CurrentCombatTarget, mob.ApproachRadius);
                mob.SetMoveDestination(flankTarget);
            }
        }

        public void Exit(AIBaseController controller) { }
    }

    public class MobAttackState : IAIState
    {
        public AIStateType StateType => AIStateType.Attack;

        public void Enter(AIBaseController controller)
        {
            // In reach: who strikes from afar stops here, not at its place round the target (far from the viewer the AI thinks
            // once in 2 s, a mage ran a quarter of the way round in that time)
            HostileMobAI mob = controller as HostileMobAI;
            if (mob != null && mob.Combat != null && mob.Combat.IsRanged) mob.StopMovement();
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;

            // Check retreat condition
            if (mob.Health.CurrentHealth <= mob.Health.MaxHealth * mob.RetreatHealthPercent)
            {
                mob.ChangeState(AIStateType.Retreat);
                return;
            }

            if (mob.CurrentCombatTarget == null || !mob.CurrentCombatTarget.IsAlive)
            {
                mob.SetTarget(null);
                mob.ChangeState(AIStateType.Wander);
                return;
            }

            if (mob.IsClimbing) return;
            float dist = mob.CurrentCombatTarget.DistanceFrom(mob.transform.position);
            bool melee = mob.Combat == null || !mob.Combat.IsRanged;
            if (dist > mob.AttackRange + 1.8f || (melee && Building.WallBarriers.Blocked(mob.transform.position, mob.CurrentCombatTarget.transform.position, false)))
            {
                mob.ChangeState(AIStateType.Chase); // out of reach, or a wall between (a sword does not reach over it)
                return;
            }

            // Face target
            Vector3 lookDir = mob.CurrentCombatTarget.transform.position - mob.transform.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                mob.transform.rotation = Quaternion.Slerp(mob.transform.rotation, Quaternion.LookRotation(lookDir), 12f * deltaTime);
            }

            if (mob.Combat != null && mob.Combat.CanAttack)
            {
                mob.Combat.TryAttack(mob.CurrentCombatTarget);
                mob.StopMovement();
            }
            else if (melee && mob.CurrentCombatTarget.BodyRadius <= 0f)
            {
                // Combat Strafe: Move laterally around target during attack cooldown
                Vector3 strafePos = mob.GetFlankingDestination(mob.CurrentCombatTarget, mob.AttackRange * 0.95f, circling: true);
                mob.SetMoveDestination(strafePos);
            }
            else
            {
                mob.StopMovement(); // who shoots stands and shoots; a building does not dodge
            }
        }

        public void Exit(AIBaseController controller) { }
    }

    public class MobRetreatState : IAIState
    {
        public AIStateType StateType => AIStateType.Retreat;
        private float healAccumulator;

        public void Enter(AIBaseController controller)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;

            // Drop the old target so allies can recruit this mob again once it has recovered.
            mob.SetTarget(null);
            mob.SetMoveDestination(mob.GuardOrigin);
            healAccumulator = 0f;
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;

            float distToCamp = Vector3.Distance(mob.transform.position, mob.GuardOrigin);
            if (distToCamp <= 4.0f)
            {
                // Accumulate fractional healing so the rate does not depend on the AI tick interval.
                healAccumulator += mob.RetreatHealPerSecond * deltaTime;
                int wholeHeal = Mathf.FloorToInt(healAccumulator);
                if (wholeHeal > 0)
                {
                    healAccumulator -= wholeHeal;
                    mob.Health.Heal(wholeHeal);
                }
                if (mob.Health.CurrentHealth >= mob.Health.MaxHealth * 0.80f)
                {
                    mob.ChangeState(AIStateType.Wander);
                }
            }
            else
            {
                mob.SetMoveDestination(mob.GuardOrigin);
            }
        }

        public void Exit(AIBaseController controller) { }
    }
}
