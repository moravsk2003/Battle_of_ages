using UnityEngine;

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
        [SerializeField] private float moveSpeed = 5.0f;
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
        private bool isWorker;             // gatherers do not pick fights
        private int gridId = -1;
        private Vector3 steerDir, steerDesired;
        private float steerTimer, steerElapsed, engageTimer, settleTimer;
        private BattleOfAges.Combat.HealthComponent health;
        private bool touchingSolidNearTarget;

        public bool IsSelected { get; private set; }

        private void Awake()
        {
            isPlayerControlled = GetComponent<BattleOfAges.Player.FirstPersonController>() != null;
            health = GetComponent<BattleOfAges.Combat.HealthComponent>();
            if (!isPlayerControlled) BattleOfAges.Combat.UnitCorpse.AttachTo(gameObject);
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
            isWorker = GetComponent<NpcResourceGatherer>() != null;
            if (!isPlayerControlled) EnsureStartingKit();
        }

        /// <summary>Own units get the kit their model is drawn with (knight: steel set, villager: axe).</summary>
        private void EnsureStartingKit()
        {
            if (GetComponent<BattleOfAges.Crafting.Equipment>() != null) return;
            var visual = GetComponentInChildren<BattleOfAges.Utils.CharacterGear>(true);
            var type = visual != null ? visual.Type
                : name.Contains("Knight") ? BattleOfAges.Utils.HumanoidModelBuilder.HumanType.KnightGuard : BattleOfAges.Utils.HumanoidModelBuilder.HumanType.Villager;
            gameObject.AddComponent<BattleOfAges.Crafting.Equipment>().InitLoadout(BattleOfAges.Crafting.StartingKits.For(type));
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
                gridId = UnitSpatialGrid.Register(this, GetComponent<BattleOfAges.Combat.HealthComponent>(), UnitSpatialGrid.TeamPlayer,
                                                  characterController != null ? characterController.radius : 0.4f);
        }

        private void OnDisable()
        {
            UnitRegistry.Unregister(this);
            UnitSpatialGrid.Unregister(gridId);
            gridId = -1;
        }

        private BattleOfAges.Combat.HealthComponent attackTarget;
        private BattleOfAges.Combat.CombatUnit combatUnit;

        public void IssueAttackCommand(BattleOfAges.Combat.HealthComponent target)
        {
            if (target != null)
            {
                IssueMoveCommand(target.transform.position); // clears the previous attack target...
            }
            attackTarget = target;                           // ...so the new one is set afterwards (was lost before)
        }

        private void Update()
        {
            if (health != null && !health.IsAlive) return; // fallen units neither walk nor fight
            if (combatUnit == null) combatUnit = GetComponent<BattleOfAges.Combat.CombatUnit>();

            // Soldiers engage raiders that come near on their own (idle or after a fight), not workers or the hero.
            if (!isPlayerControlled && !isWorker && combatUnit != null && attackTarget == null && !hasTarget)
            {
                engageTimer -= Time.deltaTime;
                if (engageTimer <= 0f)
                {
                    engageTimer = EngageInterval;
                    int enemy = UnitSpatialGrid.FindNearest(transform.position, EngageRadius, UnitSpatialGrid.TeamHostile);
                    if (enemy >= 0) attackTarget = UnitSpatialGrid.Health(enemy);
                }
            }

            // Attack Target tracking logic
            if (attackTarget != null)
            {
                if (!attackTarget.IsAlive)
                {
                    attackTarget = null;
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
                GroundMotor.Move(transform, gridId, Vector3.zero, radius, Time.deltaTime);
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
                moveDir = steerDir;
                if (touchingSolidNearTarget)
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
                Vector3 moveVelocity = moveDir * moveSpeed;
                moveVelocity.y = -9.81f;

                if (!isPlayerControlled)
                {
                    GroundMotor.Move(transform, gridId, moveDir * moveSpeed, radius, Time.deltaTime);
                }
                else if (characterController != null)
                {
                    characterController.Move(moveVelocity * Time.deltaTime);
                }
                else
                {
                    Vector3 nextPos = Vector3.MoveTowards(currentPos, destPos, moveSpeed * Time.deltaTime);
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
            touchingSolidNearTarget = !isPlayerControlled && centerHit && hitCenter.distance < 0.35f && IsSolid(hitCenter)
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
            if (movedDist < 0.20f * elapsed * moveSpeed)
            {
                stuckTimer += elapsed;
                if (stuckTimer > 0.25f)
                {
                    if (stuckTimer > 0.8f && Random.value < 0.1f * Mathf.Max(1f, elapsed / 0.016f)) avoidSideSign = -avoidSideSign;
                    Vector3 wallBypassDir = (Vector3.Cross(Vector3.up, desiredDir).normalized * avoidSideSign + desiredDir * 0.4f).normalized;
                    finalDir = wallBypassDir;

                    if (stuckTimer > 1.0f)
                    {
                        Vector3 lateralNudge = Vector3.Cross(Vector3.up, desiredDir).normalized * (avoidSideSign * 2.0f * elapsed);
                        if (isPlayerControlled && characterController != null) characterController.Move(lateralNudge);
                        else GroundMotor.Move(transform, gridId, lateralNudge / Mathf.Max(elapsed, 1e-4f), characterController != null ? characterController.radius : 0.4f, elapsed);
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
            if (hit.collider is TerrainCollider || hit.collider.GetComponent<BattleOfAges.World.ProceduralBiomeChunk>() != null) return false;
            return true;
        }

        public void SnapToTerrain()
        {
            float groundY = GetTerrainHeight(transform.position);
            transform.position = new Vector3(transform.position.x, groundY, transform.position.z);
        }

        public static float GetTerrainHeight(Vector3 worldPos)
        {
            if (BattleOfAges.World.InfiniteWorldManager.Instance != null)
            {
                return BattleOfAges.World.InfiniteWorldManager.GetSampledTerrainHeight(worldPos.x, worldPos.z);
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

            return BattleOfAges.World.InfiniteWorldManager.GetSampledTerrainHeight(worldPos.x, worldPos.z);
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

            if (selectionIndicator != null)
            {
                selectionIndicator.SetActive(selected);
            }
        }

        public void IssueMoveCommand(Vector3 destination)
        {
            attackTarget = null;
            targetPosition = destination;
            targetPosition.y = GetTerrainHeight(destination);
            hasTarget = true;
            stuckTimer = 0f;
        }
    }
}
