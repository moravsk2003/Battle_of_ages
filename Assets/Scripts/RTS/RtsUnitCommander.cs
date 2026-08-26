using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Managers;
using BattleOfAges.InputSystem;
using BattleOfAges.CameraSystem;
using BattleOfAges.UI;
using BattleOfAges.Building;
using BattleOfAges.Resources;

namespace BattleOfAges.RTS
{
    [DisallowMultipleComponent]
    public class RtsUnitCommander : MonoBehaviour
    {
        private const float DRAG_THRESHOLD_SQR = 100f;

        [Header("Selection Settings")]
        [SerializeField] private float singleClickRadius = 3.5f;

        [Header("References")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private SelectionBoxUI selectionBoxUI;

        private readonly List<RtsUnit> selectedUnits = new List<RtsUnit>(64);
        private readonly List<RtsUnit> tempUnitsBuffer = new List<RtsUnit>(64);

        private Vector3 startMousePos;
        private bool isDraggingSelection = false;

        private void Start()
        {
            InitializeReferences();
        }

        private void InitializeReferences()
        {
            if (gameModeManager == null)
            {
                gameModeManager = FindFirstObjectByType<GameModeManager>();
            }

            if (mapCamera == null)
            {
                RtsMapCameraController mapController = FindFirstObjectByType<RtsMapCameraController>();
                if (mapController != null)
                {
                    mapCamera = mapController.GetComponent<Camera>();
                }
            }

            if (selectionBoxUI == null)
            {
                selectionBoxUI = FindFirstObjectByType<SelectionBoxUI>();
                if (selectionBoxUI == null)
                {
                    GameObject boxObj = new GameObject("SelectionBoxUI");
                    selectionBoxUI = boxObj.AddComponent<SelectionBoxUI>();
                }
            }

            if (FindFirstObjectByType<SelectedUnitUI>() == null)
            {
                GameObject selUIObj = new GameObject("SelectedUnitUI");
                selUIObj.AddComponent<SelectedUnitUI>();
            }
        }

        private void Update()
        {
            if (gameModeManager != null && gameModeManager.CurrentMode != GameMode.RtsMap)
            {
                if (isDraggingSelection)
                {
                    isDraggingSelection = false;
                    if (selectionBoxUI != null) selectionBoxUI.HideBox();
                }
                return;
            }

            if (mapCamera == null || !mapCamera.enabled)
            {
                InitializeReferences();
                if (mapCamera == null || !mapCamera.enabled) return;
            }

            HandleSelectionInput();
            HandleMovementInput();
        }

        private void HandleSelectionInput()
        {
            if (UniversalInput.GetMouseButtonDown(0))
            {
                startMousePos = UniversalInput.GetMousePosition();
                isDraggingSelection = true;
            }

            if (UniversalInput.GetMouseButton(0) && isDraggingSelection)
            {
                Vector3 currentMouse = UniversalInput.GetMousePosition();
                if ((currentMouse - startMousePos).sqrMagnitude > DRAG_THRESHOLD_SQR)
                {
                    if (selectionBoxUI != null)
                    {
                        selectionBoxUI.ShowBox(startMousePos, currentMouse);
                    }
                }
            }

            if (UniversalInput.GetMouseButtonUp(0) && isDraggingSelection)
            {
                isDraggingSelection = false;
                if (selectionBoxUI != null) selectionBoxUI.HideBox();

                Vector3 endMousePos = UniversalInput.GetMousePosition();
                if ((endMousePos - startMousePos).sqrMagnitude > DRAG_THRESHOLD_SQR)
                {
                    HandleBoxSelection(startMousePos, endMousePos);
                }
                else
                {
                    HandleSingleSelection(endMousePos);
                }
            }
        }

        private void HandleSingleSelection(Vector3 mousePos)
        {
            bool isShift = UniversalInput.IsShiftHeld();
            Ray ray = mapCamera.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                RtsUnit hitUnit = hit.collider.GetComponentInParent<RtsUnit>();
                if (hitUnit != null)
                {
                    if (!isShift) DeselectAll();
                    SelectUnit(hitUnit);
                    return;
                }

                BattleOfAges.Crafting.WorkbenchNode wb = hit.collider.GetComponentInParent<BattleOfAges.Crafting.WorkbenchNode>();
                if (wb != null)
                {
                    BattleOfAges.UI.WorkbenchUI.EnsureInstance().OpenUI(wb);
                    return;
                }

                // Fallback nearest check at hit point
                RtsUnit nearestUnit = UnitRegistry.FindNearestUnit(hit.point, singleClickRadius);
                if (nearestUnit != null)
                {
                    if (!isShift) DeselectAll();
                    SelectUnit(nearestUnit);
                    return;
                }
            }

            if (!isShift) DeselectAll();
        }

        private void HandleBoxSelection(Vector3 screenStart, Vector3 screenEnd)
        {
            bool isShift = UniversalInput.IsShiftHeld();
            if (!isShift) DeselectAll();

            Rect selectionRect = GetScreenRect(screenStart, screenEnd);
            UnitRegistry.GetUnitsInScreenRect(selectionRect, mapCamera, tempUnitsBuffer);

            int count = tempUnitsBuffer.Count;
            for (int i = 0; i < count; i++)
            {
                SelectUnit(tempUnitsBuffer[i]);
            }
            tempUnitsBuffer.Clear();
        }

        public IReadOnlyList<RtsUnit> SelectedUnits => selectedUnits;

        private void HandleMovementInput()
        {
            if (UniversalInput.GetMouseButtonDown(1) && selectedUnits.Count > 0)
            {
                int count = selectedUnits.Count;
                Vector3 mousePos = UniversalInput.GetMousePosition();
                Ray ray = mapCamera.ScreenPointToRay(mousePos);

                if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
                {
                    // 0. Right-click Enemy Target / Dummy Target
                    BattleOfAges.Combat.HealthComponent enemyTarget = hit.collider.GetComponentInParent<BattleOfAges.Combat.HealthComponent>();
                    if (enemyTarget != null && enemyTarget.IsAlive)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            RtsUnit unit = selectedUnits[i];
                            if (unit == null) continue;

                            unit.IssueAttackCommand(enemyTarget);
                        }
                        return;
                    }

                    // 1. Right-click Mine
                    MineNode mine = hit.collider.GetComponentInParent<MineNode>();
                    if (mine != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            RtsUnit unit = selectedUnits[i];
                            if (unit == null) continue;

                            NpcResourceGatherer gatherer = unit.GetComponent<NpcResourceGatherer>();
                            if (gatherer != null && !mine.IsFullWorkers)
                            {
                                gatherer.AssignToWorkInMine(mine);
                            }
                        }
                        return;
                    }

                    // 1.5. Right-click Workbench
                    BattleOfAges.Crafting.WorkbenchNode workbench = hit.collider.GetComponentInParent<BattleOfAges.Crafting.WorkbenchNode>();
                    if (workbench != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            RtsUnit unit = selectedUnits[i];
                            if (unit == null) continue;
                            if (!workbench.IsWorkerFull)
                            {
                                workbench.AssignWorker(unit);
                            }
                        }
                        return;
                    }

                    // 2. Right-click Tree
                    ResourceNodeTree tree = hit.collider.GetComponentInParent<ResourceNodeTree>();
                    if (tree != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            RtsUnit unit = selectedUnits[i];
                            if (unit == null) continue;

                            NpcResourceGatherer gatherer = unit.GetComponent<NpcResourceGatherer>();
                            if (gatherer != null)
                            {
                                gatherer.CommandHarvestTree(tree);
                            }
                        }
                        return;
                    }

                    // 3. Right-click Ore Pebble
                    OrePebbleNode pebble = hit.collider.GetComponentInParent<OrePebbleNode>();
                    if (pebble != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            RtsUnit unit = selectedUnits[i];
                            if (unit == null) continue;

                            NpcResourceGatherer gatherer = unit.GetComponent<NpcResourceGatherer>();
                            if (gatherer != null)
                            {
                                gatherer.CommandHarvestPebble(pebble);
                            }
                        }
                        return;
                    }

                    // 4. Right-click Storage Chest
                    StorageChestNode chest = hit.collider.GetComponentInParent<StorageChestNode>();
                    if (chest != null)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            RtsUnit unit = selectedUnits[i];
                            if (unit == null) continue;

                            NpcResourceGatherer gatherer = unit.GetComponent<NpcResourceGatherer>();
                            if (gatherer != null)
                            {
                                gatherer.CommandDeliverToChest();
                            }
                        }
                        return;
                    }

                    // 5. Right-click Terrain
                    Vector3 targetPoint = hit.point;
                    targetPoint.y = RtsUnit.GetTerrainHeight(targetPoint);

                    int cols = Mathf.CeilToInt(Mathf.Sqrt(count));

                    for (int i = 0; i < count; i++)
                    {
                        RtsUnit unit = selectedUnits[i];
                        if (unit == null) continue;

                        int row = i / cols;
                        int col = i % cols;
                        Vector3 offset = (count > 1) 
                            ? new Vector3((col - cols / 2f) * 2.2f, 0, (row - cols / 2f) * 2.2f) 
                            : Vector3.zero;

                        Vector3 unitTarget = targetPoint + offset;
                        unitTarget.y = RtsUnit.GetTerrainHeight(unitTarget);
                        unit.IssueMoveCommand(unitTarget);
                    }
                }
            }
        }

        private Rect GetScreenRect(Vector3 screenPos1, Vector3 screenPos2)
        {
            screenPos1.y = Screen.height - screenPos1.y;
            screenPos2.y = Screen.height - screenPos2.y;

            float xMin = Mathf.Min(screenPos1.x, screenPos2.x);
            float xMax = Mathf.Max(screenPos1.x, screenPos2.x);
            float yMin = Mathf.Min(screenPos1.y, screenPos2.y);
            float yMax = Mathf.Max(screenPos1.y, screenPos2.y);

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void SelectUnit(RtsUnit unit)
        {
            if (unit != null && !selectedUnits.Contains(unit))
            {
                selectedUnits.Add(unit);
                unit.SetSelected(true);
            }
        }

        public void DeselectAll()
        {
            int count = selectedUnits.Count;
            for (int i = 0; i < count; i++)
            {
                if (selectedUnits[i] != null)
                {
                    selectedUnits[i].SetSelected(false);
                }
            }
            selectedUnits.Clear();
        }
    }
}
