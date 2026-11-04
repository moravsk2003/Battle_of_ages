using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.UI;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.AI
{
    /// <summary>
    /// The early threat of a campaign (docs/GAME_DESIGN.md §18.1, 0.2.1): bandits come for the player's settlement from
    /// the first day, a bigger band each time, long before a neighbouring king can (its nearest is kilometres away). A
    /// band appears a little way off, the HUD says from where, and it goes for the hall (or the hero) and whatever of
    /// the player's is near. Only in a bounded world: the fixed seed-0 world keeps M0's rules.
    /// </summary>
    public sealed class BanditRaids : MonoBehaviour
    {
        /// <summary>Game hour of the next band and how many came so far (kept in a save).</summary>
        public static double NextRaidHour { get; set; } = GameBalance.BanditFirstRaidHours;
        public static int RaidCount { get; set; }

        private static readonly string[] Sides = { "зі сходу", "з півночі", "із заходу", "з півдня" };
        private readonly List<HostileMobAI> band = new List<HostileMobAI>();
        private float secondClock;

        public int BandAlive { get { band.RemoveAll(m => m == null || m.Health == null || !m.Health.IsAlive); return band.Count; } }

        /// <summary>How many bandits come in this raid (0: the first).</summary>
        public static int BandSize(int raid) => Mathf.Min(GameBalance.BanditRaidMax, GameBalance.BanditRaidFirst + raid * GameBalance.BanditRaidGrowth);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reset();

        /// <summary>A new campaign: the first band comes at the first raid hour.</summary>
        public static void Reset()
        {
            NextRaidHour = GameBalance.BanditFirstRaidHours;
            RaidCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<BanditRaids>() != null) return;
            var go = new GameObject("BanditRaids");
            DontDestroyOnLoad(go);
            go.AddComponent<BanditRaids>();
        }

        private void Update()
        {
            if (!WorldSettings.Bounded || !SaveLoad.SaveSystem.InGame) return;
            if (WorldClock.GameHours >= NextRaidHour && BandAlive == 0) Send();
            secondClock += Time.deltaTime;
            if (secondClock < 1f) return;
            secondClock = 0f;
            Lead();
        }

        private static Vector3 Settlement()
        {
            if (TownHall.Current != null) return TownHall.Current.transform.position;
            GameObject p = Managers.GameContext.Player;
            return p != null ? p.transform.position : Vector3.zero;
        }

        /// <summary>What the band is after: the hall, or the hero while there is none.</summary>
        private static HealthComponent MainTarget()
        {
            HealthComponent hall = TownHall.Current != null ? TownHall.Current.GetComponent<HealthComponent>() : null;
            if (hall != null && hall.IsAlive) return hall;
            GameObject p = Managers.GameContext.Player;
            HealthComponent hero = p != null ? p.GetComponent<HealthComponent>() : null;
            return hero != null && hero.IsAlive ? hero : null;
        }

        /// <summary>A band comes: on dry land inside the world, some way from the settlement, in the direction it is told about.</summary>
        public void Send()
        {
            HealthComponent target = MainTarget();
            NextRaidHour = WorldClock.GameHours + GameBalance.BanditRaidEveryHours;
            if (target == null) return;
            Vector3 home = Settlement();
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector3 at = home;
            for (int tries = 0; tries < 12; tries++, a += 0.7f)
            {
                at = WorldSettings.ClampInside(home + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * GameBalance.BanditSpawnDistance, 40f);
                if (!Lakes.InWater(at.x, at.z, 15f)) break;
            }
            int size = BandSize(RaidCount);
            RaidCount++;
            for (int i = 0; i < size; i++)
            {
                Vector2 r = Random.insideUnitCircle * 6f;
                Vector3 p = at + new Vector3(r.x, 0f, r.y);
                p.y = RtsUnit.GetTerrainHeight(p);
                GameObject mob = HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "RaidBandit_" + i);
                CharacterController cc = mob.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                mob.transform.position = p;
                if (cc != null) cc.enabled = true;
                HostileMobAI ai = mob.GetComponent<HostileMobAI>();
                band.Add(ai);
                ai.OrderAttack(target);
            }
            int side = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(at.z - home.z, at.x - home.x), Mathf.PI * 2f) / (Mathf.PI * 0.5f)) % 4;
            StrategyHud.Notify($"Бандити йдуть на поселення: {size}, {Sides[side]}!");
            UiSound.Play(UiSound.Kind.Alert);
        }

        /// <summary>Once a second: everyone of the band with nothing to hit goes for what is nearest, or the main target.</summary>
        private void Lead()
        {
            if (BandAlive == 0) return;
            HealthComponent main = MainTarget();
            foreach (HostileMobAI m in band)
            {
                if (m == null) continue;
                m.EngageNear(GameBalance.RaidEngageRadius);
                HealthComponent t = m.CurrentCombatTarget;
                if (t != null && t.IsAlive) continue;
                int near = UnitSpatialGrid.FindNearestEnemy(m.transform.position, GameBalance.KingdomRaidSearchRadius, UnitSpatialGrid.TeamHostile);
                HealthComponent next = near >= 0 ? UnitSpatialGrid.Health(near) : main;
                if (next != null) m.OrderAttack(next);
            }
        }
    }
}
