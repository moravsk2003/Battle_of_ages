using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// A campfire's body, built when the scene starts: a ring of rough stones, a low bed of ash and embers, round logs burnt at
    /// the tips and leaning together, soft flames and sparks. Its light (a child in the scene) hangs above the logs: right among
    /// them it burned the wood and the ash white.
    /// </summary>
    public sealed class CampfireModel : MonoBehaviour
    {
        private const float LightHeight = 1.35f, LightIntensity = 3.2f;
        private static Mesh mesh;
        private Light fireLight;

        private void Awake()
        {
            Material look = MeshKit.PropMaterial();
            if (look == null) return;
            if (mesh == null) mesh = Build();
            var go = new GameObject("CampfireBody");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = look;

            Transform old = transform.Find("FireParticles");
            if (old != null) old.gameObject.SetActive(false);
            Material soft = ChimneySmoke.SoftMaterial;
            if (soft != null)
            {
                Flames(soft);
                Sparks(soft);
            }
            Transform lit = transform.Find("FireLight");
            fireLight = lit != null ? lit.GetComponent<Light>() : null;
            if (fireLight != null)
            {
                lit.localPosition = new Vector3(0f, LightHeight, 0f);
                fireLight.shadows = LightShadows.None; // the logs' shadows lay across the ash like planks
            }
        }

        private void Update()
        {
            if (fireLight == null) return;
            float t = Time.time;
            fireLight.intensity = LightIntensity * (0.86f + 0.14f * Mathf.PerlinNoise(t * 5f, 3.7f) + 0.06f * Mathf.PerlinNoise(t * 13f, 9.1f));
        }

        private ParticleSystem Particles(string name, Material look, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, height, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = look;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            return ps;
        }

        /// <summary>Tongues rising from the heart of the logs: small, bright at the root, thinning and reddening upward.</summary>
        private void Flames(Material look)
        {
            ParticleSystem ps = Particles("Flames", look, 0.08f);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 44;
            main.duration = 2f;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.75f, 1.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.52f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = ps.emission;
            emission.rateOverTime = 42f;
            var shape = ps.shape;
            shape.angle = 6f;
            shape.radius = 0.2f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.75f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.15f)));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.92f, 0.55f), 0f), new GradientColorKey(new Color(1f, 0.58f, 0.13f), 0.45f), new GradientColorKey(new Color(0.85f, 0.2f, 0.04f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.12f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ps.Play(true);
        }

        /// <summary>A few sparks that wander up and go out.</summary>
        private void Sparks(Material look)
        {
            ParticleSystem ps = Particles("Sparks", look, 0.35f);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 14;
            main.duration = 3f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.45f, 0.1f));
            var emission = ps.emission;
            emission.rateOverTime = 5f;
            var shape = ps.shape;
            shape.angle = 16f;
            shape.radius = 0.2f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 1.2f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            ps.Play(true);
        }

        public static Mesh Build()
        {
            var kit = new MeshKit();
            var rng = new System.Random(31);
            float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            Vector3 stoneS = new Vector3(3f, 0.2f, 0.5f), woodS = new Vector3(4f, 0f, 0.25f), emberS = new Vector3(6f, 0.1f, 0.6f);

            // Ash: a low round bed with a torn edge (a square slab read as a plank under the logs)
            var ash = new[] { new Vector2(0.7f, -0.01f), new Vector2(0.62f, 0.022f), new Vector2(0.42f, 0.05f), new Vector2(0.18f, 0.066f), new Vector2(0.01f, 0.07f) };
            kit.Lathe(Matrix4x4.identity, ash, 18, emberS, (row, a) => Color.Lerp(new Color(0.16f, 0.15f, 0.14f), new Color(0.055f, 0.05f, 0.045f), row / 4f),
                (row, a) => 1f + 0.09f * Mathf.Sin(a * 3f + 1.1f) + 0.06f * Mathf.Sin(a * 7f + 2.3f));
            for (int i = 0; i < 16; i++)
            {
                float a = Rand(0f, Mathf.PI * 2f), r = Rand(0f, 0.42f);
                Color glow = Color.Lerp(new Color(0.75f, 0.2f, 0.04f), new Color(1f, 0.55f, 0.1f), Rand(0f, 1f));
                kit.Box(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, 0.07f, Mathf.Sin(a) * r), Quaternion.Euler(Rand(-10f, 10f), Rand(0f, 180f), Rand(-10f, 10f)), Vector3.one), new Vector3(Rand(0.05f, 0.12f), 0.05f, Rand(0.05f, 0.12f)), 0.015f, emberS, _ => glow, 0.012f, i);
            }

            // Stones of different sizes, none round
            for (int i = 0; i < 11; i++)
            {
                float a = i / 11f * Mathf.PI * 2f + Rand(-0.08f, 0.08f), r = 0.76f + Rand(-0.03f, 0.03f);
                Vector3 size = new Vector3(Rand(0.28f, 0.4f), Rand(0.16f, 0.27f), Rand(0.22f, 0.3f));
                Color stone = Color.Lerp(new Color(0.33f, 0.32f, 0.3f), new Color(0.52f, 0.5f, 0.46f), Rand(0f, 1f));
                kit.Box(Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, size.y * 0.42f, Mathf.Sin(a) * r), Quaternion.Euler(Rand(-6f, 6f), -a * Mathf.Rad2Deg + Rand(-14f, 14f), Rand(-6f, 6f)), Vector3.one), size, 0.05f, stoneS, _ => stone, 0.025f, i);
            }

            // Round logs leaning together: burnt black at the fire, brown at the foot
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * 360f + Rand(-12f, 12f), length = Rand(0.82f, 1f), tilt = Rand(46f, 58f), radius = Rand(0.05f, 0.068f);
                Quaternion turn = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(tilt, 0f, 0f);
                Vector3 foot = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.03f, -0.46f);
                Color bark = Color.Lerp(new Color(0.2f, 0.12f, 0.06f), new Color(0.3f, 0.19f, 0.1f), Rand(0f, 1f)), burnt = new Color(0.045f, 0.035f, 0.03f);
                Log(kit, Matrix4x4.TRS(foot, turn, Vector3.one), radius, length, woodS, bark, burnt);
            }
            // Two lying across the foot of the stack, as yet barely burnt
            for (int i = 0; i < 2; i++)
            {
                float a = 40f + i * 120f, length = 0.72f;
                Quaternion lying = Quaternion.Euler(0f, a, 90f);
                Vector3 middle = Quaternion.Euler(0f, a + 90f, 0f) * new Vector3(0f, 0f, 0.5f) + Vector3.up * 0.07f;
                Log(kit, Matrix4x4.TRS(middle + lying * new Vector3(0f, -length * 0.5f, 0f), lying, Vector3.one), 0.06f, length, woodS, new Color(0.26f, 0.16f, 0.08f), new Color(0.1f, 0.07f, 0.05f));
            }
            return kit.ToMesh("Campfire");
        }

        /// <summary>A round log along the frame's Y, from bark to char at its far end.</summary>
        private static void Log(MeshKit kit, Matrix4x4 frame, float radius, float length, Vector3 surface, Color bark, Color burnt)
        {
            var profile = new[]
            {
                new Vector2(0.005f, 0f), new Vector2(radius * 0.9f, 0.012f), new Vector2(radius, length * 0.25f), new Vector2(radius * 0.97f, length * 0.6f),
                new Vector2(radius * 0.8f, length * 0.92f), new Vector2(0.005f, length)
            };
            kit.Lathe(frame, profile, 7, surface, (row, a) => Color.Lerp(bark, burnt, Mathf.SmoothStep(0f, 1f, row / 3.4f)),
                (row, a) => 1f + 0.07f * Mathf.Sin(a * 2f + row * 1.4f));
        }
    }
}
