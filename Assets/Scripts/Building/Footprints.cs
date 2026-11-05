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
        private struct Rect { public float X, Z, HalfX, HalfZ, Cos, Sin; }

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
                float yaw = b.transform.eulerAngles.y * Mathf.Deg2Rad, cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
                Vector3 p = b.transform.position;
                float halfX, halfZ;
                if (b.Type == BuildingType.Mine)
                {
                    // The rock hill, not a square round the whole yard: it is wider than the old square and stood out of it
                    // (units pressed into the rock), while the track and the cart in front of the adit stay walkable
                    Vector3 s = b.transform.lossyScale;
                    halfX = MineNode.HillHalfX * s.x;
                    halfZ = MineNode.HillHalfZ * s.z;
                    float cz = MineNode.HillCenterZ * s.z;
                    p += new Vector3(cz * sin, 0f, cz * cos);
                }
                else
                {
                    Vector3 size = BuildingData.GetDefinition(b.Type).Size;
                    halfX = halfZ = Mathf.Max(size.x, size.z) * 0.5f;
                }
                var rect = new Rect { X = p.x, Z = p.z, HalfX = halfX, HalfZ = halfZ, Cos = cos, Sin = sin };
                float reach = Mathf.Max(halfX, halfZ) * 1.42f + 1f;
                for (int x = Cell(p.x - reach); x <= Cell(p.x + reach); x++)
                    for (int z = Cell(p.z - reach); z <= Cell(p.z + reach); z++)
                    {
                        if (!cells.TryGetValue((x, z), out List<Rect> list)) cells[(x, z)] = list = new List<Rect>(2);
                        list.Add(rect);
                    }
            }
        }

        private static void ToLocal(Rect r, float x, float z, out float lx, out float lz)
        {
            float dx = x - r.X, dz = z - r.Z;
            lx = dx * r.Cos - dz * r.Sin;
            lz = dx * r.Sin + dz * r.Cos;
        }

        private static void ToWorld(Rect r, float lx, float lz, out float x, out float z)
        {
            x = r.X + lx * r.Cos + lz * r.Sin;
            z = r.Z - lx * r.Sin + lz * r.Cos;
        }

        /// <summary>
        /// The building with a unit's radius round it is a rectangle with rounded corners: the nearest point of the walls to a
        /// local point (q) and how far it is (inside the walls, 0).
        /// </summary>
        private static float Off(Rect r, float lx, float lz, out float qx, out float qz)
        {
            qx = Mathf.Clamp(lx, -r.HalfX, r.HalfX);
            qz = Mathf.Clamp(lz, -r.HalfZ, r.HalfZ);
            return Mathf.Sqrt((lx - qx) * (lx - qx) + (lz - qz) * (lz - qz));
        }

        private static bool In(Rect r, float x, float z, float radius)
        {
            ToLocal(r, x, z, out float lx, out float lz);
            return Off(r, lx, lz, out _, out _) < radius;
        }

        private static bool InAny(List<Rect> list, float x, float z, float radius)
        {
            foreach (Rect r in list) if (In(r, x, z, radius)) return true;
            return false;
        }

        /// <summary>A local point pushed out to the unit's radius from the walls; one inside them goes out by the nearest side.</summary>
        private static void PushOut(Rect r, float radius, ref float lx, ref float lz)
        {
            float d = Off(r, lx, lz, out float qx, out float qz), reach = radius + 1e-3f; // just outside, past rounding to and from its axes
            if (d > 1e-4f)
            {
                lx = qx + (lx - qx) / d * reach;
                lz = qz + (lz - qz) / d * reach;
            }
            else if (r.HalfX - Mathf.Abs(lx) < r.HalfZ - Mathf.Abs(lz)) lx = (lx >= 0f ? 1f : -1f) * (r.HalfX + reach);
            else lz = (lz >= 0f ? 1f : -1f) * (r.HalfZ + reach);
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
                ToLocal(r, p.x, p.z, out float lx, out float lz);
                if (Off(r, lx, lz, out _, out _) >= radius) continue;
                float ox = lx, oz = lz;
                PushOut(r, radius, ref ox, ref oz);
                ToWorld(r, ox, oz, out float x, out float z);
                way = new Vector3(x - p.x, 0f, z - p.z);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The step a unit may take: a step into a building goes along its wall instead, at its whole pace, and round its corners
        /// like round a rock. Always the same way round, the one its way is shorter by: the side of the building's middle it heads
        /// past, which holds from step to step as it walks round (by the step's slant along the wall, or by the nearer corner,
        /// it swung back and forth, and one walking straight at a wall stood there for good).
        /// </summary>
        public static Vector3 Constrain(Vector3 p, Vector3 step, float radius)
        {
            if (Time.unscaledTime >= nextRemake || nextRemake < 0f) Remake();
            float x = p.x + step.x, z = p.z + step.z;
            if (!cells.TryGetValue((Cell(x), Cell(z)), out List<Rect> list)) return step;
            foreach (Rect r in list)
            {
                if (!In(r, x, z, radius)) continue;
                ToLocal(r, p.x, p.z, out float px, out float pz);
                ToLocal(r, x, z, out float nx, out float nz);
                float lsx = nx - px, lsz = nz - pz, length = Mathf.Sqrt(lsx * lsx + lsz * lsz);
                float o = Off(r, px, pz, out float qx, out float qz);
                if (o > 1e-4f)
                {
                    float ox = (px - qx) / o, oz = (pz - qz) / o; // out of the wall here
                    float way = px * lsz - pz * lsx >= 0f ? 1f : -1f; // round the middle the way it heads: anticlockwise or clockwise
                    nx = px - oz * way * length;
                    nz = pz + ox * way * length;
                }
                PushOut(r, radius, ref nx, ref nz);
                ToWorld(r, nx, nz, out x, out z);
                break;
            }
            if (InAny(list, x, z, radius)) return Vector3.zero; // between two buildings: it stays
            return new Vector3(x - p.x, step.y, z - p.z);
        }

                /// <summary>A building was placed, moved or gone: see it at once (tests; twice a second otherwise).</summary>
        public static void Refresh() => Remake();
    }
}
