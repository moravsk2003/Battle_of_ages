using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// What happens to a fallen unit (AI or RTS, never the player): it stops blocking and thinking at once (collider,
    /// AI, combat off), the death animation plays, then animation and foot IK stop, the body lies for a while, sinks
    /// into the ground and is removed. After a 500 vs 500 battle this keeps hundreds of corpses from costing animation,
    /// physics and draw calls forever.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitCorpse : MonoBehaviour
    {
        public const float FreezeAfter = 2.5f, SinkAfter = 14f, SinkDuration = 3f;

        private float deathTime = -1f;
        private bool frozen;

        public static void AttachTo(GameObject unit)
        {
            if (unit.GetComponent<UnitCorpse>() == null) unit.AddComponent<UnitCorpse>();
        }

        private void Awake()
        {
            HealthComponent health = GetComponent<HealthComponent>();
            if (health != null) health.OnDeath += HandleDeath;
            enabled = false; // only ticks while dead
        }

        private void HandleDeath()
        {
            deathTime = Time.time;
            enabled = true;
            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false; // the living walk over the fallen
            foreach (MonoBehaviour b in GetComponents<MonoBehaviour>())
            {
                if (b == this || b is HealthComponent) continue;
                b.enabled = false; // AI, RTS movement, combat, wander, gatherer
            }
        }

        private void Update()
        {
            if (deathTime < 0f) return;
            float t = Time.time - deathTime;
            if (!frozen && t >= FreezeAfter)
            {
                frozen = true;
                foreach (Animator a in GetComponentsInChildren<Animator>()) a.enabled = false; // keeps the last (dead) pose
                foreach (FootIK ik in GetComponentsInChildren<FootIK>()) ik.enabled = false;
                foreach (CharacterAnimationDriver d in GetComponentsInChildren<CharacterAnimationDriver>()) d.enabled = false;
                foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (t >= SinkAfter)
            {
                transform.position += Vector3.down * (1.2f / SinkDuration) * Time.deltaTime;
                if (t >= SinkAfter + SinkDuration) Destroy(gameObject);
            }
        }
    }
}
