using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Resources
{
    /// <summary>
    /// Resource deposit chest where NPC workers drop off harvested wood and ore.
    /// Uses static shader caching and shared procedural materials.
    /// </summary>
    [DisallowMultipleComponent]
    public class StorageChestNode : MonoBehaviour
    {
        private static Shader cachedLitShader;
        private static Material cachedWoodMat;
        private static Material cachedLidMat;
        private static Material cachedGoldMat;
        private static Material cachedIronMat;

        private static readonly List<StorageChestNode> activeChests = new List<StorageChestNode>();

        public static IReadOnlyList<StorageChestNode> ActiveChests => activeChests;
        public static StorageChestNode Instance => (activeChests.Count > 0) ? activeChests[0] : null;

        [Header("Chest Configuration")]
        [SerializeField] private float interactionRadius = 4.5f;

        public float InteractionRadius => interactionRadius;

        private void OnEnable()
        {
            if (!activeChests.Contains(this))
            {
                activeChests.Add(this);
            }
        }

        private void OnDisable()
        {
            activeChests.Remove(this);
        }

        public Vector3 GetChestDepositPosition()
        {
            return transform.position;
        }

        public static Vector3 GetNearestChestDepositPosition(Vector3 searchPos)
        {
            if (activeChests.Count == 0) return Vector3.zero;

            StorageChestNode nearest = activeChests[0];
            float minSqrDst = (searchPos - nearest.transform.position).sqrMagnitude;

            int count = activeChests.Count;
            for (int i = 1; i < count; i++)
            {
                StorageChestNode chest = activeChests[i];
                if (chest == null || !chest.gameObject.activeInHierarchy) continue;

                float sqrDst = (searchPos - chest.transform.position).sqrMagnitude;
                if (sqrDst < minSqrDst)
                {
                    minSqrDst = sqrDst;
                    nearest = chest;
                }
            }

            return nearest.transform.position;
        }

        public static GameObject CreateStorageChestPrefab(string name = "StorageChest")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<StorageChestNode>();

            if (cachedLitShader == null)
            {
                cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }
            if (cachedWoodMat == null) cachedWoodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.30f, 0.15f, 0.055f), 0f, 0.22f);
            if (cachedLidMat == null) cachedLidMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.43f, 0.23f, 0.075f), 0f, 0.24f);
            if (cachedGoldMat == null) cachedGoldMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(0.95f, 0.66f, 0.12f), 0.78f, 0.62f);
            if (cachedIronMat == null) cachedIronMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, new Color(0.16f, 0.18f, 0.19f), 0.72f, 0.44f);

            // 1. Box Base
            GameObject boxObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boxObj.name = "ChestBox";
            boxObj.transform.SetParent(root.transform, false);
            boxObj.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            boxObj.transform.localScale = new Vector3(1.4f, 0.9f, 0.95f);
            boxObj.GetComponent<MeshRenderer>().material = cachedWoodMat;

            // 2. Lid
            GameObject lidObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lidObj.name = "ChestLid";
            lidObj.transform.SetParent(root.transform, false);
            lidObj.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            lidObj.transform.localScale = new Vector3(1.44f, 0.25f, 1.0f);
            lidObj.GetComponent<MeshRenderer>().material = cachedLidMat;

            // Individual planks and iron straps stop the chest reading as a single cube.
            for (int i = -1; i <= 1; i++)
            {
                GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plank.name = $"FrontPlank_{i + 2}";
                plank.transform.SetParent(root.transform, false);
                plank.transform.localPosition = new Vector3(0f, 0.45f + i * 0.26f, 0.485f);
                plank.transform.localScale = new Vector3(1.28f, 0.24f, 0.035f);
                plank.GetComponent<MeshRenderer>().sharedMaterial = cachedLidMat;
            }

            for (int i = -1; i <= 1; i += 2)
            {
                GameObject strap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strap.name = i < 0 ? "IronStrapLeft" : "IronStrapRight";
                strap.transform.SetParent(root.transform, false);
                strap.transform.localPosition = new Vector3(i * 0.47f, 0.63f, 0.51f);
                strap.transform.localScale = new Vector3(0.09f, 1.03f, 0.05f);
                strap.GetComponent<MeshRenderer>().sharedMaterial = cachedIronMat;
            }

            // 3. Lock Band
            GameObject lockObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lockObj.name = "ChestLock";
            lockObj.transform.SetParent(root.transform, false);
            lockObj.transform.localPosition = new Vector3(0f, 0.65f, 0.48f);
            lockObj.transform.localScale = new Vector3(0.25f, 0.35f, 0.1f);
            lockObj.GetComponent<MeshRenderer>().material = cachedGoldMat;

            GameObject lockShackle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lockShackle.name = "ChestLockShackle";
            lockShackle.transform.SetParent(root.transform, false);
            lockShackle.transform.localPosition = new Vector3(0f, 0.92f, 0.48f);
            lockShackle.transform.localScale = new Vector3(0.12f, 0.18f, 0.12f);
            lockShackle.GetComponent<MeshRenderer>().sharedMaterial = cachedIronMat;

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.55f, 0f);
            col.size = new Vector3(1.6f, 1.2f, 1.2f);

            foreach (var childCol in root.GetComponentsInChildren<Collider>())
            {
                if (childCol != col) DestroyImmediate(childCol);
            }

            return root;
        }
    }
}
