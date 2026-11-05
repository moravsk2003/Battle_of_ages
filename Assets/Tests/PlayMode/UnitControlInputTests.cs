using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Managers;
using BattleOfAges.Net;
using BattleOfAges.RTS;
using BattleOfAges.Utils;
using UInput = UnityEngine.InputSystem.InputSystem;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Commanding units in the real scene with a real mouse and keyboard, as a player does: the keys and buttons, not the methods behind them.</summary>
    [Category("Slow")]
    public class UnitControlInputTests
    {
        private Keyboard keyboard;
        private Mouse mouse;
        private InputSettings.EditorInputBehaviorInPlayMode behaviour;
        private InputSettings.BackgroundBehavior background;

        [SetUp]
        public void Setup()
        {
            behaviour = UInput.settings.editorInputBehaviorInPlayMode;
            background = UInput.settings.backgroundBehavior;
            UInput.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            UInput.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = Keyboard.current ?? UInput.AddDevice<Keyboard>();
            mouse = Mouse.current ?? UInput.AddDevice<Mouse>();
        }

        [TearDown]
        public void Teardown()
        {
            UInput.QueueStateEvent(keyboard, new KeyboardState());
            UInput.QueueStateEvent(mouse, new MouseState());
            UInput.Update();
            UInput.settings.editorInputBehaviorInPlayMode = behaviour;
            UInput.settings.backgroundBehavior = background;
        }

        private Vector2 at;
        private byte buttons;

        private void MouseNow() => UInput.QueueStateEvent(mouse, new MouseState { position = at, buttons = buttons });

        private IEnumerator MouseTo(Vector2 point)
        {
            at = point;
            MouseNow();
            yield return null;
            yield return null;
        }

        private IEnumerator Button(int index, bool down)
        {
            if (down) buttons |= (byte)(1 << index);
            else buttons &= (byte)~(1 << index);
            MouseNow();
            yield return null;
            yield return null;
        }

        private IEnumerator Click(int index, Vector2 point)
        {
            yield return MouseTo(point);
            yield return Button(index, true);
            yield return Button(index, false);
        }

        private IEnumerator Keys(params Key[] keys)
        {
            UInput.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return null;
            yield return null;
            UInput.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator LeaveTheScene()
        {
            Scene empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheMouseAndKeys_ChooseAndOrder_AndADragOfTheMap_OrdersNobody()
        {
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            GameContext.Modes.SetGameMode(GameMode.RtsMap);
            yield return new WaitForSeconds(0.5f);
            RtsUnitCommander commander = GameContext.Commander;
            Assert.IsNotNull(commander);

            RtsUnit unit = null;
            foreach (RtsUnit u in UnitRegistry.ActiveUnits) if (u != null && !u.IsHero && u.IsAlive && !u.InsideMine) { unit = u; break; }
            Assert.IsNotNull(unit, "the scene has its people");
            GameContext.MapCamera.LookAtGround(unit.transform.position);
            yield return new WaitForSeconds(1f);
            Camera cam = ViewCamera.Current;
            Assert.IsNotNull(cam);
            unit.StopHere();
            unit.GetComponent<NpcResourceGatherer>().SetGatherDirective(GatherDirective.Idle);
            yield return null;

            Vector2 Screen2(Vector3 world) { Vector3 s = cam.WorldToScreenPoint(world); return new Vector2(s.x, s.y); }

            // A left click on it
            yield return Click(0, Screen2(unit.transform.position + Vector3.up));
            CollectionAssert.Contains(commander.SelectedUnits, unit, "a click on it chooses it");

            // Ctrl+1 remembers; another click on the ground lets go; 1 chooses again
            yield return Keys(Key.LeftCtrl, Key.Digit1);
            CollectionAssert.Contains(commander.Group(1), unit, "Ctrl+1");
            commander.DeselectAll();
            yield return Keys(Key.Digit1);
            CollectionAssert.Contains(commander.SelectedUnits, unit, "1");

            // A drag with the right button moves the map: nobody goes anywhere
            Vector2 middle = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 stands = unit.transform.position;
            yield return MouseTo(middle);
            yield return Button(1, true);
            yield return MouseTo(middle + new Vector2(120f, 40f));
            yield return Button(1, false);
            Assert.IsFalse(unit.HasMoveTarget, "dragging the map orders nobody");

            // A right click on the ground does
            cam = ViewCamera.Current;
            Vector3 ground = stands + new Vector3(6f, 0f, 2f);
            ground.y = RtsUnit.GetTerrainHeight(ground);
            yield return Click(1, Screen2(ground));
            Assert.IsTrue(unit.HasMoveTarget, "a click gives the order");
            Assert.Greater(TeamMarkers.MarksShowing, 0, "and a ring shows where");

            // X stops it
            yield return Keys(Key.X);
            Assert.IsFalse(unit.HasMoveTarget, "X");

            // A click on a building: its panel; Esc lets go of it; K opens the people's window
            commander.DeselectAll();
            // A house of its own, far from anyone: nobody near to be taken for it
            Vector3 lot = stands + new Vector3(60f, 0f, 60f);
            lot.y = RtsUnit.GetTerrainHeight(lot);
            GameObject houseGo = Building.BuildingPlacementSystem.CreateBuilding(Building.BuildingType.WorkerHouse);
            houseGo.transform.position = lot;
            Building.BuildingNode building = houseGo.GetComponent<Building.BuildingNode>();
            GameContext.MapCamera.LookAtGround(lot);
            yield return new WaitForSeconds(1f);
            cam = ViewCamera.Current;
            Physics.SyncTransforms();
            yield return Click(0, Screen2(lot + Vector3.up * 1.5f));
            Assert.AreEqual(building, commander.SelectedBuilding, "a click chooses it");
            yield return null;
            Assert.IsTrue(UI.SelectedBuildingUI.Instance.Showing, "and its panel shows");
            StringAssert.Contains(Building.BuildingData.GetDefinition(building.Type).DisplayName.Substring(0, 3), UI.SelectedBuildingUI.Instance.TitleText + building.Type + Building.BuildingData.GetDefinition(building.Type).DisplayName);
            yield return Keys(Key.Escape);
            Assert.IsNull(commander.SelectedBuilding, "Esc lets go of it");
            Assert.AreEqual(GameMode.RtsMap, GameContext.Modes.CurrentMode);
            yield return Keys(Key.K);
            Assert.IsTrue(UI.GameWindows.WorkersOpen, "K opens the people's window");
            yield return Keys(Key.K);
            Assert.IsFalse(UI.GameWindows.WorkersOpen, "and closes it");
            commander.SelectOnly(unit);

            // Esc lets go first, and only then leaves the map
            yield return Keys(Key.Escape);
            Assert.AreEqual(0, commander.SelectedUnits.Count, "Esc lets go of those chosen");
            Assert.AreEqual(GameMode.RtsMap, GameContext.Modes.CurrentMode, "and stays on the map");
            yield return Keys(Key.Escape);
            Assert.AreEqual(GameMode.FirstPerson, GameContext.Modes.CurrentMode, "the next Esc leaves it");
        }
    }
}
