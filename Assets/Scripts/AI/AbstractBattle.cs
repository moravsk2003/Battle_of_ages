using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.AI
{
    /// <summary>Units of one kind in an army that is only data (docs/GAME_DESIGN.md §6.2): how many, of what rank and gear, how whole.</summary>
    [Serializable]
    public struct UnitGroup
    {
        public int Rank, Gear, Count;
        /// <summary>Share of their full health they have, 0..1.</summary>
        public float Health;

        public UnitGroup(UnitRank rank, int gear, int count, float health = 1f)
        {
            Rank = (int)rank;
            Gear = gear;
            Count = count;
            Health = health;
        }
    }

    /// <summary>What a unit of a rank and gear level can do: the numbers a real unit of an NPC kingdom has with the same kit.</summary>
    public readonly struct UnitStats
    {
        public readonly float Health, Damage, Cooldown, Reduction;

        private UnitStats(float health, float damage, float cooldown, float reduction)
        {
            Health = health;
            Damage = damage;
            Cooldown = cooldown;
            Reduction = reduction;
        }

        public float Dps => Damage / Mathf.Max(0.05f, Cooldown);

        /// <summary>The kit of an NPC kingdom's unit: a person's axe; a soldier's sword, armour and shield of the gear level.</summary>
        public static Item[] Kit(UnitRank rank, int gear)
        {
            // By slot, as Equipment.LoadSlots reads it (in a row, the armour lay in the shield's slot and gave no health)
            var kit = new Item[6];
            void Put(Item item) => kit[(int)item.Slot] = item;
            if (rank == UnitRank.Person) { Put(new Item(ItemType.Axe, 1)); return kit; }
            Put(new Item(ItemType.Sword, gear));
            Put(new Item(ItemType.Armor, gear));
            Put(new Item(ItemType.Shield, gear));
            return kit;
        }

        /// <summary>The same kit for an archer: a bow of the sword's level (a melee weapon's, at least 1) instead, no shield (a bow takes both hands).</summary>
        public static Item[] ArcherKit(Item[] kit)
        {
            var archer = new Item[6];
            int level = 1;
            for (int i = 0; i < 6 && kit != null && i < kit.Length; i++)
            {
                Item item = kit[i];
                if (item.IsEmpty) continue;
                if (item.Slot == EquipSlot.Weapon) { level = Mathf.Max(1, item.Level); continue; }
                if (item.Slot == EquipSlot.Shield) continue;
                archer[(int)item.Slot] = item;
            }
            var bow = new Item(ItemType.Bow, level);
            archer[(int)bow.Slot] = bow;
            return archer;
        }

        /// <summary>The Central Empire's kit (§9.1.1): every piece, of the level its rank has.</summary>
        public static Item[] EmpireKit(UnitRank rank)
        {
            int level = rank == UnitRank.General ? GameBalance.EmpireHeroGear : rank == UnitRank.Person ? GameBalance.EmpirePersonGear : GameBalance.EmpireSoldierGear;
            var kit = new Item[6];
            foreach (ItemType type in new[] { ItemType.Sword, ItemType.Shield, ItemType.Helmet, ItemType.Armor, ItemType.Gloves, ItemType.Boots })
            {
                var item = new Item(type, level);
                kit[(int)item.Slot] = item;
            }
            return kit;
        }

        public static UnitStats Of(UnitRank rank, int gear)
        {
            float health = GameBalance.RankHealth[(int)rank], strength = GameBalance.RankStrength[(int)rank], damage = 1f, cooldown = GameBalance.UnitBaseCooldown, reduction = 0f, healthBonus = 0f;
            foreach (Item it in Kit(rank, gear))
            {
                damage = Mathf.Max(damage, ItemStatCalculator.WeaponDamageMultiplier(it));
                cooldown *= ItemStatCalculator.CooldownMultiplier(it);
                reduction += ItemStatCalculator.DamageReduction(it);
                healthBonus += ItemStatCalculator.HealthBonus(it);
            }
            return new UnitStats(Mathf.Round(health * (1f + healthBonus)), Mathf.Max(1f, Mathf.Round(strength * damage)), cooldown, Mathf.Min(reduction, ItemStatCalculator.MAX_DAMAGE_REDUCTION));
        }
    }

    /// <summary>
    /// A battle of two armies that are only data (§6.2), by Lanchester's law: each side deals its living units' damage per
    /// second to the other, through its armour; as many as can reach fight (a front that widens with the root of the
    /// numbers). The coefficients are not guessed: AbstractBattleCalibration plays real battles and they are fitted to those.
    /// </summary>
    public static class AbstractBattle
    {
        public struct Outcome
        {
            /// <summary>0, 1, or -1: neither fell in the time.</summary>
            public int Winner;
            /// <summary>Share of each side's starting health lost.</summary>
            public float LossA, LossB;
            public float Seconds;
        }

        private sealed class Side
        {
            public List<UnitGroup> Groups;
            public float[] Pool;
            public float[] Unit;
            public float[] Dps;
            public float[] Reduction;
            public float StartHealth;

            public float Health { get { float h = 0f; foreach (float p in Pool) h += p; return h; } }
            public float Units { get { float n = 0f; for (int i = 0; i < Pool.Length; i++) n += Pool[i] / Unit[i]; return n; } }
        }

        private static Side Make(List<UnitGroup> groups)
        {
            var s = new Side { Groups = groups, Pool = new float[groups.Count], Unit = new float[groups.Count], Dps = new float[groups.Count], Reduction = new float[groups.Count] };
            for (int i = 0; i < groups.Count; i++)
            {
                UnitStats st = UnitStats.Of((UnitRank)groups[i].Rank, groups[i].Gear);
                s.Unit[i] = st.Health;
                s.Dps[i] = st.Dps;
                s.Reduction[i] = st.Reduction;
                s.Pool[i] = st.Health * groups[i].Count * groups[i].Health;
                s.StartHealth += s.Pool[i];
            }
            return s;
        }

        /// <summary>How many of a side of this many can strike at once, up against a side of that many.</summary>
        public static float Engaged(float units, float enemies)
        {
            if (units <= 0f) return 0f;
            float front = GameBalance.AbstractFrontBase + GameBalance.AbstractFrontPerRoot * Mathf.Sqrt(Mathf.Max(units, enemies));
            return Mathf.Min(units, front);
        }

        private static void Strike(Side from, Side to, float seconds)
        {
            float units = from.Units, enemies = to.Units;
            if (units <= 0f || enemies <= 0f) return;
            float share = Engaged(units, enemies) / units;
            float dealt = 0f;
            for (int i = 0; i < from.Pool.Length; i++) dealt += (from.Pool[i] / from.Unit[i]) * from.Dps[i];
            dealt *= share * GameBalance.AbstractDamageShare * seconds;
            // It falls on the groups as they stand in the mass, each through its own armour
            float total = to.Health;
            for (int i = 0; i < to.Pool.Length && total > 0f; i++)
                to.Pool[i] = Mathf.Max(0f, to.Pool[i] - dealt * (to.Pool[i] / total) * (1f - to.Reduction[i]));
        }

        /// <summary>Fights it out: no one is changed, the outcome is returned. One step is a second of the battle.</summary>
        public static Outcome Resolve(List<UnitGroup> a, List<UnitGroup> b, float maxSeconds = 600f)
        {
            Side sa = Make(a), sb = Make(b);
            float t = 0f;
            const float step = 0.5f;
            // Death is a whole unit at a time and the last ones count: a side under half a unit is gone
            while (t < maxSeconds && sa.Units > 0.5f && sb.Units > 0.5f)
            {
                var snapshot = (float[])sa.Pool.Clone();
                Strike(sb, sa, step);
                float[] after = sa.Pool;
                sa.Pool = snapshot; // both strike with what they had at the start of the step
                Strike(sa, sb, step);
                sa.Pool = after;
                t += step;
            }
            bool aDown = sa.Units <= 0.5f, bDown = sb.Units <= 0.5f;
            return new Outcome
            {
                Winner = aDown && !bDown ? 1 : bDown && !aDown ? 0 : -1,
                LossA = aDown ? 1f : 1f - sa.Health / Mathf.Max(1f, sa.StartHealth),
                LossB = bDown ? 1f : 1f - sb.Health / Mathf.Max(1f, sb.StartHealth),
                Seconds = t
            };
        }

        /// <summary>A group list from text as the calibration writes it: "Warrior.1x20+Knight.1x5" (rank.gear x count).</summary>
        public static List<UnitGroup> Parse(string text)
        {
            var list = new List<UnitGroup>();
            foreach (string part in text.Split('+'))
            {
                string[] rankRest = part.Split('.');
                string[] gearCount = rankRest[1].Split('x');
                list.Add(new UnitGroup((UnitRank)Enum.Parse(typeof(UnitRank), rankRest[0]), int.Parse(gearCount[0]), int.Parse(gearCount[1])));
            }
            return list;
        }
    }
}
