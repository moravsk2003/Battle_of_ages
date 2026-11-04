using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Net;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// M8 step 4, what one process can check of a session; two players between two processes are NetDemo's ("-netdemo sessionhost",
    /// "-netdemo sessionjoin"): the same world, each in its place, each seeing the other.
    /// </summary>
    public class NetSessionTests
    {
        private string folder;

        [SetUp]
        public void Setup()
        {
            folder = Path.Combine(Path.GetTempPath(), "boa_session_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.FolderOverride = folder;
        }

        [TearDown]
        public void Teardown()
        {
            NetSession.Stop();
            SaveSystem.FolderOverride = null;
            NpcKingdom.RemoveAll();
            KingdomRealm.Clear();
            WorldSettings.Set(0, 0f);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
        }

        [UnityTearDown]
        public IEnumerator LeaveTheScene()
        {
            Scene empty = SceneManager.CreateScene("Empty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [Test]
        public void APlayersSite_IsFree_AndTheSameForASeed()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            Vector3 a = NetSession.SiteFor(1, 16), again = NetSession.SiteFor(1, 16), b = NetSession.SiteFor(2, 16);
            Assert.AreEqual(a, again, "the same world gives the same place");
            Assert.Greater(new Vector2(a.x - b.x, a.z - b.z).magnitude, 100f, "each its own");
            Assert.Greater(new Vector2(a.x, a.z).magnitude, StartSites.BaseClearance - 1f, "away from the host's base");
            foreach (Vector3 site in StartSites.Choose(15))
                Assert.Greater(new Vector2(a.x - site.x, a.z - site.z).magnitude, 100f, "and from the campaign's heroes");
        }

        [Test]
        public void AChunksLife_HasOneOwner_TillItIsGivenUp()
        {
            var claims = new LifeClaims();
            var chunk = new Vector2Int(4, -2);
            Assert.IsTrue(claims.TryClaim(chunk, 1), "the first one there");
            Assert.IsTrue(claims.TryClaim(chunk, 1), "it stays its");
            Assert.IsFalse(claims.TryClaim(chunk, 2), "not another's");
            Assert.IsFalse(claims.Release(chunk, 2), "nor can another give it up");
            Assert.IsTrue(claims.Release(chunk, 1));
            Assert.IsTrue(claims.TryClaim(chunk, 2), "free now");
            claims.TryClaim(new Vector2Int(5, 5), 2);
            claims.TryClaim(new Vector2Int(6, 6), 3);
            List<Vector2Int> freed = claims.ReleaseAll(2);
            Assert.AreEqual(2, freed.Count, "a player gone gives up all it held");
            Assert.AreEqual(1, claims.Count);
        }

        [Test]
        public void AGuest_IsTheSameGuest_ThroughTheHostsSave()
        {
            GuestBook.Clear();
            GuestRecord first = GuestBook.Of("aaa"), second = GuestBook.Of("bbb");
            Assert.AreEqual(1, first.Number);
            Assert.AreEqual(2, second.Number, "each its own place");
            Assert.AreSame(first, GuestBook.Of("aaa"), "a guest coming back is the same one");
            first.Save = "{\"Wood\":777}";

            var host = new SaveData();
            host.Guests.AddRange(GuestBook.All);
            SaveData read = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(host));
            GuestBook.Load(read.Guests);
            Assert.AreEqual(2, GuestBook.All.Count);
            Assert.AreEqual(777, JsonUtility.FromJson<SaveData>(GuestBook.Of("aaa").Save).Wood, "its game whole, through the host's save");
            Assert.AreEqual(1, GuestBook.Of("aaa").Number, "and its place");
            Assert.AreEqual(3, GuestBook.Of("ccc").Number, "a new one comes after them");
            GuestBook.Clear();
        }

        [Test]
        public void ThisMachine_KeepsItsName()
        {
            string token = GuestBook.LocalToken;
            Assert.IsFalse(string.IsNullOrEmpty(token));
            Assert.AreEqual(token, GuestBook.LocalToken);
        }

        [UnityTest]
        public IEnumerator WhatOthersDoToATree_IsRemembered_AndShownIfItStandsHere()
        {
            GameObject go = BattleOfAges.Utils.SteppeTreeBuilder.CreateSteppeTree("Shared_Tree", 1);
            go.transform.position = new Vector3(5200f, 0f, 5400f);
            Resources.ResourceNodeTree tree = go.GetComponent<Resources.ResourceNodeTree>();
            WorldPersistence.RegisterTree(tree, "shared:1");
            yield return null;
            try
            {
                WorldPersistence.ApplyTree(new TreeSave { Key = "shared:1", State = (int)Resources.TreeState.WoodPile, Wood = 3 });
                Assert.AreEqual(Resources.TreeState.WoodPile, tree.CurrentState, "the one standing here falls");
                Assert.AreEqual(3, tree.WoodRemaining);
                Assert.IsTrue(WorldPersistence.TryGetTree("shared:1", out TreeSave known));
                Assert.AreEqual(3, known.Wood);

                WorldPersistence.ListeningToTrees = true;
                WorldPersistence.ApplyTree(new TreeSave { Key = "shared:1", State = (int)Resources.TreeState.WoodPile, Wood = 1 });
                Assert.AreEqual(1, tree.WoodRemaining, "wood taken from the pile");
                Assert.AreEqual(0, WorldPersistence.TakeChangedTrees().Count, "what was told is not told back");
                WorldPersistence.NoteTree(tree);
                List<TreeSave> changed = WorldPersistence.TakeChangedTrees();
                Assert.AreEqual(1, changed.Count, "a change by a hand here is");
                Assert.AreEqual("shared:1", changed[0].Key);

                WorldPersistence.ApplyTree(new TreeSave { Key = "shared:1", State = (int)Resources.TreeState.WholeTree });
                Assert.AreEqual(Resources.TreeState.WholeTree, tree.CurrentState, "grown back elsewhere, it stands here too");
                Assert.IsFalse(WorldPersistence.TryGetTree("shared:1", out _));
            }
            finally
            {
                WorldPersistence.ListeningToTrees = false;
                WorldPersistence.Clear();
                Object.Destroy(go);
            }
        }

        [Test]
        public void AnothersZone_ClearsTheLandHere_ButIsNotOurs()
        {
            ZoneMap.Clear();
            ZoneMap.ClearForeign();
            Vector3 at = new Vector3(5300f, 0f, 5300f);
            ZoneMap.SetForeign(ZoneMap.CellOf(at), ZoneType.Fields);
            Assert.AreEqual(ZoneType.None, ZoneMap.At(at), "nothing of ours grows there");
            Assert.AreEqual(ZoneType.Fields, ZoneMap.ForeignAt(at));
            Assert.IsTrue(ProceduralBiomeChunk.ZoneClearsTrees(at), "but no tree stands on it, as on its owner's machine");
            ZoneMap.SetForeign(ZoneMap.CellOf(at), ZoneType.None);
            Assert.AreEqual(0, ZoneMap.ForeignCount);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator AGuest_DoesNotSave_TheHostDoes()
        {
            SaveSystem.NewGame();
            yield return new WaitForSeconds(3f);
            Assert.IsTrue(SaveSystem.Save(1), "alone it saves");
            Assert.IsTrue(NetSession.Join("127.0.0.1", 27790), "going to a host (none there: it does not matter here)");
            Assert.IsTrue(NetSession.IsGuest);
            Assert.IsFalse(SaveSystem.Save(2), "a guest's game is saved on its host");
            NetSession.Stop();

            Assert.IsTrue(NetSession.Host(27791));
            Assert.IsTrue(NetSession.IsHost);
            Assert.IsFalse(NetSession.IsGuest);
            Assert.IsTrue(SaveSystem.Save(2), "a host saves");
            StringAssert.Contains("7777", NetSession.AddressLine());
        }

        [Category("Slow")]
        [UnityTest, Timeout(60000)]
        public IEnumerator AGuestLeft_WithNoAnswer_IsToldSo()
        {
            string said = null;
            System.Action<string> listen = why => said = why;
            NetSession.Failed += listen;
            Assert.IsTrue(NetSession.Join("127.0.0.1", 27792));
            float end = Time.realtimeSinceStartup + 30f;
            while (said == null && Time.realtimeSinceStartup < end) yield return null;
            NetSession.Failed -= listen;
            Assert.IsNotNull(said, "it does not wait for ever");
            Assert.IsFalse(NetSession.Active, "and the session is over");
        }
    }
}
