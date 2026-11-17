using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BattleOfAges.Player
{
    /// <summary>
    /// Separates the local first-person body from the hands viewmodel.
    /// The main camera never renders the local body; the arms are drawn in its frame by ViewmodelPass, over the
    /// world and with their own field of view. The "FPS Viewmodel Camera" object only holds the arms and those
    /// numbers: its Camera is off (a second camera in the stack cost close to a millisecond a frame).
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
        public Transform ViewmodelRoot => viewmodelRoot;

        private void Awake()
        {
            Configure();
            // The hero's body is sculpted with its arms: taking a bow (or putting it down) makes it again (and tells this setup)
            if (Application.isPlaying && GetComponent<Utils.RankModel>() == null) gameObject.AddComponent<Utils.RankModel>();
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

            // The FPS camera does not see its own body. It does cull the arms: ViewmodelPass draws them in its frame
            // (the renderer's own passes leave the layer out).
            mainCamera.cullingMask &= ~worldBodyMask;
            mainCamera.cullingMask |= viewmodelMask;

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
            ViewmodelPass.Owner = mainCamera;
            ViewmodelPass.FieldOfView = viewmodelFieldOfView;
            ViewmodelPass.Near = viewmodelNearClip;
            ViewmodelPass.Far = viewmodelFarClip;
            ViewmodelPass.Ensure();
            ArmsCastNoShadow();
        }

        private static readonly System.Collections.Generic.List<Renderer> armRenderers = new System.Collections.Generic.List<Renderer>();
        private float nextShadowCheck;

        /// <summary>
        /// The arms are in the main camera's view now, so they would cast into the world's shadow map: a pair of arms'
        /// shadow on the ground with no body. A weapon taken later joins them: looked at twice a second.
        /// </summary>
        private void ArmsCastNoShadow()
        {
            if (viewmodelRoot == null) return;
            viewmodelRoot.GetComponentsInChildren(true, armRenderers);
            foreach (Renderer r in armRenderers)
                if (r.shadowCastingMode != ShadowCastingMode.Off) r.shadowCastingMode = ShadowCastingMode.Off;
            armRenderers.Clear();
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextShadowCheck) return;
            nextShadowCheck = Time.unscaledTime + 0.5f;
            ArmsCastNoShadow();
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
            // No second camera: it stays an overlay (so it never renders on its own), off and out of the stack
            UniversalAdditionalCameraData mainData = mainCamera.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData viewmodelData = viewmodelCamera.GetUniversalAdditionalCameraData();
            viewmodelData.renderType = CameraRenderType.Overlay;
            if (mainData.cameraStack != null) mainData.cameraStack.Remove(viewmodelCamera);
            viewmodelCamera.enabled = false;
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
