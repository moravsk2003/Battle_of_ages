using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Draws every distant character in one batch (a BatchRendererGroup) instead of a renderer each: ten thousand
    /// renderers cost the frame their culling, their bounds, their property blocks and the dispatch of every move.
    /// A character far off (GpuSkinnedCharacter) takes a place here: where it stands, its body's frame, which baked
    /// clip it plays. The crowd's job (AI.CrowdSim) writes the places of the units it moves straight into these arrays,
    /// with the clip their pace asks for; the rest are followed by their transforms. Before a frame is drawn one job
    /// makes the matrices and they go to the GPU as they are; for each view (the camera, each cascade of the sun's
    /// shadows) a job picks who is seen, with the full distant mesh or, small on the screen, a light one.
    /// With no graphics device (a headless host) the places are kept and nothing is drawn.
    /// </summary>
    public static unsafe class CrowdDraw
    {
        /// <summary>Below this share of the screen's height a character is drawn with its light mesh (GpuSkinBake.LowMesh).</summary>
        public const float LowScreenHeight = 0.045f;
        public const int MaxSubmeshes = 4;
        internal const byte Shown = 1, Dead = 2, NoShadow = 4;
        private const float CharacterHeight = 2f;
        private const int MaxSplits = 8;

        /// <summary>Characters can be drawn this way here (else they keep their Animators at any distance).</summary>
        public static bool Usable => headless || BatchRendererGroup.BufferTarget == BatchBufferTarget.RawBuffer;
        private static bool headless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        /// <summary>Places taken now, and how many instances the last camera view drew (tests, probes).</summary>
        public static int Count { get; private set; }
        public static int LastSeen => lastSeen.IsCreated ? lastSeen[0] : 0;
        /// <summary>Nothing of the batch is drawn (probes: what it costs).</summary>
        public static bool Hide;
        /// <summary>A hunt for a fault (command line "-gpudebug far|low|hide|noswing"): which mesh every view takes (0 by size, 1 always the full, 2 always the light).</summary>
        public static int ForceMesh;
        /// <summary>A hunt for a fault ("-gpudebug keepkinds"): a kind nobody wears keeps its place and its registrations.</summary>
        public static bool KeepKinds;
        /// <summary>Kinds made since the start (a check of how often bodies change their look in the batch).</summary>
        public static int KindsMade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ReadDebugArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-gpudebug");
            if (at < 0 || at + 1 >= args.Length) return;
            string what = args[at + 1];
            ForceMesh = what.Contains("far") ? 1 : what.Contains("low") ? 2 : 0;
            Hide = what.Contains("hide");
            GpuSkinnedCharacter.NoSwing = what.Contains("noswing");
            GpuSkinnedCharacter.NoLoose = what.Contains("noloose");
            KeepKinds = what.Contains("keepkinds");
            Debug.Log($"[GpuSkin] debug: mesh {ForceMesh}, hide {Hide}, no swing {GpuSkinnedCharacter.NoSwing}");
        }

        private struct KindInfo
        {
            public BatchMeshID Far, Low;
            public int Submeshes, Layer, Users;
            public byte Shadows;
            public float CentreY, Radius;
        }

        private sealed class KindKey
        {
            public Mesh Far, Low;
            public Material[] Materials;
            public int Layer;
            public bool Shadows;
        }

        // One each place
        internal static NativeArray<float3> Pos;
        internal static NativeArray<quaternion> Rot;
        internal static NativeArray<float4> Loco, Prev, Swing, Fade;
        internal static NativeArray<byte> Flags;
        private static NativeArray<float4x4> local;
        private static NativeArray<int> kindOf;
        private static NativeArray<float3x4> objectToWorld, worldToObject;
        private static NativeArray<int> lastSeen;
        private static int capacity, high;
        private static readonly Stack<int> free = new Stack<int>();

        private static NativeList<KindInfo> kinds;
        private static NativeList<BatchMaterialID> kindMaterials; // MaxSubmeshes each kind
        private static readonly List<KindKey> kindKeys = new List<KindKey>();

        private static BatchRendererGroup group;
        private static GraphicsBuffer buffer;
        private static BatchID batch;
        private static bool created, dirty, cullPending;
        private static JobHandle lastCull;
        private static readonly int ObjectToWorldId = Shader.PropertyToID("unity_ObjectToWorld"), WorldToObjectId = Shader.PropertyToID("unity_WorldToObject"),
            LocoId = Shader.PropertyToID("_BrgLoco"), PrevId = Shader.PropertyToID("_BrgLocoPrev"), SwingId = Shader.PropertyToID("_BrgSwing"), FadeId = Shader.PropertyToID("_BrgFade");

        // ------------------------------------------------------------------ made and unmade

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Dispose();

        /// <summary>The arrays exist (the crowd's job is given them even when nobody is drawn).</summary>
        internal static void EnsureCreated()
        {
            if (created) return;
            created = true;
            kinds = new NativeList<KindInfo>(16, Allocator.Persistent);
            kindMaterials = new NativeList<BatchMaterialID>(16 * MaxSubmeshes, Allocator.Persistent);
            lastSeen = new NativeArray<int>(1, Allocator.Persistent);
            kindKeys.Clear();
            free.Clear();
            high = 0;
            Count = 0;
            if (!headless)
            {
                group = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
                group.SetGlobalBounds(new Bounds(Vector3.zero, Vector3.one * 1e7f));
                RenderPipelineManager.beginContextRendering -= BeforeRendering;
                RenderPipelineManager.beginContextRendering += BeforeRendering;
            }
            Grow(4096);
            Application.quitting -= Dispose;
            Application.quitting += Dispose;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Dispose;
#endif
        }

        private static void Dispose()
        {
            if (!created) return;
            CompleteCulling();
            created = false;
            RenderPipelineManager.beginContextRendering -= BeforeRendering;
            if (group != null) { group.Dispose(); group = null; }
            meshIds.Clear();
            materialIds.Clear();
            if (buffer != null) { buffer.Dispose(); buffer = null; }
            void Free<T>(ref NativeArray<T> a) where T : struct { if (a.IsCreated) a.Dispose(); a = default; }
            Free(ref Pos); Free(ref Rot); Free(ref Loco); Free(ref Prev); Free(ref Swing); Free(ref Fade); Free(ref Flags);
            Free(ref local); Free(ref kindOf); Free(ref objectToWorld); Free(ref worldToObject); Free(ref lastSeen);
            if (kinds.IsCreated) kinds.Dispose();
            if (kindMaterials.IsCreated) kindMaterials.Dispose();
            kindKeys.Clear();
            free.Clear();
            capacity = high = 0;
            Count = 0;
            dirty = false;
        }

        private static void Grow(int wanted)
        {
            CompleteCulling();
            int n = Mathf.Max(wanted, capacity * 2);
            NativeArray<T> Grown<T>(NativeArray<T> old) where T : struct
            {
                var a = new NativeArray<T>(n, Allocator.Persistent);
                if (old.IsCreated) { NativeArray<T>.Copy(old, a, capacity); old.Dispose(); }
                return a;
            }
            Pos = Grown(Pos); Rot = Grown(Rot); Loco = Grown(Loco); Prev = Grown(Prev); Swing = Grown(Swing); Fade = Grown(Fade); Flags = Grown(Flags);
            local = Grown(local); objectToWorld = Grown(objectToWorld); worldToObject = Grown(worldToObject);
            var k = new NativeArray<int>(n, Allocator.Persistent);
            if (kindOf.IsCreated) { NativeArray<int>.Copy(kindOf, k, capacity); kindOf.Dispose(); }
            for (int i = capacity; i < n; i++) k[i] = -1;
            kindOf = k;
            capacity = n;
            if (group == null) return;

            // The batch's buffer: 64 bytes of zeros (what an instance reads for a value nobody gave), then an array each value
            const int matrix = 48, vector = 16;
            int toWorldAt = 2 * matrix, toObjectAt = toWorldAt + matrix * n, locoAt = toObjectAt + matrix * n, prevAt = locoAt + vector * n,
                swingAt = prevAt + vector * n, fadeAt = swingAt + vector * n, bytes = fadeAt + vector * n;
            if (buffer != null) { group.RemoveBatch(batch); buffer.Dispose(); }
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bytes / 4, 4);
            var zero = new NativeArray<float4>(6, Allocator.Temp);
            buffer.SetData(zero, 0, 0, 6);
            zero.Dispose();
            var metadata = new NativeArray<MetadataValue>(6, Allocator.Temp);
            metadata[0] = new MetadataValue { NameID = ObjectToWorldId, Value = 0x80000000 | (uint)toWorldAt };
            metadata[1] = new MetadataValue { NameID = WorldToObjectId, Value = 0x80000000 | (uint)toObjectAt };
            metadata[2] = new MetadataValue { NameID = LocoId, Value = 0x80000000 | (uint)locoAt };
            metadata[3] = new MetadataValue { NameID = PrevId, Value = 0x80000000 | (uint)prevAt };
            metadata[4] = new MetadataValue { NameID = SwingId, Value = 0x80000000 | (uint)swingAt };
            metadata[5] = new MetadataValue { NameID = FadeId, Value = 0x80000000 | (uint)fadeAt };
            batch = group.AddBatch(metadata, buffer.bufferHandle);
            metadata.Dispose();
            dirty = true;
        }

        // ------------------------------------------------------------------ the kinds: a mesh, its light mesh, its materials

        private static int KindFor(Mesh far, Mesh low, Material[] materials, int layer, bool shadows)
        {
            int spare = -1;
            for (int i = 0; i < kindKeys.Count; i++)
            {
                KindKey k = kindKeys[i];
                if (k == null) { if (spare < 0) spare = i; continue; }
                if (k.Far != far || k.Low != low || k.Layer != layer || k.Shadows != shadows || k.Materials.Length != materials.Length) continue;
                bool same = true;
                for (int m = 0; m < materials.Length && same; m++) same = k.Materials[m] == materials[m];
                if (same) return i;
            }
            KindsMade++;
            var key = new KindKey { Far = far, Low = low, Materials = (Material[])materials.Clone(), Layer = layer, Shadows = shadows };
            Bounds b = far.bounds;
            var info = new KindInfo
            {
                Submeshes = Mathf.Min(materials.Length, far.subMeshCount), Layer = layer, Shadows = (byte)(shadows ? 1 : 0), Users = 0,
                CentreY = CharacterHeight * 0.5f, Radius = Mathf.Max(CharacterHeight, b.extents.magnitude)
            };
            if (spare < 0) { spare = kindKeys.Count; kindKeys.Add(null); kinds.Add(default); for (int s = 0; s < MaxSubmeshes; s++) kindMaterials.Add(default); }
            if (group != null)
            {
                info.Far = MeshId(far);
                info.Low = MeshId(low);
                for (int s = 0; s < info.Submeshes; s++) kindMaterials[spare * MaxSubmeshes + s] = MaterialId(materials[s]);
            }
            kindKeys[spare] = key;
            kinds[spare] = info;
            return spare;
        }

        // Meshes and materials are registered with the batch once however many kinds use them, and let go when none does. Two
        // kinds can share a mesh and materials (they differ in layer or shadow); each registered and let go of its own, and the
        // first to go took the ids from under the other, whose bodies were then drawn with whatever got those ids next: another
        // mesh skinned by this bake, stretched across the sky (testers, 0.3.10-0.3.11; seen in -smokemass battles too).
        private static readonly Dictionary<Mesh, (BatchMeshID id, int users)> meshIds = new Dictionary<Mesh, (BatchMeshID, int)>();
        private static readonly Dictionary<Material, (BatchMaterialID id, int users)> materialIds = new Dictionary<Material, (BatchMaterialID, int)>();

        private static BatchMeshID MeshId(Mesh m)
        {
            if (meshIds.TryGetValue(m, out var e)) { meshIds[m] = (e.id, e.users + 1); return e.id; }
            BatchMeshID id = group.RegisterMesh(m);
            meshIds[m] = (id, 1);
            return id;
        }

        private static void ReleaseMesh(Mesh m)
        {
            if (m is null || !meshIds.TryGetValue(m, out var e)) return;
            if (e.users > 1) { meshIds[m] = (e.id, e.users - 1); return; }
            meshIds.Remove(m);
            group.UnregisterMesh(e.id);
        }

        private static BatchMaterialID MaterialId(Material m)
        {
            if (materialIds.TryGetValue(m, out var e)) { materialIds[m] = (e.id, e.users + 1); return e.id; }
            BatchMaterialID id = group.RegisterMaterial(m);
            materialIds[m] = (id, 1);
            return id;
        }

        private static void ReleaseMaterial(Material m)
        {
            if (m is null || !materialIds.TryGetValue(m, out var e)) return;
            if (e.users > 1) { materialIds[m] = (e.id, e.users - 1); return; }
            materialIds.Remove(m);
            group.UnregisterMaterial(e.id);
        }

        /// <summary>How many meshes and materials the batch holds registered now (tests).</summary>
        public static int RegisteredMeshes => meshIds.Count;

        private static void Use(int kind, int by)
        {
            if (kind < 0) return;
            KindInfo info = kinds[kind];
            info.Users += by;
            if (info.Users <= 0 && !KeepKinds)
            {
                // Nobody wears it: its mesh may be destroyed (a kit changed), so the batch lets go of it
                if (group != null)
                {
                    KindKey key = kindKeys[kind];
                    ReleaseMesh(key.Far);
                    ReleaseMesh(key.Low);
                    for (int s = 0; s < info.Submeshes; s++) ReleaseMaterial(key.Materials[s]);
                }
                kindKeys[kind] = null;
                info = default;
            }
            kinds[kind] = info;
        }

        // ------------------------------------------------------------------ the places

        /// <summary>
        /// A place for a character: its distant mesh and the light one, the materials of its submeshes, and the frame of
        /// its body in the unit's own (a unit at pos, turned rot, has its body at TR(pos, rot) * local). -1: it cannot be drawn so.
        /// </summary>
        internal static int Add(Mesh far, Mesh low, Material[] materials, int layer, bool shadows, Matrix4x4 bodyLocal, Vector3 pos, Quaternion rot)
        {
            if (!Usable || far == null || low == null || materials == null || materials.Length == 0 || materials.Length > MaxSubmeshes) return -1;
            EnsureCreated();
            CompleteCulling();
            int slot;
            if (free.Count > 0) slot = free.Pop();
            else
            {
                if (high == capacity) Grow(capacity * 2);
                slot = high++;
            }
            int kind = KindFor(far, low, materials, layer, shadows);
            Use(kind, 1);
            kindOf[slot] = kind;
            local[slot] = bodyLocal;
            Pos[slot] = pos;
            Rot[slot] = rot;
            Flags[slot] = 0;
            Loco[slot] = Prev[slot] = new float4(0f, 1f, 30f, 0f);
            Swing[slot] = new float4(0f, 0f, 1f, -1f);
            Fade[slot] = float4.zero;
            Count++;
            dirty = true;
            return slot;
        }

        /// <summary>Another mesh or other materials for the same character (its kit changed).</summary>
        internal static bool SetKind(int slot, Mesh far, Mesh low, Material[] materials, int layer, bool shadows, Matrix4x4 bodyLocal)
        {
            if (slot < 0 || !created || far == null || low == null || materials.Length == 0 || materials.Length > MaxSubmeshes) return false;
            CompleteCulling();
            int kind = KindFor(far, low, materials, layer, shadows);
            if (kind != kindOf[slot])
            {
                Use(kind, 1);
                Use(kindOf[slot], -1);
                kindOf[slot] = kind;
            }
            local[slot] = bodyLocal;
            dirty = true;
            return true;
        }

        internal static void Remove(int slot)
        {
            if (slot < 0 || !created || slot >= high || kindOf[slot] < 0) return;
            CompleteCulling();
            Use(kindOf[slot], -1);
            kindOf[slot] = -1;
            Flags[slot] = 0;
            free.Push(slot);
            Count--;
            dirty = true;
        }

        internal static void SetPose(int slot, Vector3 pos, Quaternion rot)
        {
            if (cullPending) CompleteCulling();
            Pos[slot] = pos;
            Rot[slot] = rot;
            dirty = true;
        }

        internal static void SetFlag(int slot, byte flag, bool on)
        {
            if (slot < 0 || !created) return;
            if (cullPending) CompleteCulling();
            Flags[slot] = (byte)(on ? Flags[slot] | flag : Flags[slot] & ~flag);
            dirty = true;
        }

        internal static bool HasFlag(int slot, byte flag) => slot >= 0 && created && (Flags[slot] & flag) != 0;

        internal static Vector4 GetLoco(int slot) => Loco[slot];
        internal static Vector4 GetPrev(int slot) => Prev[slot];
        internal static Vector4 GetFade(int slot) => Fade[slot];
        internal static byte GetFlags(int slot) => Flags[slot];
        internal static Matrix4x4 GetLocal(int slot) => local[slot];
        /// <summary>Places in use go up to this (some below are free).</summary>
        internal static int High => high;
        /// <summary>The place's matrix as it went to the GPU with the last frame drawn.</summary>
        internal static Matrix4x4 GetObjectToWorld(int slot)
        {
            float3x4 m = objectToWorld[slot];
            var r = Matrix4x4.identity;
            r.SetColumn(0, new Vector4(m.c0.x, m.c0.y, m.c0.z, 0f));
            r.SetColumn(1, new Vector4(m.c1.x, m.c1.y, m.c1.z, 0f));
            r.SetColumn(2, new Vector4(m.c2.x, m.c2.y, m.c2.z, 0f));
            r.SetColumn(3, new Vector4(m.c3.x, m.c3.y, m.c3.z, 1f));
            return r;
        }
        /// <summary>
        /// Uploads now and reads the batch's buffer back from the GPU: whether every place in use holds there what the arrays
        /// hold here (its matrix and its clips). A check for the smoke run's dump, slow: never in play.
        /// </summary>
        internal static string VerifyUpload()
        {
            if (!created || buffer == null || high == 0) return "nothing uploaded";
            CompleteCulling();
            dirty = true;
            Flush();
            const int matrix = 48, vector = 16;
            int n = capacity, toWorldAt = 2 * matrix, toObjectAt = toWorldAt + matrix * n, locoAt = toObjectAt + matrix * n;
            var gpuWorld = new float3x4[high];
            var gpuLoco = new float4[high];
            var gpuSwing = new float4[high];
            buffer.GetData(gpuWorld, 0, toWorldAt / matrix, high);
            buffer.GetData(gpuLoco, 0, locoAt / vector, high);
            buffer.GetData(gpuSwing, 0, locoAt / vector + 2 * n, high);
            int bad = 0;
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < high; i++)
            {
                if (kindOf[i] < 0) continue;
                float3x4 a = objectToWorld[i], g = gpuWorld[i];
                float d = math.cmax(math.abs(a.c0 - g.c0)) + math.cmax(math.abs(a.c1 - g.c1)) + math.cmax(math.abs(a.c2 - g.c2)) + math.cmax(math.abs(a.c3 - g.c3));
                float dl = math.cmax(math.abs(Loco[i] - gpuLoco[i])), ds = math.cmax(math.abs(Swing[i] - gpuSwing[i]));
                if (d < 1e-3f && dl < 1e-3f && ds < 1e-3f) continue;
                if (bad++ < 12) lines.AppendLine($"  place {i}: matrix off by {d:0.###}, clip {Loco[i]} here vs {gpuLoco[i]} there, swing {Swing[i]} vs {gpuSwing[i]}");
            }
            return $"{bad} of the places in use differ on the GPU\n{lines}";
        }

        internal static Vector3 GetPos(int slot) => Pos[slot];
        internal static Quaternion GetRot(int slot) => Rot[slot];
        /// <summary>The mesh and the first material the batch draws this place with (null: none).</summary>
        internal static Mesh KindMesh(int slot) => Taken(slot) && kindKeys[kindOf[slot]] != null ? kindKeys[kindOf[slot]].Far : null;
        internal static Material KindMaterial(int slot) => Taken(slot) && kindKeys[kindOf[slot]] != null ? kindKeys[kindOf[slot]].Materials[0] : null;
        internal static int KindOf(int slot) => Taken(slot) ? kindOf[slot] : -1;
        internal static Mesh KindLow(int slot) => Taken(slot) && kindKeys[kindOf[slot]] != null ? kindKeys[kindOf[slot]].Low : null;
        internal static Material[] KindMaterials(int slot) => Taken(slot) && kindKeys[kindOf[slot]] != null ? kindKeys[kindOf[slot]].Materials : null;
        internal static int KindSubmeshes(int slot) => Taken(slot) ? kinds[kindOf[slot]].Submeshes : 0;
        internal static Vector4 GetSwing(int slot) => Swing[slot];
        /// <summary>Someone has this place now (its kind is set).</summary>
        internal static bool Taken(int slot) => created && slot >= 0 && slot < high && kindOf[slot] >= 0;
        internal static void SetLoco(int slot, Vector4 v) { if (cullPending) CompleteCulling(); Loco[slot] = v; dirty = true; }
        internal static void SetPrev(int slot, Vector4 v) { if (cullPending) CompleteCulling(); Prev[slot] = v; dirty = true; }
        internal static void SetSwing(int slot, Vector4 v) { if (cullPending) CompleteCulling(); Swing[slot] = v; dirty = true; }
        internal static void SetFade(int slot, Vector4 v) { if (cullPending) CompleteCulling(); Fade[slot] = v; dirty = true; }

        /// <summary>The crowd's job wrote places and clips: they go to the GPU with the next frame drawn.</summary>
        internal static void Touch() => dirty = true;

        /// <summary>No view's job reads the places any more (before anyone writes them).</summary>
        internal static void CompleteCulling()
        {
            if (!cullPending) return;
            cullPending = false;
            lastCull.Complete();
            lastCull = default;
        }

        // ------------------------------------------------------------------ the frame: matrices to the GPU

        private static void BeforeRendering(ScriptableRenderContext context, List<Camera> cameras) => Flush();

        private static readonly int SkinTimeId = Shader.PropertyToID("_GpuSkinTime");

        /// <summary>The clips' clock as the code keeps it (GpuSkinnedCharacter, AI.CrowdSim): Time.timeSinceLevelLoad.</summary>
        public static float SkinTime => Time.timeSinceLevelLoad;

        private static void Flush()
        {
            Shader.SetGlobalFloat(SkinTimeId, SkinTime); // (the shader's clock: URP's _Time is Time.time, ahead in a built game)
            if (!created || !dirty || buffer == null || high == 0) return;
            CompleteCulling();
            dirty = false;
            new MatrixJob { Pos = Pos, Rot = Rot, Local = local, Kind = kindOf, ToWorld = objectToWorld, ToObject = worldToObject }.Schedule(high, 256).Complete();
            const int matrix = 48, vector = 16;
            int n = capacity, toWorldAt = 2 * matrix, toObjectAt = toWorldAt + matrix * n, locoAt = toObjectAt + matrix * n;
            buffer.SetData(objectToWorld, 0, toWorldAt / matrix, high);
            buffer.SetData(worldToObject, 0, toObjectAt / matrix, high);
            buffer.SetData(Loco, 0, locoAt / vector, high);
            buffer.SetData(Prev, 0, locoAt / vector + n, high);
            buffer.SetData(Swing, 0, locoAt / vector + 2 * n, high);
            buffer.SetData(Fade, 0, locoAt / vector + 3 * n, high);
        }

        [BurstCompile]
        private struct MatrixJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> Pos;
            [ReadOnly] public NativeArray<quaternion> Rot;
            [ReadOnly] public NativeArray<float4x4> Local;
            [ReadOnly] public NativeArray<int> Kind;
            [WriteOnly] public NativeArray<float3x4> ToWorld, ToObject;

            public void Execute(int i)
            {
                if (Kind[i] < 0) return;
                float4x4 m = math.mul(float4x4.TRS(Pos[i], Rot[i], new float3(1f)), Local[i]);
                float4x4 inverse = math.inverse(m);
                ToWorld[i] = new float3x4(m.c0.xyz, m.c1.xyz, m.c2.xyz, m.c3.xyz);
                ToObject[i] = new float3x4(inverse.c0.xyz, inverse.c1.xyz, inverse.c2.xyz, inverse.c3.xyz);
            }
        }

        // ------------------------------------------------------------------ a view: who is seen, and with which mesh

        private static JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, BatchCullingContext context, BatchCullingOutput output, IntPtr userContext)
        {
            if (!created || high == 0 || Count == 0 || Hide) return default;
            if (context.viewType != BatchCullingViewType.Camera && context.viewType != BatchCullingViewType.Light) return default;
            Flush(); // (a view asked for outside the pipeline's own frame)
            int splits = Mathf.Min(context.cullingSplits.Length, MaxSplits);
            if (splits == 0) return default;
            var planes = new NativeArray<float4>(context.cullingPlanes.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < planes.Length; i++)
            {
                Plane p = context.cullingPlanes[i];
                planes[i] = new float4(p.normal, p.distance);
            }
            var ranges = new NativeArray<int2>(splits, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int s = 0; s < splits; s++) ranges[s] = new int2(context.cullingSplits[s].cullingPlaneOffset, context.cullingSplits[s].cullingPlaneCount);
            LODParameters lod = context.lodParameters;
            float scale = (lod.isOrthographic ? CharacterHeight / (2f * Mathf.Max(0.01f, lod.orthoSize)) : CharacterHeight / (2f * Mathf.Tan(lod.fieldOfView * 0.5f * Mathf.Deg2Rad))) * QualitySettings.lodBias;
            var job = new CullJob
            {
                Pos = Pos, Kind = kindOf, Flags = Flags, Kinds = kinds.AsArray(), Materials = kindMaterials.AsArray(), Planes = planes, Splits = ranges,
                Output = output.drawCommands, Batch = batch, High = high, Eye = lod.cameraPosition, Scale = scale, Ortho = lod.isOrthographic,
                LayerMask = context.cullingLayerMask, Light = context.viewType == BatchCullingViewType.Light,
                LowHeight = LowScreenHeight, CullHeight = GpuSkinnedCharacter.CullScreenHeight, Seen = lastSeen, ForceMesh = ForceMesh
            };
            JobHandle handle = job.Schedule();
            lastCull = cullPending ? JobHandle.CombineDependencies(lastCull, handle) : handle;
            cullPending = true;
            return handle;
        }

        [BurstCompile]
        private struct CullJob : IJob
        {
            [ReadOnly] public NativeArray<float3> Pos;
            [ReadOnly] public NativeArray<int> Kind;
            [ReadOnly] public NativeArray<byte> Flags;
            [ReadOnly] public NativeArray<KindInfo> Kinds;
            [ReadOnly] public NativeArray<BatchMaterialID> Materials;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<float4> Planes;
            [ReadOnly, DeallocateOnJobCompletion] public NativeArray<int2> Splits;
            [NativeDisableContainerSafetyRestriction] public NativeArray<BatchCullingOutputDrawCommands> Output;
            [NativeDisableContainerSafetyRestriction] public NativeArray<int> Seen;
            public BatchID Batch;
            public int High;
            public float3 Eye;
            public float Scale, LowHeight, CullHeight;
            public int ForceMesh;
            public bool Ortho, Light;
            public uint LayerMask;

            public void Execute()
            {
                int splits = Splits.Length, kindCount = Kinds.Length, buckets = kindCount * 2 * splits;
                var counts = new NativeArray<int>(buckets, Allocator.Temp);
                var code = new NativeArray<ushort>(High, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                // Who is in which split of the view, and with which mesh
                for (int i = 0; i < High; i++)
                {
                    code[i] = 0;
                    int k = Kind[i];
                    byte flags = Flags[i];
                    if (k < 0 || (flags & Shown) == 0) continue;
                    KindInfo kind = Kinds[k];
                    if ((LayerMask & (1u << kind.Layer)) == 0) continue;
                    if (Light && (kind.Shadows == 0 || (flags & NoShadow) != 0)) continue;
                    float3 p = Pos[i];
                    float size = Ortho ? Scale : Scale / math.max(0.01f, math.distance(p, Eye));
                    if (size < CullHeight) continue; // a speck
                    float3 centre = p + new float3(0f, kind.CentreY, 0f);
                    int mask = 0;
                    for (int s = 0; s < splits; s++)
                    {
                        int2 span = Splits[s];
                        bool inside = true;
                        for (int j = span.x; j < span.x + span.y; j++)
                        {
                            float4 plane = Planes[j];
                            if (math.dot(plane.xyz, centre) + plane.w < -kind.Radius) { inside = false; break; }
                        }
                        if (inside) mask |= 1 << s;
                    }
                    if (mask == 0) continue;
                    // A shadow is cast by the light mesh always: its outline is the same within a few centimetres, and the
                    // sun's cascades drew every body of the crowd again at full weight (a third of the frame's triangles)
                    int low = ForceMesh == 1 ? 0 : ForceMesh == 2 ? 1 : Light || size < LowHeight ? 1 : 0;
                    code[i] = (ushort)(mask | (low << 15));
                    for (int s = 0; s < splits; s++) if ((mask & (1 << s)) != 0) counts[(k * 2 + low) * splits + s]++;
                }

                var offsets = new NativeArray<int>(buckets, Allocator.Temp);
                int total = 0, commands = 0, ranges = 0;
                for (int k = 0; k < kindCount; k++)
                {
                    bool any = false;
                    for (int b = k * 2 * splits; b < (k + 1) * 2 * splits; b++)
                    {
                        offsets[b] = total;
                        total += counts[b];
                        if (counts[b] > 0) { commands += Kinds[k].Submeshes; any = true; }
                    }
                    if (any) ranges++;
                }
                if (!Light) Seen[0] = total;
                if (total == 0 || commands == 0) return;

                BatchCullingOutputDrawCommands o = default;
                o.visibleInstances = (int*)UnsafeUtility.Malloc(sizeof(int) * (long)total, UnsafeUtility.AlignOf<int>(), Allocator.TempJob);
                o.visibleInstanceCount = total;
                o.drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>() * (long)commands, UnsafeUtility.AlignOf<BatchDrawCommand>(), Allocator.TempJob);
                o.drawCommandCount = commands;
                o.drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>() * (long)ranges, UnsafeUtility.AlignOf<BatchDrawRange>(), Allocator.TempJob);
                o.drawRangeCount = ranges;

                var cursor = new NativeArray<int>(buckets, Allocator.Temp);
                cursor.CopyFrom(offsets);
                for (int i = 0; i < High; i++)
                {
                    int c = code[i];
                    if (c == 0) continue;
                    int first = (Kind[i] * 2 + (c >> 15)) * splits;
                    for (int s = 0; s < splits; s++)
                        if ((c & (1 << s)) != 0) { int b = first + s; o.visibleInstances[cursor[b]] = i; cursor[b]++; }
                }

                int command = 0, range = 0;
                for (int k = 0; k < kindCount; k++)
                {
                    KindInfo kind = Kinds[k];
                    int begin = command;
                    for (int low = 0; low < 2; low++)
                        for (int s = 0; s < splits; s++)
                        {
                            int b = (k * 2 + low) * splits + s;
                            if (counts[b] == 0) continue;
                            for (int sub = 0; sub < kind.Submeshes; sub++)
                                o.drawCommands[command++] = new BatchDrawCommand
                                {
                                    flags = BatchDrawCommandFlags.None, visibleOffset = (uint)offsets[b], visibleCount = (uint)counts[b], batchID = Batch,
                                    materialID = Materials[k * MaxSubmeshes + sub], meshID = low == 1 ? kind.Low : kind.Far, submeshIndex = (ushort)sub,
                                    splitVisibilityMask = (ushort)(1 << s), sortingPosition = 0
                                };
                        }
                    if (command == begin) continue;
                    o.drawRanges[range++] = new BatchDrawRange
                    {
                        drawCommandsBegin = (uint)begin, drawCommandsCount = (uint)(command - begin), drawCommandsType = BatchDrawCommandType.Direct,
                        filterSettings = new BatchFilterSettings
                        {
                            renderingLayerMask = 0xffffffff, layer = (byte)kind.Layer, motionMode = MotionVectorGenerationMode.Camera,
                            shadowCastingMode = kind.Shadows != 0 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true,
                            staticShadowCaster = false, allDepthSorted = false
                        }
                    };
                }
                Output[0] = o;
            }
        }
    }
}
