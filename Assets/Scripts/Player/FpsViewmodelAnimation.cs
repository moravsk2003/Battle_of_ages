using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Utils;

namespace BattleOfAges.Player
{
    /// <summary>
    /// First-person arm animation: idle breathing sway, a step bob paced by the body's real speed (by the distance walked,
    /// not the clock: a stride per step, longer and heavier the faster), the arms lagging behind a turn of the view, a dip
    /// on landing from a jump, and keyframed sword swings on the shoulder pivots ("RightArmPivot" / "LeftArmPivot" from
    /// HumanoidModelBuilder.CreateViewmodelArms): anticipation (raise back) -> fast diagonal slash -> follow-through ->
    /// recovery, with the shield bracing. Blows struck one after another alternate: a forehand slash down-left, then a
    /// backhand cut from the left rising to the right.
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

        // The backhand, the second blow of a pair: from the left of the body across and up to the right.
        private static readonly Vector3[] BackEuler =
        {
            Vector3.zero,
            new Vector3(-20f, -30f, 62f),   // wind-up: the hand across to the left, the blade up to the left
            new Vector3(-14f, -8f, -30f),   // strike: the hand at the centre, the blade sweeping flat to the right
            new Vector3(-8f, 4f, -50f),     // follow-through: low on the right
            Vector3.zero
        };
        private static readonly Vector3[] BackOffset =
        {
            Vector3.zero,
            new Vector3(-0.16f, 0.12f, 0.06f),
            new Vector3(-0.04f, 0.10f, 0.08f),
            new Vector3(0.03f, 0.06f, 0.05f),
            Vector3.zero
        };
        /// <summary>A blow started within this many seconds of the last one's end is the other of the pair.</summary>
        private const float ComboWindow = 0.7f;

        private Vector3 restPosition;
        private Quaternion restRotation;
        private Transform rightPivot, leftPivot;
        private Vector3 rightRestPos, leftRestPos;
        private Quaternion rightRestRot, leftRestRot;
        private float movementBlend;
        private float recoil;
        private float swingTime = -1f;   // < 0: not swinging
        private bool posedManually;
        private bool swingAtRest;
        private int swingVariant;          // 0 forehand, 1 backhand
        private float lastSwingEnd = -10f;

        // The body the arms belong to: its speed paces the steps, its landing dips them; the view's turn drags them
        private CharacterController body;
        private float stepPhase, airTime, lastFallSpeed, landDip, landSpeed, airLift;
        private bool wasGrounded = true;
        private Vector3 lastBodyPos;
        private bool bodyKnown;
        private float lastYaw, lastPitch;
        private bool lookKnown;
        private Vector2 lookSway;

        /// <summary>The swing being played or posed: 0 the forehand, 1 the backhand.</summary>
        public int SwingVariant => swingVariant;
        /// <summary>How far the step bob has gone (radians, a step per 2 pi).</summary>
        public float StepPhase => stepPhase;

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
            body = GetComponentInParent<CharacterController>();
            bodyKnown = false;
            swingAtRest = false;
            lookKnown = false;
        }

        /// <summary>Can be invoked by a weapon script after an attack or a shot.</summary>
        public void PlayRecoil(float strength = 1f)
        {
            recoil = Mathf.Clamp01(strength);
        }

        /// <summary>Starts the first-person sword swing.</summary>
        public void PlayPunchAnimation()
        {
            // A blow right after another (or cutting into its recovery) is the other of the pair
            bool chained = swingTime >= 0f || Time.time - lastSwingEnd < ComboWindow;
            swingVariant = chained ? 1 - swingVariant : 0;
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

        /// <summary>Freezes the arms at a swing phase (0..1) for previews; negative = rest pose. variant 1: the backhand.</summary>
        public void PoseSwing(float phase, int variant = 0)
        {
            if (rightPivot == null) CacheRest();
            posedManually = true;
            swingVariant = variant;
            swingTime = phase;
            swingAtRest = false;
            ApplySwing(phase);
        }

        /// <summary>
        /// The body's horizontal speed, by how far it really went since the last frame, and whether it stands on the ground
        /// (no body: the move input, as a walk).
        /// </summary>
        private float BodySpeed(float dt, out bool grounded, out float vertical)
        {
            if (body != null && body.enabled)
            {
                Vector3 pos = body.transform.position;
                Vector3 moved = bodyKnown && dt > 0f ? (pos - lastBodyPos) / dt : Vector3.zero;
                lastBodyPos = pos;
                bodyKnown = true;
                grounded = body.isGrounded;
                vertical = moved.y;
                return Mathf.Min(new Vector2(moved.x, moved.z).magnitude, 30f); // (a teleport is no stride)
            }
            grounded = true;
            vertical = 0f;
            return Mathf.Clamp01(UniversalInput.GetMoveInput().magnitude) * 4f;
        }

        /// <summary>A stride per step: longer the faster (a walk ~1.2 m, a run ~1.8 m).</summary>
        private static float StepLength(float speed) => 0.9f + 0.1f * Mathf.Min(speed, 10f);

        private void LateUpdate()
        {
            if (posedManually) return;

            if (swingTime >= 0f)
            {
                swingTime += Time.deltaTime / swingDuration;
                if (swingTime >= 1f) { swingTime = -1f; lastSwingEnd = Time.time; }
            }
            if (shotTime >= 0f)
            {
                shotTime += Time.deltaTime;
                if (shotTime >= shotDraw + BowRecover + BowRenock) shotTime = -1f;
            }
            if (Bow) ApplyBow(shotTime); else ApplySwing(swingTime);

            float dt = Time.deltaTime;
            float speed = BodySpeed(dt, out bool grounded, out float vertical);

            // Steps: by the distance walked, a bob per step and a sway side to side per stride; heavier when running
            float targetBlend = grounded ? Mathf.Clamp01(speed / 2.5f) : 0f;
            movementBlend = Mathf.MoveTowards(movementBlend, targetBlend, transitionSpeed * 0.5f * dt);
            if (grounded) stepPhase = Mathf.Repeat(stepPhase + speed * dt / StepLength(speed) * Mathf.PI * 2f, Mathf.PI * 4f);
            float amplitude = walkBobAmount * Mathf.Lerp(0.8f, 1.5f, Mathf.InverseLerp(3f, 9f, speed)) * movementBlend;
            float walkWave = Mathf.Sin(stepPhase) * amplitude;
            float sideWave = Mathf.Cos(stepPhase * 0.5f) * amplitude * 0.55f;

            // A jump: the arms float up a little in the air and dip on landing, harder from higher (a damped spring)
            if (!grounded) { airTime += dt; lastFallSpeed = Mathf.Max(0f, -vertical); }
            else
            {
                if (!wasGrounded && airTime > 0.15f) landSpeed -= Mathf.Clamp01(lastFallSpeed / 12f) * 0.9f;
                airTime = 0f;
            }
            wasGrounded = grounded;
            landSpeed += (-landDip * 120f - landSpeed * 16f) * dt;
            landDip += landSpeed * dt;
            airLift = Mathf.MoveTowards(airLift, grounded ? 0f : 0.012f, 0.08f * dt);

            // The view turning: the arms lag behind it (by its speed, a few degrees at most) and catch up
            Transform view = transform.parent;
            if (view != null && dt > 0f)
            {
                Vector3 e = view.eulerAngles;
                Vector2 target = Vector2.zero;
                if (lookKnown)
                    target = new Vector2(Mathf.Clamp(-Mathf.DeltaAngle(lastYaw, e.y) / dt * 0.02f, -5f, 5f),
                                         Mathf.Clamp(-Mathf.DeltaAngle(lastPitch, e.x) / dt * 0.02f, -4f, 4f));
                lastYaw = e.y; lastPitch = e.x; lookKnown = true;
                lookSway = Vector2.Lerp(lookSway, target, 1f - Mathf.Exp(-12f * dt));
            }

            float time = Time.time;
            float calm = 1f - 0.5f * movementBlend; // breathing shows less while walking
            float idlePitch = Mathf.Sin(time * idleSwaySpeed) * idleSwayAmount * calm;
            float idleYaw = Mathf.Cos(time * idleSwaySpeed * 0.78f) * idleSwayAmount * calm;
            recoil = Mathf.MoveTowards(recoil, 0f, 4f * dt);

            transform.localPosition = Vector3.Lerp(transform.localPosition,
                restPosition + new Vector3(sideWave, walkWave + landDip + airLift, -recoil * 0.04f), transitionSpeed * dt);
            transform.localRotation = Quaternion.Slerp(transform.localRotation,
                restRotation * Quaternion.Euler(idlePitch - walkWave * 45f - landDip * 60f + lookSway.y, idleYaw + lookSway.x, sideWave * 35f + lookSway.x * 0.5f),
                transitionSpeed * dt);
        }

        private void ApplySwing(float t)
        {
            if (rightPivot == null) return;
            if (t < 0f)
            {
                if (swingAtRest) return;
                swingAtRest = true;
                rightPivot.localPosition = rightRestPos;
                rightPivot.localRotation = rightRestRot;
                if (leftPivot != null) { leftPivot.localPosition = leftRestPos; leftPivot.localRotation = leftRestRot; }
                return;
            }
            swingAtRest = false;

            Sample(t, swingVariant, out Vector3 euler, out Vector3 offset);
            rightPivot.localPosition = rightRestPos + offset;
            rightPivot.localRotation = rightRestRot * Quaternion.Euler(euler);

            if (leftPivot != null)
            {
                // Shield arm braces in and up while the sword swings, then relaxes.
                float brace = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * (swingVariant == 1 ? 1.8f : 1f); // the backhand starts on the shield side
                leftPivot.localPosition = leftRestPos + new Vector3(-0.04f, -0.03f, 0f) * brace; // shield moves out of the blade path
                leftPivot.localRotation = leftRestRot * Quaternion.Euler(6f * brace, -8f * brace, 6f * brace);
            }
        }

        private static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        /// <summary>The bow at this moment of a shot (seconds; negative: held low, ready).</summary>
        private void ApplyBow(float t)
        {
            if (rightPivot == null || leftPivot == null) return;
            swingAtRest = false;
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
        private static void Sample(float t, int variant, out Vector3 euler, out Vector3 offset)
        {
            Vector3[] eulers = variant == 1 ? BackEuler : KeyEuler, offsets = variant == 1 ? BackOffset : KeyOffset;
            int k = 0;
            while (k < KeyTime.Length - 2 && t > KeyTime[k + 1]) k++;
            float u = Mathf.InverseLerp(KeyTime[k], KeyTime[k + 1], t);
            u = k == 1 ? u * u * u * (u * (6f * u - 15f) + 10f) : u * u * (3f - 2f * u); // strike: smootherstep
            euler = Vector3.Lerp(eulers[k], eulers[k + 1], u);
            offset = Vector3.Lerp(offsets[k], offsets[k + 1], u);
        }
    }
}
