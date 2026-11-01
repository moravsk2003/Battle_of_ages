using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;

namespace BattleOfAges.World
{
    public enum ZoneType { None, Housing, Fields, Forest, Crafts, Mining, Trade }

    /// <summary>
    /// The zones the player marks on the map (docs/GAME_DESIGN.md §10.1, §18.3, M5 step 3): cells of 8 m, each of one
    /// type. Only on dry, not too steep land inside the settlement's reach (§5.3); trade from a hall II on (the first
    /// epoch has no market). Workers raise each zone's buildings by themselves (ZoneGrower). Kept in saves.
    /// </summary>
    public static class ZoneMap
    {
        public const float CellSize = 8f;
        /// <summary>No zone on ground steeper than this (rise per metre between a cell's corners).</summary>
        public const float MaxSlope = 0.35f;
        public static readonly ZoneType[] Types = { ZoneType.Housing, ZoneType.Fields, ZoneType.Forest, ZoneType.Crafts, ZoneType.Mining, ZoneType.Trade };

        private static readonly Dictionary<Vector2Int, ZoneType> cells = new Dictionary<Vector2Int, ZoneType>();
        private static readonly int[] counts = new int[Types.Length + 1];

        /// <summary>The zones changed (the overlay redraws, the grower looks again).</summary>
        public static event Action Changed;
        public static int Version { get; private set; }
        public static IReadOnlyDictionary<Vector2Int, ZoneType> Cells => cells;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Clear(); Changed = null; }

        public static void Clear()
        {
            cells.Clear();
            Array.Clear(counts, 0, counts.Length);
            Version++;
            Changed?.Invoke();
        }

        public static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
        public static Vector3 CenterOf(Vector2Int c)
        {
            var p = new Vector3((c.x + 0.5f) * CellSize, 0f, (c.y + 0.5f) * CellSize);
            p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
            return p;
        }

        public static ZoneType At(Vector3 p) => cells.TryGetValue(CellOf(p), out ZoneType t) ? t : ZoneType.None;
        public static int Count(ZoneType type) => counts[(int)type];

        private static Vector3 Home() => TownHall.Current != null ? TownHall.Current.transform.position : Vector3.zero;

        /// <summary>How far from its hall a settlement marks zones (§5.3 without the population term: R = 300 m, more with each epoch).</summary>
        public static float Reach()
        {
            int level = TownHall.Current != null ? TownHall.Current.Level : 1;
            return GameBalance.ZoneReachBase * (1f + 0.6f * (level - 1));
        }

        /// <summary>Why a cell cannot be of this type, or null if it can.</summary>
        public static string Forbidden(Vector2Int c, ZoneType type)
        {
            if (type == ZoneType.Trade && (TownHall.Current == null || TownHall.Current.Level < 2)) return "Торгівля — від ратуші II";
            Vector3 p = CenterOf(c);
            if (!WorldSettings.Inside(p, CellSize)) return "Тут край світу";
            Vector3 flat = p - Home(); flat.y = 0f;
            if (flat.magnitude > Reach()) return "Надто далеко від поселення: покращте ратушу";
            if (Lakes.InWater(p.x, p.z, CellSize * 0.5f)) return "На воді не можна";
            float half = CellSize * 0.5f;
            float rise = Mathf.Max(
                Mathf.Abs(InfiniteWorldManager.GetSampledTerrainHeight(p.x + half, p.z) - InfiniteWorldManager.GetSampledTerrainHeight(p.x - half, p.z)),
                Mathf.Abs(InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z + half) - InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z - half))) / CellSize;
            return rise > MaxSlope ? "Надто круто" : null;
        }

        /// <summary>Sets a cell without the checks (a save, tests); None clears it.</summary>
        public static void SetRaw(Vector2Int c, ZoneType type)
        {
            if (cells.TryGetValue(c, out ZoneType was)) { counts[(int)was]--; cells.Remove(c); }
            if (type != ZoneType.None) { cells[c] = type; counts[(int)type]++; }
            Version++;
        }

        /// <summary>Marks the cells within a brush round a point; returns how many changed, and why any was refused.</summary>
        public static int Paint(Vector3 at, float radius, ZoneType type, out string refused)
        {
            refused = null;
            int changed = 0, r = Mathf.CeilToInt(radius / CellSize);
            Vector2Int mid = CellOf(at);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var c = new Vector2Int(mid.x + dx, mid.y + dz);
                    Vector3 p = new Vector3((c.x + 0.5f) * CellSize, at.y, (c.y + 0.5f) * CellSize);
                    if ((p - at).sqrMagnitude > radius * radius) continue;
                    if (cells.TryGetValue(c, out ZoneType was) && was == type) continue;
                    string why = Forbidden(c, type);
                    if (why != null) { refused = why; continue; }
                    SetRaw(c, type);
                    changed++;
                }
            if (changed > 0) Changed?.Invoke();
            return changed;
        }

        /// <summary>Clears the cells within a brush; returns how many.</summary>
        public static int Erase(Vector3 at, float radius)
        {
            int changed = 0, r = Mathf.CeilToInt(radius / CellSize);
            Vector2Int mid = CellOf(at);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    var c = new Vector2Int(mid.x + dx, mid.y + dz);
                    Vector3 p = new Vector3((c.x + 0.5f) * CellSize, at.y, (c.y + 0.5f) * CellSize);
                    if ((p - at).sqrMagnitude > radius * radius || !cells.ContainsKey(c)) continue;
                    SetRaw(c, ZoneType.None);
                    changed++;
                }
            if (changed > 0) Changed?.Invoke();
            return changed;
        }

        public static string Name(ZoneType t)
        {
            switch (t)
            {
                case ZoneType.Housing: return "Житло";
                case ZoneType.Fields: return "Поля";
                case ZoneType.Forest: return "Ліс";
                case ZoneType.Crafts: return "Ремесла";
                case ZoneType.Mining: return "Рудня";
                case ZoneType.Trade: return "Торгівля";
                default: return "—";
            }
        }

        public static string Description(ZoneType t)
        {
            switch (t)
            {
                case ZoneType.Housing: return "Хати: кожна прискорює приріст населення";
                case ZoneType.Fields: return "Поля й селяни на них: їжа";
                case ZoneType.Forest: return "Лісопилки: дерево, більше в густому лісі";
                case ZoneType.Crafts: return "Верстаки: зброя, броня, переплавка";
                case ZoneType.Mining: return "Шахти: руда, багатша на жилах";
                case ZoneType.Trade: return "Ринок: обмін ресурсів (від ратуші II)";
                default: return "";
            }
        }

        public static Color Colour(ZoneType t)
        {
            switch (t)
            {
                case ZoneType.Housing: return new Color(0.95f, 0.75f, 0.3f);
                case ZoneType.Fields: return new Color(0.85f, 0.9f, 0.25f);
                case ZoneType.Forest: return new Color(0.2f, 0.75f, 0.3f);
                case ZoneType.Crafts: return new Color(0.9f, 0.45f, 0.2f);
                case ZoneType.Mining: return new Color(0.55f, 0.6f, 0.7f);
                case ZoneType.Trade: return new Color(0.75f, 0.4f, 0.9f);
                default: return Color.clear;
            }
        }

        /// <summary>The zones for a save: packed cells.</summary>
        public static List<int> Save()
        {
            var list = new List<int>(cells.Count * 3);
            foreach (var pair in cells) { list.Add(pair.Key.x); list.Add(pair.Key.y); list.Add((int)pair.Value); }
            return list;
        }

        public static void Load(List<int> saved)
        {
            cells.Clear();
            Array.Clear(counts, 0, counts.Length);
            if (saved != null)
                for (int i = 0; i + 2 < saved.Count; i += 3)
                {
                    var c = new Vector2Int(saved[i], saved[i + 1]);
                    cells[c] = (ZoneType)saved[i + 2];
                    counts[saved[i + 2]]++;
                }
            Version++;
            Changed?.Invoke();
        }
    }
}
