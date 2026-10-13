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
        [SerializeField] private float assaultRadius = 80.0f; // Only raid a player who is within this distance of the camp

        private HostileMobAI leaderMobAI;
        private Vector3 campCenterPosition;
        private int initialSquadSize;
        private float assaultTimer = 0f;

        public SquadCommand CurrentCommand => currentSquadCommand;
        public IReadOnlyList<HostileMobAI> SquadMembers => squadMembers;

        private void Awake()
        {
            leaderMobAI = GetComponent<HostileMobAI>();
        }

        private void Start()
        {
            // Captured in Start: spawners position the leader only after AddComponent (Awake).
            campCenterPosition = transform.position;
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
            // A dead warlord can no longer command the squad.
            if (leaderMobAI != null && leaderMobAI.Health != null && !leaderMobAI.Health.IsAlive) return;

            HealthComponent playerHp = (leaderMobAI != null) ? leaderMobAI.GetPlayerHealth() : null;
            if (playerHp == null || Vector3.Distance(campCenterPosition, playerHp.transform.position) > assaultRadius) return;

            if (squadMembers.Count >= Mathf.Max(2, initialSquadSize / 2))
            {
                OrderAssaultOnPlayerBase();
            }
        }

        public void OrderAssaultOnPlayerBase()
        {
            // Without a living target the Chase state would immediately fall back to Wander,
            // so there is nothing to assault.
            HealthComponent playerHp = (leaderMobAI != null) ? leaderMobAI.GetPlayerHealth() : null;
            if (playerHp == null || !playerHp.IsAlive) return;

            currentSquadCommand = SquadCommand.AssaultPlayerBase;

            Debug.Log($"[Hostile Leader] {gameObject.name} наказує загону ШТУРМУВАТИ базу гравця! Загін: {squadMembers.Count} бійців.");

            for (int i = 0; i < squadMembers.Count; i++)
            {
                if (squadMembers[i] == null) continue;
                squadMembers[i].OrderAttack(playerHp);
            }

            if (leaderMobAI != null)
            {
                leaderMobAI.OrderAttack(playerHp);
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
