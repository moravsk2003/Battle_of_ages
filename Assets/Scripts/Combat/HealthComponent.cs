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

        /// <summary>Sets the current health directly (a loaded save), clamped to 1..max.</summary>
        public void SetCurrentHealth(int health)
        {
            currentHealth = Mathf.Clamp(health, 1, maxHealth);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
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

        /// <summary>Who landed the last hit (the provoked AI goes after it).</summary>
        public CombatUnit LastAttacker { get; set; }

        /// <summary>Every Nth hit does nothing (the tank, §8.1; 0: off). A counter, no chance.</summary>
        public int IgnoreEveryNthHit { get; set; }
        private int hitCounter;

        /// <summary>A hit; armour (DamageReduction) absorbs its share unless the attack goes through armour (the mage).</summary>
        public void TakeDamage(int damage, bool ignoreArmor = false)
        {
            if (!IsAlive || damage <= 0) return;
            if (IgnoreEveryNthHit > 0 && ++hitCounter >= IgnoreEveryNthHit) { hitCounter = 0; return; }
            if (!ignoreArmor && DamageReduction > 0f) damage = Mathf.Max(1, Mathf.RoundToInt(damage * (1f - Mathf.Min(DamageReduction, GameBalance.MaxDamageReduction))));

            currentHealth = Mathf.Max(0, currentHealth - damage);
            OnTakeDamage?.Invoke(damage);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);

            // (No per-hit Debug.Log: in a 100 vs 100 battle hundreds of hits per second each cost a stack trace.)

            if (currentHealth <= 0)
            {
                OnDeath?.Invoke();
            }
        }

        /// <summary>Extra healing per game hour as shares of max and of current HP (the tank's Regeneration, §8.1).</summary>
        public float RegenMaxPerHour { get; set; }
        public float RegenCurrentPerHour { get; set; }
        private float regenCarry; // HP is whole: fractions wait here

        /// <summary>
        /// Heals for the elapsed game hours: natural regeneration from the current HP (docs/GAME_DESIGN.md §11.2), so the
        /// badly wounded heal slowly, plus any extra regeneration. Works in battle too.
        /// </summary>
        public void Regenerate(float gameHours)
        {
            if (!IsAlive || currentHealth >= maxHealth || gameHours <= 0f) { regenCarry = 0f; return; }
            regenCarry += (currentHealth * (GameBalance.NaturalRegenCurrentPerHour + RegenCurrentPerHour)
                           + maxHealth * RegenMaxPerHour) * gameHours;
            int whole = (int)regenCarry;
            if (whole <= 0) return;
            regenCarry -= whole;
            Heal(whole);
        }

        public void Heal(int amount)
        {
            if (!IsAlive || amount <= 0) return;

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }
    }
}
