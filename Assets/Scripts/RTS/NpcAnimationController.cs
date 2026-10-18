using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.RTS
{
    [DisallowMultipleComponent]
    public class NpcAnimationController : MonoBehaviour
    {
        private Animator animator;
        private Animation legacyAnimation;
        private CharacterController characterController;
        private Vector3 lastPosition;
        private Transform visualModel;

        private float currentSpeed;
        private Vector3 initialVisualLocalPos;
        private Quaternion initialVisualLocalRot;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            lastPosition = transform.position;
            FindVisualModelReferences();
        }

        private void Start()
        {
            FindVisualModelReferences();
        }

        private static readonly int IsWalkingHash = Animator.StringToHash("IsWalking");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private bool hasWalkClip = false;
        private bool hasIdleClip = false;
        private bool riggedDriver; // rigged models are animated by Utils.CharacterAnimationDriver

        private void FindVisualModelReferences()
        {
            if (visualModel == null)
            {
                visualModel = transform.Find("VisualModel");
            }

            if (visualModel != null)
            {
                riggedDriver = visualModel.GetComponent<CharacterAnimationDriver>() != null;
                if (animator == null) animator = visualModel.GetComponentInChildren<Animator>();
                if (legacyAnimation == null) legacyAnimation = visualModel.GetComponentInChildren<Animation>();

                if (legacyAnimation != null)
                {
                    hasWalkClip = legacyAnimation.GetClip("Walk") != null;
                    hasIdleClip = legacyAnimation.GetClip("Idle") != null;
                }

                initialVisualLocalPos = visualModel.localPosition;
                initialVisualLocalRot = visualModel.localRotation;
            }
        }

        private void Update()
        {
            if (visualModel == null) FindVisualModelReferences();
            if (riggedDriver) return;

            // Calculate current movement speed
            Vector3 delta = transform.position - lastPosition;
            delta.y = 0f;
            currentSpeed = delta.magnitude / Mathf.Max(0.0001f, Time.deltaTime);
            lastPosition = transform.position;

            bool isWalking = currentSpeed > 0.2f;

            // 1. Mecanim Animator parameters
            if (animator != null && animator.enabled)
            {
                animator.SetBool(IsWalkingHash, isWalking);
                animator.SetFloat(SpeedHash, currentSpeed);
            }

            // 2. Legacy Animation clip fallback
            if (legacyAnimation != null && legacyAnimation.enabled)
            {
                if (isWalking)
                {
                    if (hasWalkClip && !legacyAnimation.IsPlaying("Walk"))
                    {
                        legacyAnimation.CrossFade("Walk", 0.15f);
                    }
                }
                else
                {
                    if (hasIdleClip && !legacyAnimation.IsPlaying("Idle"))
                    {
                        legacyAnimation.CrossFade("Idle", 0.15f);
                    }
                }
            }

            // 3. Dynamic Procedural Walk Stride & Bobbing (visual body stride & step motion)
            if (visualModel != null)
            {
                if (isWalking)
                {
                    float walkTime = Time.time * 11f; // Walk stride frequency
                    float bobHeight = Mathf.Abs(Mathf.Sin(walkTime)) * 0.08f;
                    float tiltAngle = Mathf.Sin(walkTime * 0.5f) * 4.5f;

                    visualModel.localPosition = initialVisualLocalPos + new Vector3(0f, bobHeight, 0f);
                    visualModel.localRotation = initialVisualLocalRot * Quaternion.Euler(0f, 0f, tiltAngle);
                }
                else
                {
                    // Smooth return to rest stance
                    visualModel.localPosition = Vector3.Lerp(visualModel.localPosition, initialVisualLocalPos, 10f * Time.deltaTime);
                    visualModel.localRotation = Quaternion.Slerp(visualModel.localRotation, initialVisualLocalRot, 10f * Time.deltaTime);
                }
            }
        }
    }
}
