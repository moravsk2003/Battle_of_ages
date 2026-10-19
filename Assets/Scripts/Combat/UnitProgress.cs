using System;
using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// A unit's rank and its battle experience (docs/GAME_DESIGN.md §11.1). Experience comes only from battle events
    /// (+1 per hit, +10 per kill, +5 for surviving a fight) and is checked only when it is added: a person becomes a
    /// warrior at 30, a warrior a knight at 100, a knight a general at 500 (counted within the rank). People also become
    /// warriors from work (TownHall rolls that once per game hour). Heroes keep their rank: it changes only by death.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitProgress : MonoBehaviour
    {
        [SerializeField] private UnitRank rank;
        [SerializeField] private bool isHero;
        [SerializeField] private int battleXp;
        private float lastSurvivalAward = float.NegativeInfinity;

        public UnitRank Rank => rank;
        public bool IsHero => isHero;
        public int BattleXp => battleXp;

        /// <summary>Raised after the unit moved up a rank.</summary>
        public event Action<UnitProgress> Promoted;

        /// <summary>Gives the unit this rank (base stats of the rank under its worn kit) and remembers it.</summary>
        public static UnitProgress Setup(GameObject unit, UnitRank rank, bool hero = false)
        {
            UnitProgress p = unit.GetComponent<UnitProgress>();
            if (p == null) p = unit.AddComponent<UnitProgress>();
            p.rank = rank;
            p.isHero = hero;
            p.battleXp = 0;
            p.ApplyStats();
            return p;
        }

        /// <summary>A loaded save: the rank and the experience gathered in it.</summary>
        public static UnitProgress Restore(GameObject unit, UnitRank rank, int battleXp)
        {
            UnitProgress p = Setup(unit, rank);
            p.battleXp = Mathf.Max(0, battleXp);
            return p;
        }

        /// <summary>Battle experience needed to leave this rank (0: the top rank).</summary>
        public static int XpToPromote(UnitRank rank) =>
            rank == UnitRank.General ? 0 : GameBalance.PromotionXp[(int)rank];

        public void OnHit() => AddBattleXp(GameBalance.XpPerHit);
        public void OnKill() => AddBattleXp(GameBalance.XpPerKill);

        /// <summary>A fight ended and the unit is alive (at most once per survival window).</summary>
        public void OnFightSurvived()
        {
            if (Time.time - lastSurvivalAward < GameBalance.SurvivalAwardWindow) return;
            lastSurvivalAward = Time.time;
            AddBattleXp(GameBalance.XpPerSurvival);
        }

        public void AddBattleXp(int xp)
        {
            if (isHero || xp <= 0 || rank == UnitRank.General) return;
            battleXp += xp;
            if (battleXp >= XpToPromote(rank)) Promote();
        }

        /// <summary>Moves one rank up: the stats of the new rank, the same share of HP; the experience starts over.</summary>
        public void Promote()
        {
            if (isHero || rank == UnitRank.General) return;
            rank++;
            battleXp = 0;
            ApplyStats();
            Promoted?.Invoke(this);
        }

        private void ApplyStats()
        {
            HealthComponent health = GetComponent<HealthComponent>();
            float share = health != null && health.MaxHealth > 0 ? (float)health.CurrentHealth / health.MaxHealth : 1f;
            Equipment gear = GetComponent<Equipment>();
            CombatUnit combat = GetComponent<CombatUnit>();
            float cooldown = gear != null ? gear.BaseCooldown : combat != null ? combat.AttackCooldown : 2f;
            float range = gear != null ? gear.BaseRange : combat != null ? combat.AttackRange : 3f;
            UnitRanks.Apply(gameObject, rank, isHero, cooldown, range);
            if (health != null && health.IsAlive) health.SetCurrentHealth(Mathf.Max(1, Mathf.RoundToInt(share * health.MaxHealth)));
        }
    }
}
