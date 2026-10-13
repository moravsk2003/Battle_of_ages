#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.EditorTools
{
    /// <summary>
    /// Regenerates the base SteppeTerrain mesh from the current height function and re-seats the existing
    /// grounded base objects (player, NPCs, props) on the new surface, keeping their height above ground.
    /// Unlike "Setup Steppe Terrain & Trees" it does not recreate UI, character models or lighting.
    /// </summary>
    public static class RebuildBaseTerrainTool
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";

        [MenuItem("Battle of Ages/Rebuild Base Terrain (keep objects)")]
        public static void RebuildBaseTerrain()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            GameObject oldTerrain = GameObject.Find("SteppeTerrain");
            if (oldTerrain == null)
            {
                Debug.LogError("[RebuildBaseTerrain] SteppeTerrain not found in the scene.");
                return;
            }

            Collider oldCollider = oldTerrain.GetComponent<Collider>();
            var heightsAboveGround = new List<KeyValuePair<Transform, float>>();
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == oldTerrain || !IsGroundedObject(root)) continue;
                if (TryGetSurfaceHeight(oldCollider, root.transform.position, out float oldGround))
                {
                    heightsAboveGround.Add(new KeyValuePair<Transform, float>(root.transform, root.transform.position.y - oldGround));
                }
            }

            int siblingIndex = oldTerrain.transform.GetSiblingIndex();
            Object.DestroyImmediate(oldTerrain);

            GameObject newTerrain = SteppeTerrainGenerator.CreateSteppeTerrain("SteppeTerrain", 192f, 96);
            newTerrain.transform.SetSiblingIndex(siblingIndex);
            Physics.SyncTransforms();
            Collider newCollider = newTerrain.GetComponent<Collider>();

            foreach (KeyValuePair<Transform, float> entry in heightsAboveGround)
            {
                Vector3 p = entry.Key.position;
                if (TryGetSurfaceHeight(newCollider, p, out float newGround))
                {
                    entry.Key.position = new Vector3(p.x, newGround + entry.Value, p.z);
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log($"[RebuildBaseTerrain] Base plateau height {InfiniteWorldManager.BaseZoneHeight:F2} m; re-seated {heightsAboveGround.Count} objects.");
        }

        private static bool IsGroundedObject(GameObject go)
        {
            if (go.GetComponent<RectTransform>() != null) return false; // UI canvases
            return go.GetComponentInChildren<CharacterController>(true) != null
                   || go.GetComponentInChildren<Collider>(true) != null
                   || go.GetComponentInChildren<Renderer>(true) != null;
        }

        private static bool TryGetSurfaceHeight(Collider surface, Vector3 position, out float height)
        {
            Ray ray = new Ray(new Vector3(position.x, 500f, position.z), Vector3.down);
            if (surface != null && surface.Raycast(ray, out RaycastHit hit, 1000f))
            {
                height = hit.point.y;
                return true;
            }
            height = 0f;
            return false;
        }
    }
}
#endif
