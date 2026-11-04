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

        private void Awake() => Managers.GameContext.Register(this);

        private void Start()
        {
            InitializeReferences();
        }

        private void InitializeReferences()
        {
            if (gameModeManager == null)
            {
                gameModeManager = Managers.GameContext.Modes;
            }

            if (mapCamera == null)
            {
                RtsMapCameraController mapController = Managers.GameContext.MapCamera;
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

            if (ZonePainter.Painting) return; // the mouse is the brush now
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
                    if (selectedUnits.Contains(hitUnit)) GameSound.Select(hitUnit);
                    return;
                }

                WorkbenchNode wb = hit.collider.GetComponentInParent<WorkbenchNode>();
                if (wb != null)
                {
                    WorkbenchUI.EnsureInstance().OpenUI(wb);
                    return;
                }
                MarketNode market = hit.collider.GetComponentInParent<MarketNode>();
                if (market != null && market.enabled) { TradeUI.Open(); return; }

                // Fallback nearest check at hit point
                RtsUnit nearestUnit = UnitRegistry.FindNearestUnit(hit.point, singleClickRadius);
                if (nearestUnit != null)
                {
                    if (!isShift) DeselectAll();
                    SelectUnit(nearestUnit);
                    if (selectedUnits.Contains(nearestUnit)) GameSound.Select(nearestUnit);
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
            RtsUnit loudest = null; // one reply for a group: of its strongest
            int best = -1;
            for (int i = 0; i < count; i++)
            {
                SelectUnit(tempUnitsBuffer[i]);
                UnitProgress p = tempUnitsBuffer[i].GetComponent<UnitProgress>();
                int rank = p == null ? 0 : p.IsHero ? 10 : (int)p.Rank;
                if (rank > best && selectedUnits.Contains(tempUnitsBuffer[i])) { best = rank; loudest = tempUnitsBuffer[i]; }
            }
            tempUnitsBuffer.Clear();
            GameSound.Select(loudest);
        }

        public IReadOnlyList<RtsUnit> SelectedUnits => selectedUnits;

        /// <summary>The selected units walk to a ground point in formation (a right-click on the minimap).</summary>
        public void OrderMoveTo(Vector3 point)
        {
            tempUnitsBuffer.Clear();
            foreach (RtsUnit u in selectedUnits) if (u != null) tempUnitsBuffer.Add(u);
            if (tempUnitsBuffer.Count > 0) Net.CommandBus.Submit(Net.UnitOrderCommand.For(Net.UnitOrderCommand.Kind.Move, tempUnitsBuffer, null, point));
            tempUnitsBuffer.Clear();
        }

        private void HandleMovementInput()
        {
            if (!UniversalInput.GetMouseButtonDown(1) || selectedUnits.Count == 0 || PointerOverUi()) return;
            Ray ray = mapCamera.ScreenPointToRay(UniversalInput.GetMousePosition());
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;

            // A snapshot: a worker that enters a building leaves the selection while we loop
            tempUnitsBuffer.Clear();
            foreach (RtsUnit u in selectedUnits) if (u != null) tempUnitsBuffer.Add(u);
            // On open ground under a tree's leaves: the tree (a trunk is hard to hit from above)
            ResourceNodeTree crown = IsOpenGround(hit.collider) ? ResourceGrid.CrownUnderRay(ray, hit.point) : null;
            if (crown != null) Net.CommandBus.Submit(Net.UnitOrderCommand.For(Net.UnitOrderCommand.Kind.Tree, tempUnitsBuffer, crown, hit.point));
            else OrderUnits(hit, tempUnitsBuffer);
            tempUnitsBuffer.Clear();
        }

        /// <summary>Nothing to give an order about here: no unit, building or resource.</summary>
        public static bool IsOpenGround(Collider c) =>
            c != null && c.GetComponentInParent<HealthComponent>() == null && c.GetComponentInParent<BuildingNode>() == null && c.GetComponentInParent<ResourceNodeTree>() == null
            && c.GetComponentInParent<OrePebbleNode>() == null && c.GetComponentInParent<StorageChestNode>() == null;

        /// <summary>Right-click order on what was hit: attack, work in a mine or at a workbench, gather, deliver, or move.</summary>
        private static void OrderUnits(RaycastHit hit, List<RtsUnit> units)
        {
            Collider target = hit.collider;
            var kind = Net.UnitOrderCommand.Kind.Move;
            Component what = null;
            HealthComponent enemy = target.GetComponentInParent<HealthComponent>();
            if ((what = target.GetComponentInParent<WorkPlace>()) != null) kind = Net.UnitOrderCommand.Kind.Place;
            else if ((what = target.GetComponentInParent<ConstructionSite>()) != null) kind = Net.UnitOrderCommand.Kind.Site;
            else if (target.GetComponentInParent<WallSegment>() != null || target.GetComponentInParent<WallPier>() != null) kind = Net.UnitOrderCommand.Kind.Wall;
            else if (enemy != null && enemy.IsAlive && enemy.GetComponent<BuildingNode>() == null) { kind = Net.UnitOrderCommand.Kind.Attack; what = enemy; } // not an own building
            else if ((what = target.GetComponentInParent<MineNode>()) != null) kind = Net.UnitOrderCommand.Kind.Mine;
            else if ((what = target.GetComponentInParent<WorkbenchNode>()) != null) kind = Net.UnitOrderCommand.Kind.Bench;
            else if ((what = target.GetComponentInParent<ResourceNodeTree>()) != null) kind = Net.UnitOrderCommand.Kind.Tree;
            else if ((what = target.GetComponentInParent<OrePebbleNode>()) != null) kind = Net.UnitOrderCommand.Kind.Pebble;
            else if (target.GetComponentInParent<StorageChestNode>() != null) kind = Net.UnitOrderCommand.Kind.Chest;
            Net.CommandBus.Submit(Net.UnitOrderCommand.For(kind, units, what, hit.point));
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

        /// <summary>Selects just this unit (a card in the selection panel, the idle-worker button).</summary>
        public void SelectOnly(RtsUnit unit)
        {
            DeselectAll();
            SelectUnit(unit);
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
