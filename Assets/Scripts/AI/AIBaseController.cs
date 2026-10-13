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

        [Header("Obstacle Avoidance (no NavMesh: movement is straight-line)")]
        [SerializeField] private float avoidanceProbeDistance = 1.2f;
        [SerializeField] private float avoidanceRecheckInterval = 0.1f;
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
        private int consecutiveDetours;

        private static readonly float[] SteerAngles = { 35f, 70f, 105f, 140f };
        private static readonly RaycastHit[] probeHits = new RaycastHit[16];
        private Vector3 steerDir;
        private float steerTimer;
        private int steerSide = 1;

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
            // Spawners add the CharacterController and only then move the object into place. With
            // Physics.autoSyncTransforms off, the first CharacterController.Move() would use the stale
            // physics position and snap the unit back to where it was created (the world origin).
            Physics.SyncTransforms();

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
                float remainingDistance = moveDir.magnitude;
                moveDir.Normalize();
                moveDir = SteerAroundObstacles(moveDir, remainingDistance);
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
        /// Local obstacle avoidance: probes ahead with the unit's capsule and, if the direct way is blocked,
        /// picks the closest free direction (trying the side it turned to last time first, to avoid
        /// oscillating). If it is boxed in, it backs out. Re-evaluated ~10 times per second.
        /// </summary>
        private Vector3 SteerAroundObstacles(Vector3 desiredDir, float remainingDistance)
        {
            steerTimer -= Time.deltaTime;
            if (steerTimer > 0f && steerDir != Vector3.zero) return steerDir;
            steerTimer = avoidanceRecheckInterval;

            // Never probe past the destination, so units can still walk right up to what they are approaching.
            float probeDistance = Mathf.Min(avoidanceProbeDistance, remainingDistance);
            if (IsPathClear(desiredDir, probeDistance))
            {
                steerDir = desiredDir;
                return steerDir;
            }

            for (int i = 0; i < SteerAngles.Length; i++)
            {
                Vector3 sameSide = Quaternion.Euler(0f, SteerAngles[i] * steerSide, 0f) * desiredDir;
                if (IsPathClear(sameSide, avoidanceProbeDistance))
                {
                    steerDir = sameSide;
                    return steerDir;
                }

                Vector3 otherSide = Quaternion.Euler(0f, -SteerAngles[i] * steerSide, 0f) * desiredDir;
                if (IsPathClear(otherSide, avoidanceProbeDistance))
                {
                    steerSide = -steerSide;
                    steerDir = otherSide;
                    return steerDir;
                }
            }

            steerDir = -desiredDir; // boxed in: back out
            return steerDir;
        }

        private bool IsPathClear(Vector3 dir, float distance)
        {
            CharacterController cc = CharacterCtrl;
            if (cc == null) return true;

            // Slightly thinner than the controller so surfaces it is already touching are not "inside" the cast,
            // and lifted by the step offset so the ground and small bumps are ignored.
            float radius = cc.radius * 0.9f;
            Vector3 center = transform.position + cc.center;
            float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            Vector3 bottom = center + Vector3.down * half + Vector3.up * cc.stepOffset;
            Vector3 top = center + Vector3.up * half;
            if (bottom.y > top.y) bottom.y = top.y;

            float walkableNormalY = Mathf.Cos(cc.slopeLimit * Mathf.Deg2Rad);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, dir, probeHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = probeHits[i];
                if (hit.distance <= 0f) continue;                         // overlapping at start
                if (hit.collider.transform.IsChildOf(transform)) continue; // own colliders
                if (hit.collider is CharacterController) continue;         // other units push/slide on their own
                if (IsTerrain(hit.collider) && hit.normal.y >= walkableNormalY) continue; // walkable ground or slope
                return false;                                              // props (campfire, tents, trees...) always block
            }
            return true;
        }

        private static bool IsTerrain(Collider c)
        {
            if (c is TerrainCollider) return true;
            GameObject go = c.gameObject;
            return go.GetComponent<BattleOfAges.World.ProceduralBiomeChunk>() != null
                   || go.name == "SteppeTerrain"
                   || go.name == "Ground";
        }

        /// <summary>
        /// Fallback for when steering still makes no progress (e.g. concave spots): it side-steps along the
        /// obstacle. It keeps to the same side (with a longer side-step the second time) and only switches
        /// sides when the side-step itself is blocked. If it is still stuck after that, the destination is
        /// treated as unreachable and dropped, so the AI state picks a new one instead of the unit drifting
        /// further and further away.
        /// </summary>
        private Vector3 ApplyObstacleDetour(Vector3 desiredDir, Vector3 currentPos)
        {
            const int maxDetoursPerDestination = 2;

            float dt = Time.deltaTime;
            bool detouring = detourTimer > 0f;
            if (detouring) detourTimer -= dt;

            stuckTimer += dt;
            if (stuckTimer >= stuckCheckInterval)
            {
                Vector3 delta = currentPos - lastProgressPos;
                delta.y = 0f;
                bool blocked = delta.magnitude < moveSpeed * stuckTimer * stuckProgressRatio;
                ResetStuckDetection();

                if (blocked)
                {
                    if (consecutiveDetours >= maxDetoursPerDestination)
                    {
                        consecutiveDetours = 0;
                        detourTimer = 0f;
                        hasDestination = false; // unreachable: let the state choose another destination
                        return desiredDir;
                    }

                    if (detouring) detourSide = -detourSide; // this side is blocked too: try the other one
                    consecutiveDetours++;
                    detourTimer = detourDuration * consecutiveDetours;
                    detourDir = Quaternion.Euler(0f, 90f * detourSide, 0f) * desiredDir;
                    return detourDir;
                }

                if (!detouring) consecutiveDetours = 0; // free movement again
            }

            return detourTimer > 0f ? detourDir : desiredDir;
        }

        private void ResetStuckDetection()
        {
            stuckTimer = 0f;
            lastProgressPos = transform.position;
        }
    }
}
