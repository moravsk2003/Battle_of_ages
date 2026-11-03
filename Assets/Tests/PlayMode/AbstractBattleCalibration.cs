using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// The real battles the abstract one is fitted to (docs/GAME_DESIGN.md §6.2): units of two NPC kingdoms, as they fight
    /// in the game, on open ground. Writes Assets/Tests/EditMode/AbstractBattleData.csv (or CALIBRATION_OUT) for
    /// AbstractBattleTests. Run it again after any change of the combat balance.
    /// </summary>
    public class AbstractBattleCalibration
    {
        private static readonly Vector3 Far = new Vector3(3000f, 0f, 3000f);
        private const float Apart = 26f, Spacing = 1.6f, MaxSeconds = 240f, Speed = 4f;

        /// <summary>What is played, each so many times: side A, side B.</summary>
        private static readonly string[] Battles =
        {
            "Person.1x10|Person.1x10", "Person.1x20|Person.1x10", "Person.1x40|Person.1x20",
            "Warrior.1x10|Warrior.1x10", "Warrior.1x20|Warrior.1x10", "Warrior.1x30|Warrior.1x15", "Warrior.1x30|Warrior.1x20",
            "Warrior.1x50|Warrior.1x50", "Warrior.1x100|Warrior.1x60", "Warrior.1x100|Warrior.1x100", "Warrior.1x200|Warrior.1x200", "Warrior.1x200|Warrior.1x120",
            "Warrior.1x10|Person.1x30", "Warrior.1x20|Person.1x50", "Warrior.1x20|Person.1x100",
            "Knight.1x5|Warrior.1x10", "Knight.1x5|Warrior.1x20", "Knight.1x10|Warrior.1x30", "Knight.1x20|Warrior.1x60",
            "Warrior.3x20|Warrior.1x20", "Warrior.3x20|Warrior.1x30", "Knight.3x10|Warrior.3x25", "Warrior.4x30|Warrior.2x50",
            "Warrior.1x20+Knight.1x5|Warrior.1x40", "Person.1x30+Warrior.1x10|Warrior.1x25", "Knight.2x10+Warrior.2x40|Knight.2x5+Warrior.2x70",
        };

        private readonly List<GameObject> spawned = new List<GameObject>();

        private NpcKingdom Kingdom(string name, int gear, Vector3 home)
        {
            NpcKingdom k = NpcKingdom.FromSave(new KingdomSave { Name = name, Home = home, Hall = gear + 1, Wood = 0f, Team = 0 });
            k.enabled = false; // the battle alone: no births, no raids, no thinking
            spawned.Add(k.gameObject);
            return k;
        }

        private void Spawn(List<UnitGroup> groups, Vector3 front, float away, int team, string name, List<HostileMobAI> into)
        {
            int index = 0;
            foreach (UnitGroup g in groups)
            {
                NpcKingdom k = Kingdom(name + g.Gear + "_" + g.Rank, g.Gear, front + new Vector3(0f, 0f, away * 200f));
                k.TeamForTests = team;
                for (int i = 0; i < g.Count; i++, index++)
                {
                    Vector3 at = front + new Vector3((index % 20 - 9.5f) * Spacing, 0f, away * (index / 20) * Spacing);
                    at.y = 0f;
                    HostileMobAI mob = k.AddSubject((UnitRank)g.Rank, at, withModel: false);
                    mob.SetGuardOrigin(at);
                    spawned.Add(mob.gameObject);
                    into.Add(mob);
                }
            }
        }

        private static int Standing(List<HostileMobAI> side)
        {
            int n = 0;
            foreach (HostileMobAI m in side) if (m != null && m.Health.IsAlive) n++;
            return n;
        }

        private static float HealthOf(List<HostileMobAI> side)
        {
            float h = 0f;
            foreach (HostileMobAI m in side) if (m != null && m.Health.IsAlive) h += m.Health.CurrentHealth;
            return h;
        }

        private static float MaxOf(List<HostileMobAI> side)
        {
            float h = 0f;
            foreach (HostileMobAI m in side) if (m != null) h += m.Health.MaxHealth;
            return h;
        }

        /// <summary>Each unit, while it has no enemy at hand, is sent at the nearest: an army that came to fight.</summary>
        private static void Order(List<HostileMobAI> side, List<HostileMobAI> enemies)
        {
            foreach (HostileMobAI m in side)
            {
                if (m == null || !m.Health.IsAlive || (m.CurrentCombatTarget != null && m.CurrentCombatTarget.IsAlive)) continue;
                HostileMobAI best = null;
                float bestSqr = float.MaxValue;
                foreach (HostileMobAI e in enemies)
                {
                    if (e == null || !e.Health.IsAlive) continue;
                    float d = (e.transform.position - m.transform.position).sqrMagnitude;
                    if (d < bestSqr) { bestSqr = d; best = e; }
                }
                if (best != null) m.OrderAttack(best.Health);
            }
        }

        private void Clear()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            NpcKingdom.RemoveAll();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheNumbers_AreThoseOfARealUnit()
        {
            WorldSettings.Set(0, 0f);
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(100f, 1f, 100f);
            spawned.Add(ground);
            foreach (UnitRank rank in new[] { UnitRank.Person, UnitRank.Warrior, UnitRank.Knight })
                foreach (int gear in new[] { 1, 3, 4 })
                {
                    NpcKingdom k = Kingdom("Stats" + gear + rank, gear, Far);
                    HostileMobAI mob = k.AddSubject(rank, Far, withModel: false);
                    spawned.Add(mob.gameObject);
                    yield return null;
                    UnitStats st = UnitStats.Of(rank, gear);
                    string at = $"{rank} gear {gear}: real hp {mob.Health.MaxHealth} dmg {mob.Combat.AttackDamage} cd {mob.Combat.AttackCooldown} red {mob.Health.DamageReduction}; sum hp {st.Health} dmg {st.Damage} cd {st.Cooldown} red {st.Reduction}: ";
                    Debug.Log("[Stats] " + at);
                    Assert.AreEqual(mob.Health.MaxHealth, st.Health, 1f, at + "health");
                    Assert.AreEqual(mob.Combat.AttackDamage, st.Damage, 1f, at + "damage");
                    Assert.AreEqual(mob.Combat.AttackCooldown, st.Cooldown, 0.02f, at + "cooldown");
                    Assert.AreEqual(mob.Health.DamageReduction, st.Reduction, 0.005f, at + "armour");
                }
            Clear();
        }

        [UnityTest, Timeout(3000000), Explicit("Plays real battles, run on demand")]
        public IEnumerator PlayTheBattles()
        {
            string path = System.Environment.GetEnvironmentVariable("CALIBRATION_OUT");
            if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.dataPath, "Tests/EditMode/AbstractBattleData.csv");
            int repeats = int.TryParse(System.Environment.GetEnvironmentVariable("CALIBRATION_REPEATS"), out int r) ? r : 2;
            LogAssert.ignoreFailingMessages = true;
            WorldSettings.Set(0, 0f);
            var lines = new List<string> { "a;b;lossA;lossB;aliveA;aliveB;seconds" };
            foreach (string battle in Battles)
                for (int run = 0; run < repeats; run++)
                {
                    string[] sides = battle.Split('|');
                    var ground = new GameObject("Ground");
                    BoxCollider box = ground.AddComponent<BoxCollider>();
                    box.center = new Vector3(Far.x, -0.5f, Far.z);
                    box.size = new Vector3(600f, 1f, 600f);
                    spawned.Add(ground);
                    var a = new List<HostileMobAI>();
                    var b = new List<HostileMobAI>();
                    Spawn(AbstractBattle.Parse(sides[0]), Far + new Vector3(0f, 0f, -Apart * 0.5f), -1f, UnitSpatialGrid.TeamKingdomFirst, "A", a);
                    Spawn(AbstractBattle.Parse(sides[1]), Far + new Vector3(0f, 0f, Apart * 0.5f), 1f, UnitSpatialGrid.TeamKingdomFirst + 1, "B", b);
                    yield return null;
                    float maxA = MaxOf(a), maxB = MaxOf(b), start = Time.time, order = 0f;
                    float lastA = maxA, lastB = maxB, lastChange = start;
                    Time.timeScale = Speed;
                    while (Time.time - start < MaxSeconds && Standing(a) > 0 && Standing(b) > 0 && Time.time - lastChange < 20f)
                    {
                        if (Time.time >= order)
                        {
                            order = Time.time + 1f;
                            Order(a, b);
                            Order(b, a);
                            float ha = HealthOf(a), hb = HealthOf(b);
                            if (ha < lastA - 0.5f || hb < lastB - 0.5f) lastChange = Time.time;
                            lastA = ha; lastB = hb;
                        }
                        yield return null;
                    }
                    Time.timeScale = 1f;
                    yield return new WaitForSeconds(0.5f);
                    CultureInfo inv = CultureInfo.InvariantCulture;
                    lines.Add(string.Format(inv, "{0};{1};{2:0.000};{3:0.000};{4};{5};{6:0.0}", sides[0], sides[1], 1f - HealthOf(a) / Mathf.Max(1f, maxA), 1f - HealthOf(b) / Mathf.Max(1f, maxB), Standing(a), Standing(b), Time.time - start));
                    File.WriteAllLines(path, lines);
                    Clear();
                    yield return null;
                }
        }
    }
}
