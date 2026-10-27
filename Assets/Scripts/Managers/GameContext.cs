using UnityEngine;
using BattleOfAges.CameraSystem;
using BattleOfAges.Combat;
using BattleOfAges.Player;
using BattleOfAges.RTS;

namespace BattleOfAges.Managers
{
    /// <summary>
    /// The few objects the whole game talks to — the mode, the commander, the map camera, the player — in one place
    /// instead of a scene search in each script. They register themselves when they wake; until then (or in a test
    /// scene) the first call finds them once. A missing player is looked for at most once a frame (it may be dead or
    /// not loaded yet), not by every caller.
    /// </summary>
    public static class GameContext
    {
        private static GameModeManager modes;
        private static RtsUnitCommander commander;
        private static RtsMapCameraController mapCamera;
        private static GameObject player;
        private static int playerSearchFrame = -1;

        public static GameModeManager Modes => modes != null ? modes : modes = Object.FindFirstObjectByType<GameModeManager>();
        public static RtsUnitCommander Commander => commander != null ? commander : commander = Object.FindFirstObjectByType<RtsUnitCommander>();
        public static RtsMapCameraController MapCamera => mapCamera != null ? mapCamera : mapCamera = Object.FindFirstObjectByType<RtsMapCameraController>();

        /// <summary>The player's hero object (named "Player"), or null.</summary>
        public static GameObject Player
        {
            get
            {
                if (player != null) return player;
                if (Time.frameCount == playerSearchFrame) return null;
                playerSearchFrame = Time.frameCount;
                return player = GameObject.Find("Player");
            }
        }

        public static HealthComponent PlayerHealth => Player != null ? Player.GetComponent<HealthComponent>() : null;
        public static PlayerCombatController PlayerCombat => Player != null ? Player.GetComponent<PlayerCombatController>() : null;

        /// <summary>The map is open (not first person).</summary>
        public static bool MapOpen => Modes != null && Modes.CurrentMode == GameMode.RtsMap;

        public static void Register(GameModeManager m) => modes = m;
        public static void Register(RtsUnitCommander c) => commander = c;
        public static void Register(RtsMapCameraController c) => mapCamera = c;
        public static void RegisterPlayer(GameObject p) => player = p;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Clear()
        {
            modes = null; commander = null; mapCamera = null; player = null;
            playerSearchFrame = -1;
        }
    }
}
