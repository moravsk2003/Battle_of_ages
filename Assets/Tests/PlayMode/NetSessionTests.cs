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
                // Not the test runner's own scene: run alone, it is the only one loaded, and unloading it stopped the run for good
                // (it hung with -Filter NetSessionTests; KNOWN_ISSUES 54)
                if (scene != empty && scene.isLoaded && !scene.name.StartsWith("InitTestScene")) yield return SceneManager.UnloadSceneAsync(scene);
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

        /// <summary>A guest over the wire, by hand: says hello, hears the lobby, sends its choice (one process holds one NetSession).</summary>
        private sealed class WireGuest : System.IDisposable
        {
            public readonly NetLink Link;
            public NetLobby Lobby;
            public NetWelcome Welcome;
            public string Refused;
            public NetPauseState Pause;

            public WireGuest(ushort port, string token, string name)
            {
                Link = NetLink.Join("127.0.0.1", port);
                Link.Connected += _ => Say(NetSession.HelloMessage, JsonUtility.ToJson(new NetHello { Token = token, Version = NetSession.GameVersion, Name = name }));
                Link.Received += (peer, channel, data, length) =>
                {
                    if (length < 1) return;
                    string text = System.Text.Encoding.UTF8.GetString(data, 1, length - 1);
                    if (data[0] == NetSession.LobbyMessage) Lobby = JsonUtility.FromJson<NetLobby>(text);
                    else if (data[0] == NetSession.WelcomeMessage) Welcome = JsonUtility.FromJson<NetWelcome>(text);
                    else if (data[0] == NetSession.RefusedMessage) Refused = JsonUtility.FromJson<NetText>(text).Text;
                    else if (data[0] == NetSession.PauseMessage) Pause = JsonUtility.FromJson<NetPauseState>(text);
                };
            }

            public void Say(byte kind, string text)
            {
                byte[] utf = System.Text.Encoding.UTF8.GetBytes(text);
                var message = new byte[utf.Length + 1];
                message[0] = kind;
                System.Array.Copy(utf, 0, message, 1, utf.Length);
                Link.Send(0, NetChannel.Reliable, message, message.Length);
            }

            public IEnumerator Until(System.Func<bool> done, float seconds = 10f)
            {
                float end = Time.realtimeSinceStartup + seconds;
                while (!done() && Time.realtimeSinceStartup < end) { Link.Poll(); yield return null; }
            }

            public void Dispose() => Link.Dispose();
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator ALobby_SeatsAGuest_TakesItsHero_AndTurnsAwayTheSameMachineTwice()
        {
            const ushort port = 27795;
            Assert.IsTrue(NetSession.HostLobby(new CampaignSetup { Seed = 777, Size = 4000f, Heroes = 10 }, null, "", port));
            Assert.IsTrue(NetSession.HostingLobby);
            using (var guest = new WireGuest(port, "lobby-guest", "Гість"))
            {
                yield return guest.Until(() => guest.Lobby != null);
                Assert.IsNotNull(guest.Lobby, "the lobby comes to one who says hello");
                Assert.AreEqual(1, guest.Lobby.You);
                Assert.AreEqual(2, guest.Lobby.Seats.Count);
                Assert.AreEqual("Гість", guest.Lobby.SeatOf(1).Name);
                StringAssert.Contains("Нова кампанія", guest.Lobby.Campaign);

                guest.Say(NetSession.LobbyChoiceMessage, JsonUtility.ToJson(new NetLobbyChoice { Class = (int)Combat.HeroClass.Mage, HasArtifact = true, Artifact = (int)Combat.ArtifactType.Wisdom, Ready = true }));
                yield return guest.Until(() => guest.Lobby.SeatOf(1).Ready);
                Assert.IsTrue(guest.Lobby.SeatOf(1).Ready, "its choice is taken and told back");
                Assert.AreEqual((int)Combat.HeroClass.Mage, NetSession.Current.Lobby.SeatOf(1).Class, "the host has it too");

                using (var twin = new WireGuest(port, "lobby-guest", "Двійник"))
                {
                    yield return twin.Until(() => twin.Refused != null || twin.Lobby != null);
                    StringAssert.Contains("уже в лобі", twin.Refused, "the same machine twice is turned away");
                }
                guest.Say(NetSession.LobbyChoiceMessage, JsonUtility.ToJson(new NetLobbyChoice { Class = (int)Combat.HeroClass.Mage, HasArtifact = true, Artifact = (int)Combat.ArtifactType.CentralEmpire, Ready = true }));
                yield return guest.Until(() => !string.IsNullOrEmpty(guest.Lobby.Said));
                StringAssert.Contains("хост", guest.Lobby.Said, "told why the empire was not taken");
                Assert.AreEqual((int)Combat.ArtifactType.Wisdom, guest.Lobby.SeatOf(1).Artifact);
            }
            Assert.IsTrue(NetSession.HostingLobby, "the lobby stays open, its game not begun");
        }

        [Category("Slow")]
        [UnityTest, Timeout(90000)]
        public IEnumerator ALobbysGame_GivesTheGuestItsWorldAndHero_AndTurnsANewcomerAway()
        {
            const ushort port = 27796;
            Assert.IsTrue(NetSession.HostLobby(new CampaignSetup { Seed = 777, Size = 4000f, Heroes = 10 }, null, "", port));
            using (var guest = new WireGuest(port, "start-guest", "Гість"))
            {
                yield return guest.Until(() => guest.Lobby != null);
                guest.Say(NetSession.LobbyChoiceMessage, JsonUtility.ToJson(new NetLobbyChoice { Class = (int)Combat.HeroClass.Archer, HasArtifact = true, Artifact = (int)Combat.ArtifactType.Lightning, Ready = false }));
                yield return guest.Until(() => guest.Lobby.SeatOf(1).Class == (int)Combat.HeroClass.Archer);
                StringAssert.Contains("Гість", NetSession.StartLobbyGame(), "not ready: it says who is waited for");
                Assert.IsNull(NetSession.StartLobbyGame(force: true), "pressed again, it starts without waiting");

                yield return guest.Until(() => guest.Welcome != null, 60f);
                Assert.IsNotNull(guest.Welcome, "the world comes once the host's is up");
                Assert.AreEqual(777, guest.Welcome.WorldSeed);
                Assert.AreEqual((int)Combat.HeroClass.Archer, guest.Welcome.Class, "with the hero it chose");
                Assert.AreEqual((int)Combat.ArtifactType.Lightning, guest.Welcome.Artifact);
                Assert.IsTrue(guest.Welcome.HasArtifact);
                Assert.IsFalse(NetSession.HostingLobby, "the game is on");
                Assert.IsNotNull(Managers.GameContext.Player, "the host is in its world");
            }
            using (var late = new WireGuest(port, "late-guest", "Пізній"))
            {
                yield return late.Until(() => late.Refused != null || late.Welcome != null);
                StringAssert.Contains("лише через лобі", late.Refused, "a new player once the game is on is turned away");
            }
            using (var back = new WireGuest(port, "start-guest", "Гість"))
            {
                yield return back.Until(() => back.Refused != null || back.Welcome != null);
                Assert.IsNull(back.Refused, "one of the lobby comes back at any time");
                Assert.IsNotNull(back.Welcome);
                Assert.AreEqual((int)Combat.HeroClass.Archer, back.Welcome.Class, "its hero kept, though it had not saved yet");
            }
        }

        /// <summary>
        /// The network pause (§14.1, §14.4): a guest pauses the game for everyone (three times a game), the host lifts it; a guest
        /// leaving stops the game for everyone till the host's word, and the word "a bot" lets it go on.
        /// </summary>
        [UnityTest, Timeout(90000)]
        public IEnumerator APause_StopsTheGameForAll_AndAPlayerLeaving_WaitsForTheHostsWord()
        {
            const ushort port = 27797;
            NetSession.AlwaysBot = false;
            Assert.IsTrue(NetSession.HostLobby(new CampaignSetup { Seed = 777, Size = 4000f, Heroes = 10 }, null, "", port));
            var guest = new WireGuest(port, "pause-guest", "Гість");
            try
            {
                yield return guest.Until(() => guest.Lobby != null);
                Assert.IsNull(NetSession.StartLobbyGame(force: true));
                yield return guest.Until(() => guest.Welcome != null, 60f);
                Assert.IsNotNull(guest.Welcome);
                yield return guest.Until(() => NetSession.PauseState != null, 10f);

                for (int i = 0; i < NetSession.PausesPerPlayer; i++)
                {
                    guest.Say(NetSession.PauseRequestMessage, JsonUtility.ToJson(new NetPauseRequest { Pause = true }));
                    yield return guest.Until(() => NetSession.NetPaused && guest.Pause != null && guest.Pause.On);
                    Assert.IsTrue(NetSession.NetPaused, "the guest's pause stops the game");
                    Assert.AreEqual(1, guest.Pause.By, "told to everyone, with whose it is");
                    yield return null;
                    Assert.AreEqual(0f, Time.timeScale, "time stands");
                    Assert.IsTrue(NetSession.CanUnpause, "the host may always lift it");
                    NetSession.RequestPause(false);
                    yield return guest.Until(() => guest.Pause != null && !guest.Pause.On);
                    Assert.IsFalse(NetSession.NetPaused);
                }
                yield return null;
                Assert.AreEqual(1f, Time.timeScale, "time goes on");
                guest.Say(NetSession.PauseRequestMessage, JsonUtility.ToJson(new NetPauseRequest { Pause = true }));
                yield return guest.Until(() => NetSession.NetPaused, 2f);
                Assert.IsFalse(NetSession.NetPaused, "no pauses left for it this game");

                guest.Dispose();
                guest = null;
                float end = Time.realtimeSinceStartup + 20f;
                while (!NetSession.NetPaused && Time.realtimeSinceStartup < end) yield return null;
                Assert.IsTrue(NetSession.PauseState.AwaitingHost, "a player left: everyone waits for the host's word");
                Assert.IsFalse(NetSession.CanUnpause, "it is not lifted as a pause is");
                StringAssert.Contains("Гість", NetSession.WaitingNames);
                NetSession.HostPutBots();
                yield return null;
                Assert.IsFalse(NetSession.NetPaused, "the word given, the game goes on");
                Assert.AreEqual(1f, Time.timeScale);
            }
            finally
            {
                guest?.Dispose();
                Time.timeScale = 1f;
            }
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
