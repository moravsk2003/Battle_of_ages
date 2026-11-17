using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review of the Blender models (docs/MODELS.md §6, not a regular test): every ImportedModel in
    /// Resources/Models (or CAPTURE_MODELS = "Weapons/Kerambit,Props/Barrel") spawned on the plateau facing the light, a
    /// knight beside it for scale; hand-sized ones float at hand height. Per model: front, three-quarter, side, back,
    /// top, a close-up and a far shot (the distant mesh); then the line-up. CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class ImportedModelCapture
    {
        [TearDown]
        public void GpuBackOn() => GpuSkinnedCharacter.Enabled = true;

        [UnityTest]
        public IEnumerator CaptureImportedModels()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "imported");

            var models = new List<ImportedModel>();
            string only = Environment.GetEnvironmentVariable("CAPTURE_MODELS");
            if (!string.IsNullOrEmpty(only))
            {
                foreach (string key in only.Split(','))
                {
                    string[] p = key.Trim().Split('/');
                    ImportedModel m = p.Length == 2 ? ImportedModel.Load(p[0], p[1]) : null;
                    if (m == null) Debug.LogWarning($"[ImportedModelCapture] No model {key} in Resources/{ImportedModel.ResourcesFolder}");
                    else models.Add(m);
                }
            }
            else models.AddRange(UnityEngine.Resources.LoadAll<ImportedModel>(ImportedModel.ResourcesFolder));
            Assert.IsNotEmpty(models, "no converted models (Battle of Ages/Models/Convert Blender Models)");

            GpuSkinnedCharacter.Enabled = false; // the knight for scale drawn near, with its own mesh
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

            // In a row, each with the room its size needs
            var placed = new List<(ImportedModel model, GameObject go, Vector3 centre, float size)>();
            var stage = new List<Vector3>();
            float along = 0f;
            foreach (ImportedModel m in models)
            {
                float size = Mathf.Max(m.Bounds.size.x, m.Bounds.size.y, m.Bounds.size.z, 0.05f);
                float room = Mathf.Max(size * 2f, 4.4f); // the knight stands out of the close views
                Vector3 spot = Ground(origin + right * (along + room * 0.5f));
                along += room;
                bool handSized = size < 1.2f;
                Vector3 at = spot + Vector3.up * (handSized ? 1.1f - m.Bounds.center.y : 0f);
                GameObject go = m.Spawn(null, "Cap_" + m.name);
                go.transform.SetPositionAndRotation(at, facing);
                GameObject knight = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.KnightGuard, "Cap_Scale");
                knight.transform.SetPositionAndRotation(Ground(spot + right * (room * 0.5f - 0.3f) - fwd * 0.5f), facing);
                placed.Add((m, go, go.transform.TransformPoint(m.Bounds.center), size));
                stage.Add(spot);
            }
            ModelCapture.ClearStage(stage.ToArray(), Mathf.Max(along, 6f));

            var views = new List<(string, Func<(Vector3, Vector3)>)>();
            foreach (var (m, go, centre, size) in placed)
            {
                Transform t = go.transform;
                float d = Mathf.Max(size * 1.4f, 0.35f);
                (Vector3, Vector3) Orbit(float yaw, float height, float dist) =>
                    (centre + Quaternion.AngleAxis(yaw, Vector3.up) * t.forward * dist + Vector3.up * height * dist, centre);
                string n = m.Category + "_" + m.name;
                views.Add(($"{n}_front", () => Orbit(0f, 0.15f, d)));
                views.Add(($"{n}_three_quarter", () => Orbit(-40f, 0.35f, d)));
                views.Add(($"{n}_right_side", () => Orbit(90f, 0.1f, d)));
                views.Add(($"{n}_back", () => Orbit(180f, 0.2f, d)));
                views.Add(($"{n}_top", () => (centre + Vector3.up * d * 1.1f + t.forward * d * 0.15f, centre)));
                views.Add(($"{n}_close", () => Orbit(-25f, 0.2f, d * 0.45f)));
                views.Add(($"{n}_far_lod", () => Orbit(-20f, 0.25f, Mathf.Max(size * 40f, 25f))));
            }
            views.Add(("lineup", () => (Ground(origin + right * along * 0.5f + fwd * Mathf.Max(along * 0.7f, 5f)) + Vector3.up * 2.2f,
                                         Ground(origin + right * along * 0.5f) + Vector3.up * 1f)));

            var capture = new CaptureRenderer(dir, views.Count, 40f);
            for (int j = 0; j < views.Count; j++)
            {
                (Vector3 pos, Vector3 target) = views[j].Item2();
                capture.Camera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                SkinnedMeshLod.ViewerOverride = capture.Camera;
                SkinnedMeshLod.RefreshAll(capture.Camera);
                yield return null;
                capture.Shoot(j, views[j].Item1, pos, target);
                yield return null;
            }
            SkinnedMeshLod.ViewerOverride = null;
            capture.Finish();
        }
    }
}
