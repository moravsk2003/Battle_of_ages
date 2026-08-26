using UnityEngine;

namespace BattleOfAges.Combat
{
    [DisallowMultipleComponent]
    public class DummyWiggleAnimation : MonoBehaviour
    {
        private HealthComponent healthComponent;
        private Transform visualPivot;

        private float wobbleAngle = 0f;
        private float wobbleVelocity = 0f;
        private Quaternion restRotation;

        private void Awake()
        {
            healthComponent = GetComponent<HealthComponent>();
            visualPivot = transform.Find("VisualModel");
            if (visualPivot == null) visualPivot = transform;

            restRotation = visualPivot.localRotation;
        }

        private void OnEnable()
        {
            if (healthComponent == null) healthComponent = GetComponent<HealthComponent>();
            if (healthComponent != null)
            {
                healthComponent.OnTakeDamage += HandleHitWobble;
            }
        }

        private void OnDisable()
        {
            if (healthComponent != null)
            {
                healthComponent.OnTakeDamage -= HandleHitWobble;
            }
        }

        private void HandleHitWobble(int damage)
        {
            // Trigger physical spring wobble on hit
            wobbleVelocity = 28f;
            wobbleAngle = 18f;
        }

        private void Update()
        {
            if (Mathf.Abs(wobbleAngle) > 0.05f || Mathf.Abs(wobbleVelocity) > 0.05f)
            {
                // Spring physics simulation
                float springForce = -wobbleAngle * 40f;
                float damping = -wobbleVelocity * 6.0f;
                wobbleVelocity += (springForce + damping) * Time.deltaTime;
                wobbleAngle += wobbleVelocity * Time.deltaTime;

                if (visualPivot != null)
                {
                    visualPivot.localRotation = restRotation * Quaternion.Euler(wobbleAngle, 0f, wobbleAngle * 0.6f);
                }
            }
            else
            {
                wobbleAngle = 0f;
                wobbleVelocity = 0f;
                if (visualPivot != null) visualPivot.localRotation = restRotation;
            }
        }
    }
}
