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
        /// <summary>Units with a job outside the mines: the miners stay at the mine until its button sends them out (a player report: the job window pulled them out).</summary>
        public static int DistributableCount { get { int n = 0; foreach (NpcResourceGatherer w in activeWorkers) if (w != null && w.IsWorking && !w.IsMiner) n++; return n; } }
        public static int MinerCount { get { int n = 0; foreach (NpcResourceGatherer w in activeWorkers) if (w != null && w.IsMiner) n++; return n; } }

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
                if (worker != null && worker.IsWorking && !worker.IsMiner && worker.gameObject.activeInHierarchy)
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
                if (activeWorkers[i] != null && !activeWorkers[i].IsMiner && activeWorkers[i].gameObject.activeInHierarchy)
                {
                    if (activeWorkers[i].CurrentDirective == directive) count++;
                }
            }
            return count;
        }

        private static readonly List<NpcResourceGatherer> workingBuffer = new List<NpcResourceGatherer>(64);

        /// <summary>Splits the units that have a job between the jobs (soldiers on stand-by keep standing by, miners keep mining).</summary>
        /// <summary>
        /// Gives the jobs out in these numbers (the rest work on their own, Auto) without reshuffling: a worker already on
        /// a job keeps it while that job still needs hands; the missing hands come from the nearest free workers (to a mine,
        /// a tree, a pebble); a worker with something in hand is moved last. Only workers whose job changes are told.
        /// </summary>
        public static void ApplyJobDistribution(int branchesCount, int chopCount, int pebblesCount, int haulCount, int autoCount)
        {
            workingBuffer.Clear();
            foreach (NpcResourceGatherer w in activeWorkers) if (w != null && w.IsWorking && !w.IsMiner) workingBuffer.Add(w);

            var want = new (GatherDirective d, int n)[]
            {
                (GatherDirective.HaulOreFromMineToChest, haulCount), (GatherDirective.WoodChopTreesOnly, chopCount),
                (GatherDirective.WoodGatherBranchesOnly, branchesCount), (GatherDirective.OrePebblesOnly, pebblesCount),
            };
            var assigned = new Dictionary<NpcResourceGatherer, GatherDirective>();
            // each distance is a physics search: worked out once per worker and job
            var distances = new Dictionary<(NpcResourceGatherer, GatherDirective), float>();
            float Dist(NpcResourceGatherer w, GatherDirective d)
            {
                if (!distances.TryGetValue((w, d), out float v)) distances[(w, d)] = v = w.DistanceToWork(d);
                return v;
            }

            // 1. Keep those already on each job, nearest to the work first
            foreach (var (d, n) in want)
            {
                var on = new List<NpcResourceGatherer>();
                foreach (NpcResourceGatherer w in workingBuffer) if (w.CurrentDirective == d) on.Add(w);
                on.Sort((a, b) => Dist(a, d).CompareTo(Dist(b, d)));
                for (int i = 0; i < on.Count && i < n; i++) assigned[on[i]] = d;
            }

            // 2. Fill each job from the free workers nearest to it; those carrying something go last
            foreach (var (d, n) in want)
            {
                int have = 0;
                foreach (var kv in assigned) if (kv.Value == d) have++;
                if (have >= n) continue;
                var free = new List<NpcResourceGatherer>();
                foreach (NpcResourceGatherer w in workingBuffer) if (!assigned.ContainsKey(w)) free.Add(w);
                free.Sort((a, b) =>
                {
                    int c = a.IsCarrying.CompareTo(b.IsCarrying);
                    return c != 0 ? c : Dist(a, d).CompareTo(Dist(b, d));
                });
                for (int i = 0; i < free.Count && have < n; i++, have++) assigned[free[i]] = d;
            }

            // 3. Everyone else works on their own; tell only those whose job changes
            foreach (NpcResourceGatherer w in workingBuffer)
            {
                GatherDirective d = assigned.TryGetValue(w, out GatherDirective a) ? a : GatherDirective.Auto;
                if (w.CurrentDirective != d) w.SetGatherDirective(d);
            }
        }
    }
}
