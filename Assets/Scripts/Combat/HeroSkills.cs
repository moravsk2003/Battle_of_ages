using UnityEngine;
using BattleOfAges.RTS;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// The class skills of a hero (docs/GAME_DESIGN.md §8.1), learnt with skill points. Each class has two skills in a
    /// branch: the second can be learnt only after the first.
    /// tank - 1. Regeneration (passive; start: +1% max and +2% current HP per game hour, each further point +1% max or
    ///           +2% current); 2. Instant heal (Q; 5% of max HP, +4% per point up to 25%; every 12 minutes) - after the
    ///           start of Regeneration and at least one point in one of its branches;
    /// archer - 1. Long shot (passive, 2 points: +5% range); 2. Piercing arrow (Q: the next shot hits for 200% and goes on
    ///           through: 100% to the enemy behind, 50% to the next; every 100 s);
    /// mage - 1. Fire fan (Q: a 60° cone, 60% of the mage's damage to each target falling to half at the edge, through
    ///           armour; every 10 s); 2. Meteor (R: falls on the nearest enemy in range after 1 s, 5x the mage's damage to
    ///           everyone within 2.5 m, through armour; every 20 s). Both are cast like an attack: a wind-up before, standing after;
    /// DD - 1. Dash (Q: 1 s of x2 run speed, +1 per point up to x5; every 20 s); 2. Critical hit (R: the next hit deals
    ///           double damage; every 40 s).
    /// The first active skill is on Q, the second on R.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroSkills : MonoBehaviour
    {
        [SerializeField] private bool regenStart;
        [SerializeField] private int regenMaxPoints, regenCurrentPoints;
        [SerializeField] private int healLevel;     // 0..6
        [SerializeField] private bool longShot;
        [SerializeField] private bool piercingArrow;
        [SerializeField] private bool fireFan;
        [SerializeField] private bool meteor;
        [SerializeField] private int dashLevel;     // 0..4: x2..x5
        [SerializeField] private bool criticalHit;
        private readonly float[] readyAt = new float[2]; // Time.time when each active skill (Q, R) can be used again

        public bool RegenStart => regenStart;
        public int RegenMaxPoints => regenMaxPoints;
        public int RegenCurrentPoints => regenCurrentPoints;
        public int HealLevel => healLevel;
        public bool LongShot => longShot;
        public bool PiercingArrow => piercingArrow;
        public bool FireFan => fireFan;
        public bool Meteor => meteor;
        public int DashLevel => dashLevel;
        public bool CriticalHit => criticalHit;

        private HeroProgress hero;
        private HeroProgress Hero => hero != null ? hero : hero = GetComponent<HeroProgress>();
        private HeroClass Class => Hero != null ? Hero.Class : HeroClass.None;
        private CombatUnit combat;
        private CombatUnit Combat => combat != null ? combat : combat = GetComponent<CombatUnit>();

        // ------------------------------------------------------------------ learning

        /// <summary>The tank's Instant heal needs the start of Regeneration and a point in one of its branches.</summary>
        public bool CanLearnHeal => regenStart && regenMaxPoints + regenCurrentPoints > 0;

        public bool LearnRegenStart() => Class == HeroClass.Tank && !regenStart && Pay(1) && Set(() => regenStart = true);
        public bool LearnRegenMax() => Class == HeroClass.Tank && regenStart && Pay(1) && Set(() => regenMaxPoints++);
        public bool LearnRegenCurrent() => Class == HeroClass.Tank && regenStart && Pay(1) && Set(() => regenCurrentPoints++);
        public bool LearnHeal() => Class == HeroClass.Tank && CanLearnHeal && healLevel < GameBalance.HealMaxLevel && Pay(1) && Set(() => healLevel++);
        public bool LearnLongShot() => Class == HeroClass.Archer && !longShot && Pay(GameBalance.LongShotCost) && Set(() => longShot = true);
        public bool LearnPierce() => Class == HeroClass.Archer && longShot && !piercingArrow && Pay(1) && Set(() => piercingArrow = true);
        public bool LearnFireFan() => Class == HeroClass.Mage && !fireFan && Pay(1) && Set(() => fireFan = true);
        public bool LearnMeteor() => Class == HeroClass.Mage && fireFan && !meteor && Pay(1) && Set(() => meteor = true);
        public bool LearnDash() => Class == HeroClass.Dd && dashLevel < GameBalance.DashMaxLevel && Pay(1) && Set(() => dashLevel++);
        public bool LearnCriticalHit() => Class == HeroClass.Dd && dashLevel > 0 && !criticalHit && Pay(1) && Set(() => criticalHit = true);

        private bool Pay(int points) => Hero != null && Hero.TakeSkillPoints(points);
        private bool Set(System.Action change) { change(); ApplyPassives(); return true; }

        // ------------------------------------------------------------------ passives

        /// <summary>Extra regeneration per game hour: shares of max and of current HP.</summary>
        public float RegenMaxShare => regenStart ? GameBalance.RegenStepMax * (1 + regenMaxPoints) : 0f;
        public float RegenCurrentShare => regenStart ? GameBalance.RegenStepCurrent * (1 + regenCurrentPoints) : 0f;
        public float HealShare => healLevel <= 0 ? 0f : GameBalance.HealFirst + GameBalance.HealStep * (healLevel - 1);
        public float DashFactor => dashLevel <= 0 ? 1f : 1f + dashLevel;

        public void ApplyPassives()
        {
            HealthComponent health = GetComponent<HealthComponent>();
            if (health != null) { health.RegenMaxPerHour = RegenMaxShare; health.RegenCurrentPerHour = RegenCurrentShare; }
            if (Combat != null)
            {
            }
            if (Hero != null)
            {
                Hero.RangeBonus = longShot ? GameBalance.LongShotRangeBonus : 0f;
                Hero.ApplyStats(); // range follows
            }
        }

        // ------------------------------------------------------------------ the active skills (Q, R)

        /// <summary>Has an active skill in the slot (0: Q, 1: R).</summary>
        public bool HasActiveIn(int slot) => slot == 0
            ? (Class == HeroClass.Tank && healLevel > 0) || (Class == HeroClass.Mage && fireFan) || (Class == HeroClass.Dd && dashLevel > 0)
              || (Class == HeroClass.Archer && piercingArrow)
            : (Class == HeroClass.Mage && meteor) || (Class == HeroClass.Dd && criticalHit);
        public bool HasActive => HasActiveIn(0) || HasActiveIn(1);
        public float CooldownLeft => CooldownLeftIn(0);
        public float CooldownLeftIn(int slot) => Mathf.Max(0f, readyAt[slot] - Time.time);

        /// <summary>Uses the class's first active skill (Q).</summary>
        public bool UseActive(Vector3 forward) => UseActive(0, forward);

        /// <summary>Uses an active skill (0: Q, 1: R); false if there is none, it is recharging, or it has no target.</summary>
        public bool UseActive(int slot, Vector3 forward)
        {
            if (!HasActiveIn(slot) || CooldownLeftIn(slot) > 0f) return false;
            HealthComponent health = GetComponent<HealthComponent>();
            if (health != null && !health.IsAlive) return false;
            switch (Class)
            {
                case HeroClass.Tank:
                    health.Heal(Mathf.Max(1, Mathf.RoundToInt(health.MaxHealth * HealShare)));
                    readyAt[slot] = Time.time + GameBalance.HealCooldown;
                    return true;
                case HeroClass.Mage:
                    if (Combat == null) return false;
                    if (slot == 0)
                    {
                        Vector3 dir = forward;
                        CastLikeAnAttack(() => CastFireFan(dir));
                        readyAt[slot] = Time.time + GameBalance.FireFanCooldown;
                        return true;
                    }
                    int enemy = UnitSpatialGrid.FindNearest(transform.position, Combat.AttackRange, Combat.EnemyTeam);
                    if (enemy < 0) return false;
                    Vector3 spot = UnitSpatialGrid.Position(enemy);
                    CastLikeAnAttack(() => MeteorStrike.Drop(Combat, spot));
                    readyAt[slot] = Time.time + GameBalance.MeteorCooldown;
                    return true;
                case HeroClass.Archer:
                    if (Combat == null) return false;
                    Combat.NextShotPiercing = true; // the next arrow: x2 and on through
                    readyAt[slot] = Time.time + GameBalance.PierceCooldown;
                    return true;
                case HeroClass.Dd:
                    if (Combat == null) return false;
                    if (slot == 0) { Combat.Boost(DashFactor, GameBalance.DashSeconds); readyAt[slot] = Time.time + GameBalance.DashCooldown; }
                    else { Combat.NextHitCritical = true; readyAt[slot] = Time.time + GameBalance.CritCooldown; }
                    return true;
            }
            return false;
        }

        /// <summary>A spell is cast like the mage's attack: it goes off after the wind-up and the mage stands meanwhile and a moment after.</summary>
        private void CastLikeAnAttack(System.Action effect)
        {
            float windup = GameBalance.MageWindup;
            Combat.Recover(windup + GameBalance.RangedRootSeconds, rooted: true);
            Combat.TriggerAttackAnimation();
            if (windup <= 0f || !Application.isPlaying) { effect(); return; }
            PendingHits.AddAction(windup, () => { if (this != null && Combat != null) effect(); });
        }

        private static readonly int[] found = new int[256];

        /// <summary>Hits every enemy in the cone in front: the mage's damage, less towards the edge of its range.</summary>
        public int CastFireFan(Vector3 forward)
        {
            if (Combat == null) return 0;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : transform.forward;
            float range = Combat.AttackRange;
            int n = UnitSpatialGrid.Query(transform.position, range, Combat.EnemyTeam, found); // the caster's enemies, whichever side it is on
            int hits = 0;
            for (int i = 0; i < n; i++)
            {
                HealthComponent target = UnitSpatialGrid.Health(found[i]);
                if (target == null || !target.IsAlive) continue;
                Vector3 to = target.transform.position - transform.position;
                to.y = 0f;
                float d = to.magnitude;
                if (d > range || (d > 0.01f && Vector3.Angle(forward, to) > GameBalance.FireFanAngle * 0.5f)) continue;
                float falloff = GameBalance.FireFanDamageShare * (1f - (1f - GameBalance.FireFanEdgeDamage) * Mathf.Clamp01(d / range));
                Combat.DeliverHit(target, Mathf.Max(1, Mathf.RoundToInt(Combat.AttackDamage * falloff)), ignoreArmor: true);
                hits++;
            }
            FireFanEffect.Spawn(transform.position + Vector3.up * 1.2f, forward, range);
            return hits;
        }

        // ------------------------------------------------------------------ saves

        public void Restore(bool start, int maxPts, int currentPts, int heal, bool shot, bool fan, int dash,
                            int pierce = 0, bool meteorLearnt = false, bool crit = false)
        {
            regenStart = start; regenMaxPoints = Mathf.Max(0, maxPts); regenCurrentPoints = Mathf.Max(0, currentPts);
            healLevel = Mathf.Clamp(heal, 0, GameBalance.HealMaxLevel); longShot = shot; fireFan = fan;
            dashLevel = Mathf.Clamp(dash, 0, GameBalance.DashMaxLevel);
            piercingArrow = pierce > 0; meteor = meteorLearnt; criticalHit = crit;
            ApplyPassives();
        }
    }
}
