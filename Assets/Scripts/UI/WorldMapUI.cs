using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.Net;
using BattleOfAges.SaveLoad;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The global map (N, docs/GAME_DESIGN.md §5.4): the relief of the whole world and every kingdom on it, with the player, its
    /// settlements and the other players. For looking only for now: no orders, no fog. Closes with its key, Esc or the cross.
    /// </summary>
    public sealed class WorldMapUI : MonoBehaviour
    {
        private const float MapPx = 900f, ListWidth = 330f, RefreshEvery = 1f, HeroMark = 18f, DrawMillisecondsPerFrame = 2f;

        private static readonly Color Enemy = new Color(0.92f, 0.27f, 0.2f), EmpireColour = new Color(0.98f, 0.78f, 0.25f), Band = new Color(0.72f, 0.70f, 0.66f),
                                      Own = new Color(0.32f, 0.62f, 1f), OtherPlayer = new Color(0.35f, 0.92f, 0.85f);

        private static WorldMapUI instance;
        public static WorldMapUI Instance => instance;
        public static bool IsVisible => instance != null && instance.open;

        private GameObject window;
        private RawImage picture;
        private RectTransform marks, hero;
        private Text list;
        private bool open;
        private float refresh;
        private readonly List<Mark> pool = new List<Mark>();
        private int used;

        private sealed class Mark
        {
            public RectTransform Rect;
            public Image Dot;
            public Text Label;
            public string Tip;
        }

        /// <summary>Marks showing now, and the words beside the map (tests).</summary>
        public int MarksShown => used;
        public string ListText => list != null ? list.text : "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("WorldMapUI").AddComponent<WorldMapUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            window.SetActive(false);
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy() { if (instance == this) instance = null; }

        public void Open()
        {
            open = true;
            window.SetActive(true);
            picture.texture = WorldMapPicture.Current;
            refresh = 0f;
            Refresh();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            open = false;
            window.SetActive(false);
            GameModeManager modes = GameContext.Modes;
            if (!GameWindows.AnyModalOpen && !GameWindows.Paused && (modes == null || modes.CurrentMode == GameMode.FirstPerson))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void Update()
        {
            bool key = KeybindsManager.GetKeyDown(KeyAction.WorldMap);
            if (open)
            {
                if (key) { Close(); return; } // (Esc: the pause menu closes every window)
                refresh -= Time.unscaledDeltaTime;
                if (refresh <= 0f) Refresh();
                return;
            }
            if (!SaveSystem.InGame) return;
            WorldMapPicture.Advance(DrawMillisecondsPerFrame); // drawn ahead, a little each frame: the map opens at once
            if (key && !GameWindows.Paused && !GameWindows.AnyModalOpen) Open();
        }

        // ------------------------------------------------------------------ what is on it

        private void Refresh()
        {
            refresh = RefreshEvery;
            used = 0;
            CoarseWorldMap map = CoarseWorldMap.Current;
            GameObject player = GameContext.Player;
            Vector3 me = player != null ? player.transform.position : Vector3.zero;

            List<WorldMapEntry> kingdoms = WorldMapData.Now();
            kingdoms.Sort((a, b) => Flat(a.At, me).CompareTo(Flat(b.At, me)));
            foreach (WorldMapEntry k in kingdoms)
            {
                bool band = k.Kind == WorldMapKind.FreeBand, empire = k.Kind == WorldMapKind.Empire;
                Show(map, k.At, band ? Band : empire ? EmpireColour : Enemy, band ? 10f : empire ? 22f : 16f, band ? "" : k.Name, Describe(k, me));
            }
            foreach (TownHall hall in TownHall.All)
            {
                if (hall != null) Show(map, hall.transform.position, Own, 16f, "", TipText.Title("Ваше поселення", Own) + $"\nРатуша: рівень {hall.Level}");
            }
            NetSession session = NetSession.Current;
            if (session != null)
                foreach (NetPlayerAt other in session.PlayersOnMap())
                {
                    if (other.Number == session.Number) continue;
                    string name = other.Number == 0 ? "Хост" : "Гравець " + other.Number;
                    Show(map, other.At, OtherPlayer, 13f, name, TipText.Title(name, OtherPlayer) + "\n" + Far(other.At, me));
                }
            for (int i = used; i < pool.Count; i++) if (pool[i].Rect.gameObject.activeSelf) pool[i].Rect.gameObject.SetActive(false);

            bool here = player != null;
            if (hero.gameObject.activeSelf != here) hero.gameObject.SetActive(here);
            if (here)
            {
                hero.anchoredPosition = OnMap(map, me);
                hero.localEulerAngles = new Vector3(0f, 0f, -player.transform.eulerAngles.y);
                hero.SetAsLastSibling();
            }
            list.text = ListOf(kingdoms, me);
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static Vector2 OnMap(CoarseWorldMap map, Vector3 world)
        {
            Vector2 p = WorldMapPicture.Place(map, world);
            return (p - new Vector2(0.5f, 0.5f)) * MapPx;
        }

        private void Show(CoarseWorldMap map, Vector3 at, Color color, float size, string label, string tip)
        {
            while (pool.Count <= used) pool.Add(NewMark());
            Mark m = pool[used++];
            if (!m.Rect.gameObject.activeSelf) m.Rect.gameObject.SetActive(true);
            m.Rect.anchoredPosition = OnMap(map, at);
            m.Rect.sizeDelta = new Vector2(size, size);
            m.Dot.color = color;
            m.Label.text = label;
            m.Tip = tip;
            // A name stays on the map, however near the edge its kingdom lies
            float half = m.Label.preferredWidth * 0.5f + 4f, x = m.Rect.anchoredPosition.x;
            float shift = Mathf.Clamp(x, -MapPx * 0.5f + half, MapPx * 0.5f - half) - x;
            m.Label.rectTransform.parent.GetComponent<RectTransform>().anchoredPosition = new Vector2(shift, -2f);
        }

        private Mark NewMark()
        {
            RectTransform r = UiTheme.Rect(marks, "Mark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
            var m = new Mark { Rect = r };
            RectTransform edge = UiTheme.Child(r, "Edge", Vector2.zero, Vector2.one, new Vector2(-2f, -2f), new Vector2(2f, 2f));
            Image outline = edge.gameObject.AddComponent<Image>();
            outline.sprite = UiTheme.RoundSprite;
            outline.color = new Color(0.05f, 0.04f, 0.03f, 0.9f);
            outline.raycastTarget = false;
            RectTransform inner = UiTheme.Child(r, "Dot", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            m.Dot = inner.gameObject.AddComponent<Image>();
            m.Dot.sprite = UiTheme.RoundSprite;
            RectTransform words = UiTheme.Rect(r, "Name", new Vector2(0.5f, 0f), new Vector2(0f, -2f), new Vector2(240f, 22f));
            words.pivot = new Vector2(0.5f, 1f);
            m.Label = UiTheme.Label(words, "Text", "", 16, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            Tooltip.Set(inner.gameObject, () => m.Tip);
            return m;
        }

        /// <summary>A kingdom in words: what it is, how big, how far.</summary>
        public static string Describe(WorldMapEntry k, Vector3 from)
        {
            string kind = k.Kind == WorldMapKind.Empire ? "Центральна імперія" : k.Kind == WorldMapKind.FreeBand ? "Вільна ватага, без ратуші" : "Королівство";
            Color color = k.Kind == WorldMapKind.Empire ? EmpireColour : k.Kind == WorldMapKind.FreeBand ? Band : Enemy;
            var b = new StringBuilder(TipText.Title(k.Name, color));
            if (k.Name != kind) b.Append('\n').Append(TipText.Dim(kind));
            if (k.Kind != WorldMapKind.FreeBand) b.Append('\n').Append(k.Hall > 0 ? $"Ратуша: рівень {k.Hall}" : "Ратуші ще немає");
            b.Append($"\nЛюдей: {k.People}\n").Append(Far(k.At, from));
            return b.ToString();
        }

        /// <summary>How far, as a player reads it.</summary>
        public static string Far(Vector3 at, Vector3 from)
        {
            float metres = Flat(at, from);
            return metres < 1000f ? $"До вас: {Mathf.RoundToInt(metres / 10f) * 10} м" : $"До вас: {metres / 1000f:0.0} км";
        }

        /// <summary>Beside the map: what the colours mean, and the kingdoms from the nearest.</summary>
        private static string ListOf(List<WorldMapEntry> kingdoms, Vector3 me)
        {
            var b = new StringBuilder();
            b.Append(Colour(Enemy)).Append("● королівство</color>\n");
            b.Append(Colour(EmpireColour)).Append("● Центральна імперія</color>\n");
            b.Append(Colour(Band)).Append("● вільна ватага</color>\n");
            b.Append(Colour(Own)).Append("● ваше поселення</color>\n");
            if (NetSession.Active) b.Append(Colour(OtherPlayer)).Append("● інший гравець</color>\n");
            b.Append("<color=#ffffff>▲ ви</color>\n\n");
            int standing = 0;
            foreach (WorldMapEntry k in kingdoms) if (k.Kind != WorldMapKind.FreeBand) standing++;
            b.Append("<b>Королівств: ").Append(standing).Append("</b>\n");
            if (standing == 0) b.Append(NetSession.IsGuest ? "Хост ще не сказав, де вони.\n" : "У цьому світі їх немає.\n");
            foreach (WorldMapEntry k in kingdoms)
            {
                if (k.Kind == WorldMapKind.FreeBand) continue;
                float km = Flat(k.At, me) / 1000f;
                b.Append(k.Name).Append(": ").Append(km.ToString("0.0")).Append(" км, людей ").Append(k.People).Append('\n');
            }
            return b.ToString().TrimEnd('\n');
        }

        private static string Colour(Color c) => "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">";

        // ------------------------------------------------------------------ the window

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("WorldMapCanvas", 125);
            canvas.transform.SetParent(transform, false); // survives scene loads with this object
            window = canvas.gameObject;
            var size = new Vector2(MapPx + ListWidth + 58f, MapPx + 92f);
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, size);
            PopIn.On(panel);
            UiTheme.PanelImage(panel);

            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(600f, 48f));
            UiTheme.Label(head, "Text", "Мапа світу", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(46f, 44f), Close, 28);

            RectTransform view = UiTheme.Rect(panel, "Map", new Vector2(0f, 0f), new Vector2(22f, 22f), new Vector2(MapPx, MapPx));
            view.gameObject.AddComponent<RectMask2D>();
            picture = view.gameObject.AddComponent<RawImage>();
            picture.raycastTarget = false;
            marks = UiTheme.Rect(view, "Marks", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MapPx, MapPx));

            hero = UiTheme.Rect(marks, "Hero", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(HeroMark, HeroMark * 1.5f));
            Image arrow = hero.gameObject.AddComponent<Image>();
            arrow.sprite = HeroArrow;
            arrow.raycastTarget = false;

            RectTransform side = UiTheme.Rect(panel, "List", new Vector2(1f, 1f), new Vector2(-18f, -66f), new Vector2(ListWidth, MapPx - 10f));
            list = UiTheme.Label(side, "Text", "", 18, TextAnchor.UpperLeft, UiTheme.Ink);
            list.horizontalOverflow = HorizontalWrapMode.Wrap;
            list.supportRichText = true;
            list.lineSpacing = 1.1f;
        }

        private static Sprite heroArrow;

        /// <summary>A white arrowhead, up, dark round the edge: it reads on any ground.</summary>
        private static Sprite HeroArrow
        {
            get
            {
                if (heroArrow != null) return heroArrow;
                const int w = 28, h = 42;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float half = (1f - (y + 0.5f) / h) * (w * 0.5f), off = Mathf.Abs(x + 0.5f - w * 0.5f);
                        float notch = y < h * 0.22f ? (h * 0.22f - y) / (h * 0.22f) * w * 0.32f : 0f;
                        bool inside = off < half && off >= notch;
                        bool core = off < half - 3f && off >= notch + 1.5f && y > 3 && y < h - 6;
                        tex.SetPixel(x, y, !inside ? Color.clear : core ? Color.white : new Color(0.05f, 0.04f, 0.03f));
                    }
                tex.Apply();
                heroArrow = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
                return heroArrow;
            }
        }
    }
}
