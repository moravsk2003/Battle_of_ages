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
    /// foundation, frame, done — only while workers stand at it (up to MaxBuilders; each adds its hands). Till then its
    /// model is hidden and its working parts are off (a mine does not dig, a hall gives no cap). Workers on their own come
    /// by themselves, others are sent with a right click. In a campaign only: the seed-0 world builds at once.
    /// </summary>
    public sealed class ConstructionSite : MonoBehaviour
    {
        public const int MaxBuilders = 4;
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
        private readonly List<Behaviour> switchedOff = new List<Behaviour>();
        private readonly List<Renderer> hidden = new List<Renderer>();
        private GameObject stageModel;
        private int stage = -1;
        private float half, hammerClock, handsClock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();

        /// <summary>Seconds of one worker's work: a base and more the dearer the building.</summary>
        public static float SecondsFor(BuildingDefinition def) =>
            GameBalance.BuildBaseSeconds + GameBalance.BuildSecondsPerResource * (def.WoodCost + def.OreCost);

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
            return true;
        }

        public void Leave(NpcResourceGatherer worker) => builders.Remove(worker);

        /// <summary>A worker's place round the site.</summary>
        public Vector3 SpotFor(NpcResourceGatherer worker)
        {
            int i = Mathf.Max(0, builders.IndexOf(worker));
            float a = i * Mathf.PI * 0.5f + 0.6f, c = Mathf.Cos(a), s = Mathf.Sin(a);
            // On the square round the footprint: on a circle of a wide building's half side the place lay inside it, the
            // worker stopped at its edge and never counted as there
            float reach = (half + SpotClearance) / Mathf.Max(Mathf.Abs(c), Mathf.Abs(s));
            Vector3 p = transform.position + transform.rotation * new Vector3(c * reach, 0f, s * reach);
            p.y = RtsUnit.GetTerrainHeight(p);
            return p;
        }

        private void Update()
        {
            builders.RemoveAll(b => b == null);
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
                foreach (NpcResourceGatherer b in builders) b.GetComponent<CombatUnit>()?.TriggerAttackAnimation(); // the swing of a hammer
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
            GameSound.At(GameSound.Kind.Place, transform.position);
            Destroy(this);
        }

        /// <summary>Foundation under a third of the work, then the frame rising.</summary>
        private void ShowStage()
        {
            int now = Progress < 1f / 3f ? 0 : 1;
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

        /// <summary>A plank floor on stones; the frame adds corner posts and beams round the footprint.</summary>
        public static Mesh StageMesh(Vector3 size, int stage)
        {
            var kit = new MeshKit();
            Color stone = new Color(0.45f, 0.43f, 0.4f), plank = new Color(0.62f, 0.46f, 0.28f), post = new Color(0.5f, 0.36f, 0.2f);
            float w = Mathf.Max(1.5f, size.x), d = Mathf.Max(1.5f, size.z), h = Mathf.Max(1.5f, size.y);
            // Deep: on a slope a site sits on its highest corner, and its footing goes down into the ground instead of hanging over it
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 0.2f - FootingDepth * 0.5f, 0f)), new Vector3(w + 0.3f, FootingDepth, d + 0.3f), 0.03f, Vector3.zero, _ => stone);
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 0.27f, 0f)), new Vector3(w, 0.1f, d), 0.02f, Vector3.zero, _ => plank);
            if (stage >= 1)
            {
                foreach (float x in new[] { -0.5f, 0.5f })
                    foreach (float z in new[] { -0.5f, 0.5f })
                        kit.Box(Matrix4x4.Translate(new Vector3(x * w, h * 0.5f, z * d)), new Vector3(0.2f, h, 0.2f), 0.02f, Vector3.zero, _ => post);
                foreach (float z in new[] { -0.5f, 0.5f })
                    kit.Box(Matrix4x4.Translate(new Vector3(0f, h, z * d)), new Vector3(w + 0.2f, 0.16f, 0.16f), 0.02f, Vector3.zero, _ => post);
                foreach (float x in new[] { -0.5f, 0.5f })
                    kit.Box(Matrix4x4.Translate(new Vector3(x * w, h, 0f)), new Vector3(0.16f, 0.16f, d + 0.2f), 0.02f, Vector3.zero, _ => post);
            }
            return kit.ToMesh("ConstructionStage");
        }
    }
}
