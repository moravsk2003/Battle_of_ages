using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Resources;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    public enum CaravanGood { Wood, Food, Ore }

    /// <summary>A cart on its way to another settlement with goods off the stock: they arrive with it (M5 step 7).</summary>
    public sealed class Caravan : MonoBehaviour
    {
        private const float ArriveWithin = 8f, Radius = 0.9f;
        private static readonly List<Caravan> all = new List<Caravan>();
        public static IReadOnlyList<Caravan> All => all;

        public TownHall From { get; private set; }
        public TownHall To { get; private set; }
        public CaravanGood Good { get; private set; }
        public int Amount { get; private set; }

        private float deadline, stuck, aside;
        private Vector3 lastAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() => all.Remove(this);

        public static Caravan Create(Vector3 at, TownHall from, TownHall to, CaravanGood good, int amount)
        {
            var go = new GameObject("Caravan");
            at.y = RtsUnit.GetTerrainHeight(at);
            go.transform.position = at;
            var kit = new MeshKit();
            Vector3 woodS = new Vector3(4f, 0f, 0.25f), clothS = new Vector3(1f, 0f, 0.2f);
            Color wood = new Color(0.36f, 0.23f, 0.12f), dark = new Color(0.2f, 0.13f, 0.08f), mule = new Color(0.32f, 0.27f, 0.22f);
            Color load = good == CaravanGood.Wood ? new Color(0.55f, 0.38f, 0.2f) : good == CaravanGood.Food ? new Color(0.8f, 0.68f, 0.35f) : new Color(0.45f, 0.46f, 0.5f);
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 0.95f, -0.8f)), new Vector3(1.3f, 0.35f, 2.2f), 0.04f, woodS, _ => wood);
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 1.4f, -0.8f)), new Vector3(1.05f, 0.6f, 1.8f), 0.08f, clothS, _ => load);
            foreach (float x in new[] { -0.75f, 0.75f })
                kit.Box(Matrix4x4.Translate(new Vector3(x, 0.45f, -0.8f)), new Vector3(0.14f, 0.9f, 0.9f), 0.03f, woodS, _ => dark);
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 0.95f, 1.15f)), new Vector3(0.45f, 0.6f, 1.2f), 0.06f, clothS, _ => mule);
            kit.Box(Matrix4x4.Translate(new Vector3(0f, 1.3f, 1.9f)), new Vector3(0.28f, 0.34f, 0.55f), 0.05f, clothS, _ => mule);
            foreach (float x in new[] { -0.14f, 0.14f })
                foreach (float z in new[] { 0.75f, 1.55f })
                    kit.Box(Matrix4x4.Translate(new Vector3(x, 0.34f, z)), new Vector3(0.12f, 0.68f, 0.12f), 0.02f, clothS, _ => mule);
            go.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("Caravan");
            go.AddComponent<MeshRenderer>().sharedMaterial = MeshKit.PropMaterial();
            Caravan c = go.AddComponent<Caravan>();
            c.From = from;
            c.To = to;
            c.Good = good;
            c.Amount = amount;
            c.lastAt = at;
            Vector3 off = to != null ? to.transform.position - at : Vector3.zero;
            off.y = 0f;
            c.deadline = Time.time + off.magnitude / GameBalance.CaravanSpeed * 3f + 30f;
            return c;
        }

        private void Update()
        {
            TownHall hall = To != null ? To : TownHall.Nearest(transform.position);
            if (hall == null) { Destroy(gameObject); return; }
            Vector3 dir = hall.transform.position - transform.position;
            dir.y = 0f;
            if (dir.magnitude <= ArriveWithin || Time.time > deadline) { Deliver(hall); return; }
            dir.Normalize();

            // A cliff stops it dead (a lake only turns it along the shore): it goes round for a while
            if ((transform.position - lastAt).sqrMagnitude > 0.25f) { lastAt = transform.position; stuck = 0f; }
            else stuck += Time.deltaTime;
            if (stuck > 4f) { stuck = 0f; aside = 5f; }
            if (aside > 0f) { aside -= Time.deltaTime; dir = Quaternion.Euler(0f, 70f, 0f) * dir; }

            GroundMotor.Move(transform, -1, dir * GameBalance.CaravanSpeed, Radius, Time.deltaTime, true);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 4f * Time.deltaTime);
        }

        private void Deliver(TownHall hall)
        {
            SettlementStock stock = hall.Stock;
            if (stock != null)
            {
                if (Good == CaravanGood.Wood) stock.AddResource(ResourceType.Wood, Amount);
                else if (Good == CaravanGood.Food) stock.AddFood(Amount);
                else stock.AddOre(0, Amount);
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Who sends what: a settlement with a market sends what it has to spare to one that runs short, one cart a route at a time; the market's window sends by hand.</summary>
    public static class Caravans
    {
        private static readonly Dictionary<(TownHall, TownHall), double> lastSent = new Dictionary<(TownHall, TownHall), double>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => lastSent.Clear();

        public static int Have(SettlementStock stock, CaravanGood good) =>
            good == CaravanGood.Wood ? stock.WoodCount : good == CaravanGood.Food ? stock.FoodCount : stock.GetOreTierCount(0);

        /// <summary>The other settlement nearest this one, or null.</summary>
        public static TownHall NearestOther(TownHall hall)
        {
            TownHall best = null;
            float bestSqr = float.MaxValue;
            foreach (TownHall h in TownHall.All)
            {
                if (h == hall) continue;
                float d = (h.transform.position - hall.transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = h; }
            }
            return best;
        }

        /// <summary>Takes the goods off the stock of from and sets out; false if it does not have them.</summary>
        public static bool Send(TownHall from, TownHall to, CaravanGood good, int amount)
        {
            if (from == null || to == null || from == to || amount <= 0 || from.Stock == null) return false;
            SettlementStock stock = from.Stock;
            if (Have(stock, good) < amount) return false;
            if (good == CaravanGood.Wood) stock.ConsumeResource(ResourceType.Wood, amount);
            else if (good == CaravanGood.Food) stock.SetFood(stock.Food - amount);
            else stock.ConsumeOre(0, amount);
            Vector3 toward = to.transform.position - from.transform.position;
            toward.y = 0f;
            Caravan.Create(from.transform.position + toward.normalized * 8f, from, to, good, amount);
            lastSent[(from, to)] = WorldClock.GameHours;
            return true;
        }

        /// <summary>What to send from a to b, if a has it to spare and b runs short: food first, then wood, then ore.</summary>
        public static bool Needed(TownHall a, TownHall b, out CaravanGood good, out int amount)
        {
            SettlementStock from = a.Stock, to = b.Stock;
            good = CaravanGood.Food;
            amount = 0;
            if (from == null || to == null) return false;
            float toPerDay = Mathf.Max(1, b.People) * GameBalance.FoodPerPersonPerDay, fromPerDay = Mathf.Max(1, a.People) * GameBalance.FoodPerPersonPerDay;
            if (to.Food < toPerDay * GameBalance.CaravanFoodShortDays)
            {
                amount = Mathf.Min(GameBalance.CaravanLoadFood, Mathf.FloorToInt(from.Food - fromPerDay * GameBalance.CaravanFoodSpareDays));
                if (amount > 0) return true;
            }
            if (to.WoodCount < GameBalance.CaravanWoodShort)
            {
                good = CaravanGood.Wood;
                amount = Mathf.Min(GameBalance.CaravanLoadWood, from.WoodCount - GameBalance.CaravanWoodSpare);
                if (amount > 0) return true;
            }
            if (to.GetOreTierCount(0) < GameBalance.CaravanOreShort)
            {
                good = CaravanGood.Ore;
                amount = Mathf.Min(GameBalance.CaravanLoadOre, from.GetOreTierCount(0) - GameBalance.CaravanOreSpare);
                if (amount > 0) return true;
            }
            return false;
        }

        /// <summary>Every settlement with a market looks over the others (the zone grower's clock).</summary>
        public static void Tick()
        {
            var halls = TownHall.All;
            if (halls.Count < 2) return;
            foreach (TownHall a in halls)
            {
                if (!MarketNode.In(a)) continue;
                foreach (TownHall b in halls)
                {
                    if (a == b) continue;
                    if (lastSent.TryGetValue((a, b), out double at) && WorldClock.GameHours - at < GameBalance.CaravanEveryHours) continue;
                    bool onTheRoad = false;
                    foreach (Caravan c in Caravan.All) if (c.From == a && c.To == b) onTheRoad = true;
                    if (!onTheRoad && Needed(a, b, out CaravanGood good, out int amount)) Send(a, b, good, amount);
                }
            }
        }
    }
}
