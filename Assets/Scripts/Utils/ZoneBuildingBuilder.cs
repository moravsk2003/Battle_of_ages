using UnityEngine;
using BattleOfAges.Building;

namespace BattleOfAges.Utils
{
    /// <summary>The zones' new buildings (M5 step 4), one shared mesh each on the prop material: a tilled field with rows of crop, a sawmill shed by its log pile, a market of awninged stalls.</summary>
    public static class ZoneBuildingBuilder
    {
        private static readonly Color Soil = new Color(0.36f, 0.25f, 0.15f), Crop = new Color(0.78f, 0.68f, 0.25f), Oak = new Color(0.42f, 0.28f, 0.16f),
                                      Plank = new Color(0.62f, 0.46f, 0.28f), Thatch = new Color(0.66f, 0.55f, 0.3f), LogEnd = new Color(0.78f, 0.62f, 0.4f),
                                      Awning = new Color(0.72f, 0.2f, 0.16f), AwningB = new Color(0.88f, 0.78f, 0.55f);
        private static Mesh field, sawmill, market;

        private static GameObject Root(string name, BuildingType type, Mesh mesh, Vector3 colliderSize)
        {
            var root = new GameObject(name);
            root.AddComponent<BuildingNode>().SetType(type);
            Material look = MeshKit.PropMaterial();
            if (look != null)
            {
                var model = new GameObject("Model");
                model.transform.SetParent(root.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                model.AddComponent<MeshRenderer>().sharedMaterial = look;
            }
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, colliderSize.y * 0.5f, 0f);
            col.size = colliderSize;
            return root;
        }

        public static GameObject CreateField(string name = "Field")
        {
            if (field == null) field = FieldMesh();
            GameObject go = Root(name, BuildingType.Field, field, new Vector3(13f, 0.6f, 13f));
            go.GetComponent<BoxCollider>().isTrigger = true; // for the mouse only: farmers walk on it, and a solid one above the ground kept them off
            go.AddComponent<FieldNode>();
            return go;
        }

        public static GameObject CreateSawmill(string name = "Sawmill")
        {
            if (sawmill == null) sawmill = SawmillMesh();
            GameObject go = Root(name, BuildingType.Sawmill, sawmill, new Vector3(7f, 3.4f, 6f));
            go.AddComponent<SawmillNode>();
            return go;
        }

        public static GameObject CreateMarket(string name = "Market")
        {
            if (market == null) market = MarketMesh();
            GameObject go = Root(name, BuildingType.Market, market, new Vector3(9f, 3f, 9f));
            go.AddComponent<MarketNode>();
            return go;
        }

        /// <summary>A copy of a flat mesh laid over the land under this place: each vertex up or down by the ground under it.</summary>
        public static Mesh Draped(Mesh flat, Transform at)
        {
            Mesh mesh = Object.Instantiate(flat);
            mesh.name = flat.name + "_Draped";
            Vector3[] v = mesh.vertices;
            float y = at.position.y;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 w = at.TransformPoint(v[i]);
                v[i].y += RTS.RtsUnit.GetTerrainHeight(w) - y;
            }
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void B(MeshKit kit, Vector3 at, Vector3 size, Color c, float yaw = 0f) =>
            kit.Box(Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one), size, 0.02f, Vector3.zero, _ => c);

        /// <summary>The ploughed land alone, no thicker than paint: a fine sheet to lie on the relief (Draped), furrows in its colour.</summary>
        private static void Tilth(MeshKit kit)
        {
            const int n = 27;
            const float half = 6.5f;
            var p = new Vector3[n, n];
            var rng = new System.Random(77);
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    bool edge = i == 0 || j == 0 || i == n - 1 || j == n - 1;
                    float jx = edge ? (float)rng.NextDouble() * 0.5f - 0.25f : 0f, jz = edge ? (float)rng.NextDouble() * 0.5f - 0.25f : 0f;
                    p[i, j] = new Vector3(-half + 2f * half * i / (n - 1) + jx, 0.07f, -half + 2f * half * j / (n - 1) + jz);
                }
            kit.Grid(p, false, new Vector3(1f, 0f, 0.05f), (i, j) =>
            {
                float furrow = 0.5f + 0.5f * Mathf.Sin(j * Mathf.PI * 2f / 3.2f);
                return Color.Lerp(Soil * 0.72f, Soil * 1.1f, furrow);
            }, Vector3.up);
        }

        /// <summary>
        /// The field from afar: the land as a coarse sheet and each row of corn as one low block. Flat, the same mesh for every
        /// field (so they are drawn as one batch): it is laid on the slope by turning it (AttachField), not vertex by vertex.
        /// </summary>
        public static Mesh FieldFarModel
        {
            get
            {
                if (fieldFar != null) return fieldFar;
                var kit = new MeshKit();
                B(kit, new Vector3(0f, 0.02f, 0f), new Vector3(13.2f, 0.12f, 13.2f), Soil * 0.9f);
                Color corn = Color.Lerp(new Color(0.55f, 0.58f, 0.24f), Color.Lerp(Crop, new Color(0.88f, 0.72f, 0.33f), 0.5f), 0.6f);
                for (int r = 0; r < 8; r++) B(kit, new Vector3(0f, 0.6f, -5.6f + r * 1.6f), new Vector3(12.3f, 1.1f, 0.85f), r % 2 == 0 ? corn : corn * 0.94f);
                return fieldFar = kit.ToMesh("FieldFar");
            }
        }
        private static Mesh fieldFar;

        /// <summary>
        /// A field's model under its root: near, the mesh given (draped over the relief, this field's own); past ~55 m the
        /// shared far one, tilted to the slope under the field. Its 18 thousand triangles and a draw of its own were paid for
        /// every field in sight, however far. Neither casts a shadow (tilth lying on the ground).
        /// </summary>
        public static void AttachField(GameObject root, Mesh nearDraped)
        {
            Material look = MeshKit.PropMaterial();
            if (look == null) return;
            Renderer Part(string name, Mesh m)
            {
                var p = new GameObject(name);
                p.transform.SetParent(root.transform, false);
                p.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer mr = p.AddComponent<MeshRenderer>();
                mr.sharedMaterial = look;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return mr;
            }
            Renderer near = Part("Near", nearDraped), far = Part("Far", FieldFarModel);
            // The slope under it, in the field's own frame
            const float half = 6.5f;
            Transform t = root.transform;
            float H(float x, float z) => RTS.RtsUnit.GetTerrainHeight(t.TransformPoint(new Vector3(x, 0f, z)));
            float xp = H(half, 0f), xm = H(-half, 0f), zp = H(0f, half), zm = H(0f, -half);
            Vector3 normal = new Vector3(-(xp - xm) / (2f * half), 1f, -(zp - zm) / (2f * half)).normalized;
            far.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
            far.transform.localPosition = new Vector3(0f, (xp + xm + zp + zm) * 0.25f - t.position.y, 0f);
            LODGroup lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.25f, new[] { near }), new LOD(0.004f, new[] { far }) });
            lod.RecalculateBounds();
        }

        /// <summary>The bare models, for a kingdom's town (no building of the player's).</summary>
        public static Mesh FieldModel => field != null ? field : (field = FieldMesh());
        public static Mesh SawmillModel => sawmill != null ? sawmill : (sawmill = SawmillMesh());

        /// <summary>A field's site: the land ploughed, nothing yet growing.</summary>
        public static Mesh FieldSite { get { if (fieldSite == null) { var kit = new MeshKit(); Tilth(kit); fieldSite = kit.ToMesh("FieldSite"); } return fieldSite; } }
        private static Mesh fieldSite;

        /// <summary>Ploughed land with wheat on it: no platform, the ground itself changes (lying on the relief, FieldNode). Thin round stalks with tapering ears, leaning each its own way.</summary>
        private static Mesh FieldMesh()
        {
            var kit = new MeshKit();
            Tilth(kit);
            var rng = new System.Random(4217);
            float Rand(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            Vector3 straw = new Vector3(1f, 0f, 0.15f);
            for (int r = 0; r < 8; r++)
            {
                float z = -5.6f + r * 1.6f;
                for (int i = 0; i < 72; i++)
                {
                    float h = Rand(0.8f, 1.2f), x = Rand(-6.1f, 6.1f), side = Rand(-0.45f, 0.45f);
                    Quaternion lean = Quaternion.Euler(Rand(-11f, 11f), Rand(0f, 180f), Rand(-11f, 11f));
                    Vector3 foot = new Vector3(x, 0.05f, z + side);
                    Color stalk = Color.Lerp(new Color(0.55f, 0.58f, 0.24f), Crop * 0.92f, Rand(0f, 1f));
                    Color ear = Color.Lerp(Crop, new Color(0.88f, 0.72f, 0.33f), Rand(0f, 1f));
                    kit.Lathe(Matrix4x4.TRS(foot, lean, Vector3.one), new[] { new Vector2(0.012f, 0f), new Vector2(0.007f, h) }, 3, straw, (a, b0) => stalk);
                    kit.Lathe(Matrix4x4.TRS(foot + lean * new Vector3(0f, h, 0f), lean, Vector3.one), new[] { new Vector2(0.005f, 0f), new Vector2(0.024f, 0.07f), new Vector2(0.02f, 0.17f), new Vector2(0.002f, 0.27f) }, 4, straw, (a, b0) => ear);
                }
            }
            return kit.ToMesh("Field");
        }

        private static Mesh SawmillMesh()
        {
            var kit = new MeshKit();
            B(kit, new Vector3(0f, 0.1f, 0f), new Vector3(7f, 0.2f, 5.4f), Plank);
            foreach (float x in new[] { -3.2f, 3.2f })
                foreach (float z in new[] { -2.4f, 2.4f })
                    B(kit, new Vector3(x, 1.5f, z), new Vector3(0.24f, 2.8f, 0.24f), Oak);
            B(kit, new Vector3(0f, 3.05f, -1.35f), new Vector3(7.4f, 0.16f, 3.1f), Thatch);
            B(kit, new Vector3(0f, 3.05f, 1.35f), new Vector3(7.4f, 0.16f, 3.1f), Thatch * 0.92f);
            B(kit, new Vector3(0f, 3.25f, 0f), new Vector3(7.4f, 0.2f, 0.3f), Thatch * 0.8f);
            B(kit, new Vector3(-0.6f, 0.85f, 0f), new Vector3(3.6f, 0.16f, 1f), Oak); // the saw bench
            foreach (float x in new[] { -2f, 0.8f }) B(kit, new Vector3(x, 0.5f, 0f), new Vector3(0.2f, 0.7f, 0.9f), Oak);
            B(kit, new Vector3(-0.6f, 1.05f, 0f), new Vector3(3.2f, 0.26f, 0.26f), LogEnd);
            for (int i = 0; i < 6; i++) // the log pile
            {
                int row = i < 3 ? 0 : i < 5 ? 1 : 2;
                float z = (i < 3 ? i - 1f : i < 5 ? i - 3.5f : 0f) * 0.42f;
                B(kit, new Vector3(2.2f, 0.42f + row * 0.36f, z + 1.2f), new Vector3(2.6f, 0.36f, 0.36f), i % 2 == 0 ? LogEnd : Oak * 1.2f);
            }
            return kit.ToMesh("Sawmill");
        }

        private static Mesh MarketMesh()
        {
            var kit = new MeshKit();
            B(kit, new Vector3(0f, 0.06f, 0f), new Vector3(9f, 0.12f, 9f), Plank * 0.8f);
            foreach (Vector3 s in new[] { new Vector3(-2.6f, 0f, -2.4f), new Vector3(2.6f, 0f, -2.4f), new Vector3(0f, 0f, 2.6f) })
            {
                B(kit, s + new Vector3(0f, 0.55f, 0f), new Vector3(2.8f, 0.9f, 1.3f), Plank); // the counter
                foreach (float x in new[] { -1.4f, 1.4f })
                    foreach (float z in new[] { -0.8f, 0.8f })
                        B(kit, s + new Vector3(x, 1.25f, z), new Vector3(0.12f, 2.4f, 0.12f), Oak);
                for (int i = 0; i < 4; i++) // the striped awning
                    B(kit, s + new Vector3(-1.2f + i * 0.8f, 2.5f, 0f), new Vector3(0.8f, 0.08f, 2.1f), i % 2 == 0 ? Awning : AwningB);
                B(kit, s + new Vector3(-0.7f, 1.12f, 0f), new Vector3(0.5f, 0.3f, 0.5f), Crop);
                B(kit, s + new Vector3(0.6f, 1.12f, 0.1f), new Vector3(0.6f, 0.26f, 0.4f), LogEnd);
            }
            return kit.ToMesh("Market");
        }
    }
}
