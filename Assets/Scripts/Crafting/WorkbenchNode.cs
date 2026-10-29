using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Resources;
using BattleOfAges.Utils;
using BattleOfAges.RTS;
using BattleOfAges.Player;
using BattleOfAges.UI;

namespace BattleOfAges.Crafting
{
    /// <summary>
    /// Workbench building component where up to 4 NPC workers craft weapons, armor and tools, and melt ore up a tier
    /// (GameBalance.SmeltRatio to one). A called worker goes to its place at the bench (two in front, two behind) and
    /// stays there (NpcResourceGatherer.WorkingAtBench); the crafting speed counts those standing at their places.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorkbenchNode : MonoBehaviour
    {
        private static readonly List<WorkbenchNode> activeWorkbenches = new List<WorkbenchNode>();
        public static IReadOnlyList<WorkbenchNode> ActiveWorkbenches => activeWorkbenches;
        public static WorkbenchNode Instance => (activeWorkbenches.Count > 0) ? activeWorkbenches[0] : null;

        private static Shader cachedLitShader;

        private CraftingQueue craftingQueue;
        private readonly List<RtsUnit> assignedWorkers = new List<RtsUnit>(4);

        public CraftingQueue Queue
        {
            get
            {
                EnsureQueueInitialized();
                return craftingQueue;
            }
        }
        /// <summary>A called worker counts for the crafting speed once it stands within this of its place.</summary>
        public const float SlotReach = 1.6f;
        private static readonly Vector3[] Slots = { new Vector3(-0.7f, 0f, 1.45f), new Vector3(0.7f, 0f, 1.45f), new Vector3(-0.7f, 0f, -1.45f), new Vector3(0.7f, 0f, -1.45f) };
        private int presentWorkers;

        /// <summary>Workers at their places (the crafting speed).</summary>
        public int AssignedWorkerCount => Queue.AssignedWorkerCount;
        /// <summary>Workers called to the bench, there or on the way.</summary>
        public int CalledWorkerCount => assignedWorkers.Count;
        public bool IsWorkerFull => assignedWorkers.Count >= CraftingQueue.MAX_WORKERS;

        private void EnsureQueueInitialized()
        {
            if (craftingQueue == null)
            {
                craftingQueue = new CraftingQueue();
                craftingQueue.OnTaskCompleted += HandleTaskCompleted;
            }
        }

        private void OnEnable()
        {
            if (!activeWorkbenches.Contains(this))
            {
                activeWorkbenches.Add(this);
            }
        }

        private void OnDisable()
        {
            activeWorkbenches.Remove(this);
        }

        private void Awake()
        {
            EnsureQueueInitialized();
        }

        private void Start()
        {
            SnapToTerrain();
        }

        public void SnapToTerrain()
        {
            Vector3 pos = transform.position;
            pos.y = RtsUnit.GetTerrainHeight(pos);
            transform.position = pos;
        }

        private void Update()
        {
            // The speed counts the called workers standing at their places (one still walking up does not craft yet)
            assignedWorkers.RemoveAll(w => w == null);
            int present = 0;
            for (int i = 0; i < assignedWorkers.Count; i++)
            {
                Vector3 off = assignedWorkers[i].transform.position - SlotFlat(i);
                off.y = 0f;
                if (off.sqrMagnitude <= SlotReach * SlotReach) present++;
            }
            if (present != presentWorkers)
            {
                presentWorkers = present;
                craftingQueue.SetWorkerCount(present);
            }
            craftingQueue.Tick(Time.deltaTime);
        }

        /// <summary>Calls the unit to a free place at the bench: a villager works there until called off, anyone else walks there.</summary>
        public bool AssignWorker(RtsUnit worker)
        {
            if (worker == null || IsWorkerFull || assignedWorkers.Contains(worker)) return false;
            CallOff(worker, this);
            assignedWorkers.Add(worker);
            if (worker.TryGetComponent(out NpcResourceGatherer gatherer)) gatherer.AssignToBench(this);
            else worker.IssueMoveCommand(SlotOf(worker));
            return true;
        }

        public bool UnassignWorker(RtsUnit worker)
        {
            if (worker == null || !assignedWorkers.Remove(worker)) return false;
            if (worker.TryGetComponent(out NpcResourceGatherer gatherer)) gatherer.LeaveBench(this);
            return true;
        }

        /// <summary>A unit given another order leaves whatever bench it was called to (but the one it is sent to now).</summary>
        public static void CallOff(RtsUnit unit, WorkbenchNode except = null)
        {
            if (unit == null) return;
            foreach (WorkbenchNode bench in activeWorkbenches)
                if (bench != null && bench != except && bench.assignedWorkers.Contains(unit)) { bench.UnassignWorker(unit); return; }
        }

        /// <summary>The unit's place at the bench (its own position when it is not called here).</summary>
        public Vector3 SlotOf(RtsUnit unit)
        {
            int i = assignedWorkers.IndexOf(unit);
            return i >= 0 ? GetWorkerSlotPosition(i) : (unit != null ? unit.transform.position : transform.position);
        }

        private Vector3 SlotFlat(int index) => transform.position + transform.rotation * Slots[index % Slots.Length];

        public IReadOnlyList<RtsUnit> Workers => assignedWorkers;

        /// <summary>Restores a saved queue (resources were paid when it was queued); smeltTier -1 for an item.</summary>
        public void LoadQueue(IEnumerable<(ItemType type, int level, float progress, int smeltTier, int smeltCount)> tasks)
        {
            Queue.ClearQueue();
            foreach (var t in tasks)
                (t.smeltTier >= 0 ? Queue.EnqueueSmelt(t.smeltTier, t.smeltCount) : Queue.EnqueueItem(t.type, t.level)).ProgressSeconds = t.progress;
        }

        /// <summary>Calls the nearest villager who is not at the bench yet (falls back to any own unit).</summary>
        public RtsUnit AssignNearestFreeWorker()
        {
            assignedWorkers.RemoveAll(w => w == null);
            RtsUnit best = null;
            float bestScore = float.MaxValue;
            foreach (RtsUnit u in FindObjectsByType<RtsUnit>(FindObjectsSortMode.None))
            {
                if (u == null || assignedWorkers.Contains(u) || u.GetComponent<FirstPersonController>() != null) continue;
                NpcResourceGatherer g = u.GetComponent<NpcResourceGatherer>();
                if (g != null && g.IsStationed) continue; // in a mine or at another bench
                float score = Vector3.Distance(transform.position, u.transform.position) + (g != null ? 0f : 1000f);
                if (score < bestScore) { bestScore = score; best = u; }
            }
            return best != null && AssignWorker(best) ? best : null;
        }

        /// <summary>Sends the last called worker away from the bench.</summary>
        public bool UnassignLastWorker()
        {
            assignedWorkers.RemoveAll(w => w == null);
            if (assignedWorkers.Count > 0) return UnassignWorker(assignedWorkers[assignedWorkers.Count - 1]);
            return craftingQueue.UnassignWorker();
        }

        /// <summary>Removes a queued task and gives its wood and ore back.</summary>
        public bool CancelTask(string taskId)
        {
            CraftingTask task = null;
            foreach (CraftingTask t in Queue.Queue) if (t.TaskId == taskId) { task = t; break; }
            if (task == null || !Queue.CancelTask(taskId)) return false;
            ResourceManager rm = ResourceManager.Instance;
            if (rm != null && task.IsSmelt) rm.AddOre(task.SmeltTier, task.SmeltCount * Combat.GameBalance.SmeltRatio);
            else if (rm != null)
            {
                rm.AddResource(ResourceType.Wood, ItemStatCalculator.CalculateWoodCost(task.Type, task.Level));
                rm.AddOre(ItemStatCalculator.CalculateRequiredOreTier(task.Level), ItemStatCalculator.CalculateOreCost(task.Type, task.Level));
            }
            return true;
        }

        /// <summary>Whether the stock covers the item (wood, and ore of the required tier or better).</summary>
        public static bool CanAfford(ItemType type, int level)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return true;
            return rm.WoodCount >= ItemStatCalculator.CalculateWoodCost(type, level)
                && rm.GetOreCountAtLeast(ItemStatCalculator.CalculateRequiredOreTier(level)) >= ItemStatCalculator.CalculateOreCost(type, level);
        }

        /// <summary>Two places in front of the bench and two behind it (the sides have the vise and the anvil).</summary>
        public Vector3 GetWorkerSlotPosition(int slotIndex)
        {
            Vector3 pos = SlotFlat(slotIndex);
            pos.y = RtsUnit.GetTerrainHeight(pos);
            return pos;
        }

        /// <summary>Pieces of the next tier the stock of this tier melts into now (GameBalance.SmeltRatio each).</summary>
        public static int SmeltableCount(int tier)
        {
            ResourceManager rm = ResourceManager.Instance;
            return rm == null || tier < 0 ? 0 : rm.GetOreTierCount(tier) / Combat.GameBalance.SmeltRatio;
        }

        /// <summary>Queues melting count x SmeltRatio ore of the tier into count pieces of the next; the ore is taken now (cancel gives it back).</summary>
        public bool TryEnqueueSmelt(int tier, int count)
        {
            if (count <= 0 || tier < 0 || tier >= OreTierHelper.MAX_TIERS - 1) return false;
            int ore = count * Combat.GameBalance.SmeltRatio;
            ResourceManager rm = ResourceManager.Instance;
            if (rm != null && (rm.GetOreTierCount(tier) < ore || !rm.ConsumeOre(tier, ore))) return false;
            craftingQueue.EnqueueSmelt(tier, count);
            return true;
        }

        public bool TryEnqueueItem(ItemType type, int level)
        {
            int woodCost = ItemStatCalculator.CalculateWoodCost(type, level);
            int oreCost = ItemStatCalculator.CalculateOreCost(type, level);
            int oreTier = ItemStatCalculator.CalculateRequiredOreTier(level);

            ResourceManager rm = ResourceManager.Instance;
            if (rm != null)
            {
                if (rm.WoodCount < woodCost || rm.GetOreCountAtLeast(oreTier) < oreCost)
                {
                    Debug.Log($"[Workbench] Недостатньо ресурсів для створення {type} Lvl {level}! Потрібно: {woodCost} Дерева, {oreCost} руди рів. {oreTier}+.");
                    return false;
                }

                rm.ConsumeResource(ResourceType.Wood, woodCost);
                rm.ConsumeOreAtLeast(oreTier, oreCost);
            }

            craftingQueue.EnqueueItem(type, level);
            return true;
        }

        /// <summary>Finished items crafted by this workbench (they go straight to the shared Armory).</summary>
        public event System.Action<Item> OnItemCrafted;

        private void HandleTaskCompleted(CraftingTask task)
        {
            if (task == null) return;
            if (task.IsSmelt)
            {
                ResourceManager.Instance?.AddOre(task.SmeltTier + 1, task.SmeltCount);
                StrategyHud.Notify($"Переплавлено: руда {CraftingTask.Tier(task.SmeltTier + 1)} ×{task.SmeltCount}");
                return;
            }
            var item = new Item(task.Type, task.Level);
            Armory.Instance.Add(item);
            OnItemCrafted?.Invoke(item);
            StrategyHud.Notify("Виготовлено: " + item);
        }

        public static GameObject CreateWorkbenchPrefab(string name = "Workbench")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<WorkbenchNode>();
            root.AddComponent<BuildingNode>();

            if (cachedLitShader == null)
            {
                cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }

            // Oak bench with a tool board, a leg vise, an anvil on a stump and a bucket (one shared mesh, see WorkbenchBuilder)
            WorkbenchBuilder.Attach(root.transform);

            // Root Collider (the bench and the anvil beside it)
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(-0.38f, 1.0f, 0f);
            col.size = new Vector3(3.95f, 2.0f, 1.5f);

            foreach (var childCol in root.GetComponentsInChildren<Collider>())
            {
                if (childCol != col) DestroyImmediate(childCol);
            }

            return root;
        }
    }
}
