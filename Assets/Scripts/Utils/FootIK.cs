using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Ground-adaptive feet for the generic rigs (Unity's built-in foot IK is humanoid-only). After the Animator has
    /// posed the skeleton: each foot looks for the ground under it, the hips drop so the lower foot can reach it, and a
    /// two-bone IK (upper leg, lower leg) moves each foot by its own ground offset; feet tilt to the slope. The
    /// animation's own foot lift is kept, because only the difference to the unit's flat ground plane is applied.
    /// </summary>
    [DisallowMultipleComponent]
    public class FootIK : MonoBehaviour
    {
        [SerializeField] private float maxHipDrop = 0.35f;
        [SerializeField] private float maxFootTilt = 30f;
        [SerializeField] private float smoothing = 14f;

        private Transform hips, upperL, lowerL, footL, upperR, lowerR, footR;
        private Transform owner;                    // the unit (CharacterController), its colliders are ignored
        private float offsetL, offsetR, hipOffset;
        private Vector3 lastWrittenHipsLocal, lastAnimatedHipsLocal;
        private bool hasWritten;
        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>When false (e.g. for comparisons), feet keep the raw animation pose.</summary>
        public bool IKEnabled = true;

        private void Awake()
        {
            hips = Find("Hips");
            upperL = Find("UpperLeg.L"); lowerL = Find("LowerLeg.L"); footL = Find("Foot.L");
            upperR = Find("UpperLeg.R"); lowerR = Find("LowerLeg.R"); footR = Find("Foot.R");
        }

        private void LateUpdate()
        {
            if (!IKEnabled || hips == null || footL == null || footR == null) return;
            if (owner == null)
            {
                CharacterController cc = GetComponentInParent<CharacterController>();
                owner = cc != null ? cc.transform : transform.root;
            }

            // The Animator may skip writing (culled / frozen); never stack the hip offset on our own previous result.
            Vector3 hipsLocal = hips.localPosition;
            if (!hasWritten || hipsLocal != lastWrittenHipsLocal) lastAnimatedHipsLocal = hipsLocal;

            float groundPlane = owner.position.y;
            float dt = Time.deltaTime;
            bool hitL = GroundOffset(footL, groundPlane, out float targetL, out Vector3 normalL);
            bool hitR = GroundOffset(footR, groundPlane, out float targetR, out Vector3 normalR);
            float k = 1f - Mathf.Exp(-smoothing * Mathf.Max(dt, 1e-4f));
            offsetL = Mathf.Lerp(offsetL, hitL ? targetL : 0f, k);
            offsetR = Mathf.Lerp(offsetR, hitR ? targetR : 0f, k);
            hipOffset = Mathf.Lerp(hipOffset, Mathf.Clamp(Mathf.Min(offsetL, offsetR, 0f), -maxHipDrop, 0f), k);

            hips.localPosition = lastAnimatedHipsLocal;
            hips.position += Vector3.up * hipOffset;
            lastWrittenHipsLocal = hips.localPosition;
            hasWritten = true;

            Solve(upperL, lowerL, footL, footL.position + Vector3.up * (offsetL - hipOffset), hitL ? normalL : Vector3.up);
            Solve(upperR, lowerR, footR, footR.position + Vector3.up * (offsetR - hipOffset), hitR ? normalR : Vector3.up);
        }

        /// <summary>Height of the ground under the foot relative to the unit's ground plane.</summary>
        private bool GroundOffset(Transform foot, float groundPlane, out float offset, out Vector3 normal)
        {
            offset = 0f;
            normal = Vector3.up;
            Vector3 origin = new Vector3(foot.position.x, groundPlane + 0.8f, foot.position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, Hits, 1.8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.transform.IsChildOf(owner) || Hits[i].collider is CharacterController) continue;
                if (Hits[i].distance < best) { best = Hits[i].distance; offset = Hits[i].point.y - groundPlane; normal = Hits[i].normal; found = true; }
            }
            offset = Mathf.Clamp(offset, -maxHipDrop, 0.5f);
            return found;
        }

        /// <summary>Analytic two-bone IK: bends the knee in its current plane so the foot reaches the target.</summary>
        private void Solve(Transform a, Transform b, Transform c, Vector3 target, Vector3 groundNormal)
        {
            if (a == null || b == null || c == null) return;
            Quaternion footRotation = c.rotation;

            Vector3 pa = a.position, pb = b.position, pc = c.position;
            float lab = (pb - pa).magnitude, lcb = (pc - pb).magnitude;
            float lat = Mathf.Clamp((target - pa).magnitude, 1e-4f, lab + lcb - 1e-4f);

            float acab0 = Angle(pc - pa, pb - pa);
            float bacb0 = Angle(pa - pb, pc - pb);
            float acab1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float bacb1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

            Vector3 axis = Vector3.Cross(pc - pa, pb - pa);
            if (axis.sqrMagnitude < 1e-10f) axis = transform.right; // straight leg: bend around the hip axis
            axis.Normalize();

            a.rotation = Quaternion.AngleAxis((acab1 - acab0) * Mathf.Rad2Deg, axis) * a.rotation;
            b.rotation = Quaternion.AngleAxis((bacb1 - bacb0) * Mathf.Rad2Deg, axis) * b.rotation;
            a.rotation = Quaternion.FromToRotation(c.position - pa, target - pa) * a.rotation;

            // Keep the animated foot orientation, tilted onto the slope.
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, groundNormal);
            float angle = Quaternion.Angle(Quaternion.identity, tilt);
            if (angle > maxFootTilt) tilt = Quaternion.Slerp(Quaternion.identity, tilt, maxFootTilt / angle);
            c.rotation = tilt * footRotation;
        }

        private static float Angle(Vector3 x, Vector3 y)
        {
            float lx = x.magnitude, ly = y.magnitude;
            if (lx < 1e-6f || ly < 1e-6f) return 0f;
            return Mathf.Acos(Mathf.Clamp(Vector3.Dot(x, y) / (lx * ly), -1f, 1f));
        }

        private Transform Find(string boneName) => FindDeep(transform, boneName);

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
