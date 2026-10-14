using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for the world objects (not a regular test): buildings, workbench, chest, mine, castle wall,
    /// trees, a boulder, ore pebbles and a raider camp built side by side on the plateau, plus the scene's training
    /// dummy, each with a knight for scale. CAPTURE_VIEWS = 1..14, CAPTURE_FROM, CAPTURE_LIST = "3,8" (view numbers),
    /// CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class ObjectCapture
    {
        [UnityTest]
        public IEnumerator CaptureObjects()
        {
            int count = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_VIEWS"), out int n) ? n : 4, 1, 14);
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "objects");

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.back;
            toSun.y = 0f;
            Quaternion facing = Quaternion.LookRotation(toSun.sqrMagnitude > 1e-4f ? toSun.normalized : Vector3.back);
            Vector3 fwd = facing * Vector3.forward, right = facing * Vector3.right;

            Vector3 Ground(Vector3 p) => new Vector3(p.x, InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z), p.z);
            Vector3 origin = new Vector3(20f, 0f, -34f);
            Vector3 Slot(float along, float back = 0f) => Ground(origin + right * along - fwd * back);

            var stage = new List<Vector3>();
            GameObject Place(GameObject go, Vector3 pos, float yaw = 0f)
            {
                go.transform.SetPositionAndRotation(pos, facing * Quaternion.Euler(0f, yaw, 0f));
                stage.Add(pos);
                return go;
            }

            GameObject house = Place(BuildingNode.CreateWorkerHousePrefab("Cap_House"), Slot(-24f));
            GameObject chest = Place(StorageChestNode.CreateStorageChestPrefab("Cap_Chest"), Slot(-15f));
            GameObject mine = Place(MineNode.CreateMinePrefab("Cap_Mine"), Slot(-8f));
            GameObject bench = Place(WorkbenchNode.CreateWorkbenchPrefab("Cap_Workbench"), Slot(0f));
            GameObject wall = Place(BuildingNode.CreateCastleWallPrefab("Cap_Wall"), Slot(9f));
            GameObject tree = Place(SteppeTreeBuilder.CreateSteppeTree("Cap_Tree", 1), Slot(20f));
            GameObject pine = Place(SteppeTreeBuilder.CreatePineTree("Cap_Pine", 2), Slot(28f));
            var pebbles = new List<GameObject>();
            for (int i = 0; i < 3; i++) pebbles.Add(Place(OrePebbleNode.CreatePebblePrefab("Cap_Pebble_" + i), Slot(-3f + i * 1.1f, -5f), i * 50f));

            // Boulder as the world scatter builds it
            GameObject boulder = new GameObject("Cap_Boulder");
            boulder.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshBuilder.CreateFacetedRockMesh(1.0f, 2, 77, 0.4f);
            boulder.AddComponent<MeshRenderer>().sharedMaterial = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.46f, 0.44f, 0.42f), 0.05f, 0.25f);
            boulder.transform.localScale = new Vector3(1.8f, 1.35f, 2.0f);
            Place(boulder, Slot(5f, -6f) + Vector3.down * 0.15f, 30f);

            Vector3 campCentre = Ground(origin - fwd * 26f);
            GameObject camp = HostileCampBuilder.CreateHostileCamp(campCentre, "Cap_Camp");
            foreach (var mob in camp.GetComponentsInChildren<BattleOfAges.AI.HostileMobAI>()) mob.enabled = false;
            stage.Add(campCentre);

            GameObject dummy = GameObject.Find("DummyTarget_1");
            GameObject campfire = GameObject.Find("Campfire");
            GameObject fortress = GameObject.Find("FortressWall_1");

            // Knights for scale
            var knights = new List<GameObject>();
            foreach (GameObject g in new[] { house, mine, bench, wall, tree })
            {
                GameObject k = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.KnightGuard, "Cap_Scale");
                k.transform.SetPositionAndRotation(Ground(g.transform.position + right * 3.2f + fwd * 1.5f), facing);
                knights.Add(k);
            }
            // CAPTURE_EQUATOR = "r,g,b": try another trilight horizon (ambient) colour for this capture only
            string eq = Environment.GetEnvironmentVariable("CAPTURE_EQUATOR");
            if (!string.IsNullOrEmpty(eq))
            {
                string[] c = eq.Split(',');
                if (c.Length == 3) RenderSettings.ambientEquatorColor = new Color(float.Parse(c[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture));
            }
            GrassFieldRenderer grassField = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            if (grassField != null) grassField.enabled = false;
            stage.Add(campCentre);
            ModelCapture.ClearStage(stage.ToArray(), 7f);
            yield return null;
            yield return null;
            ModelCapture.Pose(knights, "Locomotion", 0f);
            yield return null;

            Bounds B(GameObject g)
            {
                var b = new Bounds(g.transform.position, Vector3.one * 0.5f);
                foreach (Renderer r in g.GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
                return b;
            }
            (Vector3, Vector3) Look(GameObject g, float side, float dist, float height = 0.45f)
            {
                if (g == null) return (origin + Vector3.up * 50f, origin);
                Bounds b = B(g);
                float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
                Vector3 c = b.center;
                Vector3 dir = (fwd + right * side).normalized;
                return (c + dir * size * dist + Vector3.up * size * height, c);
            }

            var views = new List<(string, Func<(Vector3, Vector3)>)>
            {
                ("worker_house", () => Look(house, -0.6f, 1.25f)),
                ("storage_chest", () => Look(chest, -0.5f, 1.6f, 0.6f)),
                ("mine", () => Look(mine, -0.5f, 1.3f)),
                ("workbench", () => Look(bench, -0.5f, 1.4f, 0.6f)),
                ("castle_wall", () => Look(wall, -0.6f, 1.0f, 0.3f)),
                ("raider_camp", () => (campCentre + fwd * 14f - right * 6f + Vector3.up * 6f, campCentre + Vector3.up * 0.8f)),
                ("steppe_tree", () => Look(tree, -0.4f, 1.3f, 0.1f)),
                ("pine_tree", () => Look(pine, -0.4f, 1.3f, 0.1f)),
                ("boulder_and_ore", () => (Ground(origin + right * 2f + fwd * 4.5f) + Vector3.up * 2.2f, Ground(origin + right * 2f - fwd * 5.5f) + Vector3.up * 0.4f)),
                ("training_dummy", () => Look(dummy, 0.5f, 1.4f, 0.2f)),
                ("scene_campfire", () => Look(campfire, 0.4f, 1.6f, 0.7f)),
                ("scene_fortress_wall", () => Look(fortress, 0.5f, 1.0f, 0.3f)),
                ("lineup_overview", () => (origin + fwd * 30f + Vector3.up * 14f, Ground(origin) + Vector3.up * 2f)),
                ("house_detail", () => Look(house, 0.3f, 0.6f, 0.05f)),
            };

            // CAPTURE_FOCUS = house / chest / mine / workbench / wall / dummy: 14 views around that one object instead
            var focusObjects = new Dictionary<string, GameObject>
            {
                { "house", house }, { "chest", chest }, { "mine", mine }, { "workbench", bench }, { "wall", wall }, { "dummy", dummy }, { "camp", camp }
            };
            string focus = Environment.GetEnvironmentVariable("CAPTURE_FOCUS");
            if (!string.IsNullOrEmpty(focus) && focusObjects.TryGetValue(focus, out GameObject fo) && fo != null)
            {
                Bounds fb = B(fo);
                float fs = Mathf.Max(fb.size.x, fb.size.y, fb.size.z);
                Vector3 fc = fb.center, ff = fo.transform.forward, fr = fo.transform.right, fg = Ground(fo.transform.position);
                (Vector3, Vector3) Orbit(float yaw, float dist, float height, float lookY = 0.5f)
                {
                    Vector3 d = Quaternion.AngleAxis(yaw, Vector3.up) * ff;
                    return (fg + d * fs * dist + Vector3.up * fs * height, fg + Vector3.up * fb.size.y * lookY);
                }
                (Vector3, Vector3) Close(Vector3 localTarget, Vector3 localCam) =>
                    (fo.transform.TransformPoint(localCam), fo.transform.TransformPoint(localTarget));
                views = new List<(string, Func<(Vector3, Vector3)>)>
                {
                    ("front_three_quarter_left", () => Orbit(-35f, 1.5f, 0.55f)),
                    ("front", () => Orbit(0f, 1.6f, 0.4f)),
                    ("front_three_quarter_right", () => Orbit(40f, 1.5f, 0.55f)),
                    ("right_side", () => Orbit(90f, 1.6f, 0.35f)),
                    ("rear_three_quarter", () => Orbit(145f, 1.5f, 0.55f)),
                    ("rear", () => Orbit(180f, 1.6f, 0.4f)),
                    ("left_side", () => Orbit(-90f, 1.6f, 0.35f)),
                    ("low_angle", () => Orbit(-20f, 0.95f, 0.05f, 0.6f)),
                    ("top_down", () => Orbit(-30f, 0.9f, 1.6f, 0.2f)),
                    ("detail_front", () => Close(new Vector3(0f, fb.size.y * 0.3f, fb.extents.z * 0.8f), new Vector3(-fs * 0.22f, fb.size.y * 0.35f, fb.extents.z + fs * 0.35f))),
                    ("detail_left", () => Close(new Vector3(-fb.extents.x * 0.8f, fb.size.y * 0.35f, 0f), new Vector3(-fb.extents.x - fs * 0.35f, fb.size.y * 0.4f, fs * 0.15f))),
                    ("detail_top", () => Close(new Vector3(0f, fb.size.y * 0.75f, fb.extents.z * 0.5f), new Vector3(fs * 0.25f, fb.size.y * 0.9f, fb.extents.z + fs * 0.3f))),
                    ("with_knight", () => (fg + ff * fs * 1.3f + fr * fs * 0.9f + Vector3.up * 1.6f, fg + fr * 2.2f + Vector3.up * 1.2f)),
                    ("far", () => Orbit(-25f, 6f, 1.6f)),
                };
                ModelCapture.ClearStage(new[] { fg }, fs * 2.5f);
            }

            var picked = new List<int>();
            string list = Environment.GetEnvironmentVariable("CAPTURE_LIST");
            if (!string.IsNullOrEmpty(list))
                foreach (string part in list.Split(',')) if (int.TryParse(part, out int v) && v >= 1 && v <= views.Count) picked.Add(v - 1);
            if (picked.Count == 0)
            {
                int from = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_FROM"), out int f0) ? f0 : 0, 0, views.Count - 1);
                for (int i = from; i < Mathf.Min(from + count, views.Count); i++) picked.Add(i);
            }
            var capture = new CaptureRenderer(dir, picked.Count, 50f, drawGrass: false);
            for (int j = 0; j < picked.Count; j++)
            {
                (Vector3 pos, Vector3 target) = views[picked[j]].Item2();
                capture.Camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                SkinnedMeshLod.ViewerOverride = capture.Camera;
                SkinnedMeshLod.RefreshAll(capture.Camera);
                yield return null;
                capture.Shoot(j, views[picked[j]].Item1, pos, target);
                yield return null;
            }
            SkinnedMeshLod.ViewerOverride = null;
            capture.Finish();
        }
    }
}
