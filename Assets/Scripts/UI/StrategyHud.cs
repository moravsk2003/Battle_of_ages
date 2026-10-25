using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;

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

        private Text woodText, oreText, workerText, armyText, clockText, modeText, hpText;
        private RectTransform mode;
        private RectTransform fpGroup, rtsGroup, crosshair, hpFill, minimapDots, toastColumn;
        private RawImage minimapImage;
        private Camera minimapCam;
        private RenderTexture minimapRt;
        private Vector3 minimapCentre = new Vector3(1e9f, 0f, 0f);
        private float minimapTimer, dotTimer, pollTimer, hostileTimer;
        private HostileMobAI[] hostiles;
        private const float MinimapHalfSize = 90f, MinimapPx = 280f;
        private RectTransform minimapFrame, heading;
        private int minimapPlacedFor = -1;
        private readonly List<Image> dots = new List<Image>();
        private readonly List<(RectTransform bar, RectTransform fill)> unitBars = new List<(RectTransform, RectTransform)>();
        private readonly Dictionary<MineNode, (RectTransform rt, Text text, Button button)> mineLabels = new Dictionary<MineNode, (RectTransform, Text, Button)>();

        private int lastWood = -1, lastOre = -1, lastWorkers = -1, lastBuildings = -1;
        private readonly List<(RectTransform rt, Text text, CanvasGroup group, float born, string key, int amount)> toasts = new List<(RectTransform, Text, CanvasGroup, float, string, int)>();

        private void Start()
        {
            active = this;
            canvas = UiTheme.CreateCanvas("StrategyHUD", 5);
            canvas.transform.SetParent(transform, false);
            root = (RectTransform)canvas.transform;
            BuildTopBar();
            BuildFirstPerson();
            BuildMap();
            toastColumn = UiTheme.Rect(root, "Toasts", new Vector2(1f, 0f), new Vector2(-24f, 300f), new Vector2(360f, 260f));
            AttackAlerts.Raised += OnAttackAlert;
            Advisor.Create(root);
        }

        // ------------------------------------------------------------------ attack alerts

        private readonly List<(Vector3 pos, float born, Image img)> pings = new List<(Vector3, float, Image)>();

        private void OnAttackAlert(string what, Vector3 where)
        {
            Toast("alert:" + what, 1, n => (n > 1 ? $"Напад: {what} ×{n}" : $"Напад: {what}") + $"  ·  {HudTips.Key(InputSystem.KeyAction.AlertJump)} — туди", UiTheme.Bad);
            RectTransform r = UiTheme.Rect(minimapDots, "Ping", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
            Image img = r.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.RingSprite; img.raycastTarget = false; img.color = UiTheme.Bad;
            pings.Add((where, Time.unscaledTime, img));
            if (pings.Count > 6) { Destroy(pings[0].img.gameObject); pings.RemoveAt(0); }
        }

        /// <summary>Pulsing rings where we were attacked; a place off the minimap sits on its edge.</summary>
        private void UpdatePings()
        {
            float scale = MinimapPx / (2f * MinimapHalfSize), edge = MinimapPx * 0.5f - 8f;
            for (int i = pings.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - pings[i].born;
                if (age > AttackAlerts.PingSeconds || pings[i].img == null) { if (pings[i].img != null) Destroy(pings[i].img.gameObject); pings.RemoveAt(i); continue; }
                Vector3 d = pings[i].pos - minimapCentre;
                Vector2 p = new Vector2(d.x, d.z) * scale;
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
        private void OnMinimapPressed(Vector2 local, bool right, bool drag)
        {
            if (modes == null || modes.CurrentMode != GameMode.RtsMap || GameWindows.BlockGameplay) return;
            float scale = MinimapPx / (2f * MinimapHalfSize);
            Vector3 world = minimapCentre + new Vector3(local.x / scale, 0f, local.y / scale);
            world.y = RtsUnit.GetTerrainHeight(world);
            if (right)
            {
                if (drag || commander == null || commander.SelectedUnits.Count == 0) return;
                commander.OrderMoveTo(world);
                RectTransform r = UiTheme.Rect(minimapDots, "MovePing", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
                Image img = r.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.RingSprite; img.raycastTarget = false; img.color = UiTheme.Good;
                pings.Add((world, Time.unscaledTime - AttackAlerts.PingSeconds + 1.2f, img)); // a short green ring
                return;
            }
            var cam = FindFirstObjectByType<CameraSystem.RtsMapCameraController>();
            if (cam != null) cam.LookAtGround(world);
        }

        /// <summary>Space on the map: the camera goes to the last attack.</summary>
        private void JumpToAlert(bool map)
        {
            if (!map || !AttackAlerts.HasLast || GameWindows.BlockGameplay || !InputSystem.KeybindsManager.GetKeyDown(InputSystem.KeyAction.AlertJump)) return;
            var cam = FindFirstObjectByType<CameraSystem.RtsMapCameraController>();
            if (cam != null) cam.LookAtGround(AttackAlerts.Last);
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

            woodText = Entry(bar, UiTheme.Icon.Wood, "Деревина", HudTips.Wood);
            oreText = Entry(bar, UiTheme.Icon.Ore, "Руда", HudTips.Ore);
            workerText = Entry(bar, UiTheme.Icon.Worker, "Робітники", HudTips.Workers);
            armyText = Entry(bar, UiTheme.Icon.Army, "Військо", HudTips.Army);
            clockText = Entry(bar, UiTheme.Icon.Clock, "Час", HudTips.Clock);
            clockText.fontSize = 19;
            clockText.color = UiTheme.InkDim;

            mode = UiTheme.Rect(root, "ModeHint", new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(470f, 44f));
            UiTheme.PanelImage(mode);
            modeText = UiTheme.Label(mode, "Text", "", 19, TextAnchor.MiddleCenter, UiTheme.InkDim);
        }

        private Text Entry(RectTransform parent, UiTheme.Icon icon, string name, System.Func<string> tip)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = Color.clear; // a hover target for the tooltip
            Tooltip.Set(go, tip);
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

            RectTransform vitals = UiTheme.Rect(fpGroup, "Vitals", new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(380f, 86f));
            UiTheme.PanelImage(vitals);
            UiTheme.IconImage(vitals, UiTheme.Icon.Heart, new Vector2(0f, 0.5f), new Vector2(14f, 6f), 40f);
            RectTransform back = UiTheme.Rect(vitals, "Bar", new Vector2(0f, 1f), new Vector2(64f, -14f), new Vector2(296f, 26f));
            UiTheme.PanelImage(back, UiTheme.SlotSprite);
            hpFill = UiTheme.Rect(back, "Fill", new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(290f, 20f));
            UiTheme.PanelImage(hpFill, UiTheme.BarSprite, new Color(0.72f, 0.16f, 0.13f));
            hpText = UiTheme.Label(back, "Value", "", 18, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);

            // Hero level and the experience to the next one
            RectTransform lv = UiTheme.Rect(vitals, "Level", new Vector2(0f, 0f), new Vector2(12f, 10f), new Vector2(48f, 24f));
            levelText = UiTheme.Label(lv, "Text", "", 15, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            RectTransform xpBack = UiTheme.Rect(vitals, "Xp", new Vector2(0f, 0f), new Vector2(64f, 16f), new Vector2(296f, 12f));
            UiTheme.PanelImage(xpBack, UiTheme.SlotSprite);
            xpFill = UiTheme.Rect(xpBack, "Fill", new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 7f));
            UiTheme.PanelImage(xpFill, UiTheme.BarSprite, UiTheme.Gold);

            // Q / R skills and the one-shot artifacts (1 / 2) at the bottom middle
            abilityBar = UiTheme.Rect(fpGroup, "Abilities", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(4 * 86f, 80f));
            abilitySlots.Add(AbilitySlot(InputSystem.KeyAction.Ability));
            abilitySlots.Add(AbilitySlot(InputSystem.KeyAction.Ability2));
            abilitySlots.Add(AbilitySlot(InputSystem.KeyAction.UseFullHeal));
            abilitySlots.Add(AbilitySlot(InputSystem.KeyAction.UseLightning));
            for (int i = 0; i < abilitySlots.Count; i++)
            {
                int index = i;
                Tooltip.Set(abilitySlots[i].rt.gameObject, () => AbilityTip(index));
            }

            // The alarm, in both views, under the resource bar
            alarmBanner = UiTheme.Rect(root, "AlarmBanner", new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(420f, 40f));
            UiTheme.PanelImage(alarmBanner, null, new Color(1f, 0.62f, 0.58f));
            UiTheme.IconImage(alarmBanner, UiTheme.Icon.Bell, new Vector2(0f, 0.5f), new Vector2(12f, 0f), 28f);
            UiTheme.Label(alarmBanner, "Text", "Тривога: усі до зброї", 19, TextAnchor.MiddleCenter, UiTheme.Bad, FontStyle.Bold);
            alarmBanner.gameObject.SetActive(false);
        }

        private Text levelText;
        private RectTransform xpFill, abilityBar, alarmBanner;
        private readonly List<(RectTransform rt, Image icon, Image cooldown, Text seconds, Text key)> abilitySlots = new List<(RectTransform, Image, Image, Text, Text)>();
        private HeroSkills playerSkills;
        private HeroArtifacts playerArtifacts;
        private HeroProgress playerProgress;

        private (RectTransform, Image, Image, Text, Text) AbilitySlot(InputSystem.KeyAction key)
        {
            RectTransform rt = UiTheme.Rect(abilityBar, "Slot_" + key, new Vector2(0f, 0f), Vector2.zero, new Vector2(72f, 72f));
            UiTheme.PanelImage(rt, UiTheme.SlotSprite);
            RectTransform ic = UiTheme.Rect(rt, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(50f, 50f));
            Image icon = ic.gameObject.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            RectTransform cd = UiTheme.Child(rt, "Cooldown", Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            Image cool = cd.gameObject.AddComponent<Image>();
            cool.sprite = UiTheme.BarSprite; cool.color = new Color(0f, 0f, 0f, 0.62f); cool.raycastTarget = false;
            cool.type = Image.Type.Filled; cool.fillMethod = Image.FillMethod.Radial360; cool.fillOrigin = (int)Image.Origin360.Top; cool.fillClockwise = false;
            Text secs = UiTheme.Label(rt, "Seconds", "", 22, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            RectTransform k = UiTheme.Rect(rt, "Key", new Vector2(0f, 1f), new Vector2(4f, -2f), new Vector2(30f, 22f));
            Text keyText = UiTheme.Label(k, "Text", "", 15, TextAnchor.UpperLeft, UiTheme.Gold, FontStyle.Bold);
            rt.gameObject.SetActive(false);
            return (rt, icon, cool, secs, keyText);
        }

        private static readonly InputSystem.KeyAction[] AbilityKeys =
            { InputSystem.KeyAction.Ability, InputSystem.KeyAction.Ability2, InputSystem.KeyAction.UseFullHeal, InputSystem.KeyAction.UseLightning };

        /// <summary>Which slots show (a skill the class has, an artifact held), their icons, cooldown rings and keys.</summary>
        private void UpdateAbilities()
        {
            if (playerSkills == null && playerHealth != null) playerSkills = playerHealth.GetComponent<HeroSkills>();
            if (playerArtifacts == null && playerHealth != null) playerArtifacts = playerHealth.GetComponent<HeroArtifacts>();
            if (playerProgress == null && playerHealth != null) playerProgress = playerHealth.GetComponent<HeroProgress>();

            if (playerProgress != null)
            {
                levelText.text = "Рів. " + playerProgress.Level;
                float f = Mathf.Clamp01(playerProgress.Xp / (float)Mathf.Max(1, playerProgress.XpToNextLevel));
                xpFill.sizeDelta = new Vector2(292f * f, 7f);
            }

            int shown = 0;
            for (int i = 0; i < abilitySlots.Count; i++)
            {
                var s = abilitySlots[i];
                bool on;
                float left = 0f, total = 1f;
                if (i < 2)
                {
                    on = playerSkills != null && playerSkills.HasActiveIn(i);
                    if (on) { left = playerSkills.CooldownLeftIn(i); total = Mathf.Max(0.01f, playerSkills.CooldownOf(i)); s.icon.sprite = SkillIcon(playerSkills.Class, i); }
                }
                else
                {
                    ArtifactType a = i == 2 ? ArtifactType.FullHeal : ArtifactType.Lightning;
                    on = playerArtifacts != null && playerArtifacts.Has(a);
                    if (on) s.icon.sprite = UiTheme.IconSprite(a == ArtifactType.FullHeal ? UiTheme.Icon.Heart : UiTheme.Icon.Bolt);
                }
                if (s.rt.gameObject.activeSelf != on) s.rt.gameObject.SetActive(on);
                if (!on) continue;
                s.rt.anchoredPosition = new Vector2(shown++ * 86f, 0f);
                s.cooldown.fillAmount = left > 0f ? left / total : 0f;
                s.seconds.text = left > 0f ? (left >= 60f ? Mathf.CeilToInt(left / 60f) + " хв" : Mathf.CeilToInt(left).ToString()) : "";
                s.icon.color = left > 0f ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
                s.key.text = HudTips.Key(AbilityKeys[i]);
            }
            abilityBar.sizeDelta = new Vector2(Mathf.Max(1, shown) * 86f - 14f, 72f);
        }

        private static Sprite SkillIcon(HeroClass c, int slot)
        {
            switch (c)
            {
                case HeroClass.Tank: return UiTheme.IconSprite(UiTheme.Icon.Heart);
                case HeroClass.Mage: return UiTheme.IconSprite(UiTheme.Icon.Fire);
                case HeroClass.Archer: return UiTheme.ItemSprite(ItemType.Bow, 4);
                default: return slot == 0 ? UiTheme.ItemSprite(ItemType.Boots, 4) : UiTheme.ItemSprite(ItemType.Sword, 4);
            }
        }

        private string AbilityTip(int i)
        {
            if (i < 2)
            {
                if (playerSkills == null) return null;
                float left = playerSkills.CooldownLeftIn(i);
                return TipText.Title(playerSkills.ActiveName(i), UiTheme.Gold) + "  " + TipText.Key(HudTips.Key(AbilityKeys[i]))
                       + "\n" + (left > 0f ? TipText.Dim($"Перезарядка ще {left:0} с") : TipText.Good("Готове"))
                       + "\n" + TipText.Dim($"Перезарядка {playerSkills.CooldownOf(i):0} с. Опис — у вікні героя ") + TipText.Key(HudTips.Key(InputSystem.KeyAction.Hero));
            }
            ArtifactType a = i == 2 ? ArtifactType.FullHeal : ArtifactType.Lightning;
            return TipText.Title(Artifacts.Name(a), UiTheme.Gold) + "  " + TipText.Key(HudTips.Key(AbilityKeys[i])) + "\n" + Artifacts.Description(a) + "\n" + TipText.Dim("Одноразовий: після використання зникає");
        }

        private void BuildMap()
        {
            rtsGroup = UiTheme.Rect(root, "Map", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rtsGroup.anchorMin = Vector2.zero; rtsGroup.anchorMax = Vector2.one; rtsGroup.sizeDelta = Vector2.zero;

            RectTransform frame = UiTheme.Rect(root, "Minimap", new Vector2(0f, 0f), new Vector2(18f, 18f), new Vector2(MinimapPx + 24f, MinimapPx + 24f));
            minimapFrame = frame;
            UiTheme.PanelImage(frame);
            RectTransform view = UiTheme.Rect(frame, "View", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MinimapPx, MinimapPx));
            view.gameObject.AddComponent<RectMask2D>();
            minimapImage = view.gameObject.AddComponent<RawImage>();
            minimapImage.color = new Color(0.9f, 0.9f, 0.9f);
            minimapImage.raycastTarget = true; // clicks: MinimapInput
            view.gameObject.AddComponent<MinimapInput>().Pressed = OnMinimapPressed;
            Tooltip.Set(view.gameObject, () => modes != null && modes.CurrentMode == GameMode.RtsMap
                ? TipText.Title("Мапа", UiTheme.Gold) + "\nЛКМ — камера туди (можна вести), ПКМ — обрані йдуть туди." + "\n" + TipText.Dim("Сині — свої, червоні — вороги, жовті — обрані, кільця — напад.")
                : null);
            minimapDots = UiTheme.Rect(view, "Dots", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MinimapPx, MinimapPx));
            heading = UiTheme.Rect(minimapDots, "Heading", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 16f));
            heading.pivot = new Vector2(0.5f, 0f);
            var headingImg = heading.gameObject.AddComponent<Image>();
            headingImg.sprite = UiTheme.BarSprite; headingImg.color = Color.white; headingImg.raycastTarget = false;
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
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData mmData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(minimapCam);
            mmData.renderShadows = false; // a map from above needs no shadows (a whole extra scene pass)
            mmData.renderPostProcessing = false;
            minimapImage.texture = minimapRt;

            // The call to arms, over the minimap
            alarmButton = UiTheme.TextButton(rtsGroup, "Alarm", "", new Vector2(0f, 0f), new Vector2(18f, MinimapPx + 24f + 30f), new Vector2(MinimapPx + 24f, 46f), SettlementAlarm.Toggle, 16);
            alarmText = alarmButton.GetComponentInChildren<Text>();
            Tooltip.Set(alarmButton.gameObject, HudTips.Alarm);

            // Workers doing nothing: count, and the next one on a click or the key
            idleButton = UiTheme.TextButton(rtsGroup, "IdleWorkers", "", new Vector2(0f, 0f), new Vector2(18f, MinimapPx + 24f + 30f + 54f), new Vector2(MinimapPx + 24f, 46f), SelectNextIdle, 16);
            idleText = idleButton.GetComponentInChildren<Text>();
            Tooltip.Set(idleButton.gameObject, () => TipText.Title("Робітники без діла", UiTheme.Gold)
                + "\nСтоять без наказу або не знаходять роботи." + "\n" + TipText.Dim("Клік — наступний: виділити й показати  ") + TipText.Key(HudTips.Key(InputSystem.KeyAction.IdleWorker)));
        }

        private Button idleButton;
        private Text idleText;
        private float idleTimer;

        private void UpdateIdleButton(bool map)
        {
            if (map && InputSystem.KeybindsManager.GetKeyDown(InputSystem.KeyAction.IdleWorker) && !GameWindows.BlockGameplay) SelectNextIdle();
            idleTimer -= Time.unscaledDeltaTime;
            if (idleTimer > 0f || idleText == null) return;
            idleTimer = 0.5f;
            int n = IdleWorkers.Count;
            idleText.text = $"Без діла: {n}  ({HudTips.Key(InputSystem.KeyAction.IdleWorker)})";
            idleText.color = n > 0 ? UiTheme.Gold : UiTheme.InkDim;
        }

        /// <summary>Selects the next idle worker and takes the map camera to it.</summary>
        public void SelectNextIdle()
        {
            NpcResourceGatherer w = IdleWorkers.Next();
            if (w == null) { Notify("Усі робітники при ділі"); return; }
            RtsUnit u = w.GetComponent<RtsUnit>();
            if (commander != null && u != null) commander.SelectOnly(u);
            var cam = FindFirstObjectByType<CameraSystem.RtsMapCameraController>();
            if (cam != null) cam.LookAtGround(w.transform.position);
        }

        private Button alarmButton;
        private Text alarmText;
        private bool alarmShown;

        private void UpdateAlarmButton()
        {
            if (alarmText == null) return;
            bool on = SettlementAlarm.Active;
            if (on == alarmShown && alarmText.text.Length > 0) return;
            alarmShown = on;
            string key = InputSystem.KeybindsManager.GetKeyDisplayString(InputSystem.KeybindsManager.GetKey(InputSystem.KeyAction.Alarm));
            alarmText.text = on ? $"Відбій ({key})" : $"Тривога: до зброї ({key})";
            alarmText.color = on ? UiTheme.Bad : UiTheme.Ink;
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (root == null) return;
            if (modes == null) modes = FindFirstObjectByType<GameModeManager>();
            if (commander == null) commander = FindFirstObjectByType<RtsUnitCommander>();
            if (playerHealth == null) { GameObject p = GameObject.Find("Player"); if (p != null) playerHealth = p.GetComponent<HealthComponent>(); }
            bool map = modes != null && modes.CurrentMode == GameMode.RtsMap;
            UpdateAlarmButton();
            if (alarmBanner.gameObject.activeSelf != SettlementAlarm.Active) alarmBanner.gameObject.SetActive(SettlementAlarm.Active);
            clockText.text = HudTips.ClockShort();

            ResourceManager res = ResourceManager.Instance;
            int wood = res != null ? res.WoodCount : 0, ore = res != null ? res.OreCount : 0;
            woodText.text = wood.ToString();
            oreText.text = OreSummary(res, ore);
            int workers = WorkerRegistry.WorkerCount;
            workerText.text = workers + "/" + TownHall.PopulationCap;
            workerText.color = workers >= TownHall.PopulationCap ? UiTheme.Bad : UiTheme.Ink;
            armyText.text = Mathf.Max(0, UnitRegistry.ActiveUnits.Count - workers).ToString();
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            bool placing = map && placer != null && placer.IsPlacing;
            modeText.text = placing ? placer.Hint : map ? "Мапа  ·  M — від першої особи  ·  F1 — довідка" : "Від першої особи  ·  M — мапа  ·  F1 — довідка";
            mode.sizeDelta = placing ? new Vector2(600f, 70f) : new Vector2(470f, 44f); // the placing hint takes two lines

            fpGroup.gameObject.SetActive(!map);
            rtsGroup.gameObject.SetActive(map);
            PlaceMinimap(map);
            UpdateMinimap(map);
            if (!map)
            {
                crosshair.gameObject.SetActive(!Cursor.visible && Time.timeScale > 0f);
                UpdateAbilities();
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
                UpdateUnitBars();
            }

            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer <= 0f)
            {
                pollTimer = 0.3f;
                if (lastWood >= 0 && wood > lastWood) Toast("wood", wood - lastWood, n => $"+{n} деревини");
                if (lastOre >= 0 && ore > lastOre) Toast("ore", ore - lastOre, n => $"+{n} руди");
                if (lastWorkers >= 0 && workers > lastWorkers) Toast("worker", workers - lastWorkers, n => n > 1 ? $"Нові робітники: {n}" : "Новий робітник");
                int buildings = BuildingNode.Active.Count;
                if (lastBuildings >= 0 && buildings > lastBuildings)
                {
                    BuildingNode b = BuildingNode.Active[buildings - 1];
                    Toast("build" + buildings, 1, n => "Збудовано: " + BuildingData.GetDefinition(b.Type).DisplayName);
                }
                lastWood = wood; lastOre = ore; lastWorkers = workers; lastBuildings = buildings;
            }
            UpdateMineLabels(map);
            UpdateHallLabel(map);
            UpdateToasts();
            UpdatePings();
            JumpToAlert(map);
            UpdateIdleButton(map);
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

        /// <summary>The minimap shows in both views: on the map at the bottom left, in first person smaller at the top right.</summary>
        private void PlaceMinimap(bool map)
        {
            if (minimapFrame == null || minimapPlacedFor == (map ? 1 : 0)) return;
            minimapPlacedFor = map ? 1 : 0;
            Vector2 corner = map ? Vector2.zero : Vector2.one;
            minimapFrame.anchorMin = minimapFrame.anchorMax = minimapFrame.pivot = corner;
            minimapFrame.anchoredPosition = map ? new Vector2(18f, 18f) : new Vector2(-18f, -18f);
            minimapFrame.localScale = Vector3.one * (map ? 1f : 0.72f);
            minimapCentre = new Vector3(1e9f, 0f, 0f); // draw it again for the new view
        }

        private static float InfiniteGround(Vector3 p) => World.InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);

        private void UpdateMinimap(bool map)
        {
            Camera main = ViewCamera.Current;
            if (main == null) return;
            // Centre: in first person the player; on the map where the view looks at the ground
            Vector3 c = main.transform.position;
            Vector3 f = main.transform.forward;
            if (map && f.y < -0.1f) c += f * ((c.y - InfiniteGround(c)) / -f.y);
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

            // Where the player looks (first person), every frame
            Vector3 me = main.transform.position - minimapCentre;
            heading.anchoredPosition = new Vector2(me.x, me.z) * (MinimapPx / (2f * MinimapHalfSize));
            heading.localEulerAngles = new Vector3(0f, 0f, -main.transform.eulerAngles.y);
            if (heading.gameObject.activeSelf != !map) heading.gameObject.SetActive(!map);

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
            Camera cam = ViewCamera.Current;
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

        // ------------------------------------------------------------------ mine labels

        /// <summary>
        /// Over every mine: workers inside and the ore waiting for haulers, with a button that sends one worker out (map
        /// view; in first person the label shows within 30 m, without the button).
        /// </summary>
        private void UpdateMineLabels(bool map)
        {
            Camera cam = ViewCamera.Current;
            foreach (MineNode mine in MineNode.ActiveMines)
            {
                if (mine == null) continue;
                if (!mineLabels.TryGetValue(mine, out var label)) mineLabels[mine] = label = MineLabel(mine);
                bool show = cam != null && mine.gameObject.activeInHierarchy && !mine.name.StartsWith("BuildGhost");
                Vector3 sp = show ? cam.WorldToScreenPoint(mine.transform.position + Vector3.up * 4.6f) : Vector3.zero;
                show &= sp.z > 0f && (map || sp.z < 30f);
                if (label.rt.gameObject.activeSelf != show) label.rt.gameObject.SetActive(show);
                if (!show) continue;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out Vector2 local);
                label.rt.anchoredPosition = local;
                // the two best tiers above the common ore ("III·1  I·7")
                string tiers = "";
                for (int t = mine.HighestTierInStock, shown = 0; t >= 1 && shown < 2; t--)
                {
                    int n = mine.OreOfTier(t);
                    if (n <= 0) continue;
                    tiers += (shown++ == 0 ? "  " : " ") + (t < Roman.Length ? Roman[t] : t.ToString()) + "·" + n;
                }
                if (tiers.Length > 0) tiers = "<size=15><color=#c9b89a>" + tiers + "</color></size>";
                label.text.text = $"Шахта  ·  робітники <color=#F5CC73>{mine.AssignedWorkerCount}/{MineNode.MAX_WORKERS}</color>  ·  {mine.OrePerSecond:0.#}/с\n" +
                                  $"руда <color={(mine.AccumulatedOreCount >= mine.MaxAccumulatedOre ? "#DB5242" : "#F5CC73")}>{mine.AccumulatedOreCount}/{mine.MaxAccumulatedOre}</color>{tiers}";
                bool canRelease = map && mine.AssignedWorkerCount > 0;
                if (label.button.gameObject.activeSelf != canRelease) label.button.gameObject.SetActive(canRelease);
            }
            if (mineLabels.Count > MineNode.ActiveMines.Count + 4) PruneMineLabels();
        }

        // ------------------------------------------------------------------ town hall label

        private RectTransform hallLabel;
        private Text hallText;
        private Button hallButton;
        private Text hallButtonText;

        /// <summary>Over the town hall: level, population against its cap, and the upgrade with its ore requirement.</summary>
        private void UpdateHallLabel(bool map)
        {
            TownHall hall = TownHall.Current;
            Camera cam = ViewCamera.Current;
            bool show = hall != null && cam != null;
            Vector3 sp = show ? cam.WorldToScreenPoint(hall.transform.position + Vector3.up * 7.5f) : Vector3.zero;
            show &= sp.z > 0f && (map || sp.z < 40f);
            if (hallLabel == null)
            {
                if (!show) return;
                hallLabel = UiTheme.Rect(root, "HallLabel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 60f));
                hallLabel.pivot = new Vector2(0.5f, 0f);
                UiTheme.PanelImage(hallLabel, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.94f)).raycastTarget = false;
                RectTransform tr = UiTheme.Rect(hallLabel, "Text", new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(200f, 52f));
                hallText = UiTheme.Label(tr, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.Ink);
                hallText.supportRichText = true;
                hallButton = UiTheme.TextButton(hallLabel, "Upgrade", "", new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(176f, 46f), () => TownHall.Current?.TryUpgrade(), 13);
                hallButtonText = hallButton.GetComponentInChildren<Text>();
                hallButtonText.supportRichText = true;
                hallButtonText.horizontalOverflow = HorizontalWrapMode.Wrap;
                hallLabel.SetAsFirstSibling();
            }
            if (hallLabel.gameObject.activeSelf != show) hallLabel.gameObject.SetActive(show);
            if (!show) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out Vector2 local);
            hallLabel.anchoredPosition = local;
            UpdateWallRow(map);
            int pop = TownHall.Population, cap = TownHall.PopulationCap;
            hallText.text = $"Ратуша <color=#F5CC73>{ItemTypeNames.Roman(hall.Level)}</color>\nнаселення <color={(pop >= cap ? "#DB5242" : "#F5CC73")}>{pop}/{cap}</color>";
            bool max = hall.Level >= TownHall.MaxLevel;
            bool canShowButton = map && !max;
            if (hallButton.gameObject.activeSelf != canShowButton) hallButton.gameObject.SetActive(canShowButton);
            if (!canShowButton) return;
            TownHall.UpgradeCost(hall.Level + 1, out int wood, out int ore, out int tier);
            bool ok = hall.CanUpgrade(out _);
            hallButtonText.text = $"Покращити до {ItemTypeNames.Roman(hall.Level + 1)}\n<size=11>{wood} дер.  ·  {ore} руди {ItemTypeNames.Roman(tier)}+</size>";
            hallButton.interactable = ok;
        }

        private RectTransform wallRow;
        private Text wallText, wallButtonText;
        private Button wallButton;

        /// <summary>Under the hall's label on the map, once there are walls: their level and the button to raise it (§10.5).</summary>
        private void UpdateWallRow(bool map)
        {
            bool show = map && WallSegment.Active.Count > 0;
            if (wallRow == null)
            {
                if (!show) return;
                wallRow = UiTheme.Rect(hallLabel, "WallRow", new Vector2(0.5f, 0f), new Vector2(0f, -4f), new Vector2(400f, 56f));
                wallRow.pivot = new Vector2(0.5f, 1f);
                UiTheme.PanelImage(wallRow, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.94f)).raycastTarget = false;
                RectTransform tr = UiTheme.Rect(wallRow, "Text", new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(200f, 50f));
                wallText = UiTheme.Label(tr, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.Ink);
                wallText.supportRichText = true;
                wallButton = UiTheme.TextButton(wallRow, "UpgradeWalls", "", new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(176f, 44f), () => Fortifications.TryUpgrade(), 13);
                wallButtonText = wallButton.GetComponentInChildren<Text>();
                wallButtonText.supportRichText = true;
                wallButtonText.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            if (wallRow.gameObject.activeSelf != show) wallRow.gameObject.SetActive(show);
            if (!show) return;
            int level = Fortifications.Level;
            wallText.text = $"Стіни <color=#F5CC73>{ItemTypeNames.Roman(level)}</color>\nурон зі стін <color=#F5CC73>+{Mathf.RoundToInt(Fortifications.DamageBonus * 100f)}%</color>";
            bool max = level >= Fortifications.MaxLevel;
            if (wallButton.gameObject.activeSelf == max) wallButton.gameObject.SetActive(!max);
            if (max) return;
            Fortifications.UpgradeCost(level + 1, out int wood, out int ore, out int tier);
            wallButtonText.text = $"Стіни до {ItemTypeNames.Roman(level + 1)}\n<size=11>{wood} дер.  ·  {ore} руди {ItemTypeNames.Roman(tier)}+</size>";
            wallButton.interactable = Fortifications.CanUpgrade(out _);
        }

        private (RectTransform, Text, Button) MineLabel(MineNode mine)
        {
            RectTransform rt = UiTheme.Rect(root, "MineLabel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(350f, 56f));
            rt.pivot = new Vector2(0.5f, 0f);
            UiTheme.PanelImage(rt, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.92f)).raycastTarget = false;
            RectTransform tr = UiTheme.Rect(rt, "Text", new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(250f, 50f));
            Text text = UiTheme.Label(tr, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.Ink);
            text.supportRichText = true;
            Button b = UiTheme.TextButton(rt, "Release", "Вивести", new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(84f, 38f), () => mine.ReleaseOneWorker(), 15);
            rt.SetAsFirstSibling(); // under the windows and toasts
            return (rt, text, b);
        }

        private void PruneMineLabels()
        {
            var gone = new List<MineNode>();
            foreach (var pair in mineLabels) if (pair.Key == null) { Destroy(pair.Value.rt.gameObject); gone.Add(pair.Key); }
            foreach (MineNode m in gone) mineLabels.Remove(m);
        }

        private void HideUnitBars(int from)
        {
            for (int i = from; i < unitBars.Count; i++) if (unitBars[i].bar.gameObject.activeSelf) unitBars[i].bar.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ toasts

        private static StrategyHud active;

        /// <summary>Shows a short message in the toast column (crafted items, trophies...). No-op without a HUD.</summary>
        public static void Notify(string message)
        {
            if (active == null || active.toastColumn == null) return;
            active.Toast("msg:" + message, 1, n => n > 1 ? message + "  ×" + n : message);
        }

        private void Toast(string key, int amount, System.Func<int, string> text, Color? color = null)
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
            Text label = UiTheme.Label(rt, "Text", text(amount), 19, TextAnchor.MiddleCenter, color ?? UiTheme.Gold);
            if (color.HasValue) rt.GetComponent<Image>().color = new Color(1f, 0.75f, 0.7f);
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
            AttackAlerts.Raised -= OnAttackAlert;
            if (minimapRt != null) minimapRt.Release();
        }
    }
}
