using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.Net;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Tests
{
    /// <summary>A guest's save in parts (docs/GAME_DESIGN.md §14.4): whole the first time, then only what changed, and the host puts it together.</summary>
    public class GuestSavePartsTests
    {
        private static SaveData Save(int people, int trees)
        {
            var s = new SaveData { Wood = 40, Title = "Гість", GameHours = 12.5 };
            for (int i = 0; i < people; i++) s.Units.Add(new UnitSave());
            for (int i = 0; i < trees; i++) s.World.Trees.Add(new TreeSave { Key = "tree_" + i, State = 2 });
            s.Zones.AddRange(new[] { 1, 2, 3 });
            return s;
        }

        [Test]
        public void TheFirstSaveGoesWhole_ThenOnlyWhatChanged_AndTheHostHasItAll()
        {
            var host = new GuestSaveParts.Assembly();
            SaveData first = Save(5, 300);
            string firstWhole = JsonUtility.ToJson(first);
            string[] sent = GuestSaveParts.Split(first);
            byte[] message = GuestSaveParts.Encode(6, sent, null);
            Assert.AreEqual((1 << GuestSaveParts.Count) - 1, message[1], "every part the first time");
            Assert.AreEqual(firstWhole, host.Take(message, message.Length));

            // A little later: one more person, the same trees, the clock on
            SaveData second = Save(6, 300);
            second.GameHours = 12.6;
            string secondWhole = JsonUtility.ToJson(second);
            string[] now = GuestSaveParts.Split(second);
            byte[] delta = GuestSaveParts.Encode(6, now, sent);
            Assert.AreEqual((1 << GuestSaveParts.Units) | (1 << GuestSaveParts.Rest), delta[1], "the people and the rest, not the trees");
            Assert.Less(delta.Length, message.Length / 4, "a fraction of the whole");
            Assert.AreEqual(secondWhole, host.Take(delta, delta.Length));

            Assert.IsNull(GuestSaveParts.Encode(6, now, now), "nothing changed: nothing to send");
        }

        [Test]
        public void AHostThatMissedTheWhole_WaitsForIt()
        {
            string[] a = GuestSaveParts.Split(Save(3, 10));
            string[] b = GuestSaveParts.Split(Save(4, 10));
            byte[] delta = GuestSaveParts.Encode(6, b, a);
            Assert.IsNull(new GuestSaveParts.Assembly().Take(delta, delta.Length), "parts alone are not a save");
        }
    }
}
