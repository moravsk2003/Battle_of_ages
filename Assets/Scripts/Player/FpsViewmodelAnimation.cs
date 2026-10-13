using UnityEngine;
using BattleOfAges.InputSystem;

namespace BattleOfAges.Player
{
    /// <summary>
    /// First-person arm animation: idle breathing sway, walk bob, and a keyframed sword swing on the shoulder pivots
    /// ("RightArmPivot" / "LeftArmPivot" from HumanoidModelBuilder.CreateViewmodelArms):
    /// anticipation (raise back) -> fast diagonal slash -> follow-through -> recovery, with the shield bracing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FpsViewmodelAnimation : MonoBehaviour
    {
        [SerializeField] private float idleSwayAmount = 1.2f;
        [SerializeField] private float idleSwaySpeed = 1.6f;
        [SerializeField] private float walkBobAmount = 0.035f;
        [SerializeField] private float walkBobSpeed = 8f;
        [SerializeField] private float transitionSpeed = 10f;
        [SerializeField] private float swingDuration = 0.6f;

        // Swing keys for the right shoulder: time (0..1), euler offset, position offset (viewmodel space).
        private static readonly float[] KeyTime = { 0f, 0.24f, 0.42f, 0.56f, 1f };
        private static readonly Vector3[] KeyEuler =
        {
            Vector3.zero,
            new Vector3(-42f, 12f, -30f),   // wind-up: blade raised back over the right shoulder
            new Vector3(-16f, -30f, 58f),   // strike: hand at the centre, blade slashing down-left across the view
            new Vector3(24f, -50f, 76f),    // follow-through: low on the left
            Vector3.zero
        };
        private static readonly Vector3[] KeyOffset =
        {
            Vector3.zero,
            new Vector3(0.05f, 0.07f, -0.05f),
            new Vector3(-0.14f, 0.10f, 0.08f),
            new Vector3(-0.22f, -0.04f, 0.04f),
            Vector3.zero
        };

        private Vector3 restPosition;
        private Quaternion restRotation;
        private Transform rightPivot, leftPivot;
        private Vector3 rightRestPos, leftRestPos;
        private Quaternion rightRestRot, leftRestRot;
        private float movementBlend;
        private float recoil;
        private float swingTime = -1f;   // < 0: not swinging
        private bool posedManually;

        private void Awake() => CacheRest();
        private void OnEnable() => CacheRest();

        private void CacheRest()
        {
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            rightPivot = transform.Find("RightArmPivot");
            leftPivot = transform.Find("LeftArmPivot");
            if (rightPivot != null) { rightRestPos = rightPivot.localPosition; rightRestRot = rightPivot.localRotation; }
            if (leftPivot != null) { leftRestPos = leftPivot.localPosition; leftRestRot = leftPivot.localRotation; }
        }

        /// <summary>Can be invoked by a weapon script after an attack or a shot.</summary>
        public void PlayRecoil(float strength = 1f)
        {
            recoil = Mathf.Clamp01(strength);
        }

        /// <summary>Starts the first-person sword swing.</summary>
        public void PlayPunchAnimation()
        {
            swingTime = 0f;
            recoil = 0.35f;
        }

        /// <summary>Freezes the arms at a swing phase (0..1) for previews; negative = rest pose.</summary>
        public void PoseSwing(float phase)
        {
            if (rightPivot == null) CacheRest();
            posedManually = true;
            swingTime = phase;
            ApplySwing(phase);
        }

        private void LateUpdate()
        {
            if (posedManually) return;

            if (swingTime >= 0f)
            {
                swingTime += Time.deltaTime / swingDuration;
                if (swingTime >= 1f) swingTime = -1f;
            }
            ApplySwing(swingTime);

            Vector2 movement = UniversalInput.GetMoveInput();
            float targetBlend = Mathf.Clamp01(movement.magnitude);
            movementBlend = Mathf.MoveTowards(movementBlend, targetBlend, transitionSpeed * Time.deltaTime);

            float time = Time.time;
            float walkWave = Mathf.Sin(time * walkBobSpeed) * walkBobAmount * movementBlend;
            float sideWave = Mathf.Cos(time * walkBobSpeed * 0.5f) * walkBobAmount * 0.55f * movementBlend;
            float idlePitch = Mathf.Sin(time * idleSwaySpeed) * idleSwayAmount;
            float idleYaw = Mathf.Cos(time * idleSwaySpeed * 0.78f) * idleSwayAmount;
            recoil = Mathf.MoveTowards(recoil, 0f, 4f * Time.deltaTime);

            transform.localPosition = Vector3.Lerp(transform.localPosition,
                restPosition + new Vector3(sideWave, walkWave, -recoil * 0.04f), transitionSpeed * Time.deltaTime);
            transform.localRotation = Quaternion.Slerp(transform.localRotation,
                restRotation * Quaternion.Euler(idlePitch - walkWave * 45f, idleYaw, sideWave * 35f), transitionSpeed * Time.deltaTime);
        }

        private void ApplySwing(float t)
        {
            if (rightPivot == null) return;
            if (t < 0f)
            {
                rightPivot.localPosition = rightRestPos;
                rightPivot.localRotation = rightRestRot;
                if (leftPivot != null) { leftPivot.localPosition = leftRestPos; leftPivot.localRotation = leftRestRot; }
                return;
            }

            Sample(t, out Vector3 euler, out Vector3 offset);
            rightPivot.localPosition = rightRestPos + offset;
            rightPivot.localRotation = rightRestRot * Quaternion.Euler(euler);

            if (leftPivot != null)
            {
                // Shield arm braces in and up while the sword swings, then relaxes.
                float brace = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                leftPivot.localPosition = leftRestPos + new Vector3(-0.04f, -0.03f, 0f) * brace; // shield moves out of the blade path
                leftPivot.localRotation = leftRestRot * Quaternion.Euler(6f * brace, -8f * brace, 6f * brace);
            }
        }

        /// <summary>Piecewise eased interpolation between the swing keys (fast in the strike segment).</summary>
        private static void Sample(float t, out Vector3 euler, out Vector3 offset)
        {
            int k = 0;
            while (k < KeyTime.Length - 2 && t > KeyTime[k + 1]) k++;
            float u = Mathf.InverseLerp(KeyTime[k], KeyTime[k + 1], t);
            u = k == 1 ? u * u * u * (u * (6f * u - 15f) + 10f) : u * u * (3f - 2f * u); // strike: smootherstep
            euler = Vector3.Lerp(KeyEuler[k], KeyEuler[k + 1], u);
            offset = Vector3.Lerp(KeyOffset[k], KeyOffset[k + 1], u);
        }
    }
}
