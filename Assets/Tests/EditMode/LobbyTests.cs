using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Net;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Tests
{
    /// <summary>The lobby's rules (docs/GAME_DESIGN.md §14.4): who comes in, what each may choose, who is waited for.</summary>
    public class LobbyTests
    {
        [SetUp]
        public void Setup() => GuestBook.Clear();

        [TearDown]
        public void Teardown() => GuestBook.Clear();

        [Test]
        public void ANewPlayer_ChoosesItsHero_AndIsWaitedFor()
        {
            var lobby = new LobbyHost();
            Assert.IsNull(lobby.Join(5, "aaa", "  Петро  "));
            GuestRecord r = lobby.Here[5];
            Assert.AreEqual("Петро", r.Name);
            Assert.AreEqual((int)HeroClass.Tank, r.Class, "a hero to start from");
            CollectionAssert.AreEqual(new[] { "Петро" }, lobby.NotReady(), "not ready till it says so");

            Assert.IsNull(lobby.Choose(5, new NetLobbyChoice { Class = (int)HeroClass.Mage, HasArtifact = true, Artifact = (int)ArtifactType.Wisdom, Ready = true }));
            Assert.AreEqual((int)HeroClass.Mage, r.Class);
            Assert.AreEqual((int)ArtifactType.Wisdom, r.Artifact);
            Assert.AreEqual(0, lobby.NotReady().Count);

            NetLobby view = lobby.View(r.Number);
            Assert.AreEqual(r.Number, view.You);
            Assert.AreEqual(2, view.Seats.Count, "the host and the guest");
            Assert.IsTrue(view.Seats[0].IsHost);
            LobbySeat seat = view.SeatOf(r.Number);
            Assert.IsTrue(seat.Here && seat.Ready && !seat.Returning);
            Assert.AreEqual((int)HeroClass.Mage, seat.Class);
        }

        [Test]
        public void TheEmpire_IsTheHostsAlone_AndNoSuchThings()
        {
            var lobby = new LobbyHost();
            lobby.Join(1, "aaa", "");
            StringAssert.Contains("хост", lobby.Choose(1, new NetLobbyChoice { Class = (int)HeroClass.Dd, HasArtifact = true, Artifact = (int)ArtifactType.CentralEmpire }));
            Assert.AreEqual((int)Artifacts.All[0], lobby.Here[1].Artifact, "the empire is not taken");
            Assert.AreEqual((int)HeroClass.Dd, lobby.Here[1].Class, "the rest of the choice is");
            Assert.IsNotNull(lobby.Choose(1, new NetLobbyChoice { Class = (int)HeroClass.None }), "a class there is not");
            Assert.AreEqual((int)HeroClass.Dd, lobby.Here[1].Class);

            lobby.ChooseHost(new NetLobbyChoice { Class = (int)HeroClass.Archer, HasArtifact = true, Artifact = (int)ArtifactType.CentralEmpire });
            Assert.AreEqual((int)ArtifactType.CentralEmpire, lobby.Host.Artifact, "the host may");
            Assert.AreEqual((int)HeroClass.Archer, lobby.Host.Class);
        }

        [Test]
        public void APlayerOfTheSave_ComesBackToItsKingdom_AndOneWhoLeftIsForgotten()
        {
            var save = new SaveData();
            save.Player.Level = 7;
            save.Player.Class = (int)HeroClass.Tank;
            GuestRecord old = GuestBook.Of("old");
            old.Name = "Оля";
            old.Save = JsonUtility.ToJson(save);
            var lobby = new LobbyHost { FromSave = true };

            NetLobby before = lobby.View(0);
            Assert.AreEqual(2, before.Seats.Count, "its place waits for it");
            Assert.IsFalse(before.Seats[1].Here);
            Assert.IsTrue(before.Seats[1].Returning);
            StringAssert.Contains("7", before.Seats[1].Hero);

            Assert.IsNull(lobby.Join(3, "old", ""));
            Assert.AreSame(old, lobby.Here[3], "the same one, known by its token");
            Assert.AreEqual("Оля", lobby.Here[3].Name, "with its name");
            lobby.Choose(3, new NetLobbyChoice { Class = (int)HeroClass.Mage, Ready = true });
            Assert.AreNotEqual((int)HeroClass.Mage, lobby.Here[3].Class, "a returning hero is its own: nothing chosen");
            Assert.AreEqual(0, lobby.NotReady().Count, "but it is ready");

            Assert.IsNull(lobby.Join(4, "new", "Ніхто"));
            Assert.AreEqual(2, GuestBook.All.Count);
            lobby.Leave(4);
            Assert.IsNull(GuestBook.Find("new"), "a new one gone before the game is forgotten");
            lobby.Leave(3);
            Assert.IsNotNull(GuestBook.Find("old"), "one of the save is not");
            Assert.AreEqual(2, lobby.View(0).Seats.Count);
        }

        [Test]
        public void TheSameMachine_CannotComeTwice_NorElevenPlayers()
        {
            var lobby = new LobbyHost();
            Assert.IsNull(lobby.Join(1, "a", ""));
            Assert.IsNotNull(lobby.Join(2, "a", ""), "the same token twice");
            for (int i = 2; i < LobbyHost.MaxPlayers; i++) Assert.IsNull(lobby.Join(i + 10, "t" + i, ""), "player " + (i + 1));
            Assert.AreEqual(LobbyHost.MaxPlayers, lobby.SeatCount());
            StringAssert.Contains("повне", lobby.Join(99, "late", ""));
            lobby.Starting = true;
            Assert.IsNotNull(lobby.Join(100, "t2", ""), "nobody in once it starts");
        }

        [Test]
        public void ANameIsKeptShort_AndPlain()
        {
            Assert.AreEqual("Петро", LobbyHost.CleanName("  Петро  "));
            string tagged = LobbyHost.CleanName("<color=red>x</color>");
            StringAssert.DoesNotContain("<", tagged, "no rich text in others' lists");
            StringAssert.DoesNotContain(">", tagged);
            Assert.AreEqual(LobbyHost.MaxNameLength, LobbyHost.CleanName(new string('я', 50)).Length);
            Assert.AreEqual("", LobbyHost.CleanName(null));
            Assert.AreEqual("Гравець 4", LobbyHost.NameOf(new GuestRecord { Number = 4 }));
        }

        [Test]
        public void ARecordOfAnOldSave_ReadsWithoutTheLobbysFields()
        {
            GuestRecord r = JsonUtility.FromJson<GuestRecord>("{\"Token\":\"x\",\"Number\":2,\"Save\":\"\"}");
            Assert.AreEqual("", r.Name);
            Assert.AreEqual((int)HeroClass.None, r.Class);
            Assert.IsNull(r.Hero);
        }

        [Test]
        public void AGuestsSave_GoesAsOftenAsItsSizeAllows()
        {
            // A tenth of a 1 Mbit/s upload (§14.4, GuestSaveProbe): a small save every 5 s, a late one (~200 KB) every ~16 s,
            // and none waits over a minute
            float was = NetSession.GuestSaveSeconds;
            NetSession.GuestSaveSeconds = 0f;
            try
            {
                Assert.AreEqual(NetSession.GuestSaveMinSeconds, NetSession.GuestSaveInterval(4 * 1024), 1e-3f);
                Assert.AreEqual(NetSession.GuestSaveMinSeconds, NetSession.GuestSaveInterval(48 * 1024), 1e-3f);
                Assert.AreEqual(16.1f, NetSession.GuestSaveInterval(201 * 1024), 0.1f);
                Assert.AreEqual(NetSession.GuestSaveMaxSeconds, NetSession.GuestSaveInterval(5 * 1024 * 1024), 1e-3f);
                NetSession.GuestSaveSeconds = 3f;
                Assert.AreEqual(3f, NetSession.GuestSaveInterval(201 * 1024), 1e-3f, "a fixed interval wins (NetDemo)");
            }
            finally { NetSession.GuestSaveSeconds = was; }
        }
    }
}
