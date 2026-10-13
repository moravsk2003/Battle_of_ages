using UnityEngine;
using BattleOfAges.RTS;

namespace BattleOfAges.AI
{
    public enum AnimalType
    {
        Deer,
        Boar,
        Rabbit
    }

    /// <summary>
    /// AI Controller for peaceful/neutral wildlife (Deer, Boar, Rabbit).
    /// Features wandering, grazing animations, and fleeing when player or threats approach.
    /// </summary>
    [DisallowMultipleComponent]
    public class PeacefulAnimalAI : AIBaseController
    {
        [Header("Animal Settings")]
        [SerializeField] private AnimalType animalType = AnimalType.Deer;
        [SerializeField] private float threatDetectionRadius = 14.0f;
        [SerializeField] private float safeDistance = 28.0f;
        [SerializeField] private float wanderRadius = 18.0f;

        private const float HerdAlertRadius = 22.0f;
        private static readonly Collider[] overlapBuffer = new Collider[64];

        private Vector3 spawnOrigin;
        private Transform cachedPlayer;
        private Transform detectedThreat;
        private Transform visualModel;
        private Transform headTransform;
        private Transform[] legTransforms;

        private float animPhase;
        private float grazeTimer;

        public AnimalType Type => animalType;
        public float ThreatDetectionRadius => threatDetectionRadius;
        public float SafeDistance => safeDistance;
        public float WanderRadius => wanderRadius;
        public Vector3 SpawnOrigin => spawnOrigin;
        public Transform DetectedThreat => detectedThreat;

        protected override void Awake()
        {
            spawnOrigin = transform.position;
            base.Awake();
            CacheVisualNodes();
            ConfigureAnimalStats();
        }

        protected override void Start()
        {
            base.Start();

            // Spawners create the animal at the world origin and move it afterwards,
            // so the home point is re-captured here and the first wander target re-picked.
            spawnOrigin = transform.position;
            if (CurrentStateType == AIStateType.Wander) ChangeState(AIStateType.Wander);
        }

        public void InitializeAnimal(AnimalType type)
        {
            animalType = type;
            ConfigureAnimalStats();
            CacheVisualNodes();
        }

        private void ConfigureAnimalStats()
        {
            if (animalType == AnimalType.Boar)
            {
                if (healthComponent != null) healthComponent.InitializeHealth(120);
                moveSpeed = 4.0f;
                threatDetectionRadius = 10.0f;
            }
            else if (animalType == AnimalType.Rabbit)
            {
                if (healthComponent != null) healthComponent.InitializeHealth(30);
                moveSpeed = 6.5f;
                threatDetectionRadius = 16.0f;
            }
            else // Deer
            {
                if (healthComponent != null) healthComponent.InitializeHealth(80);
                moveSpeed = 5.2f;
                threatDetectionRadius = 14.0f;
            }
        }

        private void CacheVisualNodes()
        {
            visualModel = transform.Find("VisualModel");
            if (visualModel != null)
            {
                headTransform = visualModel.Find("Head");
            }
        }

        protected override void RegisterStates()
        {
            states[AIStateType.Idle] = new AnimalIdleState();
            states[AIStateType.Wander] = new AnimalWanderState();
            states[AIStateType.Flee] = new AnimalFleeState();

            ChangeState(AIStateType.Wander);
        }

        public void SetDetectedThreat(Transform threat)
        {
            detectedThreat = threat;
        }

        public Transform FindThreatNearby()
        {
            if (cachedPlayer == null)
            {
                GameObject player = GameObject.Find("Player");
                if (player != null) cachedPlayer = player.transform;
            }

            if (cachedPlayer != null)
            {
                float dist = Vector3.Distance(transform.position, cachedPlayer.position);
                if (dist <= threatDetectionRadius)
                {
                    return cachedPlayer;
                }
            }
            return null;
        }

        protected override void Update()
        {
            base.Update();
            AnimateProceduralVisuals(Time.deltaTime);
        }

        private void AnimateProceduralVisuals(float dt)
        {
            if (visualModel == null) return;

            bool isMoving = (CurrentStateType == AIStateType.Wander || CurrentStateType == AIStateType.Flee) &&
                            Vector3.Distance(transform.position, destination) > 0.6f;

            if (isMoving)
            {
                float freq = (CurrentStateType == AIStateType.Flee) ? 14f : 8f;
                animPhase += dt * freq;

                // Hopping/trotting vertical bounce
                float bounce = (animalType == AnimalType.Rabbit)
                    ? Mathf.Abs(Mathf.Sin(animPhase)) * 0.22f
                    : Mathf.Sin(animPhase * 2f) * 0.06f;

                visualModel.localPosition = new Vector3(0f, bounce, 0f);

                // Body pitch/roll lean
                float roll = Mathf.Sin(animPhase) * 3.5f;
                visualModel.localRotation = Quaternion.Euler(0f, 0f, roll);

                if (headTransform != null)
                {
                    headTransform.localRotation = Quaternion.identity;
                }
            }
            else
            {
                // Idle / Grazing animation: gentle breathing and head dip down to eat grass
                grazeTimer += dt;
                float grazeDip = Mathf.Sin(grazeTimer * 1.5f);
                visualModel.localPosition = Vector3.Lerp(visualModel.localPosition, Vector3.zero, dt * 5f);

                if (headTransform != null)
                {
                    // Head lowers towards the ground when grazing
                    float headPitch = Mathf.Clamp(grazeDip * 25f + 10f, 0f, 35f);
                    headTransform.localRotation = Quaternion.Euler(headPitch, 0f, 0f);
                }
            }
        }

        public void AlertNearbyHerd(Transform threat)
        {
            if (threat == null) return;
            int count = Physics.OverlapSphereNonAlloc(transform.position, HerdAlertRadius, overlapBuffer);
            for (int i = 0; i < count; i++)
            {
                PeacefulAnimalAI herdAnimal = overlapBuffer[i].GetComponentInParent<PeacefulAnimalAI>();
                if (herdAnimal != null && herdAnimal != this && herdAnimal.CurrentStateType != AIStateType.Flee)
                {
                    herdAnimal.SetDetectedThreat(threat);
                    herdAnimal.ChangeState(AIStateType.Flee);
                }
            }
        }
    }

    public class AnimalIdleState : IAIState
    {
        public AIStateType StateType => AIStateType.Idle;
        private float idleDuration;
        private float idleTimer;

        public void Enter(AIBaseController controller)
        {
            controller.StopMovement();
            idleDuration = Random.Range(2.5f, 6.0f);
            idleTimer = 0f;
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            PeacefulAnimalAI animal = controller as PeacefulAnimalAI;
            if (animal == null) return;

            Transform threat = animal.FindThreatNearby();
            if (threat != null)
            {
                animal.SetDetectedThreat(threat);
                animal.ChangeState(AIStateType.Flee);
                return;
            }

            idleTimer += deltaTime;
            if (idleTimer >= idleDuration)
            {
                animal.ChangeState(AIStateType.Wander);
            }
        }

        public void Exit(AIBaseController controller) { }
    }

    public class AnimalWanderState : IAIState
    {
        public AIStateType StateType => AIStateType.Wander;
        private float wanderTimer;
        private float maxWanderDuration = 10f;

        public void Enter(AIBaseController controller)
        {
            PeacefulAnimalAI animal = controller as PeacefulAnimalAI;
            if (animal == null) return;

            Vector2 randomCircle = Random.insideUnitCircle * animal.WanderRadius;
            Vector3 targetPos = animal.SpawnOrigin + new Vector3(randomCircle.x, 0f, randomCircle.y);
            targetPos.y = RtsUnit.GetTerrainHeight(targetPos);

            animal.SetMoveDestination(targetPos);
            wanderTimer = 0f;
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            PeacefulAnimalAI animal = controller as PeacefulAnimalAI;
            if (animal == null) return;

            Transform threat = animal.FindThreatNearby();
            if (threat != null)
            {
                animal.SetDetectedThreat(threat);
                animal.ChangeState(AIStateType.Flee);
                return;
            }

            wanderTimer += deltaTime;
            // HasDestination is cleared by the movement code on (horizontal) arrival; a 3D distance
            // check here could never succeed on slopes where pivot and terrain height differ.
            if (!animal.HasDestination || wanderTimer >= maxWanderDuration)
            {
                animal.ChangeState(AIStateType.Idle);
            }
        }

        public void Exit(AIBaseController controller) { }
    }

    public class AnimalFleeState : IAIState
    {
        public AIStateType StateType => AIStateType.Flee;
        private float fleeRepathTimer;

        public void Enter(AIBaseController controller)
        {
            PeacefulAnimalAI animal = controller as PeacefulAnimalAI;
            if (animal != null && animal.DetectedThreat != null)
            {
                animal.AlertNearbyHerd(animal.DetectedThreat);
            }
            RepathFlee(controller);
            fleeRepathTimer = 0f;
        }

        public void UpdateState(AIBaseController controller, float deltaTime)
        {
            PeacefulAnimalAI animal = controller as PeacefulAnimalAI;
            if (animal == null) return;

            Transform threat = ResolveThreat(animal);
            if (threat == null)
            {
                animal.ChangeState(AIStateType.Idle);
                return;
            }

            float distToThreat = Vector3.Distance(animal.transform.position, threat.position);
            if (distToThreat >= animal.SafeDistance)
            {
                animal.SetDetectedThreat(null);
                animal.ChangeState(AIStateType.Idle);
                return;
            }

            fleeRepathTimer += deltaTime;
            if (fleeRepathTimer >= 0.5f)
            {
                fleeRepathTimer = 0f;
                RepathFlee(controller);
            }
        }

        private void RepathFlee(AIBaseController controller)
        {
            PeacefulAnimalAI animal = controller as PeacefulAnimalAI;
            if (animal == null) return;

            Transform threat = ResolveThreat(animal);
            if (threat == null) return;

            Vector3 fleeDir = (animal.transform.position - threat.position).normalized;
            if (fleeDir == Vector3.zero) fleeDir = Random.insideUnitSphere;
            fleeDir.y = 0;
            fleeDir.Normalize();

            Vector3 fleeTarget = animal.transform.position + fleeDir * 20.0f;
            fleeTarget.y = RtsUnit.GetTerrainHeight(fleeTarget);

            animal.SetMoveDestination(fleeTarget);
        }

        // Explicit == null instead of '??': the C# null-coalescing operator bypasses Unity's
        // destroyed-object check and would return a destroyed Transform.
        private static Transform ResolveThreat(PeacefulAnimalAI animal)
        {
            Transform threat = animal.DetectedThreat;
            return threat != null ? threat : animal.FindThreatNearby();
        }

        public void Exit(AIBaseController controller) { }
    }
}
