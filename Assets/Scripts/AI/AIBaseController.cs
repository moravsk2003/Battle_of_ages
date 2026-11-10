using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.World;

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

        /// <summary>
        /// Movement LOD: beyond mediumDistance from the viewer a unit moves every 2nd frame, beyond distantDistance every 4th,
        /// by the time gone since (its step longer, the same pace). Movement was most of an NPC's cost (~9 us a frame, idle or
        /// not): fifty grown kingdoms real ran at 13 FPS (KingdomLoadProbe).
        /// </summary>
        public const int MidMoveStride = 2, FarMoveStride = 4;
        private float moveClock;
        private int moveSlot;
        private static int viewerFrame = -1;
        private static Vector3 viewerCamera;
        private static bool hasViewerCamera;

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
        public float MoveSpeed => CurrentMoveSpeed;
        /// <summary>Speed used for movement; characters follow CharacterSpeed (HostileMobAI), wildlife its own value.</summary>
        protected virtual float CurrentMoveSpeed => moveSpeed;
        public Vector3 Destination => destination;
        public bool HasDestination => hasDestination;

        public void TickAI(float deltaTime)
        {
            if (currentState != null)
            {
                currentState.UpdateState(this, deltaTime);
            }
        }

        private int gridId = -1;
        private float settleTimer;

        /// <summary>Team in UnitSpatialGrid (raiders by default; animals are neutral).</summary>
        protected virtual int GridTeam => UnitSpatialGrid.TeamHostile;
        public int GridId => gridId;

        protected virtual void Awake()
        {
            characterController = GetComponent<CharacterController>();
            healthComponent = GetComponent<HealthComponent>();
            combatUnit = GetComponent<CombatUnit>();
            UnitCorpse.AttachTo(gameObject);
            if (states.Count == 0) RegisterStates();
        }

        protected virtual void OnEnable()
        {
            if (gridId < 0) gridId = UnitSpatialGrid.Register(this, Health, GridTeam, CharacterCtrl != null ? CharacterCtrl.radius : 0.5f);
            if (Combat != null) Combat.Team = GridTeam; // its bolts (an NPC mage) look for its enemies
            CrowdSim.Add(this);
        }

        // ------------------------------------------------------------------ in the crowd (CrowdSim moves every unit in one job)

        internal int crowdIndex = -1;
        /// <summary>Its body drawn on the GPU far off: the crowd's job says when its size on screen or its pace changed.</summary>
        internal Utils.GpuSkinnedCharacter crowdLook;
        private bool needsRoute;
        /// <summary>The job may move it: no road round a town's ring to follow, and nothing else moving it (a climb).</summary>
        internal bool CrowdFast => !needsRoute && CrowdCanGlide;
        protected virtual bool CrowdCanGlide => true;
        /// <summary>It has something of its own to do every frame (an animal's legs): CrowdEveryFrame is called.</summary>
        internal virtual bool CrowdWantsEveryFrame => false;
        protected internal virtual void CrowdEveryFrame(float dt) { }
        internal float CrowdSpeed => CurrentMoveSpeed;
        internal float CrowdStop => stoppingDistance;
        internal float CrowdTurn => rotationSpeed;
        internal float CrowdClose => closeDistance;
        internal float CrowdMid => mediumDistance;
        internal float CrowdFar => distantDistance;
        internal int CrowdTeam => GridTeam;
        /// <summary>Its speed, its climb or its side changed: the crowd is told.</summary>
        protected void CrowdChanged() => CrowdSim.Push(this);

        internal void CrowdThink(float elapsed)
        {
            if (currentState != null) currentState.UpdateState(this, elapsed);
            CrowdSim.Push(this);
        }

        internal void CrowdManagedMove(float dt)
        {
            ExecuteMovement(dt);
            CrowdSim.Push(this);
        }

        internal void CrowdArrived()
        {
            hasDestination = false;
            CrowdSim.Push(this);
        }

        protected virtual void OnDisable()
        {
            CrowdSim.Remove(this);
            UnitSpatialGrid.Unregister(gridId);
            gridId = -1;
        }

        /// <summary>Moves the unit to its current GridTeam in the unit grid (a team given after it was spawned).</summary>
        protected void RefreshGridTeam()
        {
            if (gridId >= 0)
            {
                UnitSpatialGrid.Unregister(gridId);
                gridId = UnitSpatialGrid.Register(this, Health, GridTeam, CharacterCtrl != null ? CharacterCtrl.radius : 0.5f);
            }
            if (Combat != null) Combat.Team = GridTeam;
            CrowdSim.Refresh(this);
        }

        protected virtual void Start()
        {
            moveSlot = Mathf.Abs(GetInstanceID()) % FarMoveStride; // the far ones spread over the frames
            // Movement is kinematic (GroundMotor), so there is no CharacterController.Move that could snap back to a stale
            // physics position; the old per-unit Physics.SyncTransforms() made spawning 1000 units quadratic.
            FindPlayerViewer();
            if (states.Count == 0) RegisterStates();
        }

        protected abstract void RegisterStates();

        private void FindPlayerViewer()
        {
            if (playerViewer == null)
            {
                GameObject player = Managers.GameContext.Player;
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

        /// <summary>
        /// The unit's frame by itself, as it was before CrowdSim: used where there is no endless world under it (a bare test
        /// scene) or with CrowdSim.Enabled off. There is no Update here: ten thousand of them cost 10 ms a frame in calls alone.
        /// </summary>
        internal void ManagedUpdate()
        {
            ManagedFrame();
            CrowdEveryFrame(Time.deltaTime);
        }

        private void ManagedFrame()
        {
            if (healthComponent != null && !healthComponent.IsAlive)
            {
                return;
            }

            if (playerViewer == null)
            {
                FindPlayerViewer();
            }

            // Distance to the viewer for the AI LOD: the hero, or the camera over the ground (on the map it looks down from
            // ~100 m, anywhere: units under it are near)
            float distToViewer = ViewerDistance();

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

            // Physical movement: every frame near the viewer, every 2nd or 4th frame further off (each unit on its own frame)
            int stride = distToViewer < mediumDistance ? 1 : distToViewer < distantDistance ? MidMoveStride : FarMoveStride;
            moveClock += Time.deltaTime;
            if (stride == 1 || (Time.frameCount + moveSlot) % stride == 0)
            {
                float dt = Mathf.Min(moveClock, 0.25f);
                moveClock = 0f;
                ExecuteMovement(dt);
            }
        }

        private float ViewerDistance()
        {
            if (viewerFrame != Time.frameCount)
            {
                viewerFrame = Time.frameCount;
                Camera cam = Utils.ViewCamera.Current;
                hasViewerCamera = cam != null;
                if (hasViewerCamera) viewerCamera = cam.transform.position;
            }
            Vector3 p = transform.position;
            float d = playerViewer != null ? Vector3.Distance(p, playerViewer.position) : 10f;
            if (hasViewerCamera)
            {
                float dx = p.x - viewerCamera.x, dz = p.z - viewerCamera.z;
                d = Mathf.Min(d, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return d;
        }

        public void SetMoveDestination(Vector3 targetPos)
        {
            destination = targetPos;
            destination.y = RtsUnit.GetTerrainHeight(destination);
            hasDestination = true;
            if (crowdIndex >= 0)
            {
                // A road round a town's ring is walked by the old code (KingdomTown.Route, asked every step)
                Vector3 via = KingdomTown.Route(transform.position, destination);
                needsRoute = via.x != destination.x || via.z != destination.z;
                CrowdSim.Push(this);
            }
        }

        public void StopMovement()
        {
            hasDestination = false;
            if (crowdIndex >= 0) CrowdSim.Push(this);
        }

        /// <summary>Moves the unit by the time gone since its last move (one frame near the viewer, a few further off).</summary>
        protected virtual void ExecuteMovement(float dt)
        {
            float radius = characterController != null ? characterController.radius : 0.5f;
            if (!hasDestination)
            {
                ResetStuckDetection();
                // Idle: only get out of the way of overlapping neighbours; re-seat on the ground a few times a second.
                GroundMotor.Move(transform, gridId, Vector3.zero, radius, dt, GridTeam == UnitSpatialGrid.TeamPlayer);
                settleTimer -= dt;
                if (settleTimer <= 0f) { settleTimer = 0.3f; GroundMotor.Settle(transform); }
                return;
            }

            Vector3 currentPos = transform.position;
            Vector3 targetPos = KingdomTown.Route(currentPos, destination);
            targetPos.y = currentPos.y;

            Vector3 moveDir = targetPos - currentPos;
            moveDir.y = 0f;

            if (moveDir.sqrMagnitude > stoppingDistance * stoppingDistance)
            {
                float remainingDistance = moveDir.magnitude;
                moveDir.Normalize();
                moveDir = SteerAroundObstacles(moveDir, remainingDistance, dt);
                moveDir = ApplyObstacleDetour(moveDir, currentPos, dt);

                // Smooth rotation
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Mathf.Min(1f, rotationSpeed * dt));
                }

                GroundMotor.Move(transform, gridId, moveDir * CurrentMoveSpeed * BattleFront.Factor(GridTeam, moveDir), radius, dt, GridTeam == UnitSpatialGrid.TeamPlayer);
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
        private Vector3 SteerAroundObstacles(Vector3 desiredDir, float remainingDistance, float dt)
        {
            steerTimer -= dt;
            if (steerTimer > 0f && steerDir != Vector3.zero) return steerDir;
            steerTimer = avoidanceRecheckInterval;

            // Never probe past the destination, so units can still walk right up to what they are approaching.
            float probeDistance = Mathf.Min(avoidanceProbeDistance, remainingDistance);
            if (IsPathClear(desiredDir, probeDistance))
            {
                steerDir = desiredDir;
                return steerDir;
            }

            steerDir = SteerRound(desiredDir);
            return steerDir;
        }

        /// <summary>The way ahead is blocked (the crowd's probe found it so): the direction to take instead.</summary>
        internal Vector3 CrowdSteerBlocked(Vector3 desiredDir) => SteerRound(desiredDir);

        /// <summary>The closest free direction to the blocked one, the side it turned to last time first; boxed in, it backs out.</summary>
        private Vector3 SteerRound(Vector3 desiredDir)
        {
            for (int i = 0; i < SteerAngles.Length; i++)
            {
                Vector3 sameSide = Quaternion.Euler(0f, SteerAngles[i] * steerSide, 0f) * desiredDir;
                if (IsPathClear(sameSide, avoidanceProbeDistance)) return sameSide;

                Vector3 otherSide = Quaternion.Euler(0f, -SteerAngles[i] * steerSide, 0f) * desiredDir;
                if (IsPathClear(otherSide, avoidanceProbeDistance))
                {
                    steerSide = -steerSide;
                    return otherSide;
                }
            }

            return -desiredDir; // boxed in: back out
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
                if (Blocks(probeHits[i], probeHits[i].collider, transform, walkableNormalY)) return false;
            }
            return true;
        }

        /// <summary>What a probe ahead ran into stops the unit: not itself, other units or ground it can walk up; props always do.</summary>
        internal static bool Blocks(RaycastHit hit, Collider c, Transform self, float walkableNormalY)
        {
            if (hit.distance <= 0f) return false;                 // overlapping at start
            if (c is CharacterController) return false;           // other units push/slide on their own
            if (c.transform.IsChildOf(self)) return false;        // own colliders
            if (IsTerrain(c) && hit.normal.y >= walkableNormalY) return false; // walkable ground or slope
            return true;                                          // props (campfire, tents, trees...) always block
        }

        private static readonly Dictionary<int, bool> terrainKnown = new Dictionary<int, bool>();

        private static bool IsTerrain(Collider c)
        {
            if (c is TerrainCollider) return true;
            // Asked once per collider: the name is a new string each time, and GetComponent made a "missing" object in the
            // editor for every miss (400 KB a frame with ten thousand units)
            int id = c.GetInstanceID();
            if (terrainKnown.TryGetValue(id, out bool known)) return known;
            GameObject go = c.gameObject;
            known = go.TryGetComponent(out ProceduralBiomeChunk _) || go.name == "SteppeTerrain" || go.name == "Ground";
            terrainKnown[id] = known;
            return known;
        }

        /// <summary>
        /// Fallback for when steering still makes no progress (e.g. concave spots): it side-steps along the
        /// obstacle. It keeps to the same side (with a longer side-step the second time) and only switches
        /// sides when the side-step itself is blocked. If it is still stuck after that, the destination is
        /// treated as unreachable and dropped, so the AI state picks a new one instead of the unit drifting
        /// further and further away.
        /// </summary>
        private Vector3 ApplyObstacleDetour(Vector3 desiredDir, Vector3 currentPos, float dt)
        {
            const int maxDetoursPerDestination = 2;

            bool detouring = detourTimer > 0f;
            if (detouring) detourTimer -= dt;

            stuckTimer += dt;
            if (stuckTimer >= stuckCheckInterval)
            {
                Vector3 delta = currentPos - lastProgressPos;
                delta.y = 0f;
                bool blocked = delta.magnitude < CurrentMoveSpeed * stuckTimer * stuckProgressRatio;
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
