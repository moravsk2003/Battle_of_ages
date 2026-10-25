using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// The look of the Lightning artifact's strike: a jagged bolt from the sky onto the target, flickering for a moment,
    /// with a flash of light. Only a look: the damage is dealt by the caller.
    /// </summary>
    public sealed class LightningStrike : MonoBehaviour
    {
        public const float Duration = 0.35f, Height = 28f;
        private const int Points = 12;

        private LineRenderer line;
        private Light flash;
        private Vector3 at;
        private float born, nextJag;

        public static void Show(Vector3 target)
        {
            if (!Application.isPlaying) return;
            Material look = RuntimeShaders.UnlitMaterial(new Color(0.75f, 0.85f, 1f));
            if (look == null) return; // without its shader nothing to draw
            var go = new GameObject("LightningStrike");
            var s = go.AddComponent<LightningStrike>();
            s.at = target;
            s.born = Time.time;
            s.line = go.AddComponent<LineRenderer>();
            s.line.sharedMaterial = look;
            s.line.positionCount = Points;
            s.line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0.32f));
            s.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            s.line.receiveShadows = false;
            s.Jag();
            s.flash = new GameObject("Flash").AddComponent<Light>();
            s.flash.transform.SetParent(go.transform, false);
            s.flash.transform.position = target + Vector3.up * 2f;
            s.flash.type = LightType.Point;
            s.flash.color = new Color(0.7f, 0.8f, 1f);
            s.flash.range = 18f;
            s.flash.intensity = 6f;
            s.flash.shadows = LightShadows.None;
        }

        /// <summary>A new zigzag from the sky down to the target (the bolt flickers).</summary>
        private void Jag()
        {
            Vector3 top = at + Vector3.up * Height + new Vector3(Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
            Vector3 bottom = at + Vector3.up * 0.8f;
            for (int i = 0; i < Points; i++)
            {
                float t = i / (Points - 1f);
                Vector3 p = Vector3.Lerp(top, bottom, t);
                if (i > 0 && i < Points - 1) p += new Vector3(Random.Range(-0.9f, 0.9f), 0f, Random.Range(-0.9f, 0.9f)) * (1f - t * 0.6f);
                line.SetPosition(i, p);
            }
            nextJag = Time.time + 0.06f;
        }

        private void Update()
        {
            float age = Time.time - born;
            if (age >= Duration) { Destroy(gameObject); return; }
            if (Time.time >= nextJag) Jag();
            if (flash != null) flash.intensity = 6f * (1f - age / Duration);
        }
    }
}
