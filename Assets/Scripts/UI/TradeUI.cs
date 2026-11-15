using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Resources;
using TMPro;

namespace BattleOfAges.UI
{
    /// <summary>The market's window (M5 step 4, a click on a market): fixed exchanges of the settlement's wood, food and common ore, at a loss, for when one runs short and another piles up.</summary>
    public sealed class TradeUI : MonoBehaviour
    {
        public enum Good { Wood, Food, Ore }

        /// <summary>
        /// The fixed exchanges. No offer gives more of a good for an hour of a worker's other job than a worker of that good
        /// makes itself (MarketTests): 10 wood for 5 food was 25 times a farmer's yield, and with the plenty of wood it replaced
        /// fields and mines.
        /// </summary>
        public static readonly (Good give, int giveAmount, Good take, int takeAmount)[] Offers =
        {
            (Good.Wood, 100, Good.Food, 1), (Good.Food, 10, Good.Wood, 5),
            (Good.Wood, 10, Good.Ore, 4), (Good.Ore, 5, Good.Wood, 1),
            (Good.Food, 10, Good.Ore, 4), (Good.Ore, 240, Good.Food, 1)
        };

        private struct Offer { public Good Give, Take; public int GiveAmount, TakeAmount; public Button Button; }

        private static TradeUI instance;
        private GameObject window;
        private Offer[] offers;
        private TMP_Text stock, caravanTitle;
        private RectTransform panel;
        private GameObject caravanRow;
        private Button[] caravanButtons;
        private static readonly (CaravanGood Good, int Amount)[] Loads =
        {
            (CaravanGood.Wood, 100), (CaravanGood.Food, 40), (CaravanGood.Ore, 40)
        };

        public static bool IsOpen => instance != null && instance.window != null && instance.window.activeSelf;
        /// <summary>What the last exchange made here gave, or why it gave nothing (the window's bottom line; tests).</summary>
        public static string LastSaid { get; private set; } = "";
        private TMP_Text said;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => LastSaid = "";

        public static void Open()
        {
            if (instance == null)
            {
                instance = new GameObject("TradeUI").AddComponent<TradeUI>();
                instance.Build();
            }
            instance.window.SetActive(true);
            PopIn.On(instance.panel); // pressed while open: it answers all the same
            instance.Refresh();
        }

        private void Close() => window.SetActive(false);

        private static string Name(Good g) => g == Good.Wood ? Loc.Pick("дерева", "wood") : g == Good.Food ? Loc.Pick("їжі", "food") : Loc.Pick("руди", "ore");

        private static string ButtonGoodName(Good g) => g == Good.Wood ? Loc.Pick("дерева", "wood") : g == Good.Food ? Loc.Pick("їжі", "food") : Loc.Pick("руди (0)", "ore (0)");

        private static string CaravanName(CaravanGood g) => g == CaravanGood.Wood ? Loc.Pick("дерева", "wood") : g == CaravanGood.Food ? Loc.Pick("їжі", "food") : Loc.Pick("руди (0)", "ore (0)");

        /// <summary>The stock the window trades from: the settlement being looked at.</summary>
        private static SettlementStock Here => ResourceManager.Instance != null ? ResourceManager.Instance.Viewed : null;

        private static int Have(Good g) => Have(Here, g);

        private static int Have(SettlementStock stock, Good g)
        {
            if (stock == null) return 0;
            return g == Good.Wood ? stock.WoodCount : g == Good.Food ? stock.FoodCount : stock.GetOreTierCount(0);
        }

        /// <summary>Makes one exchange from the settlement being looked at, if its stock has it; false otherwise.</summary>
        public static bool Trade(Good give, int giveAmount, Good take, int takeAmount) => Trade(Here, give, giveAmount, take, takeAmount, true);

        /// <summary>
        /// One exchange from this stock (a market's own settlement: TradeCommand carries its hall, so a guest's trade on
        /// the host takes from the guest's settlement, not from the one the host looks at). Says: the window's bottom line.
        /// </summary>
        public static bool Trade(SettlementStock stock, Good give, int giveAmount, Good take, int takeAmount, bool mine)
        {
            if (stock == null) return false;
            if (Have(stock, give) < giveAmount)
            {
                if (mine) LastSaid = Loc.Pick($"Не вистачає {Name(give)}: треба {giveAmount}", $"Not enough {Name(give)}: {giveAmount} needed");
                return false;
            }
            if (give == Good.Wood) stock.ConsumeResource(ResourceType.Wood, giveAmount);
            else if (give == Good.Food) stock.SetFood(stock.Food - giveAmount);
            else stock.ConsumeOre(0, giveAmount);
            if (take == Good.Wood) stock.AddResource(ResourceType.Wood, takeAmount);
            else if (take == Good.Food) stock.AddFood(takeAmount);
            else stock.AddOre(0, takeAmount);
            if (mine)
            {
                LastSaid = Loc.Pick($"Обміняно: {giveAmount} {Name(give)} → +{takeAmount} {Name(take)}", $"Traded: {giveAmount} {Name(give)} → +{takeAmount} {Name(take)}");
                GameSound.Play2D(GameSound.Kind.Coins);
            }
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
            said.text = LastSaid;
            stock.text = Loc.Pick(
                $"Є: дерева <color=#F5CC73>{Have(Good.Wood)}</color>  ·  їжі <color=#F5CC73>{Have(Good.Food)}</color>  ·  звичайної руди <color=#F5CC73>{Have(Good.Ore)}</color>",
                $"Stock: wood <color=#F5CC73>{Have(Good.Wood)}</color>  ·  food <color=#F5CC73>{Have(Good.Food)}</color>  ·  common ore <color=#F5CC73>{Have(Good.Ore)}</color>");
            foreach (Offer o in offers)
            {
                o.Button.interactable = Have(o.Give) >= o.GiveAmount;
                TMP_Text bt = o.Button.GetComponentInChildren<TMP_Text>();
                if (bt != null) bt.text = $"{o.GiveAmount} {ButtonGoodName(o.Give)}  →  {o.TakeAmount} {ButtonGoodName(o.Take)}";
            }
            TownHall here = TownHall.Viewed, other = here != null ? Caravans.NearestOther(here) : null;
            bool several = other != null;
            if (caravanRow.activeSelf != several)
            {
                caravanRow.SetActive(several);
                panel.sizeDelta = new Vector2(620f, several ? 636f : 546f);
            }
            if (!several) return;
            caravanTitle.text = Loc.Pick($"Караван до: {other.Title}", $"Caravan to: {other.Title}");
            for (int i = 0; i < Loads.Length; i++)
            {
                caravanButtons[i].interactable = here.Stock != null && Caravans.Have(here.Stock, Loads[i].Good) >= Loads[i].Amount;
                TMP_Text bt = caravanButtons[i].GetComponentInChildren<TMP_Text>();
                if (bt != null) bt.text = $"{Loads[i].Amount} {CaravanName(Loads[i].Good)}";
            }
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("TradeCanvas", 118);
            canvas.transform.SetParent(transform, false);
            window = canvas.gameObject;
            panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 546f));
            UiTheme.PanelImage(panel);
            PopIn.On(panel);
            RectTransform head = UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(400f, 44f));
            UiTheme.Label(head, "Text", "Ринок", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.Divider(panel, -62f, 400f);
            UiTheme.TextButton(panel, "Close", "×", new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(46f, 44f), Close, 28);
            RectTransform st = UiTheme.Rect(panel, "Stock", new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(580f, 30f));
            stock = UiTheme.Label(st, "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Ink);
            LocText.Detach(stock);
            stock.richText = true;

            var list = Offers;
            offers = new Offer[list.Length];
            for (int i = 0; i < list.Length; i++)
            {
                var (give, giveN, take, takeN) = list[i];
                float y = -116f - i * 60f;
                Button b = UiTheme.TextButton(panel, "Offer" + i, $"{giveN} {ButtonGoodName(give)}  →  {takeN} {ButtonGoodName(take)}", new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(460f, 50f), () =>
                {
                    TownHall hall = TownHall.Viewed;
                    Net.CommandBus.Submit(new Net.TradeCommand { Hall = hall != null ? Net.WorldEntity.IdOf(hall).Value : 0, Give = give, GiveAmount = giveN, Take = take, TakeAmount = takeN });
                    Refresh();
                }, 19);
                LocText.Detach(b.GetComponentInChildren<TMP_Text>());
                Tooltip.Set(b.gameObject, () => OfferTip(give, giveN, take, takeN));
                offers[i] = new Offer { Give = give, GiveAmount = giveN, Take = take, TakeAmount = takeN, Button = b };
            }
            // What the last exchange gave (else a click on a dear offer, 100 wood for 1 food, looked like nothing)
            RectTransform saidRect = UiTheme.Rect(panel, "Said", new Vector2(0.5f, 1f), new Vector2(0f, -116f - list.Length * 60f + 4f), new Vector2(580f, 26f));
            said = UiTheme.Label(saidRect, "Text", "", 16, TextAnchor.MiddleCenter, UiTheme.Gold);
            LocText.Detach(said);
            RectTransform row = UiTheme.Rect(panel, "Caravans", new Vector2(0.5f, 1f), new Vector2(0f, -490f - 26f), new Vector2(580f, 100f));
            caravanRow = row.gameObject;
            RectTransform rowHead = UiTheme.Rect(row, "Title", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(580f, 30f));
            caravanTitle = UiTheme.Label(rowHead, "Text", "", 18, TextAnchor.MiddleCenter, UiTheme.Gold);
            LocText.Detach(caravanTitle);
            caravanButtons = new Button[Loads.Length];
            for (int i = 0; i < Loads.Length; i++)
            {
                var load = Loads[i];
                caravanButtons[i] = UiTheme.TextButton(row, "Send" + i, $"{load.Amount} {CaravanName(load.Good)}", new Vector2(0.5f, 1f), new Vector2((i - 1) * 190f, -38f), new Vector2(180f, 50f), () =>
                {
                    TownHall here = TownHall.Viewed;
                    if (here != null) Caravans.Send(here, Caravans.NearestOther(here), load.Good, load.Amount);
                    Refresh();
                }, 18);
                LocText.Detach(caravanButtons[i].GetComponentInChildren<TMP_Text>());
                Tooltip.Set(caravanButtons[i].gameObject, () => CaravanTip(load.Good, load.Amount));
            }
            caravanRow.SetActive(false);
            window.SetActive(false);
        }

        private static string OfferTip(Good give, int giveN, Good take, int takeN)
        {
            int haveGive = Have(give), haveTake = Have(take);
            string status = haveGive >= giveN
                ? TipText.Good(Loc.Pick($"У поселенні є: {haveGive} {ButtonGoodName(give)}", $"Settlement stock: {haveGive} {ButtonGoodName(give)}"))
                : TipText.Bad(Loc.Pick($"Бракує: є {haveGive} з {giveN} {ButtonGoodName(give)}", $"Not enough: have {haveGive} of {giveN} {ButtonGoodName(give)}"));
            string oreNote = (give == Good.Ore || take == Good.Ore)
                ? "\n" + TipText.Dim(Loc.Pick("Ринок приймає й видає лише звичайну руду рівня 0.", "The market only trades common (tier 0) ore."))
                : "";
            return TipText.Title(Loc.Pick($"Обмін: {giveN} {ButtonGoodName(give)} → {takeN} {ButtonGoodName(take)}", $"Trade: {giveN} {ButtonGoodName(give)} → {takeN} {ButtonGoodName(take)}"), UiTheme.Gold)
                + "\n" + status
                + Loc.Pick($"\nПісля обміну на складі буде: {haveTake + takeN} {ButtonGoodName(take)}", $"\nStock after trade: {haveTake + takeN} {ButtonGoodName(take)}")
                + oreNote
                + "\n" + TipText.Dim(Loc.Pick("Клік — обміняти зі складу цього поселення.", "Click — trade from this settlement's stock."));
        }

        private static string CaravanTip(CaravanGood good, int amount)
        {
            TownHall here = TownHall.Viewed, other = here != null ? Caravans.NearestOther(here) : null;
            int have = here != null && here.Stock != null ? Caravans.Have(here.Stock, good) : 0;
            string status = have >= amount
                ? TipText.Good(Loc.Pick($"На складі є: {have} {CaravanName(good)}", $"In stock: {have} {CaravanName(good)}"))
                : TipText.Bad(Loc.Pick($"Бракує: є {have} з {amount} {CaravanName(good)}", $"Not enough: have {have} of {amount} {CaravanName(good)}"));
            string targetName = other != null ? other.Title : Loc.Pick("інше поселення", "another settlement");
            return TipText.Title(Loc.Pick($"Караван: {amount} {CaravanName(good)} → {targetName}", $"Caravan: {amount} {CaravanName(good)} → {targetName}"), UiTheme.Gold)
                + "\n" + status
                + Loc.Pick($"\nВезтиме {amount} {CaravanName(good)} зі швидкістю {GameBalance.CaravanSpeed:0.#} м/с.", $"\nSends {amount} {CaravanName(good)} at {GameBalance.CaravanSpeed:0.#} m/s.")
                + (good == CaravanGood.Ore ? "\n" + TipText.Dim(Loc.Pick("Перевозить лише звичайну руду рівня 0.", "Transports only common (tier 0) ore.")) : "")
                + "\n" + TipText.Dim(Loc.Pick($"Поселення з ринком також самі шлють каравани що-{GameBalance.CaravanEveryHours:0} год гри, коли тут надлишок, а там нестача.", $"Settlements with a market also send automatic caravans every {GameBalance.CaravanEveryHours:0} game hour(s) when surplus meets shortage."));
        }
    }
}
