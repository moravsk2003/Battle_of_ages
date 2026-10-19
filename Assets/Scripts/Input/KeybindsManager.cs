using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.InputSystem
{
    public enum KeyAction
    {
        WorkerCommandUI,
        Inventory,
        BuildMenu,
        ToggleGameMode,
        InteractGather,
        ChopTree,
        Equipment,
        Hero,
        Ability
    }

    /// <summary>
    /// Centralized Keybindings Manager supporting runtime key rebinding,
    /// PlayerPrefs persistence, and notification events.
    /// Default key for WorkerCommandUI is set to 'K'.
    /// </summary>
    public static class KeybindsManager
    {
        private static readonly Dictionary<KeyAction, KeyCode> defaultKeybinds = new Dictionary<KeyAction, KeyCode>
        {
            { KeyAction.WorkerCommandUI, KeyCode.K },
            { KeyAction.Inventory,        KeyCode.I },
            { KeyAction.BuildMenu,        KeyCode.B },
            { KeyAction.ToggleGameMode,   KeyCode.M },
            { KeyAction.InteractGather,   KeyCode.E },
            { KeyAction.ChopTree,         KeyCode.F },
            { KeyAction.Equipment,       KeyCode.C },
            { KeyAction.Hero,            KeyCode.H },
            { KeyAction.Ability,         KeyCode.Q }
        };

        private static readonly Dictionary<KeyAction, string> actionNamesUk = new Dictionary<KeyAction, string>
        {
            { KeyAction.WorkerCommandUI, "📜 Розподіл робіт" },
            { KeyAction.Inventory,        "🎒 Інвентар" },
            { KeyAction.BuildMenu,        "🏗 Меню будівництва" },
            { KeyAction.ToggleGameMode,   "🔄 Зміна режиму (RTS / FPS)" },
            { KeyAction.InteractGather,   "🖐 Збір / Взаємодія" },
            { KeyAction.ChopTree,         "🪓 Рубання дерев" },
            { KeyAction.Equipment,       "Спорядження" },
            { KeyAction.Hero,            "Герой" },
            { KeyAction.Ability,         "Уміння класу" }
        };

        private static readonly Dictionary<KeyAction, KeyCode> currentKeybinds = new Dictionary<KeyAction, KeyCode>();

        public static event Action OnKeybindsChanged;

        static KeybindsManager()
        {
            LoadAllKeybinds();
        }

        public static void LoadAllKeybinds()
        {
            currentKeybinds.Clear();
            HashSet<KeyCode> assignedKeys = new HashSet<KeyCode>();

            foreach (KeyAction action in Enum.GetValues(typeof(KeyAction)))
            {
                KeyCode defaultKey = defaultKeybinds[action];
                string prefKey = GetPrefKey(action);
                KeyCode targetKey = defaultKey;

                if (PlayerPrefs.HasKey(prefKey))
                {
                    string savedStr = PlayerPrefs.GetString(prefKey, defaultKey.ToString());
                    if (Enum.TryParse<KeyCode>(savedStr, out var parsedKey) && parsedKey != KeyCode.None)
                    {
                        targetKey = parsedKey;
                    }
                }

                // If another action is already using targetKey, fall back to unique defaultKey
                if (assignedKeys.Contains(targetKey))
                {
                    targetKey = defaultKey;
                    if (assignedKeys.Contains(targetKey))
                    {
                        targetKey = KeyCode.None;
                    }
                    PlayerPrefs.SetString(prefKey, targetKey.ToString());
                }

                if (targetKey != KeyCode.None)
                {
                    assignedKeys.Add(targetKey);
                }
                currentKeybinds[action] = targetKey;
            }

            PlayerPrefs.Save();
        }

        public static KeyCode GetKey(KeyAction action)
        {
            if (currentKeybinds.TryGetValue(action, out var key))
            {
                return key;
            }
            return defaultKeybinds.TryGetValue(action, out var defKey) ? defKey : KeyCode.None;
        }

        public static void SetKey(KeyAction action, KeyCode newKey)
        {
            if (newKey == KeyCode.None || newKey == KeyCode.Escape) return;

            // Automatically resolve conflicts: if another action already uses newKey, reset it to fallback
            foreach (KeyAction otherAction in Enum.GetValues(typeof(KeyAction)))
            {
                if (otherAction != action && currentKeybinds.TryGetValue(otherAction, out var existingKey))
                {
                    if (existingKey == newKey)
                    {
                        KeyCode fallbackKey = defaultKeybinds.TryGetValue(otherAction, out var def) ? def : KeyCode.None;
                        if (fallbackKey == newKey) fallbackKey = KeyCode.None;
                        currentKeybinds[otherAction] = fallbackKey;
                        PlayerPrefs.SetString(GetPrefKey(otherAction), fallbackKey.ToString());
                    }
                }
            }

            currentKeybinds[action] = newKey;
            PlayerPrefs.SetString(GetPrefKey(action), newKey.ToString());
            PlayerPrefs.Save();

            OnKeybindsChanged?.Invoke();
        }

        public static void ResetToDefaults()
        {
            foreach (var kvp in defaultKeybinds)
            {
                currentKeybinds[kvp.Key] = kvp.Value;
                PlayerPrefs.DeleteKey(GetPrefKey(kvp.Key));
            }
            PlayerPrefs.Save();
            OnKeybindsChanged?.Invoke();
        }

        public static bool GetKeyDown(KeyAction action)
        {
            KeyCode key = GetKey(action);
            return UniversalInput.IsKeyDown(key);
        }

        public static bool GetKeyHeld(KeyAction action)
        {
            KeyCode key = GetKey(action);
            return UniversalInput.IsKeyHeld(key);
        }

        public static string GetActionDisplayName(KeyAction action)
        {
            return actionNamesUk.TryGetValue(action, out var name) ? name : action.ToString();
        }

        public static string GetKeyDisplayString(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Alpha0: return "0";
                case KeyCode.Alpha1: return "1";
                case KeyCode.Alpha2: return "2";
                case KeyCode.Alpha3: return "3";
                case KeyCode.Alpha4: return "4";
                case KeyCode.Alpha5: return "5";
                case KeyCode.Alpha6: return "6";
                case KeyCode.Alpha7: return "7";
                case KeyCode.Alpha8: return "8";
                case KeyCode.Alpha9: return "9";
                case KeyCode.Keypad0: return "Num 0";
                case KeyCode.Keypad1: return "Num 1";
                case KeyCode.Keypad2: return "Num 2";
                case KeyCode.Keypad3: return "Num 3";
                case KeyCode.Keypad4: return "Num 4";
                case KeyCode.Keypad5: return "Num 5";
                case KeyCode.Keypad6: return "Num 6";
                case KeyCode.Keypad7: return "Num 7";
                case KeyCode.Keypad8: return "Num 8";
                case KeyCode.Keypad9: return "Num 9";
                case KeyCode.Return: return "Enter";
                case KeyCode.LeftShift: return "LShift";
                case KeyCode.RightShift: return "RShift";
                case KeyCode.LeftControl: return "LCtrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt: return "LAlt";
                case KeyCode.RightAlt: return "RAlt";
                default: return key.ToString();
            }
        }

        private static string GetPrefKey(KeyAction action)
        {
            return $"Settings_Keybind_{action}";
        }
    }
}
