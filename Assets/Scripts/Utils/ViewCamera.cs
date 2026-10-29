using UnityEngine;
using BattleOfAges.CameraSystem;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// The camera the player is looking through: the RTS map camera while it is active (it is not tagged MainCamera,
    /// so Camera.main is null on the map), otherwise Camera.main. Cached for a frame.
    /// </summary>
    public static class ViewCamera
    {
        private static Camera cached;
        private static int frame = -1;

        public static Camera Current
        {
            get
            {
                if (frame == Time.frameCount && cached != null) return cached;
                frame = Time.frameCount;
                RtsMapCameraController map = Managers.GameContext.MapCamera; // the one registered last (a new scene's, not a stale one)
                Camera mapCam = map != null ? map.GetComponent<Camera>() : null;
                cached = mapCam != null && mapCam.isActiveAndEnabled ? mapCam : Camera.main;
                return cached;
            }
        }
    }
}
