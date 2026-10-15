using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;

namespace BattleOfAges.Player
{
    /// <summary>
    /// Puts the player's worn weapon and shield into the first-person fists: separate meshes under the arm pivots
    /// (so they swing with the arms), rebuilt only when that gear changes and cached per item.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FpsViewmodelGear : MonoBehaviour
    {
        private Equipment gear;
        private MeshFilter weapon, shield;
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        public void Bind(Equipment equipment)
        {
            if (gear != null) gear.Changed -= Refresh;
            gear = equipment;
            if (gear != null) gear.Changed += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (gear != null) gear.Changed -= Refresh;
        }

        private MeshFilter Holder(string pivot, string name)
        {
            Transform p = transform.Find(pivot);
            if (p == null) return null;
            Transform t = p.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(p, false);
                t.gameObject.layer = p.gameObject.layer;
                t.gameObject.AddComponent<MeshFilter>();
                MeshRenderer mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ViewmodelArmsBuilder.SharedMaterial();
                Transform arm = p.Find(pivot == "RightArmPivot" ? "RightArm" : "LeftArm");
                MeshRenderer armRenderer = arm != null ? arm.GetComponent<MeshRenderer>() : null;
                if (armRenderer != null) { mr.sharedMaterial = armRenderer.sharedMaterial; mr.shadowCastingMode = armRenderer.shadowCastingMode; }
            }
            return t.GetComponent<MeshFilter>();
        }

        private void Refresh()
        {
            if (weapon == null) weapon = Holder("RightArmPivot", "HeldWeapon");
            if (shield == null) shield = Holder("LeftArmPivot", "HeldShield");
            Item w = gear != null ? gear[EquipSlot.Weapon] : Item.None;
            Item s = gear != null ? gear[EquipSlot.Shield] : Item.None;
            if (weapon != null)
            {
                weapon.sharedMesh = w.IsEmpty ? null : Get("W" + (int)w.Type + ":" + w.Level, () => ViewmodelArmsBuilder.BuildWeapon(w.Type, w.Level));
                weapon.GetComponent<MeshRenderer>().enabled = !w.IsEmpty;
            }
            if (shield != null)
            {
                shield.sharedMesh = s.IsEmpty ? null : Get("S" + s.Level, () => ViewmodelArmsBuilder.BuildShield(s.Level));
                shield.GetComponent<MeshRenderer>().enabled = !s.IsEmpty;
            }
        }

        private static Mesh Get(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out Mesh m) && m != null) return m;
            m = build();
            Cache[key] = m;
            return m;
        }
    }
}
