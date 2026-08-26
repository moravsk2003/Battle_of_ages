using UnityEngine;

namespace BattleOfAges.AI
{
    public enum AIStateType
    {
        Idle,
        Wander,
        Flee,
        Chase,
        Attack,
        Retreat,
        SquadFollowLeader,
        SquadAssault
    }

    /// <summary>
    /// Base interface for modular AI states within the Finite State Machine.
    /// </summary>
    public interface IAIState
    {
        AIStateType StateType { get; }
        void Enter(AIBaseController controller);
        void UpdateState(AIBaseController controller, float deltaTime);
        void Exit(AIBaseController controller);
    }
}
