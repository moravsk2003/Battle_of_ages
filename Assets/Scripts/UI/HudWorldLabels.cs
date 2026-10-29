using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Managers;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Plates that stand over things in the world, under the rest of the HUD: over each mine its workers and stock (with
    /// a button that sends a miner out), over the town hall its level and population with the upgrade (and the walls'
    /// level under it), and health bars over the selected units on the map. In first person the plates show only near.
    /// </summary>
    public sealed class HudWorldLabels : MonoBehaviour
    {
        private static readonly string[] Roman = { "0", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };

        private RectTransform layer;
        private readonly Dictionary<MineNode, (RectTransform rt, Text text, Button button)> mines = new Dictionary<MineNode, (RectTransform, Text, Button)>();
        private readonly Dictionary<MineNode, long> mineShown = new Dictionary<MineNode, long>();
        private readonly List<(RectTransform bar, RectTransform fill)> unitBars = new List<(RectTransform, RectTransform)>();
        private RectTransform hallLabel, wallRow;
        private Text hallText, hallButtonText, wallText, wallButtonText;
        private Button hallButton, wallButton;
        private Shown hallShown, hallButtonShown, wallShown, wallButtonShown;

        public static HudWorldLabels Create(RectTransform root)
        {
            RectTransform layer = UiTheme.Child(root, "WorldLabels", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            layer.SetAsFirstSibling(); // under the windows, toasts and bars
            HudWorldLabels w = layer.gameObject.AddComponent<HudWorldLabels>();
            w.layer = layer;
            return w;
        }

        private void Update()
        {
            bool map = GameContext.MapOpen;
            Camera cam = ViewCamera.Current;
            UpdateMines(cam, map);
            UpdateHall(cam, map);
            if (map) UpdateUnitBars(cam);
            else HideUnitBars(0);
        }

        /// <summary>Where a world point is on this layer, or false when it is behind the camera.</summary>
        private bool OnScreen(Camera cam, Vector3 world, out Vector2 local, out float depth)
        {
            Vector3 sp = cam.WorldToScreenPoint(world);
            depth = sp.z;
            local = default;
            if (sp.z <= 0f) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, sp, null, out local);
            return true;
        }

        // ------------------------------------------------------------------ mines

        private void UpdateMines(Camera cam, bool map)
        {
            foreach (MineNode mine in MineNode.ActiveMines)
            {
                if (mine == null) continue;
                if (!mines.TryGetValue(mine, out var label)) mines[mine] = label = MineLabel(mine);
                bool show = cam != null && mine.gameObject.activeInHierarchy && !BuildGhost.Is(mine);
                Vector2 local = default;
                show = show && OnScreen(cam, mine.transform.position + Vector3.up * 4.6f, out local, out float depth) && (map || depth < 30f);
                if (label.rt.gameObject.activeSelf != show) label.rt.gameObject.SetActive(show);
                if (!show) continue;
                label.rt.anchoredPosition = local;
                bool canRelease = map && mine.AssignedWorkerCount > 0;
                if (label.button.gameObject.activeSelf != canRelease) label.button.gameObject.SetActive(canRelease);
                // only when something on it changed: workers, rate, stock and its two best tiers
                long key = Shown.Key(mine.AssignedWorkerCount, Mathf.RoundToInt(mine.OrePerSecond * 10f), mine.AccumulatedOreCount, mine.MaxAccumulatedOre);
                for (int t = mine.HighestTierInStock, n2 = 0; t >= 1 && n2 < 2; t--) { int n = mine.OreOfTier(t); if (n > 0) { key = Shown.Key(key, t * 100000L + n); n2++; } }
                if (mineShown.TryGetValue(mine, out long was) && was == key) continue;
                mineShown[mine] = key;
                label.text.text = MineText(mine);
            }
            if (mines.Count > MineNode.ActiveMines.Count + 4) PruneMines();
        }

        private static string MineText(MineNode mine)
        {
            // the two best tiers above the common ore ("III·1  I·7")
            string tiers = "";
            for (int t = mine.HighestTierInStock, shown = 0; t >= 1 && shown < 2; t--)
            {
                int n = mine.OreOfTier(t);
                if (n <= 0) continue;
                tiers += (shown++ == 0 ? "  " : " ") + (t < Roman.Length ? Roman[t] : t.ToString()) + "·" + n;
            }
            if (tiers.Length > 0) tiers = "<size=15><color=#c9b89a>" + tiers + "</color></size>";
            return $"Шахта  ·  робітники <color=#F5CC73>{mine.AssignedWorkerCount}/{MineNode.MAX_WORKERS}</color>  ·  {mine.OrePerSecond:0.#}/с\n" +
                   $"руда <color={(mine.AccumulatedOreCount >= mine.MaxAccumulatedOre ? "#DB5242" : "#F5CC73")}>{mine.AccumulatedOreCount}/{mine.MaxAccumulatedOre}</color>{tiers}";
        }

        private static string MineHowTo() =>
            TipText.Title("Як працює шахта", UiTheme.Gold) +
            "\n1. На мапі виділіть робітників і клацніть " + TipText.Key("ПКМ") + " по шахті: вони заходять усередину (до " + MineNode.MAX_WORKERS + ")." +
            "\n2. Кожен копає 1 руду/с, з кайлом більше. Руда складається в шахті (число тут, на табличці)." +
            "\n3. Інші робітники з наказом «Шахта» або «Самостійно» носять її в скриню — лише тоді вона у ваших запасах." +
            "\n4. Зрідка трапляється руда вищого рангу (I, II…): з неї кують кращу зброю й броню." +
            "\n" + TipText.Dim("«Вивести» — забрати одного робітника з шахти. Повна шахта (червоне число) стоїть: потрібні носії.");

        private (RectTransform, Text, Button) MineLabel(MineNode mine)
        {
            RectTransform rt = UiTheme.Rect(layer, "MineLabel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(350f, 56f));
            rt.pivot = new Vector2(0.5f, 0f);
            UiTheme.PanelImage(rt, UiTheme.PanelSprite, new Color(1f, 1f, 1f, 0.92f));
            Tooltip.Set(rt.gameObject, MineHowTo); // a tester could not make out how the ore comes about: the whole chain on hover
            RectTransform tr = UiTheme.Rect(rt, "Text", new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(250f, 50f));
            Text text = UiTheme.Label(tr, "Text", "", 16, TextAnchor.MiddleLeft, UiTheme.Ink);
            text.supportRichText = true;
            Button b = UiTheme.TextButton(rt, "Release", "Вивести", new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(84f, 38f), () => mine.ReleaseOneWorker(), 15);
            return (rt, text, b);
        }

        private void PruneMines()
        {
            var gone = new List<MineNode>();
            foreach (var pair in mines) if (pair.Key == null) { Destroy(pair.Value.rt.gameObject); gone.Add(pair.Key); }
            foreach (MineNode m in gone) { mines.Remove(m); mineShown.Remove(m); }
        }

        // ------------------------------------------------------------------ town hall and walls

        /// <summary>Over the town hall: level, population against its cap, and the upgrade with its ore requirement.</summary>
        private void UpdateHall(Camera cam, bool map)
        {
            TownHall hall = TownHall.Current;
            Vector2 local = default;
            bool show = hall != null && cam != null && OnScreen(cam, hall.transform.position + Vector3.up * 7.5f, out local, out float depth) && (map || depth < 40f);
            if (hallLabel == null)
            {
                if (!show) return;
                BuildHallLabel();
            }
            if (hallLabel.gameObject.activeSelf != show) hallLabel.gameObject.SetActive(show);
            if (!show) return;
            hallLabel.anchoredPosition = local;
            UpdateWallRow(map);
            int pop = TownHall.Population, cap = TownHall.PopulationCap;
            if (hallShown.Changed(Shown.Key(hall.Level, pop, cap)))
                hallText.text = $"Ратуша <color=#F5CC73>{ItemTypeNames.Roman(hall.Level)}</color>\nнаселення <color={(pop >= cap ? "#DB5242" : "#F5CC73")}>{pop}/{cap}</color>";
            bool canShowButton = map && hall.Level < TownHall.MaxLevel;
            if (hallButton.gameObject.activeSelf != canShowButton) hallButton.gameObject.SetActive(canShowButton);
            if (!canShowButton) return;
            TownHall.UpgradeCost(hall.Level + 1, out int wood, out int ore, out int tier);
            if (hallButtonShown.Changed(Shown.Key(hall.Level, wood, ore, tier)))
                hallButtonText.text = $"Покращити до {ItemTypeNames.Roman(hall.Level + 1)}\n<size=11>{wood} дер.  ·  {ore} руди {ItemTypeNames.Roman(tier)}+</size>";
            hallButton.interactable = hall.CanUpgrade(out _);
        }

        private void BuildHallLabel()
        {
            hallLabel = UiTheme.Rect(layer, "HallLabel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 60f));
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
            if (wallShown.Changed(level))
                wallText.text = $"Стіни <color=#F5CC73>{ItemTypeNames.Roman(level)}</color>\nшкода зі стін <color=#F5CC73>+{Mathf.RoundToInt(Fortifications.DamageBonus * 100f)}%</color>";
            bool max = level >= Fortifications.MaxLevel;
            if (wallButton.gameObject.activeSelf == max) wallButton.gameObject.SetActive(!max);
            if (max) return;
            Fortifications.UpgradeCost(level + 1, out int wood, out int ore, out int tier);
            if (wallButtonShown.Changed(Shown.Key(level, wood, ore, tier)))
                wallButtonText.text = $"Стіни до {ItemTypeNames.Roman(level + 1)}\n<size=11>{wood} дер.  ·  {ore} руди {ItemTypeNames.Roman(tier)}+</size>";
            wallButton.interactable = Fortifications.CanUpgrade(out _);
        }

        // ------------------------------------------------------------------ unit bars

        private void UpdateUnitBars(Camera cam)
        {
            RtsUnitCommander commander = GameContext.Commander;
            var selected = commander != null ? commander.SelectedUnits : null;
            int used = 0;
            if (cam != null && selected != null)
                for (int i = 0; i < selected.Count && used < 64; i++)
                {
                    RtsUnit u = selected[i];
                    HealthComponent hp = u != null ? u.GetComponent<HealthComponent>() : null;
                    if (hp == null || hp.MaxHealth <= 0) continue;
                    if (!OnScreen(cam, u.transform.position + Vector3.up * 2.3f, out Vector2 local, out _)) continue;
                    while (unitBars.Count <= used)
                    {
                        RectTransform bar = UiTheme.Rect(layer, "UnitBar", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 10f));
                        UiTheme.PanelImage(bar, UiTheme.SlotSprite);
                        RectTransform fill = UiTheme.Rect(bar, "Fill", new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(60f, 6f));
                        UiTheme.PanelImage(fill, UiTheme.BarSprite, UiTheme.Good);
                        unitBars.Add((bar, fill));
                    }
                    var (b, fl) = unitBars[used++];
                    b.gameObject.SetActive(true);
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
    }
}
