using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.Tests
{
    /// <summary>M6 step 3: the world out of sight goes on by itself: raids, marches, battles, falls, free heroes on the road.</summary>
    public class FarWorldTests
    {
        private readonly System.Random rng = new System.Random(5);

        [SetUp]
        public void Setup() => WorldSettings.Set(2024, WorldSettings.DefaultSize);

        [TearDown]
        public void Teardown()
        {
            NpcKingdom.RemoveAll();
            WorldSettings.Set(0, 0f);
        }

        private static KingdomState Kingdom(string name, Vector3 at, int hall, int warriors, int people = 0, bool free = false)
        {
            var s = new KingdomState { Name = name, Home = at, Hall = hall, HallHealth = hall > 0 ? GameBalance.TownHallHealth[hall] : 0, Free = free, HasKing = true, Team = 5 };
            s.King = new UnitState { Rank = (int)UnitRank.General, Position = at };
            s.KingClass = (int)HeroClass.Tank;
            for (int i = 0; i < warriors; i++) s.Subjects.Add(new UnitState { Rank = (int)UnitRank.Warrior, Position = at });
            for (int i = 0; i < people; i++) s.Subjects.Add(new UnitState { Rank = (int)UnitRank.Person, Position = at });
            s.Age = 100f; // past its first raid
            return s.EnsureIds();
        }

        private void Run(List<KingdomState> world, int hours)
        {
            for (int h = 0; h < hours; h++)
                foreach (KingdomState s in world)
                {
                    s.Age += 1f;
                    FarWorld.Hour(s, world, 1f, rng);
                }
        }

        [Test]
        public void AStrongKing_SendsARaid_LeavingAGarrison_AtTheNearestItCanBeat()
        {
            KingdomState strong = Kingdom("Сильне", new Vector3(0f, 0f, 3000f), 3, 40);
            KingdomState near = Kingdom("Близьке", new Vector3(0f, 0f, 3800f), 1, 4);
            KingdomState further = Kingdom("Далеке", new Vector3(0f, 0f, 1000f), 1, 4);
            var world = new List<KingdomState> { strong, near, further };

            Assert.IsTrue(FarWorld.ConsiderRaid(strong, world));
            Assert.AreEqual(1, strong.Armies.Count);
            ArmyState army = strong.Armies[0];
            Assert.AreEqual(near.Id, army.TargetKingdomId, "the nearest");
            Assert.AreEqual(12, strong.Subjects.Count, "30% stay home");
            Assert.AreEqual(28, army.Units.Count, "the rest go");
            Assert.IsTrue(army.WithHero);
            Assert.AreEqual(strong.Id, army.KingdomId);
            Assert.IsFalse(army.Id.IsNone);
            Assert.IsFalse(FarWorld.ConsiderRaid(strong, world), "one at a time");
        }

        [Test]
        public void NoRaid_BeforeItsFirstHour_OrAgainstTheStronger()
        {
            KingdomState young = Kingdom("Молоде", new Vector3(0f, 0f, 3000f), 3, 40);
            young.Age = 1f;
            KingdomState other = Kingdom("Інше", new Vector3(0f, 0f, 3800f), 1, 1);
            Assert.IsFalse(FarWorld.ConsiderRaid(young, new List<KingdomState> { young, other }), "too early");

            KingdomState weak = Kingdom("Слабке", new Vector3(0f, 0f, 3000f), 1, 2);
            KingdomState mighty = Kingdom("Могутнє", new Vector3(0f, 0f, 3800f), 3, 40);
            Assert.IsFalse(FarWorld.ConsiderRaid(weak, new List<KingdomState> { weak, mighty }), "it knows better");
        }

        [Test]
        public void ARaid_Marches_ItTakesTime_AndArrives()
        {
            KingdomState attacker = Kingdom("Похід", new Vector3(0f, 0f, 3000f), 3, 40);
            KingdomState target = Kingdom("Ціль", new Vector3(0f, 0f, 3800f), 1, 4);
            var world = new List<KingdomState> { attacker, target };
            FarWorld.ConsiderRaid(attacker, world);
            float start = (attacker.Armies[0].Position - target.Home).magnitude;
            FarWorld.Hour(attacker, world, 1f, rng);
            float after = (attacker.Armies[0].Position - target.Home).magnitude;
            Assert.Less(after, start, "on its way");
            Assert.Greater(start - after, 100f, "at a walking pace of an hour");
            Assert.Less(start - after, 700f, "no faster");
            Assert.AreEqual(4, target.Subjects.Count, "no fight from afar");
        }

        [Test]
        public void AWeakKingdom_Falls_AndTheRaidersComeHome()
        {
            KingdomState attacker = Kingdom("Завойовник", new Vector3(0f, 0f, 3000f), 3, 60);
            KingdomState target = Kingdom("Жертва", new Vector3(0f, 0f, 3600f), 1, 3);
            var world = new List<KingdomState> { attacker, target };
            int startPeople = attacker.Subjects.Count;
            Run(world, 60);
            Assert.IsTrue(target.Defeated, "its people and hall are gone");
            Assert.AreEqual(0, attacker.Armies.Count, "the raid is home");
            Assert.Greater(attacker.Subjects.Count, startPeople * 0.8f, "at small cost");
            foreach (UnitState u in attacker.Subjects) Assert.Less(Vector2.Distance(new Vector2(u.Position.x, u.Position.z), new Vector2(attacker.Home.x, attacker.Home.z)), 15f, "at home");
            Assert.Greater(attacker.LastRaidEnd, 0f);
        }

        [Test]
        public void ADefendedHall_IsNotTouched_AndABeatenRaidGoesBack()
        {
            KingdomState attacker = Kingdom("Сміливець", new Vector3(0f, 0f, 3000f), 1, 10);
            KingdomState target = Kingdom("Фортеця", new Vector3(0f, 0f, 3300f), 3, 30);
            var world = new List<KingdomState> { attacker, target };
            int hall = target.HallHealth;
            // Sent in spite of the numbers (the terms would not let it)
            FarWorld.StartRaid(attacker, new List<UnitState>(attacker.Subjects), target);
            Run(world, 40);
            Assert.IsFalse(target.Defeated);
            Assert.AreEqual(hall, target.HallHealth, "they never got to it");
            Assert.AreEqual(0, attacker.Armies.Count, "over");
            Assert.Less(attacker.Subjects.Count, 10, "and it cost them");
            Assert.Less(target.Subjects.Count, 30, "both sides bleed");
        }

        [Test]
        public void AKingFalling_StartsAnInterregnum_AnEmperorEndsAnEmpire()
        {
            KingdomState attacker = Kingdom("Сила", new Vector3(0f, 0f, 3000f), 5, 200);
            KingdomState king = Kingdom("Королівство", new Vector3(0f, 0f, 3300f), 1, 2, 20);
            var world = new List<KingdomState> { attacker, king };
            attacker.Armies.Clear();
            FarWorld.StartRaid(attacker, new List<UnitState>(attacker.Subjects), king);
            Run(world, 3);
            Assert.IsFalse(king.HasKing, "wiped out, it died");

            KingdomState empire = Kingdom("Імперія", new Vector3(0f, 0f, 3300f), 1, 1);
            empire.Empire = true;
            KingdomState rival = Kingdom("Суперник", new Vector3(0f, 0f, 3000f), 5, 200);
            var other = new List<KingdomState> { rival, empire };
            FarWorld.StartRaid(rival, new List<UnitState>(rival.Subjects), empire);
            Run(other, 3);
            Assert.IsTrue(empire.Defeated, "an empire is its emperor's");
        }

        [Test]
        public void AFreeHero_WalksTheWorld_WithItsBand()
        {
            KingdomState band = Kingdom("Ватага", new Vector3(500f, 0f, 500f), 0, 4, 0, free: true);
            var world = new List<KingdomState> { band };
            Vector3 was = band.Home;
            float furthest = 0f;
            for (int h = 0; h < 80; h++)
            {
                band.Age += 1f;
                FarWorld.Hour(band, world, 1f, rng);
                furthest = Mathf.Max(furthest, (band.Home - was).magnitude);
                Assert.Less(Vector2.Distance(new Vector2(band.Subjects[0].Position.x, band.Subjects[0].Position.z), new Vector2(band.Home.x, band.Home.z)), 5f, "the band goes with it");
                Assert.IsTrue(WorldSettings.Inside(band.Home, 100f), "inside the world");
            }
            Assert.Greater(furthest, GameBalance.FreeHeroWalkMin * 0.5f, "it has gone places");
            Assert.AreEqual(0, band.Armies.Count, "it sends no raids");
        }

        [Test]
        public void AFreeHero_KeepsOutOfThePlayersBase()
        {
            Vector3 home = new Vector3(600f, 0f, 600f);
            KingdomState band = Kingdom("Обережна", home, 0, 1, 0, free: true);
            var world = new List<KingdomState> { band };
            for (int h = 0; h < 200; h++)
            {
                band.Age += 1f;
                FarWorld.Hour(band, world, 1f, rng, keepOutOf: Vector3.zero);
                if (band.Walking) Assert.GreaterOrEqual(new Vector2(band.WalkingTo.x, band.WalkingTo.z).magnitude, GameBalance.FreeHeroKeepOut - 1f);
            }
        }

        [Test]
        public void AFarKing_GoesForThePlayer_IfStrongEnough_AsIfNearer()
        {
            KingdomState king = Kingdom("На гравця", new Vector3(0f, 0f, 3000f), 3, 40);
            KingdomState neighbour = Kingdom("Сусід", new Vector3(0f, 0f, 3800f), 1, 4);
            var world = new List<KingdomState> { king, neighbour };
            var strongPlayer = new FarWorld.PlayerTarget { Position = new Vector3(0f, 0f, 1500f), Guards = FarWorld.Power(king, king.Subjects, true) };
            Assert.IsTrue(FarWorld.ConsiderRaid(king, world, strongPlayer));
            Assert.AreEqual(neighbour.Id, king.Armies[0].TargetKingdomId, "too strong: the neighbour instead");
            Assert.IsFalse(king.Armies[0].TargetsPlayer);

            KingdomState other = Kingdom("Інший", new Vector3(0f, 0f, 3000f), 3, 40);
            KingdomState near = Kingdom("Ближче", new Vector3(0f, 0f, 3900f), 1, 4);
            var player = new FarWorld.PlayerTarget { Position = new Vector3(0f, 0f, 1500f), Guards = 1f }; // 1500 m counts as 750, before 900
            var others = new List<KingdomState> { other, near };
            Assert.IsTrue(FarWorld.ConsiderRaid(other, others, player));
            Assert.IsTrue(other.Armies[0].TargetsPlayer, "the player, though the neighbour is nearer");

            // It marches up to the settlement and waits there for the real fight, losing no one
            int people = other.Armies[0].Units.Count;
            for (int h = 0; h < 40; h++) FarWorld.Hour(other, others, 1f, rng, null, player);
            Assert.AreEqual(1, other.Armies.Count, "still on its raid");
            Assert.AreEqual(people, other.Armies[0].Units.Count);
            Assert.Less((other.Armies[0].Position - player.Position).magnitude, GameBalance.KingdomGuardRadius + 400f, "at the door");
        }

        [Test]
        public void AnArmyOnTheRoad_MarchesOn_ThoughItsTargetIsRealNow()
        {
            KingdomState attacker = Kingdom("Хід", new Vector3(0f, 0f, 3000f), 3, 40);
            KingdomState target = Kingdom("Справжня", new Vector3(0f, 0f, 3800f), 1, 4);
            var world = new List<KingdomState> { attacker, target };
            FarWorld.ConsiderRaid(attacker, world);
            WorldId id = target.Id;
            Vector3 home = target.Home;
            var alone = new List<KingdomState> { attacker }; // it has gone from the far ones
            float start = (attacker.Armies[0].Position - home).magnitude;
            FarWorld.Hour(attacker, alone, 1f, rng, null, null, w => w == id ? home : (Vector3?)null);
            Assert.IsFalse(attacker.Armies[0].Returning);
            Assert.Less((attacker.Armies[0].Position - home).magnitude, start);
            FarWorld.Hour(attacker, alone, 1f, rng);
            for (int h = 0; h < 10 && attacker.Armies.Count > 0; h++)
            {
                Assert.IsTrue(attacker.Armies[0].Returning, "it is gone altogether: home");
                FarWorld.Hour(attacker, alone, 1f, rng);
            }
            Assert.AreEqual(0, attacker.Armies.Count, "and home it came");
            Assert.AreEqual(40, attacker.Subjects.Count, "with everyone");
        }

        [Test]
        public void AnArmyOnTheRoad_IsSaved_AndItsPeopleAreNotLostTurningReal()
        {
            KingdomState attacker = Kingdom("Збережене", new Vector3(0f, 0f, 3000f), 3, 40);
            KingdomState target = Kingdom("Далі", new Vector3(0f, 0f, 3800f), 1, 4);
            var world = new List<KingdomState> { attacker, target };
            FarWorld.ConsiderRaid(attacker, world);
            FarWorld.Hour(attacker, world, 1f, rng);

            KingdomState back = JsonUtility.FromJson<KingdomState>(JsonUtility.ToJson(attacker));
            Assert.AreEqual(1, back.Armies.Count);
            Assert.AreEqual(attacker.Armies[0].TargetKingdomId, back.Armies[0].TargetKingdomId);
            Assert.AreEqual(attacker.Armies[0].Position, back.Armies[0].Position);
            Assert.AreEqual(28, back.Armies[0].Units.Count);

            NpcKingdom real = NpcKingdom.FromState(back);
            Assert.AreEqual(40, real.Population, "the army came back to it");
        }

        [Test]
        public void AnHourOfAFullWorld_FitsTheBudgetPerKingdom()
        {
            var world = new List<KingdomState>();
            for (int i = 0; i < 30; i++)
            {
                float a = i / 30f * Mathf.PI * 2f;
                world.Add(Kingdom("Світ " + i, new Vector3(Mathf.Cos(a) * 3000f, 0f, Mathf.Sin(a) * 3000f), 3, 200, 300));
            }
            Run(world, 5); // armies out, in the worst of it
            var watch = Stopwatch.StartNew();
            double worst = 0;
            foreach (KingdomState s in world)
            {
                double t0 = watch.Elapsed.TotalMilliseconds;
                s.Age += 1f;
                KingdomRealm.Advance(s, 1f);
                FarWorld.Hour(s, world, 1f, rng);
                worst = System.Math.Max(worst, watch.Elapsed.TotalMilliseconds - t0);
            }
            TestContext.WriteLine($"30 kingdoms of 500: {watch.Elapsed.TotalMilliseconds:0.00} ms in all, the slowest {worst:0.00} ms");
            Assert.Less(worst, GameBalance.FarTickBudgetMs * 2f, "one kingdom fits a frame's budget (twice over for the editor's noise)");
        }
    }
}
