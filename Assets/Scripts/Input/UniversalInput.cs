using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BattleOfAges.InputSystem
{
    /// <summary>
    /// Unified input wrapper bridging Unity Input System (New) and Legacy Input Manager.
    /// When the new Input System has the device it answers alone; the legacy manager is only a fallback (no device, or a
    /// key the new system has no name for) and only compiled when it is enabled, so with "Input System Package (New)"
    /// nothing calls it (a legacy call there throws, and an exception per frame is far slower than the query itself).
    /// </summary>
    public static class UniversalInput
    {
        private const float MOUSE_DELTA_SENSITIVITY = 0.1f;
        private const float SCROLL_WHEEL_SENSITIVITY = 0.005f;

        public static bool IsShiftHeld()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null) return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
            return false;
#endif
        }

        public static bool GetKeyDownEscape()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null) return kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        // KeyCode -> Key, parsed once per key (key.ToString() + Enum.TryParse every frame was garbage for the GC)
        private static readonly System.Collections.Generic.Dictionary<KeyCode, Key> keyMap = new System.Collections.Generic.Dictionary<KeyCode, Key>();

        private static Key ToInputKey(KeyCode code)
        {
            if (keyMap.TryGetValue(code, out Key k)) return k;
            string name = code.ToString();
            if (name.StartsWith("Alpha")) name = "Digit" + name.Substring(5);
            else if (code == KeyCode.Return) name = "Enter";
            else if (code == KeyCode.LeftControl) name = "LeftCtrl";
            else if (code == KeyCode.RightControl) name = "RightCtrl";
            if (!System.Enum.TryParse(name, true, out k)) k = Key.None;
            keyMap[code] = k;
            return k;
        }
#endif

        public static bool IsKeyDown(KeyCode key)
        {
            if (key == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            Key inputKey = ToInputKey(key);
            if (kb != null && inputKey != Key.None) return kb[inputKey].wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#else
            return false;
#endif
        }

        public static bool IsKeyHeld(KeyCode key)
        {
            if (key == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            Key inputKey = ToInputKey(key);
            if (kb != null && inputKey != Key.None) return kb[inputKey].isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#else
            return false;
#endif
        }

        public static bool GetKeyDownM()
        {
            return KeybindsManager.GetKeyDown(KeyAction.ToggleGameMode);
        }

        public static Vector2 GetMoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                Vector2 move = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                return move;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#else
            return Vector2.zero;
#endif
        }

        public static Vector2 GetMouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null) return mouse.delta.ReadValue() * MOUSE_DELTA_SENSITIVITY;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
#else
            return Vector2.zero;
#endif
        }

        public static bool IsJumpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null) return kb.spaceKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.ButtonControl Button(Mouse mouse, int button) =>
            button == 0 ? mouse.leftButton : button == 1 ? mouse.rightButton : button == 2 ? mouse.middleButton : null;
#endif

        public static bool GetMouseButtonDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null) { var b = Button(mouse, button); return b != null && b.wasPressedThisFrame; }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#else
            return false;
#endif
        }

        public static bool GetMouseButton(int button)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null) { var b = Button(mouse, button); return b != null && b.isPressed; }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#else
            return false;
#endif
        }

        public static bool GetMouseButtonUp(int button)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null) { var b = Button(mouse, button); return b != null && b.wasReleasedThisFrame; }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(button);
#else
            return false;
#endif
        }

        public static Vector3 GetMousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null) return mouse.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector3.zero;
#endif
        }

        public static float GetScrollWheel()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null) return mouse.scroll.ReadValue().y * SCROLL_WHEEL_SENSITIVITY;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetAxis("Mouse ScrollWheel");
#else
            return 0f;
#endif
        }
    }
}
