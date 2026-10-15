using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.UI;
using BattleOfAges.RTS;

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
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float jumpHeight = 1.2f;

        [Header("Look Configuration")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private float mouseSensitivity = 1.0f;
        [SerializeField] private float lookMultiplier = 7.5f;
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
            if (!isControlEnabled || IsAnyUIOpen()) return;

            HandleMovement();
            HandleMouseLook();
        }

        private bool IsAnyUIOpen()
        {
            return (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) ||
                   (WorkbenchUI.Instance != null && WorkbenchUI.Instance.IsOpen) || EquipmentUI.IsVisible ||
                   (NpcGatherCommandUI.Instance != null && NpcGatherCommandUI.Instance.IsOpen) ||
                   (BuildMenuUI.Instance != null && BuildMenuUI.Instance.IsOpen) ||
                   (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused());
        }

        private void HandleMovement()
        {
            isGrounded = characterController.isGrounded;
            if (isGrounded && velocity.y < 0f)
            {
                velocity.y = GROUND_STICK_FORCE;
            }

            Vector2 inputMove = UniversalInput.GetMoveInput();
            Vector3 move = transform.right * inputMove.x + transform.forward * inputMove.y;
            
            if (move.sqrMagnitude > 1.0f)
            {
                move.Normalize();
            }

            characterController.Move(move * moveSpeed * Time.deltaTime);

            if (UniversalInput.IsJumpPressed() && isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravity);
            }

            velocity.y += gravity * Time.deltaTime;
            characterController.Move(velocity * Time.deltaTime);
        }

        private void HandleMouseLook()
        {
            EnsureCameraTransform();

            Vector2 mouseDelta = UniversalInput.GetMouseDelta();
            
            float currentSens = (SettingsManager.Instance != null) ? SettingsManager.Instance.FpsSensitivity : mouseSensitivity;
            float mouseX = mouseDelta.x * currentSens * lookMultiplier;
            float mouseY = mouseDelta.y * currentSens * lookMultiplier;

            pitch -= mouseY;
            pitch = Mathf.Clamp(pitch, minPitchAngle, maxPitchAngle);

            if (cameraTransform != null)
            {
                cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            transform.Rotate(Vector3.up * mouseX);
        }

        public void EnableControl(bool enable)
        {
            isControlEnabled = enable;
            if (!enable)
            {
                velocity = Vector3.zero;
            }
        }

        public bool IsControlEnabled() => isControlEnabled;
    }
}
