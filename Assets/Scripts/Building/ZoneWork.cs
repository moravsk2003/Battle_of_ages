using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.World;

namespace BattleOfAges.Building
{
    /// <summary>
    /// What two zones do on their own, once a game hour (the zone comparison in docs/GAME_DESIGN.md §18.3 found they did
    /// nothing): in a Forest felled trees grow back, in Crafts a master at a bench keeps the armoury in plain goods, while
    /// the stock has more than the settlement needs for building.
    /// </summary>
    public static class ZoneWork
    {
        private static readonly Dictionary<ResourceNodeTree, float> regrowing = new Dictionary<ResourceNodeTree, float>();
        private static readonly HashSet<WorkbenchNode> manned = new HashSet<WorkbenchNode>();
        private static readonly List<ResourceNodeTree> buffer = new List<ResourceNodeTree>();

        /// <summary>What a master keeps in the armoury, in this order, this many of each.</summary>
        private static readonly ItemType[] Goods = { ItemType.Axe, ItemType.Sword, ItemType.Shield, ItemType.Armor, ItemType.Helmet, ItemType.Bow };
        public const int GoodsKept = 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { regrowing.Clear(); manned.Clear(); }

        public static void Hour()
        {
            GrowForests();
            if (!CentralEmpire.Active) RunBenches();
        }

        private static void GrowForests()
        {
            buffer.Clear();
            buffer.AddRange(ResourceNodeTree.Felled);
            foreach (ResourceNodeTree tree in buffer)
            {
                if (tree == null || ZoneMap.At(tree.transform.position) != ZoneType.Forest) { regrowing.Remove(tree); continue; }
                regrowing.TryGetValue(tree, out float hours);
                hours += 1f;
                if (hours < GameBalance.ForestRegrowHours) { regrowing[tree] = hours; continue; }
                regrowing.Remove(tree);
                tree.ResetTreeState();
                SaveLoad.WorldPersistence.NoteTree(tree);
            }
        }

        private static void RunBenches()
        {
            manned.RemoveWhere(b => b == null);
            TownHall hall = TownHall.Current;
            int level = hall != null ? Mathf.Max(1, hall.Level - 1) : 1;
            foreach (WorkbenchNode bench in WorkbenchNode.ActiveWorkbenches)
            {
                if (bench == null || BuildGhost.Is(bench) || ZoneMap.At(bench.transform.position) != ZoneType.Crafts) continue;
                bool work = bench.Queue.HasWork;
                if (!work && TryNext(level, out ItemType type)) work = bench.TryEnqueueItem(type, level);
                if (work)
                {
                    if (bench.CalledWorkerCount == 0 && bench.AssignNearestFreeWorker(onlyWorking: true) != null) manned.Add(bench);
                }
                else if (manned.Remove(bench)) bench.UnassignLastWorker(); // nothing to do: back to its other work
            }
        }

        /// <summary>The next good short in the armoury, if the stock can spare it (a settlement first builds).</summary>
        private static bool TryNext(int level, out ItemType type)
        {
            type = default;
            ResourceManager rm = ResourceManager.Instance;
            foreach (ItemType t in Goods)
            {
                if (Armory.Instance.CountOf(new Item(t, level)) >= GoodsKept) continue;
                int wood = ItemStatCalculator.CalculateWoodCost(t, level), ore = ItemStatCalculator.CalculateOreCost(t, level);
                int tier = ItemStatCalculator.CalculateRequiredOreTier(level);
                if (rm != null && (rm.WoodCount - wood < GameBalance.CraftsKeepWood || rm.GetOreCountAtLeast(tier) - ore < GameBalance.CraftsKeepOre)) continue;
                type = t;
                return true;
            }
            return false;
        }
    }
}
