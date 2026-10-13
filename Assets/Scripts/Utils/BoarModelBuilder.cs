using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Wild boar (Sus scrofa) sculpted with SdfCreature: high bristly withers sloping to a narrow rump, deep laterally
    /// compressed body, long wedge head ending in a flat snout disc with nostrils, upturned tusks under flared lips,
    /// small deep-set eyes, erect cupped hairy ears, slim legs with cloven hooves and dewclaws, thin tail with a tuft.
    /// Coat: dark grizzled brown, black legs and dorsal stripe, lighter grizzled cheeks; bristle mane along the spine.
    /// Rig (animated by PeacefulAnimalAI): LegPivot_0..3 / Knee_0..3, NeckPivot / Head / Ear_L, Ear_R, TailPivot.
    /// </summary>
    public static class BoarModelBuilder
    {
        private static readonly Color Hide = new Color(0.25f, 0.19f, 0.14f);
        private static readonly Color Hoof = new Color(0.20f, 0.18f, 0.17f);
        private static readonly Color Disc = new Color(0.30f, 0.22f, 0.21f);
        private static readonly Color InnerEar = new Color(0.30f, 0.20f, 0.17f);
        private static readonly Color Crease = new Color(0.08f, 0.06f, 0.05f);
        private static readonly Color Dark = new Color(0.075f, 0.06f, 0.05f);
        private static readonly Color Grizzle = new Color(0.50f, 0.43f, 0.35f);

        public static void Build(Transform root)
        {
            var sdf = new SdfCreature(root) { Paint = PaintCoat, AmbientOcclusion = 0.55f };

            // ---- torso (root bone)
            sdf.Ellipsoid(root, new Vector3(0f, 0.80f, 0.26f), new Vector3(0.22f, 0.29f, 0.32f), Vector3.zero, Hide, 0.08f);   // chest + shoulders
            sdf.Ellipsoid(root, new Vector3(0f, 0.96f, 0.18f), new Vector3(0.12f, 0.13f, 0.30f), new Vector3(-6f, 0f, 0f), Hide, 0.10f); // withers hump
            sdf.Ellipsoid(root, new Vector3(0f, 0.74f, -0.12f), new Vector3(0.20f, 0.25f, 0.40f), Vector3.zero, Hide, 0.10f); // barrel
            sdf.Ellipsoid(root, new Vector3(0f, 0.60f, 0.04f), new Vector3(0.16f, 0.14f, 0.34f), Vector3.zero, Hide, 0.08f);  // belly
            sdf.Ellipsoid(root, new Vector3(0f, 0.76f, -0.45f), new Vector3(0.20f, 0.20f, 0.21f), Vector3.zero, Hide, 0.08f); // rump
            sdf.Ellipsoid(root, new Vector3(0f, 0.87f, -0.30f), new Vector3(0.14f, 0.09f, 0.25f), new Vector3(8f, 0f, 0f), Hide, 0.10f); // croup

            // ---- legs: 0 front-left, 1 front-right, 2 hind-left, 3 hind-right
            for (int i = 0; i < 4; i++)
            {
                bool hind = i >= 2;
                float side = (i % 2 == 0) ? -1f : 1f;
                Transform pivot = Joint("LegPivot_" + i, root, new Vector3(side * (hind ? 0.13f : 0.14f), 0.50f, hind ? -0.40f : 0.36f), Quaternion.identity);
                Vector3 kneePos = hind ? new Vector3(0f, -0.24f, -0.03f) : new Vector3(0f, -0.25f, 0f);
                Transform knee = Joint("Knee_" + i, pivot, kneePos, Quaternion.identity);

                if (hind)
                {
                    sdf.Ellipsoid(pivot, new Vector3(side * 0.02f, 0.20f, -0.03f), new Vector3(0.095f, 0.22f, 0.15f), Vector3.zero, Hide, 0.07f);   // ham
                    sdf.Cone(pivot, new Vector3(0f, 0.05f, 0f), kneePos, 0.092f, 0.044f, Hide, 0.05f);                                            // gaskin
                    sdf.Ellipsoid(knee, new Vector3(0f, 0f, -0.02f), new Vector3(0.034f, 0.045f, 0.042f), Vector3.zero, Hide, 0.03f);              // hock
                    Lower(sdf, knee, 0.26f, 0.035f, side);
                }
                else
                {
                    sdf.Ellipsoid(pivot, new Vector3(side * 0.01f, 0.20f, 0.01f), new Vector3(0.08f, 0.21f, 0.13f), Vector3.zero, Hide, 0.07f);    // shoulder
                    sdf.Cone(pivot, new Vector3(0f, 0.05f, 0.01f), kneePos, 0.085f, 0.042f, Hide, 0.05f);                                         // forearm
                    sdf.Ellipsoid(knee, new Vector3(0f, 0f, 0.006f), new Vector3(0.038f, 0.044f, 0.040f), Vector3.zero, Hide, 0.03f);              // carpus
                    Lower(sdf, knee, 0.25f, 0.0f, side);
                }
            }

            // ---- neck and head
            Transform neck = Joint("NeckPivot", root, new Vector3(0f, 0.82f, 0.50f), Quaternion.Euler(16f, 0f, 0f));
            sdf.Cone(neck, new Vector3(0f, 0.02f, -0.06f), new Vector3(0f, -0.02f, 0.12f), 0.21f, 0.18f, Hide, 0.10f);
            sdf.Ellipsoid(neck, new Vector3(0f, -0.14f, 0.08f), new Vector3(0.13f, 0.10f, 0.16f), Vector3.zero, Hide, 0.08f);  // throat
            sdf.Ellipsoid(neck, new Vector3(0f, 0.17f, -0.06f), new Vector3(0.06f, 0.08f, 0.22f), Vector3.zero, Hide, 0.08f);  // mane ridge

            Transform head = Joint("Head", neck, new Vector3(0f, 0f, 0.10f), Quaternion.identity);
            sdf.Ellipsoid(head, new Vector3(0f, 0.05f, 0.04f), new Vector3(0.145f, 0.17f, 0.17f), Vector3.zero, Hide, 0.08f);           // cranium
            sdf.Cone(head, new Vector3(0f, 0.05f, 0.08f), new Vector3(0f, -0.03f, 0.51f), 0.12f, 0.05f, Hide, 0.06f, new Vector3(0.82f, 1f, 1f)); // face wedge
            sdf.Cone(head, new Vector3(0f, -0.07f, 0.06f), new Vector3(0f, -0.08f, 0.46f), 0.11f, 0.04f, Hide, 0.05f, new Vector3(0.85f, 1f, 1f)); // lower jaw
            sdf.Ellipsoid(head, new Vector3(0f, -0.045f, 0.545f), new Vector3(0.062f, 0.056f, 0.03f), Vector3.zero, Disc, 0.025f, 0f);  // snout disc
            foreach (float s in new[] { -1f, 1f })
            {
                sdf.Ellipsoid(head, new Vector3(s * 0.085f, -0.05f, 0.10f), new Vector3(0.07f, 0.10f, 0.12f), Vector3.zero, Hide, 0.06f);    // jowl
                sdf.Ellipsoid(head, new Vector3(s * 0.055f, -0.078f, 0.41f), new Vector3(0.035f, 0.03f, 0.07f), Vector3.zero, Hide, 0.03f);  // lip flare over the tusk
                sdf.Ellipsoid(head, new Vector3(s * 0.07f, 0.105f, 0.19f), new Vector3(0.045f, 0.03f, 0.07f), Vector3.zero, Hide, 0.03f);    // brow ridge
                sdf.CarveEllipsoid(head, new Vector3(s * 0.022f, -0.048f, 0.572f), new Vector3(0.011f, 0.015f, 0.012f), Vector3.zero, 0.006f, Crease); // nostril
                sdf.CarveCone(head, new Vector3(s * 0.045f, -0.096f, 0.49f), new Vector3(s * 0.08f, -0.088f, 0.22f), 0.009f, 0.009f, 0.012f, Crease);   // mouth line
                sdf.CarveEllipsoid(head, new Vector3(s * 0.088f, 0.075f, 0.215f), new Vector3(0.026f, 0.022f, 0.028f), Vector3.zero, 0.012f);    // eye socket
                sdf.GlossSphere(head, new Vector3(s * 0.083f, 0.075f, 0.217f), 0.019f, new Color(0.05f, 0.035f, 0.03f));

                // Ear: flat pointed cone with a cupped inner face, tilted back and out, hair tuft at the tip
                Transform ear = Joint(s < 0f ? "Ear_L" : "Ear_R", head, new Vector3(s * 0.10f, 0.20f, 0.02f), Quaternion.Euler(-12f, s * 25f, -s * 38f));
                sdf.Cone(ear, Vector3.zero, new Vector3(0f, 0.19f, 0.012f), 0.066f, 0.010f, Hide, 0.03f, new Vector3(1f, 1f, 0.40f));
                sdf.CarveCone(ear, new Vector3(0f, 0.04f, 0.033f), new Vector3(0f, 0.14f, 0.022f), 0.044f, 0.006f, 0.008f, InnerEar, new Vector3(1f, 1f, 0.4f));
                sdf.AddTuft(ear, new Vector3(0f, 0.15f, 0.01f), Vector3.up, 10, 0.045f, 0.35f, 0.006f, Dark, Grizzle);

                // Tusk: from the lip corner forward, out and curling up and back
                sdf.GlossTube(head, new[]
                {
                    new Vector3(s * 0.055f, -0.10f, 0.42f), new Vector3(s * 0.075f, -0.085f, 0.455f), new Vector3(s * 0.092f, -0.05f, 0.47f),
                    new Vector3(s * 0.10f, -0.01f, 0.455f), new Vector3(s * 0.098f, 0.02f, 0.425f)
                }, new[] { 0.016f, 0.015f, 0.012f, 0.008f, 0.002f }, new Color(0.90f, 0.86f, 0.76f));
            }

            // ---- tail
            Transform tail = Joint("TailPivot", root, new Vector3(0f, 0.84f, -0.64f), Quaternion.identity);
            sdf.Cone(tail, Vector3.zero, new Vector3(0f, -0.20f, -0.05f), 0.032f, 0.011f, Hide, 0.03f);
            sdf.AddTuft(tail, new Vector3(0f, -0.20f, -0.05f), new Vector3(0f, -1f, -0.25f), 18, 0.09f, 0.25f, 0.006f, Dark, Hide);

            // ---- hair: bristle mane from the crown along the spine, grizzled cheek whiskers, short coat bristles
            sdf.Strands(new[]
            {
                new Vector3(0f, 1.0f, 0.70f), new Vector3(0f, 1.04f, 0.50f), new Vector3(0f, 1.09f, 0.26f), new Vector3(0f, 1.07f, 0.02f), new Vector3(0f, 0.98f, -0.24f)
            }, 340, 0.035f, t => Mathf.Lerp(0.06f, 0.15f, Mathf.Sin(Mathf.Clamp01(t * 1.25f) * Mathf.PI)) + 0.03f, 0.009f, Dark, new Color(0.24f, 0.19f, 0.15f), new Vector3(0f, 0f, -0.45f));
            sdf.BodyBristles(1600, 0.025f, 0.055f, 0.0055f, new Vector3(0f, -0.35f, -1f), 0.6f, (p, n) => p.z > 1.03f ? 0f : (p.y < 0.42f ? 0.12f : 1f));
            sdf.BodyBristles(260, 0.04f, 0.075f, 0.006f, new Vector3(0f, -1f, -0.6f), 1f, (p, n) => CheekMask(p) > 0.3f ? 1f : 0f);

            SkinnedMeshRenderer skin = sdf.Apply("Boar", 0.019f);

            // Individual coat shade (darker / lighter, warmer / greyer) so a sounder is not a row of clones.
            var block = new MaterialPropertyBlock();
            float shade = Random.Range(0.82f, 1.12f), warm = Random.Range(-0.05f, 0.05f);
            block.SetColor("_BaseColor", new Color(shade * (1f + warm), shade, shade * (1f - warm)));
            for (int i = 0; i < 2; i++) skin.SetPropertyBlock(block, i); // coat and hair, not eyes / tusks
        }

        /// <summary>Cannon, fetlock, pastern, two pointed hoof toes with a cleft and dewclaws below the carpus / hock (knee bone space).</summary>
        private static void Lower(SdfCreature sdf, Transform knee, float height, float forward, float side)
        {
            float f = forward;
            sdf.Cone(knee, Vector3.zero, new Vector3(0f, -height + 0.08f, f + 0.005f), 0.040f, 0.030f, Hide, 0.03f);          // cannon
            sdf.Ellipsoid(knee, new Vector3(0f, -height + 0.075f, f), new Vector3(0.031f, 0.032f, 0.033f), Vector3.zero, Hide, 0.02f); // fetlock
            sdf.Cone(knee, new Vector3(0f, -height + 0.075f, f), new Vector3(0f, -height + 0.05f, f + 0.02f), 0.027f, 0.024f, Hide, 0.015f); // pastern
            foreach (float t in new[] { -1f, 1f })
            {
                // Toe: wedge from the heel (up, back) to a blunt point on the ground (front)
                sdf.Cone(knee, new Vector3(t * 0.015f, -height + 0.038f, f + 0.012f), new Vector3(t * 0.019f, -height + 0.009f, f + 0.062f), 0.019f, 0.009f, Hoof, 0.006f, null, 0f);
                sdf.Ellipsoid(knee, new Vector3(t * 0.026f, -height + 0.062f, f - 0.03f), new Vector3(0.010f, 0.017f, 0.012f), new Vector3(-25f, 0f, 0f), Hoof, 0.006f, 0f); // dewclaw
            }
            sdf.CarveCone(knee, new Vector3(0f, -height + 0.03f, f + 0.03f), new Vector3(0f, -height + 0.0f, f + 0.08f), 0.0045f, 0.006f, 0.003f, Crease); // cleft between the toes
        }

        private static float CheekMask(Vector3 p)
        {
            // Sides of the head behind the snout (rest pose, VisualModel space)
            float along = Mathf.InverseLerp(0.62f, 0.72f, p.z) * Mathf.InverseLerp(1.0f, 0.88f, p.z);
            float height = Mathf.InverseLerp(0.48f, 0.58f, p.y) * Mathf.InverseLerp(0.86f, 0.74f, p.y);
            float sideways = Mathf.InverseLerp(0.05f, 0.10f, Mathf.Abs(p.x));
            return along * height * sideways;
        }

        private static Color PaintCoat(Vector3 p, Vector3 n, Color c, float paintable)
        {
            if (paintable < 0.01f) return c;
            float g = SdfCreature.Noise(p * 26f), g2 = SdfCreature.Noise(p * 7f + Vector3.one * 3.1f);
            Color col = c * Mathf.Lerp(0.82f, 1.14f, g2);
            // Black stockings
            col = Color.Lerp(col, Dark, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.52f, 0.30f, p.y)) * 0.92f);
            // Dark dorsal stripe and mane
            float dorsal = Mathf.InverseLerp(0.45f, 0.95f, n.y) * Mathf.InverseLerp(0.16f, 0.03f, Mathf.Abs(p.x));
            col = Color.Lerp(col, Dark, dorsal * 0.7f);
            // Lighter grizzled cheeks, dark muzzle towards the disc
            col = Color.Lerp(col, Grizzle, CheekMask(p) * 0.75f);
            col = Color.Lerp(col, Dark, Mathf.InverseLerp(0.90f, 1.08f, p.z) * 0.55f);
            // Grizzled speckle across the coat, slightly paler belly
            col = Color.Lerp(col, Grizzle, Mathf.InverseLerp(0.55f, 0.9f, g) * 0.35f);
            col = Color.Lerp(col, Grizzle * 0.8f, Mathf.InverseLerp(-0.3f, -0.9f, n.y) * 0.25f);
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
