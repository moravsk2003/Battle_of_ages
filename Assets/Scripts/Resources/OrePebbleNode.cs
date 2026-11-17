using System.Collections;
using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Resources
{
    /// <summary>
    /// Ore pebble pickup node with faceted stylized crystal nodes and glowing ore gems.
    /// </summary>
    [DisallowMultipleComponent]
    public class OrePebbleNode : MonoBehaviour
    {
        public bool IsPickedUp { get; private set; }
        public MonoBehaviour TargetedBy { get; set; }

        private Vector2Int gridCell;
        private bool inGrid;

        /// <summary>The rock's foot, to walk round (its collider is much wider: it is what a click finds).</summary>
        private const float SolidRadius = 0.35f;
        /// <summary>The ground its rock hides from the grass (the rock is wider than its foot).</summary>
        private const float CoverRadius = 0.72f;

        private void OnEnable() { gridCell = ResourceGrid.Add(this); inGrid = true; transform.hasChanged = false; StandSolid(); }
        private void OnDisable() { if (inGrid) ResourceGrid.Remove(this, gridCell); inGrid = false; World.SolidProps.Remove(this); }
        private void Start() => SyncPosition();

        // Moved after it was enabled (placed by a builder, or respawned): into its new cell
        public void SyncPosition()
        {
            if (!transform.hasChanged) return;
            transform.hasChanged = false;
            if (inGrid) gridCell = ResourceGrid.Move(this, gridCell);
            StandSolid();
        }

        private void StandSolid()
        {
            if (IsPickedUp) World.SolidProps.Remove(this);
            else World.SolidProps.Set(this, transform.position, SolidRadius, CoverRadius);
        }

        public int PickupPebble()
        {
            if (IsPickedUp) return 0;

            IsPickedUp = true;
            World.SolidProps.Remove(this);
            int rolledTier = OreTierHelper.DeterminePebbleOreTier();

            StartCoroutine(RespawnRoutine(3.0f));
            return rolledTier;
        }

        private IEnumerator RespawnRoutine(float delay)
        {
            SetVisualsActive(false);

            yield return new WaitForSeconds(delay);

            float terrainSizeHalf = 70f;
            float randX = Random.Range(-terrainSizeHalf, terrainSizeHalf);
            float randZ = Random.Range(-terrainSizeHalf, terrainSizeHalf);

            Vector3 newPos = new Vector3(randX, 0f, randZ);
            newPos.y = RtsUnit.GetTerrainHeight(newPos);

            transform.position = newPos;
            TargetedBy = null;
            IsPickedUp = false;
            SyncPosition();
            StandSolid();

            SetVisualsActive(true);
        }

        private void SetVisualsActive(bool active)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = active;
            }

            Collider[] colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = active;
            }
        }

        // ------------------------------------------------------------------ its look

        /// <summary>Share of the screen height below which a pebble is not drawn.</summary>
        public const float CullScreenHeight = 0.008f;
        /// <summary>The rock's frame in the pebble: squat and wider than deep; its foot a little in the ground.</summary>
        private static readonly Vector3 RockAt = new Vector3(0f, 0.13f, 0f), RockSize = new Vector3(0.62f, 0.55f, 0.56f);
        private static readonly Color OreColour = new Color(0.86f, 0.64f, 0.2f);
        private static Mesh rockMesh, nuggetMesh;

        /// <summary>The rock (veins of the ore on some faces) and the nuggets in it: the same two meshes for every pebble.</summary>
        private static void EnsureMeshes()
        {
            if (rockMesh != null && nuggetMesh != null) return;
            rockMesh = ProceduralMeshBuilder.CreateOreRockMesh(42, OreColour);
            // Three nuggets half sunk where the rock faces up (rock-local), the biggest near the top
            Vector3[] v = rockMesh.vertices, n = rockMesh.normals;
            var centres = new Vector3[3];
            float[] sizes = { 0.24f, 0.18f, 0.14f };
            Vector2[] aim = { new Vector2(0.15f, 0.1f), new Vector2(-0.45f, -0.3f), new Vector2(0.35f, -0.55f) }; // across the top, apart
            for (int k = 0; k < 3; k++)
            {
                float best = float.MaxValue;
                for (int i = 0; i < v.Length; i++)
                {
                    if (n[i].y < 0.35f) continue;
                    float d = new Vector2(v[i].x - aim[k].x, v[i].z - aim[k].y).sqrMagnitude;
                    if (d < best) { best = d; centres[k] = v[i] * 0.97f; }
                }
            }
            nuggetMesh = ProceduralMeshBuilder.CreateNuggetsMesh(centres, sizes, 5);
        }

        public static GameObject CreatePebblePrefab(string name = "OrePebble")
        {
            GameObject root = new GameObject(name);
            BuildVisual(root.transform);

            SphereCollider col = root.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0.35f, 0);
            col.radius = 1.2f;

            root.AddComponent<OrePebbleNode>(); // (after its look: one made before it is remade below)
            return root;
        }

        private static void BuildVisual(Transform root)
        {
            EnsureMeshes();
            Material stoneMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.42f, 0.40f, 0.38f), 0.05f, 0.25f);
            Material oreMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(1.0f, 0.78f, 0.22f), 0.85f, 0.75f);

            var visual = new GameObject("VisualModel");
            visual.transform.SetParent(root, false);
            Part("RockCore", visual.transform, rockMesh, stoneMat);
            Part("OreNuggets", visual.transform, nuggetMesh, oreMat);

            // Hundreds of these were drawn however far: gone once they are a speck (~65 m)
            LODGroup lod = root.GetComponent<LODGroup>();
            if (lod == null) lod = root.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(CullScreenHeight, visual.GetComponentsInChildren<Renderer>()) });
            lod.RecalculateBounds();
        }

        private static void Part(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = RockAt;
            go.transform.localScale = RockSize;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // tiny props: shadows cost hundreds of draw calls for no visible gain
            r.receiveShadows = true;
        }

        /// <summary>A pebble kept in the scene with an older look (a rock with two cylinders on it) is made anew.</summary>
        private void Awake()
        {
            Transform visual = transform.Find("VisualModel");
            Transform rock = visual != null ? visual.Find("RockCore") : null;
            MeshFilter mf = rock != null ? rock.GetComponent<MeshFilter>() : null;
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "OreRock" && visual.Find("OreNuggets") != null) return;
            if (visual != null)
            {
                visual.SetParent(null, false);
                if (Application.isPlaying) Destroy(visual.gameObject); else DestroyImmediate(visual.gameObject);
            }
            BuildVisual(transform);
        }
    }
}
