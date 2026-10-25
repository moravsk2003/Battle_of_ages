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
                        GameObject hall = BattleOfAges.Building.TownHall.CreatePrefab("CaptureHall");
                        hall.transform.position = hit.point + cam.transform.right * 14f;
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
                ("tooltip_workers", () => { Hover("Робітники"); return Wait(0.2f); }),
                ("rts_one_unit", () =>
                {
                    RtsUnit first = commander != null && commander.SelectedUnits.Count > 0 ? commander.SelectedUnits[0] : null;
                    if (first != null) commander.SelectOnly(first);
                    Hover("Single");
                    return Wait(0.3f);
                }),
                ("build_menu", () => { BuildMenuUI.Instance?.OpenMenu(); Hover("Btn_CastleWall"); return Wait(0.3f); }),
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
                ("hero", () =>
                {
                    EquipmentUI.Instance.Close();
                    var hero = UnityEngine.Object.FindFirstObjectByType<BattleOfAges.Combat.HeroProgress>();
                    if (hero != null) hero.AddXp(100 + 200 + 150);
                    HeroUI.Instance?.Open();
                    return Wait(0.3f);
                }),
                ("fp_abilities", () =>
                {
                    HeroUI.Instance?.Close();
                    var hero = UnityEngine.Object.FindFirstObjectByType<BattleOfAges.Combat.HeroProgress>();
                    if (hero != null)
                    {
                        hero.ChooseClass(BattleOfAges.Combat.HeroClass.Mage);
                        var skills = hero.GetComponent<BattleOfAges.Combat.HeroSkills>();
                        if (skills != null) { skills.LearnFireFan(); skills.LearnMeteor(); skills.UseActive(0, hero.transform.forward); }
                        var bag = hero.GetComponent<BattleOfAges.Combat.HeroArtifacts>();
                        if (bag != null) { bag.Add(BattleOfAges.Combat.ArtifactType.FullHeal); bag.Add(BattleOfAges.Combat.ArtifactType.Lightning); }
                        hero.GetComponent<BattleOfAges.Combat.HealthComponent>()?.TakeDamage(140);
                    }
                    SettlementAlarm.Raise();
                    if (hero != null) AttackAlerts.Report("робітник", hero.transform.position + new Vector3(40f, 0f, 25f), Time.unscaledTime);
                    return Wait(0.4f);
                }),
                ("rts_alert", () => { modes?.SetGameMode(GameMode.RtsMap); return Wait(0.6f); }),
                ("pause", () => { SettlementAlarm.StandDown(); modes?.SetGameMode(GameMode.FirstPerson); PauseMenuController.Instance?.PauseGame(); return Wait(0.3f); }),
                ("settings", () => { PauseMenuController.Instance?.OpenSettings(); return Wait(0.3f); }),
                ("help", () =>
                {
                    PauseMenuController.Instance?.CloseSettings();
                    PauseMenuController.Instance?.ResumeGame();
                    HelpUI.Instance?.Open();
                    return Wait(0.3f);
                }),
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
                // Higher sorting order in front, all within a few cm of the camera (far planes went behind the world)
                foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    c.renderMode = RenderMode.ScreenSpaceCamera;
                    c.worldCamera = capture.Camera;
                }
                var stack = new List<Canvas>();
                foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.isRootCanvas && c.worldCamera == capture.Camera) stack.Add(c);
                stack.Sort((a, b) => b.sortingOrder.CompareTo(a.sortingOrder));
                for (int k = 0; k < stack.Count; k++) stack[k].planeDistance = 0.2f + 0.004f * k;
                Canvas.ForceUpdateCanvases();
                Vector3 pos = main != null ? main.transform.position : Vector3.up * 30f;
                Vector3 target = main != null ? pos + main.transform.forward : Vector3.zero;
                capture.Shoot(shot++, steps[i].name, pos, target);
                yield return null;
            }
            Time.timeScale = 1f;
            capture.Finish();
        }

        /// <summary>Shows the tooltip of the named element at once (the cursor sits at the screen's corner in batch).</summary>
        private static void Hover(string name)
        {
            foreach (Tooltip t in UnityEngine.Object.FindObjectsByType<Tooltip>(FindObjectsSortMode.None))
                if (t.name == name && t.isActiveAndEnabled) { TooltipUI.Hover(t); TooltipUI.ShowNow(); return; }
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
