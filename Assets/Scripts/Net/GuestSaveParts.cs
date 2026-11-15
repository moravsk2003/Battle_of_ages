using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Net
{
    /// <summary>
    /// A guest's save in parts (docs/GAME_DESIGN.md §14.4): its people, its buildings, its zones, the world it changed (felled
    /// trees, camps, animals) and the rest. The guest sends only the parts that changed since its last save to this host — the
    /// first save of a session whole — and the host keeps the parts it has and puts the save together from them. A late
    /// settlement's 3000 felled trees went every time before; now they go when another tree falls.
    /// Message: [kind][mask][for each part in the mask: its length (4 bytes) and its JSON in UTF-8].
    /// </summary>
    public static class GuestSaveParts
    {
        public const int Units = 0, Buildings = 1, Zones = 2, World = 3, Rest = 4, Count = 5;

        [Serializable] private sealed class UnitsPart { public List<UnitSave> Units = new List<UnitSave>(); }
        [Serializable] private sealed class BuildingsPart { public List<BuildingSave> Buildings = new List<BuildingSave>(); }
        [Serializable] private sealed class ZonesPart { public List<int> Zones = new List<int>(); }
        [Serializable] private sealed class WorldPart { public WorldSave World = new WorldSave(); }

        /// <summary>The save as the JSON of its parts (the save's lists are taken out of it).</summary>
        public static string[] Split(SaveData save)
        {
            var parts = new string[Count];
            parts[Units] = JsonUtility.ToJson(new UnitsPart { Units = save.Units });
            parts[Buildings] = JsonUtility.ToJson(new BuildingsPart { Buildings = save.Buildings });
            parts[Zones] = JsonUtility.ToJson(new ZonesPart { Zones = save.Zones });
            parts[World] = JsonUtility.ToJson(new WorldPart { World = save.World ?? new WorldSave() });
            save.Units = new List<UnitSave>();
            save.Buildings = new List<BuildingSave>();
            save.Zones = new List<int>();
            save.World = new WorldSave();
            parts[Rest] = JsonUtility.ToJson(save);
            return parts;
        }

        /// <summary>The message with the parts that differ from those sent before (all of them when nothing was); null if none do.</summary>
        public static byte[] Encode(byte kind, string[] parts, string[] sentBefore)
        {
            int mask = 0;
            var bytes = new byte[Count][];
            int length = 2;
            for (int i = 0; i < Count; i++)
            {
                if (sentBefore != null && sentBefore[i] != null && string.Equals(sentBefore[i], parts[i], StringComparison.Ordinal)) continue;
                mask |= 1 << i;
                bytes[i] = Encoding.UTF8.GetBytes(parts[i]);
                length += 4 + bytes[i].Length;
            }
            if (mask == 0) return null;
            var message = new byte[length];
            message[0] = kind;
            message[1] = (byte)mask;
            int at = 2;
            for (int i = 0; i < Count; i++)
            {
                if (bytes[i] == null) continue;
                int n = bytes[i].Length;
                message[at] = (byte)n; message[at + 1] = (byte)(n >> 8); message[at + 2] = (byte)(n >> 16); message[at + 3] = (byte)(n >> 24);
                Array.Copy(bytes[i], 0, message, at + 4, n);
                at += 4 + n;
            }
            return message;
        }

        /// <summary>A host's parts of one guest's save, read as they come.</summary>
        public sealed class Assembly
        {
            private List<UnitSave> units;
            private List<BuildingSave> buildings;
            private List<int> zones;
            private WorldSave world;
            private SaveData rest;

            /// <summary>Takes in a message; the whole save as JSON once every part has come (null before, or if it is broken).</summary>
            public string Take(byte[] data, int length)
            {
                if (length < 2) return null;
                int mask = data[1], at = 2;
                for (int i = 0; i < Count; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    if (at + 4 > length) return null;
                    int n = data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24;
                    if (n < 0 || at + 4 + n > length) return null;
                    string json = Encoding.UTF8.GetString(data, at + 4, n);
                    at += 4 + n;
                    switch (i)
                    {
                        case Units: units = JsonUtility.FromJson<UnitsPart>(json)?.Units; break;
                        case Buildings: buildings = JsonUtility.FromJson<BuildingsPart>(json)?.Buildings; break;
                        case Zones: zones = JsonUtility.FromJson<ZonesPart>(json)?.Zones; break;
                        case World: world = JsonUtility.FromJson<WorldPart>(json)?.World; break;
                        default: rest = JsonUtility.FromJson<SaveData>(json); break;
                    }
                }
                if (rest == null || units == null || buildings == null || zones == null || world == null) return null;
                rest.Units = units;
                rest.Buildings = buildings;
                rest.Zones = zones;
                rest.World = world;
                string whole = JsonUtility.ToJson(rest);
                rest.Units = new List<UnitSave>(); // (the parts stay the parts' own)
                rest.Buildings = new List<BuildingSave>();
                rest.Zones = new List<int>();
                rest.World = new WorldSave();
                return whole;
            }
        }
    }
}
