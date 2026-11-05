using System.Linq;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The knight's clips hold a guard (KnightClipTool): the sword up, not through the legs; the shield facing forward, not flat.</summary>
    public class KnightHeldClipsTests
    {
        private GameObject model;

        [TearDown]
        public void Teardown()
        {
            if (model != null) Object.Destroy(model);
        }

        [TestCase("Idle", 0.3f)]
        [TestCase("Walk", 0.3f)]
        [TestCase("Run", 0f)] // the body leans into a run, the blade with it
        public void TheGuardHolds_AllThroughTheClip(string clipName, float lowestBlade)
        {
            model = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.KnightGuard, "HeldClips");
            Animator animator = model.GetComponentInChildren<Animator>();
            AnimationClip clip = animator.runtimeAnimatorController.animationClips.First(c => c.name == clipName);
            Transform[] bones = model.GetComponentsInChildren<Transform>(true);
            Transform palm = bones.First(t => t.name == "Palm.R"), forearm = bones.First(t => t.name == "LowerArm.L");

            for (float t = 0f; t <= clip.length; t += 1f / 30f)
            {
                clip.SampleAnimation(animator.gameObject, t);
                Vector3 blade = model.transform.InverseTransformDirection(-palm.right);
                Vector3 face = model.transform.InverseTransformDirection(-forearm.right);
                Assert.Greater(blade.y, lowestBlade, $"{clipName} at {t:F2} s: the blade drops");
                Assert.Greater(face.z, 0.5f, $"{clipName} at {t:F2} s: the shield does not face forward");
            }
        }
    }
}
