using System;
using UnityEngine;

namespace BattleOfAges.Combat
{
    [DisallowMultipleComponent]
    public class HealthComponent : MonoBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private int currentHealth;

        public int MaxHealth => maxHealth;
        public int CurrentHealth => currentHealth;
        public bool IsAlive => currentHealth > 0;
        /// <summary>Share of every incoming hit that worn armour absorbs (set by Equipment), 0..0.8.</summary>
        public float DamageReduction { get; set; }

        public event Action<int, int> OnHealthChanged; // (current, max)
        public event Action<int> OnTakeDamage;        // (damageAmount)
        public event Action OnDeath;

        private void Awake()
        {
            currentHealth = maxHealth;
        }

        public void InitializeHealth(int health)
        {
            maxHealth = Mathf.Max(1, health);
            currentHealth = maxHealth;
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        /// <summary>Changes max health without healing: the damage already taken stays taken.</summary>
        public void SetMaxHealthKeepDamage(int health)
        {
            int missing = maxHealth - currentHealth;
            maxHealth = Mathf.Max(1, health);
            if (currentHealth > 0) currentHealth = Mathf.Clamp(maxHealth - missing, 1, maxHealth);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        public void TakeDamage(int damage)
        {
            if (!IsAlive || damage <= 0) return;
            if (DamageReduction > 0f) damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - Mathf.Min(DamageReduction, 0.8f))));

            currentHealth = Mathf.Max(0, currentHealth - damage);
            OnTakeDamage?.Invoke(damage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            // (No per-hit Debug.Log: in a 100 vs 100 battle hundreds of hits per second each cost a stack trace.)

            if (currentHealth <= 0)
            {
                OnDeath?.Invoke();
            }
        }

        public void Heal(int amount)
        {
            if (!IsAlive || amount <= 0) return;

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }
    }
}
