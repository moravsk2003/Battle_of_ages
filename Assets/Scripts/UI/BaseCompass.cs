using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Where the player's base is (its hall, TownHall.Current): in first person a mark slides along the top of the screen
    /// towards it with how far it is, and stands at the edge with an arrow while it is behind; on the map it shows at the
    /// screen's edge, towards the hall, while the hall is off the screen. Hidden near home (HideWithin) and with no hall.
    /// </summary>
    public sealed class BaseCompass : MonoBehaviour
    {
        public const float HideWithin = 30f;

        private static BaseCompass instance;
        private RectTransform layer, mark;
        private TMP_Text label;
        private string shown;

        /// <summary>The mark is up, and its place on its layer (tests).</summary>
        public static bool Shown => instance != null && instance.mark != null && instance.mark.gameObject.activeSelf;
        public static Vector2 Place => instance != null && instance.mark != null ? instance.mark.anchoredPosition : Vector2.zero;
        public static string Said => instance != null && instance.label != null ? instance.label.text : "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("BaseCompass").AddComponent<BaseCompass>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Canvas canvas = UiTheme.CreateCanvas("BaseCompassCanvas", 40);
            canvas.transform.SetParent(transform, false);
            layer = (RectTransform)canvas.transform;
            mark = UiTheme.Rect(layer, "BaseMark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 34f));
            UiTheme.PanelImage(mark, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.9f)).raycastTarget = false;
            Image icon = UiTheme.IconImage(mark, UiTheme.Icon.House, new Vector2(0f, 0.5f), new Vector2(10f, 0f), 22f);
            icon.raycastTarget = false;
            label = UiTheme.Label(UiTheme.Rect(mark, "Text", new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(126f, 28f)), "Text", "", 15, TextAnchor.MiddleLeft, UiTheme.Ink);
            ((RectTransform)label.transform.parent).pivot = new Vector2(0f, 0.5f);
            LocText.Detach(label);
            mark.gameObject.SetActive(false);
        }

        private void OnDestroy() { if (instance == this) instance = null; }

        private void LateUpdate()
        {
            Camera cam = Utils.ViewCamera.Current;
            TownHall hall = TownHall.Current;
            bool on = cam != null && hall != null && !GameWindows.Paused && Managers.GameContext.Player != null && !GameWindows.AnyModalOpen;
            Vector2 at = default;
            float distance = 0f;
            string arrow = "";
            if (on)
            {
                Vector3 home = hall.transform.position;
                distance = new Vector2(home.x - Managers.GameContext.Player.transform.position.x, home.z - Managers.GameContext.Player.transform.position.z).magnitude;
                on = distance > HideWithin;
                if (on && Managers.GameContext.MapOpen) on = OnMapEdge(cam, home, out at, out arrow);
                else if (on) at = OnCompass(cam, home, out arrow);
            }
            if (mark.gameObject.activeSelf != on) mark.gameObject.SetActive(on);
            if (!on) return;
            mark.anchoredPosition = at;
            string said = arrow == "‹" ? $"‹ {Loc.Pick("База", "Base")} {Metres(distance)}"
                        : arrow == "›" ? $"{Loc.Pick("База", "Base")} {Metres(distance)} ›"
                        : $"{Loc.Pick("База", "Base")} {Metres(distance)}";
            if (said != shown) { shown = said; label.text = said; }
        }

        private static string Metres(float d) => d >= 1000f ? $"{d / 1000f:0.0} {Loc.Pick("км", "km")}" : $"{Mathf.RoundToInt(d / 10f) * 10} {Loc.Pick("м", "m")}";

        /// <summary>First person: along the top, by the hall's bearing from where the camera looks; at the edge with an arrow past the view.</summary>
        private Vector2 OnCompass(Camera cam, Vector3 home, out string arrow)
        {
            Vector3 look = cam.transform.forward, to = home - cam.transform.position;
            look.y = 0f; to.y = 0f;
            float bearing = Vector3.SignedAngle(look, to, Vector3.up);
            float halfFov = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect) * 0.5f;
            float half = layer.rect.width * 0.5f - 100f;
            arrow = bearing < -halfFov ? "‹" : bearing > halfFov ? "›" : "";
            return new Vector2(Mathf.Clamp(bearing / halfFov, -1f, 1f) * half, layer.rect.height * 0.5f - 96f);
        }

        /// <summary>The map: at the screen's edge towards the hall while it is off the screen (false while it is on it).</summary>
        private bool OnMapEdge(Camera cam, Vector3 home, out Vector2 at, out string arrow)
        {
            at = default;
            arrow = "";
            Vector3 vp = cam.WorldToViewportPoint(home);
            if (vp.z > 0f && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f) return false; // in sight
            Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (vp.z < 0f) dir = -dir;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.up;
            float scale = Mathf.Min(0.42f / Mathf.Max(1e-4f, Mathf.Abs(dir.x)), 0.40f / Mathf.Max(1e-4f, Mathf.Abs(dir.y)));
            Vector2 edge = dir * scale;
            at = new Vector2(edge.x * layer.rect.width, edge.y * layer.rect.height);
            arrow = dir.x < 0f ? "‹" : "›";
            return true;
        }
    }
}
