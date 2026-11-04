using NUnit.Framework;
using BattleOfAges.Combat;
using BattleOfAges.UI;

namespace BattleOfAges.EditModeTests
{
    /// <summary>The market does not replace the jobs: what a worker's hour buys there is less than what a worker of that good makes.</summary>
    public class MarketTests
    {
        private static float MadePerHour(TradeUI.Good g) =>
            g == TradeUI.Good.Wood ? GameBalance.SawyerWoodPerHour
            : g == TradeUI.Good.Food ? GameBalance.FarmerFoodPerHour
            : GameBalance.GameHourSeconds; // a miner digs 1 ore a second (Як працює шахта)

        [Test]
        public void NoOffer_BeatsDoingTheJobItself()
        {
            foreach (var (give, giveAmount, take, takeAmount) in TradeUI.Offers)
            {
                float bought = MadePerHour(give) / giveAmount * takeAmount;
                Assert.Less(bought, MadePerHour(take), $"{giveAmount} {give} for {takeAmount} {take}");
            }
        }

        [Test]
        public void GoingRound_LosesGoods()
        {
            foreach (var a in TradeUI.Offers)
                foreach (var b in TradeUI.Offers)
                {
                    if (a.take != b.give || b.take != a.give) continue;
                    float back = (float)a.takeAmount / a.giveAmount * b.takeAmount / b.giveAmount;
                    Assert.Less(back, 1f, $"{a.give} to {a.take} and back");
                }
        }
    }
}
