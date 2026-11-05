using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// A unit wears its rank's model (RankLooks): a person becomes a warrior in a gambeson, then a knight in plate, then a
    /// general with cape and crown. Checks on start (a loaded save, a unit built with another model) and on every
    /// promotion; swaps the model in place, with its worn gear, its pose under the unit and whether it was hidden.
    /// Heroes keep theirs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RankModel : MonoBehaviour
    {
        private UnitProgress progress;

        private void Start()
        {
            progress = GetComponent<UnitProgress>();
            if (progress != null) progress.Promoted += OnPromoted;
            Sync();
        }

        private void OnDestroy()
        {
            if (progress != null) progress.Promoted -= OnPromoted;
        }

        private void OnPromoted(UnitProgress p) => Sync();

        /// <summary>The model of the unit's rank, if it wears another one. True if it was swapped.</summary>
        public bool Sync()
        {
            if (progress == null) progress = GetComponent<UnitProgress>();
            if (progress == null || progress.IsHero) return false;
            CharacterGear current = GetComponentInChildren<CharacterGear>(true);
            if (current == null) return false; // no rigged model (a test unit, a far one)
            HumanoidModelBuilder.HumanType want = RankLooks.ModelFor(progress.Rank);
            if (current.Type == want) return false;

            Transform old = current.transform;
            GameObject fresh = HumanoidModelBuilder.CreateHumanoidModel(want, old.name);
            fresh.transform.SetParent(old.parent, false);
            fresh.transform.localPosition = old.localPosition;
            fresh.transform.localRotation = old.localRotation;
            fresh.transform.localScale = old.localScale;

            // Hidden (in a mine or a building) stays hidden
            Renderer shown = old.GetComponentInChildren<Renderer>(true);
            bool hidden = shown != null && !shown.enabled;
            old.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            if (hidden)
            {
                foreach (Renderer r in fresh.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                GpuSkinnedCharacter gpu = fresh.GetComponentInChildren<GpuSkinnedCharacter>(true);
                if (gpu != null) gpu.SetHidden(true);
            }

            Equipment gear = GetComponent<Equipment>();
            if (gear != null) gear.LoadSlots(gear.SlotsCopy()); // the worn gear on the new body
            return true;
        }
    }
}
