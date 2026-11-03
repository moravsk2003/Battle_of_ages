using UnityEngine;
using UnityEngine.EventSystems;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The mouse cursor says what a right-click will do (docs/GAME_DESIGN.md §21, stage 2.6): over an enemy with units
    /// selected — crossed swords (attack), over a tree, ore or a mine — the resource (gather), while placing a building —
    /// the hammer; otherwise the gold arrow, which is also the cursor in every window.
    /// </summary>
    public sealed class MapCursor : MonoBehaviour
    {
        public enum Look { Arrow, Attack, Wood, Ore, Build }

        public static Look Current { get; private set; } = (Look)(-1);
        private GameModeManager modes;
        private RtsUnitCommander commander;
        private float timer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<MapCursor>() != null) return;
            var go = new GameObject("MapCursor");
            DontDestroyOnLoad(go);
            go.AddComponent<MapCursor>();
        }

        private void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.05f;
            if (modes == null) modes = Managers.GameContext.Modes;
            if (commander == null) commander = Managers.GameContext.Commander;
            Set(Decide());
        }

        private Look Decide()
        {
            bool map = modes != null && modes.CurrentMode == GameMode.RtsMap;
            if (!map || GameWindows.BlockGameplay) return Look.Arrow;
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            if (placer != null && placer.IsPlacing) return Look.Build;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return Look.Arrow;
            if (commander == null || commander.SelectedUnits.Count == 0) return Look.Arrow;
            Camera cam = ViewCamera.Current;
            if (cam == null || !Physics.Raycast(cam.ScreenPointToRay(InputSystem.UniversalInput.GetMousePosition()), out RaycastHit hit, 1000f)) return Look.Arrow;
            Look look = LookAt(hit.collider);
            if (look == Look.Arrow && RtsUnitCommander.IsOpenGround(hit.collider) && ResourceGrid.CrownUnderRay(cam.ScreenPointToRay(InputSystem.UniversalInput.GetMousePosition()), hit.point) != null) return Look.Wood;
            return look;
        }

        /// <summary>What the cursor shows over this collider, with units selected.</summary>
        public static Look LookAt(Collider c)
        {
            if (c == null) return Look.Arrow;
            if (c.GetComponentInParent<HostileMobAI>() != null) return Look.Attack;
            CombatUnit cu = c.GetComponentInParent<CombatUnit>();
            if (cu != null && cu.Team != UnitSpatialGrid.TeamPlayer && cu.Team != UnitSpatialGrid.TeamNeutral) return Look.Attack;
            if (c.GetComponentInParent<PeacefulAnimalAI>() != null) return Look.Attack;
            if (c.GetComponentInParent<ConstructionSite>() != null || c.GetComponentInParent<WorkPlace>() != null) return Look.Build; // send them to work here
            if (c.GetComponentInParent<ResourceNodeTree>() != null) return Look.Wood;
            if (c.GetComponentInParent<OrePebbleNode>() != null || c.GetComponentInParent<MineNode>() != null) return Look.Ore;
            return Look.Arrow;
        }

        private static void Set(Look look)
        {
            if (look == Current) return;
            Current = look;
            if (Application.isBatchMode) return;
            switch (look)
            {
                case Look.Attack: Cursor.SetCursor(UiTheme.CursorTexture(UiTheme.Icon.Army), new Vector2(16f, 16f), CursorMode.Auto); break;
                case Look.Wood: Cursor.SetCursor(UiTheme.CursorTexture(UiTheme.Icon.Wood), new Vector2(16f, 16f), CursorMode.Auto); break;
                case Look.Ore: Cursor.SetCursor(UiTheme.CursorTexture(UiTheme.Icon.Ore), new Vector2(16f, 16f), CursorMode.Auto); break;
                case Look.Build: Cursor.SetCursor(UiTheme.CursorTexture(UiTheme.Icon.Hammer), new Vector2(16f, 16f), CursorMode.Auto); break;
                default: Cursor.SetCursor(UiTheme.CursorTexture(null), new Vector2(2f, 2f), CursorMode.Auto); break;
            }
        }
    }
}
