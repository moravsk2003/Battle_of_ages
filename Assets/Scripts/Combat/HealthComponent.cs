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

        public void TakeDamage(int damage)
        {
            if (!IsAlive || damage <= 0) return;

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
