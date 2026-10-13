using UnityEngine;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Red deer sculpted with SdfCreature: deep narrow chest under a raised withers, tucked-up flank, rounded rump,
    /// long neck thickened by a throat mane, wedge head with a dark nose, large eyes and preorbital glands, broad cupped
    /// ears, slim legs with real joints (elbow, carpus, stifle, hock with its point) and cloven hooves with dewclaws.
    /// Coat: warm red-brown, darker spine and lower legs, pale belly, inner thighs, throat and chin, cream rump patch
    /// with a dark rim around a dark-topped tail. Stags carry branched antlers (a separate last submesh: hinds are
    /// given one material less and render without them), hinds are a little smaller.
    /// Rig (animated by PeacefulAnimalAI): LegPivot_0..3 / Knee_0..3, NeckPivot / Head / Ear_L, Ear_R, TailPivot.
    /// </summary>
    public static class DeerModelBuilder
    {
        private static readonly Color Coat = new Color(0.53f, 0.36f, 0.23f);
        private static readonly Color Dorsal = new Color(0.38f, 0.24f, 0.14f);
        private static readonly Color Flank = new Color(0.62f, 0.46f, 0.31f);
        private static readonly Color Belly = new Color(0.86f, 0.79f, 0.66f);
        private static readonly Color White = new Color(0.93f, 0.89f, 0.80f);
        private static readonly Color LegDark = new Color(0.33f, 0.24f, 0.17f);
        private static readonly Color EarColor = new Color(0.50f, 0.38f, 0.27f);
        private static readonly Color RumpColor = new Color(0.84f, 0.73f, 0.55f);
        private static readonly Color InnerEar = new Color(0.86f, 0.78f, 0.68f);
        private static readonly Color Dark = new Color(0.07f, 0.06f, 0.055f);
        private static readonly Color Hoof = new Color(0.15f, 0.13f, 0.12f);
        private static readonly Color Crease = new Color(0.10f, 0.07f, 0.05f);
        private static readonly Color AntlerBase = new Color(0.34f, 0.26f, 0.18f);
        private static readonly Color AntlerTip = new Color(0.88f, 0.82f, 0.70f);

        private static int spawned;
        private static Material[] stagMaterials;

        /// <summary>Stag (antlers, full size) or hind (antler submesh not rendered, a little smaller). visualModel = the deer's VisualModel.</summary>
        public static void SetStag(Transform visualModel, bool stag)
        {
            if (stagMaterials == null) return;
            Material[] mats = stag ? stagMaterials : new[] { stagMaterials[0], stagMaterials[1], stagMaterials[2] };
            foreach (SkinnedMeshRenderer skin in visualModel.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterials = mats;
            visualModel.localScale = Vector3.one * (stag ? 1f : 0.92f);
        }

        public static void Build(Transform root)
        {
            var sdf = new SdfCreature(root) { Paint = PaintCoat, AmbientOcclusion = 0.5f };

            // ---- torso (root bone)
            sdf.Ellipsoid(root, new Vector3(0f, 1.08f, 0.30f), new Vector3(0.19f, 0.26f, 0.28f), Vector3.zero, Coat, 0.09f);              // chest
            sdf.Ellipsoid(root, new Vector3(0f, 1.27f, 0.26f), new Vector3(0.10f, 0.10f, 0.24f), new Vector3(-8f, 0f, 0f), Coat, 0.10f);  // withers
            sdf.Ellipsoid(root, new Vector3(0f, 1.10f, -0.06f), new Vector3(0.20f, 0.23f, 0.37f), Vector3.zero, Coat, 0.10f);           // barrel
            sdf.Ellipsoid(root, new Vector3(0f, 0.98f, 0.06f), new Vector3(0.16f, 0.12f, 0.26f), Vector3.zero, Coat, 0.08f);             // belly
            sdf.Ellipsoid(root, new Vector3(0f, 1.15f, -0.40f), new Vector3(0.165f, 0.18f, 0.22f), Vector3.zero, Coat, 0.09f);           // rump
            sdf.Ellipsoid(root, new Vector3(0f, 1.27f, -0.32f), new Vector3(0.12f, 0.08f, 0.26f), new Vector3(6f, 0f, 0f), Coat, 0.10f); // croup
            sdf.Ellipsoid(root, new Vector3(0f, 1.00f, 0.48f), new Vector3(0.12f, 0.16f, 0.11f), Vector3.zero, Coat, 0.07f);             // brisket
            foreach (float s in new[] { -1f, 1f })
                sdf.Ellipsoid(root, new Vector3(s * 0.13f, 1.18f, 0.36f), new Vector3(0.06f, 0.15f, 0.11f), new Vector3(-20f, 0f, 0f), Coat, 0.07f); // shoulder blade

            // ---- legs: 0 front-left, 1 front-right, 2 hind-left, 3 hind-right
            for (int i = 0; i < 4; i++)
            {
                bool hind = i >= 2;
                float side = (i % 2 == 0) ? -1f : 1f;
                Transform pivot = Joint("LegPivot_" + i, root, hind ? new Vector3(side * 0.105f, 1.08f, -0.40f) : new Vector3(side * 0.115f, 1.00f, 0.40f), Quaternion.identity);
                Vector3 kneePos = hind ? new Vector3(0f, -0.60f, -0.13f) : new Vector3(0f, -0.56f, 0f);
                Transform knee = Joint("Knee_" + i, pivot, kneePos, Quaternion.identity);

                if (hind)
                {
                    sdf.Ellipsoid(pivot, new Vector3(side * 0.02f, 0f, -0.01f), new Vector3(0.085f, 0.25f, 0.16f), new Vector3(10f, 0f, 0f), Coat, 0.07f); // ham
                    sdf.Ellipsoid(pivot, new Vector3(side * 0.01f, -0.20f, 0.06f), new Vector3(0.055f, 0.08f, 0.06f), Vector3.zero, Coat, 0.05f);         // stifle
                    sdf.Cone(pivot, new Vector3(0f, -0.20f, 0.03f), kneePos, 0.066f, 0.028f, Coat, 0.05f, new Vector3(0.85f, 1f, 1f));                    // gaskin
                    sdf.Cone(pivot, new Vector3(0f, -0.36f, -0.07f), kneePos + new Vector3(0f, 0.03f, -0.035f), 0.026f, 0.015f, Coat, 0.03f);              // hamstring to the point of hock
                    sdf.Ellipsoid(knee, new Vector3(0f, 0f, -0.01f), new Vector3(0.025f, 0.038f, 0.034f), Vector3.zero, Coat, 0.02f);                      // hock
                    Lower(sdf, knee, 0.48f, 0.02f);
                }
                else
                {
                    sdf.Ellipsoid(pivot, new Vector3(side * 0.015f, 0.02f, 0f), new Vector3(0.07f, 0.19f, 0.11f), Vector3.zero, Coat, 0.06f);   // upper arm
                    sdf.Ellipsoid(pivot, new Vector3(0f, -0.15f, -0.06f), new Vector3(0.045f, 0.05f, 0.05f), Vector3.zero, Coat, 0.04f);        // elbow
                    sdf.Cone(pivot, new Vector3(0f, -0.12f, -0.01f), kneePos, 0.064f, 0.029f, Coat, 0.04f, new Vector3(0.85f, 1f, 1f));          // forearm
                    sdf.Ellipsoid(knee, new Vector3(0f, 0f, 0.006f), new Vector3(0.027f, 0.034f, 0.031f), Vector3.zero, Coat, 0.02f);            // carpus
                    Lower(sdf, knee, 0.44f, 0f);
                }
            }

            // ---- neck: deep at the base, laterally compressed, throat mane
            Transform neck = Joint("NeckPivot", root, NeckBase, Quaternion.identity);
            sdf.Cone(neck, Vector3.zero, new Vector3(0f, 0.39f, 0.24f), 0.16f, 0.08f, Coat, 0.08f, new Vector3(0.75f, 1f, 1f));
            sdf.Ellipsoid(neck, new Vector3(0f, -0.02f, 0.06f), new Vector3(0.12f, 0.17f, 0.12f), new Vector3(-35f, 0f, 0f), Coat, 0.08f);  // base
            sdf.Ellipsoid(neck, new Vector3(0f, 0.12f, 0.13f), new Vector3(0.07f, 0.13f, 0.07f), new Vector3(-30f, 0f, 0f), Coat, 0.06f);   // throat mane

            // ---- head
            Transform head = Joint("Head", neck, HeadPos - NeckBase, Quaternion.identity);
            sdf.Ellipsoid(head, new Vector3(0f, 0.01f, 0f), new Vector3(0.072f, 0.07f, 0.085f), Vector3.zero, Coat, 0.04f);                     // cranium
            sdf.Ellipsoid(head, new Vector3(0f, 0.035f, 0.06f), new Vector3(0.06f, 0.04f, 0.07f), new Vector3(15f, 0f, 0f), Coat, 0.04f);       // forehead
            sdf.Cone(head, new Vector3(0f, 0f, 0.04f), new Vector3(0f, -0.055f, 0.25f), 0.062f, 0.03f, Coat, 0.04f, new Vector3(0.82f, 1f, 1f)); // face
            sdf.Cone(head, new Vector3(0f, -0.045f, 0.02f), new Vector3(0f, -0.088f, 0.235f), 0.05f, 0.022f, Coat, 0.03f, new Vector3(0.8f, 1f, 1f)); // lower jaw
            sdf.Ellipsoid(head, new Vector3(0f, -0.06f, 0.262f), new Vector3(0.034f, 0.032f, 0.03f), Vector3.zero, Dark, 0.02f, 0f);            // nose
            foreach (float s in new[] { -1f, 1f })
            {
                sdf.Ellipsoid(head, new Vector3(s * 0.042f, -0.035f, 0.05f), new Vector3(0.03f, 0.04f, 0.055f), Vector3.zero, Coat, 0.03f);       // cheek
                sdf.Ellipsoid(head, new Vector3(s * 0.055f, 0.045f, 0.07f), new Vector3(0.028f, 0.016f, 0.035f), Vector3.zero, Coat, 0.02f);      // brow
                sdf.CarveEllipsoid(head, new Vector3(s * 0.066f, 0.022f, 0.075f), new Vector3(0.019f, 0.016f, 0.02f), Vector3.zero, 0.008f);      // eye socket
                sdf.GlossSphere(head, new Vector3(s * 0.058f, 0.022f, 0.077f), 0.016f, new Color(0.05f, 0.035f, 0.03f));
                sdf.CarveCone(head, new Vector3(s * 0.052f, 0.008f, 0.10f), new Vector3(s * 0.044f, -0.006f, 0.128f), 0.006f, 0.004f, 0.006f, Crease); // preorbital gland
                sdf.CarveEllipsoid(head, new Vector3(s * 0.018f, -0.055f, 0.284f), new Vector3(0.008f, 0.012f, 0.01f), new Vector3(0f, s * 20f, 0f), 0.005f, Crease); // nostril
                sdf.CarveCone(head, new Vector3(s * 0.02f, -0.082f, 0.25f), new Vector3(s * 0.04f, -0.075f, 0.12f), 0.006f, 0.006f, 0.008f, Crease);  // mouth line

                // Ear: broad leaf, pale cupped inner face, tilted out and back
                Transform ear = Joint(s < 0f ? "Ear_L" : "Ear_R", head, new Vector3(s * 0.055f, 0.065f, -0.035f), Quaternion.Euler(-15f, s * 20f, -s * 52f));
                sdf.Cone(ear, Vector3.zero, new Vector3(0f, 0.05f, 0f), 0.022f, 0.03f, EarColor, 0.015f);
                sdf.Ellipsoid(ear, new Vector3(0f, 0.09f, 0f), new Vector3(0.044f, 0.088f, 0.02f), Vector3.zero, EarColor, 0.015f);
                sdf.Ellipsoid(ear, new Vector3(0f, 0.168f, 0f), new Vector3(0.016f, 0.02f, 0.016f), Vector3.zero, Dark * 2f, 0.012f, 0.3f);    // dark tip
                sdf.CarveEllipsoid(ear, new Vector3(0f, 0.09f, 0.02f), new Vector3(0.031f, 0.07f, 0.014f), Vector3.zero, 0.01f, InnerEar);

                // Antler: beam up, out and back from the pedicle, brow and trez tines forward, crown fork at the top
                sdf.Tube(head, new[]
                {
                    new Vector3(s * 0.038f, 0.05f, -0.004f), new Vector3(s * 0.052f, 0.10f, -0.014f), new Vector3(s * 0.08f, 0.15f, -0.035f), new Vector3(s * 0.125f, 0.23f, -0.075f),
                    new Vector3(s * 0.17f, 0.32f, -0.10f), new Vector3(s * 0.195f, 0.41f, -0.09f), new Vector3(s * 0.19f, 0.49f, -0.05f), new Vector3(s * 0.165f, 0.55f, 0f)
                }, new[] { 0.021f, 0.024f, 0.017f, 0.015f, 0.013f, 0.011f, 0.009f, 0.003f }, AntlerBase, AntlerTip);
                Color tineBase = Color.Lerp(AntlerBase, AntlerTip, 0.45f);
                sdf.Tube(head, new[]
                {
                    new Vector3(s * 0.078f, 0.145f, -0.036f), new Vector3(s * 0.085f, 0.17f, 0.02f), new Vector3(s * 0.09f, 0.19f, 0.08f), new Vector3(s * 0.085f, 0.235f, 0.13f), new Vector3(s * 0.08f, 0.26f, 0.15f)
                }, new[] { 0.013f, 0.011f, 0.008f, 0.005f, 0.002f }, tineBase, AntlerTip);
                sdf.Tube(head, new[]
                {
                    new Vector3(s * 0.122f, 0.225f, -0.077f), new Vector3(s * 0.135f, 0.26f, -0.02f), new Vector3(s * 0.14f, 0.30f, 0.04f), new Vector3(s * 0.138f, 0.32f, 0.06f)
                }, new[] { 0.011f, 0.009f, 0.005f, 0.002f }, tineBase, AntlerTip);
                sdf.Tube(head, new[]
                {
                    new Vector3(s * 0.194f, 0.405f, -0.092f), new Vector3(s * 0.215f, 0.46f, -0.13f), new Vector3(s * 0.225f, 0.52f, -0.15f), new Vector3(s * 0.226f, 0.545f, -0.155f)
                }, new[] { 0.009f, 0.007f, 0.004f, 0.002f }, tineBase, AntlerTip);
            }

            // ---- tail: short, flat, white tuft underneath
            Transform tail = Joint("TailPivot", root, new Vector3(0f, 1.28f, -0.60f), Quaternion.identity);
            sdf.Cone(tail, Vector3.zero, new Vector3(0f, -0.14f, -0.05f), 0.04f, 0.028f, Coat, 0.03f, new Vector3(1f, 1f, 0.6f));
            sdf.AddTuft(tail, new Vector3(0f, -0.13f, -0.03f), new Vector3(0f, -1f, 0.2f), 14, 0.05f, 0.35f, 0.005f, White, Belly);

            // ---- hair: shaggy throat mane
            sdf.BodyBristles(500, 0.03f, 0.065f, 0.006f, new Vector3(0f, -1f, -0.3f), 0.5f, (p, n) => ManeMask(p, n));

            // Full mesh up close (~10 m at a 60 deg view), a coarse one beyond, culled when a speck (as the characters)
            SkinnedMeshRenderer[] skins = sdf.ApplyWithLod("Deer", 0.017f, 0.028f, 0.18f, 0.006f);

            if (skins[0].sharedMaterials.Length > 3) stagMaterials = skins[0].sharedMaterials;
            SetStag(root, spawned++ % 3 == 0); // two hinds to a stag

            // Individual coat shade so a herd is not a row of clones.
            var block = new MaterialPropertyBlock();
            float shade = Random.Range(0.88f, 1.1f), warm = Random.Range(-0.04f, 0.04f);
            block.SetColor("_BaseColor", new Color(shade * (1f + warm), shade, shade * (1f - warm)));
            foreach (SkinnedMeshRenderer skin in skins)
                for (int i = 0; i < 2; i++) skin.SetPropertyBlock(block, i); // coat and hair, not eyes / hooves / antlers
        }

        /// <summary>Cannon, fetlock, pastern, two pointed hoof toes with a cleft and dewclaws below the carpus / hock (knee bone space).</summary>
        private static void Lower(SdfCreature sdf, Transform knee, float height, float f)
        {
            float h = height;
            sdf.Cone(knee, Vector3.zero, new Vector3(0f, -h + 0.075f, f + 0.005f), 0.025f, 0.022f, Coat, 0.02f, new Vector3(0.9f, 1f, 1f)); // cannon
            sdf.Ellipsoid(knee, new Vector3(0f, -h + 0.07f, f), new Vector3(0.022f, 0.025f, 0.026f), Vector3.zero, Coat, 0.015f);               // fetlock
            sdf.Cone(knee, new Vector3(0f, -h + 0.07f, f), new Vector3(0f, -h + 0.035f, f + 0.018f), 0.021f, 0.02f, Coat, 0.012f);               // pastern
            foreach (float t in new[] { -1f, 1f })
            {
                sdf.Cone(knee, new Vector3(t * 0.011f, -h + 0.03f, f + 0.008f), new Vector3(t * 0.013f, -h + 0.006f, f + 0.05f), 0.016f, 0.006f, Hoof, 0.005f, null, 0f); // toe
                sdf.Ellipsoid(knee, new Vector3(t * 0.019f, -h + 0.07f, f - 0.024f), new Vector3(0.008f, 0.013f, 0.009f), new Vector3(-25f, 0f, 0f), Hoof, 0.005f, 0f);   // dewclaw
            }
            sdf.CarveCone(knee, new Vector3(0f, -h + 0.024f, f + 0.025f), new Vector3(0f, -h, f + 0.065f), 0.004f, 0.005f, 0.003f, Crease); // cleft
        }

        // ------------------------------------------------------------------ coat (rest pose, VisualModel space)

        private static readonly Vector3 NeckBase = new Vector3(0f, 1.24f, 0.46f);
        private static readonly Vector3 HeadPos = new Vector3(0f, 1.67f, 0.72f);       // rest position of the Head joint
        private static readonly Vector3 ThroatNormal = new Vector3(0f, -0.524f, 0.852f); // front of the neck

        private static float Near(Vector3 p, Vector3 c, float inner, float outer) => Mathf.InverseLerp(outer, inner, Vector3.Distance(p, c));

        private static float ManeMask(Vector3 p, Vector3 n)
        {
            // Underside and sides of the lower two thirds of the neck
            float along = Mathf.InverseLerp(1.16f, 1.26f, p.y) * Mathf.InverseLerp(1.50f, 1.42f, p.y) * Mathf.InverseLerp(0.44f, 0.52f, p.z) * Mathf.InverseLerp(0.78f, 0.72f, p.z);
            float facing = Mathf.InverseLerp(-0.1f, 0.5f, Vector3.Dot(n, ThroatNormal));
            return along * facing;
        }

        private static Color PaintCoat(Vector3 p, Vector3 n, Color c, float paintable)
        {
            if (paintable < 0.01f) return c;
            float g = SdfCreature.Noise(p * 9f + Vector3.one * 2.3f), g2 = SdfCreature.Noise(p * 31f);
            Color col = c * Mathf.Lerp(0.9f, 1.08f, g) * Mathf.Lerp(0.96f, 1.03f, g2);
            float ax = Mathf.Abs(p.x);
            bool torso = p.z > -0.66f && p.z < 0.62f;

            // Darker spine (also along the back of the neck), lighter lower flanks
            float dorsal = Mathf.InverseLerp(0.35f, 0.9f, n.y) * Mathf.InverseLerp(0.12f, 0.02f, ax);
            dorsal = Mathf.Max(dorsal, Near(p, new Vector3(0f, 1.488f, 0.542f), 0.06f, 0.22f) * 0.7f * Mathf.InverseLerp(0.2f, 0.7f, -Vector3.Dot(n, ThroatNormal)));
            col = Color.Lerp(col, Dorsal, dorsal * 0.55f);
            if (torso) col = Color.Lerp(col, Flank, Mathf.InverseLerp(1.14f, 0.98f, p.y) * Mathf.InverseLerp(0.80f, 0.92f, p.y) * 0.35f);

            // Pale belly, inner thighs and forearms
            float belly = Mathf.InverseLerp(-0.25f, -0.75f, n.y) * Mathf.InverseLerp(1.04f, 0.9f, p.y) * (torso ? 1f : 0f) * Mathf.InverseLerp(0.55f, 0.4f, p.z);
            float inner = Mathf.InverseLerp(0.15f, 0.75f, -Mathf.Sign(p.x) * n.x) * Mathf.InverseLerp(1.02f, 0.88f, p.y) * Mathf.InverseLerp(0.25f, 0.55f, p.y);
            col = Color.Lerp(col, Belly, Mathf.Max(belly * 0.9f, inner * 0.7f));

            // Lower legs: darker towards the hooves, a dark band at the coronet
            col = Color.Lerp(col, LegDark, Mathf.InverseLerp(0.62f, 0.18f, p.y) * 0.55f);
            col = Color.Lerp(col, Dark * 2f, Mathf.InverseLerp(0.075f, 0.04f, p.y) * 0.7f);

            // Rump patch: cream, with a dark rim, around the tail (whose top is dark)
            float rump = Mathf.InverseLerp(-0.15f, -0.7f, n.z) * Mathf.InverseLerp(0.17f, 0.08f, ax)
                       * Mathf.InverseLerp(0.86f, 0.98f, p.y) * Mathf.InverseLerp(1.42f, 1.30f, p.y) * Mathf.InverseLerp(-0.40f, -0.50f, p.z);
            col = Color.Lerp(col, Dorsal * 0.8f, Mathf.Clamp01(1f - Mathf.Abs(rump - 0.3f) / 0.2f) * 0.5f);
            col = Color.Lerp(col, RumpColor, Mathf.InverseLerp(0.35f, 0.7f, rump));
            float tailTop = Mathf.InverseLerp(-0.61f, -0.66f, p.z) * Mathf.InverseLerp(1.10f, 1.18f, p.y) * Mathf.InverseLerp(-0.2f, -0.6f, n.z) * Mathf.InverseLerp(0.05f, 0.02f, ax);
            col = Color.Lerp(col, Dorsal, tailTop * 0.5f);

            // Head and throat: white throat patch and chin, pale band behind the nose, pale eye rings, darker forehead
            float throat = Near(p, new Vector3(0f, 1.488f, 0.722f), 0.05f, 0.10f) * Mathf.InverseLerp(0.2f, 0.6f, Vector3.Dot(n, ThroatNormal));
            float chin = Near(p, HeadPos + new Vector3(0f, -0.085f, 0.16f), 0.03f, 0.06f) * Mathf.InverseLerp(-0.1f, -0.5f, n.y);
            col = Color.Lerp(col, White, Mathf.Max(throat * 0.85f, chin * 0.8f));
            float muzzle = Near(p, HeadPos + new Vector3(0f, -0.055f, 0.24f), 0.035f, 0.05f) * (1f - Near(p, HeadPos + new Vector3(0f, -0.06f, 0.262f), 0.032f, 0.04f));
            col = Color.Lerp(col, Belly, muzzle * 0.7f);
            foreach (float s in new[] { -1f, 1f })
            {
                float d = Vector3.Distance(p, HeadPos + new Vector3(s * 0.064f, 0.022f, 0.075f));
                col = Color.Lerp(col, Belly, Mathf.Clamp01(1f - Mathf.Abs(d - 0.026f) / 0.008f) * 0.55f);
            }
            col = Color.Lerp(col, Dorsal * 0.9f, Near(p, HeadPos + new Vector3(0f, 0.06f, 0.05f), 0.03f, 0.07f) * Mathf.InverseLerp(0.3f, 0.8f, n.y) * 0.6f);

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
