using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Crafting
{
    [System.Serializable]
    public class CraftingTask
    {
        public string TaskId;
        public ItemType Type;
        public int Level;
        public float TargetDurationSeconds;
        public float ProgressSeconds;
        /// <summary>A smelting task: ore of this tier (-1: an item) melted into SmeltCount pieces of the next tier, GameBalance.SmeltRatio each.</summary>
        public int SmeltTier = -1;
        public int SmeltCount;
        public bool IsSmelt => SmeltTier >= 0;

        public float NormalizedProgress => (TargetDurationSeconds > 0f) ? Mathf.Clamp01(ProgressSeconds / TargetDurationSeconds) : 1.0f;
        public bool IsCompleted => ProgressSeconds >= TargetDurationSeconds;

        public CraftingTask(ItemType type, int level)
        {
            TaskId = Guid.NewGuid().ToString("N");
            Type = type;
            Level = ItemStatCalculator.ClampLevel(level);
            TargetDurationSeconds = ItemStatCalculator.CalculateCraftTime(type, level);
            ProgressSeconds = 0f;
        }

        /// <summary>Melts count x SmeltRatio ore of the tier into count pieces of the next tier, SmeltSecondsPerPiece each with one worker.</summary>
        public static CraftingTask Smelt(int tier, int count)
        {
            var task = new CraftingTask(ItemType.Sword, 1) { SmeltTier = Mathf.Max(0, tier), SmeltCount = Mathf.Max(1, count) };
            task.TargetDurationSeconds = task.SmeltCount * Combat.GameBalance.SmeltSecondsPerPiece;
            return task;
        }

        /// <summary>An ore tier as the HUD writes it (0, I .. V, then digits).</summary>
        public static string Tier(int tier) { string r = ItemTypeNames.Roman(tier); return r.Length > 0 ? r : tier.ToString(); }

        public override string ToString() =>
            IsSmelt ? $"Переплавка: руда {Tier(SmeltTier)} ×{SmeltCount * Combat.GameBalance.SmeltRatio} → {Tier(SmeltTier + 1)} ×{SmeltCount}" : new Item(Type, Level).ToString();
    }

    /// <summary>
    /// Core business logic engine for the Workbench crafting queue and multi-NPC work distribution.
    /// Supports up to 4 assigned workers with accelerating speed multipliers.
    /// </summary>
    public class CraftingQueue
    {
        public const int MAX_WORKERS = 4;

        private readonly List<CraftingTask> queue = new List<CraftingTask>();
        private int assignedWorkerCount = 0;

        public int AssignedWorkerCount => assignedWorkerCount;
        public IReadOnlyList<CraftingTask> Queue => queue;
        public CraftingTask ActiveTask => (queue.Count > 0) ? queue[0] : null;
        public bool HasWork => queue.Count > 0;
        public bool IsWorkerFull => assignedWorkerCount >= MAX_WORKERS;

        public event Action<CraftingTask> OnTaskAdded;
        public event Action<CraftingTask> OnTaskCompleted;
        public event Action<CraftingTask> OnTaskCancelled;
        public event Action<int> OnWorkerCountChanged;

        public bool AssignWorker()
        {
            if (assignedWorkerCount >= MAX_WORKERS) return false;
            assignedWorkerCount++;
            OnWorkerCountChanged?.Invoke(assignedWorkerCount);
            return true;
        }

        public bool UnassignWorker()
        {
            if (assignedWorkerCount <= 0) return false;
            assignedWorkerCount--;
            OnWorkerCountChanged?.Invoke(assignedWorkerCount);
            return true;
        }

        public void SetWorkerCount(int count)
        {
            assignedWorkerCount = Mathf.Clamp(count, 0, MAX_WORKERS);
            OnWorkerCountChanged?.Invoke(assignedWorkerCount);
        }

        public static float CalculateSpeedMultiplier(int workerCount)
        {
            switch (workerCount)
            {
                case 0: return 0.0f;
                case 1: return 1.0f;
                case 2: return 1.75f;
                case 3: return 2.4f;
                case 4: return 3.0f;
                default:
                    return (workerCount > 4) ? 3.0f : 0.0f;
            }
        }

        public CraftingTask EnqueueItem(ItemType type, int level) => Enqueue(new CraftingTask(type, level));

        public CraftingTask EnqueueSmelt(int tier, int count) => Enqueue(CraftingTask.Smelt(tier, count));

        private CraftingTask Enqueue(CraftingTask task)
        {
            queue.Add(task);
            OnTaskAdded?.Invoke(task);
            return task;
        }

        public bool CancelTask(string taskId)
        {
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].TaskId == taskId)
                {
                    CraftingTask removed = queue[i];
                    queue.RemoveAt(i);
                    OnTaskCancelled?.Invoke(removed);
                    return true;
                }
            }
            return false;
        }

        public void ClearQueue()
        {
            while (queue.Count > 0)
            {
                CraftingTask task = queue[0];
                queue.RemoveAt(0);
                OnTaskCancelled?.Invoke(task);
            }
        }

        public bool Tick(float deltaTime)
        {
            if (queue.Count == 0 || assignedWorkerCount <= 0) return false;

            float speedMultiplier = CalculateSpeedMultiplier(assignedWorkerCount);
            CraftingTask current = queue[0];
            current.ProgressSeconds += deltaTime * speedMultiplier;

            if (current.IsCompleted)
            {
                queue.RemoveAt(0);
                OnTaskCompleted?.Invoke(current);
                return true;
            }

            return false;
        }
    }
}
