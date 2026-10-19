using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>The look of the mage's Fire fan: a spray of flame blobs over the cone that fade out in half a second.</summary>
    public sealed class FireFanEffect : MonoBehaviour
    {
        private const float Life = 0.5f;
        private static Material material;
        private Vector3 velocity;
        private float age;

        public static void Spawn(Vector3 origin, Vector3 forward, float range)
        {
            if (!Application.isPlaying) return; // only a look
            if (material == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                material = new Material(s) { color = new Color(1f, 0.55f, 0.15f) };
            }
            const int blobs = 14;
            for (int i = 0; i < blobs; i++)
            {
                float angle = Mathf.Lerp(-GameBalance.FireFanAngle * 0.5f, GameBalance.FireFanAngle * 0.5f, i / (blobs - 1f));
                Vector3 dir = Quaternion.Euler(0f, angle + Random.Range(-4f, 4f), 0f) * forward;
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "FireFan";
                Destroy(go.GetComponent<Collider>());
                go.transform.position = origin;
                go.transform.localScale = Vector3.one * 0.5f;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.AddComponent<FireFanEffect>().velocity = dir * (range / Life) * Random.Range(0.8f, 1f);
            }
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= Life) { Destroy(gameObject); return; }
            transform.position += velocity * Time.deltaTime;
            transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.4f, age / Life) * (1f - age / Life);
        }
    }
}
