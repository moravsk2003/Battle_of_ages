using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Player;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.RTS
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public class RtsUnit : MonoBehaviour
    {
        private const float STOPPING_DISTANCE_SQR = 0.25f;

        [Header("Selection Visuals")]
        [SerializeField] private GameObject selectionIndicator;

        [Header("Movement Configuration")]
        [SerializeField] private float rotationSpeed = 10.0f;

        private CharacterController characterController;
        private Vector3 targetPosition;
        private bool hasTarget = false;
        private static Collider cachedTerrainCollider;

        // Anti-stuck & obstacle avoidance state
        private Vector3 lastPos;
        private float stuckTimer = 0f;
        private float avoidSideSign = 1.0f; // +1 right, -1 left

        // Scale: kinematic movement (GroundMotor), steering probes 10x per second, spatial grid for engagement
        private const float SteerInterval = 0.1f, EngageInterval = 0.4f, EngageRadius = 14f;
        private bool isPlayerControlled;   // the hero keeps CharacterController physics
        private NpcResourceGatherer gatherer;
        /// <summary>Busy with a job: working units do not go looking for fights (soldiers are units without a job).</summary>
        private bool IsWorking => gatherer != null && gatherer.IsWorking;
        public bool IsHero => isPlayerControlled;
        /// <summary>Made by BuildingNode.CreateWorkerNpc (a settlement's villager, rebuilt from the save), not a scene unit.</summary>
        public bool IsVillager { get; set; }
        public bool IsAlive => health == null || health.IsAlive;
        public bool InsideMine => gatherer != null && gatherer.IsInsideMine;
        private int gridId = -1;
        private Vector3 steerDir, steerDesired;
        private float steerTimer, steerElapsed, engageTimer, settleTimer;
        private float routeTimer;
        private Vector3 routeVia, routeFor;
        private bool routeInGateway;
        private HealthComponent health;
        private CombatUnit combat;
        /// <summary>CharacterSpeed formula: Global x modifier x sqrt(strength) (workers without a CombatUnit move like a person).</summary>
        private float MoveSpeed => combat != null ? combat.MoveSpeed : CharacterSpeed.Of(null);
        private bool touchingSolidNearTarget;

        public bool IsSelected { get; private set; }
        /// <summary>Ordered to attack or engaged in a fight (the job waits meanwhile).</summary>
        public bool HasAttackTarget => attackTarget != null;

        private void Awake()
        {
            isPlayerControlled = GetComponent<FirstPersonController>() != null;
            // Villagers placed in the scene by hand (not made by CreateWorkerNpc) are known by their name: the one place left that reads it
            if (!IsVillager && name.StartsWith("NPC_Villager")) IsVillager = true;
            health = GetComponent<HealthComponent>();
            combat = GetComponent<CombatUnit>();
            if (!isPlayerControlled)
            {
                // Every unit can fight (docs/GAME_DESIGN.md §11.1), also villagers placed in the scene
                if (health == null) health = gameObject.AddComponent<HealthComponent>();
                if (combat == null) combat = gameObject.AddComponent<CombatUnit>();
                UnitCorpse.AttachTo(gameObject);
            }
            engageTimer = Random.value * EngageInterval; // stagger the searches across frames
            characterController = GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.stepOffset = 0.55f;
                characterController.slopeLimit = 55.0f;
                characterController.center = new Vector3(0, 1.0f, 0);
                characterController.height = 2.0f;
                characterController.radius = 0.40f;
            }

            targetPosition = transform.position;
            SetupSelectionIndicatorVisuals();
        }

        private void Start()
        {
            SnapToTerrain();
            lastPos = transform.position;
            gatherer = GetComponent<NpcResourceGatherer>();
            if (!isPlayerControlled)
            {
                // One kind of unit (docs/GAME_DESIGN.md §11.1): a soldier works as a villager does, a villager fights as a soldier
                // does; one stands by only when told to stand somewhere (RtsUnitCommander.MoveInFormation)
                if (gatherer == null) gatherer = gameObject.AddComponent<NpcResourceGatherer>();
                EnsureStartingKit();
                CentralEmpire.ApplyTo(gameObject); // the empire's units wear its gear and do not work
            }
        }

        /// <summary>
        /// Own units get the kit their model is drawn with (knight: steel set, villager: axe) on top of the base stats
        /// of their rank (knight model: knight, otherwise a person).
        /// </summary>
        private void EnsureStartingKit()
        {
            var visual = GetComponentInChildren<CharacterGear>(true);
            var type = visual != null ? visual.Type
                : name.Contains("Knight") ? HumanoidModelBuilder.HumanType.KnightGuard : HumanoidModelBuilder.HumanType.Villager;
            if (GetComponent<Equipment>() == null) gameObject.AddComponent<Equipment>().LoadSlots(StartingKits.For(type));
            if (GetComponent<UnitProgress>() != null) return;
            UnitProgress.Setup(gameObject, type == HumanoidModelBuilder.HumanType.KnightGuard ? UnitRank.Knight : UnitRank.Person);
            if (health != null) health.InitializeHealth(health.MaxHealth);
        }

        private void SetupSelectionIndicatorVisuals()
        {
            if (selectionIndicator == null)
            {
                Transform childInd = transform.Find("SelectionIndicator");
                if (childInd != null) selectionIndicator = childInd.gameObject;
            }

            if (selectionIndicator == null) return;

            selectionIndicator.SetActive(false);
            selectionIndicator.transform.localPosition = new Vector3(0f, 0.15f, 0f);

            MeshRenderer mr = selectionIndicator.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                Shader unlitShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
                Material mat = new Material(unlitShader);
                mat.color = new Color(0.2f, 0.85f, 1.0f, 0.85f);
                
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
                
                mr.material = mat;
            }
        }

        private void OnEnable()
        {
            UnitRegistry.Register(this);
            if (gridId < 0)
                gridId = UnitSpatialGrid.Register(this, GetComponent<HealthComponent>(), UnitSpatialGrid.TeamPlayer,
                                                  characterController != null ? characterController.radius : 0.4f);
        }

        private void OnDisable()
        {
            UnitRegistry.Unregister(this);
            UnitSpatialGrid.Unregister(gridId);
            Building.WallWalk.Release(this);
            gridId = -1;
        }

        private HealthComponent attackTarget;
        private CombatUnit combatUnit;

        /// <summary>
        /// Attack this target. ordered: the player's order — the unit follows it anywhere. Otherwise (a
        /// fight it picked itself, a worker hitting back) it is on a leash: it gives up past the leash from where the
        /// fight began and goes back there (GameBalance.UnitLeashRadius, WorkerLeashRadius).
        /// </summary>
        public void IssueAttackCommand(HealthComponent target, bool ordered = true)
        {
            bool keepLeash = !ordered && leashed; // the next enemy of the same fight keeps where the fight began
            bool keepDefending = keepLeash && defending;
            Vector3 keepOrigin = leashOrigin;
            if (target != null && wallState == WallState.Posted && combatUnit != null
                && Vector3.Distance(transform.position, target.transform.position) <= combatUnit.AttackRange)
            {
                attackTarget = target; // in reach from the post: stays up there
                return;
            }
            if (target != null)
            {
                IssueMoveCommand(target.transform.position); // clears the previous attack target...
            }
            if (OnWall) leaveTarget = target; // after coming down
            else attackTarget = target;       // ...so the new one is set afterwards (was lost before)
            if (ordered) leashed = false;
            else if (keepLeash) { leashed = true; leashOrigin = keepOrigin; defending = keepDefending; }
            else StartLeash();
        }

        /// <summary>The alarm's order: at this enemy, but only as far as AlarmChaseRadius from home - a raid that runs is let go.</summary>
        public void IssueDefendCommand(HealthComponent target, Vector3 home)
        {
            IssueAttackCommand(target);
            leashed = true;
            leashOrigin = home;
            defending = true;
        }

        /// <summary>The alarm is over: a fight it ordered ends with it.</summary>
        public void StopDefending()
        {
            if (!defending) return;
            attackTarget = null;
            hasTarget = false;
            EndLeash();
        }

        // ------------------------------------------------------------------ the leash

        private bool leashed, defending;
        private Vector3 leashOrigin;

        /// <summary>On a leash in a fight it picked itself: the point it will not chase further than the leash from.</summary>
        public bool Leashed => leashed;
        public Vector3 LeashOrigin => leashOrigin;
        public float LeashRadius => defending ? GameBalance.AlarmChaseRadius : IsWorking && !SettlementAlarm.ActiveAt(transform.position) ? GameBalance.WorkerLeashRadius : GameBalance.UnitLeashRadius;

        private void StartLeash()
        {
            if (leashed) return; // the next enemy of the same fight: the leash stays where the fight began
            leashed = true;
            leashOrigin = transform.position;
        }

        /// <summary>The fight is over (won or given up): a soldier walks back to where it began; a worker's job takes it on.</summary>
        private void EndLeash()
        {
            if (!leashed) return;
            leashed = false;
            defending = false;
            if (!IsWorking && FlatDistance(transform.position, leashOrigin) > 2f) IssueMoveCommand(leashOrigin);
        }

        // ------------------------------------------------------------------ on the walls (§10.5)

        private enum WallState { None, ToStairs, Climbing, Posted, Leaving }
        private WallState wallState;
        private readonly System.Collections.Generic.List<Vector3> wallPath = new System.Collections.Generic.List<Vector3>();
        private int wallLeg;
        private bool leaveToMove;
        private Vector3 leaveTo;
        private HealthComponent leaveTarget;

        /// <summary>On the stairs, the walkway or its post (moved along the way, not by the motor).</summary>
        public bool OnWall => wallState >= WallState.Climbing;
        public bool IsPostedOnWall => wallState == WallState.Posted;
        /// <summary>Going up to the walls or on them: its job and the alarm leave it be.</summary>
        public bool WallBound => wallState != WallState.None;
        public bool IsRanged => combatUnit != null && combatUnit.IsRanged;

        /// <summary>The alarm is over: one it sent up comes down (one on the way there turns back).</summary>
        public void StandDownFromWall()
        {
            if (!PostedByAlarm) return;
            if (wallState == WallState.ToStairs) { wallState = WallState.None; Building.WallWalk.Release(this); hasTarget = false; }
            else LeaveWall();
            PostedByAlarm = false;
        }
        /// <summary>Sent up by the alarm (comes down when it stands down).</summary>
        public bool PostedByAlarm { get; private set; }

        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        /// <summary>Sends the unit to a post on the walls: to the foot of the stairs, up and along the walkway.</summary>
        public bool SendToWall(Building.WallWalk.Post post, bool byAlarm = false)
        {
            if (isPlayerControlled || OnWall || !post.Valid) return false;
            if (!Building.WallWalk.PathUp(post, wallPath)) return false;
            Building.WallWalk.Reserve(post, this);
            attackTarget = null;
            targetPosition = wallPath[0];
            hasTarget = true;
            stuckTimer = 0f;
            wallState = WallState.ToStairs;
            PostedByAlarm = byAlarm;
            return true;
        }

        /// <summary>Down from the walls the way it came, then on to a place (or the target set meanwhile).</summary>
        public void LeaveWall()
        {
            if (!OnWall || wallState == WallState.Leaving) return; // (already on the way down: only where to next changed)
            if (wallState == WallState.Posted) // back along the way up: from the post to the foot of the stairs
            {
                wallPath.Reverse();
                wallLeg = 0;
            }
            else wallPath.RemoveRange(wallLeg + 1, wallPath.Count - wallLeg - 1); // turn back where it is on the way up
            if (wallState == WallState.Climbing) { wallPath.Reverse(); wallLeg = wallPath.Count - 1 - wallLeg; }
            wallState = WallState.Leaving;
            attackTarget = null;
            if (combatUnit != null) combatUnit.OnWall = false;
            Building.WallWalk.Release(this);
        }

        private void UpdateOnWall()
        {
            if (wallState == WallState.Posted) { DefendFromPost(); return; }
            // Along the way: the stairs and the walkway at walking pace
            if (wallLeg >= wallPath.Count - 1)
            {
                if (wallState == WallState.Climbing)
                {
                    wallState = WallState.Posted;
                    if (combatUnit != null) combatUnit.OnWall = true;
                }
                else
                {
                    wallState = WallState.None;
                    PostedByAlarm = false;
                    hasTarget = false;
                    GroundMotor.Settle(transform);
                    if (leaveTarget != null && leaveTarget.IsAlive) IssueAttackCommand(leaveTarget);
                    else if (leaveToMove) IssueMoveCommand(leaveTo);
                    leaveTarget = null;
                    leaveToMove = false;
                }
                return;
            }
            Vector3 next = wallPath[wallLeg + 1];
            Vector3 step = Vector3.MoveTowards(transform.position, next, MoveSpeed * 0.8f * Time.deltaTime);
            Vector3 dir = next - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), rotationSpeed * Time.deltaTime);
            transform.position = step;
            if ((step - next).sqrMagnitude < 1e-4f) wallLeg++;
        }

        /// <summary>At its post: fights whatever comes within reach (from the walls: further, harder), never steps down for it.</summary>
        private void DefendFromPost()
        {
            if (combatUnit == null) return;
            float range = combatUnit.AttackRange;
            if (attackTarget != null && (!attackTarget.IsAlive || Vector3.Distance(transform.position, attackTarget.transform.position) > range + 0.5f)) attackTarget = null;
            if (attackTarget == null)
            {
                engageTimer -= Time.deltaTime;
                if (engageTimer > 0f) return;
                engageTimer = EngageInterval;
                int enemy = UnitSpatialGrid.FindNearestEnemy(transform.position, range, UnitSpatialGrid.TeamPlayer);
                if (enemy < 0) return;
                attackTarget = UnitSpatialGrid.Health(enemy);
                if (attackTarget == null) return;
            }
            Vector3 look = attackTarget.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), rotationSpeed * Time.deltaTime);
            combatUnit.TryAttack(attackTarget);
        }

        private void Update()
        {
            if (health != null && !health.IsAlive) return; // fallen units neither walk nor fight
            if (combatUnit == null) combatUnit = GetComponent<CombatUnit>();
            if (wallState == WallState.ToStairs && FlatDistance(transform.position, wallPath[0]) < 1.2f) { wallState = WallState.Climbing; wallLeg = 0; hasTarget = false; }
            if (wallState >= WallState.Climbing) { UpdateOnWall(); return; }

            // Soldiers (and everyone during an alarm) engage raiders that come near on their own; a worker goes for an
            // enemy close to it (one kind of unit: people fight too), even on the way to work. Not the hero, not in a mine.
            bool soldier = !IsWorking || SettlementAlarm.ActiveAt(transform.position);
            if (!isPlayerControlled && !InsideMine && combatUnit != null && attackTarget == null && (!hasTarget || !soldier))
            {
                engageTimer -= Time.deltaTime;
                if (engageTimer <= 0f)
                {
                    engageTimer = EngageInterval;
                    int enemy = UnitSpatialGrid.FindNearestEnemy(transform.position, soldier ? EngageRadius : GameBalance.WorkerEngageRadius, UnitSpatialGrid.TeamPlayer);
                    if (enemy >= 0) { attackTarget = UnitSpatialGrid.Health(enemy); if (attackTarget != null) StartLeash(); }
                }
            }

            // Attack Target tracking logic
            if (attackTarget != null)
            {
                if (!attackTarget.IsAlive)
                {
                    attackTarget = null;
                    UnitProgress progress = GetComponent<UnitProgress>();
                    if (progress != null) progress.OnFightSurvived();
                    EndLeash();
                }
                else if (leashed && FlatDistance(transform.position, leashOrigin) > LeashRadius)
                {
                    // Too far from where the fight began: let it run, go back
                    attackTarget = null;
                    hasTarget = false;
                    EndLeash();
                }
                else
                {
                    float distToTarget = Vector3.Distance(transform.position, attackTarget.transform.position);
                    float range = (combatUnit != null) ? combatUnit.AttackRange : 2.5f;

                    if (distToTarget <= range)
                    {
                        // Stop moving, face target and attack
                        hasTarget = false;

                        Vector3 lookDir = attackTarget.transform.position - transform.position;
                        lookDir.y = 0f;
                        if (lookDir.sqrMagnitude > 0.01f)
                        {
                            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(lookDir), rotationSpeed * Time.deltaTime);
                        }

                        if (combatUnit != null)
                        {
                            combatUnit.TryAttack(attackTarget);
                        }
                    }
                    else
                    {
                        // Move closer to attack target
                        targetPosition = attackTarget.transform.position;
                        hasTarget = true;
                    }
                }
            }

            float radius = characterController != null ? characterController.radius : 0.4f;
            if (!hasTarget)
            {
                if (isPlayerControlled)
                {
                    // Stick to ground when idle
                    if (characterController != null && !characterController.isGrounded)
                    {
                        characterController.Move(Vector3.down * 9.81f * Time.deltaTime);
                    }
                    return;
                }
                // Idle unit: step aside for overlapping neighbours, stay on the ground
                GroundMotor.Move(transform, gridId, Vector3.zero, radius, Time.deltaTime, true);
                settleTimer -= Time.deltaTime;
                if (settleTimer <= 0f) { settleTimer = 0.3f; GroundMotor.Settle(transform); }
                return;
            }

            Vector3 currentPos = transform.position;
            Vector3 destPos = targetPosition;
            destPos.y = currentPos.y;

            Vector3 moveDir = destPos - currentPos;
            moveDir.y = 0f;

            if (moveDir.sqrMagnitude > STOPPING_DISTANCE_SQR)
            {
                bool inGateway = false;
                if (!isPlayerControlled)
                {
                    // Across an own wall: by way of the gates and round the walls' ends (§10.5); asked again 4 times a
                    // second, at once at a new target or on reaching the waypoint
                    routeTimer -= Time.deltaTime;
                    if (routeTimer <= 0f || routeFor != targetPosition || (routeVia - currentPos).sqrMagnitude < 0.36f)
                    {
                        routeVia = Building.WallBarriers.Route(currentPos, targetPosition, out routeInGateway);
                        routeFor = targetPosition;
                        routeTimer = 0.25f;
                    }
                    inGateway = routeInGateway;
                    Vector3 via = routeVia, toVia = via - currentPos;
                    toVia.y = 0f;
                    if ((via.x != targetPosition.x || via.z != targetPosition.z) && toVia.sqrMagnitude > 0.04f) moveDir = toVia;
                }
                moveDir.Normalize();

                // 1. Multi-whisker obstacle avoidance & anti-stuck steering (3 sphere casts): 10 times per second for
                //    units, re-evaluated at once when the wanted direction turns noticeably (new target, flanking).
                steerTimer -= Time.deltaTime;
                steerElapsed += Time.deltaTime;
                if (isPlayerControlled || steerTimer <= 0f || steerDir == Vector3.zero || Vector3.Dot(moveDir, steerDesired) < 0.9f)
                {
                    steerDir = CalculateAvoidanceDirection(currentPos, moveDir, isPlayerControlled ? Time.deltaTime : steerElapsed);
                    steerDesired = moveDir;
                    steerTimer = SteerInterval;
                    steerElapsed = 0f;
                }
                if (!inGateway) moveDir = steerDir; // in a gateway the way is clear: no probes against the jambs
                if (touchingSolidNearTarget && !inGateway)
                {
                    touchingSolidNearTarget = false;
                    hasTarget = false;
                    stuckTimer = 0f;
                    return;
                }

                // 2. Smooth Rotation towards Steering Direction
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }

                // 3. Movement: the hero uses CharacterController physics, units the kinematic ground motor
                float speed = MoveSpeed * BattleFront.Factor(UnitSpatialGrid.TeamPlayer, moveDir); // the pull of a battle
                Vector3 moveVelocity = moveDir * speed;
                moveVelocity.y = -9.81f;

                if (!isPlayerControlled)
                {
                    GroundMotor.Move(transform, gridId, moveDir * speed, radius, Time.deltaTime, true);
                }
                else if (characterController != null)
                {
                    characterController.Move(moveVelocity * Time.deltaTime);
                }
                else
                {
                    Vector3 nextPos = Vector3.MoveTowards(currentPos, destPos, speed * Time.deltaTime);
                    nextPos.y = GetTerrainHeight(nextPos);
                    transform.position = nextPos;
                }
            }
            else
            {
                hasTarget = false;
                stuckTimer = 0f;
            }
        }

        private Vector3 CalculateAvoidanceDirection(Vector3 currentPos, Vector3 desiredDir, float elapsed)
        {
            Vector3 rayOrigin = currentPos + Vector3.up * 0.8f;
            Vector3 finalDir = desiredDir;

            // Multi-whisker Raycasts (Center 3.2m, Left 35° 2.8m, Right 35° 2.8m)
            bool centerHit = Physics.SphereCast(rayOrigin, 0.40f, desiredDir, out RaycastHit hitCenter, 3.0f);
            
            Quaternion leftRot = Quaternion.Euler(0, -35f, 0);
            Quaternion rightRot = Quaternion.Euler(0, 35f, 0);
            Vector3 leftDir = leftRot * desiredDir;
            Vector3 rightDir = rightRot * desiredDir;

            bool leftHit = Physics.SphereCast(rayOrigin, 0.35f, leftDir, out RaycastHit hitLeft, 2.5f);
            bool rightHit = Physics.SphereCast(rayOrigin, 0.35f, rightDir, out RaycastHit hitRight, 2.5f);

            bool centerBlocked = centerHit && IsValidObstacle(hitCenter);
            // Walked right up to a solid prop next to the destination (tree being chopped, chest, building): arrived.
            // The CharacterController used to stop the unit here; kinematic movement must do it explicitly.
            // (the window covers the distance walked until the next probe, so faster units cannot step past it)
            touchingSolidNearTarget = !isPlayerControlled && centerHit && hitCenter.distance < 0.35f + MoveSpeed * SteerInterval && IsSolid(hitCenter)
                                      && Vector3.Distance(currentPos, targetPosition) < 4f;
            bool leftBlocked = leftHit && IsValidObstacle(hitLeft);
            bool rightBlocked = rightHit && IsValidObstacle(hitRight);

            if (centerBlocked)
            {
                if (!rightBlocked) avoidSideSign = 1.0f;
                else if (!leftBlocked) avoidSideSign = -1.0f;

                Vector3 perpendicularSteer = Vector3.Cross(Vector3.up, desiredDir).normalized * avoidSideSign;
                finalDir = (desiredDir * 0.2f + perpendicularSteer * 1.8f).normalized;
            }
            else if (leftBlocked)
            {
                Vector3 rightSteer = Vector3.Cross(Vector3.up, desiredDir).normalized;
                finalDir = (desiredDir + rightSteer * 1.2f).normalized;
            }
            else if (rightBlocked)
            {
                Vector3 leftSteer = -Vector3.Cross(Vector3.up, desiredDir).normalized;
                finalDir = (desiredDir + leftSteer * 1.2f).normalized;
            }

            // Anti-Stuck Detector: If unit gets pressed against a building wall or obstacle, force lateral crawl & un-stick nudge
            float movedDist = Vector3.Distance(currentPos, lastPos);
            if (movedDist < 0.20f * elapsed * MoveSpeed)
            {
                stuckTimer += elapsed;
                // Held up by a prop straight ahead: round it at once. By nothing solid there, so by other units: press on
                // and shove a while first, and only then go round
                float bypassAfter = centerBlocked ? 0.25f : GameBalance.PressBeforeBypass;
                if (stuckTimer > bypassAfter)
                {
                    if (stuckTimer > bypassAfter + 0.55f && Random.value < 0.1f * Mathf.Max(1f, elapsed / 0.016f)) avoidSideSign = -avoidSideSign;
                    Vector3 wallBypassDir = (Vector3.Cross(Vector3.up, desiredDir).normalized * avoidSideSign + desiredDir * 0.4f).normalized;
                    finalDir = wallBypassDir;

                    if (stuckTimer > bypassAfter + 0.75f)
                    {
                        Vector3 lateralNudge = Vector3.Cross(Vector3.up, desiredDir).normalized * (avoidSideSign * 2.0f * elapsed);
                        if (isPlayerControlled && characterController != null) characterController.Move(lateralNudge);
                        else GroundMotor.Move(transform, gridId, lateralNudge / Mathf.Max(elapsed, 1e-4f), characterController != null ? characterController.radius : 0.4f, elapsed, true);
                    }
                }
            }
            else
            {
                stuckTimer = Mathf.Max(0f, stuckTimer - elapsed * 2f);
            }

            lastPos = currentPos;
            return finalDir;
        }

        /// <summary>Still on its way somewhere.</summary>
        public bool HasMoveTarget => hasTarget;

        private bool IsValidObstacle(RaycastHit hit)
        {
            if (!IsSolid(hit)) return false;

            // If hit object is our attack target or within 2.5m of target destination, do not steer away!
            if (attackTarget != null && (hit.transform.root == attackTarget.transform || hit.transform == attackTarget.transform)) return false;
            if (hasTarget && Vector3.Distance(hit.point, targetPosition) < 2.5f) return false;
            return true;
        }

        /// <summary>A prop the unit cannot walk through (not ground, not a unit, not itself).</summary>
        private bool IsSolid(RaycastHit hit)
        {
            if (hit.collider == null) return false;
            if (hit.collider.isTrigger) return false;
            if (hit.collider is CharacterController) return false; // other units: crowd separation handles them
            if (hit.collider.gameObject == gameObject || hit.transform.root == transform) return false;

            string name = hit.collider.gameObject.name;
            if (name.Contains("SteppeTerrain") || name.Contains("Ground") || name.Contains("Chunk") || name.Contains("Terrain") || name.Contains("SelectionIndicator")) return false;
            // Only the chunk's own ground mesh is terrain; trees and rocks parented to a chunk are obstacles
            // (with kinematic movement nothing else stops a unit from walking through them).
            if (hit.collider is TerrainCollider || hit.collider.GetComponent<ProceduralBiomeChunk>() != null) return false;
            return true;
        }

        public void SnapToTerrain()
        {
            float groundY = GetTerrainHeight(transform.position);
            transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
        }

        public static float GetTerrainHeight(Vector3 worldPos)
        {
            if (InfiniteWorldManager.Instance != null)
            {
                return InfiniteWorldManager.GetSampledTerrainHeight(worldPos.x, worldPos.z);
            }

            if (cachedTerrainCollider == null)
            {
                GameObject terrainObj = GameObject.Find("SteppeTerrain") ?? GameObject.Find("Ground");
                if (terrainObj != null)
                {
                    cachedTerrainCollider = terrainObj.GetComponent<Collider>();
                }
            }

            if (cachedTerrainCollider != null)
            {
                Ray ray = new Ray(new Vector3(worldPos.x, 300f, worldPos.z), Vector3.down);
                if (cachedTerrainCollider.Raycast(ray, out RaycastHit hit, 500f))
                {
                    return hit.point.y;
                }
            }

            return InfiniteWorldManager.GetSampledTerrainHeight(worldPos.x, worldPos.z);
        }

        public void SetSelectionIndicator(GameObject indicator)
        {
            selectionIndicator = indicator;
            SetupSelectionIndicatorVisuals();
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;

            if (selectionIndicator == null)
            {
                Transform indTr = transform.Find("SelectionIndicator");
                if (indTr != null)
                {
                    selectionIndicator = indTr.gameObject;
                    SetupSelectionIndicatorVisuals();
                }
            }

            // The map's TeamMarkers draw the selection as a bright ring (the old disc was too small zoomed out)
            if (selectionIndicator != null) selectionIndicator.SetActive(false);
        }

        public void IssueMoveCommand(Vector3 destination)
        {
            if (OnWall)
            {
                // Down the walls first, then there
                leaveTo = destination;
                leaveToMove = true;
                leaveTarget = null;
                LeaveWall();
                return;
            }
            if (wallState == WallState.ToStairs) { wallState = WallState.None; Building.WallWalk.Release(this); PostedByAlarm = false; }
            attackTarget = null;
            leashed = defending = false; // a new order: whatever fight it was in is over
            targetPosition = World.WorldSettings.ClampInside(destination); // not onto the mountains round the world
            targetPosition.y = GetTerrainHeight(targetPosition);
            hasTarget = true;
            stuckTimer = 0f;
        }
    }
}
