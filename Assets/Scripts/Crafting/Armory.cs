using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Crafting
{
    /// <summary>
    /// The settlement's shared store of finished gear: the workbench delivers here, trophies land here, and the
    /// equipment window moves items between it and the worn slots of the player or any own unit.
    /// </summary>
    public sealed class Armory : MonoBehaviour
    {
        private static Armory instance;
        public static Armory Instance
        {
            get
            {
                if (instance == null) instance = FindFirstObjectByType<Armory>();
                if (instance == null) instance = new GameObject("Armory").AddComponent<Armory>();
                return instance;
            }
        }

        private readonly List<Item> items = new List<Item>();
        public IReadOnlyList<Item> Items => items;
        public static event Action Changed;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        public void Clear()
        {
            items.Clear();
            Changed?.Invoke();
        }

        public void Add(Item item)
        {
            if (item.IsEmpty) return;
            // Kept sorted: by slot, then type, best level first
            int at = items.Count;
            for (int i = 0; i < items.Count; i++)
                if (Compare(item, items[i]) < 0) { at = i; break; }
            items.Insert(at, item);
            Changed?.Invoke();
        }

        public bool Remove(Item item)
        {
            if (!items.Remove(item)) return false;
            Changed?.Invoke();
            return true;
        }

        public int CountOf(Item item)
        {
            int n = 0;
            foreach (Item i in items) if (i.Equals(item)) n++;
            return n;
        }

        /// <summary>Takes the item from the store and puts it on the unit; what was worn there goes back to the store.</summary>
        public bool EquipFromStore(Equipment target, Item item)
        {
            if (target == null || !Remove(item)) return false;
            Add(target.Equip(item));
            return true;
        }

        /// <summary>Takes the slot's item off the unit into the store.</summary>
        public bool UnequipToStore(Equipment target, EquipSlot slot)
        {
            if (target == null) return false;
            Item old = target.Unequip(slot);
            if (old.IsEmpty) return false;
            Add(old);
            return true;
        }

        private static int Compare(Item a, Item b)
        {
            if (a.Slot != b.Slot) return a.Slot.CompareTo(b.Slot);
            if (a.Type != b.Type) return a.Type.CompareTo(b.Type);
            return b.Level.CompareTo(a.Level);
        }
    }
}
