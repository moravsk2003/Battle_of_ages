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
        /// <summary>All own units (every unit can work, docs/GAME_DESIGN.md §11.1): the population the town hall caps.</summary>
        public static int WorkerCount => activeWorkers.Count;
        /// <summary>Units with a job (soldiers on stand-by are not counted): what the job window distributes.</summary>
        public static int WorkingCount { get { int n = 0; foreach (NpcResourceGatherer w in activeWorkers) if (w != null && w.IsWorking) n++; return n; } }

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
                if (worker != null && worker.IsWorking && worker.gameObject.activeInHierarchy)
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

        private static readonly List<NpcResourceGatherer> workingBuffer = new List<NpcResourceGatherer>(64);

        /// <summary>Splits the units that have a job between the jobs (soldiers on stand-by keep standing by).</summary>
        public static void ApplyJobDistribution(int branchesCount, int chopCount, int pebblesCount, int haulCount, int autoCount)
        {
            workingBuffer.Clear();
            foreach (NpcResourceGatherer w in activeWorkers) if (w != null && w.IsWorking) workingBuffer.Add(w);

            int index = 0;
            index = Assign(index, branchesCount, GatherDirective.WoodGatherBranchesOnly);
            index = Assign(index, chopCount, GatherDirective.WoodChopTreesOnly);
            index = Assign(index, pebblesCount, GatherDirective.OrePebblesOnly);
            index = Assign(index, haulCount, GatherDirective.HaulOreFromMineToChest);
            Assign(index, workingBuffer.Count - index, GatherDirective.Auto); // the rest
        }

        private static int Assign(int from, int count, GatherDirective directive)
        {
            int end = Mathf.Min(workingBuffer.Count, from + Mathf.Max(0, count));
            for (int i = from; i < end; i++) workingBuffer[i].SetGatherDirective(directive);
            return end;
        }
    }
}
