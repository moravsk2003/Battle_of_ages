using System;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.AI;
using BattleOfAges.UI;
using BattleOfAges.Utils;

namespace BattleOfAges.Crafting
{
    /// <summary>
    /// Six worn slots (weapon, shield, helmet, chest, gloves, boots) on any fighter: the player, own units, enemies.
    /// Gear scales the unit's base stats (HealthComponent max health + damage reduction, CombatUnit damage / cooldown /
    /// range) and is shown on the character model (CharacterGear). Base stats are taken from the components the first
    /// time gear changes, or set explicitly with SetBaseStats. Enemies with DropsLoot may leave one worn piece in the
    /// armory when they die.
    /// </summary>
    [DisallowMultipleComponent]
    public class Equipment : MonoBehaviour
    {
        [SerializeField] private Item[] slots = new Item[6];

        public bool DropsLoot;
        [Range(0f, 1f)] public float LootChance = 0.35f;

        /// <summary>Raised after any change (any unit) — UI windows listen to this.</summary>
        public static event Action<Equipment> AnyChanged;
        public event Action Changed;

        private bool baseSet;
        private float baseHealth = 100f, baseDamage = 10f; // float: a kit divided out and back in gives the tuned value exactly
        private float baseCooldown = 2f, baseRange = 3f;
        private HealthComponent health;
        private CombatUnit combat;
        private bool lootHooked;

        public Item this[EquipSlot slot] => Slots[(int)slot];
        public int BaseHealth { get { EnsureBase(); return Mathf.RoundToInt(baseHealth); } }
        /// <summary>Strength: the unit's own damage before the weapon (also sets its speed, see CharacterSpeed).</summary>
        public float Strength { get { EnsureBase(); return baseDamage; } }
        public int BaseDamage { get { EnsureBase(); return Mathf.RoundToInt(baseDamage); } }

        private Item[] Slots
        {
            get
            {
                if (slots == null || slots.Length != 6) Array.Resize(ref slots, 6);
                return slots;
            }
        }

        // ------------------------------------------------------------------ resulting stats

        public float DamageReduction
        {
            get
            {
                float r = 0f;
                foreach (Item i in Slots) r += ItemStatCalculator.DamageReduction(i);
                return Mathf.Min(r, ItemStatCalculator.MAX_DAMAGE_REDUCTION);
            }
        }

        public int MaxHealth { get { EnsureBase(); return Mathf.RoundToInt(baseHealth * HealthFactor()); } }
        public int Damage { get { EnsureBase(); return Mathf.Max(1, Mathf.RoundToInt(baseDamage * DamageFactor())); } }
        public float Cooldown { get { EnsureBase(); return baseCooldown * CooldownFactor(); } }
        public float Range
        {
            get
            {
                EnsureBase();
                return this[EquipSlot.Weapon].Type == ItemType.Bow && !this[EquipSlot.Weapon].IsEmpty
                    ? Mathf.Max(baseRange, ItemStatCalculator.BOW_RANGE) : baseRange;
            }
        }

        private float HealthFactor() => 1f + ItemStatCalculator.HealthBonus(this[EquipSlot.Chest]);
        private float DamageFactor() => ItemStatCalculator.WeaponDamageMultiplier(this[EquipSlot.Weapon]);
        private float CooldownFactor() => ItemStatCalculator.CooldownMultiplier(this[EquipSlot.Weapon]) * ItemStatCalculator.CooldownMultiplier(this[EquipSlot.Gloves]);

        /// <summary>Level of the item of this type when it is worn, else 0.</summary>
        public int LevelOf(ItemType type)
        {
            Item i = this[ItemTypeNames.SlotOf(type)];
            return !i.IsEmpty && i.Type == type ? i.Level : 0;
        }

        // ------------------------------------------------------------------ changing gear

        /// <summary>Stats without any gear; reapplies the worn gear on top.</summary>
        public void SetBaseStats(int health, int damage, float cooldown, float range)
        {
            baseSet = true;
            baseHealth = Mathf.Max(1, health);
            baseDamage = Mathf.Max(1, damage);
            baseCooldown = cooldown;
            baseRange = range;
            Apply();
        }

        /// <summary>
        /// Starting kit for a unit whose current stats were tuned with this kit in mind: the stats stay as they are
        /// (the base is the current value with the kit divided out; the kit's armour still absorbs its share of each hit);
        /// later changes are relative to that base.
        /// </summary>
        public void InitLoadout(params Item[] items)
        {
            Resolve();
            for (int s = 0; s < 6; s++) Slots[s] = Item.None;
            foreach (Item i in items) if (!i.IsEmpty) Slots[(int)i.Slot] = i;
            baseSet = true;
            baseHealth = Mathf.Max(1f, (health != null ? health.MaxHealth : baseHealth) / HealthFactor());
            baseDamage = Mathf.Max(1f, (combat != null ? combat.AttackDamage : baseDamage) / DamageFactor());
            baseCooldown = (combat != null ? combat.AttackCooldown : baseCooldown) / CooldownFactor();
            baseRange = combat != null ? combat.AttackRange : baseRange;
            if (this[EquipSlot.Weapon].Type == ItemType.Bow && !this[EquipSlot.Weapon].IsEmpty) baseRange = Mathf.Min(baseRange, 3f);
            Apply();
        }

        /// <summary>All six slots (weapon, shield, helmet, chest, gloves, boots), for saving.</summary>
        public Item[] SlotsCopy() => (Item[])Slots.Clone();

        /// <summary>Puts on exactly these slots (a loaded save); stats follow from the base as usual.</summary>
        public void LoadSlots(Item[] items)
        {
            EnsureBase();
            for (int s = 0; s < 6; s++) Slots[s] = items != null && s < items.Length ? items[s] : Item.None;
            Apply();
        }

        /// <summary>Wears the item; returns what was in that slot before (Item.None if it was empty).</summary>
        public Item Equip(Item item)
        {
            if (item.IsEmpty) return Item.None;
            EnsureBase();
            int s = (int)item.Slot;
            Item old = Slots[s];
            Slots[s] = item;
            Apply();
            return old;
        }

        /// <summary>Takes off what is in the slot and returns it.</summary>
        public Item Unequip(EquipSlot slot)
        {
            EnsureBase();
            Item old = Slots[(int)slot];
            if (old.IsEmpty) return Item.None;
            Slots[(int)slot] = Item.None;
            Apply();
            return old;
        }

        private void Resolve()
        {
            if (health == null) health = GetComponent<HealthComponent>();
            if (combat == null) combat = GetComponent<CombatUnit>();
        }

        private void EnsureBase()
        {
            if (baseSet) return;
            Resolve();
            baseSet = true;
            if (health != null) baseHealth = health.MaxHealth;
            if (combat != null) { baseDamage = combat.AttackDamage; baseCooldown = combat.AttackCooldown; baseRange = combat.AttackRange; }
        }

        private void Apply()
        {
            Resolve();
            if (health != null)
            {
                health.SetMaxHealthKeepDamage(MaxHealth);
                health.DamageReduction = DamageReduction;
            }
            if (combat != null)
            {
                combat.Strength = baseDamage; // the characteristic; damage = strength x weapon
                combat.InitializeCombatStats(Damage, Cooldown, Range);
            }
            CharacterGear gear = GetComponentInChildren<CharacterGear>(true);
            if (gear != null) gear.Show(this);
            Changed?.Invoke();
            AnyChanged?.Invoke(this);
        }

        /// <summary>Re-shows the gear on the model (after the model was swapped).</summary>
        public void RefreshVisuals()
        {
            CharacterGear gear = GetComponentInChildren<CharacterGear>(true);
            if (gear != null) gear.Show(this);
        }

        // ------------------------------------------------------------------ loot

        private void Start()
        {
            RefreshVisuals();
            if (!DropsLoot || lootHooked) return;
            Resolve();
            if (health == null) return;
            health.OnDeath += DropLoot;
            lootHooked = true;
        }

        private void OnDestroy()
        {
            if (lootHooked && health != null) health.OnDeath -= DropLoot;
        }

        private void DropLoot()
        {
            if (UnityEngine.Random.value > LootChance) return;
            int worn = 0;
            foreach (Item i in Slots) if (!i.IsEmpty) worn++;
            if (worn == 0) return;
            int pick = UnityEngine.Random.Range(0, worn);
            foreach (Item i in Slots)
            {
                if (i.IsEmpty) continue;
                if (pick-- > 0) continue;
                Armory.Instance.Add(i);
                StrategyHud.Notify("Трофей: " + i);
                return;
            }
        }
    }
}

namespace BattleOfAges.Crafting
{
    /// <summary>What each kind of unit carries when it appears (its tuned stats already include this kit).</summary>
    public static class StartingKits
    {
        private static Item I(ItemType t, int l) => new Item(t, l);

        public static Item[] For(HumanoidModelBuilder.HumanType type)
        {
            switch (type)
            {
                case HumanoidModelBuilder.HumanType.KnightGuard:
                    return new[] { I(ItemType.Sword, 3), I(ItemType.Shield, 3), I(ItemType.Helmet, 3), I(ItemType.Armor, 3), I(ItemType.Gloves, 2), I(ItemType.Boots, 2) };
                case HumanoidModelBuilder.HumanType.HeroPlayer:
                    return new[] { I(ItemType.Sword, 0), I(ItemType.Shield, 1) };
                case HumanoidModelBuilder.HumanType.Bandit:
                    return new[] { I(ItemType.Sword, 1), I(ItemType.Armor, 1) };
                case HumanoidModelBuilder.HumanType.Goblin:
                    return new[] { I(ItemType.Axe, 1) };
                case HumanoidModelBuilder.HumanType.Warlord:
                    return new[] { I(ItemType.Sword, 3), I(ItemType.Shield, 2), I(ItemType.Helmet, 2), I(ItemType.Armor, 2), I(ItemType.Boots, 1) };
                default: // Villager
                    return new[] { I(ItemType.Axe, 1) };
            }
        }

        public static Item[] For(HostileMobType type) => For(
            type == HostileMobType.WarlordGuard ? HumanoidModelBuilder.HumanType.Warlord
            : type == HostileMobType.Goblin ? HumanoidModelBuilder.HumanType.Goblin
            : HumanoidModelBuilder.HumanType.Bandit);
    }
}
