using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BattleOfAges.UI
{
    /// <summary>
    /// A window that appears: fades in and grows from 96% over Duration each time it is shown (unscaled time, so on the
    /// pause screen too), with the open sound. Put on the window panel (not on a canvas: a canvas sizes itself).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PopIn : MonoBehaviour
    {
        public const float Duration = 0.12f;
        public bool Sound = true;
        private CanvasGroup group;
        private float start = -1f;

        public static PopIn On(Component target, bool sound = true)
        {
            if (target == null) return null;
            PopIn p = target.GetComponent<PopIn>();
            if (p == null) p = target.gameObject.AddComponent<PopIn>();
            p.Sound = sound;
            return p;
        }

        private void OnEnable()
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            start = Time.unscaledTime;
            Apply(0f);
            if (Sound) UiSound.Play(UiSound.Kind.Open);
        }

        private void OnDisable() => Apply(1f);

        private void Update()
        {
            if (start < 0f) return;
            float t = Mathf.Clamp01((Time.unscaledTime - start) / Duration);
            Apply(t);
            if (t >= 1f) start = -1f;
        }

        private void Apply(float t)
        {
            float e = 1f - (1f - t) * (1f - t); // ease out
            if (group != null) group.alpha = e;
            transform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, e);
        }
    }

    /// <summary>A button that answers: grows a little under the cursor, sinks when pressed, ticks and clicks.</summary>
    [DisallowMultipleComponent]
    public sealed class ButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private Button button;
        private float target = 1f, scale = 1f;
        private Vector3 baseScale = Vector3.one;
        private bool hovered;

        public static void On(Button b)
        {
            if (b != null && b.GetComponent<ButtonFx>() == null) b.gameObject.AddComponent<ButtonFx>();
        }

        private void Awake()
        {
            button = GetComponent<Button>();
            baseScale = transform.localScale;
            if (button != null) button.onClick.AddListener(() => UiSound.Play(UiSound.Kind.Click));
        }

        private bool Live => button == null || button.IsInteractable();

        public void OnPointerEnter(PointerEventData e) { hovered = true; if (!Live) return; target = 1.04f; UiSound.Play(UiSound.Kind.Hover); }
        public void OnPointerExit(PointerEventData e) { hovered = false; target = 1f; }
        public void OnPointerDown(PointerEventData e)
        {
            if (!Live) { UiSound.Play(UiSound.Kind.Error); return; }
            target = 0.96f;
        }
        public void OnPointerUp(PointerEventData e) => target = hovered && Live ? 1.04f : 1f;
        private void OnDisable() { hovered = false; target = scale = 1f; transform.localScale = baseScale; }

        private void Update()
        {
            if (Mathf.Abs(scale - target) < 0.001f) return;
            scale = Mathf.MoveTowards(scale, target, Time.unscaledDeltaTime * 1.2f);
            transform.localScale = baseScale * scale;
        }
    }
}
