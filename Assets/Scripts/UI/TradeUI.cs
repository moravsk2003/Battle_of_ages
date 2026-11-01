using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Resources;

namespace BattleOfAges.UI
{
    /// <summary>The market's window (M5 step 4, a click on a market): fixed exchanges of the settlement's wood, food and common ore, at a loss, for when one runs short and another piles up.</summary>
    public sealed class TradeUI : MonoBehaviour
    {
        private enum Good { Wood, Food, Ore }

        private struct Offer { public Good Give, Take; public int GiveAmount, TakeAmount; public Button Button; }

        private static TradeUI instance;
        private GameObject window;
        private Offer[] offers;
        private Text stock, caravanTitle;
        private RectTransform panel;
        private GameObject caravanRow;
        private Button[] caravanButtons;
        private static readonly (CaravanGood Good, int Amount, string Name)[] Loads =
        {
            (CaravanGood.Wood, 100, "100 дерева"), (CaravanGood.Food, 40, "40 їжі"), (CaravanGood.Ore, 40, "40 руди")
        };

        public static bool IsOpen => instance != null && instance.window != null && instance.window.activeSelf;

        public static void Open()
        {
            if (instance == null)
            {
                instance = new GameObject("TradeUI").AddComponent<TradeUI>();
                instance.Build();
            }
            instance.window.SetActive(true);
            instance.Refresh();
        }

        private void Close() => window.SetActive(false);

        private static string Name(Good g) => g == Good.Wood ? "дерева" : g == Good.Food ? "їжі" : "руди";

        private static int Have(Good g)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return 0;
            return g == Good.Wood ? rm.WoodCount : g == Good.Food ? rm.FoodCount : rm.GetOreTierCount(0);
        }

        /// <summary>Makes one exchange if the stock has it; false otherwise.</summary>
        private static bool Trade(Good give, int giveAmount, Good take, int takeAmount)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null || Have(give) < giveAmount) return false;
            if (give == Good.Wood) rm.ConsumeResource(ResourceType.Wood, giveAmount);
            else if (give == Good.Food) rm.SetFood(rm.Food - giveAmount);
            else rm.ConsumeOre(0, giveAmount);
            if (take == Good.Wood) rm.AddResource(ResourceType.Wood, takeAmount);
            else if (take == Good.Food) rm.AddFood(takeAmount);
            else rm.AddOre(0, takeAmount);
            return true;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (UniversalInput.GetKeyDownEscape()) { Close(); return; }
            Refresh();
        }

        private void Refresh()
        {
            stock.text = $"Є: дерева <color=#F5CC73>{Have(Good.Wood)}</color>  ·  їжі <color=#F5CC73>{Have(Good.Food)}</color>  ·  звичайної руди <color=#F5CC73>{Have(Good.Ore)}</color>";
            foreach (Offer o in offers) o.Button.interactable = Have(o.Give) >= o.GiveAmount;
            TownHall here = TownHall.Viewed, other = here != null ? Caravans.NearestOther(here) : null;
            bool several = other != null;
            if (caravanRow.activeSelf != several)
            {
                caravanRow.SetActive(several);
                panel.sizeDelta = new Vector2(620f, several ? 610f : 520f);
            }
            if (!several) return;
            caravanTitle.text = $"Караван до: {other.Title}";
            for (int i = 0; i < Loads.Length; i++) caravanButtons[i].interactable = here.Stock != null && Caravans.Have(here.Stock, Loads[i].Good) >= Loads[i].Amount;
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("TradeCanvas", 118);
            canvas.transform.SetParent(transform, false);
            window = canvas.gameObject;
            panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 520f));
            UiTheme.PanelImage(panel);
            PopIn.On(panel);
            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(400f, 44f));
            UiTheme.Label(head, "Text", "Ринок", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.Divider(panel, -62f, 400f);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), Close, 28);
            RectTransform st = UiTheme.Rect(panel, "Stock", new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(580f, 30f));
            stock = UiTheme.Label(st, "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Ink);
            stock.supportRichText = true;

            var list = new[]
            {
                (Good.Wood, 10, Good.Food, 5), (Good.Food, 10, Good.Wood, 5),
                (Good.Wood, 10, Good.Ore, 4), (Good.Ore, 5, Good.Wood, 8),
                (Good.Food, 10, Good.Ore, 4), (Good.Ore, 5, Good.Food, 8)
            };
            offers = new Offer[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var (give, giveN, take, takeN) = list[i];
                float y = -116f - i * 60f;
                Button b = UiTheme.TextButton(panel, "Offer" + i, $"{giveN} {Name(give)}  →  {takeN} {Name(take)}", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 50f), () => { Trade(give, giveN, take, takeN); Refresh(); }, 19);
                offers[i] = new Offer { Give = give, GiveAmount = giveN, Take = take, TakeAmount = takeN, Button = b };
            }
            RectTransform row = UiTheme.Rect(panel, "Caravans", new Vector2(0.5f, 1f), new Vector2(0f, -490f), new Vector2(580f, 100f));
            caravanRow = row.gameObject;
            RectTransform rowHead = UiTheme.Rect(row, "Title", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(580f, 30f));
            caravanTitle = UiTheme.Label(rowHead, "Text", "", 18, TextAnchor.MiddleCenter, UiTheme.Gold);
            caravanButtons = new Button[Loads.Length];
            for (int i = 0; i < Loads.Length; i++)
            {
                var load = Loads[i];
                caravanButtons[i] = UiTheme.TextButton(row, "Send" + i, load.Name, new Vector2(0.5f, 1f), new Vector2((i - 1) * 190f, -38f), new Vector2(180f, 50f), () =>
                {
                    TownHall here = TownHall.Viewed;
                    if (here != null) Caravans.Send(here, Caravans.NearestOther(here), load.Good, load.Amount);
                    Refresh();
                }, 18);
            }
            caravanRow.SetActive(false);
            window.SetActive(false);
        }
    }
}
