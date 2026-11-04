using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.World
{
    /// <summary>
    /// What units cannot walk through out in the world: tree trunks, boulders, ore rocks, each a circle on the ground. The steering
    /// probes (RtsUnit, AIBaseController) only try to go round them, and a crowd or a sideways push took a unit right into one, where
    /// it stayed. GroundMotor keeps everyone out with these: a step into a circle slides along its edge, one found inside comes out.
    /// </summary>
    public static class SolidProps
    {
        private struct Circle { public Object Key; public float X, Z, Radius; }

        private const float CellSize = 8f;
        /// <summary>The widest unit: a circle is listed in every cell a unit touching it could stand in.</summary>
        private const float UnitReach = 1f;
        private const float BoulderLook = 1f;
        /// <summary>How much of a boulder's size its foot really covers: the rock narrows and is uneven.</summary>
        private const float BoulderFoot = 0.85f;
        /// <summary>A step cut to less than this share of itself by a prop was aimed at its middle.</summary>
        private const float HeadOnShare = 0.3f;

        private static readonly Dictionary<(int, int), List<Circle>> cells = new Dictionary<(int, int), List<Circle>>();
        private static readonly Dictionary<Object, Circle> known = new Dictionary<Object, Circle>();
        private static readonly List<Transform> boulders = new List<Transform>();
        private static Transform boulderGroup;
        private static float nextBoulderLook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            cells.Clear();
            known.Clear();
            boulders.Clear();
            boulderGroup = null;
            nextBoulderLook = 0f;
        }

        public static int Count => known.Count;

        private static int Cell(float v) => Mathf.FloorToInt(v / CellSize);

        /// <summary>This prop stands here now (again: wherever it stood before, it no longer does).</summary>
        public static void Set(Object key, Vector3 at, float radius)
        {
            Remove(key);
            var c = new Circle { Key = key, X = at.x, Z = at.z, Radius = radius };
            known[key] = c;
            float reach = radius + UnitReach;
            for (int x = Cell(at.x - reach); x <= Cell(at.x + reach); x++)
                for (int z = Cell(at.z - reach); z <= Cell(at.z + reach); z++)
                {
                    if (!cells.TryGetValue((x, z), out List<Circle> list)) cells[(x, z)] = list = new List<Circle>(2);
                    list.Add(c);
                }
        }

        public static void Remove(Object key)
        {
            if (!known.TryGetValue(key, out Circle c)) return;
            known.Remove(key);
            float reach = c.Radius + UnitReach;
            for (int x = Cell(c.X - reach); x <= Cell(c.X + reach); x++)
                for (int z = Cell(c.Z - reach); z <= Cell(c.Z + reach); z++)
                {
                    if (!cells.TryGetValue((x, z), out List<Circle> list)) continue;
                    for (int i = list.Count - 1; i >= 0; i--) if (list[i].Key == key) list.RemoveAt(i);
                    if (list.Count == 0) cells.Remove((x, z));
                }
        }

        /// <summary>A unit of this radius stands inside a prop: the shortest way out.</summary>
        public static bool Inside(Vector3 p, float radius, out Vector3 way)
        {
            way = Vector3.zero;
            LookAtBoulders();
            if (!cells.TryGetValue((Cell(p.x), Cell(p.z)), out List<Circle> list)) return false;
            foreach (Circle c in list)
            {
                float dx = p.x - c.X, dz = p.z - c.Z, reach = c.Radius + radius, d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                float d = Mathf.Sqrt(d2);
                if (d < 1e-4f) { way = new Vector3(reach, 0f, 0f); return true; }
                way = new Vector3(dx / d * (reach - d), 0f, dz / d * (reach - d));
                return true;
            }
            return false;
        }

        /// <summary>The step a unit may take: a step into a prop ends at its edge, keeping whatever of it goes along the edge.</summary>
        public static Vector3 Constrain(Vector3 p, Vector3 step, float radius)
        {
            float x = p.x + step.x, z = p.z + step.z;
            if (!cells.TryGetValue((Cell(x), Cell(z)), out List<Circle> list)) return step;
            bool met = false;
            Circle last = default;
            foreach (Circle c in list)
            {
                float dx = x - c.X, dz = z - c.Z, reach = c.Radius + radius, d2 = dx * dx + dz * dz;
                if (d2 >= reach * reach) continue;
                float d = Mathf.Max(Mathf.Sqrt(d2), 1e-4f);
                x = c.X + dx / d * reach;
                z = c.Z + dz / d * reach;
                met = true;
                last = c;
            }
            if (!met) return step;
            float asked = step.x * step.x + step.z * step.z, moved = (x - p.x) * (x - p.x) + (z - p.z) * (z - p.z);
            if (moved < asked * HeadOnShare * HeadOnShare) RoundHeadOn(p, step, radius, last, Mathf.Sqrt(asked), ref x, ref z);
            return new Vector3(x - p.x, step.y, z - p.z);
        }

        /// <summary>Straight at the middle a step along the edge is next to nothing, and the unit would stand there: it takes the whole step to one side.</summary>
        private static void RoundHeadOn(Vector3 p, Vector3 step, float radius, Circle c, float length, ref float x, ref float z)
        {
            float rx = p.x - c.X, rz = p.z - c.Z, r = Mathf.Sqrt(rx * rx + rz * rz);
            if (r < 1e-4f) return;
            rx /= r;
            rz /= r;
            float tx = -rz, tz = rx, along = tx * step.x + tz * step.z;
            // The side it already leans to; dead straight, a side that does not change from one step to the next while it stands here
            float side = Mathf.Abs(along) > 1e-3f ? Mathf.Sign(along) : (Mathf.FloorToInt(c.X + c.Z) & 1) == 0 ? 1f : -1f;
            float nx = p.x + tx * side * length - c.X, nz = p.z + tz * side * length - c.Z;
            float d = Mathf.Max(Mathf.Sqrt(nx * nx + nz * nz), 1e-4f), reach = c.Radius + radius;
            x = c.X + nx / d * reach;
            z = c.Z + nz / d * reach;
        }

        /// <summary>
        /// The scene's boulders are plain rocks with no script: a look once a second at which stand where (a new world moves them, a
        /// field clears them).
        /// </summary>
        private static void LookAtBoulders()
        {
            if (Time.unscaledTime < nextBoulderLook) return;
            nextBoulderLook = Time.unscaledTime + BoulderLook;
            if (boulderGroup == null)
            {
                foreach (Transform gone in boulders) Remove(gone);
                boulders.Clear();
                GameObject group = GameObject.Find("StylizedBoulders");
                if (group == null) return;
                boulderGroup = group.transform;
                foreach (Transform t in boulderGroup) boulders.Add(t);
            }
            foreach (Transform t in boulders)
            {
                if (t == null) continue;
                if (!t.gameObject.activeInHierarchy) { Remove(t); continue; }
                Vector3 s = t.lossyScale;
                float radius = Mathf.Max(s.x, s.z) * BoulderFoot;
                if (known.TryGetValue(t, out Circle c) && Mathf.Abs(c.X - t.position.x) < 0.1f && Mathf.Abs(c.Z - t.position.z) < 0.1f) continue;
                Set(t, t.position, radius);
            }
        }
    }
}
