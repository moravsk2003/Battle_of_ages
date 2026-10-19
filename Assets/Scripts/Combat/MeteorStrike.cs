using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// The mage's Meteor (docs/GAME_DESIGN.md §8.1): falls from the sky on a spot for a second, then hits every enemy of
    /// the caster within its radius for several times the caster's damage, through armour.
    /// </summary>
    public sealed class MeteorStrike : MonoBehaviour
    {
        private const float FallHeight = 30f;
        private static readonly int[] found = new int[256];

        private CombatUnit caster;
        private Vector3 spot;
        private float age;

        public static MeteorStrike Drop(CombatUnit caster, Vector3 spot)
        {
            Material look = Application.isPlaying ? RuntimeShaders.UnlitMaterial(new Color(1f, 0.4f, 0.1f)) : null;
            GameObject go = look != null ? GameObject.CreatePrimitive(PrimitiveType.Sphere) : new GameObject();
            go.name = "Meteor";
            if (look != null)
            {
                Destroy(go.GetComponent<Collider>());
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = look;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.transform.localScale = Vector3.one * 1.6f;
            }
            go.transform.position = spot + Vector3.up * FallHeight;
            MeteorStrike m = go.AddComponent<MeteorStrike>();
            m.caster = caster;
            m.spot = spot;
            return m;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / GameBalance.MeteorDelay);
            transform.position = spot + Vector3.up * FallHeight * (1f - t * t);
            if (t < 1f) return;
            Impact();
            Destroy(gameObject);
        }

        /// <summary>Hits everyone of the caster's enemies within the radius of the spot; returns how many.</summary>
        public int Impact()
        {
            if (caster == null) return 0;
            int damage = Mathf.RoundToInt(caster.AttackDamage * GameBalance.MeteorDamageFactor);
            int n = UnitSpatialGrid.Query(spot, GameBalance.MeteorRadius, caster.EnemyTeam, found);
            int hits = 0;
            for (int i = 0; i < n; i++)
            {
                HealthComponent target = UnitSpatialGrid.Health(found[i]);
                if (target == null || !target.IsAlive) continue;
                caster.DeliverHit(target, damage, ignoreArmor: true);
                hits++;
            }
            return hits;
        }
    }
}
