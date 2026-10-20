using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.RTS
{
    /// <summary>
    /// Uniform spatial hash of every moving agent (player units, raiders, animals), rebuilt lazily once per frame.
    /// Replaces scene-wide scans and Physics.OverlapSphere + GetComponentInParent for nearest-enemy searches,
    /// ally alerts and crowd separation, so the cost per query stays constant with 1000+ units.
    /// Zero allocations after warm-up.
    /// </summary>
    public static class UnitSpatialGrid
    {
        public const int TeamPlayer = 0, TeamHostile = 1, TeamNeutral = 2;
        /// <summary>NPC kingdoms get their own sides from here up: each one is its own side (docs/GAME_DESIGN.md §12.1).</summary>
        public const int TeamKingdomFirst = 3;

        /// <summary>
        /// Do these sides fight? Wildlife (neutral) fights nobody; camps (TeamHostile) and NPC kingdoms leave each other
        /// alone and both fight the player; NPC kingdoms fight each other.
        /// </summary>
        public static bool Hostile(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a == TeamNeutral || b == TeamNeutral) return false;
            if ((a == TeamHostile && b >= TeamKingdomFirst) || (b == TeamHostile && a >= TeamKingdomFirst)) return false;
            return true;
        }

        private const float CellSize = 4f;
        private const int TableSize = 4096; // power of two
        private const int TableMask = TableSize - 1;

        private static Transform[] transforms = new Transform[256];
        private static HealthComponent[] healths = new HealthComponent[256];
        private static Component[] owners = new Component[256];
        private static int[] teams = new int[256];
        private static float[] radii = new float[256];
        private static Vector3[] positions = new Vector3[256];
        private static int[] next = new int[256];
        private static int[] freeList = new int[256];
        private static int freeCount, capacity; // ids in [0, capacity) are used or free
        private static readonly int[] cellHead = new int[TableSize];
        private static int builtFrame = -1;

        public static int Count => capacity - freeCount;
        /// <summary>Ids in [0, Capacity) are agents or free slots (IsAlive tells).</summary>
        public static int Capacity => capacity;
        /// <summary>Re-reads every agent's position now (normally done once per frame on the first query).</summary>
        public static void Refresh() { builtFrame = -1; EnsureBuilt(); }

        public static int Register(Component owner, HealthComponent health, int team, float radius)
        {
            int id;
            if (freeCount > 0) id = freeList[--freeCount];
            else
            {
                if (capacity == transforms.Length) Grow();
                id = capacity++;
            }
            owners[id] = owner;
            transforms[id] = owner.transform;
            healths[id] = health;
            teams[id] = team;
            radii[id] = radius;
            positions[id] = owner.transform.position;
            builtFrame = -1;
            return id;
        }

        public static void Unregister(int id)
        {
            if (id < 0 || id >= capacity || transforms[id] == null) return;
            transforms[id] = null; owners[id] = null; healths[id] = null;
            if (freeCount == freeList.Length) System.Array.Resize(ref freeList, freeList.Length * 2);
            freeList[freeCount++] = id;
            builtFrame = -1;
        }

        public static Vector3 Position(int id) => positions[id];
        public static float Radius(int id) => radii[id];
        public static int Team(int id) => teams[id];
        public static Component Owner(int id) => owners[id];
        public static HealthComponent Health(int id) => healths[id];

        public static bool IsAlive(int id)
        {
            if (transforms[id] == null) return false;
            HealthComponent h = healths[id];
            return h == null || h.IsAlive;
        }

        /// <summary>Nearest living agent of the given team within maxDistance (ground distance), or -1.</summary>
        public static int FindNearest(Vector3 point, float maxDistance, int team, int excludeId = -1)
        {
            EnsureBuilt();
            int best = -1;
            float bestSqr = maxDistance * maxDistance;
            int r = Mathf.CeilToInt(maxDistance / CellSize);
            int cx = Cell(point.x), cz = Cell(point.z);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                for (int id = cellHead[Hash(cx + dx, cz + dz)]; id >= 0; id = next[id])
                {
                    if (id == excludeId || teams[id] != team || !IsAlive(id)) continue;
                    Vector3 d = positions[id] - point;
                    float sqr = d.x * d.x + d.z * d.z;
                    if (sqr < bestSqr) { bestSqr = sqr; best = id; }
                }
            }
            return best;
        }

        /// <summary>Nearest living enemy of this side (Hostile) within maxDistance (ground distance), or -1.</summary>
        public static int FindNearestEnemy(Vector3 point, float maxDistance, int myTeam, int excludeId = -1)
        {
            EnsureBuilt();
            int best = -1;
            float bestSqr = maxDistance * maxDistance;
            int r = Mathf.CeilToInt(maxDistance / CellSize);
            int cx = Cell(point.x), cz = Cell(point.z);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                for (int id = cellHead[Hash(cx + dx, cz + dz)]; id >= 0; id = next[id])
                {
                    if (id == excludeId || !Hostile(teams[id], myTeam) || !IsAlive(id)) continue;
                    Vector3 d = positions[id] - point;
                    float sqr = d.x * d.x + d.z * d.z;
                    if (sqr < bestSqr) { bestSqr = sqr; best = id; }
                }
            }
            return best;
        }

        /// <summary>Living enemies of this side (Hostile) within radius into buffer; returns the count.</summary>
        public static int QueryEnemies(Vector3 point, float radius, int myTeam, int[] buffer, int excludeId = -1)
        {
            EnsureBuilt();
            int count = 0;
            float sqrR = radius * radius;
            int r = Mathf.CeilToInt(radius / CellSize);
            int cx = Cell(point.x), cz = Cell(point.z);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                for (int id = cellHead[Hash(cx + dx, cz + dz)]; id >= 0; id = next[id])
                {
                    if (id == excludeId || !Hostile(teams[id], myTeam) || !IsAlive(id)) continue;
                    Vector3 d = positions[id] - point;
                    if (d.x * d.x + d.z * d.z > sqrR) continue;
                    buffer[count++] = id;
                    if (count == buffer.Length) return count;
                }
            }
            return count;
        }

        /// <summary>Living agents within radius (any team when team &lt; 0) into buffer; returns the count.</summary>
        public static int Query(Vector3 point, float radius, int team, int[] buffer, int excludeId = -1)
        {
            EnsureBuilt();
            int count = 0;
            float sqrR = radius * radius;
            int r = Mathf.CeilToInt(radius / CellSize);
            int cx = Cell(point.x), cz = Cell(point.z);
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                for (int id = cellHead[Hash(cx + dx, cz + dz)]; id >= 0; id = next[id])
                {
                    if (id == excludeId || (team >= 0 && teams[id] != team) || !IsAlive(id)) continue;
                    Vector3 d = positions[id] - point;
                    if (d.x * d.x + d.z * d.z > sqrR) continue;
                    buffer[count++] = id;
                    if (count == buffer.Length) return count;
                }
            }
            return count;
        }

        /// <summary>
        /// Push-apart velocity for an agent overlapping its neighbours (crowd separation instead of capsule physics).
        /// </summary>
        public static Vector3 Separation(int selfId, Vector3 position, float radius, float strength)
        {
            EnsureBuilt();
            Vector3 push = Vector3.zero;
            int cx = Cell(position.x), cz = Cell(position.z);
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int id = cellHead[Hash(cx + dx, cz + dz)]; id >= 0; id = next[id])
                {
                    if (id == selfId || !IsAlive(id)) continue;
                    Vector3 d = position - positions[id];
                    d.y = 0f;
                    float minDist = radius + radii[id];
                    float sqr = d.sqrMagnitude;
                    if (sqr >= minDist * minDist) continue;
                    float dist = Mathf.Sqrt(sqr);
                    // Coincident agents: split them deterministically by id
                    Vector3 dir = dist > 1e-4f ? d / dist : new Vector3(Mathf.Cos(selfId), 0f, Mathf.Sin(selfId));
                    push += dir * ((minDist - dist) * strength);
                }
            }
            return push;
        }

        private static void EnsureBuilt()
        {
            int frame = Time.frameCount;
            if (builtFrame == frame) return;
            builtFrame = frame;
            for (int i = 0; i < TableSize; i++) cellHead[i] = -1;
            for (int id = 0; id < capacity; id++)
            {
                Transform t = transforms[id];
                if (t == null) { next[id] = -1; continue; }
                Vector3 p = t.position;
                positions[id] = p;
                int h = Hash(Cell(p.x), Cell(p.z));
                next[id] = cellHead[h];
                cellHead[h] = id;
            }
        }

        private static int Cell(float v) => Mathf.FloorToInt(v / CellSize);
        private static int Hash(int x, int z) => ((x * 73856093) ^ (z * 19349663)) & TableMask;

        private static void Grow()
        {
            int n = transforms.Length * 2;
            System.Array.Resize(ref transforms, n);
            System.Array.Resize(ref healths, n);
            System.Array.Resize(ref owners, n);
            System.Array.Resize(ref teams, n);
            System.Array.Resize(ref radii, n);
            System.Array.Resize(ref positions, n);
            System.Array.Resize(ref next, n);
        }

        /// <summary>Forget everything (scene reload): registered transforms of the old scene are gone.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            System.Array.Clear(transforms, 0, transforms.Length);
            System.Array.Clear(owners, 0, owners.Length);
            System.Array.Clear(healths, 0, healths.Length);
            capacity = 0; freeCount = 0; builtFrame = -1;
        }
    }
}
