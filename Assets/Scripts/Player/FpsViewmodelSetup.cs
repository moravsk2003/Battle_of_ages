using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattleOfAges.Player
{
    /// <summary>
    /// Separates the local first-person body from the hands viewmodel.
    /// The main camera never renders the local body, while a URP overlay camera
    /// renders the arms in a stable foreground pass.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FpsViewmodelSetup : MonoBehaviour
    {
        public const string WorldBodyLayerName = "WorldBody";
        public const string ViewmodelLayerName = "Viewmodel";

        [Header("Camera References")]
        [SerializeField] private Camera mainCamera;
        [SerializeField] private Camera viewmodelCamera;

        [Header("Model References")]
        [SerializeField] private Transform worldBodyRoot;
        [SerializeField] private Transform viewmodelRoot;

        [Header("Rendering")]
        [SerializeField] private bool bodyShadowOnly;
        [SerializeField, Range(45f, 80f)] private float viewmodelFieldOfView = 60f;
        [SerializeField, Min(0.001f)] private float viewmodelNearClip = 0.01f;
        [SerializeField, Min(0.1f)] private float viewmodelFarClip = 3f;

        public Camera ViewmodelCamera => viewmodelCamera;

        private void Awake()
        {
            Configure();
        }

        private void OnValidate()
        {
            if (mainCamera != null && viewmodelCamera != null)
            {
                Configure();
            }
        }

        public void Configure()
        {
            ResolveMissingReferences();

            if (mainCamera == null || viewmodelCamera == null)
            {
                return;
            }

            int worldBodyLayer = LayerMask.NameToLayer(WorldBodyLayerName);
            int viewmodelLayer = LayerMask.NameToLayer(ViewmodelLayerName);
            if (worldBodyLayer < 0 || viewmodelLayer < 0)
            {
                Debug.LogError($"[{nameof(FpsViewmodelSetup)}] Add the '{WorldBodyLayerName}' and '{ViewmodelLayerName}' layers in TagManager.", this);
                return;
            }

            if (worldBodyRoot != null)
            {
                SetLayerRecursively(worldBodyRoot, worldBodyLayer);
                ApplyWorldBodyShadowMode();
            }

            if (viewmodelRoot != null)
            {
                SetLayerRecursively(viewmodelRoot, viewmodelLayer);
            }

            int worldBodyMask = 1 << worldBodyLayer;
            int viewmodelMask = 1 << viewmodelLayer;

            // Do not let the FPS camera see its own body or the viewmodel twice.
            mainCamera.cullingMask &= ~worldBodyMask;
            mainCamera.cullingMask &= ~viewmodelMask;

            viewmodelCamera.transform.SetParent(mainCamera.transform, false);
            viewmodelCamera.transform.localPosition = Vector3.zero;
            viewmodelCamera.transform.localRotation = Quaternion.identity;
            viewmodelCamera.cullingMask = viewmodelMask;
            viewmodelCamera.clearFlags = CameraClearFlags.Depth;
            viewmodelCamera.nearClipPlane = viewmodelNearClip;
            viewmodelCamera.farClipPlane = viewmodelFarClip;
            viewmodelCamera.fieldOfView = viewmodelFieldOfView;
            viewmodelCamera.allowHDR = mainCamera.allowHDR;
            viewmodelCamera.allowMSAA = mainCamera.allowMSAA;

            ConfigureUrpCameraStack();
        }

        public void SetReferences(Camera worldCamera, Camera armsCamera, Transform bodyRoot, Transform armsRoot)
        {
            mainCamera = worldCamera;
            viewmodelCamera = armsCamera;
            worldBodyRoot = bodyRoot;
            viewmodelRoot = armsRoot;
            Configure();
        }

        public void SetWorldBodyRoot(Transform bodyRoot)
        {
            worldBodyRoot = bodyRoot;
            Configure();
        }

        public void SetViewmodelCameraEnabled(bool enabled)
        {
            if (viewmodelCamera != null)
            {
                viewmodelCamera.enabled = enabled;
            }
        }

        private void ResolveMissingReferences()
        {
            if (mainCamera == null)
            {
                mainCamera = GetComponentInChildren<Camera>(true);
            }

            if (viewmodelCamera == null && mainCamera != null)
            {
                Transform cameraTransform = mainCamera.transform.Find("FPS Viewmodel Camera");
                if (cameraTransform != null)
                {
                    viewmodelCamera = cameraTransform.GetComponent<Camera>();
                }
            }

            if (worldBodyRoot == null)
            {
                worldBodyRoot = transform.Find("Hero3DModel");
            }

            if (viewmodelRoot == null && viewmodelCamera != null)
            {
                viewmodelRoot = viewmodelCamera.transform.Find("ViewmodelArms");
            }
        }

        private void ConfigureUrpCameraStack()
        {
            UniversalAdditionalCameraData mainData = mainCamera.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData viewmodelData = viewmodelCamera.GetUniversalAdditionalCameraData();
            viewmodelData.renderType = CameraRenderType.Overlay;

            if (!mainData.cameraStack.Contains(viewmodelCamera))
            {
                mainData.cameraStack.Add(viewmodelCamera);
            }
        }

        private void ApplyWorldBodyShadowMode()
        {
            ShadowCastingMode castingMode = bodyShadowOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (Renderer renderer in worldBodyRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = castingMode;
            }
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform child in root)
            {
                SetLayerRecursively(child, layer);
            }
        }
    }
}
