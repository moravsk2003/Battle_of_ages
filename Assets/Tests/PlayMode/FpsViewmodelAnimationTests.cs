using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Player;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The first-person arms: blows one after another alternate, and the step bob follows the body's real speed.</summary>
    public class FpsViewmodelAnimationTests
    {
        private GameObject body, ground;

        [TearDown]
        public void Teardown()
        {
            if (body != null) Object.Destroy(body);
            if (ground != null) Object.Destroy(ground);
        }

        private FpsViewmodelAnimation Arms()
        {
            body = new GameObject("ArmsTestBody");
            body.transform.position = new Vector3(0f, -400f, 0f);
            body.AddComponent<CharacterController>();
            ground = GameObject.CreatePrimitive(PrimitiveType.Cube); // something to stand on: steps are counted on the ground
            ground.transform.position = new Vector3(0f, -401.5f, 0f);
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            GameObject arms = HumanoidModelBuilder.CreateViewmodelArms("ArmsTest");
            arms.transform.SetParent(body.transform, false);
            return arms.AddComponent<FpsViewmodelAnimation>();
        }

        [UnityTest]
        public IEnumerator BlowsInARow_Alternate_AndAFreshOne_IsTheForehand()
        {
            FpsViewmodelAnimation arms = Arms();
            yield return null;
            Transform right = arms.transform.Find("RightArmPivot");
            Assert.IsNotNull(right);

            arms.PlayPunchAnimation();
            Assert.AreEqual(0, arms.SwingVariant, "the first blow is the forehand");
            yield return new WaitForSeconds(0.3f);
            arms.PlayPunchAnimation();
            Assert.AreEqual(1, arms.SwingVariant, "a blow cutting into the last one is the backhand");
            yield return new WaitForSeconds(0.75f); // the swing is over, still within the pair's window
            arms.PlayPunchAnimation();
            Assert.AreEqual(0, arms.SwingVariant, "then the forehand again");
            yield return new WaitForSeconds(1.6f); // over, and a pause
            arms.PlayPunchAnimation();
            Assert.AreEqual(0, arms.SwingVariant, "after a pause the forehand starts anew");

            // The two swings are other poses at the strike
            arms.PoseSwing(0.42f, 0);
            Quaternion fore = right.localRotation;
            arms.PoseSwing(0.42f, 1);
            Assert.Greater(Quaternion.Angle(fore, right.localRotation), 40f, "the backhand cuts the other way");
            arms.PoseSwing(-1f);
        }

        [UnityTest]
        public IEnumerator TheStepBob_FollowsTheDistanceWalked_AndStopsWithTheBody()
        {
            FpsViewmodelAnimation arms = Arms();
            CharacterController cc = body.GetComponent<CharacterController>();
            Physics.SyncTransforms(); // the ground was just made and moved
            for (int i = 0; i < 5; i++) { cc.Move(Vector3.down * 0.05f); yield return null; } // stand on it
            Assert.IsTrue(cc.isGrounded, "the body stands on the ground");

            float still = arms.StepPhase;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(still, arms.StepPhase, 1e-4f, "standing: no steps");

            // Walk: one step per stride, paced by the distance (the phase wraps at two steps, so count the advance)
            float advanced = 0f, last = arms.StepPhase, walked = 0f;
            int onGround = 0, stepped = 0;
            for (int i = 0; i < 60; i++)
            {
                Vector3 before = body.transform.position;
                cc.Move(Vector3.forward * 3f * Time.deltaTime + Vector3.down * 0.05f); // pressed to the ground every frame, as the game does
                yield return null;
                Vector3 moved = body.transform.position - before;
                walked += new Vector2(moved.x, moved.z).magnitude; // along the ground (the controller bobs on its skin)
                if (cc.isGrounded) onGround++;
                float now = arms.StepPhase;
                if (now != last) stepped++;
                advanced += Mathf.Repeat(now - last, Mathf.PI * 4f);
                last = now;
            }
            Assert.Greater(walked, 0.05f, "the body walked");
            float expected = walked / (0.9f + 0.1f * 3f) * Mathf.PI * 2f; // a stride of 1.2 m at 3 m/s, 2 pi a step
            Assert.AreEqual(expected, advanced, expected * 0.3f, $"walking {walked:0.00} m: the steps follow the distance (on the ground {onGround} of 60 frames, the phase moved in {stepped})");
        }
    }
}
