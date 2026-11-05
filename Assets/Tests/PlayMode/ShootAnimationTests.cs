using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A unit with a bow shoots (its own clip, the bow drawn on the GPU too); with a sword it swings.</summary>
    public class ShootAnimationTests
    {
        private GameObject model;

        [TearDown]
        public void Teardown()
        {
            if (model != null) Object.Destroy(model);
        }

        [UnityTest]
        public IEnumerator ABow_PlaysTheShot_ASword_TheSwing([Values(HumanoidModelBuilder.HumanType.Villager, HumanoidModelBuilder.HumanType.HeroPlayer)] HumanoidModelBuilder.HumanType role,
                                                             [Values(ItemType.Bow, ItemType.Sword)] ItemType weapon)
        {
            model = HumanoidModelBuilder.CreateHumanoidModel(role, "Shooter");
            model.transform.position = new Vector3(0f, -500f, 0f);
            model.AddComponent<Equipment>().Equip(new Item(weapon, 2));
            yield return null;
            yield return null; // the drivers' Start

            CharacterAnimationDriver driver = model.GetComponentInChildren<CharacterAnimationDriver>();
            Animator animator = model.GetComponentInChildren<Animator>();
            Assert.AreEqual(weapon == ItemType.Bow, model.GetComponentInChildren<CharacterGear>().HoldsBow);
            animator.enabled = true; // drawn near (a test camera may be far)
            driver.PlaySwing();
            yield return null;
            string expected = weapon == ItemType.Bow ? "Shoot" : "Attack";
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(1).IsName(expected), $"{role} with a {weapon} plays {expected}");
        }

        [Test]
        public void TheGpuBake_HasTheShot()
        {
            model = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.Villager, "Baked");
            Animator animator = model.GetComponentInChildren<Animator>();
            SkinnedMeshRenderer body = animator.GetComponentInChildren<SkinnedMeshRenderer>();
            GpuSkinBake bake = GpuSkinBake.For(animator, model.transform, body, body.sharedMesh);
            Assert.IsNotNull(bake);
            Assert.IsTrue(bake.Shoot.Valid, "far archers shoot too");
            Assert.AreEqual(1f, bake.Shoot.Length, 0.05f);
        }
    }
}
