using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    /// <summary>NPC kings (docs/GAME_DESIGN.md §12, M0): economy, the king's death, raids, the fall, saves.</summary>
    public class NpcKingdomTests
    {
        private readonly List<GameObject> extra = new List<GameObject>();

        [SetUp]
        public void SetUp() { UnitSpatialGrid.Clear(); Random.InitState(7); }

        [TearDown]
        public void TearDown()
        {
            NpcKingdom.RemoveAll();
            foreach (GameObject go in extra) if (go != null) Object.DestroyImmediate(go);
            extra.Clear();
            foreach (HostileMobAI m in Object.FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None)) Object.DestroyImmediate(m.gameObject); // deserters
            UnitSpatialGrid.Clear();
        }

        private static int Rank(HostileMobAI m) => (int)m.GetComponent<UnitProgress>().Rank;

        [Test]
        public void Economy_PeopleWork_HallCostsAndCaps()
        {
            var e = new KingdomEconomy();
            Assert.AreEqual(6, e.Cap, "no hall: the player's cap without a hall");
            e.Tick(2f, 5, 5);
            Assert.AreEqual(5 * 2 * GameBalance.KingdomWoodPerWorkerHour, e.Wood, 1e-3);
            Assert.AreEqual(5 * 2 * GameBalance.KingdomOrePerWorkerHour, e.Ore, 1e-3);
            Assert.IsTrue(e.TryUpgrade(), "hall I: 10 wood, 5 ore");
            Assert.AreEqual(1, e.HallLevel);
            Assert.AreEqual(15, e.Cap);
            Assert.IsFalse(e.TryUpgrade(), "hall II needs 30 wood and 10 ore");
            Assert.AreEqual(0, e.Tick(10f, 0, 15), "no births over the cap");
        }

        [Test]
        public void Found_KingIsAGeneralHero_WithPeople()
        {
            NpcKingdom k = NpcKingdom.Found(new Vector3(100f, 0f, 0f), "Test", 5, 10, HeroClass.Tank);
            Assert.AreEqual(5, k.Population);
            Assert.IsNotNull(k.King);
            Assert.AreEqual((int)UnitRank.General, Rank(k.King));
            Assert.AreEqual(UnitRanks.Health(UnitRank.General, hero: true), k.King.GetComponent<Equipment>().BaseHealth, "x2 base, as the player's hero");
            Assert.AreEqual(HeroClass.Tank, k.King.GetComponent<HeroProgress>().Class);
            Assert.AreEqual(0, k.Economy.HallLevel);
            k.Think();
            Assert.AreEqual(0, k.Economy.HallLevel, "no ore yet");
            k.Economy.Ore = 5;
            k.Think();
            Assert.AreEqual(1, k.Economy.HallLevel, "builds hall I as soon as it can");
        }

        [Test]
        public void KingDies_Interregnum_HalfDesert_StrongestIsCrowned()
        {
            NpcKingdom k = NpcKingdom.Found(Vector3.zero, "Test", 0, 10, HeroClass.Dd);
            for (int i = 0; i < 9; i++) k.AddSubject(UnitRank.Person, k.Spot(), withModel: false);
            k.AddSubject(UnitRank.Knight, k.Spot(), withModel: false);
            HostileMobAI oldKing = k.King;
            oldKing.Health.TakeDamage(100000, ignoreArmor: true);
            Assert.IsTrue(k.InInterregnum);
            Assert.AreEqual(5, k.Population, "half of 10 desert");
            HostileMobAI heir = k.Strongest();
            k.Advance(0f, GameBalance.InterregnumSeconds + 1f);
            Assert.IsFalse(k.InInterregnum);
            Assert.AreSame(heir, k.King, "the strongest one left");
            Assert.AreEqual(1, k.King.GetComponent<HeroProgress>().Level);
            Assert.IsTrue(k.King.GetComponent<UnitProgress>().IsHero);
            Assert.AreEqual(4, k.Population);
        }

        [Test]
        public void NobodyLeft_KingdomFalls()
        {
            NpcKingdom k = NpcKingdom.Found(Vector3.zero, "Test", 0, 10);
            NpcKingdom fallen = null;
            System.Action<NpcKingdom> on = f => fallen = f;
            NpcKingdom.Defeated += on;
            k.King.Health.TakeDamage(100000, ignoreArmor: true);
            k.Advance(0f, GameBalance.InterregnumSeconds + 1f);
            Assert.IsTrue(k.IsDefeated);
            NpcKingdom.Defeated -= on;
            Assert.AreSame(k, fallen);
        }

        [Test]
        public void HallDestroyed_KingdomFalls_KingGoesFree()
        {
            NpcKingdom k = NpcKingdom.Found(Vector3.zero, "Test", 3, 10);
            k.SetHallLevel(1);
            Assert.AreEqual(GameBalance.KingdomHallHealthPerLevel, k.Hall.MaxHealth);
            HostileMobAI king = k.King;
            k.Hall.TakeDamage(100000, ignoreArmor: true);
            Assert.IsTrue(k.IsDefeated);
            Assert.IsNull(king.Kingdom, "a free hero now (§7.3)");
            Assert.IsTrue(king.Health.IsAlive);
        }

        private HealthComponent PlayerUnit(Vector3 at, UnitRank rank, int count = 1)
        {
            HealthComponent first = null;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Guard", typeof(HealthComponent), typeof(CombatUnit));
                go.transform.position = at + Vector3.right * i;
                go.AddComponent<Equipment>();
                UnitProgress.Setup(go, rank);
                HealthComponent hp = go.GetComponent<HealthComponent>();
                hp.InitializeHealth(hp.MaxHealth);
                UnitSpatialGrid.Register(go.GetComponent<CombatUnit>(), hp, UnitSpatialGrid.TeamPlayer, 0.4f);
                extra.Add(go);
                if (first == null) first = hp;
            }
            UnitSpatialGrid.Refresh();
            return first;
        }

        [Test]
        public void Raid_GoesOnlyWhenStrongerThanTheGuards_AndTurnsBackAfterLosses()
        {
            NpcKingdom k = NpcKingdom.Found(Vector3.zero, "Test", 0, 10);
            for (int i = 0; i < 6; i++) k.AddSubject(UnitRank.Warrior, k.Spot(), withModel: false);
            Vector3 base_ = new Vector3(300f, 0f, 0f);
            HealthComponent target = PlayerUnit(base_, UnitRank.Person);
            PlayerUnit(base_ + Vector3.forward * 3f, UnitRank.General, 6);
            Assert.IsFalse(k.ConsiderRaid(target), "six generals guard it");

            foreach (GameObject go in extra) if (go.GetComponent<UnitProgress>().Rank == UnitRank.General) go.GetComponent<HealthComponent>().TakeDamage(100000, true);
            Assert.IsTrue(k.ConsiderRaid(target), "only a person guards it");
            Assert.AreEqual(5, k.Raiders.Count, "4 of 6 warriors (30% stay) and the king");
            Assert.IsTrue(new List<HostileMobAI>(k.Raiders).Contains(k.King));

            int lose = 3;
            foreach (HostileMobAI m in new List<HostileMobAI>(k.Raiders)) if (m != k.King && lose-- > 0) m.Health.TakeDamage(100000, true);
            k.Advance(0f, 1f);
            Assert.IsFalse(k.Raiding, "lost half: back home");
        }

        [Test]
        public void SaveRoundTrip_KeepsHallPeopleAndKing()
        {
            NpcKingdom k = NpcKingdom.Found(new Vector3(50f, 0f, 50f), "Test", 4, 10, HeroClass.Archer);
            k.SetHallLevel(2);
            k.AddSubject(UnitRank.Knight, k.Spot(), withModel: false);
            k.King.GetComponent<HeroProgress>().AddXp(HeroProgress.XpForLevelUp(1) + HeroProgress.XpForLevelUp(2) + 30);
            k.Economy.Wood = 42f;
            SaveLoad.KingdomSave s = JsonUtility.FromJson<SaveLoad.KingdomSave>(JsonUtility.ToJson(k.ToSave()));
            NpcKingdom.RemoveAll();

            NpcKingdom r = NpcKingdom.FromSave(s);
            Assert.AreEqual(2, r.Economy.HallLevel);
            Assert.AreEqual(42f, r.Economy.Wood, 1e-3);
            Assert.AreEqual(5, r.Population);
            Assert.AreEqual(1, new List<HostileMobAI>(r.Subjects).FindAll(m => Rank(m) == (int)UnitRank.Knight).Count);
            HeroProgress hero = r.King.GetComponent<HeroProgress>();
            Assert.AreEqual(3, hero.Level);
            Assert.AreEqual(30, hero.Xp);
            Assert.AreEqual(HeroClass.Archer, hero.Class);
            Assert.AreEqual(0, hero.AttributePoints, "spent again its usual way");
        }

        /// <summary>The tempo of an NPC kingdom on its own (§10.3): people, hall and soldiers over a 60-minute session.</summary>
        [Test]
        public void Tempo_OneHourSession()
        {
            NpcKingdom k = NpcKingdom.Found(Vector3.zero, "Test");
            var log = new System.Text.StringBuilder("game hour: people / hall / soldiers\n");
            for (int h = 1; h <= 60; h++)
            {
                for (int s = 0; s < 10; s++) k.Advance(0.1f, GameBalance.GameHourSeconds * 0.1f);
                if (h % 10 == 0)
                {
                    int soldiers = new List<HostileMobAI>(k.Subjects).FindAll(m => Rank(m) > 0).Count;
                    log.AppendLine($"{h}: {k.Population} / {k.Economy.HallLevel} / {soldiers}");
                }
            }
            Debug.Log(log.ToString());
            Assert.GreaterOrEqual(k.Economy.HallLevel, 3, log.ToString());
            Assert.GreaterOrEqual(k.Population, 25, log.ToString());
        }
    }
}
