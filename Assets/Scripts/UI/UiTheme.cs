using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Crafting;

namespace BattleOfAges.UI
{
    /// <summary>
    /// One look for the whole game UI: dark leather panels with bronze rims, gold accents, parchment text in a serif
    /// face with Cyrillic, all sprites and icons generated in code (no texture assets). Helpers build themed elements;
    /// Restyle converts UI that older scripts built with default looks (see UiRestyler).
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Panel = new Color(0.075f, 0.062f, 0.05f, 0.93f);
        public static readonly Color PanelLight = new Color(0.16f, 0.125f, 0.09f, 0.96f);
        public static readonly Color Rim = new Color(0.66f, 0.50f, 0.28f);
        public static readonly Color Gold = new Color(0.96f, 0.80f, 0.45f);
        public static readonly Color Ink = new Color(0.94f, 0.89f, 0.79f);
        public static readonly Color InkDim = new Color(0.72f, 0.66f, 0.56f);
        public static readonly Color Good = new Color(0.45f, 0.78f, 0.38f);
        public static readonly Color Bad = new Color(0.86f, 0.32f, 0.26f);

        public enum Icon { Wood, Ore, Worker, Army, Heart, Hammer, House, Wall, Fire, Bolt, Clock, Bell }

        private static Font font;
        private static Sprite panel, button, bar, slot, round;
        private static readonly Dictionary<Icon, Sprite> Icons = new Dictionary<Icon, Sprite>();

        /// <summary>Serif UI face with Cyrillic from the OS (Palatino / Georgia / Constantia), the built-in font otherwise.</summary>
        public static Font Font
        {
            get
            {
                if (font != null) return font;
                try { font = Font.CreateDynamicFontFromOSFont(new[] { "Palatino Linotype", "Book Antiqua", "Georgia", "Constantia", "Cambria" }, 32); }
                catch (Exception) { font = null; }
                if (font == null) font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        // ------------------------------------------------------------------ sprites

        // Windows and plates: leather with grain, a double bronze rim with a gold hairline inside, studs in the corners
        public static Sprite PanelSprite => panel != null ? panel : panel = Ornate(96, 10f, new Color(0.12f, 0.095f, 0.072f), new Color(0.058f, 0.046f, 0.036f), 3.2f, true, 0.55f, 0.035f);
        // Buttons: lighter leather, bevelled (light top edge, dark bottom), a gold rim
        public static Sprite ButtonSprite => button != null ? button : button = Ornate(64, 7f, new Color(0.36f, 0.27f, 0.17f), new Color(0.17f, 0.125f, 0.08f), 1.8f, false, 0.9f, 0.025f, bevel: true);
        // Slots and wells: sunk in, a dark inner shadow
        public static Sprite SlotSprite => slot != null ? slot : slot = Ornate(64, 6f, new Color(0.045f, 0.036f, 0.028f), new Color(0.085f, 0.07f, 0.055f), 1.4f, false, 0.25f, 0.02f, sunk: true);

        /// <summary>
        /// The themed frame: rounded rectangle with a vertical gradient (top to bottom) and leather grain; a bronze rim of
        /// rimWidth with a dark line outside and a gold hairline inside; optional corner studs, bevel or sunk shading. 9-sliced.
        /// </summary>
        private static Sprite Ornate(int size, float radius, Color top, Color bottom, float rimWidth, bool studs, float hairline, float grain, bool bevel = false, bool sunk = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UI_Ornate" };
            var px = new Color[size * size];
            float h = size * 0.5f;
            Color rimDark = new Color(0.03f, 0.022f, 0.015f), rimHi = new Color(0.80f, 0.62f, 0.36f), rimLo = new Color(0.42f, 0.30f, 0.16f);
            var rng = new System.Random(size * 31 + (int)(radius * 7f));
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - h, y + 0.5f - h);
                Vector2 q = new Vector2(Mathf.Abs(p.x) - (h - radius - 1f), Mathf.Abs(p.y) - (h - radius - 1f));
                float d = Mathf.Min(Mathf.Max(q.x, q.y), 0f) + new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude - radius; // <0 inside
                float a = Mathf.Clamp01(0.5f - d);
                float v = (y + 0.5f) / size;
                Color c = Color.Lerp(bottom, top, v);
                c *= 1f + ((float)rng.NextDouble() - 0.5f) * 2f * grain; // grain
                float inside = -d; // distance in from the edge
                if (sunk) c = Color.Lerp(c * 0.45f, c, Mathf.Clamp01((inside - rimWidth) / 7f));
                if (bevel)
                {
                    if (p.y > 0f) c = Color.Lerp(c, c * 1.35f, Mathf.Clamp01(1f - (inside - rimWidth) / 4f) * 0.8f);
                    else c = Color.Lerp(c, c * 0.6f, Mathf.Clamp01(1f - (inside - rimWidth) / 4f) * 0.8f);
                }
                // the rim: dark outer line, bronze lit from above, gold hairline inside
                if (inside < rimWidth + 1f)
                {
                    Color bronze = Color.Lerp(rimLo, rimHi, Mathf.Clamp01(v * 1.2f));
                    c = inside < 1f ? Color.Lerp(rimDark, bronze, Mathf.Clamp01(inside)) : Color.Lerp(bronze, c, Mathf.Clamp01(inside - rimWidth));
                }
                float hair = Mathf.Clamp01(1f - Mathf.Abs(inside - (rimWidth + 2f)) * 1.6f) * hairline;
                c = Color.Lerp(c, Gold, hair * 0.55f);
                if (studs)
                {
                    float sd = Mathf.Abs(Mathf.Abs(p.x) - (h - radius - 1f)) + Mathf.Abs(Mathf.Abs(p.y) - (h - radius - 1f));
                    if (sd < 3.6f) c = Color.Lerp(c, Color.Lerp(Gold, rimHi, Mathf.Clamp01(p.y > 0f ? 0f : 0.6f)), Mathf.Clamp01(3.6f - sd));
                }
                c.a = a;
                px[x + y * size] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            float b = Mathf.Ceil(radius + rimWidth + 4f);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static Sprite figure;
        /// <summary>A soft human silhouette, front view (the paper doll behind the equipment slots), white for tinting.</summary>
        public static Sprite FigureSprite
        {
            get
            {
                if (figure != null) return figure;
                const int W = 135, H = 205;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Figure" };
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f - W * 0.5f, y + 0.5f); // origin at the feet, centre line
                    Vector2 m = new Vector2(Mathf.Abs(p.x), p.y);           // mirrored
                    float d = (p - new Vector2(0f, 184f)).magnitude - 17f;                                  // head
                    d = Mathf.Min(d, Segment(p, new Vector2(0f, 160f), new Vector2(0f, 168f)) - 7f);         // neck
                    d = Mathf.Min(d, Box(p - new Vector2(0f, 123f), new Vector2(30f, 36f)) - 6f);            // torso
                    d = Mathf.Min(d, Segment(m, new Vector2(38f, 150f), new Vector2(50f, 84f)) - 8.5f);       // arms
                    d = Mathf.Min(d, (m - new Vector2(52f, 76f)).magnitude - 8f);                          // hands
                    d = Mathf.Min(d, Box(p - new Vector2(0f, 84f), new Vector2(28f, 10f)) - 4f);             // hips
                    d = Mathf.Min(d, Segment(m, new Vector2(15f, 76f), new Vector2(17f, 10f)) - 10f);         // legs
                    px[x + y * W] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d / 1.5f));
                }
                tex.SetPixels(px);
                tex.Apply();
                return figure = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        private static Sprite divider;
        /// <summary>A gold rule fading at both ends with a diamond in the middle (under window titles).</summary>
        public static Sprite DividerSprite
        {
            get
            {
                if (divider != null) return divider;
                const int W = 256, H = 16;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Divider" };
                var px = new Color[W * H];
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2f - 1f, vy = y + 0.5f - H * 0.5f;
                    float line = Mathf.Clamp01(1.2f - Mathf.Abs(vy)) * Mathf.Clamp01((1f - Mathf.Abs(u)) * 3f);
                    float diamond = Mathf.Clamp01(5.5f - (Mathf.Abs(x + 0.5f - W * 0.5f) + Mathf.Abs(vy) * 1.3f));
                    px[x + y * W] = new Color(Gold.r, Gold.g, Gold.b, Mathf.Max(line * 0.8f, diamond));
                }
                tex.SetPixels(px);
                tex.Apply();
                return divider = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        /// <summary>A divider image under a title: centred at offset from the parent's top, of the given width.</summary>
        public static Image Divider(Transform parent, float y, float width)
        {
            RectTransform rt = Rect(parent, "Divider", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(width, 14f));
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = DividerSprite; img.raycastTarget = false;
            return img;
        }
        /// <summary>Plain white rounded rectangle (bars, tinted chips).</summary>
        public static Sprite BarSprite => bar != null ? bar : bar = Frame(32, 6f, 0f, Color.white, Color.white, Color.white, 0f);
        public static Sprite RoundSprite => round != null ? round : round = Frame(64, 31f, 0f, Color.white, Color.white, Color.white, 0f);

        private static Sprite ring;
        /// <summary>A white ring (minimap pings, highlights), tinted by the Image colour.</summary>
        public static Sprite RingSprite
        {
            get
            {
                if (ring != null) return ring;
                const int N = 64;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI_Ring" };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = new Vector2(x + 0.5f - N * 0.5f, y + 0.5f - N * 0.5f).magnitude;
                    px[x + y * N] = new Color(1f, 1f, 1f, Mathf.Clamp01(3.5f - Mathf.Abs(d - 27f)));
                }
                tex.SetPixels(px);
                tex.Apply();
                return ring = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        /// <summary>Rounded rectangle, vertical gradient (top to bottom), rim of the given width, a faint inner highlight; 9-sliced.</summary>
        private static Sprite Frame(int size, float radius, float rimWidth, Color top, Color bottom, Color rim, float highlight)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UI_Frame" };
            var px = new Color[size * size];
            float h = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - h, y + 0.5f - h);
                Vector2 q = new Vector2(Mathf.Abs(p.x) - (h - radius), Mathf.Abs(p.y) - (h - radius));
                float d = Mathf.Min(Mathf.Max(q.x, q.y), 0f) + new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude - radius; // <0 inside
                float a = Mathf.Clamp01(0.5f - d);
                Color c = Color.Lerp(bottom, top, (y + 0.5f) / size);
                if (rimWidth > 0f)
                {
                    float rimT = Mathf.Clamp01(d + rimWidth + 0.5f);
                    c = Color.Lerp(c, rim, rimT);
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(d + rimWidth + 1.5f));
                    if (p.y > 0f) c = Color.Lerp(c, Color.white, inner * highlight * 0.35f);
                }
                c.a = a * (rimWidth > 0f ? 1f : 1f);
                px[x + y * size] = c;
            }
            tex.SetPixels(px);
            tex.Apply();
            float b = Mathf.Ceil(radius + rimWidth + 1f);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        public static Sprite IconSprite(Icon icon)
        {
            if (Icons.TryGetValue(icon, out Sprite s) && s != null) return s;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "UI_Icon_" + icon };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color acc = Color.clear;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    Vector2 p = new Vector2((x + (sx + 0.5f) / 3f) / N * 2f - 1f, (y + (sy + 0.5f) / 3f) / N * 2f - 1f);
                    Color c = Shade(icon, p);
                    acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                }
                acc /= 9f;
                px[x + y * N] = acc.a > 0f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
            }
            tex.SetPixels(px);
            tex.Apply(true);
            s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            Icons[icon] = s;
            return s;
        }

        private static readonly Dictionary<int, Texture2D> Cursors = new Dictionary<int, Texture2D>();

        /// <summary>
        /// A 32 px cursor (no mipmaps, as the OS cursor wants): a gold arrow (icon null, hotspot at its tip) or an icon
        /// (hotspot in the middle).
        /// </summary>
        public static Texture2D CursorTexture(Icon? icon)
        {
            int key = icon.HasValue ? (int)icon.Value : -1;
            if (Cursors.TryGetValue(key, out Texture2D t) && t != null) return t;
            const int N = 32;
            t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UI_Cursor_" + key };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color acc = Color.clear;
                for (int s = 0; s < 4; s++)
                {
                    float sx = x + (s % 2 + 0.5f) / 2f, sy = y + (s / 2 + 0.5f) / 2f;
                    Color c;
                    if (icon.HasValue) c = Shade(icon.Value, new Vector2(sx / N * 2f - 1f, sy / N * 2f - 1f) * 1.08f);
                    else
                    {
                        // the arrow points to the top left corner (texture y runs up: the tip is at y = N)
                        Vector2 p = new Vector2(sx, N - sy);
                        float d = Mathf.Min(Poly(p, new[] { new Vector2(2f, 2f), new Vector2(2f, 23f), new Vector2(18f, 16f) }),
                                           Poly(p, new[] { new Vector2(7.5f, 17f), new Vector2(11.5f, 15.5f), new Vector2(16.5f, 26f), new Vector2(12.5f, 28f) })); // head and tail (Poly wants convex)
                        c = d < 0f ? Color.Lerp(Gold, new Color(0.62f, 0.44f, 0.2f), Mathf.Clamp01(p.y / 26f)) : d < 1.6f ? new Color(0.07f, 0.05f, 0.03f, 1f) : Color.clear;
                    }
                    acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                }
                acc /= 4f;
                px[x + y * N] = acc.a > 0f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
            }
            t.SetPixels(px);
            t.Apply(false);
            Cursors[key] = t;
            return t;
        }

        // Icon artwork: layered signed distances in [-1, 1]^2, a dark outline under every layer.
        private static Color Shade(Icon icon, Vector2 p)
        {
            Color result = Color.clear;
            void Layer(float d, Color fill)
            {
                if (d < 0.07f) result = Over(result, new Color(0.08f, 0.06f, 0.04f, 1f));
                if (d < 0f) result = Over(result, fill);
            }
            switch (icon)
            {
                case Icon.Wood:
                    foreach (Vector2 c in new[] { new Vector2(-0.38f, -0.32f), new Vector2(0.38f, -0.32f), new Vector2(0f, 0.32f) })
                    {
                        float d = (p - c).magnitude;
                        Layer(d - 0.4f, new Color(0.42f, 0.27f, 0.14f));
                        Layer(d - 0.3f, Mathf.Repeat(d * 9f, 1f) < 0.25f ? new Color(0.72f, 0.55f, 0.33f) : new Color(0.86f, 0.70f, 0.45f));
                    }
                    break;
                case Icon.Ore:
                {
                    float d = Poly(p, new[] { new Vector2(0f, 0.9f), new Vector2(0.72f, 0.3f), new Vector2(0.5f, -0.72f), new Vector2(-0.5f, -0.72f), new Vector2(-0.72f, 0.3f) });
                    bool left = p.x < -0.15f * (p.y + 0.4f), top = p.y > 0.3f - Mathf.Abs(p.x) * 0.2f;
                    Layer(d, top ? new Color(0.93f, 0.85f, 0.72f) : left ? new Color(0.75f, 0.63f, 0.48f) : new Color(0.55f, 0.44f, 0.33f));
                    break;
                }
                case Icon.Worker:
                    Layer((p - new Vector2(0f, 0.42f)).magnitude - 0.3f, new Color(0.86f, 0.72f, 0.56f));
                    Layer(Mathf.Max((p - new Vector2(0f, -0.62f)).magnitude - 0.62f, -p.y - 0.85f), new Color(0.45f, 0.58f, 0.32f));
                    break;
                case Icon.Army:
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Vector2 a = new Vector2(-0.68f * s, -0.68f), b = new Vector2(0.62f * s, 0.62f);
                        Layer(Segment(p, a, b) - 0.09f, new Color(0.82f, 0.84f, 0.88f));
                        Vector2 g = Vector2.Lerp(a, b, 0.18f), dir = (b - a).normalized, perp = new Vector2(-dir.y, dir.x) * 0.26f;
                        Layer(Segment(p, g - perp, g + perp) - 0.07f, new Color(0.84f, 0.66f, 0.30f));
                    }
                    break;
                case Icon.Heart:
                {
                    Vector2 q = new Vector2(Mathf.Abs(p.x), p.y + 0.1f);
                    float d = Mathf.Min((q - new Vector2(0.38f, 0.32f)).magnitude - 0.42f, Poly(new Vector2(q.x, q.y), new[] { new Vector2(0f, -0.85f), new Vector2(0.78f, 0.15f), new Vector2(0f, 0.2f) }));
                    Layer(d, q.y > 0.35f && q.x > 0.25f && q.x < 0.5f ? new Color(1f, 0.55f, 0.5f) : new Color(0.82f, 0.18f, 0.16f));
                    break;
                }
                case Icon.Hammer:
                    Layer(Segment(p, new Vector2(-0.6f, -0.7f), new Vector2(0.25f, 0.35f)) - 0.1f, new Color(0.55f, 0.36f, 0.2f));
                    Layer(Box(Rotate(p - new Vector2(0.28f, 0.42f), -0.89f), new Vector2(0.45f, 0.2f)), new Color(0.72f, 0.74f, 0.78f));
                    break;
                case Icon.House:
                    Layer(Box(p - new Vector2(0f, -0.35f), new Vector2(0.6f, 0.45f)), new Color(0.88f, 0.80f, 0.62f));
                    Layer(Poly(p, new[] { new Vector2(-0.85f, 0.05f), new Vector2(0f, 0.85f), new Vector2(0.85f, 0.05f) }), new Color(0.80f, 0.62f, 0.30f));
                    Layer(Box(p - new Vector2(0f, -0.52f), new Vector2(0.17f, 0.28f)), new Color(0.38f, 0.24f, 0.13f));
                    break;
                case Icon.Fire:
                {
                    // A flame: a teardrop pointing up, a brighter core
                    Vector2 q = new Vector2(p.x * (1.25f + p.y * 0.35f), p.y + 0.25f);
                    float drop = Mathf.Min((q - new Vector2(0f, -0.2f)).magnitude - 0.55f, Poly(q, new[] { new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 0.95f) }));
                    Layer(drop, new Color(0.92f, 0.36f, 0.12f));
                    Vector2 k = new Vector2(q.x * 1.6f, (q.y + 0.25f) * 1.4f);
                    Layer(Mathf.Min((k - new Vector2(0f, -0.2f)).magnitude - 0.45f, Poly(k, new[] { new Vector2(-0.42f, 0f), new Vector2(0.42f, 0f), new Vector2(0f, 0.8f) })), new Color(1f, 0.78f, 0.3f));
                    break;
                }
                case Icon.Bolt:
                    Layer(Mathf.Min(Poly(p, new[] { new Vector2(0.15f, 0.92f), new Vector2(0.55f, 0.92f), new Vector2(0.05f, 0.05f), new Vector2(-0.3f, 0.05f) }),
                                    Poly(p, new[] { new Vector2(-0.3f, 0.12f), new Vector2(0.32f, 0.12f), new Vector2(-0.35f, -0.92f) })), new Color(1f, 0.88f, 0.4f));
                    break;
                case Icon.Clock:
                {
                    float r = p.magnitude;
                    Layer(r - 0.82f, new Color(0.9f, 0.84f, 0.7f));
                    Layer(Mathf.Min(Segment(p, Vector2.zero, new Vector2(0f, 0.55f)) - 0.07f, Segment(p, Vector2.zero, new Vector2(0.4f, -0.12f)) - 0.07f), new Color(0.3f, 0.22f, 0.14f));
                    break;
                }
                case Icon.Bell:
                    Layer(Mathf.Min(Poly(p, new[] { new Vector2(-0.62f, -0.55f), new Vector2(0.62f, -0.55f), new Vector2(0.4f, 0.4f), new Vector2(-0.4f, 0.4f) }),
                                    (p - new Vector2(0f, 0.42f)).magnitude - 0.4f), new Color(0.86f, 0.66f, 0.3f));
                    Layer((p - new Vector2(0f, -0.72f)).magnitude - 0.16f, new Color(0.62f, 0.46f, 0.2f));
                    break;
                case Icon.Wall:
                {
                    // Stone wall with three merlons and an arched gate; courses of stone as darker lines
                    float wall = Box(p - new Vector2(0f, -0.28f), new Vector2(0.86f, 0.5f));
                    foreach (float x in new[] { -0.64f, 0f, 0.64f }) wall = Mathf.Min(wall, Box(p - new Vector2(x, 0.36f), new Vector2(0.2f, 0.18f)));
                    bool course = Mathf.Repeat((p.y + 0.78f) * 4f, 1f) < 0.12f || Mathf.Repeat(p.x * 2.5f + (Mathf.Repeat((p.y + 0.78f) * 2f, 1f) < 0.5f ? 0.5f : 0f), 1f) < 0.07f;
                    Layer(wall, course ? new Color(0.52f, 0.50f, 0.46f) : new Color(0.76f, 0.73f, 0.66f));
                    Layer(Mathf.Min(Box(p - new Vector2(0f, -0.58f), new Vector2(0.22f, 0.2f)), (p - new Vector2(0f, -0.38f)).magnitude - 0.22f), new Color(0.30f, 0.20f, 0.11f));
                    break;
                }
            }
            return result;
        }

        private static Color Over(Color under, Color over) => new Color(
            Mathf.Lerp(under.r, over.r, over.a), Mathf.Lerp(under.g, over.g, over.a), Mathf.Lerp(under.b, over.b, over.a), Mathf.Max(under.a, over.a));
        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 pa = p - a, ba = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * t).magnitude;
        }
        private static float Box(Vector2 p, Vector2 h)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x) - h.x, Mathf.Abs(p.y) - h.y);
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f);
        }
        private static Vector2 Rotate(Vector2 p, float a) => new Vector2(p.x * Mathf.Cos(a) - p.y * Mathf.Sin(a), p.x * Mathf.Sin(a) + p.y * Mathf.Cos(a));
        /// <summary>Signed distance to a convex polygon (counter-clockwise or clockwise).</summary>
        private static float Poly(Vector2 p, Vector2[] v)
        {
            float d = float.MinValue;
            Vector2 c = Vector2.zero;
            foreach (Vector2 q in v) c += q;
            c /= v.Length;
            for (int i = 0; i < v.Length; i++)
            {
                Vector2 a = v[i], b = v[(i + 1) % v.Length], e = b - a;
                Vector2 n = new Vector2(e.y, -e.x).normalized;
                if (Vector2.Dot(n, c - a) > 0f) n = -n;
                d = Mathf.Max(d, Vector2.Dot(p - a, n));
            }
            return d;
        }

        // ------------------------------------------------------------------ builders

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<UiThemed>();
            Managers.DisplaySettings.Apply(scaler);
            return canvas;
        }

        /// <summary>Child RectTransform anchored at (ax, ay) of the parent with the pivot there too, at offset, of size.</summary>
        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Child RectTransform spanning anchorMin..anchorMax of the parent; offsets only when given.</summary>
        public static RectTransform Child(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            if (offsetMin.HasValue) rt.offsetMin = offsetMin.Value;
            if (offsetMax.HasValue) rt.offsetMax = offsetMax.Value;
            return rt;
        }

        /// <summary>A plain Text on go (the older windows' style; UiRestyler themes it later).</summary>
        public static Text PlainText(GameObject go, Font font, int size, TextAnchor align, Color color, string text = null, FontStyle style = FontStyle.Normal, bool raycast = true)
        {
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = color;
            if (text != null) t.text = text;
            t.raycastTarget = raycast;
            return t;
        }

        public static Image PanelImage(RectTransform rt, Sprite sprite = null, Color? tint = null)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite ?? PanelSprite;
            img.type = Image.Type.Sliced;
            img.color = tint ?? Color.white;
            return img;
        }

        public static Text Label(Transform parent, string name, string text, int size, TextAnchor align, Color? color = null, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font = Font; t.fontSize = size; t.alignment = align; t.color = color ?? Ink; t.fontStyle = style; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.75f);
            sh.effectDistance = new Vector2(1.2f, -1.2f);
            return t;
        }

        public static Image IconImage(Transform parent, Icon icon, Vector2 anchor, Vector2 offset, float size)
        {
            RectTransform rt = Rect(parent, "Icon_" + icon, anchor, offset, new Vector2(size, size));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = IconSprite(icon);
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Themed button (bronze-rimmed) with a label; returns the Button.</summary>
        public static Button TextButton(Transform parent, string name, string text, Vector2 anchor, Vector2 offset, Vector2 size, UnityEngine.Events.UnityAction onClick, int fontSize = 18)
        {
            RectTransform rt = Rect(parent, name, anchor, offset, size);
            PanelImage(rt, ButtonSprite);
            Button b = AddButton(rt.gameObject);
            ColorBlock cb = b.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.2f, 1.12f, 0.95f); cb.pressedColor = new Color(0.78f, 0.72f, 0.64f);
            cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.8f); cb.colorMultiplier = 1.25f;
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);
            Label(rt, "Text", text, fontSize, TextAnchor.MiddleCenter, Ink, FontStyle.Bold);
            return b;
        }

        // ------------------------------------------------------------------ items

        /// <summary>Rarity-style colour of an item level (I grey, II green, III blue, IV violet, V gold).</summary>
        public static Color LevelColor(int level)
        {
            switch (level)
            {
                case 0: return new Color(0.62f, 0.60f, 0.56f);
                case 1: return new Color(0.80f, 0.76f, 0.68f);
                case 2: return new Color(0.50f, 0.82f, 0.42f);
                case 3: return new Color(0.42f, 0.66f, 1.00f);
                case 4: return new Color(0.76f, 0.50f, 1.00f);
                default: return new Color(1.00f, 0.74f, 0.28f);
            }
        }

        private static readonly Dictionary<int, Sprite> ItemIcons = new Dictionary<int, Sprite>();

        /// <summary>Icon of an item; the metal / leather / wood follows its level like the model does.</summary>
        public static Sprite ItemSprite(ItemType type, int level)
        {
            int key = (int)type * 8 + Mathf.Clamp(level, 0, 5);
            if (ItemIcons.TryGetValue(key, out Sprite s) && s != null) return s;
            s = RenderIcon("UI_Item_" + type + level, p => ShadeItem(type, Mathf.Clamp(level, 0, 5), p));
            ItemIcons[key] = s;
            return s;
        }

        private static Sprite RenderIcon(string name, Func<Vector2, Color> shade)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color acc = Color.clear;
                for (int sy = 0; sy < 3; sy++)
                for (int sx = 0; sx < 3; sx++)
                {
                    Vector2 p = new Vector2((x + (sx + 0.5f) / 3f) / N * 2f - 1f, (y + (sy + 0.5f) / 3f) / N * 2f - 1f);
                    Color c = shade(p);
                    acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                }
                acc /= 9f;
                px[x + y * N] = acc.a > 0f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Color ShadeItem(ItemType type, int level, Vector2 p)
        {
            Color result = Color.clear;
            void Layer(float d, Color fill)
            {
                if (d < 0.07f) result = Over(result, new Color(0.08f, 0.06f, 0.04f, 1f));
                if (d < 0f) result = Over(result, fill);
            }
            // Finishes by level (same steps as the models)
            Color[] metals = { new Color(0.80f, 0.56f, 0.30f), new Color(0.60f, 0.62f, 0.64f), new Color(0.84f, 0.87f, 0.92f), new Color(0.34f, 0.42f, 0.62f), new Color(0.93f, 0.93f, 0.96f) };
            Color[] armour = { new Color(0.58f, 0.38f, 0.21f), new Color(0.60f, 0.62f, 0.64f), new Color(0.84f, 0.87f, 0.92f), new Color(0.34f, 0.42f, 0.62f), new Color(0.93f, 0.93f, 0.96f) };
            Color gold = new Color(0.98f, 0.76f, 0.30f);
            Color metal = level == 0 ? new Color(0.56f, 0.56f, 0.58f) : metals[level - 1], arm = armour[Mathf.Max(1, level) - 1];
            Color trim = level >= 5 ? gold : level == 4 ? new Color(0.85f, 0.87f, 0.9f) : level <= 1 ? new Color(0.36f, 0.22f, 0.12f) : new Color(0.42f, 0.43f, 0.46f);
            Color wood = level <= 1 ? new Color(0.70f, 0.50f, 0.28f) : level == 2 ? new Color(0.58f, 0.38f, 0.21f) : new Color(0.45f, 0.28f, 0.15f);
            Color leather = new Color(0.36f, 0.22f, 0.12f);
            float shine = Mathf.Clamp01(0.5f + 0.5f * (p.x - p.y) * 0.6f); // light from the top left
            Color Lit(Color c) => Color.Lerp(c * 0.82f, Color.Lerp(c, Color.white, 0.25f), shine);

            switch (type)
            {
                case ItemType.Sword:
                {
                    Vector2 a = new Vector2(-0.42f, -0.42f), b = new Vector2(0.72f, 0.72f);
                    Layer(Segment(p, new Vector2(-0.8f, -0.8f), new Vector2(-0.48f, -0.48f)) - 0.08f, leather);
                    Layer((p - new Vector2(-0.82f, -0.82f)).magnitude - 0.13f, trim);
                    Layer(Segment(p, a, b) - 0.1f * Mathf.Clamp01(1.15f - Vector2.Dot(p - a, (b - a).normalized) / 1.6f), Lit(metal));
                    Layer(Segment(p, a + new Vector2(-0.26f, 0.26f), a + new Vector2(0.26f, -0.26f)) - 0.08f, trim);
                    break;
                }
                case ItemType.Bow:
                {
                    Vector2 c = new Vector2(-0.9f, 0f);
                    float ang = Mathf.Atan2(p.y - c.y, p.x - c.x);
                    float arc = Mathf.Abs((p - c).magnitude - 1.25f) - 0.09f;
                    if (Mathf.Abs(ang) > 0.82f) arc = 9f;
                    Vector2 top = c + new Vector2(Mathf.Cos(0.82f), Mathf.Sin(0.82f)) * 1.25f, bot = new Vector2(top.x, -top.y);
                    Layer(Segment(p, top, bot) - 0.025f, new Color(0.9f, 0.86f, 0.72f));
                    Layer(arc, Lit(wood));
                    Layer(Box(p - new Vector2(0.35f, 0f), new Vector2(0.09f, 0.16f)), leather);
                    if (level >= 3) { Layer((p - top).magnitude - 0.1f, trim); Layer((p - bot).magnitude - 0.1f, trim); }
                    break;
                }
                case ItemType.Staff:
                {
                    Vector2 orb = new Vector2(0.5f, 0.6f);
                    Layer(Segment(p, new Vector2(-0.78f, -0.86f), new Vector2(0.38f, 0.44f)) - 0.075f, Lit(wood));
                    Layer(Segment(p, new Vector2(0.28f, 0.33f), new Vector2(0.4f, 0.46f)) - 0.11f, Lit(level >= 5 ? gold : metal));
                    Color gem = Color.Lerp(new Color(0.45f, 0.72f, 1f), new Color(0.78f, 0.52f, 1f), Mathf.Clamp01(level / 5f));
                    Layer((p - orb).magnitude - 0.2f, Color.Lerp(gem, Color.white, Mathf.Clamp01(0.45f - (p - orb - new Vector2(-0.07f, 0.07f)).magnitude * 3f)));
                    break;
                }
                case ItemType.Axe:
                    Layer(Segment(p, new Vector2(-0.6f, -0.8f), new Vector2(0.3f, 0.75f)) - 0.08f, Lit(wood));
                    Layer(Poly(Rotate(p - new Vector2(0.28f, 0.45f), 0.52f), new[] { new Vector2(-0.05f, 0.18f), new Vector2(0.55f, 0.38f), new Vector2(0.62f, -0.36f), new Vector2(-0.05f, -0.16f) }), Lit(metal));
                    break;
                case ItemType.Pickaxe:
                    Layer(Segment(p, new Vector2(-0.55f, -0.85f), new Vector2(0.25f, 0.62f)) - 0.08f, Lit(wood));
                    for (int i = 0; i < 16; i++)
                    {
                        float s = -1f + 2f * i / 15f;
                        Vector2 q = new Vector2(0.25f, 0.62f) + Rotate(new Vector2(s * 0.75f, -s * s * 0.22f), -0.5f);
                        Layer((p - q).magnitude - Mathf.Lerp(0.13f, 0.04f, Mathf.Abs(s)), Lit(metal));
                    }
                    break;
                case ItemType.Shield:
                {
                    float d = p.magnitude;
                    Layer(d - 0.86f, level <= 2 ? trim * (level == 1 ? 1.4f : 1f) : (level >= 5 ? gold : trim));
                    Color face = level <= 2 ? wood : Lit(arm);
                    if (level <= 2 && Mathf.Abs(Mathf.Repeat(p.x + 0.12f, 0.24f) - 0.12f) < 0.015f) face *= 0.65f;
                    Layer(d - 0.74f, face);
                    if (level >= 3) { Layer(Mathf.Min(Mathf.Abs(p.x + p.y), Mathf.Abs(p.x - p.y)) * 0.7071f - 0.05f + Mathf.Max(0f, d - 0.72f) * 9f, level >= 5 ? gold : trim); }
                    Layer(d - 0.22f, level >= 5 ? gold : Lit(metal));
                    break;
                }
                case ItemType.Helmet:
                {
                    float dome = Mathf.Max((p - new Vector2(0f, -0.15f)).magnitude - 0.72f, -(p.y + 0.3f));
                    Layer(dome, Lit(arm));
                    Layer(Box(p - new Vector2(0f, -0.28f), new Vector2(0.76f, 0.1f)), trim);
                    if (level >= 2) Layer(Box(p - new Vector2(0f, -0.52f), new Vector2(0.07f, 0.24f)), trim);
                    if (level >= 3) Layer(Box(p - new Vector2(0f, 0.32f), new Vector2(0.05f, 0.25f)), level >= 5 ? gold : trim);
                    break;
                }
                case ItemType.Armor:
                {
                    float body = Poly(p, new[] { new Vector2(-0.82f, 0.55f), new Vector2(-0.3f, 0.75f), new Vector2(0.3f, 0.75f), new Vector2(0.82f, 0.55f), new Vector2(0.6f, -0.1f), new Vector2(0.5f, -0.82f), new Vector2(-0.5f, -0.82f), new Vector2(-0.6f, -0.1f) });
                    float neck = (p - new Vector2(0f, 0.85f)).magnitude - 0.3f;
                    Color main = level == 2 ? (Mathf.Repeat((p.x * 9f + Mathf.Floor(p.y * 9f) * 0.5f), 1f) < 0.5f ? arm : arm * 0.75f) : Lit(arm);
                    Layer(Mathf.Max(body, -neck), main);
                    Layer(Mathf.Max(Box(p - new Vector2(0f, -0.55f), new Vector2(0.56f, 0.09f)), body), leather);
                    if (level >= 3) Layer(Mathf.Max(Box(p - new Vector2(0f, 0.12f), new Vector2(0.03f, 0.38f)), body), Color.Lerp(arm, Color.white, 0.35f));
                    if (level >= 4) Layer(Mathf.Max(Mathf.Abs(body + 0.04f) - 0.03f, -neck), level >= 5 ? gold : trim);
                    break;
                }
                case ItemType.Gloves:
                {
                    Color g = level == 2 ? arm * 0.92f : Lit(arm);
                    Layer(Box(p - new Vector2(0.05f, 0.18f), new Vector2(0.42f, 0.5f)) - 0.12f, g);
                    Layer(Segment(p, new Vector2(-0.42f, 0f), new Vector2(-0.62f, 0.32f)) - 0.13f, g);
                    Layer(Box(p - new Vector2(0.05f, -0.62f), new Vector2(0.48f, 0.18f)), level >= 5 ? gold : trim);
                    for (int i = 0; i < 3; i++) Layer(Box(p - new Vector2(-0.15f + i * 0.22f, 0.62f), new Vector2(0.008f, 0.12f)) - 0.004f, g * 0.6f);
                    break;
                }
                default: // boots
                {
                    Color b = level <= 2 ? leather * 1.3f : Lit(arm);
                    Layer(Box(p - new Vector2(-0.12f, 0.2f), new Vector2(0.3f, 0.58f)) - 0.05f, b);
                    Layer(Box(p - new Vector2(0.18f, -0.52f), new Vector2(0.6f, 0.2f)) - 0.08f, b);
                    Layer(Box(p - new Vector2(0.15f, -0.78f), new Vector2(0.66f, 0.06f)), new Color(0.16f, 0.1f, 0.06f));
                    Layer(Box(p - new Vector2(-0.12f, 0.72f), new Vector2(0.36f, 0.08f)), level >= 5 ? gold : trim);
                    if (level >= 2) Layer(Box(p - new Vector2(-0.12f, 0.18f), new Vector2(0.24f, 0.4f)) + 0.01f, level == 2 ? metal : Color.Lerp(arm, Color.white, 0.15f));
                    break;
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ restyling of older UI

        /// <summary>Removes emoji and pictographs the UI font cannot draw (they show as boxes), keeping text punctuation.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            bool dirty = false;
            foreach (char ch in s) if (char.IsSurrogate(ch) || (ch >= 0x2190 && ch <= 0x2BFF) || ch == 0xFE0F || ch == 0x200D) { dirty = true; break; }
            if (!dirty) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
                if (!(char.IsSurrogate(ch) || (ch >= 0x2190 && ch <= 0x2BFF) || ch == 0xFE0F || ch == 0x200D)) sb.Append(ch);
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Older windows stretch anchors on new RectTransforms but never reset the offsets, which keep the default
        /// 100 x 100 size (+-50 per side) and make elements spill over their neighbours. Zero such leftovers per axis.
        /// </summary>
        public static void FixDefaultOffsets(RectTransform rt)
        {
            Vector2 min = rt.offsetMin, max = rt.offsetMax;
            bool changed = false;
            for (int a = 0; a < 2; a++)
            {
                if (Mathf.Approximately(rt.anchorMin[a], rt.anchorMax[a])) continue;
                if (Mathf.Approximately(max[a], 50f)) { max[a] = 0f; changed = true; }
                if (Mathf.Approximately(min[a], -50f)) { min[a] = 0f; changed = true; }
            }
            if (changed) { rt.offsetMin = min; rt.offsetMax = max; }
        }

        /// <summary>A button that answers the cursor (ButtonFx: grows, sinks, ticks): every button of the game is made through this.</summary>
        public static Button AddButton(GameObject go)
        {
            Button b = go.AddComponent<Button>();
            go.AddComponent<ButtonFx>();
            return b;
        }

        /// <summary>
        /// The theme look for a part built with default looks (an older window, rows it adds later): fonts, panels,
        /// buttons, colours, once; its buttons answer the cursor too.
        /// </summary>
        public static void RestyleTree(Transform root)
        {
            foreach (Graphic g in root.GetComponentsInChildren<Graphic>(true))
            {
                FixDefaultOffsets(g.rectTransform);
                if (g.transform.parent is RectTransform parent) FixDefaultOffsets(parent);
                Restyle(g);
            }
            foreach (Button b in root.GetComponentsInChildren<Button>(true)) ButtonFx.On(b);
        }

        /// <summary>Gives one element of an older window the theme look (fonts, panels, buttons, accent colours).</summary>
        public static void Restyle(Graphic g)
        {
            if (g is Text t)
            {
                t.font = Font;
                Color.RGBToHSV(t.color, out float h, out float s, out float v);
                if (s > 0.35f && h > 0.45f && h < 0.6f) t.color = Gold;          // cyan accents -> gold
                else if (s < 0.25f && v > 0.7f) t.color = new Color(Ink.r, Ink.g, Ink.b, t.color.a);
                if (t.GetComponent<Shadow>() == null)
                {
                    var sh = t.gameObject.AddComponent<Shadow>();
                    sh.effectColor = new Color(0f, 0f, 0f, 0.7f);
                    sh.effectDistance = new Vector2(1f, -1f);
                }
                t.text = Sanitize(t.text);
                return;
            }
            if (!(g is Image img) || img.sprite != null && img.sprite.name != "UISprite" && img.sprite.name != "Background") return;
            Color.RGBToHSV(img.color, out float hh, out float ss, out float vv);
            bool isButton = img.GetComponent<Button>() != null;
            if (isButton)
            {
                bool coloured = ss > 0.45f && vv > 0.3f && (hh < 0.12f || hh > 0.9f || (hh > 0.2f && hh < 0.45f)); // red close / green apply keep their hue
                img.sprite = ButtonSprite; img.type = Image.Type.Sliced;
                img.color = coloured ? Color.Lerp(Color.white, img.color, 0.55f) : Color.white;
                var b = img.GetComponent<Button>();
                ColorBlock cb = b.colors;
                cb.normalColor = Color.white; cb.highlightedColor = new Color(1.18f, 1.12f, 1f); cb.pressedColor = new Color(0.8f, 0.75f, 0.68f); cb.selectedColor = Color.white;
                cb.colorMultiplier = 1.25f;
                b.colors = cb;
            }
            else if (vv < 0.3f && img.rectTransform.rect.width > 60f && img.rectTransform.rect.height > 30f)
            {
                img.sprite = PanelSprite; img.type = Image.Type.Sliced;
                img.color = new Color(1f, 1f, 1f, Mathf.Max(0.85f, img.color.a));
            }
            else
            {
                img.sprite = SlotSprite; img.type = Image.Type.Sliced;
                if (vv < 0.3f) img.color = Color.white;
            }
        }
    }

    /// <summary>Marks canvases built with the theme already (the restyler leaves them alone).</summary>
    public sealed class UiThemed : MonoBehaviour { }
}
