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
    /// Workbench building component where up to 4 NPC workers craft weapons, armor and tools.
    /// Features 4 worker station slots around the table and procedural 3D visuals.
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
        public int AssignedWorkerCount => Queue.AssignedWorkerCount;
        public bool IsWorkerFull => Queue.IsWorkerFull;

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
            craftingQueue.Tick(Time.deltaTime);
        }

        public bool AssignWorker(RtsUnit worker)
        {
            if (craftingQueue.IsWorkerFull) return false;
            if (worker != null && !assignedWorkers.Contains(worker))
            {
                assignedWorkers.Add(worker);
                craftingQueue.AssignWorker();

                int slotIndex = assignedWorkers.Count - 1;
                Vector3 slotPos = GetWorkerSlotPosition(slotIndex);
                worker.IssueMoveCommand(slotPos);

                return true;
            }
            return false;
        }

        public bool UnassignWorker(RtsUnit worker)
        {
            if (worker != null && assignedWorkers.Remove(worker))
            {
                craftingQueue.UnassignWorker();
                return true;
            }
            return false;
        }

        public IReadOnlyList<RtsUnit> Workers => assignedWorkers;

        /// <summary>Restores a saved queue (resources were paid when it was queued).</summary>
        public void LoadQueue(IEnumerable<(ItemType type, int level, float progress)> tasks)
        {
            Queue.ClearQueue();
            foreach (var t in tasks) Queue.EnqueueItem(t.type, t.level).ProgressSeconds = t.progress;
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
                float score = Vector3.Distance(transform.position, u.transform.position) + (u.GetComponent<NpcResourceGatherer>() != null ? 0f : 1000f);
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
            if (rm != null)
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

        public Vector3 GetWorkerSlotPosition(int slotIndex)
        {
            Vector3 offset = Vector3.forward * 1.8f;
            switch (slotIndex % 4)
            {
                case 0: offset = transform.forward * 1.8f; break;
                case 1: offset = -transform.forward * 1.8f; break;
                case 2: offset = transform.right * 1.8f; break;
                case 3: offset = -transform.right * 1.8f; break;
            }

            Vector3 pos = transform.position + offset;
            pos.y = RtsUnit.GetTerrainHeight(pos);
            return pos;
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
