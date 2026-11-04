using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.SaveLoad
{
    /// <summary>A stable number for an entity of the world's state, kept through saves and the turns between far and real (docs/GAME_DESIGN.md §14.3). 0: none.</summary>
    [Serializable]
    public struct WorldId : IEquatable<WorldId>
    {
        public int Value;

        public bool IsNone => Value == 0;
        public bool Equals(WorldId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is WorldId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => "#" + Value;
        public static bool operator ==(WorldId a, WorldId b) => a.Value == b.Value;
        public static bool operator !=(WorldId a, WorldId b) => a.Value != b.Value;
    }

    /// <summary>Hands out entity ids; the next one is kept in the save, so a loaded world goes on without reusing any.</summary>
    public static class WorldIds
    {
        /// <summary>The number the next id will have.</summary>
        public static int Next { get; private set; } = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Next = 1;

        public static WorldId New() => new WorldId { Value = Next++ };

        /// <summary>A new world, or a loaded one: ids go on from here.</summary>
        public static void Reset(int next) => Next = Mathf.Max(1, next);

        /// <summary>Never gives out this one again (a state from elsewhere that already has it).</summary>
        public static void Saw(WorldId id) { if (id.Value >= Next) Next = id.Value + 1; }

        /// <summary>Gives the id a number if it has none.</summary>
        public static void Ensure(ref WorldId id)
        {
            if (id.IsNone) id = New();
            else Saw(id);
        }
    }

    /// <summary>One unit as data.</summary>
    [Serializable]
    public class UnitState
    {
        public int Rank, BattleXp, Health;
        public Vector3 Position;
    }

    /// <summary>A hero as data (§6.2): the unit it is, its class, what it has learned. Alive: false while its kingdom has no hero (the interregnum, §7.2).</summary>
    [Serializable]
    public class HeroState
    {
        public WorldId Id, KingdomId;
        public bool Alive;
        public int Class, TotalXp;
        public UnitState Unit = new UnitState();
    }

    /// <summary>A body of units as data (§6.2); a settlement's garrison, or an army of its own on the map (M6 step 3).</summary>
    [Serializable]
    public class ArmyState
    {
        public WorldId Id, KingdomId, OwnerHeroId;
        public Vector3 Position;
        public List<UnitState> Units = new List<UnitState>();
        /// <summary>On a raid: at whom, how many set off, whether the hero leads it, whether it is on its way home or already at the target.</summary>
        public WorldId TargetKingdomId;
        public int StartCount;
        public bool WithHero, Returning, AtTarget;
    }

    /// <summary>A settlement as data (§6.2): its hall, its stock, who stands in it. A free hero's camp is one with no hall.</summary>
    [Serializable]
    public class SettlementState
    {
        public WorldId Id, KingdomId;
        public Vector3 Position;
        public int Hall, HallHealth;
        public float Wood, Ore, Births;
        public ArmyState Garrison = new ArmyState();
    }

    /// <summary>
    /// A kingdom as data (§6.2), whole: its hero, settlements and armies, as it lives far from the player and as it is saved.
    /// A real one (NpcKingdom) comes from this and goes back to it without losing anything. For now it has one settlement,
    /// its capital; the properties below speak of that one.
    /// </summary>
    [Serializable]
    public class KingdomState
    {
        public WorldId Id;
        public string Name;
        public bool Defeated;
        /// <summary>A free hero's band (§7.4): no hall, no raids. Team: its side (the same ring colour whenever it comes back).</summary>
        public bool Free;
        /// <summary>The world's Central Empire (§9.1.1).</summary>
        public bool Empire;
        public int Team;
        public float Age, Interregnum;
        /// <summary>Its age when its last raid ended (the next waits for the cooldown).</summary>
        public float LastRaidEnd = -1000f;
        /// <summary>A free hero's walk (FarWorld.Wander): hours left at this camp, or on its way to another.</summary>
        public float WaitHours;
        public bool Walking;
        public Vector3 WalkingTo;
        /// <summary>Hours it has yet to live through (KingdomRealm spreads the hour over the frames); not saved: less than an hour.</summary>
        [NonSerialized] public float PendingHours;
        public HeroState Hero = new HeroState();
        public List<SettlementState> Settlements = new List<SettlementState>();
        /// <summary>Its armies away from any settlement (M6 step 3).</summary>
        public List<ArmyState> Armies = new List<ArmyState>();

        public SettlementState Capital
        {
            get
            {
                if (Settlements.Count == 0) Settlements.Add(new SettlementState());
                return Settlements[0];
            }
        }

        public Vector3 Home { get => Capital.Position; set { Capital.Position = value; Capital.Garrison.Position = value; } }
        public int Hall { get => Capital.Hall; set => Capital.Hall = value; }
        public int HallHealth { get => Capital.HallHealth; set => Capital.HallHealth = value; }
        public float Wood { get => Capital.Wood; set => Capital.Wood = value; }
        public float Ore { get => Capital.Ore; set => Capital.Ore = value; }
        public float Births { get => Capital.Births; set => Capital.Births = value; }
        public List<UnitState> Subjects { get => Capital.Garrison.Units; set => Capital.Garrison.Units = value; }
        public bool HasKing { get => Hero.Alive; set => Hero.Alive = value; }
        public UnitState King { get => Hero.Unit; set => Hero.Unit = value; }
        public int KingClass { get => Hero.Class; set => Hero.Class = value; }
        public int KingTotalXp { get => Hero.TotalXp; set => Hero.TotalXp = value; }

        /// <summary>Everyone of it with a number, and who belongs to whom said in the numbers.</summary>
        public KingdomState EnsureIds()
        {
            WorldIds.Ensure(ref Id);
            WorldIds.Ensure(ref Hero.Id);
            Hero.KingdomId = Id;
            foreach (SettlementState s in Settlements)
            {
                WorldIds.Ensure(ref s.Id);
                s.KingdomId = Id;
                Link(s.Garrison);
            }
            foreach (ArmyState a in Armies) Link(a);
            return this;
        }

        private void Link(ArmyState army)
        {
            WorldIds.Ensure(ref army.Id);
            army.KingdomId = Id;
            army.OwnerHeroId = Hero.Id;
        }

        /// <summary>A kingdom from a save of before these states (save version 1).</summary>
        public static KingdomState FromOldSave(KingdomSave old)
        {
            var s = new KingdomState
            {
                Name = old.Name, Home = old.Home, Hall = old.Hall, HallHealth = old.HallHealth, Wood = old.Wood, Ore = old.Ore, Births = old.Births,
                Age = old.Age, Interregnum = old.Interregnum, Defeated = old.Defeated, Free = old.Free, Empire = old.Empire, Team = old.Team,
                HasKing = old.HasKing, KingClass = old.KingClass, KingTotalXp = old.KingTotalXp
            };
            if (old.King != null) s.King = Unit(old.King);
            foreach (SubjectSave u in old.Subjects) s.Subjects.Add(Unit(u));
            return s.EnsureIds();
        }

        private static UnitState Unit(SubjectSave u) => new UnitState { Rank = u.Rank, BattleXp = u.BattleXp, Health = u.Health, Position = u.Position };
    }
}
