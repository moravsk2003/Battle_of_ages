using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.AI;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The minimap, in both views (on the map at the bottom left, in first person smaller at the top right): the ground
    /// from above (rendered again when the view moves), dots for own units, raiders and the player, the view direction in
    /// first person, pulsing rings where we were attacked. On the map a press moves the camera, a right press sends the
    /// selected there, and the alert key jumps to the last attack.
    /// </summary>
    public sealed class HudMinimap : MonoBehaviour
    {
        public const float HalfSize = 90f, Px = 280f, FramePx = Px + 24f;
        private const float Scale = Px / (2f * HalfSize);

        private RectTransform frame, dots, heading;
        private Camera cam;
        private RenderTexture texture;
        private Vector3 centre = new Vector3(1e9f, 0f, 0f);
        private float redrawTimer, dotTimer, hostileTimer;
        private int placedFor = -1;
        private HostileMobAI[] hostiles;
        private readonly List<Image> dotImages = new List<Image>();
        private readonly List<(Vector3 pos, float born, Image img)> pings = new List<(Vector3, float, Image)>();

        public static HudMinimap Create(RectTransform root)
        {
            RectTransform frame = UiTheme.Rect(root, "Minimap", new Vector2(0f, 0f), new Vector2(18f, 18f), new Vector2(FramePx, FramePx));
            HudMinimap m = frame.gameObject.AddComponent<HudMinimap>();
            m.Build(frame);
            return m;
        }

        private void Build(RectTransform f)
        {
            frame = f;
            UiTheme.PanelImage(frame);
            RectTransform view = UiTheme.Rect(frame, "View", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Px, Px));
            view.gameObject.AddComponent<RectMask2D>();
            RawImage image = view.gameObject.AddComponent<RawImage>();
            image.color = new Color(0.9f, 0.9f, 0.9f);
            image.raycastTarget = true; // clicks: MinimapInput
            view.gameObject.AddComponent<MinimapInput>().Pressed = OnPressed;
            Tooltip.Set(view.gameObject, () => GameContext.MapOpen
                ? TipText.Title("Мапа", UiTheme.Gold) + "\nЛКМ — камера туди (можна вести), ПКМ — обрані йдуть туди." + "\n" + TipText.Dim("Сині — свої, червоні — вороги, жовті — обрані, кільця — напад.")
                : null);
            dots = UiTheme.Rect(view, "Dots", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Px, Px));
            heading = UiTheme.Rect(dots, "Heading", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 16f));
            heading.pivot = new Vector2(0.5f, 0f);
            var headingImg = heading.gameObject.AddComponent<Image>();
            headingImg.sprite = UiTheme.BarSprite; headingImg.color = Color.white; headingImg.raycastTarget = false;
            RectTransform title = UiTheme.Rect(frame, "Title", new Vector2(0.5f, 1f), new Vector2(0f, 16f), new Vector2(120f, 30f));
            UiTheme.PanelImage(title, UiTheme.ButtonSprite);
            UiTheme.Label(title, "Text", "Мапа", 17, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);

            texture = new RenderTexture(256, 256, 16) { name = "MinimapRT" };
            var camGo = new GameObject("MinimapCamera");
            camGo.transform.SetParent(transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = HalfSize;
            cam.nearClipPlane = 1f; cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.36f, 0.24f);
            int vm = LayerMask.NameToLayer("Viewmodel"), ui = LayerMask.NameToLayer("UI");
            cam.cullingMask = ~((vm >= 0 ? 1 << vm : 0) | (ui >= 0 ? 1 << ui : 0));
            cam.targetTexture = texture;
            cam.enabled = false;
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            data.renderShadows = false; // a map from above needs no shadows (a whole extra scene pass)
            data.renderPostProcessing = false;
            image.texture = texture;
        }

        private void OnEnable() => AttackAlerts.Raised += OnAttackAlert;
        private void OnDisable() => AttackAlerts.Raised -= OnAttackAlert;
        private void OnDestroy() { if (texture != null) texture.Release(); }

        private void Update()
        {
            bool map = GameContext.MapOpen;
            Place(map);
            Draw(map);
            UpdatePings();
            if (map && AttackAlerts.HasLast && !GameWindows.BlockGameplay && InputSystem.KeybindsManager.GetKeyDown(InputSystem.KeyAction.AlertJump))
                GameContext.MapCamera?.LookAtGround(AttackAlerts.Last);
        }

        /// <summary>On the map at the bottom left, in first person smaller at the top right.</summary>
        private void Place(bool map)
        {
            if (placedFor == (map ? 1 : 0)) return;
            placedFor = map ? 1 : 0;
            Vector2 corner = map ? Vector2.zero : Vector2.one;
            frame.anchorMin = frame.anchorMax = frame.pivot = corner;
            frame.anchoredPosition = map ? new Vector2(18f, 18f) : new Vector2(-18f, -18f);
            frame.localScale = Vector3.one * (map ? 1f : 0.72f);
            centre = new Vector3(1e9f, 0f, 0f); // draw it again for the new view
        }

        private static float Ground(Vector3 p) => World.InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);

        private void Draw(bool map)
        {
            Camera main = ViewCamera.Current;
            if (main == null) return;
            // Centre: in first person the player; on the map where the view looks at the ground
            Vector3 c = main.transform.position;
            Vector3 f = main.transform.forward;
            if (map && f.y < -0.1f) c += f * ((c.y - Ground(c)) / -f.y);
            c.y = 0f;
            redrawTimer -= Time.unscaledDeltaTime;
            if ((c - centre).sqrMagnitude > 12f * 12f || redrawTimer <= 0f)
            {
                redrawTimer = 5f;
                centre = c;
                cam.transform.SetPositionAndRotation(new Vector3(c.x, main.transform.position.y + 250f, c.z), Quaternion.Euler(90f, 0f, 0f));
                bool fog = RenderSettings.fog;
                RenderSettings.fog = false;
                cam.Render();
                RenderSettings.fog = fog;
            }

            // Where the hero looks, every frame (first person: the camera; on the map: the hero, while in the frame)
            GameObject hero = GameContext.Player;
            Transform looks = map && hero != null ? hero.transform : main.transform;
            Vector3 me = looks.position - centre;
            heading.anchoredPosition = new Vector2(me.x, me.z) * Scale;
            heading.localEulerAngles = new Vector3(0f, 0f, -looks.eulerAngles.y);
            bool showHeading = !map || (hero != null && Mathf.Abs(me.x) < HalfSize && Mathf.Abs(me.z) < HalfSize);
            if (heading.gameObject.activeSelf != showHeading) heading.gameObject.SetActive(showHeading);

            dotTimer -= Time.unscaledDeltaTime;
            if (dotTimer > 0f) return;
            dotTimer = 0.15f;
            int used = 0;
            RtsUnitCommander commander = GameContext.Commander;
            var selected = commander != null ? commander.SelectedUnits : null;
            foreach (RtsUnit u in UnitRegistry.ActiveUnits)
            {
                if (u == null || !u.IsAlive || used >= 400) continue;
                Vector3 d = u.transform.position - centre;
                if (Mathf.Abs(d.x) > HalfSize || Mathf.Abs(d.z) > HalfSize) continue;
                bool sel = selected != null && Contains(selected, u);
                Dot(used++, new Vector2(d.x, d.z) * Scale, sel ? new Color(1f, 0.95f, 0.6f) : new Color(0.35f, 0.62f, 1f), sel ? 7f : 5f);
            }
            hostileTimer -= 0.15f;
            if (hostileTimer <= 0f || hostiles == null) { hostileTimer = 1f; hostiles = FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None); }
            foreach (HostileMobAI m in hostiles)
            {
                if (used >= 500) break;
                if (m == null || m.Health == null || !m.Health.IsAlive) continue; // a body lies a while: no longer an enemy on the map
                Vector3 d = m.transform.position - centre;
                if (Mathf.Abs(d.x) > HalfSize || Mathf.Abs(d.z) > HalfSize) continue;
                Dot(used++, new Vector2(d.x, d.z) * Scale, new Color(0.95f, 0.25f, 0.2f), 5f);
            }
            Dot(used++, new Vector2(me.x, me.z) * Scale, Color.white, 9f);
            for (int i = used; i < dotImages.Count; i++) if (dotImages[i].enabled) dotImages[i].enabled = false;
        }

        private static bool Contains(IReadOnlyList<RtsUnit> list, RtsUnit u)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == u) return true;
            return false;
        }

        private void Dot(int i, Vector2 pos, Color color, float size)
        {
            while (dotImages.Count <= i)
            {
                RectTransform r = UiTheme.Rect(dots, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5f, 5f));
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.RoundSprite; img.raycastTarget = false;
                dotImages.Add(img);
            }
            Image dot = dotImages[i];
            dot.enabled = true;
            dot.color = color;
            dot.rectTransform.anchoredPosition = pos;
            dot.rectTransform.sizeDelta = new Vector2(size, size);
        }

        // ------------------------------------------------------------------ pings and presses

        private void OnAttackAlert(string what, Vector3 where) => Ping(where, UiTheme.Bad, AttackAlerts.PingSeconds);

        /// <summary>A pulsing ring at a world point for this long; off the minimap it sits on the edge.</summary>
        private void Ping(Vector3 where, Color color, float seconds)
        {
            RectTransform r = UiTheme.Rect(dots, "Ping", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
            Image img = r.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.RingSprite; img.raycastTarget = false; img.color = color;
            pings.Add((where, Time.unscaledTime - (AttackAlerts.PingSeconds - seconds), img));
            if (pings.Count > 6) { Destroy(pings[0].img.gameObject); pings.RemoveAt(0); }
        }

        private void UpdatePings()
        {
            float edge = Px * 0.5f - 8f;
            for (int i = pings.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - pings[i].born;
                if (age > AttackAlerts.PingSeconds || pings[i].img == null) { if (pings[i].img != null) Destroy(pings[i].img.gameObject); pings.RemoveAt(i); continue; }
                Vector3 d = pings[i].pos - centre;
                Vector2 p = new Vector2(d.x, d.z) * Scale;
                float m = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                if (m > edge) p *= edge / m;
                float pulse = Mathf.Repeat(age, 1f);
                RectTransform rt = pings[i].img.rectTransform;
                rt.anchoredPosition = p;
                rt.sizeDelta = Vector2.one * Mathf.Lerp(10f, 34f, pulse);
                Color tint = pings[i].img.color;
                pings[i].img.color = new Color(tint.r, tint.g, tint.b, (0.35f + 0.65f * (1f - pulse)) * Mathf.Clamp01((AttackAlerts.PingSeconds - age) / 1.5f));
                rt.SetAsLastSibling();
            }
        }

        /// <summary>A press or drag on the minimap (map view only): left — the camera goes there, right — the selected walk there.</summary>
        private void OnPressed(Vector2 local, bool right, bool drag)
        {
            if (!GameContext.MapOpen || GameWindows.BlockGameplay) return;
            Vector3 world = centre + new Vector3(local.x / Scale, 0f, local.y / Scale);
            world.y = RtsUnit.GetTerrainHeight(world);
            if (right)
            {
                RtsUnitCommander commander = GameContext.Commander;
                if (drag || commander == null || commander.SelectedUnits.Count == 0) return;
                commander.OrderMoveTo(world);
                Ping(world, UiTheme.Good, 1.2f); // a short green ring
                return;
            }
            GameContext.MapCamera?.LookAtGround(world);
        }
    }
}
