using System;
using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// A hero's level (docs/GAME_DESIGN.md §8): experience only from kills, by the victim's rank (person 2, warrior 4,
    /// knight 10, general 50; a hero victim x (1 + its level)). Going from level n to n + 1 costs 100 x n. Each level
    /// gives one attribute point (+5 strength or +50 HP) and one skill point. The hero's rank never changes (UnitProgress).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroProgress : MonoBehaviour
    {
        [SerializeField] private int level = 1;
        [SerializeField] private int xp;
        [SerializeField] private int attributePoints, skillPoints;
        [SerializeField] private int strengthPoints, healthPoints; // attribute points spent on each

        public int Level => level;
        public int Xp => xp;
        public int AttributePoints => attributePoints;
        public int SkillPoints => skillPoints;
        public int StrengthPoints => strengthPoints;
        public int HealthPoints => healthPoints;
        public int XpToNextLevel => XpForLevelUp(level);

        /// <summary>Raised after any change (XP, level, points).</summary>
        public event Action<HeroProgress> Changed;

        private CombatUnit combat;

        public static int XpForLevelUp(int fromLevel) => GameBalance.HeroXpPerLevel * Mathf.Max(1, fromLevel);

        /// <summary>XP for killing this unit: by its rank; a hero is worth (1 + its level) times its rank; others by their HP.</summary>
        public static int KillXp(GameObject victim)
        {
            if (victim == null) return 0;
            UnitProgress rank = victim.GetComponent<UnitProgress>();
            if (rank == null)
            {
                HealthComponent hp = victim.GetComponent<HealthComponent>();
                return hp != null ? Mathf.Max(1, hp.MaxHealth / GameBalance.WildlifeHpPerXp) : 0; // beasts: by their strength
            }
            int xp = UnitRanks.KillXp(rank.Rank);
            HeroProgress hero = victim.GetComponent<HeroProgress>();
            return hero != null ? xp * (1 + hero.Level) : xp;
        }

        private void OnEnable()
        {
            combat = GetComponent<CombatUnit>();
            if (combat != null) combat.Killed += OnKilled;
        }

        private void OnDisable()
        {
            if (combat != null) combat.Killed -= OnKilled;
        }

        private void OnKilled(HealthComponent victim) => AddXp(KillXp(victim != null ? victim.gameObject : null));

        public void AddXp(int amount)
        {
            if (amount <= 0) return;
            xp += amount;
            while (xp >= XpToNextLevel)
            {
                xp -= XpToNextLevel;
                level++;
                attributePoints++;
                skillPoints++;
            }
            Changed?.Invoke(this);
        }

        /// <summary>Spends an attribute point on strength (+5: damage, and speed through sqrt(strength)).</summary>
        public bool SpendOnStrength() => Spend(ref strengthPoints);

        /// <summary>Spends an attribute point on health (+50 HP).</summary>
        public bool SpendOnHealth() => Spend(ref healthPoints);

        /// <summary>Takes skill points (a skill learnt); false if there are not enough.</summary>
        public bool TakeSkillPoints(int count)
        {
            if (count <= 0 || skillPoints < count) return false;
            skillPoints -= count;
            Changed?.Invoke(this);
            return true;
        }

        private bool Spend(ref int bucket)
        {
            if (attributePoints <= 0) return false;
            attributePoints--;
            bucket++;
            ApplyStats();
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>Base stats of the hero's rank (x2) plus the spent attribute points, gear on top; the same share of HP.</summary>
        public void ApplyStats()
        {
            UnitProgress rank = GetComponent<UnitProgress>();
            Equipment gear = GetComponent<Equipment>();
            if (rank == null || gear == null) return;
            HealthComponent health = GetComponent<HealthComponent>();
            float share = health != null && health.MaxHealth > 0 ? (float)health.CurrentHealth / health.MaxHealth : 1f;
            int hp = UnitRanks.Health(rank.Rank, hero: true) + healthPoints * GameBalance.HealthPerPoint;
            int strength = UnitRanks.Strength(rank.Rank, hero: true) + strengthPoints * GameBalance.StrengthPerPoint;
            gear.SetBaseStats(hp, strength, gear.BaseCooldown, gear.BaseRange);
            if (health != null && health.IsAlive) health.SetCurrentHealth(Mathf.Max(1, Mathf.RoundToInt(share * health.MaxHealth)));
        }

        /// <summary>A loaded save.</summary>
        public void Restore(int savedLevel, int savedXp, int freeAttributes, int freeSkills, int strengthSpent, int healthSpent)
        {
            level = Mathf.Max(1, savedLevel);
            xp = Mathf.Max(0, savedXp);
            attributePoints = Mathf.Max(0, freeAttributes);
            skillPoints = Mathf.Max(0, freeSkills);
            strengthPoints = Mathf.Max(0, strengthSpent);
            healthPoints = Mathf.Max(0, healthSpent);
            ApplyStats();
            Changed?.Invoke(this);
        }
    }
}
