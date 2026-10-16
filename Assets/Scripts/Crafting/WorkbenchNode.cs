using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Resources;
using BattleOfAges.Utils;
using BattleOfAges.RTS;

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

                Debug.Log($"[Workbench] Робітник {worker.name} призначений на Верстак (Слот {slotIndex + 1}/4)!");
                return true;
            }
            return false;
        }

        public bool UnassignWorker(RtsUnit worker)
        {
            if (worker != null && assignedWorkers.Remove(worker))
            {
                craftingQueue.UnassignWorker();
                Debug.Log($"[Workbench] Робітник {worker.name} знятий з Верстака.");
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
                if (u == null || assignedWorkers.Contains(u) || u.GetComponent<BattleOfAges.Player.FirstPersonController>() != null) continue;
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
            Debug.Log($"[Workbench] Додано в чергу крафту: {type} Lvl {level}!");
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
            BattleOfAges.UI.StrategyHud.Notify("Виготовлено: " + item);
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

            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.45f, 0.28f, 0.12f), 0f, 0.25f);
            Material metalMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, new Color(0.25f, 0.28f, 0.30f), 0.7f, 0.45f);
            Material anvilMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.12f, 0.14f, 0.16f), 0.85f, 0.55f);

            // 1. Table Top
            GameObject tableTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tableTop.name = "TableTop";
            tableTop.transform.SetParent(root.transform, false);
            tableTop.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            tableTop.transform.localScale = new Vector3(2.8f, 0.25f, 1.8f);
            tableTop.GetComponent<MeshRenderer>().material = woodMat;

            // 2. Table Legs (4 corners)
            Vector3[] legPositions = new Vector3[]
            {
                new Vector3(-1.2f, 0.45f, -0.7f),
                new Vector3(1.2f, 0.45f, -0.7f),
                new Vector3(-1.2f, 0.45f, 0.7f),
                new Vector3(1.2f, 0.45f, 0.7f)
            };

            for (int i = 0; i < 4; i++)
            {
                GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                leg.name = $"TableLeg_{i}";
                leg.transform.SetParent(root.transform, false);
                leg.transform.localPosition = legPositions[i];
                leg.transform.localScale = new Vector3(0.25f, 0.9f, 0.25f);
                leg.GetComponent<MeshRenderer>().material = woodMat;
            }

            // 3. Anvil on table
            GameObject anvil = GameObject.CreatePrimitive(PrimitiveType.Cube);
            anvil.name = "Anvil";
            anvil.transform.SetParent(root.transform, false);
            anvil.transform.localPosition = new Vector3(-0.7f, 1.28f, 0.2f);
            anvil.transform.localScale = new Vector3(0.6f, 0.35f, 0.45f);
            anvil.GetComponent<MeshRenderer>().material = anvilMat;

            // 4. Vise on table edge
            GameObject vise = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vise.name = "Vise";
            vise.transform.SetParent(root.transform, false);
            vise.transform.localPosition = new Vector3(0.8f, 1.22f, -0.6f);
            vise.transform.localScale = new Vector3(0.35f, 0.25f, 0.35f);
            vise.GetComponent<MeshRenderer>().material = metalMat;

            // 5. Tool Rack / Shelf at back
            GameObject rack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rack.name = "ToolRack";
            rack.transform.SetParent(root.transform, false);
            rack.transform.localPosition = new Vector3(0f, 1.6f, -0.8f);
            rack.transform.localScale = new Vector3(2.6f, 1.0f, 0.15f);
            rack.GetComponent<MeshRenderer>().material = woodMat;

            // Root Collider
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.0f, 0f);
            col.size = new Vector3(3.2f, 2.2f, 2.2f);

            foreach (var childCol in root.GetComponentsInChildren<Collider>())
            {
                if (childCol != col) DestroyImmediate(childCol);
            }

            return root;
        }
    }
}
