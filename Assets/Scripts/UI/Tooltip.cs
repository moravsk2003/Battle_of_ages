using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BattleOfAges.InputSystem;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Hover help on any UI element: Tooltip.Set(go, "text") or Set(go, () => text) for text that changes. The text
    /// (rich text) shows next to the cursor after a short delay, on one game-wide panel (TooltipUI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Tooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Func<string> text;

        public static Tooltip Set(GameObject go, string text) => Set(go, () => text);

        public static Tooltip Set(GameObject go, Func<string> text)
        {
            if (go == null) return null;
            Tooltip t = go.GetComponent<Tooltip>();
            if (t == null) t = go.AddComponent<Tooltip>();
            t.text = text;
            // Pointer events need a raycast target on the element itself or under it
            Graphic g = go.GetComponent<Graphic>();
            if (g != null) g.raycastTarget = true;
            return t;
        }

        public string Text => text?.Invoke();

        public void OnPointerEnter(PointerEventData e) => TooltipUI.Hover(this);
        public void OnPointerExit(PointerEventData e) => TooltipUI.Leave(this);
        private void OnDisable() => TooltipUI.Leave(this);
    }

    /// <summary>Pieces of tooltip text in one style: a coloured bold title, dim hints, a stats comparison.</summary>
    public static class TipText
    {
        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        public static string Title(string text, Color color) => $"<b><color={Hex(color)}>{text}</color></b>";
        public static string Dim(string text) => $"<color={Hex(UiTheme.InkDim)}>{text}</color>";
        public static string Key(string key) => $"<color={Hex(UiTheme.Gold)}>[{key}]</color>";
        public static string Good(string text) => $"<color={Hex(UiTheme.Good)}>{text}</color>";
        public static string Bad(string text) => $"<color={Hex(UiTheme.Bad)}>{text}</color>";

        /// <summary>
        /// The lines that change between two stat sets ("Здоров'я 500 → 550 +50"), better in green, worse in red; empty
        /// when nothing changes. A shorter cooldown is better.
        /// </summary>
        public static string Compare((int health, int damage, float cooldown, float reduction, float range) a,
                                     (int health, int damage, float cooldown, float reduction, float range) b, string heading = "Якщо надягти:")
        {
            var sb = new System.Text.StringBuilder();
            void Line(string name, float from, float to, string format, string unit, bool lowerIsBetter = false)
            {
                if (Mathf.Abs(to - from) < 0.005f) return;
                bool better = lowerIsBetter ? to < from : to > from;
                string delta = (to > from ? "+" : "−") + Mathf.Abs(to - from).ToString(format) + unit;
                sb.Append($"\n{name}  {from.ToString(format)}{unit} → {to.ToString(format)}{unit}  ").Append(better ? Good(delta) : Bad(delta));
            }
            Line("Здоров'я", a.health, b.health, "0", "");
            Line("Урон", a.damage, b.damage, "0", "");
            Line("Удар раз на", a.cooldown, b.cooldown, "0.0#", " с", true);
            Line("Поглинання", a.reduction * 100f, b.reduction * 100f, "0", "%");
            Line("Дальність", a.range, b.range, "0.#", " м");
            return sb.Length == 0 ? "" : "\n" + Dim(heading) + sb;
        }
    }

    /// <summary>
    /// The one tooltip panel: above every window, sized to its text (wraps at MaxWidth), kept on screen, refreshed while
    /// shown so live numbers stay current. Unscaled time, so it works on the pause screen too.
    /// </summary>
    public sealed class TooltipUI : MonoBehaviour
    {
        public const float Delay = 0.35f, MaxWidth = 420f;

        private static TooltipUI instance;
        private RectTransform root, panel;
        private Text label;
        private Tooltip hovered;
        private float since;
        private string shown;
        private float nextRead;

        public static bool IsShowing => instance != null && instance.panel.gameObject.activeSelf;
        public static string ShownText => IsShowing ? instance.shown : null;

        private static TooltipUI Instance
        {
            get
            {
                if (instance != null) return instance;
                var go = new GameObject("TooltipUI");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<TooltipUI>();
                instance.Build();
                return instance;
            }
        }

        public static void Hover(Tooltip t)
        {
            TooltipUI ui = Instance;
            ui.hovered = t;
            ui.since = Time.unscaledTime;
            ui.panel.gameObject.SetActive(false);
        }

        public static void Leave(Tooltip t)
        {
            if (instance == null || instance.hovered != t) return;
            instance.hovered = null;
            instance.panel.gameObject.SetActive(false);
        }

        /// <summary>Shows the hovered element's text at once (tests, and a click that keeps the cursor still).</summary>
        public static void ShowNow()
        {
            if (instance == null || instance.hovered == null) return;
            instance.since = -1e6f;
            instance.nextRead = 0f;
            instance.LateUpdate();
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("TooltipCanvas", 31000);
            canvas.transform.SetParent(transform, false);
            canvas.GetComponent<GraphicRaycaster>().enabled = false;
            root = (RectTransform)canvas.transform;
            panel = UiTheme.Rect(root, "Tooltip", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 40f));
            panel.pivot = new Vector2(0f, 1f);
            UiTheme.PanelImage(panel, null, new Color(1f, 1f, 1f, 0.98f)).raycastTarget = false;
            label = UiTheme.Label(panel, "Text", "", 17, TextAnchor.UpperLeft, UiTheme.Ink);
            label.rectTransform.offsetMin = new Vector2(14f, 10f);
            label.rectTransform.offsetMax = new Vector2(-14f, -10f);
            label.supportRichText = true;
            label.lineSpacing = 1.1f;
            panel.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (hovered == null || !hovered.isActiveAndEnabled) { if (panel.gameObject.activeSelf) panel.gameObject.SetActive(false); hovered = null; return; }
            if (Time.unscaledTime - since < Delay) return;
            if (panel.gameObject.activeSelf && Time.unscaledTime < nextRead) { Place(); return; }
            nextRead = Time.unscaledTime + 0.25f;
            string text = hovered.Text;
            if (string.IsNullOrEmpty(text)) { panel.gameObject.SetActive(false); return; }
            if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
            if (text != shown)
            {
                shown = text;
                label.text = text;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                float w = Mathf.Min(label.preferredWidth, MaxWidth);
                panel.sizeDelta = new Vector2(w + 28f, 40f);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                Canvas.ForceUpdateCanvases();
                panel.sizeDelta = new Vector2(w + 28f, label.preferredHeight + 22f);
            }
            Place();
        }

        private void Place()
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, UniversalInput.GetMousePosition(), null, out Vector2 local);
            Vector2 half = root.rect.size * 0.5f, size = panel.sizeDelta;
            float x = local.x + 20f, y = local.y - 22f;
            if (x + size.x > half.x) x = local.x - 12f - size.x; // flip to the left of the cursor at the right edge
            if (y - size.y < -half.y) y = local.y + 12f + size.y; // and above it at the bottom
            panel.anchoredPosition = new Vector2(Mathf.Clamp(x, -half.x, half.x - size.x), Mathf.Clamp(y, -half.y + size.y, half.y));
        }
    }
}
