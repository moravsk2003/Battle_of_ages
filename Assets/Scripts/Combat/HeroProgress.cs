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
    public sealed class HeroProgress : MonoBehaviour, IStatModifier
    {
        [SerializeField] private int level = 1;
        [SerializeField] private int xp;
        [SerializeField] private int attributePoints, skillPoints;
        [SerializeField] private int strengthPoints, healthPoints; // attribute points spent on each
        [SerializeField] private HeroClass heroClass;

        public int Level => level;
        public int Xp => xp;
        public int AttributePoints => attributePoints;
        public int SkillPoints => skillPoints;
        public int StrengthPoints => strengthPoints;
        public int HealthPoints => healthPoints;
        public int XpToNextLevel => XpForLevelUp(level);
        public HeroClass Class => heroClass;
        /// <summary>Extra share of range for a ranged class (the archer's Long shot).</summary>
        public float RangeBonus { get; set; }

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
            HeroArtifacts bag = GetComponent<HeroArtifacts>();
            if (bag != null) amount = Mathf.RoundToInt(amount * (1f + bag.XpBonus)); // the Wisdom artifact
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


        // ------------------------------------------------------------------ class (§8.1)

        /// <summary>Picks the class once (the player at the start, NPC heroes at random); false if it is already set.</summary>
        public bool ChooseClass(HeroClass c)
        {
            if (heroClass != HeroClass.None || c == HeroClass.None) return false;
            heroClass = c;
            ApplyStats();
            Changed?.Invoke(this);
            return true;
        }

        float IStatModifier.HealthFactor => HeroClassRules.HealthFactor(heroClass);
        float IStatModifier.StrengthFactor(bool bow) => HeroClassRules.StrengthFactor(heroClass, bow);
        float IStatModifier.CooldownFactor(bool bow) => HeroClassRules.CooldownFactor(heroClass, bow);

        float IStatModifier.WeaponMultiplier(float worn) { HeroArtifacts a = GetComponent<HeroArtifacts>(); return a != null ? a.WeaponMultiplier(worn) : worn; }
        float IStatModifier.DamageReduction(float worn) { HeroArtifacts a = GetComponent<HeroArtifacts>(); return a != null ? a.DamageReduction(worn) : worn; }

        float IStatModifier.Range(float range, bool bow, float strength)
        {
            if (!HeroClassRules.IsRanged(heroClass, bow)) return range;
            if (combat == null) combat = GetComponent<CombatUnit>();
            float speed = CharacterSpeed.Of(combat != null ? combat.SpeedModifier : CharacterSpeed.HeroModifier, strength);
            return HeroClassRules.Range(speed, RangeBonus);
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
            if (health != null) health.IgnoreEveryNthHit = HeroClassRules.IgnoredHitEvery(heroClass);
            CombatUnit attack = GetComponent<CombatUnit>();
            if (attack != null) attack.FiresBolts = heroClass == HeroClass.Mage;
            float share = health != null && health.MaxHealth > 0 ? (float)health.CurrentHealth / health.MaxHealth : 1f;
            int hp = UnitRanks.Health(rank.Rank, hero: true) + healthPoints * GameBalance.HealthPerPoint;
            int strength = UnitRanks.Strength(rank.Rank, hero: true) + strengthPoints * GameBalance.StrengthPerPoint;
            gear.SetBaseStats(hp, strength, gear.BaseCooldown, gear.BaseRange);
            if (health != null && health.IsAlive) health.SetCurrentHealth(Mathf.Max(1, Mathf.RoundToInt(share * health.MaxHealth)));
        }

        /// <summary>A new hero after the old one died: level 1, no points, the class to be chosen anew (§7.2).</summary>
        public void ResetForNewHero()
        {
            level = 1;
            xp = attributePoints = skillPoints = strengthPoints = healthPoints = 0;
            heroClass = HeroClass.None;
            RangeBonus = 0f;
            ApplyStats();
            Changed?.Invoke(this);
        }

        /// <summary>A loaded save.</summary>
        public void Restore(int savedLevel, int savedXp, int freeAttributes, int freeSkills, int strengthSpent, int healthSpent, HeroClass savedClass)
        {
            heroClass = savedClass;
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
