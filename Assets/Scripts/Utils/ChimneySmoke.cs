using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>Smoke over a house's chimney: soft flat puffs that always face the viewer, rising, spreading and fading. One small particle system a chimney, culled off screen.</summary>
    public static class ChimneySmoke
    {
        private const string ChildName = "ChimneySmoke";
        private static Material material;

        /// <summary>Lights the chimney of this building, if it has a cottage's and it is not lit yet.</summary>
        public static void Ensure(GameObject building)
        {
            Transform model = building.transform.Find("HouseModel");
            if (model == null || building.transform.Find(ChildName) != null) return;
            Material look = Look();
            if (look == null) return;
            var go = new GameObject(ChildName);
            go.transform.SetParent(building.transform, false);
            go.transform.position = model.TransformPoint(WorkerHouseBuilder.ChimneyTop);
            Configure(go.AddComponent<ParticleSystem>(), look);
        }

        private static void Configure(ParticleSystem ps, Material look)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = 14;
            main.duration = 5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.61f, 0.6f, 1f), new Color(0.78f, 0.77f, 0.75f, 1f));
            main.prewarm = true;

            var emission = ps.emission;
            emission.rateOverTime = 1.7f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            // The wind takes it as it rises
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0.12f, 0.32f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 3.2f)));

            var turn = ps.rotationOverLifetime;
            turn.enabled = true;
            turn.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.12f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = look;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = 10f;
            ps.Play(true);
        }

        /// <summary>The soft round puff, for other soft particles (a campfire's flames); null where the shader is missing.</summary>
        public static Material SoftMaterial => Look();

        private static Material Look()
        {
            if (material != null) return material;
            Shader s = Shader.Find("Sprites/Default");
            if (s == null) return null;
            material = new Material(s) { name = "MAT_ChimneySmoke", mainTexture = Puff() };
            return material;
        }

        /// <summary>A soft, torn round of smoke, made once.</summary>
        private static Texture2D Puff()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SmokePuff", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f, d = Mathf.Sqrt(u * u + v * v);
                    float noise = Mathf.PerlinNoise(x * 0.11f + 3f, y * 0.11f + 7f) * 0.65f + Mathf.PerlinNoise(x * 0.27f + 11f, y * 0.27f + 5f) * 0.35f;
                    float a = Mathf.Clamp01(1f - d) * Mathf.Lerp(0.45f, 1.25f, noise);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a * a * 1.7f));
                }
            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
