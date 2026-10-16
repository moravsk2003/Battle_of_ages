using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.InputSystem;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The save / load window (main menu and pause menu): the autosave and three slots with date, play time and a
    /// summary; save (overwriting asks once more), load, delete (asks once more). Esc closes it.
    /// </summary>
    public sealed class SaveSlotsUI : MonoBehaviour
    {
        private static SaveSlotsUI instance;
        public static bool IsVisible => instance != null && instance.window != null && instance.window.activeSelf;

        private GameObject window;
        private Text title;
        private RectTransform list;
        private bool saving;
        private int armedSlot = -1;
        private string armedAction;
        private System.Action onClosed;

        public static void Open(bool save, System.Action closed = null)
        {
            if (instance == null) instance = new GameObject("SaveSlotsUI").AddComponent<SaveSlotsUI>();
            instance.saving = save;
            instance.onClosed = closed;
            instance.armedSlot = -1;
            instance.window.SetActive(true);
            instance.Refresh();
        }

        public static void Close()
        {
            if (instance == null || instance.window == null || !instance.window.activeSelf) return;
            instance.window.SetActive(false);
            System.Action cb = instance.onClosed;
            instance.onClosed = null;
            cb?.Invoke();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            window.SetActive(false);
        }

        private void OnDestroy() { if (instance == this) instance = null; }

        private void Update()
        {
            // In the game the pause menu handles Esc (it closes this window first)
            if (IsVisible && PauseMenuController.Instance == null && UniversalInput.GetKeyDownEscape()) Close();
        }

        // ------------------------------------------------------------------ actions

        private void Act(int slot, string action)
        {
            SaveMeta meta = SaveSystem.Meta(slot);
            bool needsConfirm = action == "delete" || (action == "save" && meta.Exists);
            if (needsConfirm && (armedSlot != slot || armedAction != action))
            {
                armedSlot = slot; armedAction = action;
                Refresh();
                return;
            }
            armedSlot = -1;
            switch (action)
            {
                case "save": SaveSystem.Save(slot); Refresh(); break;
                case "delete": SaveSystem.Delete(slot); Refresh(); break;
                case "load":
                    window.SetActive(false);
                    onClosed = null;
                    SaveSystem.Load(slot);
                    break;
            }
        }

        // ------------------------------------------------------------------ view

        private void Refresh()
        {
            title.text = saving ? "Зберегти гру" : "Завантажити гру";
            for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
            for (int slot = 0; slot <= SaveSystem.ManualSlots; slot++) Row(slot, (slot) * 112f);
        }

        private void Row(int slot, float y)
        {
            SaveMeta m = SaveSystem.Meta(slot);
            RectTransform row = UiTheme.Rect(list, "Slot" + slot, new Vector2(0.5f, 1f), new Vector2(0f, -y), new Vector2(760f, 102f));
            UiTheme.PanelImage(row, UiTheme.SlotSprite, slot == SaveSystem.AutoSlot ? new Color(0.85f, 0.85f, 0.95f) : Color.white);
            RectTransform nm = UiTheme.Rect(row, "Name", new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(420f, 32f));
            UiTheme.Label(nm, "Text", SaveSystem.SlotName(slot), 22, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            RectTransform info = UiTheme.Rect(row, "Info", new Vector2(0f, 0f), new Vector2(18f, 10f), new Vector2(470f, 52f));
            Text t = UiTheme.Label(info, "Text", "", 16, TextAnchor.UpperLeft, UiTheme.InkDim);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.text = m.Exists
                ? $"{m.SavedAt:dd.MM.yyyy HH:mm}   ·   у грі {PlayTime(m.PlaySeconds)}\nдерево {m.Wood}  ·  руда {m.Ore}  ·  юнітів {m.Units}  ·  будівель {m.Buildings}"
                : "Порожньо";

            float x = -14f;
            if (m.Exists && slot != SaveSystem.AutoSlot)
            {
                bool armed = armedSlot == slot && armedAction == "delete";
                UiTheme.TextButton(row, "Delete", armed ? "Точно?" : "×", new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(armed ? 92f : 44f, 44f), () => Act(slot, "delete"), armed ? 16 : 24)
                       .GetComponent<Image>().color = new Color(1f, 0.65f, 0.6f);
                x -= armed ? 102f : 54f;
            }
            if (saving)
            {
                bool armed = armedSlot == slot && armedAction == "save";
                UiTheme.TextButton(row, "Save", armed ? "Перезаписати?" : "Зберегти", new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(170f, 48f), () => Act(slot, "save"), 17);
            }
            else
            {
                Button b = UiTheme.TextButton(row, "Load", "Завантажити", new Vector2(1f, 0.5f), new Vector2(x, 0f), new Vector2(170f, 48f), () => Act(slot, "load"), 17);
                b.interactable = m.Exists;
            }
        }

        private static string PlayTime(float seconds)
        {
            int min = Mathf.FloorToInt(seconds / 60f);
            return min >= 60 ? $"{min / 60} год {min % 60:00} хв" : $"{min} хв";
        }

        private void Build()
        {
            DontDestroyOnLoad(gameObject);
            Canvas canvas = UiTheme.CreateCanvas("SaveSlotsCanvas", 400);
            canvas.transform.SetParent(transform, false);
            window = canvas.gameObject;
            RectTransform dim = UiTheme.Rect(canvas.transform, "Dim", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.sizeDelta = Vector2.zero;
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 600f));
            UiTheme.PanelImage(panel);
            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(600f, 44f));
            title = UiTheme.Label(head, "Text", "", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), Close, 28)
                   .GetComponent<Image>().color = new Color(1f, 0.72f, 0.68f);
            list = UiTheme.Rect(panel, "List", new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(760f, 450f));
            RectTransform hint = UiTheme.Rect(panel, "Hint", new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(760f, 28f));
            UiTheme.Label(hint, "Text", "F5 — швидке збереження  ·  F9 — швидке завантаження  ·  автозбереження кожні 5 хв", 15, TextAnchor.MiddleCenter, UiTheme.InkDim);
        }
    }
}
