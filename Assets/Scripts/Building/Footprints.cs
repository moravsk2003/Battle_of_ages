using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Where solid buildings stand, for the ground motor: units steer round them on their own, but nothing stopped a crowd
    /// from shoving one of its own through a wall. Fields (farmers stand on them), benches and chests (workers stand close),
    /// and fortress walls (WallBarriers) are not here. A grid of cells, remade twice a second.
    /// </summary>
    public static class Footprints
    {
        private struct Rect { public float X, Z, Half, Cos, Sin; }

        private const float CellSize = 16f, RemakeEvery = 0.5f;
        private static readonly Dictionary<(int, int), List<Rect>> cells = new Dictionary<(int, int), List<Rect>>();
        private static float nextRemake = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { cells.Clear(); nextRemake = -1f; }

        private static bool Solid(BuildingType t) =>
            t == BuildingType.TownHall || t == BuildingType.WorkerHouse || t == BuildingType.Sawmill || t == BuildingType.Market || t == BuildingType.Mine;

        private static int Cell(float v) => Mathf.FloorToInt(v / CellSize);

        private static void Remake()
        {
            nextRemake = Time.unscaledTime + RemakeEvery;
            cells.Clear();
            foreach (BuildingNode b in BuildingNode.Active)
            {
                if (b == null || !Solid(b.Type)) continue;
                Vector3 size = BuildingData.GetDefinition(b.Type).Size;
                float half = Mathf.Max(size.x, size.z) * 0.5f, yaw = b.transform.eulerAngles.y * Mathf.Deg2Rad;
                Vector3 p = b.transform.position;
                var rect = new Rect { X = p.x, Z = p.z, Half = half, Cos = Mathf.Cos(yaw), Sin = Mathf.Sin(yaw) };
                float reach = half * 1.42f + 1f;
                for (int x = Cell(p.x - reach); x <= Cell(p.x + reach); x++)
                    for (int z = Cell(p.z - reach); z <= Cell(p.z + reach); z++)
                    {
                        if (!cells.TryGetValue((x, z), out List<Rect> list)) cells[(x, z)] = list = new List<Rect>(2);
                        list.Add(rect);
                    }
            }
        }

        /// <summary>
        /// This point is inside a solid building (with the unit's radius): the way straight out of it, by its nearest side.
        /// </summary>
        public static bool Inside(Vector3 p, float radius, out Vector3 way)
        {
            way = Vector3.zero;
            if (Time.unscaledTime >= nextRemake || nextRemake < 0f) Remake();
            if (!cells.TryGetValue((Cell(p.x), Cell(p.z)), out List<Rect> list)) return false;
            foreach (Rect r in list)
            {
                float dx = p.x - r.X, dz = p.z - r.Z;
                // Into the building's own axes
                float lx = dx * r.Cos - dz * r.Sin, lz = dx * r.Sin + dz * r.Cos, reach = r.Half + radius;
                if (Mathf.Abs(lx) >= reach || Mathf.Abs(lz) >= reach) continue;
                float ox = 0f, oz = 0f;
                if (reach - Mathf.Abs(lx) < reach - Mathf.Abs(lz)) ox = (lx >= 0f ? 1f : -1f) * (reach - Mathf.Abs(lx));
                else oz = (lz >= 0f ? 1f : -1f) * (reach - Mathf.Abs(lz));
                way = new Vector3(ox * r.Cos + oz * r.Sin, 0f, -ox * r.Sin + oz * r.Cos);
                return true;
            }
            return false;
        }

        /// <summary>A building was placed, moved or gone: see it at once (tests; twice a second otherwise).</summary>
        public static void Refresh() => Remake();
    }
}
