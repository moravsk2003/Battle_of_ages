using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// A small battle on open ground for the balance tools: two sides of real units (ranks, hero classes, wind-ups,
    /// recovery) driven by a simple bot each. A brawler walks up and keeps swinging; a kiter strikes or shoots when its
    /// weapon is ready and backs off from the enemy while reloading. No pathing or crowd separation: everyone can reach.
    /// </summary>
    internal sealed class BattleSim
    {
        public sealed class Unit
        {
            public CombatUnit Combat;
            public HealthComponent Hp;
            public int Side;
            public bool Kite;
        }

        public struct Result
        {
            public int Winner;          // 0 or 1; -1: time ran out
            public float Seconds;
            public float[] HpLeft;      // share of each side's starting HP still standing
            public int[] Alive, Count;
        }

        public const float SideDistance = 30f, Spacing = 2f;
        private readonly List<Unit> units = new List<Unit>();
        private readonly List<GameObject> spawned = new List<GameObject>();
        private readonly List<int> gridIds = new List<int>();

        /// <summary>Real units of a rank; a hero gets its class and a weapon (the archer a bow, others a sword I).</summary>
        public Unit Add(int side, UnitRank rank, bool hero = false, HeroClass cls = HeroClass.None, bool kite = false, bool weapon = false)
        {
            int index = units.FindAll(u => u.Side == side).Count;
            var go = new GameObject(side == 0 ? "SideA" : "SideB", typeof(HealthComponent), typeof(CombatUnit));
            // Ranks of ten, the front rank SideDistance from the enemy's
            float x = (index % 10 - 4.5f) * Spacing, back = (index / 10) * Spacing;
            go.transform.position = new Vector3(x, 0f, side == 0 ? -SideDistance * 0.5f - back : SideDistance * 0.5f + back);
            Equipment gear = go.AddComponent<Equipment>();
            UnitProgress.Setup(go, rank, hero);
            if (hero)
            {
                HeroProgress progress = go.AddComponent<HeroProgress>();
                if (cls != HeroClass.None) progress.ChooseClass(cls);
            }
            if (weapon || hero) gear.Equip(new Item(cls == HeroClass.Archer ? ItemType.Bow : ItemType.Sword, 1));
            HealthComponent hp = go.GetComponent<HealthComponent>();
            hp.InitializeHealth(hp.MaxHealth);
            spawned.Add(go);
            var unit = new Unit { Combat = go.GetComponent<CombatUnit>(), Hp = hp, Side = side, Kite = kite };
            // In the unit grid like real units, so the mage's bolts can fly on to the next enemy
            unit.Combat.Team = side == 0 ? UnitSpatialGrid.TeamPlayer : UnitSpatialGrid.TeamHostile;
            gridIds.Add(UnitSpatialGrid.Register(unit.Combat, hp, unit.Combat.Team, 0.4f));
            units.Add(unit);
            return unit;
        }

        /// <summary>Plays until one side is gone or the time is up (game seconds; run with a raised Time.timeScale).</summary>
        public IEnumerator Run(float maxSeconds, Action<Result> done)
        {
            yield return null;
            var startHp = new float[2];
            var count = new int[2];
            foreach (Unit u in units) { startHp[u.Side] += u.Hp.MaxHealth; count[u.Side]++; }
            float t = 0f;
            while (t < maxSeconds && Alive(0) > 0 && Alive(1) > 0)
            {
                foreach (Unit u in units) if (u.Hp.IsAlive) Step(u);
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(1f); // let swings and bolts on their way land
            var left = new float[2];
            foreach (Unit u in units) left[u.Side] += u.Hp.CurrentHealth;
            int a = Alive(0), b = Alive(1);
            done(new Result
            {
                Winner = a > 0 && b == 0 ? 0 : b > 0 && a == 0 ? 1 : -1,
                Seconds = t,
                HpLeft = new[] { left[0] / Mathf.Max(1f, startHp[0]), left[1] / Mathf.Max(1f, startHp[1]) },
                Alive = new[] { a, b },
                Count = count
            });
            Clear();
        }

        public void Clear()
        {
            foreach (GameObject g in spawned) if (g != null) UnityEngine.Object.Destroy(g);
            foreach (MageBolt b in UnityEngine.Object.FindObjectsByType<MageBolt>(FindObjectsSortMode.None)) UnityEngine.Object.Destroy(b.gameObject);
            PendingHits.FlushAll();
            foreach (int id in gridIds) UnitSpatialGrid.Unregister(id);
            gridIds.Clear();
            spawned.Clear();
            units.Clear();
        }

        private int Alive(int side) => units.FindAll(u => u.Side == side && u.Hp.IsAlive).Count;

        /// <summary>
        /// Weapon ready: strike or shoot if the target is in reach, else close in. Reloading: a kiter backs off from the
        /// enemies close to it; a brawler stays at its target.
        /// </summary>
        private void Step(Unit me)
        {
            Unit target = Nearest(me, out float d);
            if (target == null) return;
            CombatUnit c = me.Combat;
            if (c.CanAttack)
            {
                if (d <= c.AttackRange + 0.5f) c.TryAttack(target.Hp);
                else Move(c, target.Combat.transform.position - c.transform.position);
                return;
            }
            if (me.Kite)
            {
                Vector3 away = Vector3.zero;
                foreach (Unit e in units)
                {
                    if (e.Side == me.Side || !e.Hp.IsAlive) continue;
                    Vector3 off = c.transform.position - e.Combat.transform.position;
                    if (off.magnitude < e.Combat.AttackRange + 3f) away += off.normalized;
                }
                if (away != Vector3.zero) Move(c, away);
            }
            else if (d > c.AttackRange * 0.9f) Move(c, target.Combat.transform.position - c.transform.position);
        }

        private Unit Nearest(Unit me, out float distance)
        {
            Unit best = null;
            distance = float.MaxValue;
            foreach (Unit u in units)
            {
                if (u.Side == me.Side || !u.Hp.IsAlive) continue;
                float d = Vector3.Distance(me.Combat.transform.position, u.Combat.transform.position);
                if (d < distance) { distance = d; best = u; }
            }
            return best;
        }

        private static void Move(CombatUnit c, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return;
            c.transform.position += dir.normalized * c.MoveSpeed * BattleFront.Factor(c.Team, dir) * Time.deltaTime; // the pull of a battle, as in the game
        }
    }
}
