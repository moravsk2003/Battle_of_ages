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
        public int Version = 1;
        public long SavedAtTicks;
        public float PlaySeconds;
        public double GameHours;          // WorldClock

        public int Wood;
        public List<TierCount> Ore = new List<TierCount>();
        public List<Item> Armory = new List<Item>();

        public PlayerSave Player = new PlayerSave();
        public List<BuildingSave> Buildings = new List<BuildingSave>();
        public List<UnitSave> Units = new List<UnitSave>();
        public WorldSave World = new WorldSave();
        public List<ArtifactSave> Pickups = new List<ArtifactSave>(); // artifacts and shards on the ground
        public int WorldEpoch;

        public int OreTotal { get { int n = 0; foreach (TierCount t in Ore) n += t.Count; return n; } }
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
        public float Yaw;
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
    }

    [Serializable]
    public struct TaskSave
    {
        public int Type, Level;
        public float Progress;
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
        public float PlaySeconds;
        public int Wood, Ore, Units, Buildings;
    }
}
