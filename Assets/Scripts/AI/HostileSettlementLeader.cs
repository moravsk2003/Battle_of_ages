using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.AI
{
    public enum SquadCommand
    {
        DefendCamp,
        Rally,
        AssaultPlayerBase,
        TacticalRetreat
    }

    /// <summary>
    /// AI Leader of a hostile camp or raider settlement.
    /// Manages a coordinated squad of hostile units, issues orders (Defend, Assault, Retreat),
    /// and reacts dynamically to battle casualties.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HostileMobAI))]
    public class HostileSettlementLeader : MonoBehaviour
    {
        [Header("Squad Management")]
        [SerializeField] private List<HostileMobAI> squadMembers = new List<HostileMobAI>();
        [SerializeField] private SquadCommand currentSquadCommand = SquadCommand.DefendCamp;
        [SerializeField] private float assaultCheckInterval = 30.0f; // Every 30s considers launching a raid
        [SerializeField] private float casualtyRetreatThreshold = 0.5f; // Retreat if > 50% squad lost

        private HostileMobAI leaderMobAI;
        private Vector3 campCenterPosition;
        private int initialSquadSize;
        private float assaultTimer = 0f;

        public SquadCommand CurrentCommand => currentSquadCommand;
        public IReadOnlyList<HostileMobAI> SquadMembers => squadMembers;

        private void Awake()
        {
            leaderMobAI = GetComponent<HostileMobAI>();
            campCenterPosition = transform.position;
        }

        private void Start()
        {
            initialSquadSize = squadMembers.Count;
            if (leaderMobAI != null && leaderMobAI.Health != null)
            {
                leaderMobAI.Health.OnDeath += HandleLeaderDeath;
            }
        }

        public void InitializeSquad(List<HostileMobAI> squad)
        {
            squadMembers.Clear();
            if (squad != null)
            {
                squadMembers.AddRange(squad);
            }
            initialSquadSize = squadMembers.Count;
        }

        public void RegisterSquadMember(HostileMobAI member)
        {
            if (member != null && !squadMembers.Contains(member))
            {
                squadMembers.Add(member);
                initialSquadSize = squadMembers.Count;
            }
        }

        private void Update()
        {
            CleanDeadSquadMembers();

            // Tactical decision making
            CheckSquadStatus();

            assaultTimer += Time.deltaTime;
            if (assaultTimer >= assaultCheckInterval)
            {
                assaultTimer = 0f;
                ConsiderAssault();
            }
        }

        private void CleanDeadSquadMembers()
        {
            for (int i = squadMembers.Count - 1; i >= 0; i--)
            {
                if (squadMembers[i] == null || (squadMembers[i].Health != null && !squadMembers[i].Health.IsAlive))
                {
                    squadMembers.RemoveAt(i);
                }
            }
        }

        private void CheckSquadStatus()
        {
            if (initialSquadSize > 0)
            {
                float remainingRatio = (float)squadMembers.Count / initialSquadSize;
                if (remainingRatio <= casualtyRetreatThreshold && currentSquadCommand == SquadCommand.AssaultPlayerBase)
                {
                    OrderTacticalRetreat();
                }
            }
        }

        public void ConsiderAssault()
        {
            if (squadMembers.Count >= Mathf.Max(2, initialSquadSize / 2))
            {
                OrderAssaultOnPlayerBase();
            }
        }

        public void OrderAssaultOnPlayerBase()
        {
            currentSquadCommand = SquadCommand.AssaultPlayerBase;

            GameObject player = GameObject.Find("Player");
            Vector3 targetPos = (player != null) ? player.transform.position : Vector3.zero;

            Debug.Log($"[Hostile Leader] {gameObject.name} наказує загону ШТУРМУВАТИ базу гравця! Загін: {squadMembers.Count} бійців.");

            for (int i = 0; i < squadMembers.Count; i++)
            {
                if (squadMembers[i] == null) continue;

                Vector2 offset = Random.insideUnitCircle * 4.0f;
                Vector3 dest = targetPos + new Vector3(offset.x, 0, offset.y);
                squadMembers[i].SetMoveDestination(dest);
                squadMembers[i].ChangeState(AIStateType.Chase);
            }

            if (leaderMobAI != null)
            {
                leaderMobAI.SetMoveDestination(targetPos);
                leaderMobAI.ChangeState(AIStateType.Chase);
            }
        }

        public void OrderTacticalRetreat()
        {
            currentSquadCommand = SquadCommand.TacticalRetreat;
            Debug.Log($"[Hostile Leader] {gameObject.name} віддає наказ ВІДСТУПАТИ до табору через великі втрати!");

            for (int i = 0; i < squadMembers.Count; i++)
            {
                if (squadMembers[i] == null) continue;
                squadMembers[i].ChangeState(AIStateType.Retreat);
            }

            if (leaderMobAI != null)
            {
                leaderMobAI.ChangeState(AIStateType.Retreat);
            }
        }

        private void HandleLeaderDeath()
        {
            Debug.Log($"[Hostile Leader] Ватажок {gameObject.name} загинув! Загін розбитий та переходить у режим хаотичного захисту.");
            currentSquadCommand = SquadCommand.DefendCamp;
        }
    }
}
