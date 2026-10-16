using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Utils;
using BattleOfAges.RTS;

namespace BattleOfAges.Resources
{
    /// <summary>
    /// Resource deposit chest with rich stylized wood planks, iron corner brackets, gold clasp, and ambient warmth.
    /// </summary>
    [DisallowMultipleComponent]
    public class StorageChestNode : MonoBehaviour
    {
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
            // Destroyed or unloaded chests must leave the registry (otherwise a scene reload leaves dead entries).
            activeChests.Remove(this);
        }

        private void Start()
        {
            SnapToTerrain();
        }

        public void SnapToTerrain()
        {
            Vector3 pos = transform.position;
            pos.y = RtsUnit.GetTerrainHeight(pos);
            transform.position = pos;
        }

        public Vector3 GetChestDepositPosition()
        {
            return transform.position;
        }

        public static Vector3 GetNearestChestDepositPosition(Vector3 searchPos)
        {
            StorageChestNode nearest = null;
            float minSqrDst = float.MaxValue;

            int count = activeChests.Count;
            for (int i = 0; i < count; i++)
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

            return nearest != null ? nearest.transform.position : Vector3.zero;
        }

        public static GameObject CreateStorageChestPrefab(string name = "StorageChest")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<StorageChestNode>();
            root.AddComponent<BattleOfAges.Building.BuildingNode>(); // a built chest is a building (saved, counted)

            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.34f, 0.18f, 0.08f), 0.0f, 0.28f);
            Material darkWoodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.24f, 0.12f, 0.05f), 0.0f, 0.22f);
            Material ironMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, new Color(0.22f, 0.24f, 0.27f), 0.75f, 0.52f);
            Material goldMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(0.96f, 0.72f, 0.18f), 0.85f, 0.72f);

            GameObject visual = new GameObject("VisualModel");
            visual.transform.SetParent(root.transform, false);

            // 1. Main Chest Base Body
            CreatePart(PrimitiveType.Cube, "ChestBase", visual.transform, new Vector3(0f, 0.42f, 0f), new Vector3(1.35f, 0.75f, 0.92f), Quaternion.identity, woodMat);

            // 2. Beveled Curved Lid
            GameObject lid = new GameObject("ChestLidRoot");
            lid.transform.SetParent(visual.transform, false);
            lid.transform.localPosition = new Vector3(0f, 0.80f, 0f);

            CreatePart(PrimitiveType.Cube, "LidCenter", lid.transform, new Vector3(0f, 0.12f, 0f), new Vector3(1.40f, 0.22f, 0.96f), Quaternion.identity, darkWoodMat);
            CreatePart(PrimitiveType.Cylinder, "LidArch", lid.transform, new Vector3(0f, 0.22f, 0f), new Vector3(0.94f, 0.69f, 0.94f), Quaternion.Euler(0f, 0f, 90f), woodMat);

            // 3. Iron Corner Brackets & Reinforcement Bands
            float[] bandXs = { -0.58f, -0.22f, 0.22f, 0.58f };
            foreach (float bx in bandXs)
            {
                CreatePart(PrimitiveType.Cube, $"IronBand_{bx}", visual.transform, new Vector3(bx, 0.42f, 0f), new Vector3(0.08f, 0.78f, 0.95f), Quaternion.identity, ironMat);
            }

            // 4. Heavy Golden Front Clasp & Keyhole
            CreatePart(PrimitiveType.Cube, "GoldClaspPlate", visual.transform, new Vector3(0f, 0.68f, 0.485f), new Vector3(0.24f, 0.28f, 0.05f), Quaternion.identity, goldMat);
            CreatePart(PrimitiveType.Sphere, "GoldClaspGem", visual.transform, new Vector3(0f, 0.70f, 0.515f), new Vector3(0.10f, 0.10f, 0.08f), Quaternion.identity, goldMat);

            // 5. Side Iron Carrying Ring Handles
            CreatePart(PrimitiveType.Cylinder, "LeftRingMount", visual.transform, new Vector3(-0.69f, 0.48f, 0f), new Vector3(0.08f, 0.04f, 0.08f), Quaternion.Euler(0f, 0f, 90f), ironMat);
            CreatePart(PrimitiveType.Cylinder, "LeftRing", visual.transform, new Vector3(-0.72f, 0.42f, 0f), new Vector3(0.24f, 0.03f, 0.24f), Quaternion.Euler(90f, 0f, 0f), ironMat);

            CreatePart(PrimitiveType.Cylinder, "RightRingMount", visual.transform, new Vector3(0.69f, 0.48f, 0f), new Vector3(0.08f, 0.04f, 0.08f), Quaternion.Euler(0f, 0f, 90f), ironMat);
            CreatePart(PrimitiveType.Cylinder, "RightRing", visual.transform, new Vector3(0.72f, 0.42f, 0f), new Vector3(0.24f, 0.03f, 0.24f), Quaternion.Euler(90f, 0f, 0f), ironMat);

            // 6. Brass Corner Studs
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    CreatePart(PrimitiveType.Sphere, $"Stud_Bot_{sx}_{sz}", visual.transform, new Vector3(sx * 0.64f, 0.10f, sz * 0.44f), new Vector3(0.06f, 0.06f, 0.06f), Quaternion.identity, goldMat);
                    CreatePart(PrimitiveType.Sphere, $"Stud_Top_{sx}_{sz}", visual.transform, new Vector3(sx * 0.64f, 0.74f, sz * 0.44f), new Vector3(0.06f, 0.06f, 0.06f), Quaternion.identity, goldMat);
                }
            }

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.55f, 0f);
            col.size = new Vector3(1.5f, 1.1f, 1.1f);

            return root;
        }

        private static GameObject CreatePart(PrimitiveType type, string partName, Transform parent, Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRot;

            MeshRenderer mr = part.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            Collider c = part.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);

            return part;
        }
    }
}
