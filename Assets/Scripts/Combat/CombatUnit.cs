using System;
using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.Combat
{
    [DisallowMultipleComponent]
    public class CombatUnit : MonoBehaviour
    {
        [Header("Attack Configuration")]
        [SerializeField] private int attackDamage = 10;
        [SerializeField] private float attackCooldown = 2.0f; // 2 seconds between attacks as requested
        [SerializeField] private float attackRange = 3.0f;

        private float nextAttackTime = 0f;
        private Vector3 originalLocalPos;
        private bool isPunching = false;
        private float punchProgress = 0f;

        public int AttackDamage => attackDamage;
        public float AttackCooldown => attackCooldown;
        public float AttackRange => attackRange;

        [SerializeField] private float strength;
        /// <summary>Strength characteristic: the damage before the weapon (Equipment sets it; without gear = AttackDamage).</summary>
        public float Strength { get => strength > 0f ? strength : attackDamage; set => strength = value; }
        [SerializeField] private float speedModifier = CharacterSpeed.DefaultModifier;
        /// <summary>Speed characteristic (person 7, soldiers 6, hero 5; see UnitRanks, CharacterSpeed).</summary>
        public float SpeedModifier { get => speedModifier; set => speedModifier = value; }
        /// <summary>Global x SpeedModifier x sqrt(strength).</summary>
        public float MoveSpeed => CharacterSpeed.Of(this);
        public bool CanAttack => Time.time >= nextAttackTime;

        /// <summary>Raised when an attack swing starts (rigged characters play their attack clip).</summary>
        public event Action OnAttackStarted;

        private void Awake()
        {
            originalLocalPos = transform.localPosition;
        }

        // Every fighter heals naturally on the world clock (builders may add the HealthComponent after this one: Start retries)
        private void OnEnable() => WorldClock.RegisterFighter(GetComponent<HealthComponent>());
        private void Start() => WorldClock.RegisterFighter(GetComponent<HealthComponent>());
        private void OnDisable() => WorldClock.UnregisterFighter(GetComponent<HealthComponent>());

        public void InitializeCombatStats(int damage, float cooldown = 2.0f, float range = 3.0f)
        {
            attackDamage = damage;
            attackCooldown = cooldown;
            attackRange = range;
        }

        public bool TryAttack(HealthComponent target)
        {
            if (target == null || !target.IsAlive) return false;
            if (!CanAttack) return false;

            float dst = Vector3.Distance(transform.position, target.transform.position);
            if (dst > attackRange + 1.0f) return false;

            nextAttackTime = Time.time + attackCooldown;
            int finalDamage = ItemStatCalculator.CalculateDamageWithHighGround(
                attackDamage, transform.position.y, target.transform.position.y);
            target.TakeDamage(finalDamage);

            TriggerAttackAnimation();
            return true;
        }

        public void TriggerAttackAnimation()
        {
            if (!isPunching)
            {
                isPunching = true;
                OnAttackStarted?.Invoke();
                punchProgress = 0f;
            }
        }

        private void Update()
        {
            // Procedural punch/swing animation for unit visual feedback
            if (isPunching)
            {
                punchProgress += Time.deltaTime * 6f; // Fast punch forward and back
                if (punchProgress >= Mathf.PI)
                {
                    isPunching = false;
                    punchProgress = 0f;
                }
                else
                {
                    float offset = Mathf.Sin(punchProgress) * 0.35f;
                    // Visual punch impulse in forward direction
                    transform.Translate(Vector3.forward * offset * Time.deltaTime * 4f, Space.Self);
                }
            }
        }
    }
}
