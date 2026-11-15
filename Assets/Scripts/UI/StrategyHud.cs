using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using InputKey = BattleOfAges.InputSystem.KeyAction;
using TMPro;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Always-on strategy HUD in the UiTheme look. It owns the canvas, the resource bar (wood, ore by tier, workers,
    /// army, clock) and the mode hint at the top, the alarm banner, and on the map the alarm and idle-worker buttons;
    /// the rest are parts of their own: HudFirstPerson (crosshair, health, skills), HudMinimap, HudWorldLabels (plates
    /// over mines and the hall, unit bars), HudToasts, Advisor, CombatFeedback. Started by UiBoot.
    /// </summary>
    public sealed class StrategyHud : MonoBehaviour
    {
        private static readonly string[] Roman = { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        private RectTransform root, mode, fpGroup, rtsGroup, alarmBanner;
        private TMP_Text woodText, oreText, foodText, workerText, armyText, clockText, modeText, alarmText, idleText;
        private Button alarmButton, idleButton;
        private bool alarmShown;
        private float idleTimer;
        private Shown clockShown, woodShown, oreShown, foodShown, workerShown, armyShown;

        private void Start()
        {
            Canvas canvas = UiTheme.CreateCanvas("StrategyHUD", 5);
            canvas.transform.SetParent(transform, false);
            root = (RectTransform)canvas.transform;
            HudWorldLabels.Create(root);
            BuildTopBar();
            fpGroup = Group("FirstPerson");
            HudFirstPerson.Create(fpGroup);
            BuildAlarmBanner();
            rtsGroup = Group("Map");
            BuildMapButtons();
            HudMinimap.Create(root);
            HudToasts.Create(root);
            HudPlayerList.Create(root);
            Advisor.Create(root);
            CombatFeedback.Create(root);
        }

        /// <summary>A full-screen group shown in one view only.</summary>
        private RectTransform Group(string name) => UiTheme.Child(root, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        /// <summary>Shows a short message in the toast column (crafted items, trophies...). No-op without a HUD.</summary>
        public static void Notify(string message)
        {
            HudToasts t = HudToasts.Active;
            if (t != null) t.Show("msg:" + message, 1, n => n > 1 ? Loc.Tr(message) + "  ×" + n : Loc.Tr(message));
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
            foodText = Entry(bar, UiTheme.Icon.Food, "Їжа", HudTips.Food);
            workerText = Entry(bar, UiTheme.Icon.Worker, "Робітники", HudTips.Workers);
            armyText = Entry(bar, UiTheme.Icon.Army, "Військо", HudTips.Army);
            clockText = Entry(bar, UiTheme.Icon.Clock, "Час", HudTips.Clock);
            clockText.fontSize = 19;
            clockText.color = UiTheme.InkDim;

            mode = UiTheme.Rect(root, "ModeHint", new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(470f, 44f));
            UiTheme.PanelImage(mode);
            modeText = UiTheme.Label(mode, "Text", "", 19, TextAnchor.MiddleCenter, UiTheme.InkDim);
        }

        private TMP_Text Entry(RectTransform parent, UiTheme.Icon icon, string name, System.Func<string> tip)
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
            TMP_Text t = UiTheme.Label(go.transform, "Value", "0", 24, TextAnchor.MiddleLeft, UiTheme.Ink, FontStyle.Bold);
            t.gameObject.AddComponent<LayoutElement>().minWidth = 34f;
            return t;
        }

        /// <summary>The alarm, in both views, under the resource bar.</summary>
        private void BuildAlarmBanner()
        {
            alarmBanner = UiTheme.Rect(root, "AlarmBanner", new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(420f, 40f));
            UiTheme.PanelImage(alarmBanner, null, new Color(1f, 0.62f, 0.58f));
            UiTheme.IconImage(alarmBanner, UiTheme.Icon.Bell, new Vector2(0f, 0.5f), new Vector2(12f, 0f), 28f);
            UiTheme.Label(alarmBanner, "Text", "Тривога: усі до зброї", 19, TextAnchor.MiddleCenter, UiTheme.Bad, FontStyle.Bold);
            alarmBanner.gameObject.SetActive(false);
        }

        /// <summary>Over the minimap on the map: the call to arms, and the workers doing nothing.</summary>
        private void BuildMapButtons()
        {
            float x = 18f, w = HudMinimap.FramePx, above = HudMinimap.FramePx + 30f;
            ZoneToolbar.Create(rtsGroup);
            alarmButton = UiTheme.TextButton(rtsGroup, "Alarm", "", new Vector2(0f, 0f), new Vector2(x, above), new Vector2(w, 46f), () => Net.CommandBus.Submit(new Net.ToggleAlarmCommand()), 16);
            alarmText = alarmButton.GetComponentInChildren<TMP_Text>();
            Tooltip.Set(alarmButton.gameObject, HudTips.Alarm);

            idleButton = UiTheme.TextButton(rtsGroup, "IdleWorkers", "", new Vector2(0f, 0f), new Vector2(x, above + 54f), new Vector2(w, 46f), SelectNextIdle, 16);
            idleText = idleButton.GetComponentInChildren<TMP_Text>();
            Tooltip.Set(idleButton.gameObject, () => TipText.Title(Loc.Pick("Робітники без діла", "Idle Workers"), UiTheme.Gold)
                + "\n" + Loc.Pick("Мають роботу, але не знаходять, що робити (немає дерев, руди, скрині поруч).", "Assigned to work, but cannot find anything to do nearby (no trees, ore, or chest).")
                + "\n" + TipText.Dim(Loc.Pick("Клік — наступний: виділити й показати  ", "Click — next: select and focus  ")) + TipText.Key(HudTips.Key(InputKey.IdleWorker)));
        }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (root == null) return;
            bool map = GameContext.MapOpen;
            if (fpGroup.gameObject.activeSelf != !map) fpGroup.gameObject.SetActive(!map);
            if (rtsGroup.gameObject.activeSelf != map) rtsGroup.gameObject.SetActive(map);
            if (alarmBanner.gameObject.activeSelf != SettlementAlarm.Active) alarmBanner.gameObject.SetActive(SettlementAlarm.Active);
            UpdateTopBar(map);
            if (map)
            {
                UpdateAlarmButton();
                UpdateIdleButton();
            }
        }

        /// <summary>The numbers at the top; a text is made again only when its number changes (no garbage per frame).</summary>
        private void UpdateTopBar(bool map)
        {
            if (clockShown.Changed((long)Combat.WorldClock.GameHours + (Loc.En ? 1000000000L : 0L))) clockText.text = HudTips.ClockShort();
            ResourceManager res = ResourceManager.Instance;
            int wood = res != null ? res.WoodCount : 0, ore = res != null ? res.OreCount : 0;
            if (woodShown.Changed(wood)) woodText.text = wood.ToString();
            bool foodRules = ResourceManager.FoodRules;
            if (foodText.transform.parent.gameObject.activeSelf != foodRules) foodText.transform.parent.gameObject.SetActive(foodRules);
            if (foodRules && ResourceManager.Instance != null)
            {
                int food = ResourceManager.Instance.FoodCount;
                if (foodShown.Changed(food))
                {
                    foodText.text = food.ToString();
                    foodText.color = ResourceManager.Instance.Hungry ? UiTheme.Bad : UiTheme.Ink;
                }
            }
            if (oreShown.Changed(OreKey(res, ore))) oreText.text = OreSummary(res, ore);
            int workers = WorkerRegistry.WorkerCount, army = Mathf.Max(0, UnitRegistry.ActiveUnits.Count - workers);
            if (workerShown.Changed(Shown.Key(workers, TownHall.PopulationCap)))
            {
                workerText.text = workers + "/" + TownHall.PopulationCap;
                workerText.color = workers >= TownHall.PopulationCap ? UiTheme.Bad : UiTheme.Ink;
            }
            if (armyShown.Changed(army)) armyText.text = army.ToString();
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            bool placing = map && placer != null && placer.IsPlacing;
            string mText = placing ? Loc.Tr(placer.Hint) : map
                ? Loc.Pick($"{HudTips.Key(InputKey.ToggleGameMode)} — від першої особи  ·  {HudTips.Key(InputKey.WorldMap)} — світ  ·  F1 — довідка", $"{HudTips.Key(InputKey.ToggleGameMode)} — first person  ·  {HudTips.Key(InputKey.WorldMap)} — world  ·  F1 — help")
                : Loc.Pick($"{HudTips.Key(InputKey.ToggleGameMode)} — мапа  ·  {HudTips.Key(InputKey.WorldMap)} — мапа світу  ·  F1 — довідка", $"{HudTips.Key(InputKey.ToggleGameMode)} — map  ·  {HudTips.Key(InputKey.WorldMap)} — world map  ·  F1 — help");
            if (modeText.text != mText) modeText.text = mText;
            Vector2 mSize = placing ? new Vector2(600f, 70f) : new Vector2(map ? 510f : 470f, 44f); // the placing hint takes two lines
            if (mode.sizeDelta != mSize) mode.sizeDelta = mSize;
        }

        /// <summary>The ore summary as a number: the total and the three highest tiers in stock with their counts.</summary>
        private static long OreKey(ResourceManager res, int total)
        {
            if (res == null) return 0;
            long k = total;
            int shown = 0;
            for (int t = Mathf.Min(res.HighestOreTier, OreTierHelper.MAX_TIERS - 1); t >= 0 && shown < 3; t--)
            {
                int n = res.GetOreTierCount(t);
                if (n <= 0) continue;
                k = Shown.Key(k, t * 100000L + n);
                shown++;
            }
            return k;
        }

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

        private void UpdateAlarmButton()
        {
            bool on = SettlementAlarm.Active;
            if (on == alarmShown && alarmText.text.Length > 0) return;
            alarmShown = on;
            string key = HudTips.Key(InputKey.Alarm);
            alarmText.text = on ? Loc.Pick($"Відбій ({key})", $"Stand down ({key})") : Loc.Pick($"Тривога: до зброї ({key})", $"Alarm: to arms ({key})");
            alarmText.color = on ? UiTheme.Bad : UiTheme.Ink;
        }

        private void UpdateIdleButton()
        {
            if (InputSystem.KeybindsManager.GetKeyDown(InputKey.IdleWorker) && !GameWindows.BlockGameplay) SelectNextIdle();
            idleTimer -= Time.unscaledDeltaTime;
            if (idleTimer > 0f) return;
            idleTimer = 0.5f;
            int n = IdleWorkers.Count;
            idleText.text = Loc.Pick($"Без діла: {n}  ({HudTips.Key(InputKey.IdleWorker)})", $"Idle: {n}  ({HudTips.Key(InputKey.IdleWorker)})");
            idleText.color = n > 0 ? UiTheme.Gold : UiTheme.InkDim;
        }

        /// <summary>Selects the next idle worker and takes the map camera to it.</summary>
        public void SelectNextIdle()
        {
            NpcResourceGatherer w = IdleWorkers.Next();
            if (w == null) { UiSound.Play(UiSound.Kind.Error); Notify("Усі робітники при ділі"); return; }
            UiSound.Play(UiSound.Kind.Click);
            RtsUnit u = w.GetComponent<RtsUnit>();
            RtsUnitCommander commander = GameContext.Commander;
            if (commander != null && u != null) commander.SelectOnly(u);
            GameContext.MapCamera?.LookAtGround(w.transform.position);
        }
    }
}
