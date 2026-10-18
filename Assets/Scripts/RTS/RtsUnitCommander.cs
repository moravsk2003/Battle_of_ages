using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using BattleOfAges.Managers;
using BattleOfAges.InputSystem;
using BattleOfAges.CameraSystem;
using BattleOfAges.UI;
using BattleOfAges.Building;
using BattleOfAges.Resources;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

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

        /// <summary>Clicks on HUD / window buttons belong to the UI, not to the map.</summary>
        private static bool PointerOverUi() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private void HandleSelectionInput()
        {
            if (UniversalInput.GetMouseButtonDown(0) && !PointerOverUi())
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

                WorkbenchNode wb = hit.collider.GetComponentInParent<WorkbenchNode>();
                if (wb != null)
                {
                    WorkbenchUI.EnsureInstance().OpenUI(wb);
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
            if (!UniversalInput.GetMouseButtonDown(1) || selectedUnits.Count == 0 || PointerOverUi()) return;
            Ray ray = mapCamera.ScreenPointToRay(UniversalInput.GetMousePosition());
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;

            // A snapshot: a worker that enters a building leaves the selection while we loop
            tempUnitsBuffer.Clear();
            foreach (RtsUnit u in selectedUnits) if (u != null) tempUnitsBuffer.Add(u);
            OrderUnits(hit, tempUnitsBuffer);
            tempUnitsBuffer.Clear();
        }

        /// <summary>Right-click order on what was hit: attack, work in a mine or at a workbench, gather, deliver, or move.</summary>
        private static void OrderUnits(RaycastHit hit, List<RtsUnit> units)
        {
            Collider target = hit.collider;

            HealthComponent enemy = target.GetComponentInParent<HealthComponent>();
            if (enemy != null && enemy.IsAlive)
            {
                foreach (RtsUnit unit in units) unit.IssueAttackCommand(enemy);
                return;
            }

            MineNode mine = target.GetComponentInParent<MineNode>();
            if (mine != null)
            {
                ForEachGatherer(units, g => { if (!mine.IsFullWorkers) g.AssignToWorkInMine(mine); });
                return;
            }

            WorkbenchNode workbench = target.GetComponentInParent<WorkbenchNode>();
            if (workbench != null)
            {
                foreach (RtsUnit unit in units) if (!workbench.IsWorkerFull) workbench.AssignWorker(unit);
                return;
            }

            ResourceNodeTree tree = target.GetComponentInParent<ResourceNodeTree>();
            if (tree != null) { ForEachGatherer(units, g => g.CommandHarvestTree(tree)); return; }

            OrePebbleNode pebble = target.GetComponentInParent<OrePebbleNode>();
            if (pebble != null) { ForEachGatherer(units, g => g.CommandHarvestPebble(pebble)); return; }

            if (target.GetComponentInParent<StorageChestNode>() != null) { ForEachGatherer(units, g => g.CommandDeliverToChest()); return; }

            MoveInFormation(units, hit.point);
        }

        private static void ForEachGatherer(List<RtsUnit> units, System.Action<NpcResourceGatherer> order)
        {
            foreach (RtsUnit unit in units)
                if (unit.TryGetComponent(out NpcResourceGatherer gatherer)) order(gatherer);
        }

        /// <summary>Spreads the units over a square grid (2.2 m apart) around the clicked point.</summary>
        private static void MoveInFormation(List<RtsUnit> units, Vector3 point)
        {
            const float spacing = 2.2f;
            int count = units.Count;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(count));
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = count > 1
                    ? new Vector3((i % cols - cols / 2f) * spacing, 0f, (i / cols - cols / 2f) * spacing)
                    : Vector3.zero;
                Vector3 target = point + offset;
                target.y = RtsUnit.GetTerrainHeight(target);
                units[i].IssueMoveCommand(target);
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
            // Workers inside a mine are out of sight; they come out with the mine's button
            if (unit != null && unit.TryGetComponent(out NpcResourceGatherer g) && g.IsInsideMine) return;
            if (unit != null && !selectedUnits.Contains(unit))
            {
                selectedUnits.Add(unit);
                unit.SetSelected(true);
            }
        }

        /// <summary>Drops one unit from the selection (it went into a building).</summary>
        public void Deselect(RtsUnit unit)
        {
            if (unit != null && selectedUnits.Remove(unit)) unit.SetSelected(false);
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
