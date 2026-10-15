using System.Collections.Generic;
using System.Text;
using UnityEngine;
using BattleOfAges.Crafting;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Shows a unit's worn Equipment on its rigged model: the gear is merged into the one skinned character mesh
    /// (GearMeshBuilder), so a fully equipped soldier still costs one renderer and one draw call. Lives on the model
    /// root made by RiggedCharacterBuilder; BaseMesh is the character without any slot gear. The knight's sculpted
    /// armour is part of its model, so knights keep their look (only their stats follow the gear).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterGear : MonoBehaviour
    {
        public HumanoidModelBuilder.HumanType Type;
        public SkinnedMeshRenderer Body;
        public Mesh BaseMesh;
        /// <summary>The warlord's horned helm belongs to his outfit: the helmet slot is not drawn.</summary>
        public bool FixedHelmet;
        /// <summary>Built on the knight skeleton (hero, warlord) rather than the villager one.</summary>
        public bool KnightRig;

        private string shown;
        private static readonly StringBuilder Key = new StringBuilder(64);
        private static readonly List<Item> Worn = new List<Item>(6);

        public bool DrawsGear => Type != HumanoidModelBuilder.HumanType.KnightGuard && Body != null && BaseMesh != null;

        public void Show(Equipment equipment)
        {
            if (!DrawsGear) return;
            Worn.Clear();
            Key.Clear();
            foreach (EquipSlot slot in ItemTypeNames.Slots)
            {
                Item item = equipment != null ? equipment[slot] : Item.None;
                if (item.IsEmpty || (FixedHelmet && slot == EquipSlot.Helmet)) continue;
                Worn.Add(item);
                Key.Append((int)item.Type).Append(':').Append(item.Level).Append(',');
            }
            string key = Key.ToString();
            if (key == shown && Body.sharedMesh != null) return;
            shown = key;
            Body.sharedMesh = Worn.Count == 0 ? BaseMesh : GearMeshBuilder.Compose(this, Worn, key);
        }
    }
}
