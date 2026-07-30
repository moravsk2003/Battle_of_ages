using UnityEngine;
using UnityEngine.Rendering;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Minimap / Map overlay marker that follows a target transform smoothly.
    /// Configured with shadow casting off and raycast ignore layer to prevent interference with RTS interaction rays.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapMarker : MonoBehaviour
    {
        private const string IGNORE_RAYCAST_LAYER = "Ignore Raycast";

        [Header("Target & Icon Settings")]
        [SerializeField] private Transform targetTransform;
        [SerializeField] private bool syncRotation = true;
        [SerializeField] private float heightOffset = 18.0f;

        private void Awake()
        {
            // Ensure MapMarker does NOT cast shadows onto 3D scene
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            // Ensure MapMarker has NO collider so it doesn't block raycasts
            Collider col = GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(col);
                }
                else
                {
                    DestroyImmediate(col);
                }
            }

            int ignoreLayer = LayerMask.NameToLayer(IGNORE_RAYCAST_LAYER);
            if (ignoreLayer != -1)
            {
                gameObject.layer = ignoreLayer;
            }
        }

        private void Start()
        {
            if (targetTransform == null && transform.parent != null)
            {
                targetTransform = transform.parent;
            }
        }

        private void LateUpdate()
        {
            if (targetTransform == null) return;

            Vector3 targetPos = targetTransform.position;
            transform.position = new Vector3(targetPos.x, targetPos.y + heightOffset, targetPos.z);

            if (syncRotation)
            {
                transform.rotation = Quaternion.Euler(90f, targetTransform.eulerAngles.y, 0f);
            }
        }
    }
}
