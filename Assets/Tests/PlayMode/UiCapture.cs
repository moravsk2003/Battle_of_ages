using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Crafting;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.UI;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for the game UI (not a regular test): the first-person HUD, the RTS view with and without a
    /// selection, the build, inventory, workbench, worker and pause windows. Screen-space overlay canvases are switched
    /// to the capture camera so they land in the screenshots. CAPTURE_LIST = "3,8" (view numbers), CAPTURE_DIR.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class UiCapture
    {
        [UnityTest]
        public IEnumerator CaptureUi()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "ui");

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);
            var res = BattleOfAges.Resources.ResourceManager.Instance;
            if (res != null)
            {
                res.AddResource(BattleOfAges.Resources.ResourceType.Wood, 37);
                res.AddOre(1, 12);
                res.AddOre(2, 4);
            }

            GameModeManager modes = UnityEngine.Object.FindFirstObjectByType<GameModeManager>();
            RtsUnitCommander commander = UnityEngine.Object.FindFirstObjectByType<RtsUnitCommander>();
            MethodInfo select = typeof(RtsUnitCommander).GetMethod("SelectUnit", BindingFlags.Instance | BindingFlags.NonPublic);

            var steps = new List<(string name, Func<IEnumerator> setup)>
            {
                ("fp_hud", () => Wait(0.3f)),
                ("rts_overview", () => { modes?.SetGameMode(GameMode.RtsMap); return Wait(0.6f); }),
                ("mine_label", () =>
                {
                    Camera cam = BattleOfAges.Utils.ViewCamera.Current;
                    if (cam != null && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.42f, 0.42f, 0f)), out RaycastHit hit, 500f))
                    {
                        GameObject m = BattleOfAges.Building.MineNode.CreateMinePrefab("CaptureMine");
                        m.transform.position = hit.point;
                        var node = m.GetComponent<BattleOfAges.Building.MineNode>();
                        for (int k = 0; k < 4; k++) node.AssignWorker();
                        node.Produce(37);
                    }
                    return Wait(0.4f);
                }),
                ("rts_selection", () =>
                {
                    int n = 0;
                    foreach (RtsUnit u in UnityEngine.Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None))
                        if (n < 4 && u.name != "Player") { select?.Invoke(commander, new object[] { u }); n++; }
                    return Wait(0.3f);
                }),
                ("build_menu", () => { BuildMenuUI.Instance?.OpenMenu(); return Wait(0.3f); }),
                ("worker_orders", () => { BuildMenuUI.Instance?.CloseMenu(); NpcGatherCommandUI.Instance?.OpenMenu(); return Wait(0.3f); }),
                ("inventory", () =>
                {
                    NpcGatherCommandUI.Instance?.CloseMenu();
                    commander?.DeselectAll();
                    modes?.SetGameMode(GameMode.FirstPerson);
                    InventoryUI.Instance?.OpenInventory();
                    return Wait(0.4f);
                }),
                ("workbench", () =>
                {
                    InventoryUI.Instance?.CloseInventory();
                    WorkbenchNode bench = UnityEngine.Object.FindFirstObjectByType<WorkbenchNode>();
                    if (bench != null) WorkbenchUI.EnsureInstance().OpenUI(bench);
                    return Wait(0.3f);
                }),
                ("equipment", () =>
                {
                    WorkbenchUI.Instance?.CloseUI();
                    foreach (var it in new[] { new Item(ItemType.Sword, 3), new Item(ItemType.Sword, 3), new Item(ItemType.Bow, 2), new Item(ItemType.Axe, 1), new Item(ItemType.Helmet, 4),
                                               new Item(ItemType.Armor, 2), new Item(ItemType.Gloves, 5), new Item(ItemType.Boots, 1), new Item(ItemType.Shield, 3), new Item(ItemType.Pickaxe, 2) })
                        Armory.Instance.Add(it);
                    EquipmentUI.Instance.OpenDefault();
                    return Wait(0.3f);
                }),
                ("pause", () => { EquipmentUI.Instance.Close(); PauseMenuController.Instance?.PauseGame(); return Wait(0.3f); }),
                ("settings", () => { PauseMenuController.Instance?.OpenSettings(); return Wait(0.3f); }),
            };

            var picked = new List<int>();
            string list = Environment.GetEnvironmentVariable("CAPTURE_LIST");
            if (!string.IsNullOrEmpty(list))
                foreach (string part in list.Split(',')) if (int.TryParse(part, out int v) && v >= 1 && v <= steps.Count) picked.Add(v - 1);
            var capture = new CaptureRenderer(dir, steps.Count, 60f, drawGrass: true);
            int shot = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                yield return steps[i].setup();
                if (picked.Count > 0 && !picked.Contains(i)) continue;
                Camera main = BattleOfAges.Utils.ViewCamera.Current;
                if (main != null)
                {
                    capture.Camera.fieldOfView = main.fieldOfView;
                    capture.Camera.orthographic = main.orthographic;
                    capture.Camera.orthographicSize = main.orthographicSize;
                }
                foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = capture.Camera;
                    c.planeDistance = 0.2f + 0.01f * c.sortingOrder;
                }
                Canvas.ForceUpdateCanvases();
                Vector3 pos = main != null ? main.transform.position : Vector3.up * 30f;
                Vector3 target = main != null ? pos + main.transform.forward : Vector3.zero;
                capture.Shoot(shot++, steps[i].name, pos, target);
                yield return null;
            }
            Time.timeScale = 1f;
            capture.Finish();
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
