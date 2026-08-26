using System.Collections;
using UnityEngine;
using BattleOfAges.RTS;

namespace BattleOfAges.Combat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthComponent))]
    public class DummyTargetNode : MonoBehaviour
    {
        [Header("Dummy Configuration")]
        [SerializeField] private int maxHp = 100;
        [SerializeField] private float respawnDelay = 1.5f;

        private HealthComponent healthComponent;
        private Collider dummyCollider;
        private MeshRenderer[] renderers;

        private void Awake()
        {
            healthComponent = GetComponent<HealthComponent>();
            dummyCollider = GetComponent<Collider>();
            renderers = GetComponentsInChildren<MeshRenderer>();
        }

        private void OnEnable()
        {
            if (healthComponent != null)
            {
                healthComponent.InitializeHealth(maxHp);
                healthComponent.OnDeath += HandleDeath;
            }
        }

        private void OnDisable()
        {
            if (healthComponent != null)
            {
                healthComponent.OnDeath -= HandleDeath;
            }
        }

        private void HandleDeath()
        {
            Debug.Log($"[DummyTarget] Манекен {gameObject.name} знищений! Респавн через {respawnDelay}с...");
            StartCoroutine(RespawnRoutine());
        }

        private IEnumerator RespawnRoutine()
        {
            // Hide visual renderers & disable collider
            SetDummyVisible(false);

            yield return new WaitForSeconds(respawnDelay);

            // Calculate random point on terrain within bounds (-40 to 40)
            Vector3 randomPos = new Vector3(
                Random.Range(-35f, 35f),
                0f,
                Random.Range(-35f, 35f)
            );
            randomPos.y = RtsUnit.GetTerrainHeight(randomPos);

            transform.position = randomPos;
            healthComponent.InitializeHealth(maxHp);

            SetDummyVisible(true);
            Debug.Log($"[DummyTarget] Манекен відродився у новій точці: {randomPos}");
        }

        private void SetDummyVisible(bool visible)
        {
            if (dummyCollider != null) dummyCollider.enabled = visible;
            if (renderers != null)
            {
                foreach (var r in renderers)
                {
                    if (r != null) r.enabled = visible;
                }
            }
        }
    }
}
