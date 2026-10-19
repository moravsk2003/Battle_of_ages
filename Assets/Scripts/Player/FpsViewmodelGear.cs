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
        private MeshFilter weapon, shield, bow, nocked;
        private LineRenderer bowString;
        private int bowLevel = -1;

        /// <summary>The worn weapon is a bow: it is in the left fist (no shield), the right hand draws.</summary>
        public bool HoldsBow { get; private set; }
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
            if (bow == null) bow = Holder("LeftArmPivot", "HeldBow");
            Item w = gear != null ? gear[EquipSlot.Weapon] : Item.None;
            Item s = gear != null ? gear[EquipSlot.Shield] : Item.None;
            HoldsBow = !w.IsEmpty && w.Type == ItemType.Bow;
            if (weapon != null)
            {
                weapon.sharedMesh = w.IsEmpty || HoldsBow ? null : Get("W" + (int)w.Type + ":" + w.Level, () => ViewmodelArmsBuilder.BuildWeapon(w.Type, w.Level));
                weapon.GetComponent<MeshRenderer>().enabled = !w.IsEmpty && !HoldsBow;
            }
            if (shield != null)
            {
                shield.sharedMesh = s.IsEmpty ? null : Get("S" + s.Level, () => ViewmodelArmsBuilder.BuildShield(s.Level));
                shield.GetComponent<MeshRenderer>().enabled = !s.IsEmpty && !HoldsBow; // the bow needs the left hand
            }
            if (bow != null)
            {
                bowLevel = HoldsBow ? w.Level : -1;
                bow.sharedMesh = HoldsBow ? Get("B" + w.Level, () => ViewmodelArmsBuilder.BuildBowLeft(w.Level)) : null;
                bow.GetComponent<MeshRenderer>().enabled = HoldsBow;
                if (HoldsBow) EnsureStringAndArrow();
                if (bowString != null) bowString.enabled = HoldsBow;
                if (nocked != null) nocked.GetComponent<MeshRenderer>().enabled = HoldsBow;
                SetDraw(0f, true);
            }
        }

        private void EnsureStringAndArrow()
        {
            if (bowString == null)
            {
                bowString = bow.gameObject.AddComponent<LineRenderer>();
                bowString.useWorldSpace = false;
                bowString.positionCount = 3;
                bowString.widthMultiplier = 0.004f;
                bowString.numCapVertices = 0;
                bowString.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bowString.sharedMaterial = RuntimeShaders.UnlitMaterial(new Color(0.78f, 0.70f, 0.55f));
            }
            if (nocked == null)
            {
                var t = new GameObject("NockedArrow").transform;
                t.SetParent(bow.transform, false);
                t.gameObject.layer = bow.gameObject.layer;
                nocked = t.gameObject.AddComponent<MeshFilter>();
                nocked.sharedMesh = Combat.ArrowShot.Mesh;
                MeshRenderer mr = t.gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterial = bow.GetComponent<MeshRenderer>().sharedMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// <summary>The string drawn this far (0..1) to the right hand, with an arrow on it or not.</summary>
        public void SetDraw(float draw, bool arrow)
        {
            if (!HoldsBow || bowLevel < 0) return;
            ViewmodelArmsBuilder.BowString(bowLevel, draw, out Vector3 top, out Vector3 nock, out Vector3 bottom);
            if (bowString != null) { bowString.SetPosition(0, top); bowString.SetPosition(1, nock); bowString.SetPosition(2, bottom); }
            if (nocked != null)
            {
                nocked.GetComponent<MeshRenderer>().enabled = arrow;
                nocked.transform.localPosition = nock + ViewmodelArmsBuilder.BowRot * new Vector3(0.012f, 0f, Combat.ArrowShot.Length * 0.5f); // on the left of the grip
                nocked.transform.localRotation = ViewmodelArmsBuilder.BowRot;
            }
        }

        /// <summary>Where the right hand holds the string at this draw, in the viewmodel root's space.</summary>
        public Vector3 NockInRoot(float draw)
        {
            ViewmodelArmsBuilder.BowString(Mathf.Max(0, bowLevel), draw, out _, out Vector3 nock, out _);
            Transform left = bow != null ? bow.transform.parent : null;
            return left != null ? left.localPosition + left.localRotation * nock : nock;
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
