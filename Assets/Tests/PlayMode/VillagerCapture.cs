using System;
using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Visual review tool for the villager-rig people (not a regular test): the peasant bare and with his axe, the warrior,
    /// the bandit, and a peasant in full kit of each finish; front, three-quarter, back, the face and the sword hand up
    /// close, walking. Logs the rig's rest positions. CAPTURE_DIR = output folder.
    /// </summary>
    [Explicit("Visual capture tool, run on demand")]
    public class VillagerCapture
    {
        [UnityTest]
        public IEnumerator CaptureVillagers()
        {
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), ".agent_vision", "villagers");
            GpuSkinnedCharacter.Enabled = false;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return new WaitForSeconds(1f);

            Vector3 c = new Vector3(18f, 0f, -24f);
            c.y = InfiniteWorldManager.GetSampledTerrainHeight(c.x, c.z);
            ModelCapture.ClearStage(new[] { c }, 14f);
            GrassFieldRenderer grass = UnityEngine.Object.FindFirstObjectByType<GrassFieldRenderer>();
            if (grass != null) grass.enabled = false;

            // The rig at rest, before anything animates it
            GameObject probe = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.Villager, "Probe");
            var log = new StringBuilder("[VillagerRig] ");
            foreach (string bone in new[] { "Hips", "Abdomen", "Torso", "Neck", "Head", "Head_end", "UpperArm.L", "LowerArm.L", "Palm.L", "MiddleHand.L", "UpperLeg.L", "LowerLeg.L", "Foot.L" })
            {
                Transform t = Find(probe.transform, bone);
                if (t != null) log.Append(bone).Append('=').Append(probe.transform.InverseTransformPoint(t.position).ToString("F3")).Append(' ');
            }
            CharacterModelLibrary lib = CharacterModelLibrary.Instance;
            if (lib != null) log.Append("eyes ").Append(lib.VillagerEyeLeft.ToString("F3")).Append(lib.VillagerEyeRight.ToString("F3"));
            Debug.Log(log.ToString());
            UnityEngine.Object.Destroy(probe);

            GameObject Spawn(HumanoidModelBuilder.HumanType type, float x, params Item[] kit)
            {
                Vector3 p = c + new Vector3(x, 0f, 0f);
                p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
                GameObject go = HumanoidModelBuilder.CreateHumanoidModel(type, "V_" + type);
                go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.back));
                Equipment e = go.AddComponent<Equipment>();
                foreach (Item item in kit) if (!item.IsEmpty) e.Equip(item);
                return go;
            }
            Item I(ItemType t, int level) => new Item(t, level);
            UnityEngine.Random.InitState(7); // which villagers are bearded
            Spawn(HumanoidModelBuilder.HumanType.Villager, -3f);
            Spawn(HumanoidModelBuilder.HumanType.Villager, -1.5f, I(ItemType.Axe, 1));
            Spawn(HumanoidModelBuilder.HumanType.Soldier, 0f, I(ItemType.Sword, 1), I(ItemType.Armor, 1));
            Spawn(HumanoidModelBuilder.HumanType.Bandit, 1.5f, I(ItemType.Sword, 1), I(ItemType.Armor, 1));
            Spawn(HumanoidModelBuilder.HumanType.Villager, 3f, I(ItemType.Sword, 3), I(ItemType.Shield, 3), I(ItemType.Helmet, 3), I(ItemType.Armor, 3), I(ItemType.Gloves, 2), I(ItemType.Boots, 2));
            GameObject walker = Spawn(HumanoidModelBuilder.HumanType.Villager, 4.5f, I(ItemType.Pickaxe, 2));
            yield return new WaitForSeconds(0.6f);

            var capture = new CaptureRenderer(dir, 10, 40f, drawGrass: false);
            Vector3 mid = c + Vector3.up * 1.0f;
            capture.Shoot(0, "front", c + new Vector3(0.75f, 1.5f, -7.5f), mid + Vector3.right * 0.75f);
            capture.Shoot(1, "three_quarter", c + new Vector3(-4.5f, 2.0f, -4.5f), mid);
            capture.Shoot(2, "back", c + new Vector3(0.75f, 1.6f, 7.5f), mid + Vector3.right * 0.75f);
            capture.Camera.fieldOfView = 20f;
            capture.Shoot(3, "face", c + new Vector3(-1.5f, 1.62f, -1.6f), c + new Vector3(-1.5f, 1.58f, 0f));
            capture.Shoot(4, "face_side", c + new Vector3(-0.1f, 1.62f, 0f), c + new Vector3(-1.5f, 1.58f, 0f));
            capture.Shoot(8, "face_other", c + new Vector3(-3f, 1.62f, -1.6f), c + new Vector3(-3f, 1.58f, 0f));
            capture.Shoot(9, "faces_warrior_bandit", c + new Vector3(0.75f, 1.62f, -2.6f), c + new Vector3(0.75f, 1.55f, 0f));
            capture.Shoot(5, "warriors", c + new Vector3(0.75f, 1.3f, -3.2f), c + new Vector3(0.75f, 1.0f, 0f));
            capture.Shoot(6, "full_kit", c + new Vector3(3f, 1.2f, -3.4f), c + new Vector3(3f, 1.0f, 0f));

            // Mid-stride
            Animator animator = walker.GetComponentInChildren<Animator>();
            foreach (CharacterAnimationDriver d in walker.GetComponentsInChildren<CharacterAnimationDriver>()) d.enabled = false;
            foreach (FootIK ik in walker.GetComponentsInChildren<FootIK>()) ik.enabled = false;
            animator.enabled = false;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                if (clip.name.Contains("Walk")) { clip.SampleAnimation(animator.gameObject, clip.length * 0.25f); break; }
            yield return null;
            capture.Camera.fieldOfView = 30f;
            capture.Shoot(7, "walking_side", c + new Vector3(8f, 1.3f, -0.5f), c + new Vector3(4.5f, 0.95f, 0f));
            yield return null;
            capture.Finish();
            GpuSkinnedCharacter.Enabled = true;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = Find(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
