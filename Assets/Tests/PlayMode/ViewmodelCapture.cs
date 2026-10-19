using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Player;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for the first-person arms (not a regular test): the player's view through the sword swing
    /// (idle, wind-up, strike, follow-through, recovery) and close-ups of the hands, sword and shield from outside.
    /// CAPTURE_VIEWS = 1..14 (priority order), CAPTURE_LIST = "3,8,10" (view numbers), CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class ViewmodelCapture
    {
        [UnityTest]
        public IEnumerator CaptureViewmodel()
        {
            int count = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_VIEWS"), out int n) ? n : 4, 1, 14);
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "viewmodel");

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);

            FpsViewmodelAnimation arms = UnityEngine.Object.FindFirstObjectByType<FpsViewmodelAnimation>();
            Assert.IsNotNull(arms, "No first-person arms in the scene");
            arms.enabled = false; // no sway / bob: frozen poses
            Transform root = arms.transform;
            Transform sword = FindDeep(root, "Sword"), shield = FindDeep(root, "Shield");
            Camera main = Camera.main;

            // Phase (< 0 = rest) per view; external views look at the arms from outside the player's eyes.
            Vector3 R(Vector3 local) => root.TransformPoint(local);
            Vector3 Hand() => sword.position;
            Vector3 Blade(float along) => sword.TransformPoint(new Vector3(0f, along, 0f));
            var views = new List<(string name, float phase, Func<(Vector3, Vector3)> cam)>
            {
                ("fp_idle", -1f, () => (main.transform.position, main.transform.position + main.transform.forward)),
                ("fp_strike", 0.42f, () => (main.transform.position, main.transform.position + main.transform.forward)),
                ("right_hand_closeup", -1f, () => (Hand() + root.TransformVector(new Vector3(-0.16f, 0.06f, 0.12f)), Hand())),
                ("fp_windup", 0.24f, () => (main.transform.position, main.transform.position + main.transform.forward)),
                ("shield_back", -1f, () => (shield.position + root.TransformVector(new Vector3(0.10f, 0.12f, -0.30f)), shield.position)),
                ("sword_side", -1f, () => (Blade(0.40f) + root.TransformVector(new Vector3(0.55f, 0.05f, 0.10f)), Blade(0.40f))),
                ("fp_follow_through", 0.56f, () => (main.transform.position, main.transform.position + main.transform.forward)),
                ("right_hand_underside", -1f, () => (Hand() + root.TransformVector(new Vector3(0.10f, -0.16f, 0.08f)), Hand())),
                ("left_hand_grip", -1f, () => (shield.position + root.TransformVector(new Vector3(0.24f, -0.02f, -0.14f)), shield.position)),
                ("arms_overview", -1f, () => (R(new Vector3(0.75f, 0.25f, 0.55f)), R(new Vector3(-0.02f, -0.25f, 0.55f)))),
                ("fp_recovery", 0.8f, () => (main.transform.position, main.transform.position + main.transform.forward)),
                ("blade_closeup", -1f, () => (Blade(0.62f) + root.TransformVector(new Vector3(0.10f, -0.02f, -0.14f)), Blade(0.55f))),
                ("hilt_closeup", -1f, () => (Blade(0.06f) + root.TransformVector(new Vector3(0.14f, 0.08f, -0.10f)), Blade(0.04f))),
                ("shield_front", -1f, () => (shield.position + root.TransformVector(new Vector3(-0.10f, 0.10f, 0.75f)), shield.position)),
            };

            var picked = new List<int>();
            string list = Environment.GetEnvironmentVariable("CAPTURE_LIST");
            if (!string.IsNullOrEmpty(list))
                foreach (string part in list.Split(',')) if (int.TryParse(part, out int v) && v >= 1 && v <= views.Count) picked.Add(v - 1);
            if (picked.Count == 0) for (int i = 0; i < Mathf.Min(count, views.Count); i++) picked.Add(i);

            var capture = new CaptureRenderer(dir, picked.Count, 60f, drawGrass: true);
            capture.Camera.cullingMask |= 1 << LayerMask.NameToLayer("Viewmodel");
            capture.Camera.nearClipPlane = 0.01f; // as the viewmodel camera
            for (int j = 0; j < picked.Count; j++)
            {
                var view = views[picked[j]];
                arms.PoseSwing(view.phase);
                yield return null;
                (Vector3 pos, Vector3 target) = view.cam();
                capture.Shoot(j, view.name, pos, target);
                yield return null;
            }
            capture.Finish();
        }

        /// <summary>The bow (CAPTURE_DIR): the player's view through a shot, and an arrow in flight from the side.</summary>
        [UnityTest]
        public IEnumerator CaptureBow()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), ".agent_vision", "bow");
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1.5f);

            var player = UnityEngine.Object.FindFirstObjectByType<PlayerCombatController>();
            player.EquipItem(BattleOfAges.Crafting.ItemType.Bow, 1);
            FpsViewmodelAnimation arms = UnityEngine.Object.FindFirstObjectByType<FpsViewmodelAnimation>();
            arms.enabled = false;
            Camera main = Camera.main;
            var shots = new (string name, float t)[] { ("bow_ready", -1f), ("bow_aim_in", 0.12f), ("bow_full_draw", 0.49f), ("bow_release", 0.53f), ("bow_down", 0.85f) };
            var capture = new CaptureRenderer(dir, shots.Length + 1, 60f, drawGrass: true);
            capture.Camera.cullingMask |= 1 << LayerMask.NameToLayer("Viewmodel");
            capture.Camera.nearClipPlane = 0.01f;
            for (int j = 0; j < shots.Length; j++)
            {
                arms.PoseShot(shots[j].t);
                yield return null;
                capture.Shoot(j, shots[j].name, main.transform.position, main.transform.position + main.transform.forward);
                yield return null;
            }

            // An arrow in flight, seen from the side
            var target = new GameObject("BowTarget", typeof(BattleOfAges.Combat.HealthComponent));
            target.transform.position = player.transform.position + player.transform.forward * 20f;
            target.GetComponent<BattleOfAges.Combat.HealthComponent>().InitializeHealth(1000);
            BattleOfAges.Combat.ArrowShot.Fire(player.GetComponent<BattleOfAges.Combat.CombatUnit>(), target.GetComponent<BattleOfAges.Combat.HealthComponent>(), 1, false);
            yield return new WaitForSeconds(0.2f);
            Time.timeScale = 0f;
            var arrow = UnityEngine.Object.FindFirstObjectByType<BattleOfAges.Combat.ArrowShot>();
            Vector3 at = arrow != null ? arrow.transform.position : target.transform.position;
            capture.Camera.cullingMask &= ~(1 << LayerMask.NameToLayer("Viewmodel"));
            capture.Shoot(shots.Length, "arrow_flight", at + player.transform.right * 1.6f + Vector3.up * 0.3f, at);
            yield return null;
            Time.timeScale = 1f;
            capture.Finish();
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
