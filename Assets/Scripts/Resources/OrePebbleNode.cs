using System.Collections;
using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Resources
{
    /// <summary>
    /// Ore pebble pickup node. Randomly rolls 100 ore tiers (Lvl 0..99) on pickup and respawns across steppe terrain.
    /// Uses static shader caching and shared procedural materials.
    /// </summary>
    [DisallowMultipleComponent]
    public class OrePebbleNode : MonoBehaviour
    {
        private static Shader cachedLitShader;
        private static Material cachedPebbleMaterial;
        private static Material cachedOreVeinMaterial;

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

        public static GameObject CreatePebblePrefab(string name = "OrePebble")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<OrePebbleNode>();

            if (cachedLitShader == null)
            {
                cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }
            if (cachedPebbleMaterial == null)
            {
                cachedPebbleMaterial = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.31f, 0.34f, 0.37f), 0f, 0.24f);
            }
            if (cachedOreVeinMaterial == null)
            {
                cachedOreVeinMaterial = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Ore, new Color(0.58f, 0.31f, 0.08f), 0.35f, 0.50f);
            }

            // A three-stone cluster has a more believable outline than the former single sphere.
            CreateRock("PebbleCore", root.transform, new Vector3(0f, 0.24f, 0f), new Vector3(0.58f, 0.34f, 0.53f), cachedPebbleMaterial);
            CreateRock("PebbleSideA", root.transform, new Vector3(-0.27f, 0.17f, 0.08f), new Vector3(0.30f, 0.22f, 0.28f), cachedPebbleMaterial);
            CreateRock("OreVein", root.transform, new Vector3(0.18f, 0.31f, 0.24f), new Vector3(0.26f, 0.16f, 0.20f), cachedOreVeinMaterial);

            SphereCollider col = root.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0.35f, 0);
            col.radius = 1.2f;

            foreach (Collider childCollider in root.GetComponentsInChildren<Collider>())
            {
                if (childCollider == col) continue;
                if (Application.isPlaying) Object.Destroy(childCollider);
                else Object.DestroyImmediate(childCollider);
            }

            return root;
        }

        private static GameObject CreateRock(string objectName, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = objectName;
            rock.transform.SetParent(parent, false);
            rock.transform.localPosition = localPosition;
            rock.transform.localScale = localScale;
            rock.transform.localRotation = Quaternion.Euler(Random.Range(0f, 360f), Random.Range(0f, 360f), Random.Range(0f, 360f));
            rock.GetComponent<MeshRenderer>().sharedMaterial = material;
            return rock;
        }
    }
}
