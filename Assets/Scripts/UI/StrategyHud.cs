using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Always-on strategy HUD in the UiTheme look: a resource bar (wood, ore by tier, workers, army) and the mode hint
    /// at the top; in first person a crosshair and the player's health; on the map a minimap (terrain rendered from
    /// above when the view moves, live dots for units) and health bars over the selected units; toasts for gathered
    /// resources, new workers and finished buildings. Started by UiRestyler.
    /// </summary>
    public sealed class StrategyHud : MonoBehaviour
    {
        private Canvas canvas;
        private RectTransform root;
        private GameModeManager modes;
        private RtsUnitCommander commander;
        private HealthComponent playerHealth;

        private Text woodText, oreText, workerText, armyText, modeText, hpText;
        private RectTransform fpGroup, rtsGroup, crosshair, hpFill, minimapDots, toastColumn;
        private RawImage minimapImage;
        private Camera minimapCam;
        private RenderTexture minimapRt;
        private Vector3 minimapCentre = new Vector3(1e9f, 0f, 0f);
        private float minimapTimer, dotTimer, pollTimer, hostileTimer;
        private HostileMobAI[] hostiles;
        private const float MinimapHalfSize = 90f, MinimapPx = 280f;
        private readonly List<Image> dots = new List<Image>();
        private readonly List<(RectTransform bar, RectTransform fill)> unitBars = new List<(RectTransform, RectTransform)>();

        private int lastWood = -1, lastOre = -1, lastWorkers = -1, lastBuildings = -1;
        private readonly List<(RectTransform rt, Text text, CanvasGroup group, float born, string key, int amount)> toasts = new List<(RectTransform, Text, CanvasGroup, float, string, int)>();

        private void Start()
        {
            canvas = UiTheme.CreateCanvas("StrategyHUD", 5);
            canvas.transform.SetParent(transform, false);
            root = (RectTransform)canvas.transform;
            BuildTopBar();
            BuildFirstPerson();
            BuildMap();
            toastColumn = UiTheme.Rect(root, "Toasts", new Vector2(1f, 0f), new Vector2(-24f, 300f), new Vector2(360f, 260f));
        }

        // ------------------------------------------------------------------ layout

        private void BuildTopBar()
        {
            RectTransform bar = UiTheme.Rect(root, "ResourceBar", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(10f, 58f));
            UiTheme.PanelImage(bar);
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(22, 22, 8, 8);
            row.spacing = 26f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var fit = bar.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            woodText = Entry(bar, UiTheme.Icon.Wood, "Деревина");
            oreText = Entry(bar, UiTheme.Icon.Ore, "Руда");
            workerText = Entry(bar, UiTheme.Icon.Worker, "Робітники");
            armyText = Entry(bar, UiTheme.Icon.Army, "Військо");

            RectTransform mode = UiTheme.Rect(root, "ModeHint", new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(330f, 44f));
            UiTheme.PanelImage(mode);
            modeText = UiTheme.Label(mode, "Text", "", 19, TextAnchor.MiddleCenter, UiTheme.InkDim);
        }

        private Text Entry(RectTransform parent, UiTheme.Icon icon, string tooltip)
        {
            var go = new GameObject(tooltip, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f; h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = false;
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var img = iconGo.AddComponent<Image>();
            img.sprite = UiTheme.IconSprite(icon); img.preserveAspect = true; img.raycastTarget = false;
            var le = iconGo.AddComponent<LayoutElement>(); le.preferredWidth = le.preferredHeight = 36f;
            Text t = UiTheme.Label(go.transform, "Value", "0", 24, TextAnchor.MiddleLeft, UiTheme.Ink, FontStyle.Bold);
            t.gameObject.AddComponent<LayoutElement>().minWidth = 34f;
            return t;
        }

        private void BuildFirstPerson()
        {
            fpGroup = UiTheme.Rect(root, "FirstPerson", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fpGroup.anchorMin = Vector2.zero; fpGroup.anchorMax = Vector2.one; fpGroup.sizeDelta = Vector2.zero;

            crosshair = UiTheme.Rect(fpGroup, "Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            void Tick(Vector2 pos, Vector2 size)
            {
                RectTransform r = UiTheme.Rect(crosshair, "Tick", new Vector2(0.5f, 0.5f), pos, size);
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.BarSprite; img.type = Image.Type.Sliced; img.color = new Color(1f, 0.96f, 0.88f, 0.85f); img.raycastTarget = false;
                r.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            }
            Tick(Vector2.zero, new Vector2(4f, 4f));
            Tick(new Vector2(0f, 11f), new Vector2(2.5f, 8f)); Tick(new Vector2(0f, -11f), new Vector2(2.5f, 8f));
            Tick(new Vector2(11f, 0f), new Vector2(8f, 2.5f)); Tick(new Vector2(-11f, 0f), new Vector2(8f, 2.5f));

            RectTransform vitals = UiTheme.Rect(fpGroup, "Vitals", new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(380f, 66f));
            UiTheme.PanelImage(vitals);
            UiTheme.IconImage(vitals, UiTheme.Icon.Heart, new Vector2(0f, 0.5f), new Vector2(14f, 0f), 40f);
            RectTransform back = UiTheme.Rect(vitals, "Bar", new Vector2(0f, 0.5f), new Vector2(64f, 0f), new Vector2(296f, 26f));
            UiTheme.PanelImage(back, UiTheme.SlotSprite);
            hpFill = UiTheme.Rect(back, "Fill", new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(290f, 20f));
            UiTheme.PanelImage(hpFill, UiTheme.BarSprite, new Color(0.72f, 0.16f, 0.13f));
            hpText = UiTheme.Label(back, "Value", "", 18, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
        }

        private void BuildMap()
        {
            rtsGroup = UiTheme.Rect(root, "Map", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rtsGroup.anchorMin = Vector2.zero; rtsGroup.anchorMax = Vector2.one; rtsGroup.sizeDelta = Vector2.zero;

            RectTransform frame = UiTheme.Rect(rtsGroup, "Minimap", new Vector2(0f, 0f), new Vector2(18f, 18f), new Vector2(MinimapPx + 24f, MinimapPx + 24f));
            UiTheme.PanelImage(frame);
            RectTransform view = UiTheme.Rect(frame, "View", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MinimapPx, MinimapPx));
            view.gameObject.AddComponent<RectMask2D>();
            minimapImage = view.gameObject.AddComponent<RawImage>();
            minimapImage.color = new Color(0.9f, 0.9f, 0.9f);
            minimapImage.raycastTarget = false;
            minimapDots = UiTheme.Rect(view, "Dots", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MinimapPx, MinimapPx));
            RectTransform title = UiTheme.Rect(frame, "Title", new Vector2(0.5f, 1f), new Vector2(0f, 16f), new Vector2(120f, 30f));
            UiTheme.PanelImage(title, UiTheme.ButtonSprite);
            UiTheme.Label(title, "Text", "Мапа", 17, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);

            minimapRt = new RenderTexture(256, 256, 16) { name = "MinimapRT" };
            var camGo = new GameObject("MinimapCamera");
            camGo.transform.SetParent(transform, false);
            minimapCam = camGo.AddComponent<Camera>();
            minimapCam.orthographic = true;
            minimapCam.orthographicSize = MinimapHalfSize;
            minimapCam.nearClipPlane = 1f; minimapCam.farClipPlane = 600f;
            minimapCam.clearFlags = CameraClearFlags.SolidColor;
            minimapCam.backgroundColor = new Color(0.3f, 0.36f, 0.24f);
            int vm = LayerMask.NameToLayer("Viewmodel"), ui = LayerMask.NameToLayer("UI");
            minimapCam.cullingMask = ~((vm >= 0 ? 1 << vm : 0) | (ui >= 0 ? 1 << ui : 0));
            minimapCam.targetTexture = minimapRt;
            minimapCam.enabled = false;
            minimapImage.texture = minimapRt;
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (root == null) return;
            if (modes == null) modes = FindFirstObjectByType<GameModeManager>();
            if (commander == null) commander = FindFirstObjectByType<RtsUnitCommander>();
            if (playerHealth == null) { GameObject p = GameObject.Find("Player"); if (p != null) playerHealth = p.GetComponent<HealthComponent>(); }
            bool map = modes != null && modes.CurrentMode == GameMode.RtsMap;

            ResourceManager res = ResourceManager.Instance;
            int wood = res != null ? res.WoodCount : 0, ore = res != null ? res.OreCount : 0;
            woodText.text = wood.ToString();
            oreText.text = OreSummary(res, ore);
            int workers = WorkerRegistry.WorkerCount;
            workerText.text = workers.ToString();
            armyText.text = Mathf.Max(0, UnitRegistry.ActiveUnits.Count - workers).ToString();
            modeText.text = map ? "Мапа  ·  M — від першої особи" : "Від першої особи  ·  M — мапа";

            fpGroup.gameObject.SetActive(!map);
            rtsGroup.gameObject.SetActive(map);
            if (!map)
            {
                crosshair.gameObject.SetActive(!Cursor.visible && Time.timeScale > 0f);
                if (playerHealth != null)
                {
                    float f = playerHealth.MaxHealth > 0 ? Mathf.Clamp01(playerHealth.CurrentHealth / (float)playerHealth.MaxHealth) : 0f;
                    hpFill.sizeDelta = new Vector2(290f * f, 20f);
                    hpText.text = $"{playerHealth.CurrentHealth} / {playerHealth.MaxHealth}";
                }
                HideUnitBars(0);
            }
            else
            {
                UpdateMinimap();
                UpdateUnitBars();
            }

            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer <= 0f)
            {
                pollTimer = 0.3f;
                if (lastWood >= 0 && wood > lastWood) Toast("wood", wood - lastWood, n => $"+{n}  деревини");
                if (lastOre >= 0 && ore > lastOre) Toast("ore", ore - lastOre, n => $"+{n}  руди");
                if (lastWorkers >= 0 && workers > lastWorkers) Toast("worker", workers - lastWorkers, n => n > 1 ? $"Нові робітники: {n}" : "Новий робітник");
                int buildings = BuildingNode.Active.Count;
                if (lastBuildings >= 0 && buildings > lastBuildings)
                {
                    BuildingNode b = BuildingNode.Active[buildings - 1];
                    Toast("build" + buildings, 1, n => "Збудовано: " + BuildingData.GetDefinition(b.Type).DisplayName);
                }
                lastWood = wood; lastOre = ore; lastWorkers = workers; lastBuildings = buildings;
            }
            UpdateToasts();
        }

        private static readonly string[] Roman = { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        /// <summary>Total ore and the three highest tiers in stock ("16   II·4  I·12").</summary>
        private static string OreSummary(ResourceManager res, int total)
        {
            if (res == null) return "0";
            var sb = new System.Text.StringBuilder(total.ToString());
            int shown = 0;
            for (int t = Mathf.Min(res.HighestOreTier, OreTierHelper.MAX_TIERS - 1); t >= 0 && shown < 3; t--)
            {
                int n = res.GetOreTierCount(t);
                if (n <= 0) continue;
                sb.Append(shown == 0 ? "   " : "  ").Append("<size=17><color=#c9b89a>").Append(t < Roman.Length ? Roman[t] : t.ToString()).Append("·").Append(n).Append("</color></size>");
                shown++;
            }
            return sb.ToString();
        }

        private void UpdateMinimap()
        {
            Camera main = BattleOfAges.Utils.ViewCamera.Current;
            if (main == null) return;
            // Centre: where the view looks at the ground
            Vector3 c = main.transform.position;
            Vector3 f = main.transform.forward;
            if (f.y < -0.1f) c += f * Mathf.Min(300f, (c.y - 10f) / -f.y);
            c.y = 0f;
            minimapTimer -= Time.unscaledDeltaTime;
            if ((c - minimapCentre).sqrMagnitude > 12f * 12f || minimapTimer <= 0f)
            {
                minimapTimer = 5f;
                minimapCentre = c;
                minimapCam.transform.SetPositionAndRotation(new Vector3(c.x, main.transform.position.y + 250f, c.z), Quaternion.Euler(90f, 0f, 0f));
                bool fog = RenderSettings.fog;
                RenderSettings.fog = false;
                minimapCam.Render();
                RenderSettings.fog = fog;
            }

            dotTimer -= Time.unscaledDeltaTime;
            if (dotTimer > 0f) return;
            dotTimer = 0.15f;
            int used = 0;
            float scale = MinimapPx / (2f * MinimapHalfSize);
            var selected = commander != null ? commander.SelectedUnits : null;
            foreach (RtsUnit u in UnitRegistry.ActiveUnits)
            {
                if (u == null || used >= 400) continue;
                Vector3 d = u.transform.position - minimapCentre;
                if (Mathf.Abs(d.x) > MinimapHalfSize || Mathf.Abs(d.z) > MinimapHalfSize) continue;
                bool sel = selected != null && Contains(selected, u);
                Dot(used++, new Vector2(d.x, d.z) * scale, sel ? new Color(1f, 0.95f, 0.6f) : new Color(0.35f, 0.62f, 1f), sel ? 7f : 5f);
            }
            hostileTimer -= 0.15f;
            if (hostileTimer <= 0f || hostiles == null) { hostileTimer = 1f; hostiles = FindObjectsByType<HostileMobAI>(FindObjectsSortMode.None); }
            foreach (HostileMobAI m in hostiles)
            {
                if (used >= 500) break;
                if (m == null) continue;
                Vector3 d = m.transform.position - minimapCentre;
                if (Mathf.Abs(d.x) > MinimapHalfSize || Mathf.Abs(d.z) > MinimapHalfSize) continue;
                Dot(used++, new Vector2(d.x, d.z) * scale, new Color(0.95f, 0.25f, 0.2f), 5f);
            }
            Vector3 v = main.transform.position - minimapCentre;
            Dot(used++, new Vector2(v.x, v.z) * scale, Color.white, 9f);
            for (int i = used; i < dots.Count; i++) if (dots[i].enabled) dots[i].enabled = false;
        }

        private static bool Contains(IReadOnlyList<RtsUnit> list, RtsUnit u)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == u) return true;
            return false;
        }

        private void Dot(int i, Vector2 pos, Color color, float size)
        {
            while (dots.Count <= i)
            {
                RectTransform r = UiTheme.Rect(minimapDots, "Dot", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(5f, 5f));
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.RoundSprite; img.raycastTarget = false;
                dots.Add(img);
            }
            Image d = dots[i];
            d.enabled = true;
            d.color = color;
            d.rectTransform.anchoredPosition = pos;
            d.rectTransform.sizeDelta = new Vector2(size, size);
        }

        private void UpdateUnitBars()
        {
            Camera cam = BattleOfAges.Utils.ViewCamera.Current;
            var selected = commander != null ? commander.SelectedUnits : null;
            int used = 0;
            if (cam != null && selected != null)
                for (int i = 0; i < selected.Count && used < 64; i++)
                {
                    RtsUnit u = selected[i];
                    HealthComponent hp = u != null ? u.GetComponent<HealthComponent>() : null;
                    if (hp == null || hp.MaxHealth <= 0) continue;
                    Vector3 sp = cam.WorldToScreenPoint(u.transform.position + Vector3.up * 2.3f);
                    if (sp.z <= 0f) continue;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out Vector2 local);
                    while (unitBars.Count <= used)
                    {
                        RectTransform bar = UiTheme.Rect(rtsGroup, "UnitBar", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 10f));
                        UiTheme.PanelImage(bar, UiTheme.SlotSprite);
                        RectTransform fill = UiTheme.Rect(bar, "Fill", new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(60f, 6f));
                        UiTheme.PanelImage(fill, UiTheme.BarSprite, UiTheme.Good);
                        unitBars.Add((bar, fill));
                    }
                    var (b, fl) = unitBars[used++];
                    b.gameObject.SetActive(true);
                    b.anchorMin = b.anchorMax = new Vector2(0.5f, 0.5f);
                    b.anchoredPosition = local;
                    float frac = Mathf.Clamp01(hp.CurrentHealth / (float)hp.MaxHealth);
                    fl.sizeDelta = new Vector2(60f * frac, 6f);
                    fl.GetComponent<Image>().color = Color.Lerp(UiTheme.Bad, UiTheme.Good, frac);
                }
            HideUnitBars(used);
        }

        private void HideUnitBars(int from)
        {
            for (int i = from; i < unitBars.Count; i++) if (unitBars[i].bar.gameObject.activeSelf) unitBars[i].bar.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ toasts

        private void Toast(string key, int amount, System.Func<int, string> text)
        {
            // Keep merging into a recent toast of the same kind instead of stacking a new one
            for (int i = toasts.Count - 1; i >= 0; i--)
                if (toasts[i].key == key && Time.unscaledTime - toasts[i].born < 2.5f)
                {
                    var t = toasts[i];
                    t.amount += amount; t.born = Time.unscaledTime;
                    t.text.text = text(t.amount);
                    toasts[i] = t;
                    return;
                }
            RectTransform rt = UiTheme.Rect(toastColumn, "Toast", new Vector2(1f, 0f), Vector2.zero, new Vector2(340f, 42f));
            UiTheme.PanelImage(rt);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            Text label = UiTheme.Label(rt, "Text", text(amount), 19, TextAnchor.MiddleCenter, UiTheme.Gold);
            toasts.Add((rt, label, group, Time.unscaledTime, key, amount));
            if (toasts.Count > 5) { Destroy(toasts[0].rt.gameObject); toasts.RemoveAt(0); }
        }

        private void UpdateToasts()
        {
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - toasts[i].born;
                if (age > 4f) { Destroy(toasts[i].rt.gameObject); toasts.RemoveAt(i); continue; }
                toasts[i].group.alpha = Mathf.Clamp01((4f - age) / 0.6f) * Mathf.Clamp01(age / 0.15f + 0.3f);
            }
            for (int i = 0; i < toasts.Count; i++)
                toasts[i].rt.anchoredPosition = Vector2.Lerp(toasts[i].rt.anchoredPosition, new Vector2(0f, (toasts.Count - 1 - i) * 48f), 0.25f);
        }

        private void OnDestroy()
        {
            if (minimapRt != null) minimapRt.Release();
        }
    }
}
