using UnityEngine;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Marks a placement preview (the see-through building that follows the cursor): it is not a real building, so the
    /// save, the HUD labels and the registries skip it. Was told apart by its name ("BuildGhost_...").
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildGhost : MonoBehaviour
    {
        public static bool Is(Component c) => c != null && c.GetComponentInParent<BuildGhost>() != null;
    }
}
