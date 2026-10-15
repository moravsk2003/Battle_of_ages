using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

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

        public enum Icon { Wood, Ore, Worker, Army, Heart, Hammer, House }

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

        public static Sprite PanelSprite => panel != null ? panel : panel = Frame(64, 12f, 2.2f, new Color(0.11f, 0.09f, 0.07f), new Color(0.055f, 0.045f, 0.035f), Rim, 0.35f);
        public static Sprite ButtonSprite => button != null ? button : button = Frame(48, 8f, 1.8f, new Color(0.30f, 0.23f, 0.15f), new Color(0.17f, 0.13f, 0.09f), Gold * 0.85f, 0.5f);
        public static Sprite SlotSprite => slot != null ? slot : slot = Frame(48, 7f, 1.4f, new Color(0.05f, 0.04f, 0.03f), new Color(0.09f, 0.075f, 0.06f), Rim * 0.8f, 0.2f);
        /// <summary>Plain white rounded rectangle (bars, tinted chips).</summary>
        public static Sprite BarSprite => bar != null ? bar : bar = Frame(32, 6f, 0f, Color.white, Color.white, Color.white, 0f);
        public static Sprite RoundSprite => round != null ? round : round = Frame(64, 31f, 0f, Color.white, Color.white, Color.white, 0f);

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
            Button b = rt.gameObject.AddComponent<Button>();
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
                case 1: return new Color(0.80f, 0.76f, 0.68f);
                case 2: return new Color(0.50f, 0.82f, 0.42f);
                case 3: return new Color(0.42f, 0.66f, 1.00f);
                case 4: return new Color(0.76f, 0.50f, 1.00f);
                default: return new Color(1.00f, 0.74f, 0.28f);
            }
        }

        private static readonly Dictionary<int, Sprite> ItemIcons = new Dictionary<int, Sprite>();

        /// <summary>Icon of an item; the metal / leather / wood follows its level like the model does.</summary>
        public static Sprite ItemSprite(BattleOfAges.Crafting.ItemType type, int level)
        {
            int key = (int)type * 8 + Mathf.Clamp(level, 1, 5);
            if (ItemIcons.TryGetValue(key, out Sprite s) && s != null) return s;
            s = RenderIcon("UI_Item_" + type + level, p => ShadeItem(type, Mathf.Clamp(level, 1, 5), p));
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

        private static Color ShadeItem(BattleOfAges.Crafting.ItemType type, int level, Vector2 p)
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
            Color metal = metals[level - 1], arm = armour[level - 1];
            Color trim = level >= 5 ? gold : level == 4 ? new Color(0.85f, 0.87f, 0.9f) : level == 1 ? new Color(0.36f, 0.22f, 0.12f) : new Color(0.42f, 0.43f, 0.46f);
            Color wood = level <= 1 ? new Color(0.70f, 0.50f, 0.28f) : level == 2 ? new Color(0.58f, 0.38f, 0.21f) : new Color(0.45f, 0.28f, 0.15f);
            Color leather = new Color(0.36f, 0.22f, 0.12f);
            float shine = Mathf.Clamp01(0.5f + 0.5f * (p.x - p.y) * 0.6f); // light from the top left
            Color Lit(Color c) => Color.Lerp(c * 0.82f, Color.Lerp(c, Color.white, 0.25f), shine);

            switch (type)
            {
                case BattleOfAges.Crafting.ItemType.Sword:
                {
                    Vector2 a = new Vector2(-0.42f, -0.42f), b = new Vector2(0.72f, 0.72f);
                    Layer(Segment(p, new Vector2(-0.8f, -0.8f), new Vector2(-0.48f, -0.48f)) - 0.08f, leather);
                    Layer((p - new Vector2(-0.82f, -0.82f)).magnitude - 0.13f, trim);
                    Layer(Segment(p, a, b) - 0.1f * Mathf.Clamp01(1.15f - Vector2.Dot(p - a, (b - a).normalized) / 1.6f), Lit(metal));
                    Layer(Segment(p, a + new Vector2(-0.26f, 0.26f), a + new Vector2(0.26f, -0.26f)) - 0.08f, trim);
                    break;
                }
                case BattleOfAges.Crafting.ItemType.Bow:
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
                case BattleOfAges.Crafting.ItemType.Axe:
                    Layer(Segment(p, new Vector2(-0.6f, -0.8f), new Vector2(0.3f, 0.75f)) - 0.08f, Lit(wood));
                    Layer(Poly(Rotate(p - new Vector2(0.28f, 0.45f), 0.52f), new[] { new Vector2(-0.05f, 0.18f), new Vector2(0.55f, 0.38f), new Vector2(0.62f, -0.36f), new Vector2(-0.05f, -0.16f) }), Lit(metal));
                    break;
                case BattleOfAges.Crafting.ItemType.Pickaxe:
                    Layer(Segment(p, new Vector2(-0.55f, -0.85f), new Vector2(0.25f, 0.62f)) - 0.08f, Lit(wood));
                    for (int i = 0; i < 16; i++)
                    {
                        float s = -1f + 2f * i / 15f;
                        Vector2 q = new Vector2(0.25f, 0.62f) + Rotate(new Vector2(s * 0.75f, -s * s * 0.22f), -0.5f);
                        Layer((p - q).magnitude - Mathf.Lerp(0.13f, 0.04f, Mathf.Abs(s)), Lit(metal));
                    }
                    break;
                case BattleOfAges.Crafting.ItemType.Shield:
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
                case BattleOfAges.Crafting.ItemType.Helmet:
                {
                    float dome = Mathf.Max((p - new Vector2(0f, -0.15f)).magnitude - 0.72f, -(p.y + 0.3f));
                    Layer(dome, Lit(arm));
                    Layer(Box(p - new Vector2(0f, -0.28f), new Vector2(0.76f, 0.1f)), trim);
                    if (level >= 2) Layer(Box(p - new Vector2(0f, -0.52f), new Vector2(0.07f, 0.24f)), trim);
                    if (level >= 3) Layer(Box(p - new Vector2(0f, 0.32f), new Vector2(0.05f, 0.25f)), level >= 5 ? gold : trim);
                    break;
                }
                case BattleOfAges.Crafting.ItemType.Armor:
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
                case BattleOfAges.Crafting.ItemType.Gloves:
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
