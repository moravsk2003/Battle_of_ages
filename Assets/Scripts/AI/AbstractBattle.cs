using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.AI
{
    /// <summary>
    /// Units of one kind in an army that is only data (docs/GAME_DESIGN.md §6.2): how many, of what rank and gear, how whole;
    /// archers (a bow instead of sword and shield), or a hero of a class (one).
    /// </summary>
    [Serializable]
    public struct UnitGroup
    {
        public int Rank, Gear, Count;
        /// <summary>Share of their full health they have, 0..1.</summary>
        public float Health;
        public bool Archers, IsHero;
        /// <summary>A hero's class (HeroClass).</summary>
        public int HeroClass;

        public UnitGroup(UnitRank rank, int gear, int count, float health = 1f, bool archers = false)
        {
            Rank = (int)rank;
            Gear = gear;
            Count = count;
            Health = health;
            Archers = archers;
            IsHero = false;
            HeroClass = 0;
        }

        /// <summary>A hero of a class, as a kingdom crowns its king (NpcKingdom.Crown).</summary>
        public static UnitGroup Hero(UnitRank rank, int gear, Combat.HeroClass cls, float health = 1f) =>
            new UnitGroup(rank, gear, 1, health) { IsHero = true, HeroClass = (int)cls };
    }

    /// <summary>What a unit of a rank and gear level can do: the numbers a real unit of an NPC kingdom has with the same kit.</summary>
    public readonly struct UnitStats
    {
        public readonly float Health, Damage, Cooldown, Reduction;
        /// <summary>Shoots (a bow, a mage's bolts): it strikes from behind the front, and before the sides meet.</summary>
        public readonly bool Ranged;
        /// <summary>A mage's bolts go through armour.</summary>
        public readonly bool PiercesArmour;
        /// <summary>A tank lets every 10th blow by: in a battle its health goes a ninth further.</summary>
        public readonly bool Tank;
        public float EffectiveHealth => Tank ? Health * 10f / 9f : Health;

        private UnitStats(float health, float damage, float cooldown, float reduction, bool ranged = false, bool pierces = false, bool tank = false)
        {
            Tank = tank;
            Health = health;
            Damage = damage;
            Cooldown = cooldown;
            Reduction = reduction;
            Ranged = ranged;
            PiercesArmour = pierces;
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

        public static UnitStats Of(UnitRank rank, int gear) => Of(new UnitGroup(rank, gear, 1));

        /// <summary>
        /// A group's unit as the game makes it: its kit (a bow for archers; a king's sword turned into a bow or a staff by its
        /// class), a hero's doubled numbers and its class's rules (HeroClassRules: health, strength with a bow, speed of blows;
        /// a tank lets every 10th blow by, a mage's bolts pierce armour).
        /// </summary>
        public static UnitStats Of(UnitGroup g)
        {
            var rank = (UnitRank)g.Rank;
            var cls = g.IsHero ? (HeroClass)g.HeroClass : HeroClass.None;
            Item[] kit = Kit(g.IsHero && rank == UnitRank.Person ? UnitRank.Warrior : rank, g.Gear);
            if (g.Archers) kit = ArcherKit(kit);
            if (cls == HeroClass.Archer || cls == HeroClass.Mage)
            {
                // NpcKingdom.Crown: a bow or a staff of the sword's level in the sword's place
                int level = Mathf.Max(1, kit[(int)EquipSlot.Weapon].IsEmpty ? 1 : kit[(int)EquipSlot.Weapon].Level);
                kit[(int)EquipSlot.Weapon] = new Item(cls == HeroClass.Archer ? ItemType.Bow : ItemType.Staff, level);
            }
            bool bow = !kit[(int)EquipSlot.Weapon].IsEmpty && kit[(int)EquipSlot.Weapon].Type == ItemType.Bow;
            float health = UnitRanks.Health(rank, g.IsHero), strength = UnitRanks.Strength(rank, g.IsHero), damage = 1f, cooldown = GameBalance.UnitBaseCooldown, reduction = 0f, healthBonus = 0f;
            foreach (Item it in kit)
            {
                damage = Mathf.Max(damage, ItemStatCalculator.WeaponDamageMultiplier(it));
                cooldown *= ItemStatCalculator.CooldownMultiplier(it);
                reduction += ItemStatCalculator.DamageReduction(it);
                healthBonus += ItemStatCalculator.HealthBonus(it);
            }
            health = Mathf.Round(health * (1f + healthBonus) * HeroClassRules.HealthFactor(cls));
            strength *= HeroClassRules.StrengthFactor(cls, bow);
            cooldown *= HeroClassRules.CooldownFactor(cls, bow);
            bool ranged = bow || cls == HeroClass.Mage;
            return new UnitStats(health, Mathf.Max(1f, Mathf.Round(strength * damage)), cooldown, Mathf.Min(reduction, ItemStatCalculator.MAX_DAMAGE_REDUCTION), ranged, cls == HeroClass.Mage, cls == HeroClass.Tank);
        }
    }

    /// <summary>
    /// A battle of two armies that are only data (§6.2), by Lanchester's law: each side deals its living units' damage per
    /// second to the other, through its armour; as many with swords as can reach fight (a front that widens with the root of
    /// the numbers, narrowed to the ways through when one side stands behind a town's wall), those who shoot all of them and
    /// already while the sides close in. A side may give up at a share of its losses (a raid's retreat). The coefficients are
    /// not guessed: AbstractBattleCalibration plays real battles and AbstractBattleFit fits them to those.
    /// </summary>
    public static class AbstractBattle
    {
        /// <summary>How the battle is fought: B behind a town's wall, the losses at which each side gives up (1: never).</summary>
        public struct Terms
        {
            public bool BWalled;
            public float RetreatA, RetreatB;
            public static Terms Open => new Terms { RetreatA = 1f, RetreatB = 1f };
        }

        public struct Outcome
        {
            /// <summary>0, 1, or -1: neither fell in the time.</summary>
            public int Winner;
            /// <summary>Share of each side's starting health lost.</summary>
            public float LossA, LossB;
            public float Seconds;
            /// <summary>The loser gave up before it was wiped out.</summary>
            public bool Retreated;
        }

        private sealed class Side
        {
            public List<UnitGroup> Groups;
            public float[] Pool;
            public float[] Unit;
            public float[] Dps;
            public float[] Reduction;
            public bool[] Ranged, Pierces;
            public float StartHealth;

            public float Health { get { float h = 0f; foreach (float p in Pool) h += p; return h; } }
            public float Units { get { float n = 0f; for (int i = 0; i < Pool.Length; i++) n += Pool[i] / Unit[i]; return n; } }
        }

        private static Side Make(List<UnitGroup> groups)
        {
            var s = new Side { Groups = groups, Pool = new float[groups.Count], Unit = new float[groups.Count], Dps = new float[groups.Count], Reduction = new float[groups.Count],
                               Ranged = new bool[groups.Count], Pierces = new bool[groups.Count] };
            for (int i = 0; i < groups.Count; i++)
            {
                UnitStats st = UnitStats.Of(groups[i]);
                s.Unit[i] = st.EffectiveHealth;
                s.Dps[i] = st.Dps * (groups[i].IsHero ? GameBalance.AbstractHeroSkillFactor : 1f); // its skills, on top
                s.Reduction[i] = st.Reduction;
                s.Ranged[i] = st.Ranged;
                s.Pierces[i] = st.PiercesArmour;
                s.Pool[i] = st.EffectiveHealth * groups[i].Count * groups[i].Health;
                s.StartHealth += s.Pool[i];
            }
            return s;
        }

        /// <summary>How many of a side of this many can strike at once, up against a side of that many (at a wall's ways: no more than they hold).</summary>
        public static float Engaged(float units, float enemies, bool walled = false)
        {
            if (units <= 0f) return 0f;
            float front = GameBalance.AbstractFrontBase + GameBalance.AbstractFrontPerRoot * Mathf.Sqrt(Mathf.Max(units, enemies));
            if (walled) front = Mathf.Min(front, GameBalance.AbstractGateFront);
            return Mathf.Min(units, front);
        }

        private static void Strike(Side from, Side to, float seconds, bool met, bool walled)
        {
            float enemies = to.Units;
            if (enemies <= 0f) return;
            // Swords: as many as reach, once the sides have met; shots: all of them, from the start
            float melee = 0f;
            for (int i = 0; i < from.Pool.Length; i++) if (!from.Ranged[i]) melee += from.Pool[i] / from.Unit[i];
            float meleeShare = met && melee > 0f ? Engaged(melee, enemies, walled) / melee : 0f;
            float dealt = 0f, piercing = 0f;
            for (int i = 0; i < from.Pool.Length; i++)
            {
                float d = (from.Pool[i] / from.Unit[i]) * from.Dps[i] * (from.Ranged[i] ? GameBalance.AbstractRangedShare : meleeShare);
                if (from.Pierces[i]) piercing += d; else dealt += d;
            }
            dealt *= GameBalance.AbstractDamageShare * seconds;
            piercing *= GameBalance.AbstractDamageShare * seconds;
            // It falls on the groups as they stand in the mass, each through its own armour (a mage's bolts through none)
            float total = to.Health;
            for (int i = 0; i < to.Pool.Length && total > 0f; i++)
                to.Pool[i] = Mathf.Max(0f, to.Pool[i] - (dealt * (1f - to.Reduction[i]) + piercing) * (to.Pool[i] / total));
        }

        /// <summary>Fights it out on open ground to the end: no one is changed, the outcome is returned.</summary>
        public static Outcome Resolve(List<UnitGroup> a, List<UnitGroup> b, float maxSeconds = 600f) => Resolve(a, b, Terms.Open, maxSeconds);

        /// <summary>Fights it out on these terms: no one is changed, the outcome is returned. One step is half a second of the battle.</summary>
        public static Outcome Resolve(List<UnitGroup> a, List<UnitGroup> b, Terms terms, float maxSeconds = 600f)
        {
            Side sa = Make(a), sb = Make(b);
            float t = 0f;
            const float step = 0.5f;
            float retreatA = terms.RetreatA <= 0f ? 1f : terms.RetreatA, retreatB = terms.RetreatB <= 0f ? 1f : terms.RetreatB;
            bool gaveUpA = false, gaveUpB = false;
            // Death is a whole unit at a time: a side down to its last few (AbstractLastUnits) is gone
            float last = Mathf.Max(0.5f, GameBalance.AbstractLastUnits);
            while (t < maxSeconds && sa.Units > last && sb.Units > last)
            {
                bool met = t >= GameBalance.AbstractApproachSeconds; // the sides close in first: only shots till then
                var snapshot = (float[])sa.Pool.Clone();
                Strike(sb, sa, step, met, terms.BWalled);
                float[] after = sa.Pool;
                sa.Pool = snapshot; // both strike with what they had at the start of the step
                Strike(sa, sb, step, met, terms.BWalled);
                sa.Pool = after;
                t += step;
                gaveUpA = 1f - sa.Health / Mathf.Max(1f, sa.StartHealth) >= retreatA;
                gaveUpB = 1f - sb.Health / Mathf.Max(1f, sb.StartHealth) >= retreatB;
                if (gaveUpA || gaveUpB) break;
            }
            bool aGone = sa.Units <= last, bGone = sb.Units <= last;
            bool aDown = aGone || gaveUpA, bDown = bGone || gaveUpB;
            return new Outcome
            {
                Winner = aDown && !bDown ? 1 : bDown && !aDown ? 0 : -1,
                LossA = aGone ? 1f : 1f - sa.Health / Mathf.Max(1f, sa.StartHealth),
                LossB = bGone ? 1f : 1f - sb.Health / Mathf.Max(1f, sb.StartHealth),
                Seconds = t,
                Retreated = (gaveUpA && !aGone) || (gaveUpB && !bGone)
            };
        }

        /// <summary>
        /// A group list from text as the calibration writes it: "Warrior.1x20+Knight.1x5" (rank.gear x count); "@bow" after a
        /// group makes them archers, "#Tank" (a HeroClass) a hero of that class: "Warrior.1x10@bow+General.2x1#Mage".
        /// </summary>
        public static List<UnitGroup> Parse(string text)
        {
            var list = new List<UnitGroup>();
            foreach (string whole in text.Split('+'))
            {
                string part = whole;
                bool archers = part.EndsWith("@bow");
                if (archers) part = part.Substring(0, part.Length - 4);
                string hero = null;
                int hash = part.IndexOf('#');
                if (hash >= 0) { hero = part.Substring(hash + 1); part = part.Substring(0, hash); }
                string[] rankRest = part.Split('.');
                string[] gearCount = rankRest[1].Split('x');
                var rank = (UnitRank)Enum.Parse(typeof(UnitRank), rankRest[0]);
                int gear = int.Parse(gearCount[0]), count = int.Parse(gearCount[1]);
                if (hero != null) list.Add(UnitGroup.Hero(rank, gear, (HeroClass)Enum.Parse(typeof(HeroClass), hero)));
                else list.Add(new UnitGroup(rank, gear, count, 1f, archers));
            }
            return list;
        }
    }
}
