using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;

namespace BattleOfAges.Crafting
{
    /// <summary>
    /// Handing gear out of the armory without clicking unit by unit: a miner takes the best pickaxe itself (on going
    /// into a mine, and when one is made while it digs), an item goes to each of the chosen units at once, and "the
    /// best from the store" upgrades their armour and their weapon of the kind they hold. A combat weapon is never
    /// swapped for a tool, and a tool never for a weapon of another kind.
    /// </summary>
    public static class GearDispenser
    {
        private static bool busy; // equipping changes the armory, which would call us again

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook()
        {
            Armory.Changed -= OnArmoryChanged;
            Armory.Changed += OnArmoryChanged;
            busy = false;
        }

        public static bool IsTool(ItemType t) => t == ItemType.Axe || t == ItemType.Pickaxe;

        /// <summary>The best item of the type in the store (None if there is none).</summary>
        public static Item BestInStore(ItemType type)
        {
            Item best = Item.None;
            foreach (Item i in Armory.Instance.Items)
                if (i.Type == type && (best.IsEmpty || i.Level > best.Level)) best = i;
            return best;
        }

        /// <summary>
        /// Gives the worker the best tool of the type from the store, if its hands are empty or hold a tool and the
        /// store's is better (any of the type beats the other tool). A sword or bow stays in its hands.
        /// </summary>
        public static bool GiveTool(Equipment gear, ItemType tool)
        {
            if (gear == null || gear.IsLocked(EquipSlot.Weapon)) return false;
            Item held = gear[EquipSlot.Weapon];
            if (!held.IsEmpty && !IsTool(held.Type)) return false;
            Item best = BestInStore(tool);
            if (best.IsEmpty) return false;
            if (!held.IsEmpty && held.Type == tool && held.Level >= best.Level) return false;
            return Equip(gear, best);
        }

        /// <summary>Puts one of the item on each of the units that do not wear it yet, while the store has it; how many got one.</summary>
        public static int GiveEach(IList<Equipment> units, Item item)
        {
            int given = 0;
            foreach (Equipment e in units)
            {
                if (e == null || e.IsLocked(item.Slot) || e[item.Slot].Equals(item)) continue;
                if (Armory.Instance.CountOf(item) == 0) break;
                if (Equip(e, item)) given++;
            }
            return given;
        }

        /// <summary>
        /// The best from the store: every armour slot gets the best piece if it beats the worn one, the weapon the best
        /// of the kind already held (an empty hand or a tool stays as it is); how many items were put on.
        /// </summary>
        public static int Upgrade(Equipment gear)
        {
            if (gear == null) return 0;
            int changed = 0;
            foreach (EquipSlot slot in ItemTypeNames.Slots)
            {
                if (gear.IsLocked(slot)) continue;
                Item worn = gear[slot];
                ItemType type;
                if (slot == EquipSlot.Weapon)
                {
                    if (worn.IsEmpty || IsTool(worn.Type)) continue;
                    type = worn.Type;
                }
                else type = TypeOf(slot);
                Item best = BestInStore(type);
                if (!best.IsEmpty && (worn.IsEmpty || best.Level > worn.Level) && Equip(gear, best)) changed++;
            }
            return changed;
        }

        public static int UpgradeAll(IList<Equipment> units)
        {
            int n = 0;
            foreach (Equipment e in units) n += Upgrade(e);
            return n;
        }

        private static ItemType TypeOf(EquipSlot slot) =>
            slot == EquipSlot.Shield ? ItemType.Shield : slot == EquipSlot.Helmet ? ItemType.Helmet
            : slot == EquipSlot.Gloves ? ItemType.Gloves : slot == EquipSlot.Boots ? ItemType.Boots : ItemType.Armor;

        private static bool Equip(Equipment gear, Item item)
        {
            bool was = busy;
            busy = true;
            try { return Armory.Instance.EquipFromStore(gear, item); }
            finally { busy = was; }
        }

        /// <summary>A pickaxe came into the store: the miners digging without a (good enough) one take it.</summary>
        private static void OnArmoryChanged()
        {
            if (busy || !Application.isPlaying) return;
            foreach (MineNode mine in MineNode.ActiveMines)
            {
                if (mine == null) continue;
                foreach (RTS.NpcResourceGatherer w in mine.Workers)
                    if (w != null) GiveTool(w.GetComponent<Equipment>(), ItemType.Pickaxe);
            }
        }
    }
}
