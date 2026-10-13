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

        /// <summary>When set (e.g. for screenshots), overrides the measured speed.</summary>
        public float? SpeedOverride { get; set; }

        private void Awake()
        {
            animator = GetComponentInChildren<Animator>();
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
            animator.SetFloat(SpeedId, SpeedOverride ?? smoothedSpeed);
        }

        private void HandleAttack()
        {
            if (animator != null && (health == null || health.IsAlive)) animator.SetTrigger(AttackId);
        }

        private void HandleDeath()
        {
            if (animator != null) animator.SetBool(DeadId, true);
        }
    }
}
