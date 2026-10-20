using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// One homing bolt of the mage's attack (docs/GAME_DESIGN.md §8.1): a volley of 5 bolts, each 20% of the damage,
    /// that fly after the target and go through armour. The hit is the caster's (experience, kills).
    /// </summary>
    public sealed class MageBolt : MonoBehaviour
    {
        private const float Speed = 24f, HitDistance = 0.6f, MaxLife = 4f;

        private CombatUnit caster;
        private HealthComponent target;
        private int damage;
        private float life;
        private Vector3 velocity;

        /// <summary>Launches the volley from the caster's chest, fanned out a little.</summary>
        public static void Volley(CombatUnit caster, HealthComponent target, int totalDamage)
        {
            int bolts = Mathf.Max(1, GameBalance.MageBolts);
            int each = Mathf.Max(1, Mathf.RoundToInt(totalDamage * GameBalance.MageBoltShare));
            Vector3 origin = caster.transform.position + Vector3.up * 1.4f;
            Vector3 toTarget = (target.transform.position + Vector3.up - origin).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toTarget).normalized;
            for (int i = 0; i < bolts; i++)
            {
                float t = bolts == 1 ? 0f : i / (bolts - 1f) - 0.5f;
                Vector3 dir = (toTarget + side * t * 0.9f + Vector3.up * 0.25f).normalized;
                Create(caster, target, each, origin, dir * Speed);
            }
        }

        private static void Create(CombatUnit caster, HealthComponent target, int damage, Vector3 origin, Vector3 velocity)
        {
            if (!Application.isPlaying) { caster.DeliverHit(target, damage, ignoreArmor: true); return; } // edit-mode tests: hit at once
            // The look first: without its shader the bolt still flies and hits, it is just not drawn
            Material look = RuntimeShaders.UnlitMaterial(new Color(0.55f, 0.75f, 1f));
            GameObject go = look != null ? GameObject.CreatePrimitive(PrimitiveType.Sphere) : new GameObject();
            go.name = "MageBolt";
            if (look != null)
            {
                Object.Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = look;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.22f;
            MageBolt b = go.AddComponent<MageBolt>();
            b.caster = caster;
            b.target = target;
            b.damage = damage;
            b.velocity = velocity;
        }

        /// <summary>The nearest living enemy of the caster within its range of this bolt, or null.</summary>
        private HealthComponent NextTarget()
        {
            if (caster == null) return null;
            int id = RTS.UnitSpatialGrid.FindNearestEnemy(transform.position, caster.AttackRange, caster.Team);
            return id >= 0 ? RTS.UnitSpatialGrid.Health(id) : null;
        }

        private void Update()
        {
            life += Time.deltaTime;
            if (target == null || !target.IsAlive) target = NextTarget(); // the target fell: fly on to the nearest enemy
            if (target == null || life > MaxLife) { Destroy(gameObject); return; }

            Vector3 aim = target.transform.position + Vector3.up;
            Vector3 to = aim - transform.position;
            if (to.magnitude <= HitDistance)
            {
                if (caster != null) caster.DeliverHit(target, damage, ignoreArmor: true);
                else target.TakeDamage(damage, ignoreArmor: true);
                Destroy(gameObject);
                return;
            }
            // Homing: turn the velocity towards the target, faster as it gets close
            velocity = Vector3.RotateTowards(velocity, to.normalized * Speed, 8f * Time.deltaTime, 0f);
            transform.position += velocity * Time.deltaTime;
        }
    }
}
