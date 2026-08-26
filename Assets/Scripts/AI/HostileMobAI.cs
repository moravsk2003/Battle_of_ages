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

        private Vector3 guardOrigin;
        private HealthComponent currentCombatTarget;

        public HostileMobType MobType => mobType;
        public float AggroRadius => aggroRadius;
        public float AttackRange => attackRange;
        public float RetreatHealthPercent => retreatHealthPercent;
        public Vector3 GuardOrigin => guardOrigin;
        public HealthComponent CurrentCombatTarget => currentCombatTarget;

        protected override void Awake()
        {
            base.Awake();
            guardOrigin = transform.position;

            if (combatUnit == null) combatUnit = gameObject.AddComponent<CombatUnit>();

            int hp = (mobType == HostileMobType.WarlordGuard) ? 200 : 100;
            int dmg = (mobType == HostileMobType.WarlordGuard) ? 25 : 15;
            healthComponent.InitializeHealth(hp);
            combatUnit.InitializeCombatStats(dmg, 1.8f, attackRange);

            moveSpeed = (mobType == HostileMobType.Goblin) ? 5.5f : 4.8f;
        }

        public void InitializeMob(HostileMobType type)
        {
            mobType = type;
            int hp = (mobType == HostileMobType.WarlordGuard) ? 250 : (mobType == HostileMobType.Bandit ? 140 : 80);
            int dmg = (mobType == HostileMobType.WarlordGuard) ? 30 : (mobType == HostileMobType.Bandit ? 18 : 12);
            if (healthComponent != null) healthComponent.InitializeHealth(hp);
            if (combatUnit != null) combatUnit.InitializeCombatStats(dmg, 1.6f, attackRange);
            moveSpeed = (mobType == HostileMobType.Goblin) ? 5.5f : 4.6f;
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
        }

        public void AlertNearbyAllies(HealthComponent target)
        {
            Collider[] allies = Physics.OverlapSphere(transform.position, 22.0f);
            for (int i = 0; i < allies.Length; i++)
            {
                HostileMobAI allyMob = allies[i].GetComponentInParent<HostileMobAI>();
                if (allyMob != null && allyMob != this && allyMob.CurrentCombatTarget == null)
                {
                    allyMob.currentCombatTarget = target;
                    allyMob.ChangeState(AIStateType.Chase);
                }
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
            // 1. High Priority: Vulnerable Player Villagers / Gatherers
            RtsUnit nearestUnit = UnitRegistry.FindNearestUnit(transform.position, aggroRadius);
            if (nearestUnit != null)
            {
                HealthComponent unitHp = nearestUnit.GetComponent<HealthComponent>();
                if (unitHp != null && unitHp.IsAlive)
                {
                    return unitHp;
                }
            }

            // 2. Medium Priority: Player Character
            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                HealthComponent playerHp = player.GetComponent<HealthComponent>();
                if (playerHp != null && playerHp.IsAlive)
                {
                    float dist = Vector3.Distance(transform.position, player.transform.position);
                    if (dist <= aggroRadius)
                    {
                        return playerHp;
                    }
                }
            }

            // 3. Low Priority: Player Walls / Fortifications blocking path
            Collider[] hits = Physics.OverlapSphere(transform.position, aggroRadius * 0.75f);
            for (int i = 0; i < hits.Length; i++)
            {
                Building.BuildingNode bNode = hits[i].GetComponentInParent<Building.BuildingNode>();
                if (bNode != null)
                {
                    HealthComponent bHp = bNode.GetComponent<HealthComponent>();
                    if (bHp != null && bHp.IsAlive)
                    {
                        return bHp;
                    }
                }
            }

            return null;
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

            // Check retreat condition (<= 25% HP)
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
            if (dist > mob.AggroRadius * 1.6f)
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
        private float strafeTimer = 0f;

        public void Enter(AIBaseController controller)
        {
            strafeTimer = 0f;
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
                strafeTimer += deltaTime;
                Vector3 strafePos = mob.GetFlankingDestination(mob.CurrentCombatTarget.transform.position, mob.AttackRange * 0.95f);
                mob.SetMoveDestination(strafePos);
            }
        }

        public void Exit(AIBaseController controller) { }
    }

    public class MobRetreatState : IAIState
    {
        public AIStateType StateType => AIStateType.Retreat;

        public void Enter(AIBaseController controller)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;
            mob.SetMoveDestination(mob.GuardOrigin);
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            HostileMobAI mob = controller as HostileMobAI;
            if (mob == null) return;

            float distToCamp = Vector3.Distance(mob.transform.position, mob.GuardOrigin);
            if (distToCamp <= 4.0f)
            {
                mob.Health.Heal(Mathf.CeilToInt(12 * deltaTime));
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
