using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BattleOfAges.InputSystem
{
    /// <summary>
    /// Unified input wrapper bridging Unity Input System (New) and Legacy Input Manager.
    /// Safely handles mouse and keyboard delta queries across all Unity Project input settings.
    /// </summary>
    public static class UniversalInput
    {
        private const float MOUSE_DELTA_SENSITIVITY = 0.1f;
        private const float SCROLL_WHEEL_SENSITIVITY = 0.005f;

        public static bool IsShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
            }
#endif
            try
            {
                return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            }
            catch
            {
                return false;
            }
        }

        public static bool GetKeyDownEscape()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                return Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame;
            }
#endif
            try
            {
                return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);
            }
            catch
            {
                return false;
            }
        }

        public static bool GetKeyDownM()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                return Keyboard.current.mKey.wasPressedThisFrame;
            }
#endif
            try
            {
                return Input.GetKeyDown(KeyCode.M);
            }
            catch
            {
                return false;
            }
        }

        public static Vector2 GetMoveInput()
        {
            Vector2 move = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) move.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) move.y -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) move.x += 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) move.x -= 1f;
            }
#endif
            if (move == Vector2.zero)
            {
                try
                {
                    move.x = Input.GetAxisRaw("Horizontal");
                    move.y = Input.GetAxisRaw("Vertical");
                }
                catch { }
            }
            return move;
        }

        public static Vector2 GetMouseDelta()
        {
            Vector2 delta = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                Vector2 rawDelta = Mouse.current.delta.ReadValue();
                if (rawDelta.sqrMagnitude > 0.0001f)
                {
                    return rawDelta * MOUSE_DELTA_SENSITIVITY;
                }
            }
#endif

            try
            {
                delta.x = Input.GetAxis("Mouse X");
                delta.y = Input.GetAxis("Mouse Y");
            }
            catch
            {
                // Fallback safely if legacy input manager is disabled in project settings
            }

            return delta;
        }

        public static bool IsJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
            try
            {
                return Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.Space);
            }
            catch
            {
                return false;
            }
        }

        public static bool GetMouseButtonDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (button == 0 && Mouse.current.leftButton.wasPressedThisFrame) return true;
                if (button == 1 && Mouse.current.rightButton.wasPressedThisFrame) return true;
                if (button == 2 && Mouse.current.middleButton.wasPressedThisFrame) return true;
            }
#endif
            try
            {
                return Input.GetMouseButtonDown(button);
            }
            catch
            {
                return false;
            }
        }

        public static bool GetMouseButton(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (button == 0 && Mouse.current.leftButton.isPressed) return true;
                if (button == 1 && Mouse.current.rightButton.isPressed) return true;
                if (button == 2 && Mouse.current.middleButton.isPressed) return true;
            }
#endif
            try
            {
                return Input.GetMouseButton(button);
            }
            catch
            {
                return false;
            }
        }

        public static bool GetMouseButtonUp(int button)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                if (button == 0 && Mouse.current.leftButton.wasReleasedThisFrame) return true;
                if (button == 1 && Mouse.current.rightButton.wasReleasedThisFrame) return true;
                if (button == 2 && Mouse.current.middleButton.wasReleasedThisFrame) return true;
            }
#endif
            try
            {
                return Input.GetMouseButtonUp(button);
            }
            catch
            {
                return false;
            }
        }

        public static Vector3 GetMousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }
#endif
            try
            {
                return Input.mousePosition;
            }
            catch
            {
                return Vector3.zero;
            }
        }

        public static float GetScrollWheel()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                return Mouse.current.scroll.ReadValue().y * SCROLL_WHEEL_SENSITIVITY;
            }
#endif
            try
            {
                return Input.GetAxis("Mouse ScrollWheel");
            }
            catch
            {
                return 0f;
            }
        }
    }
}
