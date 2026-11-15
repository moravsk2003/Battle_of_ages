using UnityEngine;

namespace BattleOfAges.AI
{
    /// <summary>
    /// A building of an NPC kingdom's town that can be broken (KingdomTown): a house, the sawmill, a mine or a segment of the
    /// ring's wall. Which one of the town's plan it is, so its ruin is remembered and the same one rises again.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KingdomTownPart : MonoBehaviour
    {
        public KingdomTown.Kind Kind;
        public int Index;
    }
}
