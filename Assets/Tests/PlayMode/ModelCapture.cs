using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for character models and animations (not a regular test). Plays SampleScene, sets up
    /// line-ups of every character role and frozen animation phases (walk / run / attack / death at 0, 25, 50, 75 %)
    /// on the flat base plateau and renders them. CAPTURE_VIEWS = 1..14 (priority order), CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class ModelCapture
    {
        private static readonly HumanoidModelBuilder.HumanType[] Roles =
        {
            HumanoidModelBuilder.HumanType.HeroPlayer, HumanoidModelBuilder.HumanType.KnightGuard, HumanoidModelBuilder.HumanType.Warlord,
            HumanoidModelBuilder.HumanType.Villager, HumanoidModelBuilder.HumanType.Bandit, HumanoidModelBuilder.HumanType.Goblin
        };

        private float g;
        private Vector3 P(float x, float z, float up = 0f) => new Vector3(x, g + up, z);

        [UnityTest]
        public IEnumerator CaptureModels()
        {
            int count = Mathf.Clamp(int.TryParse(Environment.GetEnvironmentVariable("CAPTURE_VIEWS"), out int n) ? n : 4, 1, 14);
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "models");

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);
            g = InfiniteWorldManager.GetSampledTerrainHeight(0f, 0f);

            // Stage areas on the flat plateau, south-east of the camp.
            Vector3 lineup = P(18f, -24f), walk = P(18f, -36f), attack = P(30f, -24f), run = P(30f, -36f),
                    villagerAttack = P(6f, -36f), death = P(6f, -24f), animals = P(-14f, -30f);

            Vector3 lineupCam = lineup + new Vector3(0f, 1.5f, -7.5f);
            var built = new List<GameObject>();
            for (int i = 0; i < Roles.Length; i++)
                built.Add(Spawn(Roles[i], lineup + new Vector3((i - 2.5f) * 1.5f, 0f, 0f), lineupCam));

            List<GameObject> walkers = Row(HumanoidModelBuilder.HumanType.KnightGuard, walk, Vector3.right);
            List<GameObject> runners = Row(HumanoidModelBuilder.HumanType.Bandit, run, Vector3.right);
            List<GameObject> attackers = Row(HumanoidModelBuilder.HumanType.KnightGuard, attack, Vector3.back);
            List<GameObject> villagerAttackers = Row(HumanoidModelBuilder.HumanType.Villager, villagerAttack, Vector3.back);
            List<GameObject> dying = Row(HumanoidModelBuilder.HumanType.Bandit, death, Vector3.back);

            var animalTypes = new[] { AnimalType.Deer, AnimalType.Boar, AnimalType.Rabbit };
            var displayAnimals = new List<PeacefulAnimalAI>();
            Vector3 deerGait = P(-14f, -42f);
            var gaitDeer = new List<PeacefulAnimalAI>();
            for (int i = 0; i < 4; i++)
            {
                GameObject d = AnimalModelBuilder.CreateAnimalPrefab(AnimalType.Deer, "Gait_Deer_" + i);
                PeacefulAnimalAI dai = d.GetComponent<PeacefulAnimalAI>();
                dai.enabled = false;
                d.transform.position = deerGait + new Vector3((i - 1.5f) * 2.2f, 0f, 0f);
                d.transform.rotation = Quaternion.LookRotation(Vector3.right);
                gaitDeer.Add(dai);
            }
            for (int i = 0; i < animalTypes.Length; i++)
            {
                GameObject a = AnimalModelBuilder.CreateAnimalPrefab(animalTypes[i], "Display_" + animalTypes[i]);
                PeacefulAnimalAI animalAI = a.GetComponent<PeacefulAnimalAI>();
                animalAI.enabled = false;
                displayAnimals.Add(animalAI);
                Vector3 pos = animals + new Vector3((i - 1) * 2.4f, 0f, 0f);
                a.transform.position = pos;
                a.transform.rotation = Quaternion.LookRotation(Vector3.right);
            }

            yield return null;
            yield return null; // Start() of the animation drivers has run

            Pose(walkers, "Locomotion", 1.4f);
            Pose(runners, "Locomotion", 4.2f);
            Pose(attackers, "Attack", 0f);
            Pose(villagerAttackers, "Attack", 0f);
            Pose(dying, "Death", 0f, 0.15f);
            for (int i = 0; i < displayAnimals.Count; i++) displayAnimals[i].PoseGait(0.2f + i * 0.15f, 1f);
            for (int i = 0; i < gaitDeer.Count; i++) gaitDeer[i].PoseGait(i * 0.25f, 1f);
            yield return null;

            Vector3 camp = Vector3.zero;
            var views = new List<(string, Func<(Vector3, Vector3)>)>
            {
                ("lineup_front", () => (lineupCam, lineup + Vector3.up * 1.0f)),
                ("walk_cycle_side", () => (walk + new Vector3(0f, 1.2f, -6.5f), walk + Vector3.up * 0.9f)),
                ("attack_frames", () => (attack + new Vector3(0f, 1.3f, -6.5f), attack + Vector3.up * 0.9f)),
                ("hero_closeup", () => (built[0].transform.position + new Vector3(0.6f, 1.6f, -2.2f), built[0].transform.position + Vector3.up * 1.15f)),
                ("deer_gait_cycle", () => (deerGait + new Vector3(0f, 1.3f, -7.5f), deerGait + Vector3.up * 0.8f)),
                ("knight_closeup", () => (built[1].transform.position + new Vector3(-0.7f, 1.5f, -2.0f), built[1].transform.position + Vector3.up * 1.1f)),
                ("villager_closeup", () => (built[3].transform.position + new Vector3(0.5f, 1.5f, -2.0f), built[3].transform.position + Vector3.up * 1.05f)),
                ("bandit_goblin_closeup", () => (built[4].transform.position + new Vector3(0.75f, 1.3f, -2.6f), built[4].transform.position + new Vector3(0.75f, 0.85f, 0f))),
                ("warlord_closeup", () => (built[2].transform.position + new Vector3(0.6f, 1.9f, -2.6f), built[2].transform.position + Vector3.up * 1.4f)),
                ("run_cycle_side", () => (run + new Vector3(0f, 1.2f, -6.5f), run + Vector3.up * 0.9f)),
                ("villager_attack_frames", () => (villagerAttack + new Vector3(0f, 1.3f, -6.5f), villagerAttack + Vector3.up * 0.9f)),
                ("death_frames", () => (death + new Vector3(0f, 1.6f, -6.0f), death + Vector3.up * 0.4f)),
                ("animals", () => (animals + new Vector3(0f, 1.3f, -5.5f), animals + Vector3.up * 0.6f)),
                ("live_raider_camp", () => (camp + new Vector3(-9f, 3f, -9f), camp + Vector3.up * 1.2f)),
            };

            // Unobstructed view of the models: no grass, no boulders or trees in the staging area.
            GrassFieldRenderer grassField = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            if (grassField != null) grassField.enabled = false;
            ClearStage(new[] { lineup, walk, attack, run, villagerAttack, death, animals, deerGait }, 9f);

            var capture = new CaptureRenderer(dir, count, 60f, drawGrass: false);
            for (int i = 0; i < count; i++)
            {
                if (views[i].Item1 == "live_raider_camp")
                {
                    GameObject player = GameObject.Find("Player");
                    player.GetComponent<HealthComponent>()?.InitializeHealth(1000000);
                    CharacterController cc = player.GetComponent<CharacterController>();
                    cc.enabled = false;
                    player.transform.position = new Vector3(-416f, InfiniteWorldManager.GetSampledTerrainHeight(-416f, 416f) + 1f, 416f);
                    cc.enabled = true;
                    yield return new WaitForSeconds(3f);
                    foreach (HostileSettlementLeader l in UnityEngine.Object.FindObjectsByType<HostileSettlementLeader>(FindObjectsSortMode.None))
                        camp = l.transform.position;
                }
                (Vector3 pos, Vector3 target) = views[i].Item2();
                capture.Shoot(i, views[i].Item1, pos, target);
                yield return null;
            }
            capture.Finish();
        }

        private static void ClearStage(Vector3[] areas, float radius)
        {
            foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                string n = t.name;
                if (!(n.StartsWith("Boulder") || n.StartsWith("SteppeTree") || n.StartsWith("SteppePine") || n.StartsWith("OrePebble") || n.StartsWith("CampGroveTree"))) continue;
                foreach (Vector3 a in areas)
                {
                    Vector3 d = t.position - a; d.y = 0f;
                    if (d.magnitude < radius) { t.gameObject.SetActive(false); break; }
                }
            }
        }

        private GameObject Spawn(HumanoidModelBuilder.HumanType role, Vector3 pos, Vector3 faceTowards)
        {
            GameObject go = HumanoidModelBuilder.CreateHumanoidModel(role, "Display_" + role);
            go.transform.position = pos;
            Vector3 dir = faceTowards - pos; dir.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            if (role == HumanoidModelBuilder.HumanType.Goblin) go.transform.localScale = Vector3.one * 0.75f;
            if (role == HumanoidModelBuilder.HumanType.Warlord) go.transform.localScale = Vector3.one * 1.25f;
            return go;
        }

        /// <summary>Four characters side by side (1.6 m apart along x), facing the given direction.</summary>
        private List<GameObject> Row(HumanoidModelBuilder.HumanType role, Vector3 center, Vector3 facing)
        {
            var list = new List<GameObject>();
            for (int i = 0; i < 4; i++)
            {
                GameObject go = HumanoidModelBuilder.CreateHumanoidModel(role, $"Pose_{role}_{i}");
                go.transform.position = center + new Vector3((i - 1.5f) * 1.6f, 0f, 0f);
                go.transform.rotation = Quaternion.LookRotation(facing);
                list.Add(go);
            }
            return list;
        }

        /// <summary>Freezes each character of the row at phase start + i * 25 % of the given state.</summary>
        private static void Pose(List<GameObject> row, string state, float speed, float start = 0f)
        {
            for (int i = 0; i < row.Count; i++)
            {
                Animator anim = row[i].GetComponentInChildren<Animator>();
                if (anim == null) continue;
                CharacterAnimationDriver driver = row[i].GetComponent<CharacterAnimationDriver>();
                if (driver != null) driver.SpeedOverride = speed;
                anim.SetFloat("Speed", speed);
                anim.Play(state, 0, start + i * 0.25f);
                anim.Update(0f);
                anim.speed = 0f;
            }
        }
    }
}
