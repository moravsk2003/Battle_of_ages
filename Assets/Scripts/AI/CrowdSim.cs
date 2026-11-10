using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.AI
{
    /// <summary>
    /// Moves every AI unit (AIBaseController) in one job on all cores instead of an Update per unit: ten thousand units cost
    /// 30 ms a frame as scripts (MassProbe), their walking a few tenths as a Burst job. The job does what
    /// AIBaseController.ExecuteMovement and GroundMotor.Move do on open ground: heads for the destination, turns, keeps apart
    /// from its neighbours (the unit grid), follows the relief (height tiles filled from the world's own height function),
    /// notices it is stuck, and says when the unit's AI is due to think. Where anything else is in the way (a tree, a
    /// building, a wall, a town's ring, a lake, the world's edge: ground cells classed as such) and while a unit climbs or
    /// has a road round a ring, the unit is moved by the old code that frame, so those rules stay in one place. The probes
    /// for obstacles ahead go as one batch of capsule casts.
    /// </summary>
    public static class CrowdSim
    {
        /// <summary>Off: every unit runs the old per-unit code (comparisons).</summary>
        public static bool Enabled = true;

        /// <summary>Units in the crowd, and how many of them the job moved by itself in the last frame (tests, probes).</summary>
        public static int Count => count;
        public static int LastGlided { get; private set; }
        public static int LastByHand { get; private set; }
        /// <summary>Since the game began: units moved by the job, by the old code, AI turns taken, probes ahead, frames.</summary>
        public static long TotalGlided, TotalByHand, TotalThinks, TotalProbes, TotalFrames;
        private static int slots;

        private const byte Arrived = 1, ByHand = 2, SteerDue = 4, Dropped = 8, ThinkDue = 16, NeedGround = 32, Glided = 64, ColliderFlip = 128;
        /// <summary>
        /// A unit's collider is for the hero to run into, the player's blows and clicks: past this from the viewer it is
        /// switched off (on again nearer than ColliderOn). Ten thousand moving character controllers cost the physics 8 ms a
        /// frame and every step of the hero 2 ms, wherever they stood.
        /// </summary>
        public const float ColliderOn = 220f, ColliderOff = 250f;
        /// <summary>A unit this deep in a trunk or a rock is in it (GroundMotor.InsidePropShare), and a step cut this short by one was aimed at its middle (SolidProps.HeadOnShare).</summary>
        private const float InsidePropShare = 0.8f, HeadOnShare = 0.3f;
        private const float GroundCell = 8f, GroundMargin = 3f, TileSize = 32f;
        private const int TileSide = 33, MaxHits = 8;

        private struct Body
        {
            public float Radius, Stop, RotSpeed, CapBottom, CapTop, CapRadius, Close, Mid, Far;
            public int GridId, Team, Slot;
        }

        private struct Mind
        {
            public float3 Dest;
            public float Speed;
            public byte HasDest, Fast;
        }

        private struct State
        {
            public float MoveClock, Tick, SteerTimer, StuckTimer, DetourTimer, SettleTimer, Speed;
            public float3 LastProgress, DetourDir;
            public int DetourSide, Detours;
            public byte ColliderOff, LookBand, PaceBand;
            public float PaceTold;
        }

        private static AIBaseController[] agents = new AIBaseController[0];
        private static float[] walkableY = new float[0];
        private static int count, capacity;
        private static bool created, iterating, wasRunning, holes;
        private static readonly List<AIBaseController> everyFrame = new List<AIBaseController>(), fresh = new List<AIBaseController>();

        private static NativeArray<Body> bodies;
        private static NativeArray<Mind> minds;
        private static NativeArray<State> states;
        private static NativeArray<float3> steer, outDesired;
        private static NativeArray<byte> outFlags, outLook, outWhy;
        private static NativeArray<float> outDt, outThink, outProbe, outSpeed;
        private static TransformAccessArray transforms;

        private static NativeArray<Vector3> gridPos;
        private static NativeArray<float> gridRadius;
        private static NativeArray<int> gridNext, gridHead;
        private static NativeArray<byte> gridAlive;
        private static NativeArray<float3> fronts;

        private static NativeParallelMultiHashMap<long, float3> props;
        private static NativeParallelHashMap<long, byte> cellClass;
        private static NativeParallelHashMap<long, int> tileAt;
        private static NativeList<float> tileData;
        private static readonly HashSet<long> wantedCells = new HashSet<long>(), wantedTiles = new HashSet<long>();
        private static int wallsVersion = -1, worldSeed = int.MinValue;
        private static float worldSize = -1f;

        private static NativeArray<CapsulecastCommand> casts;
        private static NativeArray<RaycastHit> castHits;
        private static int[] castAgent = new int[0];
        private static GameObject driver;
        private static readonly Dictionary<int, byte> colliderKind = new Dictionary<int, byte>();
        private static readonly Unity.Profiling.ProfilerMarker gridMarker = new Unity.Profiling.ProfilerMarker("Crowd.Grid"), jobMarker = new Unity.Profiling.ProfilerMarker("Crowd.Job"),
            afterMarker = new Unity.Profiling.ProfilerMarker("Crowd.After"), probeMarker = new Unity.Profiling.ProfilerMarker("Crowd.Probe"), groundMarker = new Unity.Profiling.ProfilerMarker("Crowd.Ground");

        // ------------------------------------------------------------------ who is in

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Dispose();
            count = 0;
            fresh.Clear();
            colliderKind.Clear();
            everyFrame.Clear();
            driver = null;
            wasRunning = false;
            Enabled = true;
        }

        private static void Create()
        {
            if (created) return;
            created = true;
            capacity = 1024;
            agents = new AIBaseController[capacity];
            walkableY = new float[capacity];
            Allocate(capacity);
            transforms = new TransformAccessArray(capacity);
            fronts = new NativeArray<float3>(64, Allocator.Persistent);
            cellClass = new NativeParallelHashMap<long, byte>(4096, Allocator.Persistent);
            if (!props.IsCreated) props = new NativeParallelMultiHashMap<long, float3>(4096, Allocator.Persistent);
            tileAt = new NativeParallelHashMap<long, int>(1024, Allocator.Persistent);
            tileData = new NativeList<float>(TileSide * TileSide * 256, Allocator.Persistent);
            Application.quitting -= Dispose;
            Application.quitting += Dispose;
        }

        private static void Allocate(int n)
        {
            NativeArray<T> Grown<T>(NativeArray<T> old) where T : struct
            {
                var a = new NativeArray<T>(n, Allocator.Persistent);
                if (old.IsCreated) { NativeArray<T>.Copy(old, a, Mathf.Min(old.Length, n)); old.Dispose(); }
                return a;
            }
            bodies = Grown(bodies); minds = Grown(minds); states = Grown(states); steer = Grown(steer); outDesired = Grown(outDesired);
            outFlags = Grown(outFlags); outLook = Grown(outLook); outWhy = Grown(outWhy); outDt = Grown(outDt); outThink = Grown(outThink); outProbe = Grown(outProbe); outSpeed = Grown(outSpeed);
        }

        private static void Dispose()
        {
            if (!created) return;
            created = false;
            void Free<T>(ref NativeArray<T> a) where T : struct { if (a.IsCreated) a.Dispose(); a = default; }
            Free(ref bodies); Free(ref minds); Free(ref states); Free(ref steer); Free(ref outDesired); Free(ref outFlags); Free(ref outDt);
            Free(ref outThink); Free(ref outProbe); Free(ref outSpeed); Free(ref gridPos); Free(ref gridRadius); Free(ref gridNext); Free(ref gridHead);
            Free(ref gridAlive); Free(ref outLook); Free(ref outWhy); Free(ref fronts); Free(ref casts); Free(ref castHits);
            if (transforms.isCreated) transforms.Dispose();
            if (cellClass.IsCreated) cellClass.Dispose();
            if (props.IsCreated) props.Dispose();
            if (tileAt.IsCreated) tileAt.Dispose();
            if (tileData.IsCreated) tileData.Dispose();
            for (int i = 0; i < count; i++) if (agents[i] != null) agents[i].crowdIndex = -1;
            count = 0;
        }

        internal static void Add(AIBaseController a)
        {
            if (!Application.isPlaying || a.crowdIndex >= 0) return;
            Create();
            if (count == capacity)
            {
                capacity *= 2;
                System.Array.Resize(ref agents, capacity);
                System.Array.Resize(ref walkableY, capacity);
                Allocate(capacity);
            }
            int i = count++;
            agents[i] = a;
            a.crowdIndex = i;
            transforms.Add(a.transform);
            states[i] = new State { DetourSide = 1, Tick = UnityEngine.Random.value * 0.05f };
            steer[i] = float3.zero;
            outFlags[i] = 0;
            outLook[i] = 0;
            outSpeed[i] = 0f;
            Refresh(a);
            if (a.CrowdWantsEveryFrame) everyFrame.Add(a);
            if (driver == null)
            {
                driver = new GameObject("CrowdSimDriver") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(driver);
                driver.AddComponent<Driver>();
            }
        }

        internal static void Remove(AIBaseController a)
        {
            int i = a.crowdIndex;
            if (i < 0 || !created) { a.crowdIndex = -1; return; }
            a.crowdIndex = -1;
            everyFrame.Remove(a);
            UnitSpatialGrid.SetFed(a.GridId, false);
            if (states[i].ColliderOff != 0 && a.Health != null && a.Health.IsAlive && a.CharacterCtrl != null) a.CharacterCtrl.enabled = true; // as it came
            if (iterating) { agents[i] = null; holes = true; Mind m = minds[i]; m.Fast = 0; m.HasDest = 0; minds[i] = m; Body b = bodies[i]; b.GridId = -1; bodies[i] = b; return; }
            TakeOut(i);
        }

        private static void TakeOut(int i)
        {
            int last = --count;
            transforms.RemoveAtSwapBack(i);
            if (i != last)
            {
                agents[i] = agents[last]; walkableY[i] = walkableY[last];
                bodies[i] = bodies[last]; minds[i] = minds[last]; states[i] = states[last]; steer[i] = steer[last];
                outFlags[i] = outFlags[last]; outSpeed[i] = outSpeed[last]; outLook[i] = 0;
                if (agents[i] != null) agents[i].crowdIndex = i;
            }
            agents[last] = null;
        }

        /// <summary>The unit's own numbers again (its place in the grid changed, or it was just added).</summary>
        internal static void Refresh(AIBaseController a)
        {
            int i = a.crowdIndex;
            if (i < 0 || !created) return;
            CharacterController cc = a.CharacterCtrl;
            float radius = cc != null ? cc.radius : 0.5f;
            var b = new Body
            {
                Radius = radius, Stop = a.CrowdStop, RotSpeed = a.CrowdTurn, GridId = a.GridId, Team = a.CrowdTeam,
                Slot = slots++ % AIBaseController.FarMoveStride, // (by its id every unit of a crowd made together had the same one)
                Close = a.CrowdClose, Mid = a.CrowdMid, Far = a.CrowdFar, CapRadius = radius * 0.9f, CapBottom = 0.5f, CapTop = 1.5f
            };
            if (cc != null)
            {
                float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
                b.CapTop = cc.center.y + half;
                b.CapBottom = Mathf.Min(b.CapTop, cc.center.y - half + cc.stepOffset);
                walkableY[i] = Mathf.Cos(cc.slopeLimit * Mathf.Deg2Rad);
            }
            else walkableY[i] = 0.7f;
            bodies[i] = b;
            // The grid takes its place from the job only once the job has seen it: a unit is made first and put in its place
            // after, and whoever asks the grid in between must find it there
            fresh.Add(a);
            Push(a);
        }

        /// <summary>Where the unit is going, how fast, and whether the job may move it: told again whenever any of it changes.</summary>
        internal static void Push(AIBaseController a)
        {
            int i = a.crowdIndex;
            if (i < 0 || !created) return;
            Vector3 d = a.Destination;
            bool has = a.HasDestination;
            Mind old = minds[i];
            if (!has || old.HasDest == 0) steer[i] = float3.zero; // a new walk: no direction kept from the last one
            minds[i] = new Mind { Dest = d, Speed = a.CrowdSpeed, HasDest = (byte)(has ? 1 : 0), Fast = (byte)(a.CrowdFast ? 1 : 0) };
        }

        /// <summary>The unit's ground speed as the job measured it (m/s, smoothed): what its animation follows far off.</summary>
        internal static float SpeedOf(AIBaseController a) => a.crowdIndex >= 0 && created ? outSpeed[a.crowdIndex] : 0f;

        /// <summary>The job moves this unit now (its animation need not measure it).</summary>
        internal static bool Moves(AIBaseController a) => a.crowdIndex >= 0 && wasRunning;
        /// <summary>The job is moving the crowd (not the old code unit by unit).</summary>
        public static bool Running => wasRunning;
        /// <summary>Why the old code moved a unit, counted since the game began: not allowed to glide, ground not known, in a prop, ground ahead not known.</summary>
        public static readonly long[] WhyByHand = new long[5];

        // ------------------------------------------------------------------ the ground: which cells are open, and how high

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        // Trunks, boulders and ore rocks as the job reads them: the circles of World.SolidProps, in its own cells

        internal static void PropAdded(int cellX, int cellZ, float x, float z, float radius)
        {
            if (!Application.isPlaying) return;
            if (!props.IsCreated) props = new NativeParallelMultiHashMap<long, float3>(4096, Allocator.Persistent);
            props.Add(Key(cellX, cellZ), new float3(x, z, radius));
        }

        internal static void PropRemoved(int cellX, int cellZ, float x, float z, float radius)
        {
            if (!props.IsCreated) return;
            var gone = new float3(x, z, radius);
            if (!props.TryGetFirstValue(Key(cellX, cellZ), out float3 c, out NativeParallelMultiHashMapIterator<long> it)) return;
            do
            {
                if (!c.Equals(gone)) continue;
                props.Remove(it);
                return;
            }
            while (props.TryGetNextValue(out c, ref it));
        }

        internal static void PropsCleared() { if (props.IsCreated) props.Clear(); }

        /// <summary>Something that stops walkers came, went or moved near this point: the cells about it are looked at again.</summary>
        public static void GroundChanged(float x, float z, float reach)
        {
            if (!created || !cellClass.IsCreated || cellClass.Count() == 0) return;
            float r = reach + GroundMargin;
            for (int cx = Mathf.FloorToInt((x - r) / GroundCell); cx <= Mathf.FloorToInt((x + r) / GroundCell); cx++)
                for (int cz = Mathf.FloorToInt((z - r) / GroundCell); cz <= Mathf.FloorToInt((z + r) / GroundCell); cz++)
                    cellClass.Remove(Key(cx, cz));
        }

        public static void GroundChangedEverywhere()
        {
            if (created && cellClass.IsCreated) { cellClass.Clear(); GroundResets++; }
        }

        /// <summary>How many times all the ground was looked at again, how many cells and height tiles are known (probes).</summary>
        public static int GroundResets;
        public static string GroundKnown => created ? $"{cellClass.Count()} cells, {tileAt.Count()} tiles, resets {GroundResets}" : "none";

        /// <summary>Open ground (1): nothing but other units and the relief to mind. Anything else near (2): the old code moves a unit there.</summary>
        private static byte Classify(int cx, int cz)
        {
            float minX = cx * GroundCell - GroundMargin, minZ = cz * GroundCell - GroundMargin, maxX = (cx + 1) * GroundCell + GroundMargin, maxZ = (cz + 1) * GroundCell + GroundMargin;
            float midX = (cx + 0.5f) * GroundCell, midZ = (cz + 0.5f) * GroundCell, round = GroundCell * 0.71f + GroundMargin;
            if (Footprints.AnyIn(minX, minZ, maxX, maxZ)) return 2; // (trunks and rocks the job goes round by itself)
            if (Lakes.InWater(midX, midZ, round + 1f) || KingdomTown.RingNear(midX, midZ, round + 2f)) return 2;
            if (WorldSettings.Bounded && WorldSettings.OutsideBy(midX, midZ) > -(round + 4f)) return 2;
            IReadOnlyList<WallBarriers.Barrier> walls = WallBarriers.All;
            for (int i = 0; i < walls.Count; i++)
            {
                WallBarriers.Barrier w = walls[i];
                if (w.Max.x >= minX && w.Min.x <= maxX && w.Max.y >= minZ && w.Min.y <= maxZ) return 2;
            }
            return 1;
        }

        private static void FillTile(long key, int tx, int tz)
        {
            int at = tileData.Length;
            tileData.ResizeUninitialized(at + TileSide * TileSide);
            float step = TileSize / (TileSide - 1);
            for (int z = 0; z < TileSide; z++)
                for (int x = 0; x < TileSide; x++)
                    tileData[at + z * TileSide + x] = InfiniteWorldManager.GetSampledTerrainHeight(tx * TileSize + x * step, tz * TileSize + z * step);
            tileAt[key] = at;
        }

        private static void WantGround(Vector3 p)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    long c = Key(Mathf.FloorToInt(p.x / GroundCell) + dx, Mathf.FloorToInt(p.z / GroundCell) + dz);
                    if (wantedCells.Count < 1024 && !cellClass.ContainsKey(c)) wantedCells.Add(c);
                }
            for (int dx = -1; dx <= 1; dx += 2)
                for (int dz = -1; dz <= 1; dz += 2)
                {
                    long t = Key(Mathf.FloorToInt((p.x + dx * 3f) / TileSize), Mathf.FloorToInt((p.z + dz * 3f) / TileSize));
                    if (wantedTiles.Count < 64 && !tileAt.ContainsKey(t)) wantedTiles.Add(t);
                }
        }

        private static void GiveGround()
        {
            foreach (long c in wantedCells) if (!cellClass.ContainsKey(c)) cellClass[c] = Classify((int)(c >> 32), (int)(uint)c);
            wantedCells.Clear();
            float until = Time.realtimeSinceStartup + 0.002f; // the height tiles within 2 ms a frame
            foreach (long t in wantedTiles)
            {
                if (tileAt.ContainsKey(t)) continue;
                FillTile(t, (int)(t >> 32), (int)(uint)t);
                if (Time.realtimeSinceStartup > until) break;
            }
            wantedTiles.Clear();
        }

        private static void WatchWorld()
        {
            int walls = WallBarriers.Version;
            if (walls != wallsVersion) { wallsVersion = walls; cellClass.Clear(); GroundResets++; }
            if (WorldSettings.Seed != worldSeed || WorldSettings.Size != worldSize)
            {
                worldSeed = WorldSettings.Seed; worldSize = WorldSettings.Size;
                cellClass.Clear(); tileAt.Clear(); tileData.Clear();
            }
            Footprints.Touch();
            SolidProps.Touch();
        }

        // ------------------------------------------------------------------ the frame

        private sealed class Driver : MonoBehaviour
        {
            private void Update() => Frame(Time.deltaTime);
        }

        private static void Frame(float dt)
        {
            if (!created || count == 0) return;
            bool run = Enabled && InfiniteWorldManager.Instance != null;
            if (run != wasRunning)
            {
                wasRunning = run;
                for (int i = 0; i < count; i++)
                {
                    AIBaseController a = agents[i];
                    if (a == null) continue;
                    UnitSpatialGrid.SetFed(a.GridId, run);
                    if (!run && states[i].ColliderOff != 0)
                    {
                        State s = states[i]; s.ColliderOff = 0; states[i] = s;
                        if (a.CharacterCtrl != null && a.Health != null && a.Health.IsAlive) a.CharacterCtrl.enabled = true;
                    }
                }
            }
            iterating = true;
            try
            {
                if (run) Run(dt);
                else for (int i = 0; i < count; i++) if (agents[i] != null) agents[i].ManagedUpdate();
            }
            finally
            {
                iterating = false;
                if (holes)
                {
                    holes = false;
                    for (int i = count - 1; i >= 0; i--) if (agents[i] == null) TakeOut(i);
                }
            }
        }

        private static void CopyGrid()
        {
            UnitSpatialGrid.EnsureCurrent();
            Vector3[] p = UnitSpatialGrid.PositionArray;
            if (!gridPos.IsCreated || gridPos.Length != p.Length)
            {
                if (gridPos.IsCreated) { gridPos.Dispose(); gridRadius.Dispose(); gridNext.Dispose(); gridAlive.Dispose(); }
                gridPos = new NativeArray<Vector3>(p.Length, Allocator.Persistent);
                gridRadius = new NativeArray<float>(p.Length, Allocator.Persistent);
                gridNext = new NativeArray<int>(p.Length, Allocator.Persistent);
                gridAlive = new NativeArray<byte>(p.Length, Allocator.Persistent);
            }
            if (!gridHead.IsCreated) gridHead = new NativeArray<int>(UnitSpatialGrid.Buckets, Allocator.Persistent);
            gridPos.CopyFrom(p);
            gridRadius.CopyFrom(UnitSpatialGrid.RadiusArray);
            gridNext.CopyFrom(UnitSpatialGrid.NextArray);
            gridAlive.CopyFrom(UnitSpatialGrid.AliveArray);
            gridHead.CopyFrom(UnitSpatialGrid.HeadArray);
        }

        private static void Run(float dt)
        {
            gridMarker.Begin();
            WatchWorld();
            CopyGrid();
            gridMarker.End();
            jobMarker.Begin();
            for (int t = 0; t < 64; t++) fronts[t] = BattleFront.InBattle(t) ? (float3)BattleFront.Front(t) : float3.zero;
            GameObject player = Managers.GameContext.Player;
            Camera cam = Utils.ViewCamera.Current;
            Camera look = Utils.GpuSkinnedCharacter.Viewer;
            bool lookOrtho = false;
            float lookScale = look != null ? Utils.GpuSkinnedCharacter.ViewScale(look, out lookOrtho) : 0f;
            var job = new MoveJob
            {
                Bodies = bodies, Minds = minds, States = states, Steer = steer, OutDesired = outDesired, OutFlags = outFlags, OutDt = outDt,
                OutThink = outThink, OutProbe = outProbe, OutSpeed = outSpeed,
                GridPos = gridPos, GridRadius = gridRadius, GridNext = gridNext, GridHead = gridHead, GridAlive = gridAlive, Fronts = fronts,
                OutLook = outLook, OutWhy = outWhy, Eye = look != null ? (float3)look.transform.position : float3.zero, LookScale = lookScale, LookOrtho = lookOrtho,
                Cells = cellClass, Tiles = tileAt, TileData = tileData.AsArray(), Props = props,
                Dt = dt, Frame = Time.frameCount, HasPlayer = player != null, Player = player != null ? (float3)player.transform.position : float3.zero,
                HasCamera = cam != null, Camera = cam != null ? (float3)cam.transform.position : float3.zero,
                WalkerSeparation = GameBalance.WalkerSeparation, StandingSeparation = GameBalance.StandingSeparation, BattlePull = GameBalance.BattlePull
            };
            job.Schedule(transforms).Complete();
            gridPos.CopyTo(UnitSpatialGrid.PositionArray);
            if (fresh.Count > 0)
            {
                foreach (AIBaseController a in fresh) if (a != null && a.crowdIndex >= 0) UnitSpatialGrid.SetFed(a.GridId, true);
                fresh.Clear();
            }
            jobMarker.End();
            afterMarker.Begin();

            // What the job could not do itself: thinking, walking by the old rules near anything solid, the probes ahead
            if (castAgent.Length < capacity) castAgent = new int[capacity];
            int probes = 0, glided = 0, byHand = 0;
            Vector3[] places = UnitSpatialGrid.PositionArray;
            for (int i = 0; i < count; i++)
            {
                if (outLook[i] != 0)
                {
                    outLook[i] = 0;
                    WhyByHand[0]++;
                    if (agents[i] != null && (object)agents[i].crowdLook != null && agents[i].crowdLook != null) agents[i].crowdLook.CrowdLook(look);
                }
                byte f = outFlags[i];
                if (f == 0) continue;
                AIBaseController a = agents[i];
                if (a == null) continue;
                if ((f & Glided) != 0) glided++;
                if ((f & ColliderFlip) != 0)
                {
                    CharacterController cc = a.CharacterCtrl;
                    if (cc != null) cc.enabled = states[i].ColliderOff == 0;
                }
                if ((f & (Arrived | Dropped)) != 0) a.CrowdArrived();
                if ((f & ByHand) != 0)
                {
                    byHand++;
                    WhyByHand[outWhy[i] < 5 ? outWhy[i] : 0]++;
                    int id = bodies[i].GridId;
                    Vector3 was = id >= 0 ? places[id] : Vector3.zero;
                    a.CrowdManagedMove(outDt[i]);
                    if (a.crowdIndex != i) continue; // (it died of it, or left)
                    Vector3 now = a.transform.position;
                    if (id >= 0)
                    {
                        places[id] = now;
                        float step = Mathf.Max(outDt[i], 1e-4f), pace = new Vector2(now.x - was.x, now.z - was.z).magnitude / step;
                        outSpeed[i] = Mathf.Lerp(outSpeed[i], pace > 30f ? 0f : pace, 1f - Mathf.Exp(-10f * step));
                    }
                    if ((f & NeedGround) != 0) WantGround(now);
                }
                if ((f & SteerDue) != 0 && a.crowdIndex == i) castAgent[probes++] = i;
                if ((f & ThinkDue) != 0 && a.crowdIndex == i) { TotalThinks++; a.CrowdThink(outThink[i]); }
            }
            TotalGlided += glided; TotalByHand += byHand; TotalProbes += probes; TotalFrames++;
            LastGlided = glided;
            LastByHand = byHand;
            for (int i = 0; i < everyFrame.Count; i++) if (everyFrame[i] != null) everyFrame[i].CrowdEveryFrame(dt);
            afterMarker.End();
            probeMarker.Begin();
            if (probes > 0) Probe(probes, places);
            probeMarker.End();
            groundMarker.Begin();
            GiveGround();
            groundMarker.End();
        }

        /// <summary>The way ahead of each unit due for a look, as one batch of capsule casts; a unit with something in its way picks its way round by the old code.</summary>
        private static void Probe(int n, Vector3[] places)
        {
            if (!casts.IsCreated || casts.Length < n)
            {
                if (casts.IsCreated) { casts.Dispose(); castHits.Dispose(); }
                int size = Mathf.NextPowerOfTwo(Mathf.Max(n, 256));
                casts = new NativeArray<CapsulecastCommand>(size, Allocator.Persistent);
                castHits = new NativeArray<RaycastHit>(size * MaxHits, Allocator.Persistent);
            }
            var query = new QueryParameters(~0, false, QueryTriggerInteraction.Ignore, false);
            for (int k = 0; k < n; k++)
            {
                int i = castAgent[k];
                Body b = bodies[i];
                Vector3 p = b.GridId >= 0 ? places[b.GridId] : Vector3.zero;
                casts[k] = new CapsulecastCommand(p + Vector3.up * b.CapBottom, p + Vector3.up * b.CapTop, b.CapRadius, outDesired[i], query, outProbe[i]);
            }
            CapsulecastCommand.ScheduleBatch(casts.GetSubArray(0, n), castHits.GetSubArray(0, n * MaxHits), 16, MaxHits).Complete();
            for (int k = 0; k < n; k++)
            {
                int i = castAgent[k];
                AIBaseController a = agents[i];
                if (a == null || a.crowdIndex != i) continue;
                bool clear = true;
                for (int h = 0; h < MaxHits; h++)
                {
                    RaycastHit hit = castHits[k * MaxHits + h];
                    int id = hit.colliderInstanceID;
                    if (id == 0) break;
                    // Most of what a probe meets in a crowd is other units: known by the collider's id, without asking for the object
                    if (!colliderKind.TryGetValue(id, out byte kind)) colliderKind[id] = kind = (byte)(hit.collider is CharacterController ? 1 : 2);
                    if (kind == 1) continue;
                    if (AIBaseController.Blocks(hit, hit.collider, a.transform, walkableY[i])) { clear = false; break; }
                }
                steer[i] = clear ? outDesired[i] : (float3)a.CrowdSteerBlocked(outDesired[i]);
            }
        }

        // ------------------------------------------------------------------ the job

        [BurstCompile]
        private struct MoveJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<Body> Bodies;
            [ReadOnly] public NativeArray<Mind> Minds;
            [NativeDisableParallelForRestriction] public NativeArray<State> States;
            [NativeDisableParallelForRestriction] public NativeArray<float3> Steer, OutDesired;
            [NativeDisableParallelForRestriction] public NativeArray<byte> OutFlags, OutLook, OutWhy;
            public float3 Eye;
            public float LookScale;
            public bool LookOrtho;
            [NativeDisableParallelForRestriction] public NativeArray<float> OutDt, OutThink, OutProbe, OutSpeed;
            [NativeDisableParallelForRestriction] public NativeArray<Vector3> GridPos; // each unit writes its own place only
            [ReadOnly] public NativeArray<float> GridRadius;
            [ReadOnly] public NativeArray<int> GridNext, GridHead;
            [ReadOnly] public NativeArray<byte> GridAlive;
            [ReadOnly] public NativeArray<float3> Fronts;
            [ReadOnly] public NativeParallelHashMap<long, byte> Cells;
            [ReadOnly] public NativeParallelHashMap<long, int> Tiles;
            [ReadOnly] public NativeArray<float> TileData;
            [ReadOnly] public NativeParallelMultiHashMap<long, float3> Props;
            public float Dt, WalkerSeparation, StandingSeparation, BattlePull;
            public int Frame;
            public bool HasPlayer, HasCamera;
            public float3 Player, Camera;

            public void Execute(int i, TransformAccess t)
            {
                Body b = Bodies[i];
                if (b.GridId < 0 || !t.isValid) { OutFlags[i] = 0; return; }
                float3 p = t.position;
                GridPos[b.GridId] = p; // wherever anything put it since
                if (GridAlive[b.GridId] == 0) { OutFlags[i] = 0; return; }
                State s = States[i];
                Mind m = Minds[i];
                byte f = 0;

                // How near the viewer: the hero, or the camera over the ground (AIBaseController.ViewerDistance)
                float d = HasPlayer ? math.distance(p, Player) : 10f;
                if (HasCamera) d = math.min(d, math.distance(p.xz, Camera.xz));
                float interval = d < b.Close ? 0.05f : d < b.Mid ? 0.2f : d < b.Far ? 1f : 2f;
                s.Tick += Dt;
                if (s.Tick >= interval) { OutThink[i] = s.Tick; s.Tick = 0f; f |= ThinkDue; }

                bool off = s.ColliderOff != 0 ? d > ColliderOn : d > ColliderOff;
                if (off != (s.ColliderOff != 0)) { s.ColliderOff = (byte)(off ? 1 : 0); f |= ColliderFlip; }

                int stride = d < b.Mid ? 1 : d < b.Far ? AIBaseController.MidMoveStride : AIBaseController.FarMoveStride;
                s.MoveClock += Dt;
                if (stride == 1 || (Frame + b.Slot) % stride == 0)
                {
                    float dt = math.min(s.MoveClock, 0.25f);
                    s.MoveClock = 0f;
                    State before = s;
                    if (m.Fast == 0) { OutDt[i] = dt; f |= ByHand; OutWhy[i] = 1; }
                    else
                    {
                        byte kept = f;
                        byte why = 0;
                        if (!Ground(p, out float here)) why = 2;
                        else if (InProp(p, b.Radius * InsidePropShare)) why = 3;
                        else if (!Move(i, ref t, b, m, ref s, p, here, dt, ref f)) why = 4;
                        if (why != 0)
                        {
                            OutWhy[i] = why;
                            s = before;
                            OutDt[i] = dt;
                            f = (byte)(kept | ByHand | NeedGround);
                        }
                    }
                }
                // How it looks from the camera: its body is told when its size on screen crosses a line or its pace changes
                // (GpuSkinnedCharacter: the lines with their slack, idle / walk / run, and the clip's rate)
                float size = LookOrtho ? LookScale : LookScale / math.max(0.01f, math.distance(p, Eye));
                byte band = (byte)(size < Utils.GpuSkinnedCharacter.CullScreenHeight ? 2
                    : (s.LookBand == 0 ? size < Utils.GpuSkinnedCharacter.FarScreenHeight : size < Utils.GpuSkinnedCharacter.FarScreenHeight * 1.15f) ? 1 : 0);
                // (a pace near a line does not flip to and fro: the line is crossed by a tenth before it counts)
                byte pace = s.PaceBand;
                if (pace == 0) { if (s.Speed > 0.44f) pace = (byte)(s.Speed < 2.8f ? 1 : 2); }
                else if (pace == 1) { if (s.Speed < 0.36f) pace = 0; else if (s.Speed > 3.05f) pace = 2; }
                else if (s.Speed < 2.55f) pace = (byte)(s.Speed < 0.4f ? 0 : 1);
                bool told = band != s.LookBand || (band == 1 && (pace != s.PaceBand || (pace != 0 && math.abs(s.Speed - s.PaceTold) > 0.12f * math.max(s.PaceTold, 0.1f))));
                if (told) { s.LookBand = band; s.PaceBand = pace; s.PaceTold = s.Speed; OutLook[i] = 1; }
                States[i] = s;
                OutFlags[i] = f;
            }

            /// <summary>The step on open ground; false where the ground ahead is not known to be open (nothing is changed then).</summary>
            private bool Move(int i, ref TransformAccess t, in Body b, in Mind m, ref State s, float3 p, float here, float dt, ref byte f)
            {
                float3 wish = float3.zero, facing = float3.zero;
                bool walking = false, arrived = false, dropped = false, due = false;
                float3 desired = float3.zero, heading = Steer[i];
                float probe = 0f;
                if (m.HasDest != 0)
                {
                    float3 to = m.Dest - p;
                    to.y = 0f;
                    float sqr = math.lengthsq(to);
                    if (sqr > b.Stop * b.Stop)
                    {
                        float remaining = math.sqrt(sqr);
                        desired = to / remaining;
                        // The look ahead ten times a second (the answer comes with the next frame)
                        s.SteerTimer -= dt;
                        bool has = math.lengthsq(heading) > 1e-6f;
                        if (s.SteerTimer <= 0f || !has)
                        {
                            s.SteerTimer = 0.1f;
                            due = true;
                            probe = math.min(1.2f, remaining);
                            if (!has) heading = desired;
                        }
                        float3 dir = heading;

                        // Stuck: a step to the side, then the other, then it gives the place up (ApplyObstacleDetour)
                        bool detouring = s.DetourTimer > 0f;
                        if (detouring) s.DetourTimer -= dt;
                        s.StuckTimer += dt;
                        if (s.StuckTimer >= 0.5f)
                        {
                            float3 gone = p - s.LastProgress;
                            gone.y = 0f;
                            bool blocked = math.length(gone) < m.Speed * s.StuckTimer * 0.25f;
                            s.StuckTimer = 0f;
                            s.LastProgress = p;
                            if (blocked)
                            {
                                if (s.Detours >= 2) { s.Detours = 0; s.DetourTimer = 0f; dropped = true; }
                                else
                                {
                                    if (detouring) s.DetourSide = -s.DetourSide;
                                    s.Detours++;
                                    s.DetourTimer = 1.2f * s.Detours;
                                    s.DetourDir = new float3(dir.z * s.DetourSide, 0f, -dir.x * s.DetourSide); // a quarter turn
                                    dir = s.DetourDir;
                                }
                            }
                            else
                            {
                                if (!detouring) s.Detours = 0;
                                if (s.DetourTimer > 0f) dir = s.DetourDir;
                            }
                        }
                        else if (s.DetourTimer > 0f) dir = s.DetourDir;

                        facing = dir;
                        float pull = 1f;
                        float3 flat = new float3(dir.x, 0f, dir.z);
                        if (b.Team >= 0 && b.Team < Fronts.Length && math.lengthsq(flat) > 1e-6f) pull = 1f + BattlePull * math.dot(math.normalize(flat), Fronts[b.Team]);
                        wish = dir * m.Speed * pull;
                        walking = true;
                    }
                    else arrived = true;
                }
                else { s.StuckTimer = 0f; s.LastProgress = p; }

                float3 v = wish;
                v.y = 0f;
                v += Separation(b.GridId, p, b.Radius, math.lengthsq(v) > 0.01f ? WalkerSeparation : StandingSeparation);
                float3 np = p;
                bool moved = false;
                if (math.lengthsq(v) >= 1e-6f)
                {
                    float top = math.max(math.length(wish) * 1.25f, 2f);
                    if (math.lengthsq(v) > top * top) v = math.normalize(v) * top;
                    np = AroundProps(p, v * dt, b.Radius);
                    if (!Ground(np, out float ground)) return false;
                    v = (np - p) / dt;
                    v.y = 0f;
                    if (ground - p.y > math.length(v) * dt * 1.43f + 0.05f) { np = p; np.y = here; } // a cliff ahead: it stays (GroundMotor.MaxClimb)
                    else np.y = ground;
                    moved = true;
                }
                else if (!walking)
                {
                    s.SettleTimer -= dt;
                    if (s.SettleTimer <= 0f)
                    {
                        s.SettleTimer = 0.3f;
                        if (math.abs(p.y - here) > 0.02f) { np.y = here; moved = true; }
                    }
                }

                if (walking && math.lengthsq(facing) > 0.01f)
                    t.rotation = math.slerp(t.rotation, quaternion.LookRotationSafe(facing, math.up()), math.min(1f, b.RotSpeed * dt));
                if (moved) { t.position = np; GridPos[b.GridId] = np; }
                float pace = math.distance(np.xz, p.xz) / math.max(dt, 1e-4f);
                s.Speed = math.lerp(s.Speed, pace > 30f ? 0f : pace, 1f - math.exp(-10f * dt));
                OutSpeed[i] = s.Speed;
                if (due) { OutDesired[i] = desired; OutProbe[i] = probe; f |= SteerDue; }
                if (arrived) f |= Arrived;
                if (dropped) f |= Dropped;
                f |= Glided;
                return true;
            }

            /// <summary>World.SolidProps.Inside: the unit stands in a trunk or a rock (the old code takes it out).</summary>
            private bool InProp(float3 p, float radius)
            {
                long key = ((long)(int)math.floor(p.x / 8f) << 32) ^ (uint)(int)math.floor(p.z / 8f);
                if (!Props.TryGetFirstValue(key, out float3 c, out NativeParallelMultiHashMapIterator<long> it)) return false;
                do
                {
                    float dx = p.x - c.x, dz = p.z - c.y, reach = c.z + radius;
                    if (dx * dx + dz * dz < reach * reach) return true;
                }
                while (Props.TryGetNextValue(out c, ref it));
                return false;
            }

            /// <summary>World.SolidProps.Constrain: a step into a trunk or a rock ends at its edge, one aimed at its middle goes round to one side.</summary>
            private float3 AroundProps(float3 p, float3 step, float radius)
            {
                float x = p.x + step.x, z = p.z + step.z;
                long key = ((long)(int)math.floor(x / 8f) << 32) ^ (uint)(int)math.floor(z / 8f);
                if (!Props.TryGetFirstValue(key, out float3 c, out NativeParallelMultiHashMapIterator<long> it)) return p + step;
                bool met = false;
                float3 last = float3.zero;
                do
                {
                    float dx = x - c.x, dz = z - c.y, reach = c.z + radius, d2 = dx * dx + dz * dz;
                    if (d2 >= reach * reach) continue;
                    float d = math.max(math.sqrt(d2), 1e-4f);
                    x = c.x + dx / d * reach;
                    z = c.y + dz / d * reach;
                    met = true;
                    last = c;
                }
                while (Props.TryGetNextValue(out c, ref it));
                if (!met) return p + step;
                float asked = step.x * step.x + step.z * step.z, moved = (x - p.x) * (x - p.x) + (z - p.z) * (z - p.z);
                if (moved < asked * HeadOnShare * HeadOnShare)
                {
                    float rx = p.x - last.x, rz = p.z - last.y, r = math.sqrt(rx * rx + rz * rz);
                    if (r >= 1e-4f)
                    {
                        rx /= r; rz /= r;
                        float tx = -rz, tz = rx, along = tx * step.x + tz * step.z, length = math.sqrt(asked);
                        float side = math.abs(along) > 1e-3f ? math.sign(along) : (((int)math.floor(last.x + last.y)) & 1) == 0 ? 1f : -1f;
                        float nx = p.x + tx * side * length - last.x, nz = p.z + tz * side * length - last.y;
                        float d = math.max(math.sqrt(nx * nx + nz * nz), 1e-4f), reach = last.z + radius;
                        x = last.x + nx / d * reach;
                        z = last.y + nz / d * reach;
                    }
                }
                return new float3(x, p.y + step.y, z);
            }

            private bool Ground(float3 p, out float height)
            {
                height = 0f;
                int cx = (int)math.floor(p.x / GroundCell), cz = (int)math.floor(p.z / GroundCell);
                if (!Cells.TryGetValue(((long)cx << 32) ^ (uint)cz, out byte open) || open != 1) return false;
                int tx = (int)math.floor(p.x / TileSize), tz = (int)math.floor(p.z / TileSize);
                if (!Tiles.TryGetValue(((long)tx << 32) ^ (uint)tz, out int at)) return false;
                float cell = TileSize / (TileSide - 1);
                float lx = (p.x - tx * TileSize) / cell, lz = (p.z - tz * TileSize) / cell;
                int ix = math.clamp((int)lx, 0, TileSide - 2), iz = math.clamp((int)lz, 0, TileSide - 2);
                float fx = math.saturate(lx - ix), fz = math.saturate(lz - iz);
                int k = at + iz * TileSide + ix;
                height = math.lerp(math.lerp(TileData[k], TileData[k + 1], fx), math.lerp(TileData[k + TileSide], TileData[k + TileSide + 1], fx), fz);
                return true;
            }

            /// <summary>UnitSpatialGrid.Separation over the grid as it was built this frame.</summary>
            private float3 Separation(int self, float3 position, float radius, float strength)
            {
                float3 push = float3.zero;
                int cx = (int)math.floor(position.x / UnitSpatialGrid.CellMetres), cz = (int)math.floor(position.z / UnitSpatialGrid.CellMetres);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int bucket = (((cx + dx) * 73856093) ^ ((cz + dz) * 19349663)) & UnitSpatialGrid.BucketMask;
                        for (int id = GridHead[bucket]; id >= 0; id = GridNext[id])
                        {
                            if (id == self || GridAlive[id] == 0) continue;
                            float3 away = position - (float3)GridPos[id];
                            away.y = 0f;
                            float min = radius + GridRadius[id], sqr = math.lengthsq(away);
                            if (sqr >= min * min) continue;
                            float dist = math.sqrt(sqr);
                            float3 dir = dist > 1e-4f ? away / dist : new float3(math.cos(self), 0f, math.sin(self));
                            push += dir * ((min - dist) * strength);
                        }
                    }
                return push;
            }
        }
    }
}
