using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// A unit wears its rank's model (RankLooks): a person becomes a warrior in a gambeson, then a knight in plate, then a
    /// general with cape and crown. Checks on start (a loaded save, a unit built with another model) and on every
    /// promotion; swaps the model in place, with its worn gear, its pose under the unit and whether it was hidden.
    /// Heroes keep theirs. A knight or general is sculpted with his arms: taking a bow (or putting it down) makes him again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RankModel : MonoBehaviour
    {
        private UnitProgress progress;
        private Equipment equipment;
        private bool syncing;

        private void Start()
        {
            progress = GetComponent<UnitProgress>();
            equipment = GetComponent<Equipment>();
            if (progress != null) progress.Promoted += OnPromoted;
            if (equipment != null) equipment.Changed += OnGearChanged;
            Sync();
        }

        private void OnDestroy()
        {
            if (progress != null) progress.Promoted -= OnPromoted;
            if (equipment != null) equipment.Changed -= OnGearChanged;
        }

        private void OnGearChanged() { if (!syncing) Sync(); }

        private void OnPromoted(UnitProgress p) => Sync();

        /// <summary>The model of the unit's rank, if it wears another one. True if it was swapped.</summary>
        public bool Sync()
        {
            if (progress == null) progress = GetComponent<UnitProgress>();
            if (progress == null) return false;
            CharacterGear current = GetComponentInChildren<CharacterGear>(true);
            if (current == null) return false; // no rigged model (a test unit, a far one)
            HumanoidModelBuilder.HumanType want = progress.IsHero ? current.Type : RankLooks.ModelFor(progress.Rank);
            Equipment gear = GetComponent<Equipment>();
            bool bow = gear != null && gear.HasBow;
            bool sculpted = want == HumanoidModelBuilder.HumanType.KnightGuard || want == HumanoidModelBuilder.HumanType.General;
            if (current.Type == want && (!sculpted || current.SculptedBow == bow)) return false;

            Transform old = current.transform;
            GameObject fresh = HumanoidModelBuilder.CreateHumanoidModel(want, old.name, sculpted && bow);
            fresh.transform.SetParent(old.parent, false);
            fresh.transform.localPosition = old.localPosition;
            fresh.transform.localRotation = old.localRotation;
            fresh.transform.localScale = old.localScale;

            // Hidden (in a mine or a building) stays hidden. By its owner's word, not by a renderer that is off: the body is off
            // too while it is drawn on the GPU far away or is a speck, and a unit promoted then stayed unseen for good (a tester
            // was hit by one, seen only on the minimap)
            GpuSkinnedCharacter was = old.GetComponentInChildren<GpuSkinnedCharacter>(true);
            Renderer shown = old.GetComponentInChildren<Renderer>(true);
            bool hidden = was != null ? was.Hidden : shown != null && !shown.enabled;
            old.gameObject.SetActive(false);
            old.SetParent(null, false); // out of the unit now: the gear below must find the new body, not this one before the frame ends
            if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            if (hidden)
            {
                foreach (Renderer r in fresh.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                GpuSkinnedCharacter gpu = fresh.GetComponentInChildren<GpuSkinnedCharacter>(true);
                if (gpu != null) gpu.SetHidden(true);
            }

            syncing = true;
            if (gear != null) gear.LoadSlots(gear.SlotsCopy()); // the worn gear on the new body
            syncing = false;
            return true;
        }
    }
}
