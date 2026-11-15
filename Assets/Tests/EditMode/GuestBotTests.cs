using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Net;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>The bot that keeps an absent player's kingdom (docs/GAME_DESIGN.md §14.4): raised from the save, guards only, gives the losses back.</summary>
    public class GuestBotTests
    {
        private readonly System.Random rng = new System.Random(7);

        [SetUp]
        public void Setup() => WorldSettings.Set(2024, WorldSettings.DefaultSize);

        [TearDown]
        public void Teardown()
        {
            KingdomRealm.Clear();
            NpcKingdom.RemoveAll();
            GameObject realm = GameObject.Find("KingdomRealm");
            if (realm != null) Object.DestroyImmediate(realm);
            WorldSettings.Set(0, 0f);
        }

        private static SaveData GuestSave(int people, int warriors)
        {
            var save = new SaveData();
            save.Player.Position = new Vector3(110f, 0f, 90f);
            save.Player.Health = 400;
            save.Player.Class = (int)HeroClass.Archer;
            save.Player.Level = 3;
            save.Player.Xp = 20;
            save.Buildings.Add(new BuildingSave { Type = (int)BuildingType.WorkerHouse, Position = new Vector3(95f, 0f, 100f) });
            save.Buildings.Add(new BuildingSave { Type = (int)BuildingType.TownHall, Position = new Vector3(100f, 0f, 100f), Level = 2, Health = 900 });
            for (int i = 0; i < people; i++) save.Units.Add(new UnitSave { Name = "P" + i, Villager = true, Rank = (int)UnitRank.Person, Health = 50, Position = new Vector3(100f + i, 0f, 104f) });
            for (int i = 0; i < warriors; i++) save.Units.Add(new UnitSave { Name = "W" + i, Rank = (int)UnitRank.Warrior, Health = 120, Position = new Vector3(100f + i, 0f, 96f) });
            return save;
        }

        private static GuestRecord Guest(SaveData save) => new GuestRecord { Token = "guest-a", Number = 2, Name = "Оля", Save = JsonUtility.ToJson(save) };

        [Test]
        public void TheKingdom_IsRaisedFromTheSave_AsItWasLeft()
        {
            KingdomState s = GuestBot.FromSave(GuestSave(5, 3), Guest(GuestSave(5, 3)));
            Assert.IsTrue(s.Guardian);
            Assert.AreEqual("guest-a", s.GuestToken);
            Assert.AreEqual(new Vector3(100f, 0f, 100f), s.Home, "where its hall stands");
            Assert.AreEqual(2, s.Hall);
            Assert.AreEqual(8, s.Subjects.Count, "everyone of it");
            Assert.AreEqual(3, s.Subjects.FindAll(u => u.Rank == (int)UnitRank.Warrior).Count);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, s.Subjects.ConvertAll(u => u.Origin), "each knows its place in the save");
            Assert.IsTrue(s.HasKing);
            Assert.AreEqual((int)HeroClass.Archer, s.KingClass);
            Assert.AreEqual(400, s.King.Health);
            Assert.AreEqual(20 + HeroProgress.XpForLevelUp(1) + HeroProgress.XpForLevelUp(2), s.KingTotalXp, "the hero's whole experience");
            Assert.Less(Vector3.Distance(s.King.Position, s.Home), 6f, "the hero waits by the hall");
            Assert.AreNotEqual(GuestBot.TeamOf(3), s.Team, "each bot a side of its own");
        }

        [Test]
        public void AGuardian_NeverGrows_NeverRaids_AndDoesNotFall()
        {
            KingdomState bot = GuestBot.FromSave(GuestSave(10, 0), Guest(GuestSave(10, 0)));
            bot.Wood = 500f;
            int hall = bot.Hall;
            KingdomRealm.Advance(bot, 200f);
            Assert.AreEqual(10, bot.Subjects.Count, "no births");
            Assert.IsTrue(bot.Subjects.TrueForAll(u => u.Rank == (int)UnitRank.Person), "no training");
            Assert.AreEqual(hall, bot.Hall, "no building");
            Assert.AreEqual(500f, bot.Wood, "nothing spent");

            // Strong, with a weak neighbour: still no raid
            for (int i = 0; i < 60; i++) bot.Subjects.Add(new UnitState { Rank = (int)UnitRank.Knight, Position = bot.Home });
            var weak = new KingdomState { Name = "Слабке", Home = bot.Home + new Vector3(500f, 0f, 0f), Hall = 1, HallHealth = 500, HasKing = true, Team = 5, Age = 100f };
            weak.King = new UnitState { Rank = (int)UnitRank.Person, Position = weak.Home };
            weak.EnsureIds();
            var world = new List<KingdomState> { bot, weak };
            for (int h = 0; h < 30; h++) FarWorld.Hour(bot, world, 1f, rng);
            Assert.AreEqual(0, bot.Armies.Count, "a bot only guards");

            // Raided and beaten, the absent player's base still stands
            var raider = new KingdomState { Name = "Набігач", Home = bot.Home + new Vector3(0f, 0f, 400f), Hall = 3, HallHealth = 2000, HasKing = true, Team = 6, Age = 100f };
            raider.King = new UnitState { Rank = (int)UnitRank.General, Position = raider.Home };
            for (int i = 0; i < 200; i++) raider.Subjects.Add(new UnitState { Rank = (int)UnitRank.Knight, Position = raider.Home });
            raider.EnsureIds();
            KingdomState small = GuestBot.FromSave(GuestSave(3, 0), Guest(GuestSave(3, 0)));
            world = new List<KingdomState> { raider, small };
            for (int h = 0; h < 40 && !small.Defeated; h++) { raider.Age += 1f; FarWorld.Hour(raider, world, 1f, rng); }
            Assert.IsFalse(small.Defeated, "its base stands till its player is back");
            Assert.Less(small.Subjects.Count, 3, "but the raid did cost it people");
        }

        [Test]
        public void ThePlayerComesBack_ToItsKingdom_LessWhomTheBotLost()
        {
            SaveData save = GuestSave(4, 2);
            GuestRecord guest = Guest(save);
            KingdomState raised = GuestBot.Raise(guest);
            Assert.IsNotNull(raised);
            Assert.IsTrue(GuestBot.Has("guest-a"));
            Assert.IsNull(GuestBot.Raise(guest), "one bot per player");

            // While away: the 2nd and 5th fell, the 1st was hurt, the hero was hurt
            raised.Subjects.RemoveAll(u => u.Origin == 2 || u.Origin == 5);
            raised.Subjects.Find(u => u.Origin == 1).Health = 10;
            raised.King.Health = 150;

            Assert.IsTrue(GuestBot.Retire(guest));
            Assert.IsFalse(GuestBot.Has("guest-a"), "the bot has gone");
            SaveData back = JsonUtility.FromJson<SaveData>(guest.Save);
            CollectionAssert.AreEqual(new[] { "P0", "P2", "P3", "W1" }, back.Units.ConvertAll(u => u.Name));
            Assert.AreEqual(10, back.Units[0].Health, "hurt as it was hurt");
            Assert.AreEqual(150, back.Player.Health);
            Assert.AreEqual(2, back.Buildings.Count, "its buildings as it left them");
            Assert.IsFalse(GuestBot.Retire(guest), "nothing more to give back");
        }

        [Test]
        public void AHeroFallen_UnderTheBot_ComesBackWithItsPlayer_BarelyAlive()
        {
            GuestRecord guest = Guest(GuestSave(1, 0));
            KingdomState raised = GuestBot.Raise(guest);
            raised.HasKing = false;
            GuestBot.Retire(guest);
            Assert.AreEqual(1, JsonUtility.FromJson<SaveData>(guest.Save).Player.Health);
        }
    }
}
