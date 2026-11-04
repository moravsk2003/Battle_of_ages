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
        /// <summary>How far from a unit's body a click still means it, on a 1080p screen.</summary>
        private const float ClickPixels = 26f;
        private const float DoubleClickSeconds = 0.35f, DoubleTapSeconds = 0.35f;
        public const int GroupCount = 10;

        [Header("References")]
        [SerializeField] private Camera mapCamera;
        [SerializeField] private GameModeManager gameModeManager;
        [SerializeField] private SelectionBoxUI selectionBoxUI;

        private readonly List<RtsUnit> selectedUnits = new List<RtsUnit>(64);
        private readonly List<RtsUnit> tempUnitsBuffer = new List<RtsUnit>(64);

        private Vector3 startMousePos;
        private bool isDraggingSelection = false;
        private bool rightArmed;
        private Vector3 rightStart;
        private RtsUnit lastClicked;
        private float lastClickAt = -10f;
        private readonly List<RtsUnit>[] groups = new List<RtsUnit>[GroupCount];
        private int lastGroupTapped = -1;
        private float lastGroupTappedAt = -10f;

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
            DropTheFallen();
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
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            if (placer != null && (placer.IsPlacing || placer.CancelledFrame == Time.frameCount))
            {
                // The mouse places the building: its clicks choose nobody and send nobody (Z, X and Tab are its too)
                isDraggingSelection = false;
                rightArmed = false;
                if (selectionBoxUI != null) selectionBoxUI.HideBox();
                return;
            }
            HandleSelectionInput();
            HandleMovementInput();
            if (!GameWindows.BlockGameplay) HandleHotkeys();
        }

        /// <summary>A unit that fell stays in no selection and no group: its card lingered, and orders went to its body.</summary>
        private void DropTheFallen()
        {
            for (int i = selectedUnits.Count - 1; i >= 0; i--)
            {
                RtsUnit u = selectedUnits[i];
                if (u != null && u.IsAlive) continue;
                if (u != null) u.SetSelected(false);
                selectedUnits.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ keys

        private void HandleHotkeys()
        {
            bool ctrl = UniversalInput.IsCtrlHeld();
            for (int n = 0; n < GroupCount; n++)
            {
                if (!UniversalInput.IsKeyDown(KeyCode.Alpha0 + n)) continue;
                if (ctrl) SetGroup(n);
                else if (UniversalInput.IsShiftHeld()) AddToGroup(n);
                else TapGroup(n);
                return;
            }
            if (ctrl && UniversalInput.IsKeyDown(KeyCode.A)) { SelectEveryone(); return; }
            if (KeybindsManager.GetKeyDown(KeyAction.StopUnits)) OrderStop();
            if (KeybindsManager.GetKeyDown(KeyAction.SelectFighters)) SelectFighters();
            if (KeybindsManager.GetKeyDown(KeyAction.CenterSelection)) CenterOnSelection();
        }

        // ------------------------------------------------------------------ groups

        /// <summary>Who is in this group now (the fallen are gone from it).</summary>
        public List<RtsUnit> Group(int n)
        {
            if (groups[n] == null) groups[n] = new List<RtsUnit>();
            groups[n].RemoveAll(u => u == null || !u.IsAlive);
            return groups[n];
        }

        /// <summary>Ctrl + number: the group is now those chosen.</summary>
        public void SetGroup(int n)
        {
            List<RtsUnit> group = Group(n);
            group.Clear();
            group.AddRange(selectedUnits);
            StrategyHud.Notify(group.Count > 0 ? $"Група {n}: {group.Count}" : $"Групу {n} розпущено");
        }

        /// <summary>Shift + number: those chosen join the group.</summary>
        public void AddToGroup(int n)
        {
            List<RtsUnit> group = Group(n);
            foreach (RtsUnit u in selectedUnits) if (!group.Contains(u)) group.Add(u);
            StrategyHud.Notify($"Група {n}: {group.Count}");
        }

        /// <summary>A number: the group is chosen; pressed again at once, the camera goes to it.</summary>
        private void TapGroup(int n)
        {
            bool again = lastGroupTapped == n && Time.unscaledTime - lastGroupTappedAt < DoubleTapSeconds;
            lastGroupTapped = n;
            lastGroupTappedAt = Time.unscaledTime;
            if (!SelectGroup(n)) return;
            if (again) CenterOnSelection();
        }

        /// <summary>Chooses the group; false if there is nobody in it.</summary>
        public bool SelectGroup(int n)
        {
            List<RtsUnit> group = Group(n);
            if (group.Count == 0) return false;
            DeselectAll();
            foreach (RtsUnit u in group) SelectUnit(u);
            SortSelection();
            GameSound.Select(selectedUnits.Count > 0 ? selectedUnits[0] : null);
            return selectedUnits.Count > 0;
        }

        // ------------------------------------------------------------------ choosing many

        /// <summary>Ranks alike: the same kind, for a double click. A hero is of no kind but its own.</summary>
        public static bool SameKind(RtsUnit a, RtsUnit b)
        {
            if (a == null || b == null) return false;
            if (a.IsHero || b.IsHero) return a == b;
            UnitProgress pa = a.GetComponent<UnitProgress>(), pb = b.GetComponent<UnitProgress>();
            return (pa != null ? pa.Rank : UnitRank.Person) == (pb != null ? pb.Rank : UnitRank.Person);
        }

        /// <summary>Everyone of this one's kind in view (a double click, Ctrl+click).</summary>
        public void SelectKindOnScreen(RtsUnit like, Camera camera, bool adding)
        {
            if (!adding) DeselectAll();
            foreach (RtsUnit u in UnitRegistry.ActiveUnits)
            {
                if (u == null || !u.IsAlive || !SameKind(u, like)) continue;
                Vector3 v = camera.WorldToViewportPoint(u.transform.position);
                if (v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f) SelectUnit(u);
            }
            SortSelection();
        }

        /// <summary>Ctrl+A: all one's people, wherever they are. Not the hero: an order to everyone would walk it off too.</summary>
        public void SelectEveryone()
        {
            DeselectAll();
            foreach (RtsUnit u in UnitRegistry.ActiveUnits) if (u != null && u.IsAlive && !u.IsHero) SelectUnit(u);
            SortSelection();
            GameSound.Select(selectedUnits.Count > 0 ? selectedUnits[0] : null);
        }

        /// <summary>Everyone of a warrior's rank and up, wherever they are.</summary>
        public void SelectFighters()
        {
            DeselectAll();
            foreach (RtsUnit u in UnitRegistry.ActiveUnits)
            {
                if (u == null || !u.IsAlive || u.IsHero) continue;
                UnitProgress p = u.GetComponent<UnitProgress>();
                if (p != null && p.Rank >= UnitRank.Warrior) SelectUnit(u);
            }
            SortSelection();
            if (selectedUnits.Count == 0) StrategyHud.Notify("Бійців ще немає: воїнами стають у боях");
            else GameSound.Select(selectedUnits[0]);
        }

        /// <summary>The camera goes to the middle of those chosen.</summary>
        public void CenterOnSelection()
        {
            if (selectedUnits.Count == 0) return;
            Vector3 middle = Vector3.zero;
            foreach (RtsUnit u in selectedUnits) middle += u.transform.position;
            Managers.GameContext.MapCamera?.LookAtGround(middle / selectedUnits.Count);
        }

        /// <summary>The strongest first: so they stand in the panel's cards.</summary>
        private void SortSelection()
        {
            selectedUnits.Sort((a, b) => Strength(b).CompareTo(Strength(a)));
        }

        private static int Strength(RtsUnit u)
        {
            if (u == null) return -1;
            if (u.IsHero) return 10;
            UnitProgress p = u.GetComponent<UnitProgress>();
            return p != null ? (int)p.Rank : 0;
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
                    ClickedUnit(hitUnit);
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

            }

            // Near a unit on the screen: it was meant (behind a tree's crown too)
            RtsUnit nearestUnit = UnitRegistry.NearestOnScreen(mousePos, mapCamera, ClickPixels * Screen.height / 1080f);
            if (nearestUnit != null)
            {
                ClickedUnit(nearestUnit);
                return;
            }

            if (!isShift) DeselectAll();
        }

        /// <summary>A click on a unit: that one; Shift: in or out of those chosen; Ctrl or a double click: all of its kind in view.</summary>
        private void ClickedUnit(RtsUnit unit)
        {
            bool isShift = UniversalInput.IsShiftHeld();
            bool twice = unit == lastClicked && Time.unscaledTime - lastClickAt < DoubleClickSeconds;
            lastClicked = unit;
            lastClickAt = Time.unscaledTime;
            if (twice || UniversalInput.IsCtrlHeld())
            {
                SelectKindOnScreen(unit, mapCamera, isShift);
                GameSound.Select(unit);
                return;
            }
            if (isShift && selectedUnits.Contains(unit)) { Deselect(unit); return; }
            if (!isShift) DeselectAll();
            SelectUnit(unit);
            if (selectedUnits.Contains(unit)) GameSound.Select(unit);
        }

        private void HandleBoxSelection(Vector3 screenStart, Vector3 screenEnd)
        {
            bool isShift = UniversalInput.IsShiftHeld();
            if (!isShift) DeselectAll();

            Rect selectionRect = GetScreenRect(screenStart, screenEnd);
            UnitRegistry.GetUnitsInScreenRect(selectionRect, mapCamera, tempUnitsBuffer);
            // The hero stands by its people: a frame round them does not walk it off with them, unless it is all that is there
            if (tempUnitsBuffer.Count > 1) tempUnitsBuffer.RemoveAll(u => u.IsHero);

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
            SortSelection();
            GameSound.Select(loudest);
        }

        public IReadOnlyList<RtsUnit> SelectedUnits => selectedUnits;

        /// <summary>The selected units walk to a ground point in formation (a right-click on the minimap).</summary>
        public void OrderMoveTo(Vector3 point)
        {
            OrderOnGround(Net.UnitOrderCommand.Kind.Move, null, point);
        }

        /// <summary>Those chosen stand where they are, and stop working (X, the "Стій" button).</summary>
        public void OrderStop()
        {
            if (selectedUnits.Count == 0) return;
            tempUnitsBuffer.Clear();
            tempUnitsBuffer.AddRange(selectedUnits);
            Net.CommandBus.Submit(Net.UnitOrderCommand.For(Net.UnitOrderCommand.Kind.Stop, tempUnitsBuffer, null, Vector3.zero));
            tempUnitsBuffer.Clear();
        }

        /// <summary>
        /// The right button: a click gives the order on letting go. If the mouse moved it was a drag of the map (RtsMapCameraController),
        /// and nobody goes anywhere. (The order used to go on pressing: dragging the map sent those chosen to where the drag began.)
        /// </summary>
        private void HandleMovementInput()
        {
            Vector3 mouse = UniversalInput.GetMousePosition();
            if (UniversalInput.GetMouseButtonDown(1))
            {
                rightArmed = selectedUnits.Count > 0 && !PointerOverUi();
                rightStart = mouse;
            }
            if (!UniversalInput.GetMouseButtonUp(1)) return;
            bool click = rightArmed && (mouse - rightStart).sqrMagnitude <= DRAG_THRESHOLD_SQR;
            rightArmed = false;
            if (!click || selectedUnits.Count == 0) return;

            Ray ray = mapCamera.ScreenPointToRay(mouse);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;
            bool plainClick = !UniversalInput.IsCtrlHeld() && !UniversalInput.IsShiftHeld();
            // On open ground under a tree's leaves: the tree (a trunk is hard to hit from above)
            ResourceNodeTree crown = plainClick && IsOpenGround(hit.collider) ? ResourceGrid.CrownUnderRay(ray, hit.point) : null;
            if (crown != null) { OrderOnGround(Net.UnitOrderCommand.Kind.Tree, crown, hit.point); return; }
            Net.UnitOrderCommand.Kind kind = KindFor(hit.collider, out Component what);
            OrderOnGround(kind, what, hit.point);
        }

        /// <summary>An order with the modifiers held (a move with Ctrl fights its way, with Shift goes after the others), and a ring where it goes.</summary>
        private void OrderOnGround(Net.UnitOrderCommand.Kind kind, Component what, Vector3 point)
        {
            if (kind == Net.UnitOrderCommand.Kind.Move)
            {
                if (UniversalInput.IsCtrlHeld()) kind = Net.UnitOrderCommand.Kind.AttackMove;
                else if (UniversalInput.IsShiftHeld()) kind = Net.UnitOrderCommand.Kind.QueueMove;
            }
            // A snapshot: a worker that enters a building leaves the selection while we loop
            tempUnitsBuffer.Clear();
            foreach (RtsUnit u in selectedUnits) if (u != null) tempUnitsBuffer.Add(u);
            if (tempUnitsBuffer.Count == 0) return;
            Net.CommandBus.Submit(Net.UnitOrderCommand.For(kind, tempUnitsBuffer, what, point));
            tempUnitsBuffer.Clear();
            Vector3 at = kind == Net.UnitOrderCommand.Kind.Attack && what != null ? what.transform.position : point;
            TeamMarkers.Flash(at, MarkOf(kind));
        }

        private static Color MarkOf(Net.UnitOrderCommand.Kind kind)
        {
            switch (kind)
            {
                case Net.UnitOrderCommand.Kind.Move: return TeamMarkers.MoveMark;
                case Net.UnitOrderCommand.Kind.QueueMove: return TeamMarkers.WayMark;
                case Net.UnitOrderCommand.Kind.AttackMove: return TeamMarkers.MarchMark;
                case Net.UnitOrderCommand.Kind.Attack: return TeamMarkers.AttackMark;
                default: return TeamMarkers.WorkMark;
            }
        }

        /// <summary>Nothing to give an order about here: no unit, building or resource.</summary>
        public static bool IsOpenGround(Collider c) =>
            c != null && c.GetComponentInParent<HealthComponent>() == null && c.GetComponentInParent<BuildingNode>() == null && c.GetComponentInParent<ResourceNodeTree>() == null
            && c.GetComponentInParent<OrePebbleNode>() == null && c.GetComponentInParent<StorageChestNode>() == null;

        /// <summary>What a right-click on this means: attack, work in a mine or at a workbench, gather, deliver, or move.</summary>
        public static Net.UnitOrderCommand.Kind KindFor(Collider target, out Component what)
        {
            var kind = Net.UnitOrderCommand.Kind.Move;
            what = null;
            HealthComponent enemy = target.GetComponentInParent<HealthComponent>();
            if (enemy != null && enemy.GetComponent<RtsUnit>() != null) enemy = null; // one's own people are walked up to, not struck
            if ((what = target.GetComponentInParent<WorkPlace>()) != null) kind = Net.UnitOrderCommand.Kind.Place;
            else if ((what = target.GetComponentInParent<ConstructionSite>()) != null) kind = Net.UnitOrderCommand.Kind.Site;
            else if (target.GetComponentInParent<WallSegment>() != null || target.GetComponentInParent<WallPier>() != null) kind = Net.UnitOrderCommand.Kind.Wall;
            else if (enemy != null && enemy.IsAlive && enemy.GetComponent<BuildingNode>() == null) { kind = Net.UnitOrderCommand.Kind.Attack; what = enemy; } // not an own building
            else if ((what = target.GetComponentInParent<MineNode>()) != null) kind = Net.UnitOrderCommand.Kind.Mine;
            else if ((what = target.GetComponentInParent<WorkbenchNode>()) != null) kind = Net.UnitOrderCommand.Kind.Bench;
            else if ((what = target.GetComponentInParent<ResourceNodeTree>()) != null) kind = Net.UnitOrderCommand.Kind.Tree;
            else if ((what = target.GetComponentInParent<OrePebbleNode>()) != null) kind = Net.UnitOrderCommand.Kind.Pebble;
            else if (target.GetComponentInParent<StorageChestNode>() != null) kind = Net.UnitOrderCommand.Kind.Chest;
            return kind;
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
