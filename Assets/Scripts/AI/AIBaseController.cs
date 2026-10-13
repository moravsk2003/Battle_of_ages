using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.AI
{
    /// <summary>
    /// Base Finite State Machine controller for all AI entities (animals, monsters, squad units).
    /// Features distance-based update throttling (AI LOD) to maintain 60+ FPS with hundreds of units.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(HealthComponent))]
    public abstract class AIBaseController : MonoBehaviour
    {
        [Header("AI LOD & Throttling Settings")]
        [SerializeField] private float closeDistance = 25f;     // Updates 15 times/sec
        [SerializeField] private float mediumDistance = 75f;    // Updates 4 times/sec
        [SerializeField] private float distantDistance = 150f;  // Updates 1 time/sec

        [Header("Movement & Navigation")]
        [SerializeField] protected float moveSpeed = 4.5f;
        [SerializeField] protected float rotationSpeed = 8.0f;
        [SerializeField] protected float stoppingDistance = 1.2f;

        [Header("Obstacle Unsticking (no NavMesh: movement is straight-line)")]
        [SerializeField] private float stuckCheckInterval = 0.5f;
        [SerializeField] private float stuckProgressRatio = 0.25f; // stuck if moved < 25% of expected distance
        [SerializeField] private float detourDuration = 1.2f;

        protected CharacterController characterController;
        protected HealthComponent healthComponent;
        protected CombatUnit combatUnit;

        protected IAIState currentState;
        protected readonly Dictionary<AIStateType, IAIState> states = new Dictionary<AIStateType, IAIState>();

        private Transform playerViewer;
        private float tickTimer = 0f;
        private float currentTickInterval = 0.066f; // ~15 Hz default

        protected Vector3 destination;
        protected bool hasDestination = false;

        private float stuckTimer;
        private Vector3 lastProgressPos;
        private float detourTimer;
        private Vector3 detourDir;
        private int detourSide = 1;

        public CharacterController CharacterCtrl => characterController != null ? characterController : (characterController = GetComponent<CharacterController>());
        public HealthComponent Health => healthComponent != null ? healthComponent : (healthComponent = GetComponent<HealthComponent>());
        public CombatUnit Combat => combatUnit != null ? combatUnit : (combatUnit = GetComponent<CombatUnit>());
        public AIStateType CurrentStateType => currentState != null ? currentState.StateType : AIStateType.Idle;
        public IReadOnlyDictionary<AIStateType, IAIState> States => states;
        public float MoveSpeed => moveSpeed;
        public Vector3 Destination => destination;
        public bool HasDestination => hasDestination;

        public void TickAI(float deltaTime)
        {
            if (currentState != null)
            {
                currentState.UpdateState(this, deltaTime);
            }
        }

        protected virtual void Awake()
        {
            characterController = GetComponent<CharacterController>();
            healthComponent = GetComponent<HealthComponent>();
            combatUnit = GetComponent<CombatUnit>();
            if (states.Count == 0) RegisterStates();
        }

        protected virtual void Start()
        {
            FindPlayerViewer();
            if (states.Count == 0) RegisterStates();
        }

        protected abstract void RegisterStates();

        private void FindPlayerViewer()
        {
            if (playerViewer == null)
            {
                GameObject player = GameObject.Find("Player");
                if (player != null) playerViewer = player.transform;
                else if (Camera.main != null) playerViewer = Camera.main.transform;
            }
        }

        public void ChangeState(AIStateType newStateType)
        {
            if (states.Count == 0)
            {
                RegisterStates();
            }

            if (states.TryGetValue(newStateType, out IAIState newState))
            {
                if (currentState != null)
                {
                    currentState.Exit(this);
                }

                currentState = newState;
                currentState.Enter(this);
            }
        }

        protected virtual void Update()
        {
            if (healthComponent != null && !healthComponent.IsAlive)
            {
                return;
            }

            if (playerViewer == null)
            {
                FindPlayerViewer();
            }

            // Calculate distance to player/camera for AI Throttling LOD
            float distToViewer = (playerViewer != null)
                ? Vector3.Distance(transform.position, playerViewer.position)
                : 10f;

            if (distToViewer < closeDistance)
            {
                currentTickInterval = 0.05f; // 20 Hz
            }
            else if (distToViewer < mediumDistance)
            {
                currentTickInterval = 0.2f;  // 5 Hz
            }
            else if (distToViewer < distantDistance)
            {
                currentTickInterval = 1.0f;  // 1 Hz
            }
            else
            {
                currentTickInterval = 2.0f;  // 0.5 Hz (distant background)
            }

            // AI Decision Logic Tick
            tickTimer += Time.deltaTime;
            if (tickTimer >= currentTickInterval)
            {
                float elapsed = tickTimer;
                tickTimer = 0f;

                if (currentState != null)
                {
                    currentState.UpdateState(this, elapsed);
                }
            }

            // Smooth physical movement execution (every frame)
            ExecuteMovement();
        }

        public void SetMoveDestination(Vector3 targetPos)
        {
            destination = targetPos;
            destination.y = RtsUnit.GetTerrainHeight(destination);
            hasDestination = true;
        }

        public void StopMovement()
        {
            hasDestination = false;
        }

        protected virtual void ExecuteMovement()
        {
            if (!hasDestination)
            {
                ResetStuckDetection();
                if (characterController != null && !characterController.isGrounded)
                {
                    characterController.Move(Vector3.down * 9.81f * Time.deltaTime);
                }
                return;
            }

            Vector3 currentPos = transform.position;
            Vector3 targetPos = destination;
            targetPos.y = currentPos.y;

            Vector3 moveDir = targetPos - currentPos;
            moveDir.y = 0f;

            if (moveDir.sqrMagnitude > stoppingDistance * stoppingDistance)
            {
                moveDir.Normalize();
                moveDir = ApplyObstacleDetour(moveDir, currentPos);

                // Smooth rotation
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }

                Vector3 velocity = moveDir * moveSpeed;
                velocity.y = -9.81f;

                if (characterController != null)
                {
                    characterController.Move(velocity * Time.deltaTime);
                }
            }
            else
            {
                hasDestination = false;
            }
        }

        /// <summary>
        /// Without pathfinding a unit walking straight at its destination can get pinned against
        /// tents, campfires, walls or trees. If it makes almost no progress, it side-steps along the
        /// obstacle for a moment, alternating sides on repeated failures.
        /// </summary>
        private Vector3 ApplyObstacleDetour(Vector3 desiredDir, Vector3 currentPos)
        {
            float dt = Time.deltaTime;

            if (detourTimer > 0f)
            {
                detourTimer -= dt;
                if (detourTimer <= 0f) ResetStuckDetection();
                return detourDir;
            }

            stuckTimer += dt;
            if (stuckTimer >= stuckCheckInterval)
            {
                Vector3 delta = currentPos - lastProgressPos;
                delta.y = 0f;
                float expected = moveSpeed * stuckTimer;

                if (delta.magnitude < expected * stuckProgressRatio)
                {
                    detourDir = Quaternion.Euler(0f, 90f * detourSide, 0f) * desiredDir;
                    detourSide = -detourSide;
                    detourTimer = detourDuration;
                    return detourDir;
                }

                ResetStuckDetection();
            }

            return desiredDir;
        }

        private void ResetStuckDetection()
        {
            stuckTimer = 0f;
            lastProgressPos = transform.position;
        }
    }
}
