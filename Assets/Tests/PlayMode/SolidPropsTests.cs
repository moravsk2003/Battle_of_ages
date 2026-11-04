using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.Net;
using BattleOfAges.RTS;
using BattleOfAges.UI;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Testers' NPCs stood inside rocks and trunks: nobody gets into one now, and whoever is in one comes out. And what the list of players says.</summary>
    public class SolidPropsTests
    {
        private static readonly Vector3 Far = new Vector3(-5200f, 0f, -5200f);
        private readonly List<GameObject> spawned = new List<GameObject>();

        // (a campaign left by another test bounds the world, and Far lies beyond its mountains)
        [SetUp]
        public void Setup() => WorldSettings.Set(0, 0f);

        [TearDown]
        public void Teardown()
        {
            foreach (GameObject go in spawned)
            {
                if (go == null) continue;
                SolidProps.Remove(go);
                Object.Destroy(go);
            }
            spawned.Clear();
            Time.timeScale = 1f;
        }

        private GameObject Rock(Vector3 at, float radius)
        {
            var go = new GameObject("TestRock");
            spawned.Add(go);
            SolidProps.Set(go, at, radius);
            return go;
        }

        [Test]
        public void AStepIntoARock_SlidesAlongItsEdge()
        {
            GameObject rock = Rock(Far, 2f);
            Vector3 from = Far + new Vector3(-3f, 0f, 0.5f);
            Vector3 step = SolidProps.Constrain(from, new Vector3(1.5f, 0f, 0f), 0.5f);
            Vector3 end = from + step;
            Assert.GreaterOrEqual(new Vector2(end.x - Far.x, end.z - Far.z).magnitude, 2.5f - 0.001f, "not into it");
            Assert.Greater(end.z - from.z, 0.01f, "round it, to the side it was already on");

            Assert.IsTrue(SolidProps.Inside(Far + new Vector3(0.5f, 0f, 0f), 0.4f, out Vector3 way));
            Assert.Greater(way.x, 0f, "the short way out");
            SolidProps.Remove(rock);
            Assert.IsFalse(SolidProps.Inside(Far + new Vector3(0.5f, 0f, 0f), 0.4f, out _), "gone, it stops nobody");
        }

        /// <summary>A worker walking dead straight at a rock's middle stood there for good: an even chance, by where it came from.</summary>
        [Test]
        public void AStepAtTheMiddle_StillGoesRound()
        {
            Rock(Far, 1.5f);
            Vector3 at = Far + new Vector3(2f, 0f, 0f);
            for (int i = 0; i < 400; i++)
            {
                Vector3 toGoal = Far + new Vector3(-9f, 0f, 0f) - at;
                at += SolidProps.Constrain(at, toGoal.normalized * 0.05f, 0.5f);
            }
            Assert.Less((at - (Far + new Vector3(-9f, 0f, 0f))).magnitude, 0.2f, "round the rock and on to where it was going");
        }

        [Test]
        public void ATree_IsSolid_OnlyWhileItStands()
        {
            GameObject go = SteppeTreeBuilder.CreateSteppeTree("Solid_Tree", 1);
            spawned.Add(go);
            go.transform.position = Far;
            Resources.ResourceNodeTree tree = go.GetComponent<Resources.ResourceNodeTree>();
            tree.StandSolid();
            Assert.IsTrue(SolidProps.Inside(Far, 0.1f, out _), "its trunk");
            tree.ConvertToWoodPile();
            Assert.IsFalse(SolidProps.Inside(Far, 0.1f, out _), "a stump is walked over");
            tree.ResetTreeState();
            Assert.IsTrue(SolidProps.Inside(Far, 0.1f, out _), "grown back");
            go.SetActive(false);
            Assert.IsFalse(SolidProps.Inside(Far, 0.1f, out _), "back in the pool");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AWorker_GoesRoundTheRocks_AndOneInARockComesOut()
        {
            Vector3 c = Far;
            c.y = RtsUnit.GetTerrainHeight(c);
            // Plain circles with no collider, the worst case: the steering probes see nothing there
            Rock(c, 1.5f);
            Rock(c + new Vector3(0f, 0f, 3.6f), 1.2f);
            GameObject unit = BuildingNode.CreateWorkerNpc(c + new Vector3(0.3f, 0f, 0.2f), "NPC_Rocks"); // starts in the rock
            spawned.Add(unit);
            unit.GetComponent<NpcResourceGatherer>().SetGatherDirective(GatherDirective.Idle);
            RtsUnit walker = unit.GetComponent<RtsUnit>();
            yield return new WaitForSeconds(0.2f);
            Vector3 to = c + new Vector3(9f, 0f, 0f);
            to.y = RtsUnit.GetTerrainHeight(to);
            walker.IssueMoveCommand(to);
            yield return new WaitForSeconds(1.5f);
            Assert.IsFalse(SolidProps.Inside(unit.transform.position, 0.3f, out _), "out of it");

            Vector3 back = c + new Vector3(-9f, 0f, 0.3f);
            back.y = RtsUnit.GetTerrainHeight(back);
            Time.timeScale = 4f;
            walker.IssueMoveCommand(back);
            float end = Time.time + 12f;
            while (Time.time < end && walker.HasMoveTarget)
            {
                Assert.IsFalse(SolidProps.Inside(unit.transform.position, 0.3f, out _), "never in one on the way");
                yield return null;
            }
            Vector3 left = unit.transform.position - back;
            left.y = 0f;
            Assert.Less(left.magnitude, 2f, $"and it gets there, straight through where the rock stands (it is {unit.transform.position - c} from the rock, on its way: {walker.HasMoveTarget})");
        }

        [Test]
        public void TheGearByte_SaysWhatIsWorn()
        {
            var go = new GameObject("Gear");
            spawned.Add(go);
            Equipment worn = go.AddComponent<Equipment>();
            Assert.AreEqual(0, UnitSnapshotWriter.GearOf(worn), "nothing on");
            worn.Equip(new Item(ItemType.Staff, 3));
            worn.Equip(new Item(ItemType.Helmet, 2));
            var read = new List<Item>();
            UnitSnapshotWriter.ReadGear(UnitSnapshotWriter.GearOf(worn), false, read);
            Assert.AreEqual(2, read.Count);
            Assert.AreEqual(ItemType.Staff, read[0].Type);
            Assert.AreEqual(ItemType.Helmet, read[1].Type);
            UnitSnapshotWriter.ReadGear(0, true, read);
            Assert.AreEqual(ItemType.Helmet, read[0].Type, "a hero's head is never bare");
        }

        [Test]
        public void TheListOfPlayers_SaysWhoIsHere()
        {
            StringAssert.Contains("самі", HudPlayerList.Describe(false, false, 0, null, null));
            string alone = HudPlayerList.Describe(true, true, 0, new[] { 0 }, "адреса 1.2.3.4, порт 7777");
            StringAssert.Contains("Хост (ви)", alone);
            StringAssert.Contains("нікого", alone);
            StringAssert.Contains("1.2.3.4", alone);
            string two = HudPlayerList.Describe(true, false, 2, new[] { 0, 2 }, null);
            StringAssert.Contains("Гравець 2 (ви)", two);
            StringAssert.DoesNotContain("нікого", two);
        }

        [UnityTest]
        public IEnumerator TheListOfPlayers_FitsItsPanel()
        {
            var canvas = new GameObject("ListCanvas", typeof(RectTransform), typeof(Canvas));
            spawned.Add(canvas);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            HudPlayerList list = HudPlayerList.Create((RectTransform)canvas.transform);
            yield return null;
            RectTransform panel = (RectTransform)list.transform.GetChild(0);
            Assert.IsFalse(panel.gameObject.activeSelf, "hidden till asked for");
            list.ShownAnyway = true;
            yield return null;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.IsTrue(panel.gameObject.activeSelf);
            UnityEngine.UI.Text words = panel.GetComponentInChildren<UnityEngine.UI.Text>();
            StringAssert.Contains("самі", words.text);
            Assert.GreaterOrEqual(panel.rect.height, words.preferredHeight, "every line inside the panel");
            float left = float.MaxValue, right = float.MinValue;
            foreach (UIVertex v in words.cachedTextGenerator.verts)
            {
                left = Mathf.Min(left, v.position.x);
                right = Mathf.Max(right, v.position.x);
            }
            Assert.LessOrEqual(right - left, panel.rect.width, "and every letter");
        }

        [Test]
        public void OnlyTheSameVersion_MayJoin()
        {
            Assert.IsTrue(NetSession.SameVersion("0.3.2", "0.3.2"));
            Assert.IsFalse(NetSession.SameVersion("0.3.1", "0.3.2"));
            Assert.IsFalse(NetSession.SameVersion(null, "0.3.2"), "one too old to say");
        }
    }
}
