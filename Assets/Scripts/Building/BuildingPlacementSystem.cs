using UnityEngine;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.CameraSystem;
using BattleOfAges.InputSystem;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Handles the placement of buildings on the map in RTS mode.
    /// Uses MaterialPropertyBlock for zero-memory-leak ghost preview tinting and OverlapBoxNonAlloc for zero-GC placement validation.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingPlacementSystem : MonoBehaviour
    {
        public static BuildingPlacementSystem Instance { get; private set; }

        private static readonly Collider[] overlapBuffer = new Collider[32];
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        private GameModeManager gameModeManager;
        private Camera mapCamera;
        private MaterialPropertyBlock ghostPropertyBlock;

        private GameObject ghostPreview;
        private BuildingType currentBuildingType;
        private bool isPlacing = false;

        public bool IsPlacing => isPlacing;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ghostPropertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            gameModeManager = FindFirstObjectByType<GameModeManager>();

            RtsMapCameraController mapController = FindFirstObjectByType<RtsMapCameraController>();
            if (mapController != null)
            {
                mapCamera = mapController.GetComponent<Camera>();
            }
        }

        private void Update()
        {
            if (!isPlacing) return;

            // Cancel if we leave RTS mode
            if (gameModeManager != null && gameModeManager.CurrentMode != GameMode.RtsMap)
            {
                CancelPlacement();
                return;
            }

            // Ensure camera reference
            if (mapCamera == null || !mapCamera.enabled)
            {
                RtsMapCameraController mapController = FindFirstObjectByType<RtsMapCameraController>();
                if (mapController != null) mapCamera = mapController.GetComponent<Camera>();
                if (mapCamera == null) return;
            }

            UpdateGhostPosition();

            // Confirm placement on Left Mouse Button
            if (UniversalInput.GetMouseButtonDown(0))
            {
                TryConfirmPlacement();
            }

            // Cancel placement on Right Mouse Button or Escape
            if (UniversalInput.GetMouseButtonDown(1) || UniversalInput.GetKeyDownEscape())
            {
                CancelPlacement();
            }
        }

        public void StartPlacement(BuildingType type)
        {
            if (isPlacing)
            {
                CancelPlacement();
            }

            BuildingDefinition def = BuildingData.GetDefinition(type);
            if (!CanAfford(def))
            {
                Debug.Log($"[Building] Недостатньо ресурсів для {def.DisplayName}!");
                return;
            }

            currentBuildingType = type;
            isPlacing = true;

            ghostPreview = BuildingNode.CreateGhostPreview(type);
            if (ghostPreview != null)
            {
                ghostPreview.SetActive(true);
            }

            Debug.Log($"[Building] Placement mode started for: {def.DisplayName}");
        }

        public void CancelPlacement()
        {
            isPlacing = false;
            if (ghostPreview != null)
            {
                Destroy(ghostPreview);
                ghostPreview = null;
            }
        }

        private void UpdateGhostPosition()
        {
            if (ghostPreview == null || mapCamera == null) return;

            Vector3 mousePos = UniversalInput.GetMousePosition();
            Ray ray = mapCamera.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                Vector3 pos = hit.point;
                pos.y = RtsUnit.GetTerrainHeight(pos);
                ghostPreview.transform.position = pos;

                BuildingDefinition def = BuildingData.GetDefinition(currentBuildingType);
                bool validLoc = CheckLocationValid(pos, def.Size);
                bool affordable = CanAfford(def);

                Color ghostColor = (validLoc && affordable)
                    ? new Color(0.2f, 0.9f, 0.4f, 0.45f)  // Green
                    : new Color(0.95f, 0.2f, 0.2f, 0.45f); // Red

                UpdateGhostColor(ghostColor);
            }
        }

        private void UpdateGhostColor(Color color)
        {
            if (ghostPreview == null || ghostPropertyBlock == null) return;

            ghostPropertyBlock.SetColor(ColorPropertyId, color);
            ghostPropertyBlock.SetColor(BaseColorPropertyId, color);

            MeshRenderer[] renderers = ghostPreview.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].SetPropertyBlock(ghostPropertyBlock);
                }
            }
        }

        private bool CheckLocationValid(Vector3 position, Vector3 size)
        {
            Vector3 paddedSize = size + new Vector3(3.2f, 0f, 3.2f);
            Vector3 center = position + new Vector3(0f, paddedSize.y * 0.5f, 0f);
            Vector3 halfExtents = paddedSize * 0.5f;

            int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, Quaternion.identity);

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = overlapBuffer[i];
                if (col == null || col.isTrigger) continue;

                if (ghostPreview != null && col.transform.IsChildOf(ghostPreview.transform)) continue;

                string colName = col.gameObject.name;
                if (colName.Contains("SteppeTerrain") || colName.Contains("Ground")) continue;

                return false;
            }

            return true;
        }

        private void TryConfirmPlacement()
        {
            BuildingDefinition def = BuildingData.GetDefinition(currentBuildingType);
            Vector3 buildPos = (ghostPreview != null) ? ghostPreview.transform.position : transform.position;

            if (!CheckLocationValid(buildPos, def.Size))
            {
                Debug.Log($"[Building] Неможливо побудувати тут: занадто близько до іншої будівлі або об'єкта! Потрібен прохід для NPC.");
                return;
            }

            if (!CanAfford(def))
            {
                Debug.Log($"[Building] Недостатньо ресурсів для {def.DisplayName}!");
                CancelPlacement();
                return;
            }

            ResourceManager rm = ResourceManager.Instance;
            if (rm == null)
            {
                CancelPlacement();
                return;
            }

            if (def.WoodCost > 0)
            {
                rm.ConsumeResource(ResourceType.Wood, def.WoodCost);
            }

            if (def.OreCost > 0)
            {
                rm.ConsumeOre(def.OreTierRequired, def.OreCost);
            }

            GameObject building = null;
            if (currentBuildingType == BuildingType.WorkerHouse)
            {
                building = BuildingNode.CreateWorkerHousePrefab("WorkerHouse");
            }
            else if (currentBuildingType == BuildingType.StorageChest)
            {
                building = StorageChestNode.CreateStorageChestPrefab("StorageChest");
            }
            else if (currentBuildingType == BuildingType.Mine)
            {
                building = MineNode.CreateMinePrefab("Mine");
            }

            if (building != null)
            {
                building.transform.position = buildPos;

                BuildingNode node = building.GetComponent<BuildingNode>();
                if (node != null)
                {
                    node.OnBuilt();
                }
            }

            if (!CanAfford(def))
            {
                CancelPlacement();
            }
        }

        private bool CanAfford(BuildingDefinition def)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return false;

            if (def.WoodCost > 0 && rm.WoodCount < def.WoodCost) return false;
            if (def.OreCost > 0 && rm.GetOreTierCount(def.OreTierRequired) < def.OreCost) return false;

            return true;
        }
    }
}
