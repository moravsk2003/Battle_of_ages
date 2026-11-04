using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.UI;
using BattleOfAges.RTS;
using BattleOfAges.Combat;

namespace BattleOfAges.Player
{
    /// <summary>
    /// First-person character controller providing smooth movement and mouse look.
    /// Subscribes to SettingsManager events for live sensitivity tuning and dynamically resolves camera references.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        private const float GROUND_STICK_FORCE = -2.0f;

        [Header("Movement Configuration")]
        [SerializeField] private float moveSpeed = 12.0f;
        private CombatUnit combat;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float jumpHeight = 1.2f;

        [Header("Look Configuration")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float mouseSensitivity = 1.0f;
        /// <summary>
        /// Degrees per mouse unit (0.1 of a pixel) at sensitivity 1: 0.06° per pixel, a full turn in ~10 cm at 1600 DPI
        /// (about CS 1.4 / Valorant 0.45). It was 0.75° per pixel: a tester spun "like a helicopter" even at 0.2.
        /// </summary>
        private const float LookScale = 0.6f;
        [SerializeField] private float minPitchAngle = -85.0f;
        [SerializeField] private float maxPitchAngle = 85.0f;

        private CharacterController characterController;
        private Vector3 velocity;
        private float pitch = 0.0f;
        private bool isGrounded;
        private bool isControlEnabled = true;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            EnsureCameraTransform();
        }

        private void Start()
        {
            EnsureCameraTransform();
            SnapToGround();

            if (SettingsManager.Instance != null)
            {
                mouseSensitivity = SettingsManager.Instance.FpsSensitivity;
                SettingsManager.Instance.OnFpsSensitivityChanged += OnSensitivityChanged;
            }
        }

        public void SnapToGround()
        {
            float groundY = RtsUnit.GetTerrainHeight(transform.position);
            transform.position = new Vector3(transform.position.x, groundY + 1.0f, transform.position.z);
        }

        private void OnDestroy()
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.OnFpsSensitivityChanged -= OnSensitivityChanged;
            }
        }

        private void EnsureCameraTransform()
        {
            if (cameraTransform == null)
            {
                Camera childCam = GetComponentInChildren<Camera>();
                if (childCam != null)
                {
                    cameraTransform = childCam.transform;
                }
                else if (Camera.main != null)
                {
                    cameraTransform = Camera.main.transform;
                }
            }
        }

        private void OnSensitivityChanged(float newSensitivity)
        {
            mouseSensitivity = newSensitivity;
        }

        private void Update()
        {
            // Gravity goes on while a window is open or the map is up (the player used to hang in the air); only the
            // input stops. A pause freezes the time, so the fall too.
            bool input = isControlEnabled && !GameWindows.BlockGameplay;
            HandleMovement(input);
            if (input) HandleMouseLook();
        }

        /// <summary>Walks forward by itself where the hero looks: on with its key, off with it, with walking back, or on the map.</summary>
        public bool AutoWalking { get; set; }

        private void HandleMovement(bool input)
        {
            isGrounded = characterController.isGrounded;
            if (isGrounded && velocity.y < 0f)
            {
                velocity.y = GROUND_STICK_FORCE;
            }

            Vector2 inputMove = input ? UniversalInput.GetMoveInput() : Vector2.zero;
            if (input && InputSystem.KeybindsManager.GetKeyDown(InputSystem.KeyAction.AutoWalk)) AutoWalking = !AutoWalking;
            if (!isControlEnabled || inputMove.y < 0f) AutoWalking = false; // the map, or walking back, ends it
            if (input && AutoWalking && inputMove.y <= 0f) inputMove.y = 1f;
            Vector3 move = transform.right * inputMove.x + transform.forward * inputMove.y;
            
            if (move.sqrMagnitude > 1.0f)
            {
                move.Normalize();
            }

            if (combat == null) combat = GetComponent<CombatUnit>();
            float speed = combat != null ? combat.MoveSpeed : moveSpeed; // CharacterSpeed formula (hero modifier 5)
            speed *= BattleFront.Factor(RTS.UnitSpatialGrid.TeamPlayer, move); // the pull of a battle
            characterController.Move(move * speed * Time.deltaTime);

            if (input && UniversalInput.IsJumpPressed() && isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravity);
            }

            velocity.y += gravity * Time.deltaTime;
            characterController.Move(velocity * Time.deltaTime);

            // Not past the mountains round a bounded world
            Vector3 p = transform.position, inside = World.WorldSettings.ClampInside(p);
            if (inside.x != p.x || inside.z != p.z) characterController.Move(new Vector3(inside.x - p.x, 0f, inside.z - p.z));
            // Nor into a lake: back out to its shore
            if (World.Lakes.TryLake(p.x, p.z, 0.3f, out World.Lakes.Lake lake))
            {
                Vector3 outward = new Vector3(p.x - lake.Center.x, 0f, p.z - lake.Center.y).normalized;
                characterController.Move(outward * Mathf.Max(0.05f, speed * Time.deltaTime));
            }
        }

        private void HandleMouseLook()
        {
            EnsureCameraTransform();

            Vector2 mouseDelta = UniversalInput.GetMouseDelta();
            
            float currentSens = (SettingsManager.Instance != null) ? SettingsManager.Instance.FpsSensitivity : mouseSensitivity;
            float mouseX = mouseDelta.x * currentSens * LookScale;
            float mouseY = mouseDelta.y * currentSens * LookScale;

            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, minPitchAngle, maxPitchAngle);

            if (cameraTransform != null)
            {
                cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            transform.Rotate(Vector3.up * mouseX);
        }

        /// <summary>The view's tilt up or down, degrees (a save keeps it).</summary>
        public float Pitch
        {
            get => pitch;
            set
            {
                pitch = Mathf.Clamp(value, minPitchAngle, maxPitchAngle);
                if (cameraTransform != null) cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        public void EnableControl(bool enable)
        {
            isControlEnabled = enable;
            if (!enable)
            {
                velocity = Vector3.zero;
            }
        }
    }
}
