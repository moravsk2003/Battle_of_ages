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
    /// Visual review tool for one armoured character role (not a regular test): 14 views around a standing specimen
    /// that faces the sun (helmet, shield, sword close-ups), an attack and a walk strip, a squad and a battle line at
    /// distance. CAPTURE_ROLE = KnightGuard / HeroPlayer / Warlord, CAPTURE_VIEWS = 1..14 (priority order),
    /// CAPTURE_FROM = first view, CAPTURE_LIST = "3,8,10" (view numbers), CAPTURE_LOD = 1 (distant meshes up close),
    /// CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class KnightCapture
    {
        [UnityTest]
        public IEnumerator CaptureKnight()
        {
            int count = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_VIEWS"), out int n) ? n : 4, 1, 14);
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "knight");
            HumanoidModelBuilder.HumanType role = Enum.TryParse(Environment.GetEnvironmentVariable("CAPTURE_ROLE"), out HumanoidModelBuilder.HumanType r)
                ? r : HumanoidModelBuilder.HumanType.KnightGuard;

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.back;
            toSun.y = 0f;
            Quaternion facing = Quaternion.LookRotation(toSun.sqrMagnitude > 1e-4f ? toSun.normalized : Vector3.back);

            Vector3 Ground(float x, float z) => new Vector3(x, InfiniteWorldManager.GetSampledTerrainHeight(x, z), z);
            Vector3 main = Ground(18f, -24f), attack = Ground(30f, -24f), walk = Ground(30f, -38f), squad = Ground(6f, -30f), line = Ground(-20f, -40f);
            ModelCapture.ClearStage(new[] { main, attack, walk, squad }, 10f);
            ModelCapture.ClearStage(new[] { line }, 22f);
            GrassFieldRenderer grassField = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            if (grassField != null) grassField.enabled = false;

            GameObject Make(Vector3 pos, Quaternion rot, string name)
            {
                pos.y = InfiniteWorldManager.GetSampledTerrainHeight(pos.x, pos.z);
                GameObject go = HumanoidModelBuilder.CreateHumanoidModel(role, name);
                go.transform.SetPositionAndRotation(pos, rot);
                return go;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            GameObject specimen = Make(main, facing, "Specimen");
            Debug.Log($"[Capture] first {role} built in {timer.ElapsedMilliseconds} ms");
            foreach (SkinnedMeshRenderer smr in specimen.GetComponentsInChildren<SkinnedMeshRenderer>())
                Debug.Log($"[Capture] {smr.name}: {smr.sharedMesh.vertexCount} verts, {smr.sharedMesh.triangles.Length / 3} tris, {smr.sharedMaterials.Length} materials");

            Quaternion side = Quaternion.LookRotation(Vector3.right);
            var attackers = new List<GameObject>();
            var walkers = new List<GameObject>();
            for (int i = 0; i < 4; i++)
            {
                attackers.Add(Make(attack + new Vector3((i - 1.5f) * 1.6f, 0f, 0f), facing, "Attack_" + i));
                walkers.Add(Make(walk + new Vector3((i - 1.5f) * 1.6f, 0f, 0f), side, "Walk_" + i));
            }
            var squadRow = new List<GameObject>();
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = squad + facing * new Vector3((i % 3 - 1) * 1.5f, 0f, -(i / 3) * 1.6f);
                squadRow.Add(Make(p, facing * Quaternion.Euler(0f, (i % 3 - 1) * 8f, 0f), "Squad_" + i));
            }
            var lineRow = new List<GameObject>();
            for (int i = 0; i < 40; i++)
            {
                Vector3 p = line + facing * new Vector3((i % 10 - 4.5f) * 1.5f, 0f, -(i / 10) * 1.6f);
                lineRow.Add(Make(p, facing, "Line_" + i));
            }
            yield return null;
            yield return null; // Start() of the animation drivers has run

            ModelCapture.Pose(new List<GameObject> { specimen }, "Locomotion", 0f);
            ModelCapture.Pose(attackers, "Attack", 0f);
            ModelCapture.Pose(walkers, "Locomotion", 1.4f);
            ModelCapture.Pose(squadRow, "Locomotion", 0f, 0.1f);
            ModelCapture.Pose(lineRow, "Locomotion", 1.4f, 0.05f);
            yield return null;

            Transform sp = specimen.transform;
            Transform Bone(string name)
            {
                foreach (Transform t in specimen.GetComponentsInChildren<Transform>()) if (t.name == name) return t;
                return null;
            }
            Transform head = Bone("Head_end") ?? Bone("Head");
            Vector3 headPos = head != null ? head.position - Vector3.up * 0.14f : sp.TransformPoint(0f, 1.62f, 0f);
            Transform forearm = Bone("LowerArm.L"), palmL = Bone("Palm.L"), palmR = Bone("Palm.R");
            Vector3 shieldPos = forearm != null && palmL != null ? Vector3.Lerp(forearm.position, palmL.position, 0.45f) : sp.TransformPoint(-0.35f, 1.0f, 0.1f);
            Vector3 swordPos = palmR != null ? palmR.position : sp.TransformPoint(0.35f, 0.9f, 0.1f);
            Vector3 L(float x, float y, float z) => sp.TransformPoint(new Vector3(x, y, z));
            Vector3 V(float x, float y, float z) => sp.TransformVector(new Vector3(x, y, z));
            Vector3 centre = L(0f, 1.05f, 0f);

            var views = new List<(string, Vector3, Vector3)>
            {
                ("front_three_quarter", L(-1.3f, 1.55f, 2.3f), centre),
                ("helmet_closeup", headPos + V(-0.4f, 0.12f, 0.62f), headPos),
                ("side_left", L(-3.0f, 1.3f, 0.1f), centre),
                ("rear_three_quarter", L(1.6f, 1.6f, -2.3f), centre),
                ("attack_frames", attack + facing * new Vector3(0f, 1.3f, 6.5f), attack + Vector3.up * 0.95f),
                ("front", L(0f, 1.3f, 2.9f), centre),
                ("helmet_profile", headPos + V(-0.8f, 0.04f, 0.04f), headPos),
                ("shield_closeup", shieldPos + V(-0.75f, 0.2f, 0.45f), shieldPos),
                ("sword_closeup", swordPos + V(0.7f, 0.25f, 0.55f), swordPos + V(0f, 0.15f, 0f)),
                ("walk_cycle_side", walk + new Vector3(0f, 1.2f, -6.5f), walk + Vector3.up * 0.9f),
                ("rear", L(0f, 1.4f, -2.9f), centre),
                ("low_angle", L(-0.9f, 0.35f, 1.8f), L(0f, 1.35f, 0f)),
                ("squad_mid_distance", squad + facing * new Vector3(-3f, 2.6f, 9f), squad + Vector3.up * 0.9f - facing * Vector3.forward * 1.6f),
                ("battle_line_far", line + facing * new Vector3(-6f, 5f, 30f), line + Vector3.up * 0.8f - facing * Vector3.forward * 2.4f),
            };

            var picked = new List<int>();
            string list = Environment.GetEnvironmentVariable("CAPTURE_LIST");
            if (!string.IsNullOrEmpty(list))
                foreach (string part in list.Split(',')) if (int.TryParse(part, out int v) && v >= 1 && v <= views.Count) picked.Add(v - 1);
            if (picked.Count == 0)
            {
                int from = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_FROM"), out int f0) ? f0 : 0, 0, views.Count - 1);
                for (int i = from; i < Mathf.Min(from + count, views.Count); i++) picked.Add(i);
            }
            // CAPTURE_LOD = 1 shows the distant meshes up close (to check them)
            if (Environment.GetEnvironmentVariable("CAPTURE_LOD") == "1")
                foreach (SkinnedMeshLod lod in UnityEngine.Object.FindObjectsByType<SkinnedMeshLod>(FindObjectsSortMode.None)) lod.NearScreenHeight = 1000f;
            var capture = new CaptureRenderer(dir, picked.Count, 50f, drawGrass: false);
            for (int j = 0; j < picked.Count; j++)
            {
                int i = picked[j];
                // Let the crowd LOD pick its meshes for this viewpoint (and skin them) before the shot
                capture.Camera.transform.position = views[i].Item2;
                capture.Camera.transform.LookAt(views[i].Item3);
                SkinnedMeshLod.ViewerOverride = capture.Camera;
                SkinnedMeshLod.RefreshAll(capture.Camera);
                yield return null;
                capture.Shoot(j, views[i].Item1, views[i].Item2, views[i].Item3);
                yield return null;
            }
            SkinnedMeshLod.ViewerOverride = null;
            capture.Finish();
        }
    }
}
