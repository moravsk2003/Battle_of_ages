using UnityEngine;
using BattleOfAges.InputSystem;

namespace BattleOfAges.Player
{
    /// <summary>Small camera-relative idle and walk animation for the procedural first-person arms.</summary>
    [DisallowMultipleComponent]
    public sealed class FpsViewmodelAnimation : MonoBehaviour
    {
        [SerializeField] private float idleSwayAmount = 1.2f;
        [SerializeField] private float idleSwaySpeed = 1.6f;
        [SerializeField] private float walkBobAmount = 0.035f;
        [SerializeField] private float walkBobSpeed = 8f;
        [SerializeField] private float transitionSpeed = 10f;

        private Vector3 restPosition;
        private Quaternion restRotation;
        private float movementBlend;
        private float recoil;

        private void Awake()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        private void OnEnable()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
        }

        private void LateUpdate()
        {
            Vector2 movement = UniversalInput.GetMoveInput();
            float targetBlend = Mathf.Clamp01(movement.magnitude);
            movementBlend = Mathf.MoveTowards(movementBlend, targetBlend, transitionSpeed * Time.deltaTime);

            float time = Time.time;
            float walkWave = Mathf.Sin(time * walkBobSpeed) * walkBobAmount * movementBlend;
            float sideWave = Mathf.Cos(time * walkBobSpeed * 0.5f) * walkBobAmount * 0.55f * movementBlend;
            float idlePitch = Mathf.Sin(time * idleSwaySpeed) * idleSwayAmount;
            float idleYaw = Mathf.Cos(time * idleSwaySpeed * 0.78f) * idleSwayAmount;

            recoil = Mathf.MoveTowards(recoil, 0f, 8f * Time.deltaTime);
            transform.localPosition = Vector3.Lerp(
                transform.localPosition,
                restPosition + new Vector3(sideWave, walkWave, -recoil * 0.06f),
                transitionSpeed * Time.deltaTime);
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation,
                restRotation * Quaternion.Euler(idlePitch - walkWave * 45f, idleYaw, sideWave * 35f),
                transitionSpeed * Time.deltaTime);
        }

        /// <summary>Can be invoked by a future weapon script after an attack or a shot.</summary>
        public void PlayRecoil(float strength = 1f)
        {
            recoil = Mathf.Clamp01(strength);
        }
    }
}
