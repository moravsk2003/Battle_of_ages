using UnityEngine;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BattleOfAges.CameraSystem
{
    /// <summary>
    /// Smooth camera controller for RTS map view.
    /// Supports keyboard panning, mouse drag panning, orthographic/perspective zoom,
    /// Q/E camera rotation (Yaw), and R/F camera angle tilt (Pitch).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class RtsMapCameraController : MonoBehaviour
    {
        private const float BASE_ZOOM_REF = 20.0f;

        [Header("Pan Configuration")]
        [SerializeField] private float panSpeed = 28.0f;
        [SerializeField] private float dragSensitivity = 1.0f;
        [SerializeField] private float smoothTime = 0.05f;

        [Header("Zoom Configuration")]
        [SerializeField] private float minZoom = 10.0f;
        [SerializeField] private float maxZoom = 100.0f;
        [SerializeField] private float zoomSensitivity = 10.0f;

        [Header("Rotation & Angle Configuration")]
        [SerializeField] private float rotationSpeed = 90.0f;
        [SerializeField] private float minPitch = 30.0f;
        [SerializeField] private float maxPitch = 90.0f;

        [Header("Camera Height & Bounds")]
        [SerializeField] private float cameraHeight = 100.0f;
        [SerializeField] private bool useBounds = false;
        [SerializeField] private Vector2 minBounds = new Vector2(-500f, -500f);
        [SerializeField] private Vector2 maxBounds = new Vector2(500f, 500f);

        private Camera mapCamera;
        private Vector3 lastMousePosition;
        private Vector3 targetPosition;
        private Vector3 currentVelocity;
        private bool isControlEnabled = false;

        private float currentYaw = 0.0f;      // Y-axis rotation (left/right)
        private float currentPitch = 60.0f;   // X-axis tilt (30° isometric to 90° top-down)

        private void Awake()
        {
            mapCamera = GetComponent<Camera>();
            mapCamera.orthographic = true;
            targetPosition = transform.position;
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        }

        private void Start()
        {
            if (SettingsManager.Instance != null)
            {
                dragSensitivity = SettingsManager.Instance.MapSensitivity;
                SettingsManager.Instance.OnMapSensitivityChanged += OnSensitivityChanged;
            }
        }

        private void OnDestroy()
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.OnMapSensitivityChanged -= OnSensitivityChanged;
            }
        }

        private void OnSensitivityChanged(float newSensitivity)
        {
            dragSensitivity = newSensitivity;
        }

        private void Update()
        {
            if (!isControlEnabled) return;

            HandleRotationAndAngle();
            HandleKeyboardPan();
            HandleMouseDragPan();
            HandleZoom();
            ApplySmoothMovement();
        }

        private void HandleRotationAndAngle()
        {
            // Q / E for Yaw rotation (rotating view left / right)
            bool keyQ = IsKeyPressed(KeyCode.Q);
            bool keyE = IsKeyPressed(KeyCode.E);

            if (keyQ) currentYaw -= rotationSpeed * Time.deltaTime;
            if (keyE) currentYaw += rotationSpeed * Time.deltaTime;

            // R / F for Pitch angle tilt (tilting camera up / down)
            bool keyR = IsKeyPressed(KeyCode.R);
            bool keyF = IsKeyPressed(KeyCode.F);

            if (keyR) currentPitch = Mathf.Clamp(currentPitch + 45f * Time.deltaTime, minPitch, maxPitch);
            if (keyF) currentPitch = Mathf.Clamp(currentPitch - 45f * Time.deltaTime, minPitch, maxPitch);

            // Middle Mouse Button drag or Right Mouse Button drag while holding Shift to rotate camera
            if (UniversalInput.GetMouseButton(2))
            {
                Vector3 mouseDelta = UniversalInput.GetMouseDelta();
                currentYaw += mouseDelta.x * 0.4f * dragSensitivity;
                currentPitch = Mathf.Clamp(currentPitch - mouseDelta.y * 0.3f * dragSensitivity, minPitch, maxPitch);
            }

            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        }

        private void HandleKeyboardPan()
        {
            Vector2 moveInput = UniversalInput.GetMoveInput();
            if (moveInput.sqrMagnitude > 0.01f)
            {
                // Calculate move direction relative to camera's current Yaw rotation angle
                Quaternion yawRotation = Quaternion.Euler(0f, currentYaw, 0f);
                Vector3 moveDir = yawRotation * new Vector3(moveInput.x, 0f, moveInput.y).normalized;

                float zoomScale = mapCamera.orthographicSize / BASE_ZOOM_REF;
                targetPosition += moveDir * panSpeed * dragSensitivity * zoomScale * Time.deltaTime;
                ClampTargetPosition();
            }
        }

        private void HandleMouseDragPan()
        {
            if (UniversalInput.GetMouseButtonDown(1) || UniversalInput.GetMouseButtonDown(2))
            {
                lastMousePosition = UniversalInput.GetMousePosition();
            }

            // Right click drag pan (when not holding shift)
            if (UniversalInput.GetMouseButton(1) && !UniversalInput.IsShiftHeld())
            {
                Vector3 currentMousePos = UniversalInput.GetMousePosition();
                Vector3 delta = currentMousePos - lastMousePosition;
                float zoomScale = mapCamera.orthographicSize / BASE_ZOOM_REF;

                Quaternion yawRotation = Quaternion.Euler(0f, currentYaw, 0f);
                Vector3 move = yawRotation * new Vector3(-delta.x, 0f, -delta.y) * dragSensitivity * zoomScale * Time.deltaTime;
                targetPosition += move;
                lastMousePosition = currentMousePos;

                ClampTargetPosition();
            }
        }

        private void HandleZoom()
        {
            float scroll = UniversalInput.GetScrollWheel();
            if (Mathf.Abs(scroll) > 0.001f)
            {
                float zoomScale = mapCamera.orthographicSize / (BASE_ZOOM_REF * 0.5f);
                mapCamera.orthographicSize -= scroll * zoomSensitivity * zoomScale;
                mapCamera.orthographicSize = Mathf.Clamp(mapCamera.orthographicSize, minZoom, maxZoom);
            }
        }

        private void ApplySmoothMovement()
        {
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);
        }

        private void ClampTargetPosition()
        {
            if (!useBounds) return;

            float clampedX = Mathf.Clamp(targetPosition.x, minBounds.x, maxBounds.x);
            float clampedZ = Mathf.Clamp(targetPosition.z, minBounds.y, maxBounds.y);
            targetPosition = new Vector3(clampedX, cameraHeight, clampedZ);
        }

        public void FocusOnPosition(Vector3 worldPosition)
        {
            targetPosition = new Vector3(worldPosition.x, cameraHeight, worldPosition.z);
            transform.position = targetPosition;
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0.0f);
        }

        public void EnableControl(bool enable)
        {
            isControlEnabled = enable;
            gameObject.SetActive(enable);
            if (enable)
            {
                targetPosition = transform.position;
            }
        }

        private bool IsKeyPressed(KeyCode code)
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (code == KeyCode.Q && Keyboard.current.qKey.isPressed) return true;
                if (code == KeyCode.E && Keyboard.current.eKey.isPressed) return true;
                if (code == KeyCode.R && Keyboard.current.rKey.isPressed) return true;
                if (code == KeyCode.F && Keyboard.current.fKey.isPressed) return true;
            }
#endif
            try { return Input.GetKey(code); } catch { return false; }
        }
    }
}
