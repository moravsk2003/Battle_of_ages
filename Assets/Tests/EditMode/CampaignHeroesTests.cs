using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>A campaign's heroes (M1 steps 4-5): their start sites, their starting kit, and far kingdoms growing by formula.</summary>
    [TestFixture]
    public class CampaignHeroesTests
    {
        [TearDown]
        public void TearDown() => WorldSettings.Set(0, 0f);

        [Test]
        public void TheSites_AreGoodPlaces_ApartFromEachOtherAndTheBase()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            List<Vector3> sites = StartSites.Choose(15);
            Assert.AreEqual(15, sites.Count, "room for fifteen heroes");
            CoarseWorldMap map = CoarseWorldMap.Current;
            float minApart = float.MaxValue;
            int byWater = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                Vector3 p = sites[i];
                Assert.Greater(new Vector2(p.x, p.z).magnitude, StartSites.BaseClearance, "not by the player's base");
                Assert.IsTrue(map.TryCell(p, out int x, out int z) && map.Habitable(x, z), "on land one can live on");
                Assert.IsFalse(Lakes.InWater(p.x, p.z, 20f));
                if (StartSites.ShoreDistance(p) < StartSites.WaterFar) byWater++;
                for (int j = 0; j < i; j++) minApart = Mathf.Min(minApart, Vector3.Distance(p, sites[j]));
            }
            TestContext.WriteLine($"15 sites: at least {minApart:0} m apart (spacing {StartSites.Spacing(15):0}), {byWater} by a lake");
            Assert.Greater(minApart, 700f, "room to grow");
            Assert.Greater(byWater, 5, "many by a lake");
            Assert.AreEqual(sites, StartSites.Choose(15), "the same for the seed");
        }

        /// <summary>The author found every kingdom far off (1.5 km round the base at least): fifty fit a 4 km world, the nearest close by.</summary>
        [Test]
        public void FiftyKingdoms_FitASmallWorld_TheNearestCloseBy()
        {
            WorldSettings.Set(2024, CampaignSetup.FirstSize);
            List<Vector3> sites = StartSites.Choose(50);
            float nearest = float.MaxValue, minApart = float.MaxValue;
            for (int i = 0; i < sites.Count; i++)
            {
                nearest = Mathf.Min(nearest, new Vector2(sites[i].x, sites[i].z).magnitude);
                for (int j = 0; j < i; j++) minApart = Mathf.Min(minApart, Vector3.Distance(sites[i], sites[j]));
                Assert.IsTrue(WorldSettings.Inside(sites[i], 0f), "inside the mountains");
            }
            TestContext.WriteLine($"{sites.Count} sites in 4 km: the nearest {nearest:0} m from the base, at least {minApart:0} m apart (spacing {StartSites.Spacing(50):0})");
            Assert.AreEqual(50, sites.Count, "room for fifty");
            Assert.Greater(nearest, StartSites.BaseClearance, "not at the door");
            Assert.Less(nearest, 1000f, "but near");
            Assert.Greater(minApart, 150f, "each a town's room");
        }

        [Test]
        public void AHeroStarts_WithFivePeopleAndSomeWood_NoHall()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            KingdomState s = KingdomRealm.NewState(new Vector3(3000f, 0f, -2000f), "Тест", false, 5, new System.Random(1));
            Assert.IsTrue(s.HasKing);
            Assert.AreEqual((int)UnitRank.General, s.King.Rank, "the hero is the strongest kind");
            Assert.AreEqual(GameBalance.KingdomStartPeople, s.Subjects.Count);
            Assert.AreEqual(GameBalance.KingdomStartWood, s.Wood);
            Assert.AreEqual(0, s.Hall);
        }

        [Test]
        public void AFarKingdom_GrowsByFormula_AFreeHeroBuildsNoHall()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            KingdomState king = KingdomRealm.NewState(new Vector3(3000f, 0f, -2000f), "Король", false, 5, new System.Random(1));
            KingdomState free = KingdomRealm.NewState(new Vector3(-3000f, 0f, 2000f), "Ватага", true, 6, new System.Random(2));
            var rows = new System.Text.StringBuilder();
            for (int h = 1; h <= 72; h++)
            {
                KingdomRealm.Advance(king, 1f);
                KingdomRealm.Advance(free, 1f);
                if (h % 12 == 0) rows.Append($" | {h} h: king {king.Subjects.Count} people, hall {king.Hall}; free {free.Subjects.Count}");
            }
            TestContext.WriteLine("far growth" + rows);
            Assert.Greater(king.Hall, 0, "a king raises a hall");
            Assert.Greater(king.Subjects.Count, GameBalance.KingdomStartPeople, "and grows");
            Assert.AreEqual(0, free.Hall, "a free hero camps");
            Assert.LessOrEqual(free.Subjects.Count, Building.TownHall.CapForLevel(0), "up to a camp's cap");
            int warriors = 0;
            foreach (UnitState u in king.Subjects) if (u.Rank >= (int)UnitRank.Warrior) warriors++;
            TestContext.WriteLine($"warriors after 3 days: {warriors}");
        }

        [Test]
        public void AFarInterregnum_EndsWithTheStrongestReigning()
        {
            WorldSettings.Set(2024, WorldSettings.DefaultSize);
            KingdomState s = KingdomRealm.NewState(new Vector3(3000f, 0f, -2000f), "Без короля", false, 5, new System.Random(1));
            s.HasKing = false;
            s.Interregnum = 30f;
            s.Subjects[2].Rank = (int)UnitRank.Knight;
            KingdomRealm.Advance(s, 1f);
            Assert.IsTrue(s.HasKing, "an hour is longer than the interregnum");
            Assert.AreEqual((int)UnitRank.Knight, s.King.Rank, "the strongest one reigns");
            Assert.AreEqual(GameBalance.KingdomStartPeople - 1, s.Subjects.Count);
        }
    }
}
