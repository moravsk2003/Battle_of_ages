using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Utils;

namespace BattleOfAges.Player
{
    /// <summary>
    /// First-person arm animation: idle breathing sway, walk bob, and a keyframed sword swing on the shoulder pivots
    /// ("RightArmPivot" / "LeftArmPivot" from HumanoidModelBuilder.CreateViewmodelArms):
    /// anticipation (raise back) -> fast diagonal slash -> follow-through -> recovery, with the shield bracing.
    /// With a bow (FpsViewmodelGear.HoldsBow) the bow is in the left fist and a shot is drawn instead: the bow comes up
    /// to the aim, the right hand draws the string back to the cheek over the wind-up, the arrow leaves, the bow kicks and
    /// comes down, and a new arrow is nocked.
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

        // Bow: the left fist up to the aim (pivot offset and turn), the shot's timeline in seconds.
        private static readonly Vector3 BowAimOffset = new Vector3(0.27f, 0.27f, 0.04f);
        private static readonly Vector3 BowAimEuler = new Vector3(-4f, 10f, 0f);
        private static readonly Vector3 BowRestOffset = new Vector3(0.10f, 0.02f, 0f);
        private const float BowAimIn = 0.18f, BowRelease = 0.06f, BowRecover = 0.45f, BowRenock = 0.35f;
        private FpsViewmodelGear held;
        private Transform swordMarker;
        private float shotTime = -1f, shotDraw = 0.5f;

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
            held = GetComponent<FpsViewmodelGear>();
            swordMarker = rightPivot != null ? rightPivot.Find("Sword") : null;
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

        /// <summary>Starts a bow shot: the string is drawn over drawSeconds (the wind-up), then the arrow leaves.</summary>
        public void PlayShotAnimation(float drawSeconds)
        {
            shotTime = 0f;
            shotDraw = Mathf.Max(0.05f, drawSeconds);
            swingTime = -1f;
        }

        private bool Bow { get { if (held == null) held = GetComponent<FpsViewmodelGear>(); return held != null && held.HoldsBow; } } // (the gear is bound after Awake)

        /// <summary>Freezes the arms at a bow-shot moment (seconds from the start; negative = rest) for previews.</summary>
        public void PoseShot(float seconds, float drawSeconds = 0.5f)
        {
            if (rightPivot == null) CacheRest();
            posedManually = true;
            shotDraw = drawSeconds;
            ApplyBow(seconds);
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
            if (shotTime >= 0f)
            {
                shotTime += Time.deltaTime;
                if (shotTime >= shotDraw + BowRecover + BowRenock) shotTime = -1f;
            }
            if (Bow) ApplyBow(shotTime); else ApplySwing(swingTime);

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

        private static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        /// <summary>The bow at this moment of a shot (seconds; negative: held low, ready).</summary>
        private void ApplyBow(float t)
        {
            if (rightPivot == null || leftPivot == null) return;
            float aim, draw, kick = 0f;
            bool arrow = true;
            if (t < 0f) { aim = 0f; draw = 0f; }
            else if (t < shotDraw)
            {
                aim = Smooth(t / Mathf.Min(BowAimIn, shotDraw));
                draw = Smooth(t / shotDraw);
                draw = draw * draw * (2f - draw); // the pull is hardest at the end
            }
            else
            {
                float r = t - shotDraw;
                draw = 1f - Smooth(r / BowRelease);             // the string snaps forward
                arrow = r > BowRecover;                          // a new arrow once the bow is down
                kick = Mathf.Sin(Mathf.Clamp01(r / 0.2f) * Mathf.PI) * (1f - Mathf.Clamp01(r / 0.2f) * 0.5f);
                aim = 1f - Smooth((r - 0.12f) / BowRecover);
            }

            // Left fist: from low-left up to the aim, a little kick of the bow on release
            leftPivot.localPosition = leftRestPos + BowRestOffset + BowAimOffset * aim + new Vector3(0f, 0.012f, -0.02f) * kick;
            leftPivot.localRotation = leftRestRot * Quaternion.Euler(BowAimEuler * aim + new Vector3(-7f, 0f, 3f) * kick);
            held.SetDraw(draw, arrow);

            // Right fist: on the string at the nock (drawn back towards the cheek), else lowered
            Vector3 hand = swordMarker != null ? swordMarker.localPosition : new Vector3(0.02f, 0.13f, 0.47f);
            Quaternion turn = rightRestRot * Quaternion.Euler(new Vector3(-10f, -35f, -70f) * aim);
            Vector3 onString = held.NockInRoot(t >= shotDraw ? 1f : draw) + new Vector3(0.025f, -0.02f, 0f); // after the release the hand stays back by the cheek
            Vector3 lowered = rightRestPos + new Vector3(0.04f, -0.10f, -0.04f);
            Vector3 grip = onString - turn * hand;
            rightPivot.localRotation = turn;
            rightPivot.localPosition = Vector3.Lerp(lowered, grip, aim);
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
