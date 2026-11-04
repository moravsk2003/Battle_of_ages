using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.AI
{
    public enum WorldMapKind { Kingdom, Empire, FreeBand }

    /// <summary>A kingdom as the global map knows it.</summary>
    [Serializable]
    public struct WorldMapEntry
    {
        public string Name;
        public Vector3 At;
        public WorldMapKind Kind;
        public int Hall, People;
    }

    /// <summary>
    /// Who stands where, for the global map (docs/GAME_DESIGN.md §5.4): every kingdom still standing, far or real. A guest has none of
    /// them on its own machine (they are the host's), so it is told them (Net.NetSession) and they are kept here.
    /// </summary>
    public static class WorldMapData
    {
        private static readonly List<WorldMapEntry> told = new List<WorldMapEntry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => told.Clear();

        /// <summary>A guest hears from its host.</summary>
        public static void Tell(List<WorldMapEntry> kingdoms)
        {
            told.Clear();
            told.AddRange(kingdoms);
        }

        /// <summary>What to show on this machine now.</summary>
        public static List<WorldMapEntry> Now()
        {
            if (Net.NetSession.IsGuest) return new List<WorldMapEntry>(told);
            return Standing(KingdomRealm.Far, NpcKingdom.All);
        }

        /// <summary>Those that still stand, near or far.</summary>
        public static List<WorldMapEntry> Standing(IReadOnlyList<KingdomState> far, IReadOnlyList<NpcKingdom> real)
        {
            var list = new List<WorldMapEntry>();
            foreach (KingdomState s in far)
            {
                if (s.Defeated) continue;
                list.Add(new WorldMapEntry
                {
                    Name = s.Name, At = s.Home, Kind = s.Empire ? WorldMapKind.Empire : s.Free ? WorldMapKind.FreeBand : WorldMapKind.Kingdom,
                    Hall = s.Hall, People = s.Subjects.Count
                });
            }
            foreach (NpcKingdom k in real)
            {
                if (k == null || k.IsDefeated) continue;
                list.Add(new WorldMapEntry
                {
                    Name = k.KingdomName, At = k.Home, Kind = k.IsEmpire ? WorldMapKind.Empire : k.Free ? WorldMapKind.FreeBand : WorldMapKind.Kingdom,
                    Hall = k.Economy.HallLevel, People = k.Population
                });
            }
            return list;
        }
    }
}
