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

        public bool IsSelected { get; private set; }

        private void Awake()
        {
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
        }

        private void OnDisable()
        {
            UnitRegistry.Unregister(this);
        }

        private BattleOfAges.Combat.HealthComponent attackTarget;
        private BattleOfAges.Combat.CombatUnit combatUnit;

        public void IssueAttackCommand(BattleOfAges.Combat.HealthComponent target)
        {
            attackTarget = target;
            if (target != null)
            {
                IssueMoveCommand(target.transform.position);
            }
        }

        private void Update()
        {
            if (combatUnit == null) combatUnit = GetComponent<BattleOfAges.Combat.CombatUnit>();

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

            if (!hasTarget)
            {
                // Stick to ground when idle
                if (characterController != null && !characterController.isGrounded)
                {
                    characterController.Move(Vector3.down * 9.81f * Time.deltaTime);
                }
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

                // 1. Advanced Multi-Whisker Obstacle Avoidance & Anti-Stuck Steering
                moveDir = CalculateAvoidanceDirection(currentPos, moveDir);

                // 2. Smooth Rotation towards Steering Direction
                if (moveDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(moveDir);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
                }

                // 3. CharacterController Physics Movement
                Vector3 moveVelocity = moveDir * moveSpeed;
                moveVelocity.y = -9.81f;

                if (characterController != null)
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

        private Vector3 CalculateAvoidanceDirection(Vector3 currentPos, Vector3 desiredDir)
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
            if (movedDist < 0.20f * Time.deltaTime * moveSpeed)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 0.25f)
                {
                    if (stuckTimer > 0.8f && Random.value < 0.1f) avoidSideSign = -avoidSideSign;
                    Vector3 wallBypassDir = (Vector3.Cross(Vector3.up, desiredDir).normalized * avoidSideSign + desiredDir * 0.4f).normalized;
                    finalDir = wallBypassDir;

                    if (stuckTimer > 1.0f && characterController != null)
                    {
                        Vector3 lateralNudge = Vector3.Cross(Vector3.up, desiredDir).normalized * (avoidSideSign * 2.0f * Time.deltaTime);
                        characterController.Move(lateralNudge);
                    }
                }
            }
            else
            {
                stuckTimer = Mathf.Max(0f, stuckTimer - Time.deltaTime * 2f);
            }

            lastPos = currentPos;
            return finalDir;
        }

        private bool IsValidObstacle(RaycastHit hit)
        {
            if (hit.collider == null) return false;
            if (hit.collider.isTrigger) return false;
            if (hit.collider.gameObject == gameObject || hit.transform.root == transform) return false;

            // If hit object is our attack target or within 2.5m of target destination, do not steer away!
            if (attackTarget != null && (hit.transform.root == attackTarget.transform || hit.transform == attackTarget.transform)) return false;
            if (hasTarget && Vector3.Distance(hit.point, targetPosition) < 2.5f) return false;

            string name = hit.collider.gameObject.name;
            if (name.Contains("SteppeTerrain") || name.Contains("Ground") || name.Contains("Chunk") || name.Contains("Terrain") || name.Contains("SelectionIndicator")) return false;
            if (hit.collider is TerrainCollider || hit.collider.GetComponent<BattleOfAges.World.ProceduralBiomeChunk>() != null || hit.collider.GetComponentInParent<BattleOfAges.World.ProceduralBiomeChunk>() != null) return false;

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
