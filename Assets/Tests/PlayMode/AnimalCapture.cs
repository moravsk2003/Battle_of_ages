using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for one animal model (not a regular test): 14 views around a single specimen that faces the
    /// sun, plus a gait strip, a grazing pose, a group and a scale comparison. CAPTURE_ANIMAL = Boar / Deer / Rabbit,
    /// CAPTURE_VIEWS = 1..14 (priority order), CAPTURE_FROM = first view, CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class AnimalCapture
    {
        [UnityTest]
        public IEnumerator CaptureAnimal()
        {
            int count = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_VIEWS"), out int n) ? n : 4, 1, 14);
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "animal");
            AnimalType type = Enum.TryParse(Environment.GetEnvironmentVariable("CAPTURE_ANIMAL"), out AnimalType t) ? t : AnimalType.Boar;

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            // Specimens face the sun so the front and three-quarter views are lit.
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.back;
            toSun.y = 0f;
            Quaternion facing = Quaternion.LookRotation(toSun.sqrMagnitude > 1e-4f ? toSun.normalized : Vector3.back);

            Vector3 Ground(float x, float z) => new Vector3(x, InfiniteWorldManager.GetSampledTerrainHeight(x, z), z);
            Vector3 main = Ground(-14f, -30f), gait = Ground(-14f, -44f), graze = Ground(-2f, -30f), group = Ground(10f, -30f), scale = Ground(10f, -44f);
            ModelCapture.ClearStage(new[] { main, gait, graze, group, scale }, 10f);
            GrassFieldRenderer grassField = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            if (grassField != null) grassField.enabled = false;

            PeacefulAnimalAI Make(Vector3 pos, Quaternion rot, string name)
            {
                GameObject a = AnimalModelBuilder.CreateAnimalPrefab(type, name);
                PeacefulAnimalAI ai = a.GetComponent<PeacefulAnimalAI>();
                ai.enabled = false;
                a.transform.SetPositionAndRotation(pos, rot);
                return ai;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            PeacefulAnimalAI specimen = Make(main, facing, "Specimen");
            Debug.Log($"[Capture] first {type} built in {timer.ElapsedMilliseconds} ms");
            timer.Restart();
            PeacefulAnimalAI grazer = Make(graze, facing, "Grazer");
            Debug.Log($"[Capture] second {type} built in {timer.ElapsedMilliseconds} ms");

            Quaternion side = Quaternion.LookRotation(Vector3.right);
            var gaitRow = new List<PeacefulAnimalAI>();
            for (int i = 0; i < 4; i++) gaitRow.Add(Make(gait + new Vector3((i - 1.5f) * 2.4f, 0f, 0f), side, "Gait_" + i));
            var herd = new List<PeacefulAnimalAI>();
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = group + facing * new Vector3((i - 1) * 1.8f, 0f, (i == 1 ? 0.8f : 0f));
                p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
                herd.Add(Make(p, facing * Quaternion.Euler(0f, (i - 1) * 35f, 0f), "Herd_" + i));
            }
            PeacefulAnimalAI scaled = Make(scale, side, "ScaleAnimal");
            GameObject knight = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.KnightGuard, "ScaleKnight");
            knight.transform.SetPositionAndRotation(scale + new Vector3(-1.6f, 0f, 0.4f), Quaternion.LookRotation(Vector3.right));
            yield return null;
            yield return null;

            specimen.PoseGait(0f, 0f);
            grazer.PoseGait(0f, 0f);
            Transform neck = grazer.transform.Find("VisualModel/NeckPivot");
            if (neck != null) neck.localRotation *= Quaternion.Euler(type == AnimalType.Deer ? 58f : 26f, 0f, 0f);
            for (int i = 0; i < gaitRow.Count; i++) gaitRow[i].PoseGait(i * 0.25f, 1f);
            for (int i = 0; i < herd.Count; i++) herd[i].PoseGait(0.15f + i * 0.3f, i == 1 ? 0f : 1f);
            scaled.PoseGait(0.1f, 1f);
            Animator knightAnim = knight.GetComponentInChildren<Animator>();
            if (knightAnim != null) { knightAnim.Update(0f); knightAnim.speed = 0f; }
            yield return null;

            // Size of the specimen: camera distances scale with it (views were framed for a ~1.7 m boar).
            Bounds b = new Bounds(main + Vector3.up * 0.5f, Vector3.zero);
            foreach (Renderer r in specimen.GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
            float s = Mathf.Max(b.size.x, b.size.z) / 1.7f;
            float hgt = b.size.y;
            Transform spT = specimen.transform;
            Transform head = spT.Find("VisualModel/NeckPivot/Head");
            Vector3 headPos = head != null ? head.position + spT.forward * 0.12f * s : b.center;
            Vector3 L(float x, float y, float z) => spT.TransformPoint(new Vector3(x * s, y * s, z * s));
            Vector3 centre = spT.TransformPoint(new Vector3(0f, hgt * 0.5f, 0.1f * s));
            Transform gzT = grazer.transform;
            Vector3 G(float x, float y, float z) => gzT.TransformPoint(new Vector3(x * s, y * s, z * s));

            var views = new List<(string, Vector3, Vector3)>
            {
                ("front_three_quarter", L(-1.5f, 0.95f, 1.9f), centre),
                ("side_left", L(-2.9f, 0.75f, 0.1f), centre),
                ("head_closeup", headPos + spT.TransformVector(new Vector3(-0.65f, 0.18f, 0.75f) * s), headPos),
                ("rear_three_quarter", L(1.7f, 1.1f, -2.0f), centre),
                ("gait_cycle", gait + new Vector3(0f, 1.0f * s, -6.0f * s), gait + Vector3.up * 0.5f * s),
                ("front", L(0f, 0.75f, 2.6f), centre),
                ("head_profile", headPos + spT.TransformVector(new Vector3(-1.15f, 0.05f, 0.05f) * s), headPos),
                ("legs_hooves", L(-0.75f, 0.2f, 0.85f), L(-0.1f, 0.12f, 0.36f)),
                ("top", L(-0.7f, 3.0f, -0.5f), centre),
                ("rear", L(0f, 0.85f, -2.7f), centre),
                ("low_angle", L(-1.3f, 0.18f, 1.8f), L(0f, 0.75f, 0.2f)),
                ("grazing", G(-1.6f, 0.8f, 1.6f), G(0f, 0.4f, 0.4f)),
                ("group_mid_distance", group + facing * new Vector3(-2.5f, 2.2f, 8.5f), group + Vector3.up * 0.5f),
                ("scale_with_knight", scale + new Vector3(-0.6f, 1.4f, -5.2f), scale + new Vector3(-0.6f, 0.8f, 0f)),
            };

            // CAPTURE_LIST = "3,8,10" picks views by number; otherwise CAPTURE_FROM + CAPTURE_VIEWS.
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
                int i = picked[j];
                capture.Shoot(j, views[i].Item1, views[i].Item2, views[i].Item3);
                yield return null;
            }
            capture.Finish();
        }
    }
}
