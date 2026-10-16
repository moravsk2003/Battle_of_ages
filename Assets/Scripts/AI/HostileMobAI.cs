using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

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
        public float AttackRange => attackRange;
        public float RetreatHealthPercent => retreatHealthPercent;
        public float RetreatHealPerSecond => retreatHealPerSecond;
        public Vector3 GuardOrigin => guardOriginLocked ? guardOrigin : transform.position;
        public HealthComponent CurrentCombatTarget => currentCombatTarget;
        public bool IsOnAssaultOrder => isOnAssaultOrder;

        protected override void Awake()
        {
            base.Awake();

            if (combatUnit == null) combatUnit = gameObject.AddComponent<CombatUnit>();

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

        protected override float CurrentMoveSpeed => Combat != null ? Combat.MoveSpeed : moveSpeed;

        public void InitializeMob(HostileMobType type)
        {
            mobType = type;
            ApplyMobStats();
        }

        public void SetGuardOrigin(Vector3 origin)
        {
            guardOrigin = origin;
            guardOriginLocked = true;

            // Re-pick the patrol point that may have been chosen around the old (pre-spawn) position.
            if (CurrentStateType == AIStateType.Wander) ChangeState(AIStateType.Wander);
        }

        /// <summary>Single source of truth for per-type mob stats.</summary>
        private void ApplyMobStats()
        {
            int hp;
            int dmg;
            switch (mobType)
            {
                case HostileMobType.WarlordGuard: hp = 250; dmg = 30; break; // speed: CharacterSpeed (damage x modifier)
                case HostileMobType.Goblin:       hp = 80;  dmg = 12; break;
                default:                          hp = 140; dmg = 18; break;
            }

            if (Health != null) Health.InitializeHealth(hp);
            if (Combat != null) Combat.InitializeCombatStats(dmg, 1.6f, attackRange);

            // The kit is part of the tuned stats; trophies can drop from it
            BattleOfAges.Crafting.Equipment gear = GetComponent<BattleOfAges.Crafting.Equipment>();
            if (gear == null) gear = gameObject.AddComponent<BattleOfAges.Crafting.Equipment>();
            gear.DropsLoot = true;
            gear.InitLoadout(BattleOfAges.Crafting.StartingKits.For(mobType));
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

        public void AlertNearbyAllies(HealthComponent target)
        {
            int count = UnitSpatialGrid.Query(transform.position, AllyAlertRadius, UnitSpatialGrid.TeamHostile, gridBuffer, GridId);
            for (int i = 0; i < count; i++)
            {
                HostileMobAI allyMob = UnitSpatialGrid.Owner(gridBuffer[i]) as HostileMobAI;
                if (allyMob == null || allyMob == this || allyMob.CurrentCombatTarget != null) continue;
                if (!allyMob.Health.IsAlive || allyMob.CurrentStateType == AIStateType.Retreat) continue;

                allyMob.currentCombatTarget = target;
                allyMob.ChangeState(AIStateType.Chase);
            }
        }

        public Vector3 GetFlankingDestination(Vector3 targetPos, float desiredRadius)
        {
            int id = Mathf.Abs(gameObject.GetInstanceID());
            float angle = ((id % 8) * 45.0f + Time.time * 20.0f) * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * desiredRadius;
            Vector3 flankPos = targetPos + offset;
            flankPos.y = RtsUnit.GetTerrainHeight(flankPos);
            return flankPos;
        }

        public HealthComponent FindNearestTarget()
        {
            // 1. High Priority: Vulnerable Player Villagers / Gatherers / soldiers (spatial grid: constant cost per query)
            int nearest = UnitSpatialGrid.FindNearest(transform.position, aggroRadius, UnitSpatialGrid.TeamPlayer);
            if (nearest >= 0)
            {
                HealthComponent unitHp = UnitSpatialGrid.Health(nearest);
                if (unitHp != null && unitHp.IsAlive)
                {
                    return unitHp;
                }
            }

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
                GameObject player = GameObject.Find("Player");
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
            Vector3 flankTarget = mob.GetFlankingDestination(mob.CurrentCombatTarget.transform.position, mob.AttackRange * 0.9f);
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

            float dist = Vector3.Distance(mob.transform.position, mob.CurrentCombatTarget.transform.position);
            if (!mob.IsOnAssaultOrder && dist > mob.AggroRadius * 1.6f)
            {
                // Lost target
                mob.SetTarget(null);
                mob.ChangeState(AIStateType.Wander);
                return;
            }

            if (dist <= mob.AttackRange)
            {
                mob.ChangeState(AIStateType.Attack);
            }
            else
            {
                Vector3 flankTarget = mob.GetFlankingDestination(mob.CurrentCombatTarget.transform.position, mob.AttackRange * 0.9f);
                mob.SetMoveDestination(flankTarget);
            }
        }

        public void Exit(AIBaseController controller) { }
    }

    public class MobAttackState : IAIState
    {
        public AIStateType StateType => AIStateType.Attack;

        public void Enter(AIBaseController controller) { }

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

            float dist = Vector3.Distance(mob.transform.position, mob.CurrentCombatTarget.transform.position);
            if (dist > mob.AttackRange + 1.8f)
            {
                mob.ChangeState(AIStateType.Chase);
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
            else
            {
                // Combat Strafe: Move laterally around target during attack cooldown
                Vector3 strafePos = mob.GetFlankingDestination(mob.CurrentCombatTarget.transform.position, mob.AttackRange * 0.95f);
                mob.SetMoveDestination(strafePos);
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
