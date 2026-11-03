using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Resources
{
    /// <summary>
    /// Trees and ore pebbles by grid cell (CellSize metres), so a worker finds the nearest one by looking at the cells
    /// round it, ring by ring, instead of asking the physics for every collider within 150 m (that walked terrain chunks,
    /// buildings and units, and a full 128-collider buffer could hide the tree next to it). Nodes add themselves when
    /// enabled, leave when disabled, and change cells when moved (Transform.hasChanged).
    /// </summary>
    public static class ResourceGrid
    {
        public const float CellSize = 16f;

        private static readonly Dictionary<Vector2Int, List<ResourceNodeTree>> trees = new Dictionary<Vector2Int, List<ResourceNodeTree>>();
        private static readonly Dictionary<Vector2Int, List<OrePebbleNode>> pebbles = new Dictionary<Vector2Int, List<OrePebbleNode>>();

        public static int TreeCount { get; private set; }
        public static int PebbleCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Clear()
        {
            trees.Clear();
            pebbles.Clear();
            TreeCount = PebbleCount = 0;
        }

        public static Vector2Int CellOf(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

        public static Vector2Int Add(ResourceNodeTree t) { TreeCount++; return Add(trees, t, t.transform.position); }
        public static void Remove(ResourceNodeTree t, Vector2Int cell) { if (Remove(trees, t, cell)) TreeCount--; }
        public static Vector2Int Add(OrePebbleNode p) { PebbleCount++; return Add(pebbles, p, p.transform.position); }
        public static void Remove(OrePebbleNode p, Vector2Int cell) { if (Remove(pebbles, p, cell)) PebbleCount--; }

        /// <summary>A node that moved: from its old cell into the one it is in now (nothing to do if it is the same).</summary>
        public static Vector2Int Move(ResourceNodeTree t, Vector2Int from) => Move(trees, t, from, t.transform.position);
        public static Vector2Int Move(OrePebbleNode p, Vector2Int from) => Move(pebbles, p, from, p.transform.position);

        private static Vector2Int Move<T>(Dictionary<Vector2Int, List<T>> map, T node, Vector2Int from, Vector3 at)
        {
            Vector2Int to = CellOf(at);
            if (to == from) return from;
            Remove(map, node, from);
            return Add(map, node, at);
        }

        private static Vector2Int Add<T>(Dictionary<Vector2Int, List<T>> map, T node, Vector3 at)
        {
            Vector2Int cell = CellOf(at);
            if (!map.TryGetValue(cell, out List<T> list)) map[cell] = list = new List<T>(8);
            list.Add(node);
            return cell;
        }

        private static bool Remove<T>(Dictionary<Vector2Int, List<T>> map, T node, Vector2Int cell)
        {
            return map.TryGetValue(cell, out List<T> list) && list.Remove(node);
        }

        public static List<ResourceNodeTree> TreesIn(Vector2Int cell) => trees.TryGetValue(cell, out List<ResourceNodeTree> l) ? l : null;
        public static List<OrePebbleNode> PebblesIn(Vector2Int cell) => pebbles.TryGetValue(cell, out List<OrePebbleNode> l) ? l : null;

        /// <summary>
        /// The cells of ring k round the centre cell (k = 0: the centre itself), appended to into. Everything in ring k
        /// is at least (k - 1) * CellSize away from any point of the centre cell: once the best found is nearer than
        /// that, later rings cannot beat it.
        /// </summary>
        public static void Ring(Vector2Int c, int k, List<Vector2Int> into)
        {
            if (k == 0) { into.Add(c); return; }
            for (int x = -k; x <= k; x++) { into.Add(new Vector2Int(c.x + x, c.y - k)); into.Add(new Vector2Int(c.x + x, c.y + k)); }
            for (int y = -k + 1; y <= k - 1; y++) { into.Add(new Vector2Int(c.x - k, c.y + y)); into.Add(new Vector2Int(c.x + k, c.y + y)); }
        }

        /// <summary>How many rings reach out to radius metres.</summary>
        /// <summary>
        /// The tree whose crown this ray goes through first, near where it reaches the ground (the map: a click on the leaves
        /// is a click on the tree, the trunk is hard to hit from above). Null with none.
        /// </summary>
        public static ResourceNodeTree CrownUnderRay(Ray ray, Vector3 groundPoint, float crownHeight = 4.6f, float crownRadius = 2.8f)
        {
            ResourceNodeTree best = null;
            float bestAlong = float.MaxValue;
            Vector2Int mid = CellOf(groundPoint);
            int reach = Mathf.CeilToInt(14f / CellSize);
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    List<ResourceNodeTree> trees = TreesIn(new Vector2Int(mid.x + dx, mid.y + dz));
                    if (trees == null) continue;
                    foreach (ResourceNodeTree t in trees)
                    {
                        if (t == null || t.CurrentState == TreeState.Depleted) continue;
                        Vector3 crown = t.transform.position + Vector3.up * crownHeight;
                        float along = Vector3.Dot(crown - ray.origin, ray.direction);
                        if (along <= 0f || along >= bestAlong) continue;
                        if ((ray.GetPoint(along) - crown).sqrMagnitude <= crownRadius * crownRadius) { best = t; bestAlong = along; }
                    }
                }
            return best;
        }

        public static int RingsFor(float radius) => Mathf.CeilToInt(radius / CellSize) + 1;
    }
}
