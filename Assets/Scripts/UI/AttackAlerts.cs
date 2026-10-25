using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// "We are under attack" (docs/GAME_DESIGN.md §21, stage 1.2): an enemy hits one of our units or buildings → one
    /// alert per place (cell of Cell metres) at most every Cooldown seconds. The HUD shows it as a red toast and a ping
    /// on the minimap; Space on the map takes the camera to the last one.
    /// </summary>
    public static class AttackAlerts
    {
        public const float Cooldown = 10f, Cell = 30f, PingSeconds = 8f;

        /// <summary>(what was attacked, where) — raised once per alert.</summary>
        public static event Action<string, Vector3> Raised;

        public static bool HasLast { get; private set; }
        public static Vector3 Last { get; private set; }
        public static float LastTime { get; private set; } = float.NegativeInfinity;

        private static readonly Dictionary<Vector2Int, float> lastAt = new Dictionary<Vector2Int, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook()
        {
            HealthComponent.AnyDamaged -= OnDamaged;
            HealthComponent.AnyDamaged += OnDamaged;
            Reset();
        }

        public static void Reset()
        {
            lastAt.Clear();
            HasLast = false;
            LastTime = float.NegativeInfinity;
        }

        private static void OnDamaged(HealthComponent target, int damage)
        {
            if (target == null || !Application.isPlaying) return;
            if (Describe(target, out string what)) Report(what, target.transform.position, Time.unscaledTime);
        }

        /// <summary>
        /// Our building or unit hit by someone not on our side → what it is ("Ратуша", "робітник"); the player hero
        /// counts only while the map is open (in first person the player feels it).
        /// </summary>
        public static bool Describe(HealthComponent target, out string what)
        {
            what = null;
            CombatUnit attacker = target.LastAttacker;
            if (attacker != null && attacker.Team == UnitSpatialGrid.TeamPlayer) return false;
            BuildingNode b = target.GetComponentInParent<BuildingNode>();
            if (b != null) { what = BuildingData.GetDefinition(b.Type).DisplayName; return true; }
            RtsUnit u = target.GetComponent<RtsUnit>();
            if (u == null) return false;
            CombatUnit own = target.GetComponent<CombatUnit>();
            if (own != null && own.Team != UnitSpatialGrid.TeamPlayer) return false;
            if (u.IsHero)
            {
                if (!MapOpen()) return false;
                what = "герой";
                return true;
            }
            UnitProgress p = target.GetComponent<UnitProgress>();
            UnitRank rank = p != null ? p.Rank : UnitRank.Person;
            what = target.GetComponent<NpcResourceGatherer>() != null && rank == UnitRank.Person ? "робітник" : HeroUI.RankName(rank);
            return true;
        }

        /// <summary>Raises the alert unless this place had one in the last Cooldown seconds; true when raised.</summary>
        public static bool Report(string what, Vector3 where, float now)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(where.x / Cell), Mathf.FloorToInt(where.z / Cell));
            if (lastAt.TryGetValue(cell, out float t) && now - t < Cooldown) return false;
            lastAt[cell] = now;
            HasLast = true;
            Last = where;
            LastTime = now;
            Raised?.Invoke(what, where);
            return true;
        }

        private static Managers.GameModeManager modes;
        private static bool MapOpen()
        {
            if (modes == null) modes = UnityEngine.Object.FindFirstObjectByType<Managers.GameModeManager>();
            return modes != null && modes.CurrentMode == Managers.GameMode.RtsMap;
        }
    }
}
