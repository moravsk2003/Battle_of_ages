using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>A campfire's body, built when the scene starts: a ring of rough stones, charred logs leaning together, glowing embers (its light and flames are its own children).</summary>
    public sealed class CampfireModel : MonoBehaviour
    {
        private static Mesh mesh;

        private void Awake()
        {
            Material look = MeshKit.PropMaterial();
            if (look == null) return;
            if (mesh == null) mesh = Build();
            var go = new GameObject("CampfireBody");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = look;
        }

        public static Mesh Build()
        {
            var kit = new MeshKit();
            var rng = new System.Random(31);
            float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            Vector3 stoneS = new Vector3(3f, 0.2f, 0.5f), woodS = new Vector3(4f, 0f, 0.25f), emberS = new Vector3(6f, 0.1f, 0.6f);

            // Ash and embers on the ground
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 0.03f, 0f)), new Vector3(1.25f, 0.06f, 1.25f), 0.02f, emberS, _ => new Color(0.1f, 0.09f, 0.08f));
            for (int i = 0; i < 14; i++)
            {
                float a = Rand(0f, Mathf.PI * 2f), r = Rand(0f, 0.48f);
                Color glow = Color.Lerp(new Color(0.75f, 0.2f, 0.04f), new Color(1f, 0.55f, 0.1f), Rand(0f, 1f));
                kit.Box(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, 0.07f, Mathf.Sin(a) * r), Quaternion.Euler(0f, Rand(0f, 180f), 0f), Vector3.one), new Vector3(Rand(0.06f, 0.14f), 0.05f, Rand(0.06f, 0.14f)), 0.015f, emberS, _ => glow);
            }

            // Stones of different sizes, none round
            for (int i = 0; i < 11; i++)
            {
                float a = i / 11f * Mathf.PI * 2f + Rand(-0.08f, 0.08f), r = 0.74f + Rand(-0.03f, 0.03f);
                Vector3 size = new Vector3(Rand(0.28f, 0.4f), Rand(0.16f, 0.27f), Rand(0.22f, 0.3f));
                Color stone = Color.Lerp(new Color(0.33f, 0.32f, 0.3f), new Color(0.52f, 0.5f, 0.46f), Rand(0f, 1f));
                kit.Box(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, size.y * 0.42f, Mathf.Sin(a) * r), Quaternion.Euler(Rand(-6f, 6f), -a * Mathf.Rad2Deg + Rand(-14f, 14f), Rand(-6f, 6f)), Vector3.one), size, 0.05f, stoneS, _ => stone, 0.025f, i);
            }

            // Logs leaning together: burnt black at the fire, brown at the foot
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * 360f + Rand(-12f, 12f), length = Rand(0.85f, 1.05f), tilt = Rand(46f, 58f);
                Quaternion turn = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                Vector3 foot = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.03f, -0.46f);
                Vector3 middle = foot + turn * new Vector3(0f, length * 0.5f, 0f);
                Color bark = Color.Lerp(new Color(0.2f, 0.12f, 0.06f), new Color(0.3f, 0.19f, 0.1f), Rand(0f, 1f)), burnt = new Color(0.05f, 0.04f, 0.035f);
                float half = length * 0.5f;
                kit.Box(Matrix4x4.TRS(middle, turn, Vector3.one), new Vector3(0.11f, length, 0.11f), 0.03f, woodS, q => Color.Lerp(bark, burnt, Mathf.SmoothStep(0f, 1f, (q.y + half) / length)));
            }
            return kit.ToMesh("Campfire");
        }
    }
}
