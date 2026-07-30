using System;
using UnityEngine;
using BattleOfAges.Player;
using BattleOfAges.CameraSystem;
using BattleOfAges.InputSystem;

namespace BattleOfAges.Managers
{
    public enum GameMode
    {
        FirstPerson,
        RtsMap
    }

    /// <summary>
    /// Central manager responsible for toggling between First-Person RPG and RTS Strategy modes.
    /// Broadcasts mode changes via event channels to decouple camera and input systems.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameModeManager : MonoBehaviour
    {
        [Header("Mode Configuration")]
        [SerializeField] private GameMode initialMode = GameMode.FirstPerson;

        [Header("References")]
        [SerializeField] private FirstPersonController playerController;
        [SerializeField] private Camera fpsCamera;
        [SerializeField] private RtsMapCameraController mapCameraController;

        public GameMode CurrentMode { get; private set; }

        public event Action<GameMode> OnGameModeChanged;

        private void Start()
        {
            InitializeReferences();
            SetGameMode(initialMode);
        }

        private void InitializeReferences()
        {
            if (playerController == null)
            {
                playerController = FindFirstObjectByType<FirstPersonController>();
            }

            if (mapCameraController == null)
            {
                mapCameraController = FindFirstObjectByType<RtsMapCameraController>();
            }

            if (fpsCamera == null && playerController != null)
            {
                fpsCamera = playerController.GetComponentInChildren<Camera>();
            }
        }

        private void Update()
        {
            if (UniversalInput.GetKeyDownM())
            {
                ToggleMode();
            }
        }

        public void ToggleMode()
        {
            GameMode nextMode = (CurrentMode == GameMode.FirstPerson) ? GameMode.RtsMap : GameMode.FirstPerson;
            SetGameMode(nextMode);
        }

        public void SetGameMode(GameMode mode)
        {
            CurrentMode = mode;

            if (CurrentMode == GameMode.FirstPerson)
            {
                ApplyFirstPersonMode();
            }
            else if (CurrentMode == GameMode.RtsMap)
            {
                ApplyRtsMapMode();
            }

            OnGameModeChanged?.Invoke(CurrentMode);
        }

        private void ApplyFirstPersonMode()
        {
            if (fpsCamera != null) fpsCamera.enabled = true;
            if (playerController != null) playerController.EnableControl(true);
            if (mapCameraController != null) mapCameraController.EnableControl(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ApplyRtsMapMode()
        {
            if (playerController != null && mapCameraController != null)
            {
                mapCameraController.FocusOnPosition(playerController.transform.position);
            }

            if (playerController != null) playerController.EnableControl(false);
            if (fpsCamera != null) fpsCamera.enabled = false;
            if (mapCameraController != null) mapCameraController.EnableControl(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public GameMode GetCurrentMode() => CurrentMode;
    }
}
