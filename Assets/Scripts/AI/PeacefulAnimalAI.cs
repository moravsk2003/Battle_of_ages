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
        private readonly Transform[] legPivots = new Transform[4];
        private readonly Transform[] knees = new Transform[4];
        private Transform neckPivot;
        private Transform tailPivot;
        private Transform earL, earR;
        private Quaternion earLRest = Quaternion.identity, earRRest = Quaternion.identity;
        private Quaternion neckRest = Quaternion.identity;
        private Quaternion headRest = Quaternion.identity;
        private Quaternion tailRest = Quaternion.identity;
        private Vector3 lastAnimPos;
        private float gaitSpeed;
        private float gaitPhase;


        private float grazeTimer;

        public AnimalType Type => animalType;
        public float ThreatDetectionRadius => threatDetectionRadius;
        public float SafeDistance => safeDistance;
        public float WanderRadius => wanderRadius;
        public Vector3 SpawnOrigin => spawnOrigin;
        public Transform DetectedThreat => detectedThreat;

        protected override int GridTeam => UnitSpatialGrid.TeamNeutral;

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
            if (visualModel == null) return;

            // Rest rotations are captured only when a joint is first found, never from an already animated pose.
            Transform neck = visualModel.Find("NeckPivot");
            if (neck != neckPivot && neck != null) neckRest = neck.localRotation;
            neckPivot = neck;
            Transform head = neckPivot != null ? neckPivot.Find("Head") : visualModel.Find("Head");
            if (head != headTransform && head != null) headRest = head.localRotation;
            headTransform = head;
            Transform tail = visualModel.Find("TailPivot");
            if (tail != tailPivot && tail != null) tailRest = tail.localRotation;
            tailPivot = tail;
            Transform el = headTransform != null ? headTransform.Find("Ear_L") : null, er = headTransform != null ? headTransform.Find("Ear_R") : null;
            if (el != earL && el != null) earLRest = el.localRotation;
            if (er != earR && er != null) earRRest = er.localRotation;
            earL = el; earR = er;
            for (int i = 0; i < 4; i++)
            {
                legPivots[i] = visualModel.Find("LegPivot_" + i);
                knees[i] = legPivots[i] != null ? legPivots[i].Find("Knee_" + i) : null;
            }
            lastAnimPos = transform.position;
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

        /// <summary>Gait from the measured ground speed: phase advances one cycle per stride length.</summary>
        private void AnimateProceduralVisuals(float dt)
        {
            if (visualModel == null || dt <= 0f) return;

            Vector3 d = transform.position - lastAnimPos;
            d.y = 0f;
            lastAnimPos = transform.position;
            float measured = d.magnitude / dt;
            if (measured > 30f) measured = 0f; // teleport / respawn
            gaitSpeed = Mathf.Lerp(gaitSpeed, measured, 1f - Mathf.Exp(-8f * dt));

            float stride = animalType == AnimalType.Rabbit ? 0.7f : (animalType == AnimalType.Boar ? 0.9f : 1.5f);
            gaitPhase = Mathf.Repeat(gaitPhase + dt * gaitSpeed / stride * Mathf.PI * 2f, Mathf.PI * 2f);
            grazeTimer += dt;
            ApplyGaitPose(gaitPhase, Mathf.Clamp01(gaitSpeed / 1.2f));
        }

        /// <summary>Freezes the animal at a gait phase (0..1) and movement amount (0 idle .. 1 moving); for previews.</summary>
        public void PoseGait(float phase01, float move)
        {
            CacheVisualNodes();
            ApplyGaitPose(phase01 * Mathf.PI * 2f, move);
        }

        /// <summary>Freezes the animal standing with its head fully down in the grass; for previews.</summary>
        public void PoseGrazing()
        {
            CacheVisualNodes();
            ApplyGaitPose(0f, 0f, 1f);
        }

        private void ApplyGaitPose(float phase, float move, float grazeOverride = -1f)
        {
            bool rabbit = animalType == AnimalType.Rabbit;
            float swing = rabbit ? 40f : 28f;
            float kneeAmp = rabbit ? 30f : 45f;

            for (int i = 0; i < 4; i++)
            {
                if (legPivots[i] == null) continue;
                // Trot: diagonal pairs (0+3, 1+2) in phase. Rabbit: bound, front pair vs hind pair.
                float off = rabbit ? (i < 2 ? 0f : Mathf.PI) : ((i == 0 || i == 3) ? 0f : Mathf.PI);
                float s = Mathf.Sin(phase + off);
                legPivots[i].localRotation = Quaternion.Euler(s * swing * move, 0f, 0f);
                if (knees[i] != null)
                {
                    float flex = Mathf.Max(0f, Mathf.Cos(phase + off)) * kneeAmp * move; // fold while the leg swings forward
                    knees[i].localRotation = Quaternion.Euler(flex, 0f, 0f);
                }
            }

            // Body: trot rises twice per cycle; rabbit hops in an arc and pitches with the jump
            float bob = rabbit ? Mathf.Max(0f, Mathf.Sin(phase)) * 0.16f : (0.5f - 0.5f * Mathf.Cos(2f * phase)) * (animalType == AnimalType.Boar ? 0.03f : 0.045f);
            visualModel.localPosition = new Vector3(0f, bob * move, 0f);
            float pitch = rabbit ? -Mathf.Sin(phase) * 10f * move : 0f;
            visualModel.localRotation = Quaternion.Euler(pitch, 0f, Mathf.Sin(phase) * 1.5f * move);

            if (neckPivot != null)
            {
                // Grazing when standing: the head goes down for a while, then up to look around
                float grazeCycle = grazeOverride >= 0f ? grazeOverride
                    : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Sin(grazeTimer * 0.45f + GetInstanceID() * 0.37f) * 1.6f));
                float grazeAngle = (animalType == AnimalType.Deer ? 85f : (animalType == AnimalType.Boar ? 22f : 18f)) * grazeCycle;
                float walkNod = Mathf.Sin(2f * phase) * 4f;
                float sniff = animalType == AnimalType.Boar ? Mathf.Sin(Time.time * 11f) * Mathf.Max(0f, Mathf.Sin(Time.time * 0.7f + GetInstanceID())) * 2.5f * (1f - move) : 0f;
                neckPivot.localRotation = neckRest * Quaternion.Euler(Mathf.Lerp(grazeAngle + sniff, walkNod, move), 0f, 0f);
                // The deer's long neck alone does not bring the muzzle to the grass: the head tips down as well
                if (animalType == AnimalType.Deer && headTransform != null)
                    headTransform.localRotation = headRest * Quaternion.Euler(22f * grazeCycle * (1f - move), 0f, 0f);
            }

            if (tailPivot != null)
            {
                tailPivot.localRotation = tailRest * Quaternion.Euler(0f, Mathf.Sin(Time.time * 7f + GetInstanceID()) * 15f * (0.4f + move), 0f);
            }

            if (earL != null && earR != null)
            {
                // Ears: an occasional flick each (independent timing), laid back while running
                float t = Time.time + GetInstanceID() * 0.37f;
                float flickL = EarFlick(t), flickR = EarFlick(t * 1.13f + 2.1f);
                earL.localRotation = earLRest * Quaternion.Euler(-28f * move - 22f * flickL, 0f, 14f * flickL);
                earR.localRotation = earRRest * Quaternion.Euler(-28f * move - 22f * flickR, 0f, -14f * flickR);
            }
        }

        /// <summary>0 most of the time, a quick 0..1..0 twitch for a quarter second every ~3.7 s.</summary>
        private static float EarFlick(float t)
        {
            float c = Mathf.Repeat(t, 3.7f);
            return c < 0.25f ? Mathf.Sin(c / 0.25f * Mathf.PI) : 0f;
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
