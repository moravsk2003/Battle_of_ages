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
    /// archer - 1. Long shot (passive, 2 points: +5% range); 2. Piercing arrow (passive: arrows ignore 10% of armour,
    ///           +5% per point up to 50%);
    /// mage - 1. Fire fan (cast on its own with the attack: a 60° cone, the mage's damage to each target falling to
    ///           50% at the edge, through armour; every 10 s); 2. Meteor (Q: falls on the nearest enemy in range after
    ///           1 s, 3x the mage's damage to everyone within 4 m, through armour; every 20 s);
    /// DD - 1. Dash (Q: 1 s of x2 run speed, +1 per point up to x5; every 20 s); 2. Critical hit (passive: every 5th
    ///           hit deals double damage).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroSkills : MonoBehaviour
    {
        [SerializeField] private bool regenStart;
        [SerializeField] private int regenMaxPoints, regenCurrentPoints;
        [SerializeField] private int healLevel;     // 0..6
        [SerializeField] private bool longShot;
        [SerializeField] private int pierceLevel;   // 0..9: 10%..50% of armour
        [SerializeField] private bool fireFan;
        [SerializeField] private bool meteor;
        [SerializeField] private int dashLevel;     // 0..4: x2..x5
        [SerializeField] private bool criticalHit;
        private float readyAt;                      // Time.time when the active skill (Q) can be used again
        private float fanReadyAt;                   // the fan casts itself with the attack

        public bool RegenStart => regenStart;
        public int RegenMaxPoints => regenMaxPoints;
        public int RegenCurrentPoints => regenCurrentPoints;
        public int HealLevel => healLevel;
        public bool LongShot => longShot;
        public int PierceLevel => pierceLevel;
        public bool FireFan => fireFan;
        public bool Meteor => meteor;
        public int DashLevel => dashLevel;
        public bool CriticalHit => criticalHit;

        private HeroProgress hero;
        private HeroProgress Hero => hero != null ? hero : hero = GetComponent<HeroProgress>();
        private HeroClass Class => Hero != null ? Hero.Class : HeroClass.None;
        private CombatUnit combat;
        private CombatUnit Combat => combat != null ? combat : combat = GetComponent<CombatUnit>();

        private void OnEnable() { if (Combat != null) { Combat.AttackLaunched -= OnAttackLaunched; Combat.AttackLaunched += OnAttackLaunched; } }
        private void OnDisable() { if (Combat != null) Combat.AttackLaunched -= OnAttackLaunched; }

        // ------------------------------------------------------------------ learning

        /// <summary>The tank's Instant heal needs the start of Regeneration and a point in one of its branches.</summary>
        public bool CanLearnHeal => regenStart && regenMaxPoints + regenCurrentPoints > 0;

        public bool LearnRegenStart() => Class == HeroClass.Tank && !regenStart && Pay(1) && Set(() => regenStart = true);
        public bool LearnRegenMax() => Class == HeroClass.Tank && regenStart && Pay(1) && Set(() => regenMaxPoints++);
        public bool LearnRegenCurrent() => Class == HeroClass.Tank && regenStart && Pay(1) && Set(() => regenCurrentPoints++);
        public bool LearnHeal() => Class == HeroClass.Tank && CanLearnHeal && healLevel < GameBalance.HealMaxLevel && Pay(1) && Set(() => healLevel++);
        public bool LearnLongShot() => Class == HeroClass.Archer && !longShot && Pay(GameBalance.LongShotCost) && Set(() => longShot = true);
        public bool LearnPierce() => Class == HeroClass.Archer && longShot && pierceLevel < GameBalance.PierceMaxLevel && Pay(1) && Set(() => pierceLevel++);
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
        /// <summary>Share of the target's armour the archer's arrows go through.</summary>
        public float PierceShare => pierceLevel <= 0 ? 0f : GameBalance.PierceFirst + GameBalance.PierceStep * (pierceLevel - 1);

        public void ApplyPassives()
        {
            HealthComponent health = GetComponent<HealthComponent>();
            if (health != null) { health.RegenMaxPerHour = RegenMaxShare; health.RegenCurrentPerHour = RegenCurrentShare; }
            if (Combat != null)
            {
                Combat.AttackLaunched -= OnAttackLaunched; // (re)subscribe: the fan casts itself with the attack
                Combat.AttackLaunched += OnAttackLaunched;
                Combat.ArmorPierce = PierceShare;
                Combat.CritEveryNth = criticalHit ? GameBalance.CritEveryNth : 0;
            }
            if (Hero != null)
            {
                Hero.RangeBonus = longShot ? GameBalance.LongShotRangeBonus : 0f;
                Hero.ApplyStats(); // range follows
            }
        }

        // ------------------------------------------------------------------ the mage's fan: with the attack

        private void OnAttackLaunched(HealthComponent target)
        {
            if (Class != HeroClass.Mage || !fireFan || target == null || Time.time < fanReadyAt) return;
            fanReadyAt = Time.time + GameBalance.FireFanCooldown;
            CastFireFan(target.transform.position - transform.position);
        }

        // ------------------------------------------------------------------ the active skill (Q)

        public bool HasActive => (Class == HeroClass.Tank && healLevel > 0) || (Class == HeroClass.Mage && meteor) || (Class == HeroClass.Dd && dashLevel > 0);
        public float CooldownLeft => Mathf.Max(0f, readyAt - Time.time);

        /// <summary>Uses the class's active skill; false if there is none, it is recharging, or it has no target.</summary>
        public bool UseActive(Vector3 forward)
        {
            if (!HasActive || CooldownLeft > 0f) return false;
            HealthComponent health = GetComponent<HealthComponent>();
            if (health != null && !health.IsAlive) return false;
            switch (Class)
            {
                case HeroClass.Tank:
                    health.Heal(Mathf.Max(1, Mathf.RoundToInt(health.MaxHealth * HealShare)));
                    readyAt = Time.time + GameBalance.HealCooldown;
                    return true;
                case HeroClass.Mage:
                    if (Combat == null) return false;
                    int enemy = UnitSpatialGrid.FindNearest(transform.position, Combat.AttackRange, Combat.EnemyTeam);
                    if (enemy < 0) return false;
                    MeteorStrike.Drop(Combat, UnitSpatialGrid.Position(enemy));
                    readyAt = Time.time + GameBalance.MeteorCooldown;
                    return true;
                case HeroClass.Dd:
                    if (Combat != null) Combat.Boost(DashFactor, GameBalance.DashSeconds);
                    readyAt = Time.time + GameBalance.DashCooldown;
                    return true;
            }
            return false;
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
                float falloff = 1f - (1f - GameBalance.FireFanEdgeDamage) * Mathf.Clamp01(d / range);
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
            pierceLevel = Mathf.Clamp(pierce, 0, GameBalance.PierceMaxLevel); meteor = meteorLearnt; criticalHit = crit;
            ApplyPassives();
        }
    }
}
