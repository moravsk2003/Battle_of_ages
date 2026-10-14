using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Steppe hare sculpted with SdfCreature: low rounded haunches over long hind feet lying flat on the ground,
    /// slim front legs with small paws, arched back, short thick neck, blunt head with a split upper lip, a nose pad,
    /// big glossy side eyes with pale rings and whiskers, long cupped ears with black tips, fluffy tail.
    /// Coat: ticked grey-brown agouti, rusty nape, buff chest, white belly, paler feet, tail dark on top, white below;
    /// soft fur fuzz over the body. A coarse distant mesh takes over beyond a few metres.
    /// Rig (animated by PeacefulAnimalAI): LegPivot_0..3 / Knee_0..3, NeckPivot / Head / Ear_L, Ear_R, TailPivot.
    /// </summary>
    public static class RabbitModelBuilder
    {
        private static readonly Color Coat = new Color(0.59f, 0.48f, 0.35f);
        private static readonly Color Ticking = new Color(0.22f, 0.17f, 0.12f);
        private static readonly Color Rust = new Color(0.66f, 0.45f, 0.27f);
        private static readonly Color Buff = new Color(0.80f, 0.69f, 0.52f);
        private static readonly Color White = new Color(0.93f, 0.91f, 0.86f);
        private static readonly Color InnerEar = new Color(0.84f, 0.70f, 0.62f);
        private static readonly Color Nose = new Color(0.52f, 0.36f, 0.33f);
        private static readonly Color Dark = new Color(0.07f, 0.06f, 0.05f);
        private static readonly Color Crease = new Color(0.12f, 0.09f, 0.07f);

        private static readonly Vector3 HeadPos = new Vector3(0f, 0.32f, 0.18f); // rest position of the Head joint

        public static void Build(Transform root)
        {
            var sdf = new SdfCreature(root) { Paint = PaintCoat, AmbientOcclusion = 0.5f, OcclusionScale = 0.45f };

            // ---- torso (root bone): big round haunches, arched back, chest
            sdf.Ellipsoid(root, new Vector3(0f, 0.17f, -0.10f), new Vector3(0.115f, 0.13f, 0.14f), Vector3.zero, Coat, 0.05f);           // haunches
            sdf.Ellipsoid(root, new Vector3(0f, 0.20f, 0.0f), new Vector3(0.10f, 0.11f, 0.16f), new Vector3(-10f, 0f, 0f), Coat, 0.05f);  // back
            sdf.Ellipsoid(root, new Vector3(0f, 0.19f, 0.10f), new Vector3(0.085f, 0.10f, 0.09f), Vector3.zero, Coat, 0.04f);             // chest
            sdf.Ellipsoid(root, new Vector3(0f, 0.12f, 0.0f), new Vector3(0.08f, 0.06f, 0.13f), Vector3.zero, Coat, 0.04f);               // belly

            // ---- legs: 0 front-left, 1 front-right, 2 hind-left, 3 hind-right
            for (int i = 0; i < 4; i++)
            {
                bool hind = i >= 2;
                float side = (i % 2 == 0) ? -1f : 1f;
                Transform pivot = Joint("LegPivot_" + i, root, hind ? new Vector3(side * 0.07f, 0.17f, -0.11f) : new Vector3(side * 0.045f, 0.15f, 0.11f), Quaternion.identity);
                if (hind)
                {
                    // Knee = heel: the long foot lies forward along the ground, the thigh bulges on the haunch
                    Transform knee = Joint("Knee_" + i, pivot, new Vector3(0f, -0.15f, -0.06f), Quaternion.identity);
                    sdf.Ellipsoid(pivot, new Vector3(side * 0.035f, -0.01f, 0.02f), new Vector3(0.05f, 0.085f, 0.10f), new Vector3(-15f, 0f, 0f), Coat, 0.025f); // thigh
                    sdf.Cone(pivot, new Vector3(0f, -0.07f, 0.04f), new Vector3(0f, -0.145f, -0.05f), 0.032f, 0.018f, Coat, 0.02f, new Vector3(0.8f, 1f, 1f)); // shin
                    sdf.Cone(knee, new Vector3(0f, 0.004f, 0f), new Vector3(0f, -0.006f, 0.125f), 0.019f, 0.017f, Coat, 0.012f, new Vector3(0.85f, 0.75f, 1f));  // foot
                    sdf.Ellipsoid(knee, new Vector3(0f, -0.006f, 0.13f), new Vector3(0.017f, 0.012f, 0.022f), Vector3.zero, Coat, 0.008f);                        // toes
                    sdf.Ellipsoid(knee, new Vector3(0f, 0.012f, -0.006f), new Vector3(0.013f, 0.016f, 0.014f), Vector3.zero, Coat, 0.008f);                       // heel
                }
                else
                {
                    Transform knee = Joint("Knee_" + i, pivot, new Vector3(0f, -0.10f, 0.01f), Quaternion.identity);
                    sdf.Ellipsoid(pivot, new Vector3(side * 0.012f, 0.03f, 0f), new Vector3(0.03f, 0.055f, 0.042f), Vector3.zero, Coat, 0.02f);        // shoulder
                    sdf.Cone(pivot, new Vector3(0f, 0.0f, 0.0f), new Vector3(0f, -0.10f, 0.01f), 0.026f, 0.0165f, Coat, 0.012f, new Vector3(0.85f, 1f, 1f)); // forearm
                    sdf.Cone(knee, Vector3.zero, new Vector3(0f, -0.038f, 0.022f), 0.0165f, 0.015f, Coat, 0.008f);                                     // wrist
                    sdf.Ellipsoid(knee, new Vector3(0f, -0.04f, 0.03f), new Vector3(0.018f, 0.012f, 0.025f), Vector3.zero, Coat, 0.006f);                // paw
                }
            }

            // ---- neck and head
            Transform neck = Joint("NeckPivot", root, new Vector3(0f, 0.25f, 0.12f), Quaternion.identity);
            sdf.Ellipsoid(neck, new Vector3(0f, 0.02f, 0.025f), new Vector3(0.062f, 0.068f, 0.06f), new Vector3(-20f, 0f, 0f), Coat, 0.035f);
            Transform head = Joint("Head", neck, HeadPos - new Vector3(0f, 0.25f, 0.12f), Quaternion.identity);
            sdf.Ellipsoid(head, new Vector3(0f, 0.012f, 0f), new Vector3(0.052f, 0.055f, 0.066f), Vector3.zero, Coat, 0.025f);               // cranium
            sdf.Ellipsoid(head, new Vector3(0f, -0.014f, 0.058f), new Vector3(0.034f, 0.031f, 0.036f), Vector3.zero, Coat, 0.02f);           // muzzle
            sdf.Ellipsoid(head, new Vector3(0f, -0.004f, 0.091f), new Vector3(0.011f, 0.008f, 0.006f), Vector3.zero, Nose, 0.006f, 0.2f);  // nose pad
            sdf.Ellipsoid(head, new Vector3(0f, -0.038f, 0.05f), new Vector3(0.022f, 0.012f, 0.026f), Vector3.zero, Coat, 0.012f);           // chin
            sdf.CarveCone(head, new Vector3(0f, -0.008f, 0.094f), new Vector3(0f, -0.03f, 0.087f), 0.0025f, 0.002f, 0.003f, Crease);         // split lip
            foreach (float s in new[] { -1f, 1f })
            {
                sdf.Ellipsoid(head, new Vector3(s * 0.03f, -0.02f, 0.02f), new Vector3(0.034f, 0.034f, 0.04f), Vector3.zero, Coat, 0.02f);       // cheek
                sdf.Ellipsoid(head, new Vector3(s * 0.016f, -0.02f, 0.078f), new Vector3(0.017f, 0.014f, 0.015f), Vector3.zero, Coat, 0.008f);   // whisker pad
                sdf.CarveEllipsoid(head, new Vector3(s * 0.007f, -0.002f, 0.095f), new Vector3(0.0035f, 0.006f, 0.004f), new Vector3(0f, 0f, s * 30f), 0.002f, Crease); // nostril
                sdf.CarveEllipsoid(head, new Vector3(s * 0.045f, 0.02f, 0.03f), new Vector3(0.018f, 0.017f, 0.019f), Vector3.zero, 0.006f);      // eye socket
                sdf.GlossSphere(head, new Vector3(s * 0.04f, 0.02f, 0.032f), 0.0155f, new Color(0.09f, 0.06f, 0.04f));
                sdf.AddTuft(head, new Vector3(s * 0.026f, -0.016f, 0.08f), new Vector3(s * 1f, -0.05f, 0.35f), 7, 0.06f, 0.25f, 0.0012f, White, White);   // whiskers

                // Ear: long leaf with a cupped pale inner face and a black tip, tilted back and slightly out
                Transform ear = Joint(s < 0f ? "Ear_L" : "Ear_R", head, new Vector3(s * 0.02f, 0.05f, -0.025f), Quaternion.Euler(-14f, s * 12f, -s * 9f));
                sdf.Cone(ear, Vector3.zero, new Vector3(0f, 0.04f, 0f), 0.013f, 0.019f, Coat, 0.012f);
                sdf.Ellipsoid(ear, new Vector3(0f, 0.115f, 0f), new Vector3(0.026f, 0.105f, 0.013f), Vector3.zero, Coat, 0.012f);
                sdf.Ellipsoid(ear, new Vector3(0f, 0.205f, 0f), new Vector3(0.015f, 0.024f, 0.011f), Vector3.zero, Dark * 2f, 0.008f, 0.3f);   // black tip
                sdf.CarveEllipsoid(ear, new Vector3(0f, 0.115f, 0.011f), new Vector3(0.017f, 0.085f, 0.007f), Vector3.zero, 0.005f, InnerEar);
            }

            // ---- tail: round fluffy scut
            Transform tail = Joint("TailPivot", root, new Vector3(0f, 0.21f, -0.235f), Quaternion.identity);
            sdf.Ellipsoid(tail, new Vector3(0f, 0f, -0.018f), new Vector3(0.03f, 0.034f, 0.026f), Vector3.zero, White, 0.015f);
            sdf.AddTuft(tail, new Vector3(0f, -0.004f, -0.03f), new Vector3(0f, -0.2f, -1f), 30, 0.025f, 0.9f, 0.003f, White, White);

            // ---- fur: soft fuzz over the coat lying back, longer on the chest and cheeks
            sdf.BodyBristles(1400, 0.010f, 0.022f, 0.0028f, new Vector3(0f, -0.3f, -1f), 0.6f, (p, n) => p.y < 0.03f ? 0f : 1f);
            sdf.BodyBristles(220, 0.012f, 0.02f, 0.0028f, new Vector3(0f, -1f, 0.2f), 0.9f,
                (p, n) => Near(p, new Vector3(0f, 0.15f, 0.17f), 0.03f, 0.07f) + Near(p, HeadPos + new Vector3(0f, -0.025f, 0.02f), 0.03f, 0.05f) * Mathf.Abs(n.x));

            SkinnedMeshRenderer[] skins = sdf.ApplyWithLod("Rabbit", 0.007f, 0.013f, 0.15f, 0.006f);

            // Individual coat shade so a group is not a row of clones.
            var block = new MaterialPropertyBlock();
            float shade = Random.Range(0.88f, 1.1f), warm = Random.Range(-0.05f, 0.05f);
            block.SetColor("_BaseColor", new Color(shade * (1f + warm), shade, shade * (1f - warm)));
            foreach (SkinnedMeshRenderer skin in skins)
                for (int i = 0; i < 2; i++) skin.SetPropertyBlock(block, i); // coat and hair, not eyes
        }

        // ------------------------------------------------------------------ coat (rest pose, VisualModel space)

        private static float Near(Vector3 p, Vector3 c, float inner, float outer) => Mathf.InverseLerp(outer, inner, Vector3.Distance(p, c));

        private static Color PaintCoat(Vector3 p, Vector3 n, Color c, float paintable)
        {
            if (paintable < 0.01f) return c;
            // Agouti ticking: fine dark flecks over a warm grey-brown, broader soft variation
            float fine = SdfCreature.Noise(p * 140f), broad = SdfCreature.Noise(p * 18f + Vector3.one * 4.1f);
            Color col = c * Mathf.Lerp(0.9f, 1.08f, broad);
            col = Color.Lerp(col, Ticking, Mathf.InverseLerp(0.55f, 0.85f, fine) * 0.45f);
            col = Color.Lerp(col, Buff, Mathf.InverseLerp(0.35f, 0.1f, fine) * 0.25f);

            // Darker along the spine, white belly and inner legs, buff chest and flanks below the midline
            col = Color.Lerp(col, Ticking * 1.6f, Mathf.InverseLerp(0.5f, 0.95f, n.y) * Mathf.InverseLerp(0.06f, 0.01f, Mathf.Abs(p.x)) * 0.35f);
            col = Color.Lerp(col, Buff, Mathf.InverseLerp(0.17f, 0.10f, p.y) * Mathf.InverseLerp(0.1f, -0.5f, n.y) * 0.5f);
            col = Color.Lerp(col, White, Mathf.InverseLerp(-0.35f, -0.8f, n.y) * Mathf.InverseLerp(0.15f, 0.08f, p.y) * 0.9f);
            col = Color.Lerp(col, Buff, Near(p, new Vector3(0f, 0.17f, 0.18f), 0.03f, 0.08f) * Mathf.InverseLerp(0f, 0.6f, n.z) * 0.7f);

            // Paler feet, rusty nape behind the ears
            col = Color.Lerp(col, Buff, Mathf.InverseLerp(0.05f, 0.015f, p.y) * 0.6f);
            col = Color.Lerp(col, Rust, Near(p, HeadPos + new Vector3(0f, 0.02f, -0.06f), 0.025f, 0.06f) * Mathf.InverseLerp(-0.2f, 0.5f, n.y + -n.z) * 0.7f);

            // Head: pale eye rings, pale muzzle and chin, darker crown
            foreach (float s in new[] { -1f, 1f })
            {
                float d = Vector3.Distance(p, HeadPos + new Vector3(s * 0.045f, 0.02f, 0.03f));
                col = Color.Lerp(col, White, Mathf.Clamp01(1f - Mathf.Abs(d - 0.021f) / 0.0055f) * 0.75f);
            }
            col = Color.Lerp(col, Buff, Near(p, HeadPos + new Vector3(0f, -0.02f, 0.07f), 0.015f, 0.04f) * 0.55f);
            col = Color.Lerp(col, White, Near(p, HeadPos + new Vector3(0f, -0.045f, 0.05f), 0.012f, 0.035f) * Mathf.InverseLerp(0f, -0.6f, n.y) * 0.8f);

            // Tail: dark on top, white below (the tail shape is white)
            float tail = Near(p, new Vector3(0f, 0.21f, -0.255f), 0.02f, 0.05f);
            col = Color.Lerp(col, Ticking * 1.4f, tail * Mathf.InverseLerp(0.1f, 0.7f, n.y) * 0.85f);

            col.a = 1f;
            return Color.Lerp(c, col, paintable);
        }

        private static Transform Joint(string name, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            Transform t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            t.localRotation = localRotation;
            return t;
        }
    }
}
