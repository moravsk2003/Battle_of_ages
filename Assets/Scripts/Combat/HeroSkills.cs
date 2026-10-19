using UnityEngine;
using BattleOfAges.RTS;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// The class skills of a hero (docs/GAME_DESIGN.md §8.1), learnt with skill points:
    /// tank - Regeneration (start: +1% max and +2% current HP per game hour; each further point +1% max or +2% current)
    ///        and Instant heal (5% of max HP, +4% per point up to 25%; every 12 minutes);
    /// archer - Long shot (passive, 2 points: +5% range);
    /// mage - Fire fan (a 60° cone, the mage's damage to each target falling to 50% at the edge, through armour; 10 s);
    /// DD - Dash (1 s of x2 run speed, +1 per point up to x5; every 20 s).
    /// Each class has one active skill (Q).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroSkills : MonoBehaviour
    {
        [SerializeField] private bool regenStart;
        [SerializeField] private int regenMaxPoints, regenCurrentPoints;
        [SerializeField] private int healLevel;     // 0..6
        [SerializeField] private bool longShot;
        [SerializeField] private bool fireFan;
        [SerializeField] private int dashLevel;     // 0..4: x2..x5
        private float readyAt;                      // Time.time when the active skill can be used again

        public bool RegenStart => regenStart;
        public int RegenMaxPoints => regenMaxPoints;
        public int RegenCurrentPoints => regenCurrentPoints;
        public int HealLevel => healLevel;
        public bool LongShot => longShot;
        public bool FireFan => fireFan;
        public int DashLevel => dashLevel;

        private HeroProgress hero;
        private HeroProgress Hero => hero != null ? hero : hero = GetComponent<HeroProgress>();
        private HeroClass Class => Hero != null ? Hero.Class : HeroClass.None;

        // ------------------------------------------------------------------ learning

        public bool LearnRegenStart() => Class == HeroClass.Tank && !regenStart && Pay(1) && Set(() => regenStart = true);
        public bool LearnRegenMax() => Class == HeroClass.Tank && regenStart && Pay(1) && Set(() => regenMaxPoints++);
        public bool LearnRegenCurrent() => Class == HeroClass.Tank && regenStart && Pay(1) && Set(() => regenCurrentPoints++);
        public bool LearnHeal() => Class == HeroClass.Tank && healLevel < GameBalance.HealMaxLevel && Pay(1) && Set(() => healLevel++);
        public bool LearnLongShot() => Class == HeroClass.Archer && !longShot && Pay(GameBalance.LongShotCost) && Set(() => longShot = true);
        public bool LearnFireFan() => Class == HeroClass.Mage && !fireFan && Pay(1) && Set(() => fireFan = true);
        public bool LearnDash() => Class == HeroClass.Dd && dashLevel < GameBalance.DashMaxLevel && Pay(1) && Set(() => dashLevel++);

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
            if (Hero != null)
            {
                Hero.RangeBonus = longShot ? GameBalance.LongShotRangeBonus : 0f;
                Hero.ApplyStats(); // range follows
            }
        }

        // ------------------------------------------------------------------ the active skill (Q)

        public bool HasActive => (Class == HeroClass.Tank && healLevel > 0) || (Class == HeroClass.Mage && fireFan) || (Class == HeroClass.Dd && dashLevel > 0);
        public float CooldownLeft => Mathf.Max(0f, readyAt - Time.time);

        /// <summary>Uses the class's active skill; false if there is none or it is still recharging.</summary>
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
                    CastFireFan(forward);
                    readyAt = Time.time + GameBalance.FireFanCooldown;
                    return true;
                case HeroClass.Dd:
                    CombatUnit combat = GetComponent<CombatUnit>();
                    if (combat != null) combat.Boost(DashFactor, GameBalance.DashSeconds);
                    readyAt = Time.time + GameBalance.DashCooldown;
                    return true;
            }
            return false;
        }

        private static readonly int[] found = new int[256];

        /// <summary>Hits every enemy in the cone in front: the mage's damage, less towards the edge of its range.</summary>
        public int CastFireFan(Vector3 forward)
        {
            CombatUnit combat = GetComponent<CombatUnit>();
            if (combat == null) return 0;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : transform.forward;
            float range = combat.AttackRange;
            int n = UnitSpatialGrid.Query(transform.position, range, UnitSpatialGrid.TeamHostile, found);
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
                combat.DeliverHit(target, Mathf.Max(1, Mathf.RoundToInt(combat.AttackDamage * falloff)), ignoreArmor: true);
                hits++;
            }
            FireFanEffect.Spawn(transform.position + Vector3.up * 1.2f, forward, range);
            return hits;
        }

        // ------------------------------------------------------------------ saves

        public void Restore(bool start, int maxPts, int currentPts, int heal, bool shot, bool fan, int dash)
        {
            regenStart = start; regenMaxPoints = Mathf.Max(0, maxPts); regenCurrentPoints = Mathf.Max(0, currentPts);
            healLevel = Mathf.Clamp(heal, 0, GameBalance.HealMaxLevel); longShot = shot; fireFan = fan;
            dashLevel = Mathf.Clamp(dash, 0, GameBalance.DashMaxLevel);
            ApplyPassives();
        }
    }
}
