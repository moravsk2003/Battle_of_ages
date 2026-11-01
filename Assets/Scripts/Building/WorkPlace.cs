using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.Building
{
    /// <summary>
    /// A building workers stand at to produce (M5 step 4): a field gives food, a sawmill wood, straight into the stock.
    /// Workers on their own come while the stock is short (WantsHands) and leave once there is enough (Wanted); any worker
    /// can be sent with a right click and then stays until told otherwise.
    /// </summary>
    public abstract class WorkPlace : MonoBehaviour
    {
        private const float Reach = 2.2f;
        private static readonly List<WorkPlace> all = new List<WorkPlace>();
        public static IReadOnlyList<WorkPlace> All => all;

        private readonly List<NpcResourceGatherer> workers = new List<NpcResourceGatherer>(4);
        public int WorkerCount => workers.Count;
        public int Present { get; private set; }
        public abstract int Slots { get; }
        /// <summary>The stock is short: workers on their own come.</summary>
        public abstract bool WantsHands { get; }
        /// <summary>Still worth standing here (they leave once there is plenty).</summary>
        public abstract bool Wanted { get; }
        public abstract string JobName { get; }
        protected abstract void Yield(int present, float hours);
        protected abstract float SpotRadius { get; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }

        private void OnDisable()
        {
            all.Remove(this);
            foreach (NpcResourceGatherer w in new List<NpcResourceGatherer>(workers)) if (w != null) w.LeavePlace(this);
            workers.Clear();
        }

        /// <summary>The nearest place in reach that wants hands, or null.</summary>
        public static WorkPlace NearestWanting(Vector3 from, float reach)
        {
            WorkPlace best = null;
            float bestSqr = reach * reach;
            foreach (WorkPlace p in all)
            {
                if (p == null || p.workers.Count >= p.Slots || !p.WantsHands) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }
            return best;
        }

        public bool Join(NpcResourceGatherer worker)
        {
            if (worker == null) return false;
            if (workers.Contains(worker)) return true;
            if (workers.Count >= Slots) return false;
            workers.Add(worker);
            return true;
        }

        public void Leave(NpcResourceGatherer worker) => workers.Remove(worker);

        public Vector3 SpotFor(NpcResourceGatherer worker)
        {
            int i = Mathf.Max(0, workers.IndexOf(worker));
            float a = i * Mathf.PI * 2f / Mathf.Max(1, Slots) + 0.4f;
            Vector3 p = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * SpotRadius;
            p.y = RtsUnit.GetTerrainHeight(p);
            return p;
        }

        private void Update()
        {
            workers.RemoveAll(w => w == null);
            int present = 0;
            foreach (NpcResourceGatherer w in workers)
            {
                Vector3 off = w.transform.position - SpotFor(w);
                off.y = 0f;
                if (off.sqrMagnitude <= Reach * Reach) present++;
            }
            Present = present;
            if (present > 0) Yield(present, WorldClock.HoursOf(Time.deltaTime));
        }

        /// <summary>This many workers' work over these game hours (the tests call it directly).</summary>
        public void Produce(int present, float hours) => Yield(present, hours);
    }

    /// <summary>A field (zone "Поля"): each farmer brings GameBalance.FarmerFoodPerHour, enough for a dozen people.</summary>
    public sealed class FieldNode : WorkPlace
    {
        public override int Slots => GameBalance.FieldFarmers;
        public override string JobName => "працює в полі";
        protected override float SpotRadius => 5f;

        private static float FoodDays()
        {
            ResourceManager rm = ResourceManager.Instance;
            float perDay = TownHall.Population * GameBalance.FoodPerPersonPerDay;
            return rm == null || perDay <= 0f ? float.MaxValue : rm.Food / perDay;
        }

        public override bool WantsHands => FoodDays() < GameBalance.FoodWantDays;
        public override bool Wanted => FoodDays() < GameBalance.FoodEnoughDays;

        protected override void Yield(int present, float hours) => ResourceManager.Instance?.AddFood(present * GameBalance.FarmerFoodPerHour * hours);
    }

    /// <summary>A sawmill (zone "Ліс"): each sawyer brings wood per hour, more in dense forest (the coarse map).</summary>
    public sealed class SawmillNode : WorkPlace
    {
        private float carry;
        private float forest = -1f;
        public override int Slots => GameBalance.SawmillWorkers;
        public override string JobName => "працює на лісопилці";
        protected override float SpotRadius => 4f;

        private static int Wood() => ResourceManager.Instance != null ? ResourceManager.Instance.WoodCount : int.MaxValue;
        public override bool WantsHands => Wood() < GameBalance.SawmillWantWood;
        public override bool Wanted => Wood() < GameBalance.SawmillEnoughWood;

        /// <summary>The forest here, 0..1, makes the rate 0.5..1.5 of the base.</summary>
        public float ForestFactor
        {
            get
            {
                if (forest < 0f)
                {
                    CoarseWorldMap map = CoarseWorldMap.Current;
                    forest = map.TryCell(transform.position, out int x, out int z) ? map.Forest(x, z) : 0.25f;
                }
                return 0.5f + forest;
            }
        }

        protected override void Yield(int present, float hours)
        {
            carry += present * GameBalance.SawyerWoodPerHour * ForestFactor * hours;
            int whole = Mathf.FloorToInt(carry);
            if (whole <= 0) return;
            carry -= whole;
            ResourceManager.Instance?.AddResource(ResourceType.Wood, whole);
        }
    }

    /// <summary>A market (zone "Торгівля", hall II+): a click opens the exchange (TradeUI).</summary>
    public sealed class MarketNode : MonoBehaviour
    {
        private static readonly List<MarketNode> all = new List<MarketNode>();
        public static bool Any => all.Count > 0;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => all.Clear();
        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() => all.Remove(this);
    }
}
