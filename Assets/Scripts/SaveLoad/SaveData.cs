using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.SaveLoad
{
    /// <summary>Everything a save holds (JsonUtility: public fields, lists, no dictionaries).</summary>
    [Serializable]
    public class SaveData
    {
        /// <summary>2: kingdoms as KingdomState (M6 step 2). A save of 1 still has them as Kingdoms (SaveSystem.Migrate).</summary>
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;
        /// <summary>The guests of this campaign, with their games (a host's save).</summary>
        public List<Net.GuestRecord> Guests = new List<Net.GuestRecord>();
        /// <summary>How many a district takes by itself (the people's window).</summary>
        public List<Building.LaborLimitSave> LaborLimits = new List<Building.LaborLimitSave>();
        /// <summary>The number the world's next entity gets.</summary>
        public int NextEntityId = 1;
        /// <summary>The player's name for this save (empty: none).</summary>
        public string Title = "";
        public long SavedAtTicks;
        public float PlaySeconds;
        public double GameHours;          // WorldClock
        /// <summary>The world (WorldSettings): 0 and 0 — the fixed endless world of saves before 0.2.0.</summary>
        public int WorldSeed;
        public float WorldSize;
        /// <summary>Bandit raids (BanditRaids): the next one's game hour (0: from before 0.2.1) and how many came.</summary>
        public double BanditNextRaid;
        public int BanditRaids;
        /// <summary>Marked zones (ZoneMap): x, z, type per cell.</summary>
        public List<int> Zones = new List<int>();
        /// <summary>Food in stock; -1: a save from before food (a full start stock then).</summary>
        public float Food = -1f;

        public int Wood;
        public List<TierCount> Ore = new List<TierCount>();
        public List<Item> Armory = new List<Item>();

        public PlayerSave Player = new PlayerSave();
        public List<BuildingSave> Buildings = new List<BuildingSave>();
        public List<CaravanSave> Caravans = new List<CaravanSave>();
        public List<UnitSave> Units = new List<UnitSave>();
        public WorldSave World = new WorldSave();
        public List<ArtifactSave> Pickups = new List<ArtifactSave>(); // artifacts and shards on the ground
        public int WorldEpoch;
        public List<KingdomState> KingdomStates = new List<KingdomState>(); // NPC kings (§12), near and far
        public List<KingdomSave> Kingdoms = new List<KingdomSave>(); // the same in a save of version 1: read, no longer written
        public bool KingdomsFounded;  // false in saves from before the NPC kings: they are founded on load
        public int WallLevel;         // Fortifications (0 in old saves: level I)

        public int OreTotal { get { int n = 0; foreach (TierCount t in Ore) n += t.Count; return n; } }
    }

    /// <summary>A kingdom in a save of version 1, only read now (KingdomState.FromOldSave).</summary>
    [Serializable]
    public class KingdomSave
    {
        public string Name;
        public Vector3 Home;
        public int Hall, HallHealth;
        public float Wood, Ore, Births, Age, Interregnum;
        public bool Defeated;
        /// <summary>A free hero's band (§7.4): no hall, no raids. Team: its side (the same ring colour whenever it is about).</summary>
        public bool Free;
        /// <summary>The world's Central Empire (§9.1.1).</summary>
        public bool Empire;
        public int Team;
        public SubjectSave King = new SubjectSave(); public bool HasKing; public int KingClass, KingTotalXp;
        public List<SubjectSave> Subjects = new List<SubjectSave>();
    }

    [Serializable]
    public class SubjectSave
    {
        public int Rank, BattleXp, Health;
        public Vector3 Position;
    }

    [Serializable]
    public struct TierCount
    {
        public int Tier, Count;
        public TierCount(int tier, int count) { Tier = tier; Count = count; }
    }

    [Serializable]
    public class PlayerSave
    {
        public Vector3 Position;
        public float Yaw, Pitch;
        public int Health;
        public Item[] Gear = new Item[6];
        public int Mode;                 // GameMode
        public Vector3 MapCameraPosition;
        public bool HasMapCamera;
        public int Level = 1, Xp, AttributePoints, SkillPoints, StrengthPoints, HealthPoints, Class; // HeroProgress
        public bool RegenStart, LongShot, FireFan, Meteor, CriticalHit; public int RegenMaxPoints, RegenCurrentPoints, HealLevel, DashLevel, PierceLevel; // HeroSkills (PierceLevel: 1 = Piercing arrow learnt)
        public List<int> Artifacts = new List<int>(), Shards = new List<int>(); public bool StartArtifactChosen; // HeroArtifacts
    }

    [Serializable]
    public class ArtifactSave
    {
        public int Type;
        public bool Shard;
        public Vector3 Position;
        public bool OwnedByPlayer;      // waits for the player's next hero
    }

    [Serializable]
    public class BuildingSave
    {
        public int Type;                 // BuildingType
        public string SceneKey;          // set for buildings that come with the scene
        public string Name;
        public Vector3 Position;
        public float Yaw;
        public int Health;
        public List<TierCount> MineOre = new List<TierCount>();
        public List<TaskSave> Queue = new List<TaskSave>();
        public int BenchWorkers;
        public int Level;                // town hall
        public int OreTier;              // a wall: the ore it is built of
        public bool UnderConstruction;   // still a site (ConstructionSite), this far along
        public float ConstructionProgress;
        public bool OwnStock;            // a town hall with its own stock, not the main one (M5 step 7)
        public int StockWood;
        public float StockFood;
        public List<TierCount> StockOre = new List<TierCount>();
    }

    /// <summary>A cart on the road, and the hall it goes to (by where it stands).</summary>
    [Serializable]
    public class CaravanSave
    {
        public Vector3 Position, To;
        public int Good, Amount;
    }

    [Serializable]
    public struct TaskSave
    {
        public int Type, Level;
        public float Progress;
        /// <summary>A smelting task: the ore tier + 1 (0: an item, so older saves read as items) and the pieces it makes.</summary>
        public int Smelt, SmeltCount;
    }

    [Serializable]
    public class UnitSave
    {
        public string Name;
        public bool Villager;            // gatherers are rebuilt; other own units are matched by name
        public Vector3 Position;
        public float Yaw;
        public int Health;
        public Item[] Gear = new Item[6];
        public int Directive;            // GatherDirective
        public int InsideMine = -1;      // index into Buildings
        public int AtBench = -1;         // index into Buildings
        public int Rank = -1;            // UnitRank (-1: older saves, the model decides)
        public int BattleXp;
    }

    /// <summary>Changes to the generated world (it is rebuilt from its seed, then these are applied).</summary>
    [Serializable]
    public class WorldSave
    {
        public List<TreeSave> Trees = new List<TreeSave>();
        public List<CampSave> Camps = new List<CampSave>();
        public List<string> KilledAnimals = new List<string>();
    }

    [Serializable]
    public struct TreeSave
    {
        public string Key;
        public int State;                // TreeState
        public int Wood;
    }

    [Serializable]
    public class CampSave
    {
        public string Camp;
        public List<string> Killed = new List<string>();
        public List<MobSave> Alive = new List<MobSave>();
    }

    [Serializable]
    public struct MobSave
    {
        public string Name;
        public Vector3 Position;
        public int Health;
    }

    /// <summary>What the slot list shows without loading the whole world.</summary>
    [Serializable]
    public class SaveMeta
    {
        public int Slot;
        public bool Exists;
        public DateTime SavedAt;
        public string Title = "";
        public float PlaySeconds;
        public int Wood, Ore, Units, Buildings;
    }
}
