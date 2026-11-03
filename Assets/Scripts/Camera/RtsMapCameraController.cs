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
        /// <summary>The settings slider at 1 moves the map this many times the old base (the base felt slow).</summary>
        public const float SensitivityBase = 2.5f;
        /// <summary>World units per screen pixel of a right-button drag at sensitivity 1 and the reference zoom (as the old drag at 60 FPS).</summary>
        private const float DragPerPixel = 1f / 60f;
        [SerializeField] private float smoothTime = 0.05f;

        [Header("Zoom Configuration")]
        [SerializeField] private float minZoom = 10.0f;
        [SerializeField] private float maxZoom = 100.0f;
        /// <summary>One wheel notch zooms by this share of the view at zoom setting 1 (the same feel near and far).</summary>
        private const float ZoomPerNotch = 0.15f;
        private float zoomSetting = 1.0f; // the wheel speed from the settings

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
        private Vector3 smoothPosition; // where the camera flies; the transform backs off from it by lift over hills
        private float lift, liftTimer;

        private float usualPitch, usualYaw, usualZoom;
        private float currentYaw = 0.0f;      // Y-axis rotation (left/right)
        private float currentPitch = 60.0f;   // X-axis tilt (30° isometric to 90° top-down)

        private void Awake()
        {
            Managers.GameContext.Register(this);
            mapCamera = GetComponent<Camera>();
            mapCamera.orthographic = true;
            usualPitch = currentPitch;
            usualYaw = currentYaw;
            usualZoom = mapCamera.orthographicSize;
            targetPosition = transform.position;
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
            smoothPosition = targetPosition;
        }

        private void Start()
        {
            if (SettingsManager.Instance != null)
            {
                dragSensitivity = SettingsManager.Instance.MapSensitivity * SensitivityBase;
                zoomSetting = SettingsManager.Instance.ZoomSensitivity;
                SettingsManager.Instance.OnMapSensitivityChanged += OnSensitivityChanged;
                SettingsManager.Instance.OnZoomSensitivityChanged += OnZoomChanged;
            }
        }

        private void OnDestroy()
        {
            if (SettingsManager.Instance != null)
            {
                SettingsManager.Instance.OnMapSensitivityChanged -= OnSensitivityChanged;
                SettingsManager.Instance.OnZoomSensitivityChanged -= OnZoomChanged;
            }
        }

        private void OnSensitivityChanged(float newSensitivity)
        {
            dragSensitivity = newSensitivity * SensitivityBase;
        }

        private void OnZoomChanged(float value) => zoomSetting = value;

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
                targetPosition += moveDir * panSpeed * (dragSensitivity / SensitivityBase) * zoomScale * Time.deltaTime; // the keys keep their speed
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
                // the mouse moved this far since the last frame: no x deltaTime (that made the drag slower the higher the FPS)
                Vector3 move = yawRotation * new Vector3(-delta.x, 0f, -delta.y) * dragSensitivity * zoomScale * DragPerPixel;
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
                float step = Mathf.Clamp(scroll * ZoomPerNotch * zoomSetting, -0.9f, 0.6f);
                mapCamera.orthographicSize *= 1f - step;
                mapCamera.orthographicSize = Mathf.Clamp(mapCamera.orthographicSize, minZoom, maxZoom);
            }
        }

        private void ApplySmoothMovement()
        {
            smoothPosition = Vector3.SmoothDamp(smoothPosition, targetPosition, ref currentVelocity, smoothTime);
            liftTimer -= Time.unscaledDeltaTime;
            if (liftTimer <= 0f) { liftTimer = 0.2f; lift = ClearanceLift(); }
            transform.position = smoothPosition - transform.forward * lift;
        }

        /// <summary>
        /// How far the camera backs off along its view to stay above every hill in the frame: it flies at cameraHeight,
        /// the ridges reach ~130 m, and what is behind the near plane is cut away (a player report: "in the mountains
        /// nothing shows from above"). The view is orthographic, so backing off changes nothing but the clipping.
        /// </summary>
        private float ClearanceLift()
        {
            // Every view ray starts on the near plane (a rectangle around the camera): a ray that starts inside a hill
            // shows its inside. Back off until the starts of the rays over the frame are above the ground (a few times:
            // backing off moves them over other ground).
            Vector3 f = transform.forward, right = transform.right, up = transform.up;
            float down = Mathf.Max(0.2f, -f.y);
            float halfW = mapCamera.orthographicSize * mapCamera.aspect, halfH = mapCamera.orthographicSize;
            float need = 0f;
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 eye = smoothPosition - f * need;
                float more = 0f;
                for (int i = -4; i <= 4; i++) // every ~1/8 of the frame: the ridges are narrow
                    for (int j = -4; j <= 4; j++)
                    {
                        Vector3 o = eye + f * mapCamera.nearClipPlane + right * (halfW * i * 0.25f) + up * (halfH * j * 0.25f);
                        float ground = World.InfiniteWorldManager.GetSampledTerrainHeight(o.x, o.z) + 8f; // (+ trees)
                        if (ground > o.y) more = Mathf.Max(more, (ground - o.y) / down);
                    }
                if (more <= 0f) break;
                need += more;
            }
            mapCamera.farClipPlane = Mathf.Max(mapCamera.farClipPlane, 1200f + need);
            return need;
        }

        private void ClampTargetPosition()
        {
            if (World.WorldSettings.Bounded) // the map does not look far past the world's edge
            {
                Vector3 inside = World.WorldSettings.ClampInside(targetPosition, -World.WorldSettings.EdgeBand * 0.5f);
                targetPosition = new Vector3(inside.x, targetPosition.y, inside.z);
            }
            if (!useBounds) return;

            float clampedX = Mathf.Clamp(targetPosition.x, minBounds.x, maxBounds.x);
            float clampedZ = Mathf.Clamp(targetPosition.z, minBounds.y, maxBounds.y);
            targetPosition = new Vector3(clampedX, cameraHeight, clampedZ);
        }

        public void FocusOnPosition(Vector3 worldPosition)
        {
            targetPosition = new Vector3(worldPosition.x, cameraHeight, worldPosition.z);
            transform.position = targetPosition;
            smoothPosition = targetPosition;
            liftTimer = 0f;
            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0.0f);
        }

        /// <summary>Moves the camera so that this ground point is in the middle of the view (the camera looks at it, tilted).</summary>
        public void LookAtGround(Vector3 point)
        {
            float pitch = Mathf.Clamp(currentPitch, 10f, 90f);
            float back = pitch >= 89.5f ? 0f : (cameraHeight - point.y) / Mathf.Tan(pitch * Mathf.Deg2Rad);
            FocusOnPosition(point - Quaternion.Euler(0f, currentYaw, 0f) * Vector3.forward * back);
        }

        /// <summary>Back to the usual angle, turn and zoom, over the same place.</summary>
        public void ResetView()
        {
            Vector3 f = transform.forward;
            Vector3 middle = f.y < -0.01f ? transform.position + f * (transform.position.y / -f.y) : transform.position;
            middle.y = 0f;
            currentPitch = usualPitch;
            currentYaw = usualYaw;
            mapCamera.orthographicSize = usualZoom;
            LookAtGround(middle);
        }

        public void EnableControl(bool enable)
        {
            isControlEnabled = enable;
            gameObject.SetActive(enable);
            if (enable)
            {
                targetPosition = smoothPosition;
            }
        }

        private bool IsKeyPressed(KeyCode code)
        {
            return UniversalInput.IsKeyHeld(code);
        }
    }
}
