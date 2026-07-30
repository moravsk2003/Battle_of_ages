using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.RTS
{
    /// <summary>
    /// High-performance static registry for tracking active NPC resource gatherer workers.
    /// Eliminates scene-wide FindObjectsByType searches and Garbage Collection allocations.
    /// </summary>
    public static class WorkerRegistry
    {
        private static readonly List<NpcResourceGatherer> activeWorkers = new List<NpcResourceGatherer>(64);

        public static IReadOnlyList<NpcResourceGatherer> ActiveWorkers => activeWorkers;
        public static int WorkerCount => activeWorkers.Count;

        public static void Register(NpcResourceGatherer worker)
        {
            if (worker != null && !activeWorkers.Contains(worker))
            {
                activeWorkers.Add(worker);
            }
        }

        public static void Unregister(NpcResourceGatherer worker)
        {
            if (worker != null)
            {
                activeWorkers.Remove(worker);
            }
        }

        public static void IssueDirectiveToAll(GatherDirective directive)
        {
            int count = activeWorkers.Count;
            for (int i = 0; i < count; i++)
            {
                NpcResourceGatherer worker = activeWorkers[i];
                if (worker != null && worker.gameObject.activeInHierarchy)
                {
                    worker.SetGatherDirective(directive);
                }
            }
        }

        public static int GetDirectiveWorkerCount(GatherDirective directive)
        {
            int count = 0;
            for (int i = 0; i < activeWorkers.Count; i++)
            {
                if (activeWorkers[i] != null && activeWorkers[i].gameObject.activeInHierarchy)
                {
                    if (activeWorkers[i].CurrentDirective == directive) count++;
                }
            }
            return count;
        }

        public static void ApplyJobDistribution(int branchesCount, int chopCount, int pebblesCount, int haulCount, int autoCount)
        {
            int index = 0;
            int total = activeWorkers.Count;

            // 1. Assign Branches
            for (int i = 0; i < branchesCount && index < total; i++, index++)
            {
                if (activeWorkers[index] != null) activeWorkers[index].SetGatherDirective(GatherDirective.WoodGatherBranchesOnly);
            }

            // 2. Assign Chop
            for (int i = 0; i < chopCount && index < total; i++, index++)
            {
                if (activeWorkers[index] != null) activeWorkers[index].SetGatherDirective(GatherDirective.WoodChopTreesOnly);
            }

            // 3. Assign Pebbles
            for (int i = 0; i < pebblesCount && index < total; i++, index++)
            {
                if (activeWorkers[index] != null) activeWorkers[index].SetGatherDirective(GatherDirective.OrePebblesOnly);
            }

            // 4. Assign Mine Haul
            for (int i = 0; i < haulCount && index < total; i++, index++)
            {
                if (activeWorkers[index] != null) activeWorkers[index].SetGatherDirective(GatherDirective.HaulOreFromMineToChest);
            }

            // 5. Remaining assigned to Auto
            for (; index < total; index++)
            {
                if (activeWorkers[index] != null) activeWorkers[index].SetGatherDirective(GatherDirective.Auto);
            }

            Debug.Log($"[WorkerRegistry] Job Distribution applied across {total} workers: Branches={branchesCount}, Chop={chopCount}, Pebbles={pebblesCount}, Haul={haulCount}, Auto={autoCount}");
        }
    }
}
