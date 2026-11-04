using System.Collections;
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
