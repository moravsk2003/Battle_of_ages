using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.UI;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// A building going up (docs/GAME_DESIGN.md §10.2, §18.3, M5 step 2): paid for when placed, it grows by stages —
    /// foundation, frame, walls, rafters, done (Stages) — only while workers stand at it (up to MaxBuilders; each adds its hands). Till then its
    /// model is hidden and its working parts are off (a mine does not dig, a hall gives no cap). Workers on their own come
    /// by themselves, others are sent with a right click. In a campaign only: the seed-0 world builds at once.
    /// </summary>
    public sealed class ConstructionSite : MonoBehaviour
    {
        public const int MaxBuilders = 4;
        /// <summary>What a site shows as it goes: footing, posts, the frame, walls half up, walls and rafters.</summary>
        public const int Stages = 5;
        /// <summary>A builder this near its place swings the hammer; one still walking there does not.</summary>
        public const float AtSpot = 1.2f;
        private const float FootingDepth = 4f;
        private const float BuilderReach = 2.2f, SpotClearance = 1f, HammerEvery = 0.8f, HandsEvery = 2f;

        private static readonly List<ConstructionSite> all = new List<ConstructionSite>();
        public static IReadOnlyList<ConstructionSite> All => all;

        public BuildingType Type { get; private set; }
        /// <summary>0..1 of the work done.</summary>
        public float Progress { get; private set; }
        public float WorkSeconds { get; private set; }
        public int BuilderCount => builders.Count;
        public IReadOnlyList<NpcResourceGatherer> Builders => builders;
        public int PresentBuilders { get; private set; }
        public bool NeedsHands => builders.Count < MaxBuilders;
        public string DisplayName => BuildingData.GetDefinition(Type).DisplayName;

        private readonly List<NpcResourceGatherer> builders = new List<NpcResourceGatherer>(MaxBuilders);
        /// <summary>Each builder's side (0 +x, 1 -x, 2 +z, 3 -z), taken when it joins and kept: a builder does not walk round the site when another leaves.</summary>
        private readonly Dictionary<NpcResourceGatherer, int> sides = new Dictionary<NpcResourceGatherer, int>();
        private Vector2 halfSize;
        private readonly List<Behaviour> switchedOff = new List<Behaviour>();
        private readonly List<Renderer> hidden = new List<Renderer>();
        private GameObject stageModel;
        private int stage = -1;
        private float half, hammerClock, handsClock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();

        /// <summary>Seconds of one worker's work: a base and more the dearer the building.</summary>
        public static float SecondsFor(BuildingDefinition def) =>
            GameBalance.BuildBaseSeconds + GameBalance.BuildSecondsPerResource * ((float)def.WoodCost / GameBalance.WoodPrice + def.OreCost);

        /// <summary>A placed building becomes a site (progress: from a save).</summary>
        public static ConstructionSite Begin(GameObject building, BuildingType type, float progress = 0f)
        {
            ConstructionSite site = building.AddComponent<ConstructionSite>();
            site.Setup(type, progress);
            return site;
        }

        /// <summary>Is a hall going up (a second one may not be placed meanwhile)?</summary>
        public static bool IsBuilding(BuildingType type)
        {
            foreach (ConstructionSite s in all) if (s != null && s.Type == type) return true;
            return false;
        }

        /// <summary>The nearest site in reach that wants hands, or null.</summary>
        public static ConstructionSite NearestNeedingHands(Vector3 from, float reach)
        {
            ConstructionSite best = null;
            float bestSqr = reach * reach;
            int bestCount = int.MaxValue;
            foreach (ConstructionSite s in all)
            {
                if (s == null || !s.NeedsHands) continue;
                float d = (s.transform.position - from).sqrMagnitude;
                if (d > reach * reach) continue;
                // One on each first: the emptiest, then the nearest
                if (s.builders.Count < bestCount || (s.builders.Count == bestCount && d < bestSqr)) { bestCount = s.builders.Count; bestSqr = d; best = s; }
            }
            return best;
        }

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() => all.Remove(this);

        private void Setup(BuildingType type, float progress)
        {
            Type = type;
            Progress = Mathf.Clamp01(progress);
            WallSegment wall = GetComponent<WallSegment>();
            if (wall != null) wall.UnderConstruction = true; // the next segment can join it already
            foreach (Behaviour b in GetComponentsInChildren<Behaviour>())
            {
                if (b == this || b is BuildingNode || !b.enabled) continue;
                b.enabled = false;
                switchedOff.Add(b);
            }
            BuildingDefinition def = BuildingData.GetDefinition(type); // (after: a hall that stands no more does not count itself as the first)
            WorkSeconds = SecondsFor(def);
            half = Mathf.Max(def.Size.x, def.Size.z) * 0.5f;
            halfSize = new Vector2(def.Size.x, def.Size.z) * 0.5f;
            if (type == BuildingType.Field) World.GrassFieldRenderer.ForgetTiles(); // the plough takes the grass from the start
            foreach (Renderer r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
            HealthComponent hp = GetComponent<HealthComponent>();
            if (hp != null) hp.InitializeHealth(Mathf.Max(1, def.MaxHealth / 4)); // scaffolding breaks easily
            ShowStage();
        }

        /// <summary>A worker takes a place here; false when the site is full.</summary>
        public bool Join(NpcResourceGatherer worker)
        {
            if (worker == null || builders.Contains(worker)) return worker != null;
            if (!NeedsHands) return false;
            builders.Add(worker);
            sides[worker] = NearestFreeSide(worker.transform.position);
            return true;
        }

        public void Leave(NpcResourceGatherer worker)
        {
            builders.Remove(worker);
            if (worker != null) sides.Remove(worker);
        }

        /// <summary>The free side nearest to where the worker comes from: it does not cross or walk round the site.</summary>
        private int NearestFreeSide(Vector3 from)
        {
            int best = 0;
            float bestSqr = float.MaxValue;
            for (int side = 0; side < MaxBuilders; side++)
            {
                if (sides.ContainsValue(side)) continue;
                float d = (SidePlace(side) - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = side; }
            }
            return best;
        }

        /// <summary>The middle of one side of the footprint, a step out from it.</summary>
        private Vector3 SidePlace(int side)
        {
            Vector3 local = side == 0 ? new Vector3(halfSize.x + SpotClearance, 0f, 0f)
                          : side == 1 ? new Vector3(-halfSize.x - SpotClearance, 0f, 0f)
                          : side == 2 ? new Vector3(0f, 0f, halfSize.y + SpotClearance)
                          : new Vector3(0f, 0f, -halfSize.y - SpotClearance);
            Vector3 p = transform.position + transform.rotation * local;
            p.y = RtsUnit.GetTerrainHeight(p);
            return p;
        }

        /// <summary>A worker's place round the site: the middle of its own side.</summary>
        public Vector3 SpotFor(NpcResourceGatherer worker)
        {
            if (worker == null || !sides.TryGetValue(worker, out int side)) side = 0;
            return SidePlace(side);
        }

        /// <summary>This builder stands at its place (and swings the hammer).</summary>
        public bool AtItsSpot(NpcResourceGatherer worker)
        {
            if (worker == null) return false;
            Vector3 off = worker.transform.position - SpotFor(worker);
            off.y = 0f;
            return off.sqrMagnitude <= AtSpot * AtSpot;
        }

        private void Update()
        {
            if (builders.RemoveAll(b => b == null) > 0)
            {
                var gone = new List<NpcResourceGatherer>();
                foreach (NpcResourceGatherer k in sides.Keys) if (k == null) gone.Add(k);
                foreach (NpcResourceGatherer k in gone) sides.Remove(k);
            }
            int present = 0;
            foreach (NpcResourceGatherer b in builders)
            {
                // At the building, wherever along it: its own footprint can keep one off the very place
                Vector3 off = Quaternion.Inverse(transform.rotation) * (b.transform.position - transform.position);
                if (Mathf.Max(Mathf.Abs(off.x), Mathf.Abs(off.z)) <= half + BuilderReach) present++;
            }
            PresentBuilders = present;
            handsClock -= Time.deltaTime;
            if (handsClock <= 0f)
            {
                handsClock = HandsEvery;
                if (builders.Count == 0) CallSomeone();
            }
            if (present == 0) return;
            hammerClock -= Time.deltaTime;
            if (hammerClock <= 0f)
            {
                hammerClock = HammerEvery;
                GameSound.At(GameSound.Kind.Hammer, transform.position);
                foreach (NpcResourceGatherer b in builders)
                    if (AtItsSpot(b)) b.GetComponent<CombatUnit>()?.TriggerAttackAnimation(); // the swing of a hammer, standing at the work
            }
            Work(present * Time.deltaTime);
        }

        /// <summary>
        /// No site stands with no one at it: with no hands it borrows the nearest worker, a free one before one on a job
        /// before a soldier (it goes back to what it did). Not one down a mine, at a bench or in a fight, nor one the player holds.
        /// </summary>
        private void CallSomeone()
        {
            NpcResourceGatherer best = null;
            int bestKind = int.MaxValue;
            float bestSqr = GameBalance.BuildersComeFrom * GameBalance.BuildersComeFrom;
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
            {
                if (w == null || w.AtSite || w.IsMiner || w.AtBench || SettlementAlarm.ActiveAt(w.transform.position)) continue;
                RtsUnit u = w.GetComponent<RtsUnit>();
                if (u == null || !u.IsAlive || u.IsSelected || u.HasAttackTarget || u.WallBound) continue;
                int kind = w.CurrentDirective == GatherDirective.Idle ? 2 : w.CurrentDirective == GatherDirective.Auto && !w.AtPlace ? 0 : 1;
                float d = (w.transform.position - transform.position).sqrMagnitude;
                if (d > GameBalance.BuildersComeFrom * GameBalance.BuildersComeFrom) continue;
                if (kind < bestKind || (kind == bestKind && d < bestSqr)) { best = w; bestKind = kind; bestSqr = d; }
            }
            if (best != null) best.AssignToSite(this, lent: bestKind > 0);
        }

        /// <summary>This many worker-seconds of work (the tests call it directly).</summary>
        public void Work(float workerSeconds)
        {
            Progress = Mathf.Clamp01(Progress + workerSeconds / Mathf.Max(0.01f, WorkSeconds));
            ShowStage();
            if (Progress >= 1f) Finish();
        }

        private void Finish()
        {
            foreach (NpcResourceGatherer b in new List<NpcResourceGatherer>(builders)) if (b != null) b.LeaveSite(this);
            if (stageModel != null) Destroy(stageModel);
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
            foreach (Behaviour b in switchedOff) if (b != null) b.enabled = true;
            WallSegment wall = GetComponent<WallSegment>();
            if (wall != null) wall.UnderConstruction = false;
            all.Remove(this);
            GetComponent<BuildingNode>()?.OnBuilt();
            GetComponent<TownHall>()?.Founded();
            StrategyHud.Notify("Збудовано: " + DisplayName);
            GameSound.At(GameSound.Kind.BuildingFinished, transform.position);
            Destroy(this);
        }

        /// <summary>The stage the work has come to: 0 at the start, Stages - 1 near the end.</summary>
        public static int StageOf(float progress) => Mathf.Clamp(Mathf.FloorToInt(progress * Stages), 0, Stages - 1);

        /// <summary>The stage shown now (tests).</summary>
        public int Stage => stage;

        /// <summary>Footing first, then the posts, the frame, the walls going up and the rafters (StageMesh).</summary>
        private void ShowStage()
        {
            int now = StageOf(Progress);
            if (now == stage) return;
            stage = now;
            if (stageModel != null) Destroy(stageModel);
            Material look = MeshKit.PropMaterial();
            if (look == null) return; // no shader: nothing drawn, the work still goes on
            BuildingDefinition def = BuildingData.GetDefinition(Type);
            stageModel = new GameObject("ConstructionStage");
            stageModel.transform.SetParent(transform, false);
            stageModel.AddComponent<MeshFilter>().sharedMesh = Type == BuildingType.Field ? Utils.ZoneBuildingBuilder.Draped(Utils.ZoneBuildingBuilder.FieldSite, transform) : StageMesh(def.Size, now);
            MeshRenderer r = stageModel.AddComponent<MeshRenderer>();
            r.sharedMaterial = look;
        }

        /// <summary>
        /// The site at a stage: 0 a plank floor on stones; 1 corner posts half up; 2 the whole frame with its top beams;
        /// 3 plank walls half up, a doorway in the front; 4 the walls up and the rafters. Each keeps all of the one before.
        /// </summary>
        public static Mesh StageMesh(Vector3 size, int stage)
        {
            var kit = new MeshKit();
            Color stone = new Color(0.45f, 0.43f, 0.4f), plank = new Color(0.62f, 0.46f, 0.28f), post = new Color(0.5f, 0.36f, 0.2f);
            float w = Mathf.Max(1.5f, size.x), d = Mathf.Max(1.5f, size.z), h = Mathf.Max(1.5f, size.y);
            // Deep: on a slope a site sits on its highest corner, and its footing goes down into the ground instead of hanging over it
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 0.2f - FootingDepth * 0.5f, 0f)), new Vector3(w + 0.3f, FootingDepth, d + 0.3f), 0.03f, Vector3.zero, _ => stone);
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 0.27f, 0f)), new Vector3(w, 0.1f, d), 0.02f, Vector3.zero, _ => plank);
            const float floor = 0.32f;
            Color board = new Color(0.68f, 0.52f, 0.33f);
            if (stage >= 1)
            {
                float postH = stage == 1 ? h * 0.5f : h;
                foreach (float x in new[] { -0.5f, 0.5f })
                    foreach (float z in new[] { -0.5f, 0.5f })
                        kit.Box(Matrix4x4.Translate(new Vector3(x * w, floor + postH * 0.5f, z * d)), new Vector3(0.2f, postH, 0.2f), 0.02f, Vector3.zero, _ => post);
            }
            if (stage >= 2)
            {
                foreach (float z in new[] { -0.5f, 0.5f })
                    kit.Box(Matrix4x4.Translate(new Vector3(0f, floor + h, z * d)), new Vector3(w + 0.2f, 0.16f, 0.16f), 0.02f, Vector3.zero, _ => post);
                foreach (float x in new[] { -0.5f, 0.5f })
                    kit.Box(Matrix4x4.Translate(new Vector3(x * w, floor + h, 0f)), new Vector3(0.16f, 0.16f, d + 0.2f), 0.02f, Vector3.zero, _ => post);
                // A middle post on the front and the back: the frame is not a bare box
                foreach (float z in new[] { -0.5f, 0.5f })
                    kit.Box(Matrix4x4.Translate(new Vector3(0f, floor + h * 0.5f, z * d)), new Vector3(0.16f, h, 0.16f), 0.02f, Vector3.zero, _ => post);
            }
            if (stage >= 3)
            {
                // Plank walls, half up, then whole; a doorway left in the front (-z)
                float wallH = stage == 3 ? h * 0.5f : h * 0.92f, y = floor + wallH * 0.5f, t = 0.08f;
                float door = Mathf.Min(1.1f, w * 0.3f), piece = (w - door) * 0.5f;
                kit.Box(Matrix4x4.Translate(new Vector3(-(door + piece) * 0.5f, y, -0.5f * d)), new Vector3(piece, wallH, t), 0.01f, Vector3.zero, _ => board);
                kit.Box(Matrix4x4.Translate(new Vector3((door + piece) * 0.5f, y, -0.5f * d)), new Vector3(piece, wallH, t), 0.01f, Vector3.zero, _ => board);
                kit.Box(Matrix4x4.Translate(new Vector3(0f, y, 0.5f * d)), new Vector3(w, wallH, t), 0.01f, Vector3.zero, _ => board);
                foreach (float x in new[] { -0.5f, 0.5f })
                    kit.Box(Matrix4x4.Translate(new Vector3(x * w, y, 0f)), new Vector3(t, wallH, d), 0.01f, Vector3.zero, _ => board);
            }
            if (stage >= 4)
            {
                // Rafters: pairs leaning to a ridge along x
                float rise = Mathf.Min(1.4f, d * 0.45f), top = floor + h;
                float slope = Mathf.Atan2(rise, d * 0.5f) * Mathf.Rad2Deg, length = Mathf.Sqrt(rise * rise + d * d * 0.25f) + 0.2f;
                int pairs = Mathf.Max(3, Mathf.RoundToInt(w / 1.1f));
                for (int i = 0; i < pairs; i++)
                {
                    float x = Mathf.Lerp(-0.5f * w, 0.5f * w, i / (float)(pairs - 1));
                    foreach (float sideZ in new[] { -1f, 1f })
                    {
                        Matrix4x4 m = Matrix4x4.TRS(new Vector3(x, top + rise * 0.5f, sideZ * d * 0.25f), Quaternion.Euler(sideZ * slope, 0f, 0f), Vector3.one);
                        kit.Box(m, new Vector3(0.12f, 0.12f, length), 0.01f, Vector3.zero, _ => post);
                    }
                }
                kit.Box(Matrix4x4.Translate(new Vector3(0f, top + rise, 0f)), new Vector3(w + 0.2f, 0.14f, 0.14f), 0.01f, Vector3.zero, _ => post);
            }
            return kit.ToMesh("ConstructionStage");
        }
    }
}
