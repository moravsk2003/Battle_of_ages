using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Resources;
using BattleOfAges.UI;

namespace BattleOfAges.RTS
{
    /// <summary>
    /// The settlement's call to arms (one kind of unit: the player's people work and fight, docs/GAME_DESIGN.md §11.1):
    /// while it sounds, everyone drops the work, comes home and goes for the enemies within AlarmRadius of the hall; it
    /// stands down by itself AlarmStandDownSeconds after the last enemy is gone, and the people go back to work. Raised
    /// with the Alarm key (T) or the map button, and by itself (AutoAlarm) when an enemy comes within AlarmTriggerRadius
    /// of the hall - as an NPC king's people defend their home.
    /// </summary>
    public sealed class SettlementAlarm : MonoBehaviour
    {
        public static bool Active { get; private set; }
        private static SettlementAlarm instance;

        private float lastEnemySeen, nextCheck;
        private bool gathered;
        private static readonly int[] buffer = new int[128];
        private static readonly List<HealthComponent> enemies = new List<HealthComponent>(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Active = false; instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install() => Ensure();

        private static SettlementAlarm Ensure()
        {
            if (instance != null) return instance;
            var go = new GameObject("SettlementAlarm");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SettlementAlarm>();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Active = false; // a new game or a loaded one starts at peace
            return instance;
        }

        /// <summary>Where the settlement is: its hall, else the chest nearest the player, else the player.</summary>
        public static bool Home(out Vector3 home)
        {
            TownHall hall = TownHall.Current;
            if (hall != null) { home = hall.transform.position; return true; }
            GameObject player = GameObject.Find("Player");
            if (player == null) { home = Vector3.zero; return false; }
            home = StorageChestNode.GetNearestChestDepositPosition(player.transform.position);
            return true;
        }

        public static void Raise(string why = null)
        {
            SettlementAlarm a = Ensure();
            if (Active) return;
            Active = true;
            a.lastEnemySeen = Time.time;
            a.gathered = false;
            a.nextCheck = 0f;
            StrategyHud.Notify(why ?? "Тривога! Усі до зброї");
        }

        public static void StandDown(string why = null)
        {
            if (!Active) return;
            Active = false;
            StrategyHud.Notify(why ?? "Відбій: усі до роботи");
        }

        public static void Toggle() { if (Active) StandDown(); else Raise(); }

        /// <summary>Living enemies of the player within radius of this place (into the shared list).</summary>
        private static List<HealthComponent> EnemiesNear(Vector3 place, float radius)
        {
            enemies.Clear();
            int n = UnitSpatialGrid.QueryEnemies(place, radius, UnitSpatialGrid.TeamPlayer, buffer);
            for (int i = 0; i < n; i++)
            {
                HealthComponent h = UnitSpatialGrid.Health(buffer[i]);
                if (h != null && h.IsAlive) enemies.Add(h);
            }
            return enemies;
        }

        private void Update()
        {
            if (!GameWindows.BlockGameplay && KeybindsManager.GetKeyDown(KeyAction.Alarm)) Toggle();
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 1f;
            Tick();
        }

        /// <summary>Once a second: raise it by itself, or lead the defence and stand down when it is over.</summary>
        public void Tick()
        {
            if (!Home(out Vector3 home)) return;
            if (!Active)
            {
                if (GameBalance.AutoAlarm && EnemiesNear(home, GameBalance.AlarmTriggerRadius).Count > 0) Raise("Тривога! Вороги біля поселення");
                return;
            }

            List<HealthComponent> near = EnemiesNear(home, GameBalance.AlarmRadius);
            if (near.Count > 0) lastEnemySeen = Time.time;
            else if (Time.time - lastEnemySeen >= GameBalance.AlarmStandDownSeconds) { StandDown(); return; }

            var units = UnitRegistry.ActiveUnits;
            for (int i = 0; i < units.Count; i++)
            {
                RtsUnit u = units[i];
                if (u == null || u.IsHero || !u.IsAlive || u.InsideMine || u.HasAttackTarget) continue;
                if (near.Count > 0)
                {
                    HealthComponent best = null;
                    float bestD = float.MaxValue;
                    foreach (HealthComponent e in near)
                    {
                        float d = (e.transform.position - u.transform.position).sqrMagnitude;
                        if (d < bestD) { bestD = d; best = e; }
                    }
                    u.IssueAttackCommand(best);
                }
                else if (!gathered && (u.transform.position - home).sqrMagnitude > GameBalance.AlarmGatherRadius * GameBalance.AlarmGatherRadius)
                {
                    Vector2 r = Random.insideUnitCircle * 8f;
                    u.IssueMoveCommand(home + new Vector3(r.x, 0f, r.y)); // everyone home
                }
            }
            if (near.Count == 0) gathered = true;
        }
    }
}
