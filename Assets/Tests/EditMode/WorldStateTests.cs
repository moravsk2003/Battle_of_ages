using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>M6 step 2: a kingdom as data, with ids that last; old saves come over whole.</summary>
    public class WorldStateTests
    {
        [SetUp]
        public void Setup() => WorldSettings.Set(2024, WorldSettings.DefaultSize);

        [TearDown]
        public void Teardown()
        {
            NpcKingdom.RemoveAll();
            WorldSettings.Set(0, 0f);
        }

        private static KingdomState NewKingdom(string name) =>
            KingdomRealm.NewState(new Vector3(1500f, 0f, -1200f), name, false, 5, new System.Random(3));

        [Test]
        public void ANewKingdom_HasItsParts_EachWithItsOwnNumber()
        {
            KingdomState s = NewKingdom("Числа");
            var ids = new HashSet<int> { s.Id.Value, s.Hero.Id.Value, s.Capital.Id.Value, s.Capital.Garrison.Id.Value };
            Assert.AreEqual(4, ids.Count, "all different");
            Assert.IsFalse(ids.Contains(0), "none without one");
            Assert.AreEqual(s.Id, s.Hero.KingdomId);
            Assert.AreEqual(s.Id, s.Capital.KingdomId);
            Assert.AreEqual(s.Id, s.Capital.Garrison.KingdomId);
            Assert.AreEqual(s.Hero.Id, s.Capital.Garrison.OwnerHeroId);
            Assert.Greater(s.Subjects.Count, 0);
            Assert.IsTrue(s.HasKing);
            Assert.AreEqual(s.Home, s.Capital.Position);
        }

        [Test]
        public void TwoKingdoms_DoNotShareANumber()
        {
            KingdomState a = NewKingdom("Перше"), b = NewKingdom("Друге");
            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreNotEqual(a.Hero.Id, b.Hero.Id);
            Assert.AreNotEqual(a.Capital.Id, b.Capital.Id);
        }

        [Test]
        public void ASaveOfTheOldShape_ComesOverWhole()
        {
            // As 0.2.11 wrote it
            const string old = "{\"Version\":1,\"KingdomsFounded\":true,\"Kingdoms\":[{\"Name\":\"Старе\",\"Home\":{\"x\":10.0,\"y\":2.0,\"z\":30.0},\"Hall\":3,\"HallHealth\":2500," +
                               "\"Wood\":40.5,\"Ore\":7.0,\"Births\":0.25,\"Age\":12.0,\"Interregnum\":0.0,\"Defeated\":false,\"Free\":false,\"Empire\":true,\"Team\":7," +
                               "\"King\":{\"Rank\":3,\"BattleXp\":0,\"Health\":900,\"Position\":{\"x\":11.0,\"y\":2.0,\"z\":31.0}},\"HasKing\":true,\"KingClass\":3,\"KingTotalXp\":450," +
                               "\"Subjects\":[{\"Rank\":1,\"BattleXp\":12,\"Health\":80,\"Position\":{\"x\":12.0,\"y\":2.0,\"z\":32.0}}]}]}";
            SaveData d = JsonUtility.FromJson<SaveData>(old);
            Assert.AreEqual(1, d.Version);
            SaveSystem.Migrate(d);

            Assert.AreEqual(SaveData.CurrentVersion, d.Version);
            Assert.AreEqual(1, d.KingdomStates.Count);
            KingdomState s = d.KingdomStates[0];
            Assert.AreEqual("Старе", s.Name);
            Assert.AreEqual(new Vector3(10f, 2f, 30f), s.Home);
            Assert.AreEqual(3, s.Hall);
            Assert.AreEqual(2500, s.HallHealth);
            Assert.AreEqual(40.5f, s.Wood);
            Assert.AreEqual(7f, s.Ore);
            Assert.AreEqual(0.25f, s.Births);
            Assert.AreEqual(12f, s.Age);
            Assert.IsTrue(s.Empire);
            Assert.AreEqual(7, s.Team);
            Assert.IsTrue(s.HasKing);
            Assert.AreEqual(3, s.KingClass);
            Assert.AreEqual(450, s.KingTotalXp);
            Assert.AreEqual(900, s.King.Health);
            Assert.AreEqual(1, s.Subjects.Count);
            Assert.AreEqual(12, s.Subjects[0].BattleXp);
            Assert.IsFalse(s.Id.IsNone, "numbered on the way");
        }

        [Test]
        public void AStateThroughJson_IsTheSame_IdsAndAll()
        {
            KingdomState s = NewKingdom("Туди й назад");
            s.Hall = 2;
            s.Wood = 123f;
            KingdomState back = JsonUtility.FromJson<KingdomState>(JsonUtility.ToJson(s));
            Assert.AreEqual(s.Id, back.Id);
            Assert.AreEqual(s.Hero.Id, back.Hero.Id);
            Assert.AreEqual(s.Capital.Id, back.Capital.Id);
            Assert.AreEqual(s.Capital.Garrison.Id, back.Capital.Garrison.Id);
            Assert.AreEqual(2, back.Hall);
            Assert.AreEqual(123f, back.Wood);
            Assert.AreEqual(s.Subjects.Count, back.Subjects.Count);
        }

        [Test]
        public void TurningReal_AndBack_KeepsTheNumbers()
        {
            KingdomState s = NewKingdom("Справжнє");
            NpcKingdom real = NpcKingdom.FromState(JsonUtility.FromJson<KingdomState>(JsonUtility.ToJson(s)));
            KingdomState back = real.ToState();
            Assert.AreEqual(s.Id, back.Id);
            Assert.AreEqual(s.Hero.Id, back.Hero.Id);
            Assert.AreEqual(s.Capital.Id, back.Capital.Id);
            Assert.AreEqual(s.Capital.Garrison.Id, back.Capital.Garrison.Id);
            Assert.AreEqual(s.Subjects.Count, back.Subjects.Count, "its people too");
        }

        [Test]
        public void ANewKing_IsANewHero_OfTheSameKingdom()
        {
            KingdomState s = NewKingdom("Спадок");
            WorldId first = s.Hero.Id, kingdom = s.Id;
            s.HasKing = false;
            s.Interregnum = 0.001f;
            KingdomRealm.Advance(s, 1f);
            Assert.IsTrue(s.HasKing, "someone reigns");
            Assert.AreNotEqual(first, s.Hero.Id, "another person");
            Assert.AreEqual(kingdom, s.Id);
            Assert.AreEqual(s.Hero.Id, s.Capital.Garrison.OwnerHeroId, "its garrison answers to the new one");
        }

        [Test]
        public void Ids_GoOnAfterALoad_NoneGivenTwice()
        {
            WorldIds.Reset(500);
            Assert.AreEqual(500, WorldIds.New().Value);
            var seen = new KingdomState { Id = new WorldId { Value = 900 } };
            seen.EnsureIds();
            Assert.Greater(WorldIds.Next, 900, "a number already in use is not given out");
            Assert.AreEqual(900, seen.Id.Value, "and it keeps its own");
        }
    }
}
