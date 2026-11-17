using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Draws a character on the GPU once it is small on screen: the Animator, foot IK and skinned renderer stop, and the
    /// character takes a place in the crowd's batch (CrowdDraw), where the baked clips (GpuSkinBake) play in the vertex
    /// shader, so a crowd of distant soldiers costs no animation, no skinning and no renderer on the CPU. Up close the
    /// Animator takes over again (exact attacks, feet on the ground). The clip follows the unit: idle / walk / run by
    /// speed, attacks on the upper body, death. A unit the crowd's job moves (AI.CrowdSim) has its place and its clip
    /// written there; any other is followed here by its transform.
    /// Only characters drawn by ONE skinned renderer (gear merged into the body) can do this; others keep the Animator.
    /// One driver re-evaluates a slice of all characters each frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GpuSkinnedCharacter : MonoBehaviour
    {
        /// <summary>Screen height fraction below which the GPU draws the character (FP view: ~11 m away).</summary>
        public const float FarScreenHeight = 0.15f;
        public const float CullScreenHeight = 0.006f;
        private const float Hysteresis = 1.15f, CharacterHeight = 2f, WalkSpeed = 0.4f, RunSpeed = 2.8f, FadeTime = 0.25f;

        /// <summary>Viewer for capture / test cameras instead of the current view camera.</summary>
        public static Camera ViewerOverride;

        /// <summary>Off: every character uses its Animator (comparisons).</summary>
        public static bool Enabled
        {
            get => gpuEnabled;
            set { gpuEnabled = value; if (!value) foreach (GpuSkinnedCharacter c in Active.ToArray()) c.SetFar(false); }
        }
        private static bool gpuEnabled = true;

        /// <summary>Every character that can be drawn on the GPU is, at any distance (side-by-side captures).</summary>
        public static bool ForceGpu;

        private static readonly List<GpuSkinnedCharacter> Active = new List<GpuSkinnedCharacter>();
        /// <summary>Those with a place in the batch: the ones nobody else moves (followed here every frame), and the crowd's.</summary>
        private static readonly List<GpuSkinnedCharacter> Loose = new List<GpuSkinnedCharacter>(), InCrowd = new List<GpuSkinnedCharacter>();
        private static int cursor;
        private static GameObject driverObject;

        public bool IsFar { get; private set; }
        /// <summary>It is in the crowd's batch now and shown there.</summary>
        public bool DrawnOnGpu => slot >= 0 && !hidden;

        private Animator animator;
        private SkinnedMeshRenderer body;
        private SkinnedMeshLod lod;
        private FootIK footIK;
        private CharacterAnimationDriver driver;
        private CombatUnit combat;
        private HealthComponent health;

        private CharacterGear gear;
        private GpuSkinBake bake;
        private Mesh sourceMesh;
        private Material[] sourceMaterials;
        private bool hidden, failed, castsShadows = true;
        private int slot = -1, listed = -1; // listed: its place in Loose or InCrowd
        private List<GpuSkinnedCharacter> list;
        private Transform poseRoot;

        private bool dead;
        private int index = -1;

        private bool started, ready, rejected;
        private AI.AIBaseController mover;
        private int passes;

        /// <summary>The camera the characters are judged from now, how big a character of this height is at 1 m from it (or at any distance: ortho).</summary>
        public static Camera Viewer => ViewerOverride != null ? ViewerOverride : ViewCamera.Current;
        public static float ViewScale(Camera cam, out bool ortho) { ortho = cam.orthographic; return (ortho ? CharacterHeight / (2f * cam.orthographicSize) : CharacterHeight / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad))) * QualitySettings.lodBias; }

        /// <summary>The crowd's job saw its size on screen cross the line between the Animator and the batch (AI.CrowdSim).</summary>
        internal void CrowdLook(Camera cam) { if (index >= 0 && cam != null) Refresh(cam); }

        /// <summary>Its place in the crowd's batch (-1: none) and the baked rows of its idle, walk and run: (first row, frames).</summary>
        internal int DrawSlot => slot;
        internal void Clips(out Vector2 idle, out Vector2 walk, out Vector2 run)
        {
            idle = walk = run = new Vector2(0f, 1f);
            if (bake == null) return;
            idle = new Vector2(bake.Idle.FirstRow, bake.Idle.Frames);
            walk = new Vector2(bake.Walk.FirstRow, bake.Walk.Frames);
            run = new Vector2(bake.Run.FirstRow, bake.Run.Frames);
        }

        /// <summary>It joined or left the crowd the job moves: who writes its place changed.</summary>
        internal void CrowdMembershipChanged() => Relist();

        // Set up on the driver's first look, not here: builders still replace the body in this frame.
        private void Start()
        {
            started = true;
            Register();
        }

        private bool Setup()
        {
            if (rejected) return false;
            animator = GetComponentInChildren<Animator>();
            body = animator != null ? animator.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (!Eligible()) { rejected = true; Destroy(this); return false; }
            animator.keepAnimatorStateOnDisable = true;
            lod = GetComponent<SkinnedMeshLod>();
            if (hidden)
            {
                if (lod != null) lod.SetHidden(true);
                body.enabled = false;
            }
            footIK = animator.GetComponent<FootIK>();
            driver = GetComponentInChildren<CharacterAnimationDriver>();
            combat = GetComponentInParent<CombatUnit>();
            health = GetComponentInParent<HealthComponent>();
            mover = GetComponentInParent<AI.AIBaseController>();
            poseRoot = mover != null ? mover.transform : transform;
            if (mover != null) mover.crowdLook = this;
            if (combat != null) combat.OnAttackStarted += HandleAttack;
            if (health != null) health.OnDeath += HandleDeath;
            dead = health != null && !health.IsAlive;
            ready = true;
            return true;
        }

        /// <summary>One skinned body in the animated model (its LOD group, if any, only culls it), drawn with Custom/CharacterSurface.</summary>
        private bool Eligible()
        {
            if (body == null || animator.runtimeAnimatorController == null) return false;
            if (animator.GetComponentsInChildren<Renderer>(true).Length != 1) return false;
            Material[] materials = body.sharedMaterials;
            if (materials.Length == 0 || materials.Length > CrowdDraw.MaxSubmeshes) return false;
            foreach (Material m in materials)
                if (m == null || m.shader == null || m.shader.name != "Custom/CharacterSurface") return false;
            return true;
        }

        private void OnEnable() { if (started) Register(); }

        private void OnDisable()
        {
            // A corpse disables its scripts: it keeps the look it has (its last pose, or the end of the fall) and its place
            // in the batch, where it is still followed as it sinks
            if (index < 0) return;
            int last = Active.Count - 1;
            Active[index] = Active[last];
            Active[index].index = index;
            Active.RemoveAt(last);
            index = -1;
        }

        private void OnDestroy()
        {
            if (combat != null) combat.OnAttackStarted -= HandleAttack;
            if (health != null) health.OnDeath -= HandleDeath;
            Release();
        }

        private void Register()
        {
            if (index >= 0) return;
            index = Active.Count;
            Active.Add(this);
            EnsureDriver();
        }

        private static void EnsureDriver()
        {
            if (!Application.isPlaying || driverObject != null) return;
            driverObject = new GameObject("GpuSkinnedCharacterDriver") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(driverObject);
            driverObject.AddComponent<Driver>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Active.Clear(); Loose.Clear(); InCrowd.Clear(); cursor = 0; driverObject = null; gpuEnabled = true; ForceGpu = false; ViewerOverride = null; }

        private sealed class Driver : MonoBehaviour
        {
            private float nextAudit;

            private void Update()
            {
                if (Time.unscaledTime >= nextAudit) { nextAudit = Time.unscaledTime + 5f; AuditNow(); }
                if (Active.Count == 0) return;
                Camera cam = ViewerOverride != null ? ViewerOverride : ViewCamera.Current;
                if (cam == null) return;
                // A sixteenth of them a frame (a quarter was 1300 objects touched every frame among five thousand)
                int n = Mathf.Min(Active.Count, Mathf.Max(32, Active.Count / 16));
                for (int i = 0; i < n; i++)
                {
                    cursor = (cursor + 1) % Active.Count;
                    GpuSkinnedCharacter c = Active[cursor];
                    // One the crowd's job moves is looked at when the job says so; here only once in a while (a new mesh, a new kit)
                    if (c.ready && !ForceGpu && (object)c.mover != null && c.mover.crowdIndex >= 0 && AI.CrowdSim.Running && (++c.passes & 3) != 0) continue;
                    c.Refresh(cam);
                }
            }

            // Whoever is in the batch and not moved by the crowd's job stands where its transform does (after everything moved)
            private void LateUpdate()
            {
                Follow(Loose);
                if (!AI.CrowdSim.Running) Follow(InCrowd);
            }

            private static void Follow(List<GpuSkinnedCharacter> who)
            {
                for (int i = who.Count - 1; i >= 0; i--)
                {
                    GpuSkinnedCharacter c = who[i];
                    if (c == null || c.poseRoot == null) { if ((object)c != null) c.Release(); else who.RemoveAt(i); continue; }
                    c.poseRoot.GetPositionAndRotation(out Vector3 p, out Quaternion r);
                    CrowdDraw.SetPose(c.slot, p, r);
                }
            }
        }

        /// <summary>Re-evaluates every character for this camera now (before a screenshot from a capture camera).</summary>
        public static void RefreshAll(Camera cam)
        {
            foreach (GpuSkinnedCharacter c in Active.ToArray()) c.Refresh(cam);
            foreach (GpuSkinnedCharacter c in Loose) if (c != null && c.poseRoot != null) { c.poseRoot.GetPositionAndRotation(out Vector3 p, out Quaternion r); CrowdDraw.SetPose(c.slot, p, r); }
            if (!AI.CrowdSim.Running) foreach (GpuSkinnedCharacter c in InCrowd) if (c != null && c.poseRoot != null) { c.poseRoot.GetPositionAndRotation(out Vector3 p, out Quaternion r); CrowdDraw.SetPose(c.slot, p, r); }
        }

        private bool Fed => (object)mover != null && mover.crowdIndex >= 0 && AI.CrowdSim.Running;

        /// <summary>
        /// A check of the crowd's batch (tests): each place has one character, every clip it plays lies within its own bake,
        /// and the crowd's job writes no unit into a place that is not its body's. Empty when all is well, else what is wrong.
        /// </summary>
        public static string AuditBatch()
        {
            var problems = new System.Text.StringBuilder();
            var owner = new Dictionary<int, GpuSkinnedCharacter>();
            foreach (List<GpuSkinnedCharacter> who in new[] { Loose, InCrowd })
                foreach (GpuSkinnedCharacter c in who)
                {
                    if (c == null) continue;
                    if (c.slot < 0) { problems.AppendLine($"{c.name}: listed without a place"); continue; }
                    if (owner.TryGetValue(c.slot, out GpuSkinnedCharacter other)) problems.AppendLine($"place {c.slot}: {other.name} and {c.name}");
                    owner[c.slot] = c;
                    if (!CrowdDraw.Taken(c.slot)) problems.AppendLine($"{c.name}: place {c.slot} given back but still held");
                    if (c.bake == null || c.bake.Texture == null) { problems.AppendLine($"{c.name}: no bake"); continue; }
                    int rows = c.bake.Texture.height - 1; // (the last row is the upper-body mask)
                    void Within(string what, Vector4 clip)
                    {
                        int first = Mathf.RoundToInt(clip.x), frames = Mathf.RoundToInt(Mathf.Abs(clip.y));
                        if (first < 0 || frames < 1 || first + frames > rows) problems.AppendLine($"{c.name}: {what} rows {first}..{first + frames - 1} outside its bake's {rows}");
                    }
                    Within("clip", CrowdDraw.GetLoco(c.slot));
                    Within("clip before", CrowdDraw.GetPrev(c.slot));
                    Vector4 swing = CrowdDraw.GetSwing(c.slot);
                    if (swing.w >= 0f) Within("swing", swing);
                }
            var draws = new List<(AI.AIBaseController unit, int slot)>();
            AI.CrowdSim.Draws(draws);
            foreach ((AI.AIBaseController unit, int slot) in draws)
                if (!owner.TryGetValue(slot, out GpuSkinnedCharacter c) || (object)c.mover != unit)
                    problems.AppendLine($"{(unit != null ? unit.name : "?")}: the crowd's job writes it into place {slot}, which is {(c != null ? c.name + "'s" : "nobody's")}");
            return problems.ToString();
        }

        private void Refresh(Camera cam)
        {
            if (!ready && !Setup()) return;
            float h = ScreenHeight(cam);
            bool wantFar = gpuEnabled && !failed && CrowdDraw.Usable && (ForceGpu || (IsFar ? h < FarScreenHeight * Hysteresis : h < FarScreenHeight));
            if (wantFar != IsFar) SetFar(wantFar);
            if (!IsFar) return;

            if ((CurrentSource() != sourceMesh || MaterialsChanged()) && !Build()) { SetFar(false); return; }
            if (!dead && !Fed) FollowLocomotion();
        }

        // The view, asked of the camera once a frame and not once a character (six calls into the engine each, a quarter of
        // ten thousand characters a frame)
        private static int viewFrame = -1;
        private static Camera viewCamera;
        private static bool viewOrtho;
        private static Vector3 viewEye;
        private static float viewScale;

        private float ScreenHeight(Camera cam)
        {
            if (viewFrame != Time.frameCount || viewCamera != cam)
            {
                viewFrame = Time.frameCount;
                viewCamera = cam;
                viewOrtho = cam.orthographic;
                viewEye = cam.transform.position;
                viewScale = (viewOrtho ? CharacterHeight / (2f * cam.orthographicSize) : CharacterHeight / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad))) * QualitySettings.lodBias;
            }
            return viewOrtho ? viewScale : viewScale / Mathf.Max(0.01f, Vector3.Distance(viewEye, transform.position));
        }

        // Knights draw their distant mesh; everyone else the body as it is (with its merged gear)
        private Mesh CurrentSource() => lod != null && lod.Far != null ? lod.Far : body.sharedMesh;

        private static readonly List<Material> materialsNow = new List<Material>();

        private bool MaterialsChanged()
        {
            if (sourceMaterials == null) return true;
            body.GetSharedMaterials(materialsNow); // (no array made: asked of thousands a frame)
            if (materialsNow.Count != sourceMaterials.Length) return true;
            for (int i = 0; i < materialsNow.Count; i++) if ((object)materialsNow[i] != sourceMaterials[i]) return true;
            return false;
        }

        /// <summary>Its place in the batch, with the current body's mesh and materials.</summary>
        private bool Build()
        {
            Mesh source = CurrentSource();
            bake = GpuSkinBake.For(animator, transform, body, source);
            Mesh mesh = bake != null ? GpuSkinBake.SkinMesh(source) : null;
            Mesh low = mesh != null ? GpuSkinBake.LowMesh(mesh) : null;
            Material[] src = body.sharedMaterials;
            if (mesh == null || low == null || src.Length == 0 || src.Length > CrowdDraw.MaxSubmeshes) { failed = true; return false; }
            var mats = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                mats[i] = bake.MaterialFor(src[i]);
                if (mats[i] == null) { failed = true; return false; }
            }
            // The body's frame in the unit's own: the unit stands at a point and is turned, the body keeps its place in it
            Matrix4x4 local = Matrix4x4.Scale(poseRoot.lossyScale) * (poseRoot.worldToLocalMatrix * body.transform.localToWorldMatrix);
            // Testers twice saw a villager in the batch stretched across the sky (0.3.10, 0.3.11), and how is not known: a body
            // that would stand far off a man's size in its first baked pose stays on its Animator, and the log says why
            float tall = DrawnHeight(mesh, bake, local);
            if (tall > MaxDrawnHeight || tall < MinDrawnHeight)
            {
                if (sizeWarnings++ < 20)
                    Debug.LogWarning($"[GpuSkin] {Describe()}: in the batch it would stand {tall:0.00} m tall; kept on its Animator. Local scale {local.lossyScale}, " +
                                     $"body scale {body.transform.lossyScale}, root scale {poseRoot.lossyScale}, mesh {mesh.name} ({mesh.vertexCount} vertices, source {source.name}), " +
                                     $"bones {body.bones.Length}, bindposes {source.bindposeCount}, controller {animator.runtimeAnimatorController.name}");
                failed = true;
                return false;
            }
            bool shadows = castsShadows && body.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off;
            if (slot < 0)
            {
                poseRoot.GetPositionAndRotation(out Vector3 p, out Quaternion r);
                slot = CrowdDraw.Add(mesh, low, mats, body.gameObject.layer, shadows, local, p, r);
                if (slot < 0) { failed = true; return false; }
            }
            else if (!CrowdDraw.SetKind(slot, mesh, low, mats, body.gameObject.layer, shadows, local)) { failed = true; return false; }
            sourceMesh = source;
            sourceMaterials = src;
            Relist();
            return true;
        }

        /// <summary>A body in the batch taller or shorter than this (its first baked pose) is not put there.</summary>
        private const float MaxDrawnHeight = 4.5f, MinDrawnHeight = 0.25f;
        private static int sizeWarnings, auditWarnings;
        /// <summary>A hunt for a fault ("-gpudebug noswing"): no attack is played on a body in the batch.</summary>
        public static bool NoSwing;
        /// <summary>A hunt for a fault ("-gpudebug noloose"): bodies nobody's crowd moves (the player's people) stay on their Animators.</summary>
        public static bool NoLoose;
        /// <summary>Bodies kept from the batch, or sent back from it, for the size they were drawn at (tests: none for a sound one).</summary>
        public static int SizeWarnings => sizeWarnings;
        private static readonly List<Vector3> heightVerts = new List<Vector3>();
        private static readonly List<Vector4> heightBones = new List<Vector4>();

        private string Describe() => (poseRoot != null && poseRoot != transform ? poseRoot.name + "/" : "") + name;

        /// <summary>How tall the body stands in the batch in the first baked pose (its main bone per vertex, a few hundred vertices).</summary>
        private static float DrawnHeight(Mesh mesh, GpuSkinBake bake, Matrix4x4 local)
        {
            var points = Samples(mesh); // (a few dozen points a mesh, read once: thousands enter the batch at once when the map opens)
            if (bake.FirstPose == null || points == null) return 1.8f; // (cannot tell: let it be)
            float lo = float.MaxValue, hi = float.MinValue;
            foreach ((Vector3 at, int b) in points)
            {
                if (b < 0 || b >= bake.FirstPose.Length) return float.PositiveInfinity; // a bone the bake has not
                float y = local.MultiplyPoint3x4(bake.FirstPose[b].MultiplyPoint3x4(at)).y;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
            return hi - lo;
        }

        // ------------------------------------------------------------------ the size it is drawn at now, worked out as the shader does

        private static readonly Dictionary<Mesh, (Vector3 at, int bone)[]> samples = new Dictionary<Mesh, (Vector3, int)[]>();

        /// <summary>A few dozen points of a mesh with the bone each hangs on most (null: it cannot be read).</summary>
        private static (Vector3 at, int bone)[] Samples(Mesh mesh)
        {
            if (mesh == null) return null;
            if (samples.TryGetValue(mesh, out var s)) return s;
            s = null;
            if (mesh.isReadable)
            {
                mesh.GetVertices(heightVerts);
                mesh.GetUVs(5, heightBones);
                if (heightVerts.Count > 0 && heightBones.Count == heightVerts.Count)
                {
                    int n = Mathf.Min(48, heightVerts.Count), step = heightVerts.Count / n;
                    s = new (Vector3, int)[n];
                    for (int i = 0; i < n; i++) s[i] = (heightVerts[i * step], (int)heightBones[i * step].x);
                }
            }
            samples[mesh] = s;
            return s;
        }

        /// <summary>
        /// The height a place of the batch is drawn at now, from what the batch holds alone: its kind's mesh, the bake its
        /// material samples, its clips and the matrix that went to the GPU (world: up is up). For any place, owned or not.
        /// </summary>
        private static float WorldHeight(int place, out string note)
        {
            note = "";
            Mesh mesh = CrowdDraw.KindMesh(place);
            GpuSkinBake used = GpuSkinBake.OfMaterial(CrowdDraw.KindMaterial(place));
            var points = Samples(mesh);
            if (used == null || points == null) { note = used == null ? "no bake for its material" : "mesh unreadable"; return -1f; }
            float now = Time.timeSinceLevelLoad;
            Vector4 loco = CrowdDraw.GetLoco(place), prev = CrowdDraw.GetPrev(place), swing = CrowdDraw.GetSwing(place), fade = CrowdDraw.GetFade(place);
            GpuSkinBake.ClipRows(loco, now, out int a0, out int a1, out float ak);
            float fadeK = fade.y > 0f ? Mathf.Clamp01((now - fade.x) / fade.y) : 1f;
            int p0 = 0, p1 = 0; float pk = 0f;
            if (fadeK < 1f) GpuSkinBake.ClipRows(prev, now, out p0, out p1, out pk);
            float swingW = 0f; int s0 = 0, s1 = 0; float sk = 0f;
            Matrix4x4 seat = Matrix4x4.zero;
            if (swing.w >= 0f)
            {
                float t = now - swing.w, length = Mathf.Abs(swing.y) / swing.z;
                swingW = Mathf.Clamp01(t * 12f) * Mathf.Clamp01(1f - (t - (length - 0.18f)) * 5f);
                if (swingW > 0f)
                {
                    GpuSkinBake.ClipRows(swing, now, out s0, out s1, out sk);
                    int anchor = (int)used.Info.x;
                    seat = used.Bone(anchor, a0, a1, ak) * used.Bone(anchor + 3, s0, s1, sk);
                }
            }
            Matrix4x4 world = CrowdDraw.GetObjectToWorld(place);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach ((Vector3 at, int bone) in points)
            {
                int column = bone * 3;
                Matrix4x4 m = used.Bone(column, a0, a1, ak);
                if (fadeK < 1f) m = LerpMatrix(used.Bone(column, p0, p1, pk), m, fadeK);
                if (swingW > 0f)
                {
                    float mask = used.Mask(column);
                    if (mask > 0f) m = LerpMatrix(m, seat * used.Bone(column, s0, s1, sk), swingW * mask);
                }
                float y = world.MultiplyPoint3x4(m.MultiplyPoint3x4(at)).y;
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
            }
            return hi - lo;
        }

        /// <summary>
        /// Every place of the batch now, to the log: how many, and each drawn far off a man's size or owned by nobody, with all
        /// it holds (the smoke run calls this beside its shots: "-smokemass").
        /// </summary>
        public static void DumpBatch(string label)
        {
            var owners = new Dictionary<int, GpuSkinnedCharacter>();
            foreach (List<GpuSkinnedCharacter> who in new[] { Loose, InCrowd })
                foreach (GpuSkinnedCharacter c in who) if (c != null && c.slot >= 0) owners[c.slot] = c;
            var lines = new System.Text.StringBuilder();
            int taken = 0, orphans = 0, wrong = 0;
            for (int place = 0; place < CrowdDraw.High; place++)
            {
                if (!CrowdDraw.Taken(place)) continue;
                taken++;
                byte flags = CrowdDraw.GetFlags(place);
                owners.TryGetValue(place, out GpuSkinnedCharacter owner);
                float tall = WorldHeight(place, out string note);
                bool odd = owner == null || tall > MaxDrawnHeight || (tall >= 0f && tall < MinDrawnHeight && (flags & CrowdDraw.Dead) == 0) || note.Length > 0;
                if (owner == null) orphans++;
                if (!odd) continue;
                wrong++;
                if (wrong > 40) continue;
                Matrix4x4 w = CrowdDraw.GetObjectToWorld(place);
                lines.AppendLine($"  place {place}: {(owner != null ? owner.Describe() + (owner.mover != null && owner.mover.crowdIndex >= 0 ? " (crowd)" : " (loose)") : "NO OWNER")}, {tall:0.00} m {note}; flags {flags}, " +
                                 $"pos {CrowdDraw.GetPos(place)}, rot {CrowdDraw.GetRot(place)} |{CrowdDraw.GetRot(place).x * CrowdDraw.GetRot(place).x + CrowdDraw.GetRot(place).y * CrowdDraw.GetRot(place).y + CrowdDraw.GetRot(place).z * CrowdDraw.GetRot(place).z + CrowdDraw.GetRot(place).w * CrowdDraw.GetRot(place).w:0.000}|, " +
                                 $"world scale {w.lossyScale}, local scale {CrowdDraw.GetLocal(place).lossyScale}, clip {CrowdDraw.GetLoco(place)}, before {CrowdDraw.GetPrev(place)}, swing {CrowdDraw.GetSwing(place)}, fade {CrowdDraw.GetFade(place)}, " +
                                 $"mesh {(CrowdDraw.KindMesh(place) != null ? CrowdDraw.KindMesh(place).name : "-")}, owner's bake rows {(owner != null && owner.bake != null ? owner.bake.Texture.height - 1 : -1)}");
            }
            // Each kind once: its meshes' every weight on a bone its bake has, weights summing to 1, all its materials one bake's
            var seenKinds = new HashSet<int>();
            var kindLines = new System.Text.StringBuilder();
            var w4 = new List<Vector4>();
            var b4 = new List<Vector4>();
            for (int place = 0; place < CrowdDraw.High; place++)
            {
                int kind = CrowdDraw.KindOf(place);
                if (kind < 0 || !seenKinds.Add(kind)) continue;
                Material[] mats = CrowdDraw.KindMaterials(place);
                GpuSkinBake used = GpuSkinBake.OfMaterial(mats != null && mats.Length > 0 ? mats[0] : null);
                int bakeBones = used != null ? (int)used.Info.x / 3 : -1;
                var notes = new System.Text.StringBuilder();
                if (mats != null) for (int s = 1; s < mats.Length; s++) if (GpuSkinBake.OfMaterial(mats[s]) != used) notes.Append($"material {s} is another bake's; ");
                if (mats != null) for (int s = 0; s < mats.Length; s++) if (used != null && mats[s] != null && mats[s].GetTexture("_BoneTex") != used.Texture) notes.Append($"material {s} samples another texture; ");
                foreach (Mesh m in new[] { CrowdDraw.KindMesh(place), CrowdDraw.KindLow(place) })
                {
                    if (m == null || !m.isReadable) { notes.Append("a mesh unreadable; "); continue; }
                    m.GetUVs(4, w4); m.GetUVs(5, b4);
                    if (b4.Count != m.vertexCount || w4.Count != m.vertexCount) { notes.Append($"{m.name}: weights {w4.Count}, bones {b4.Count} of {m.vertexCount} points; "); continue; }
                    int outside = 0, heavy = 0, maxBone = 0; float worst = 1f;
                    for (int i = 0; i < b4.Count; i++)
                    {
                        float sum = 0f;
                        for (int k = 0; k < 4; k++)
                        {
                            sum += w4[i][k];
                            if (w4[i][k] > 0f) { maxBone = Mathf.Max(maxBone, (int)b4[i][k]); if (b4[i][k] < 0f || (int)b4[i][k] >= bakeBones) outside++; }
                        }
                        if (Mathf.Abs(sum - 1f) > 0.02f) { heavy++; if (Mathf.Abs(sum - 1f) > Mathf.Abs(worst - 1f)) worst = sum; }
                    }
                    if (outside > 0 || heavy > 0) notes.Append($"{m.name}: {outside} weights past bone {bakeBones - 1} (up to {maxBone}), {heavy} points weighing not 1 (one {worst:0.###}); ");
                }
                kindLines.AppendLine($"  kind {kind}: {CrowdDraw.KindMesh(place)?.name}, {mats?.Length} materials, {CrowdDraw.KindSubmeshes(place)} submeshes drawn, bake bones {bakeBones} {notes}");
            }
            Debug.Log($"[GpuSkin] batch at {label}: {taken} places, {orphans} with no owner, {wrong} odd, {seenKinds.Count} kinds ({CrowdDraw.KindsMade} made so far); time {Time.timeSinceLevelLoad:0.00}; upload: {CrowdDraw.VerifyUpload()}\n{lines}{kindLines}");
        }

        private static Matrix4x4 LerpMatrix(Matrix4x4 a, Matrix4x4 b, float k)
        {
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i] = a[i] + (b[i] - a[i]) * k;
            return m;
        }

        /// <summary>Every few seconds the batch is checked (AuditBatch); what is wrong goes to the log, a few times at most.</summary>
        private static void AuditNow()
        {
            if (auditWarnings >= 10) return;
            string problems = AuditBatch();
            if (problems.Length == 0) return;
            auditWarnings++;
            Debug.LogWarning("[GpuSkin] the crowd's batch: " + (problems.Length > 1500 ? problems.Substring(0, 1500) + "…" : problems));
        }

        /// <summary>Its place in the batch given back.</summary>
        private void Release()
        {
            if (slot < 0) return;
            if ((object)mover != null && mover.crowdIndex >= 0) AI.CrowdSim.ForgetDraw(mover, slot); // (even if the unit looks to a new body now)
            CrowdDraw.Remove(slot);
            slot = -1;
            sourceMesh = null;
            sourceMaterials = null;
            Relist();
        }

        /// <summary>In the list of those followed here, or of those the crowd's job writes; the job is told its place.</summary>
        private void Relist()
        {
            bool crowd = (object)mover != null && mover.crowdIndex >= 0;
            List<GpuSkinnedCharacter> want = slot < 0 ? null : crowd ? InCrowd : Loose;
            if (want != list)
            {
                if (list != null)
                {
                    int last = list.Count - 1;
                    if (listed >= 0 && listed <= last && list[listed] == this)
                    {
                        list[listed] = list[last];
                        if ((object)list[listed] != null) list[listed].listed = listed;
                        list.RemoveAt(last);
                    }
                    else list.Remove(this);
                }
                list = want;
                listed = -1;
                if (list != null) { listed = list.Count; list.Add(this); EnsureDriver(); }
            }
            if (slot >= 0 && crowd)
            {
                Clips(out Vector2 idle, out Vector2 walk, out Vector2 run);
                AI.CrowdSim.SetDraw(mover, slot, idle, walk, run);
            }
        }

        private void SetFar(bool far)
        {
            if (far == IsFar) return;
            if (far)
            {
                if (!CrowdDraw.Usable) return;
                if (NoLoose && ((object)mover == null || mover.crowdIndex < 0)) return; // ("-gpudebug noloose")
                if (slot < 0 || CurrentSource() != sourceMesh || MaterialsChanged()) { if (!Build()) return; }
                IsFar = true;
                StartPose();
                animator.enabled = false;
                if (footIK != null) footIK.enabled = false;
                if (lod != null) lod.enabled = false;
                body.enabled = false;
                CrowdDraw.SetFlag(slot, CrowdDraw.Shown, !hidden);
                if (driver != null) driver.SetFar(true, dead);
            }
            else
            {
                IsFar = false;
                Release();
                animator.enabled = true;
                if (footIK != null) footIK.enabled = true;
                if (lod != null) lod.SetHidden(hidden);
                body.enabled = !hidden;
                if (lod != null) lod.enabled = true; // re-picks its mesh and hides specks
                if (driver != null) { driver.SetFar(false, dead); driver.Resync(dead); }
            }
        }

        /// <summary>Hidden by its owner (a worker inside a mine): neither body is drawn. Not the same as its renderer being off,
        /// which it also is far off (drawn in the batch) or as a speck on the screen.</summary>
        public bool Hidden => hidden;

        /// <summary>Hidden by its owner (a worker inside a mine): neither body is drawn.</summary>
        public void SetHidden(bool value)
        {
            hidden = value;
            if (lod == null) lod = GetComponent<SkinnedMeshLod>();
            if (lod != null) lod.SetHidden(value);
            if (!ready)
            {
                SkinnedMeshRenderer b = GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (b != null) b.enabled = !value;
                return;
            }
            if (IsFar) { body.enabled = false; CrowdDraw.SetFlag(slot, CrowdDraw.Shown, !hidden); }
            else if (lod == null) body.enabled = !hidden;
        }

        /// <summary>A body that no longer casts a shadow (a corpse).</summary>
        public void SetCastShadows(bool value)
        {
            castsShadows = value;
            CrowdDraw.SetFlag(slot, CrowdDraw.NoShadow, !value);
        }

        private float Now => Time.timeSinceLevelLoad; // the shader's _Time.y

        private void StartPose()
        {
            CrowdDraw.SetFade(slot, Vector4.zero);
            CrowdDraw.SetSwing(slot, new Vector4(0f, 0f, 1f, -1f));
            CrowdDraw.SetFlag(slot, CrowdDraw.Dead, dead);
            CrowdDraw.SetFlag(slot, CrowdDraw.NoShadow, !castsShadows);
            Vector4 loco;
            if (dead && bake.Death.Valid) loco = bake.Death.Play(Now - bake.Death.Length);
            else
            {
                GpuSkinBake.Clip clip = LocomotionClip();
                loco = clip.Play(Now - Random.value * clip.Length); // crowds out of step
            }
            CrowdDraw.SetLoco(slot, loco);
            CrowdDraw.SetPrev(slot, loco);
        }

        private GpuSkinBake.Clip LocomotionClip()
        {
            float speed = driver != null ? driver.Speed : 0f;
            return speed < WalkSpeed ? bake.Idle : speed < RunSpeed ? bake.Walk : bake.Run;
        }

        private void FollowLocomotion()
        {
            GpuSkinBake.Clip clip = LocomotionClip();
            Vector4 loco = CrowdDraw.GetLoco(slot);
            if (clip.FirstRow != Mathf.RoundToInt(loco.x)) { CrossFade(clip, Now); return; }
            // Same clip, new pace: keep the phase, change the rate (the feet follow the ground speed, §11.1)
            float fps = GpuSkinBake.FramesPerSecond * LocoRate(clip);
            if (dead || loco.z <= 0f || Mathf.Abs(fps / loco.z - 1f) < 0.08f) return;
            float frames = (Now - loco.w) * loco.z;
            loco.z = fps;
            loco.w = Now - frames / fps;
            CrowdDraw.SetLoco(slot, loco);
        }

        /// <summary>Playback rate of a locomotion clip for the current speed (walk and run clips have their own ground speed).</summary>
        private float LocoRate(GpuSkinBake.Clip clip)
        {
            float speed = driver != null ? driver.Speed : 0f;
            if (clip.FirstRow == bake.Walk.FirstRow) return CharacterAnimationDriver.ClipRate(speed, CharacterAnimationDriver.WalkClipSpeed);
            if (clip.FirstRow == bake.Run.FirstRow) return CharacterAnimationDriver.ClipRate(speed, CharacterAnimationDriver.RunClipSpeed);
            return 1f;
        }

        private void CrossFade(GpuSkinBake.Clip clip, float start)
        {
            CrowdDraw.SetPrev(slot, CrowdDraw.GetLoco(slot));
            Vector4 loco = clip.Play(start);
            if (!dead) loco.z *= LocoRate(clip);
            CrowdDraw.SetLoco(slot, loco);
            CrowdDraw.SetFade(slot, new Vector4(Now, FadeTime, 0f, 0f));
        }

        private void HandleAttack()
        {
            if (!IsFar || slot < 0 || dead || !bake.Attack.Valid || NoSwing) return;
            if (gear == null) gear = GetComponentInChildren<CharacterGear>(true);
            Vector4 swing = (gear != null && gear.HoldsBow && bake.Shoot.Valid ? bake.Shoot : bake.Attack).Play(Now);
            swing.y = -Mathf.Abs(swing.y); // played once
            CrowdDraw.SetSwing(slot, swing);
        }

        private void HandleDeath()
        {
            dead = true;
            if (!IsFar || slot < 0) return;
            CrowdDraw.SetFlag(slot, CrowdDraw.Dead, true); // the crowd's job leaves its clip alone from here
            if (!bake.Death.Valid) return;
            CrowdDraw.SetSwing(slot, new Vector4(0f, 0f, 1f, -1f));
            CrossFade(bake.Death, Now);
        }
    }
}
