using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Drives a rigged character's Animator from the unit it belongs to: horizontal movement speed of the parent
    /// (CharacterController units move the root, the visual model is a child) feeds the "Speed" locomotion blend,
    /// CombatUnit attacks fire "Attack", HealthComponent death sets "Dead".
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterAnimationDriver : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int DeadId = Animator.StringToHash("Dead");

        private Animator animator;

        private Vector3 lastPosition;
        private float smoothedSpeed;
        private CombatUnit combat;
        private HealthComponent health;
        private float attackTimer;      // seconds left in the upper-body swing
        private float attackLength = 0.9f;
        private float upperWeight;
        private float sentSpeed = -1f;
        private const int UpperBodyLayer = 1;

        /// <summary>When set (e.g. for screenshots), overrides the measured speed.</summary>
        public float? SpeedOverride { get; set; }

        /// <summary>Horizontal speed the locomotion follows (also for GpuSkinnedCharacter while the Animator is off).</summary>
        public float Speed => SpeedOverride ?? smoothedSpeed;

        /// <summary>The Animator is back on (GpuSkinnedCharacter drew it meanwhile): send it the current state.</summary>
        public void Resync(bool dead)
        {
            if (animator == null) return;
            animator.SetFloat(SpeedId, Speed);
            sentSpeed = Speed;
            if (dead) { animator.SetBool(DeadId, true); animator.Play("Death", 0, 1f); }
        }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                    if (clip.name == "Attack") attackLength = clip.length;
            }
        }

        // Start, not Awake/OnEnable: builders add this component before the model is parented to its unit.
        private void Start()
        {
            lastPosition = transform.position;
            combat = GetComponentInParent<CombatUnit>();
            health = GetComponentInParent<HealthComponent>();
            if (combat != null) combat.OnAttackStarted += HandleAttack;
            if (health != null) health.OnDeath += HandleDeath;

            // Desynchronise crowds: start the idle loop at a random point.
            if (animator != null && animator.runtimeAnimatorController != null) animator.Play(0, 0, Random.value);
        }

        private void OnDestroy()
        {
            if (combat != null) combat.OnAttackStarted -= HandleAttack;
            if (health != null) health.OnDeath -= HandleDeath;
        }

        private void Update()
        {
            if (animator == null) return;

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 delta = transform.position - lastPosition; // the model moves with its unit
            delta.y = 0f;
            lastPosition = transform.position;
            float measured = delta.magnitude / dt;
            if (measured > 30f) measured = 0f; // teleports / respawns are not movement

            smoothedSpeed = Mathf.Lerp(smoothedSpeed, measured, 1f - Mathf.Exp(-10f * dt));
            if (!animator.enabled) return; // drawn on the GPU meanwhile (GpuSkinnedCharacter)
            float speed = SpeedOverride ?? smoothedSpeed;
            if (Mathf.Abs(speed - sentSpeed) > 0.02f || (speed == 0f && sentSpeed != 0f)) { animator.SetFloat(SpeedId, speed); sentSpeed = speed; } // 1000 units: only on change

            // Upper-body swing layer: quick blend in, hold for the clip, blend out over the follow-through.
            if (animator.layerCount > UpperBodyLayer)
            {
                attackTimer -= dt;
                float target = attackTimer > 0.18f ? 1f : 0f;
                float weight = Mathf.MoveTowards(upperWeight, target, dt * (target > upperWeight ? 12f : 5f));
                if (weight != upperWeight) { upperWeight = weight; animator.SetLayerWeight(UpperBodyLayer, upperWeight); }
            }
        }

        private void HandleAttack()
        {
            if (animator == null || !animator.enabled || (health != null && !health.IsAlive)) return;
            if (animator.layerCount > UpperBodyLayer)
            {
                animator.Play("Attack", UpperBodyLayer, 0f);
                attackTimer = attackLength;
            }
            else animator.SetTrigger(AttackId);
        }

        private void HandleDeath()
        {
            attackTimer = 0f; // the death clip takes the whole body
            if (animator == null || !animator.enabled) return; // on the GPU: it plays the fall itself
            animator.SetBool(DeadId, true);
        }
    }
}
