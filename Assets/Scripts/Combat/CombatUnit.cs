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
        public float MoveSpeed => CharacterSpeed.Of(this) * (Time.time < boostUntil ? boostFactor : 1f) * RecoveryFactor;

        private float boostFactor = 1f, boostUntil = float.NegativeInfinity;
        /// <summary>Runs this many times faster for a while (the DD's Dash).</summary>
        public void Boost(float factor, float seconds) { boostFactor = factor; boostUntil = Time.time + seconds; }

        private float recoveryFactor = 1f, recoveryUntil = float.NegativeInfinity;
        /// <summary>Speed share right after an attack: slowed after a melee hit, rooted after a ranged shot (§11.1).</summary>
        public float RecoveryFactor => Time.time < recoveryUntil ? recoveryFactor : 1f;
        /// <summary>Shoots from afar: the mage's bolts or a bow's reach.</summary>
        public bool IsRanged => FiresBolts || attackRange >= ItemStatCalculator.BOW_RANGE - 0.01f;
        public bool CanAttack => Time.time >= nextAttackTime;

        /// <summary>Raised when an attack swing starts (rigged characters play their attack clip).</summary>
        public event Action OnAttackStarted;
        /// <summary>Raised when one of this unit's hits killed the target.</summary>
        public event Action<HealthComponent> Killed;
        private UnitProgress progress;

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
            // The swing lands after its wind-up (PendingHits), so the one walked up to can swing back (docs/GAME_DESIGN.md §11.1).
            // No hit-and-run: the striker is slowed (melee) or rooted (ranged) from the swing until a moment after it lands.
            float windup = Windup;
            recoveryFactor = IsRanged ? 0f : GameBalance.MeleeSlowFactor;
            recoveryUntil = Time.time + windup + (IsRanged ? GameBalance.RangedRootSeconds : GameBalance.MeleeSlowSeconds);
            int finalDamage = ItemStatCalculator.CalculateDamageWithHighGround(
                attackDamage, transform.position.y, target.transform.position.y);
            PendingHits.Add(this, target, finalDamage, FiresBolts, windup, attackRange);

            TriggerAttackAnimation();
            return true;
        }

        /// <summary>Time from the start of a swing to the hit: a sword swing, a bow draw, the mage's cast.</summary>
        public float Windup => FiresBolts ? GameBalance.MageWindup : IsRanged ? GameBalance.BowWindup : GameBalance.MeleeWindup;

        /// <summary>The mage (§8.1): its attack is a volley of homing bolts through armour instead of a direct hit.</summary>
        public bool FiresBolts { get; set; }

        /// <summary>Share of the damage dealt that heals this unit (the Vampiric artifact).</summary>
        public float LifeSteal { get; set; }

        /// <summary>Lands a hit of this unit (directly or from one of its bolts): damage, battle experience, kill.</summary>
        public void DeliverHit(HealthComponent target, int damage, bool ignoreArmor)
        {
            if (target == null || !target.IsAlive) return;
            int before = target.CurrentHealth;
            target.TakeDamage(damage, ignoreArmor);
            if (LifeSteal > 0f) GetComponent<HealthComponent>()?.Heal(Mathf.RoundToInt((before - target.CurrentHealth) * LifeSteal)); // the Vampiric artifact

            // Battle experience (docs/GAME_DESIGN.md §11.1); heroes count kills for their own levels (Killed)
            if (progress == null) progress = GetComponent<UnitProgress>();
            if (progress != null) progress.OnHit();
            if (!target.IsAlive)
            {
                if (progress != null) progress.OnKill();
                Killed?.Invoke(target);
            }
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
