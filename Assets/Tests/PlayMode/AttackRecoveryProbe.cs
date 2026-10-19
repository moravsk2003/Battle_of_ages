using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// Balance tool (not a regular test): how much hit-and-run a hero gets away with for each attack recovery
    /// (docs/GAME_DESIGN.md §11.1). A hero general plays kiting against a pack on open ground: the archer and the mage
    /// shoot and back off, the swordsman strikes and backs off until its next swing (compared with standing and
    /// fighting). Every value of the ranged root (0..3 s) and the melee slow (0..2 s) is played; writes
    /// attack_recovery.md to CAPTURE_DIR.
    /// </summary>
    [Explicit("Balance tool, run on demand")]
    public class AttackRecoveryProbe
    {
        private const float MaxSeconds = 120f;
        private readonly List<GameObject> spawned = new List<GameObject>();

        private struct Result { public bool heroWon; public float heroHpLost, seconds; public int killed, enemies; }

        [UnityTest]
        public IEnumerator PlayHitAndRun()
        {
            LogAssert.ignoreFailingMessages = true;
            string dir = Environment.GetEnvironmentVariable("CAPTURE_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(dir);
            float rootWas = GameBalance.RangedRootSeconds, slowWas = GameBalance.MeleeSlowSeconds;
            Time.timeScale = 10f;
            Time.maximumDeltaTime = 0.1f; // small steps at the higher speed
            var report = new StringBuilder("# Відновлення після атаки — симуляція «вдарив — відійшов»\n\n");
            report.AppendLine("Герой-генерал (×2), без очок і артефактів; вороги без спорядження. Відкрите поле, вороги біжать на героя.\n");

            report.AppendLine("## Стрільці: зупинка після пострілу\n");
            report.AppendLine("| Сценарій | зупинка, с | переміг | HP героя втрачено | убито | час, с |\n|---|---|---|---|---|---|");
            foreach (HeroClass c in new[] { HeroClass.Archer, HeroClass.Mage })
                foreach (float root in new[] { 0f, 1f, 2f, 3f })
                {
                    GameBalance.RangedRootSeconds = root;
                    Result r = default;
                    yield return Fight(c, kite: true, UnitRank.General, 3, x => r = x);
                    report.AppendLine(Row($"{HeroClassRules.Name(c)} проти 3 генералів", root, r));
                }

            report.AppendLine("\n## Мечник: сповільнення 50% після удару\n");
            report.AppendLine("| Сценарій | сповільнення, с | переміг | HP героя втрачено | убито | час, с |\n|---|---|---|---|---|---|");
            foreach ((UnitRank rank, int count) in new[] { (UnitRank.General, 2), (UnitRank.Warrior, 6) })
            {
                string pack = rank == UnitRank.General ? $"{count} генералів" : $"{count} воїнів";
                Result stand = default;
                GameBalance.MeleeSlowSeconds = 0f;
                yield return Fight(HeroClass.None, kite: false, rank, count, x => stand = x);
                report.AppendLine(Row($"стоїть і рубається проти {pack}", 0f, stand));
                foreach (float slow in new[] { 0f, 1f, 2f })
                {
                    GameBalance.MeleeSlowSeconds = slow;
                    Result r = default;
                    yield return Fight(HeroClass.None, kite: true, rank, count, x => r = x);
                    report.AppendLine(Row($"вдарив — відійшов проти {pack}", slow, r));
                }
            }

            GameBalance.RangedRootSeconds = rootWas;
            GameBalance.MeleeSlowSeconds = slowWas;
            Time.timeScale = 1f;
            Time.maximumDeltaTime = 1f / 3f;
            File.WriteAllText(Path.Combine(dir, "attack_recovery.md"), report.ToString(), new UTF8Encoding(true));
        }

        private static string Row(string name, float value, Result r) =>
            $"| {name} | {value:0} | {(r.heroWon ? "так" : "ні")} | {r.heroHpLost * 100f:0}% | {r.killed}/{r.enemies} | {r.seconds:0} |";

        // ------------------------------------------------------------------ one fight

        private IEnumerator Fight(HeroClass cls, bool kite, UnitRank enemyRank, int count, Action<Result> done)
        {
            CombatUnit hero = MakeUnit("Hero", UnitRank.General, true, Vector3.zero);
            if (cls != HeroClass.None) hero.GetComponent<HeroProgress>().ChooseClass(cls);
            hero.GetComponent<Equipment>().Equip(new Item(cls == HeroClass.Archer ? ItemType.Bow : ItemType.Sword, 1));
            HealthComponent heroHp = hero.GetComponent<HealthComponent>();
            heroHp.InitializeHealth(heroHp.MaxHealth);

            var enemies = new List<CombatUnit>();
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                enemies.Add(MakeUnit("Enemy", enemyRank, false, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 30f));
            }
            yield return null;

            float t = 0f;
            while (t < MaxSeconds && heroHp.IsAlive && enemies.Exists(e => e.GetComponent<HealthComponent>().IsAlive))
            {
                StepHero(hero, enemies, kite);
                foreach (CombatUnit e in enemies) StepEnemy(e, hero);
                t += Time.deltaTime; // game time (sped up): the cooldowns and recoveries run on it
                yield return null;
            }

            int killed = enemies.FindAll(e => !e.GetComponent<HealthComponent>().IsAlive).Count;
            done(new Result
            {
                heroWon = heroHp.IsAlive && killed == count,
                heroHpLost = 1f - (float)heroHp.CurrentHealth / heroHp.MaxHealth,
                seconds = t, killed = killed, enemies = count
            });
            foreach (GameObject g in spawned) if (g != null) UnityEngine.Object.Destroy(g);
            foreach (MageBolt b in UnityEngine.Object.FindObjectsByType<MageBolt>(FindObjectsSortMode.None)) UnityEngine.Object.Destroy(b.gameObject);
            spawned.Clear();
            yield return null;
        }

        /// <summary>
        /// Weapon ready: strike or shoot if the target is in reach, else close in. Reloading: a kiter backs off from the
        /// pack while any enemy is close; a brawler stays at its target.
        /// </summary>
        private static void StepHero(CombatUnit hero, List<CombatUnit> enemies, bool kite)
        {
            CombatUnit target = Nearest(hero, enemies, out float d);
            if (target == null) return;
            HealthComponent hp = target.GetComponent<HealthComponent>();
            if (hero.CanAttack)
            {
                // Weapon ready: strike or shoot if in reach, else close in
                if (d <= hero.AttackRange + 0.5f) hero.TryAttack(hp);
                else Move(hero, (target.transform.position - hero.transform.position).normalized);
                return;
            }

            Vector3 away = Vector3.zero;
            foreach (CombatUnit e in enemies)
            {
                if (!e.GetComponent<HealthComponent>().IsAlive) continue;
                Vector3 off = hero.transform.position - e.transform.position;
                if (off.magnitude < e.AttackRange + 3f) away += off.normalized;
            }
            // Reloading: the kiter backs off from the pack, the brawler stays at the target
            if (kite && away != Vector3.zero) Move(hero, away.normalized);
            else if (!kite && d > hero.AttackRange * 0.9f) Move(hero, (target.transform.position - hero.transform.position).normalized);
        }

        private static void StepEnemy(CombatUnit e, CombatUnit hero)
        {
            if (!e.GetComponent<HealthComponent>().IsAlive) return;
            Vector3 to = hero.transform.position - e.transform.position;
            if (to.magnitude <= e.AttackRange) { e.TryAttack(hero.GetComponent<HealthComponent>()); return; }
            Move(e, to.normalized);
        }

        private static void Move(CombatUnit u, Vector3 dir)
        {
            dir.y = 0f;
            u.transform.position += dir * u.MoveSpeed * Time.deltaTime;
        }

        private static CombatUnit Nearest(CombatUnit from, List<CombatUnit> list, out float distance)
        {
            CombatUnit best = null; distance = float.MaxValue;
            foreach (CombatUnit u in list)
            {
                if (!u.GetComponent<HealthComponent>().IsAlive) continue;
                float d = Vector3.Distance(from.transform.position, u.transform.position);
                if (d < distance) { distance = d; best = u; }
            }
            return best;
        }

        private CombatUnit MakeUnit(string name, UnitRank rank, bool hero, Vector3 position)
        {
            var go = new GameObject(name, typeof(HealthComponent), typeof(CombatUnit));
            go.transform.position = position;
            go.AddComponent<Equipment>();
            UnitProgress.Setup(go, rank, hero);
            if (hero) go.AddComponent<HeroProgress>();
            HealthComponent hp = go.GetComponent<HealthComponent>();
            hp.InitializeHealth(hp.MaxHealth);
            spawned.Add(go);
            return go.GetComponent<CombatUnit>();
        }
    }
}
