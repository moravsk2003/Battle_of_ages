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

        public int PickupPebble()
        {
            if (IsPickedUp) return 0;

            IsPickedUp = true;
            int rolledTier = OreTierHelper.DeterminePebbleOreTier();
            Debug.Log($"[Pebble Ore Pickup] Picked up Pebble! {OreTierHelper.GetTierName(rolledTier)}");

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

            SetVisualsActive(true);
            Debug.Log($"[Pebble Ore Respawn] Ore Pebble respawned at new location: {newPos}");
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

        private static Mesh sharedPebbleMesh;

        public static GameObject CreatePebblePrefab(string name = "OrePebble")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<OrePebbleNode>();

            Material stoneMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.42f, 0.40f, 0.38f), 0.05f, 0.25f);
            Material oreMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(1.0f, 0.78f, 0.22f), 0.85f, 0.75f);
            Material rubyMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.GemRuby, new Color(0.95f, 0.15f, 0.25f), 0.35f, 0.90f);

            GameObject visual = new GameObject("VisualModel");
            visual.transform.SetParent(root.transform, false);

            // Faceted Rock Core
            GameObject coreRock = new GameObject("RockCore");
            coreRock.transform.SetParent(visual.transform, false);
            coreRock.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            coreRock.transform.localScale = new Vector3(0.65f, 0.45f, 0.58f);
            MeshFilter mfCore = coreRock.AddComponent<MeshFilter>();
            if (sharedPebbleMesh == null)
            {
                sharedPebbleMesh = ProceduralMeshBuilder.CreateFacetedRockMesh(1.0f, 2, 42, 0.4f);
                sharedPebbleMesh.name = "PebbleCoreMesh";
            }
            mfCore.sharedMesh = sharedPebbleMesh;
            MeshRenderer mrCore = coreRock.AddComponent<MeshRenderer>();
            mrCore.sharedMaterial = stoneMat;
            mrCore.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mrCore.receiveShadows = true;

            // Embedded Glowing Crystal Gems
            CreateGemShard("GemShard_1", visual.transform, new Vector3(0.18f, 0.38f, 0.12f), new Vector3(0.18f, 0.28f, 0.18f), Quaternion.Euler(25f, 35f, 15f), oreMat);
            CreateGemShard("GemShard_2", visual.transform, new Vector3(-0.16f, 0.32f, -0.10f), new Vector3(0.14f, 0.22f, 0.14f), Quaternion.Euler(-20f, -45f, 10f), rubyMat);

            SphereCollider col = root.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0.35f, 0);
            col.radius = 1.2f;

            return root;
        }

        private static void CreateGemShard(string name, Transform parent, Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat)
        {
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shard.name = name;
            shard.transform.SetParent(parent, false);
            shard.transform.localPosition = localPos;
            shard.transform.localScale = localScale;
            shard.transform.localRotation = localRot;

            MeshRenderer mr = shard.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            Collider c = shard.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
    }
}
