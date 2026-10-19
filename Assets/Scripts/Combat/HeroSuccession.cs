using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Crafting;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// What happens when the player's hero dies (docs/GAME_DESIGN.md §7.2):
    /// 1. its artifacts fall where it died (HeroDied); 2. an interregnum of 90 s begins; 3. about half of the units desert
    /// and become deserter bands (hostile, not dead; units far from the town hall go first); 4. after the interregnum the
    /// strongest unit left (highest rank, then battle experience, then HP) becomes the new hero: x2 of its rank, level 1,
    /// the class chosen anew, its gear; 5. with no units left the game is over (single player: load a save).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroSuccession : MonoBehaviour
    {
        /// <summary>The hero fell here (artifacts drop, UI shows the interregnum).</summary>
        public static event Action<HeroSuccession, Vector3> HeroDied;
        /// <summary>A unit took over as the new hero.</summary>
        public static event Action<HeroSuccession> NewHero;
        /// <summary>No units were left to take over: the game is over.</summary>
        public static event Action<HeroSuccession> GameOver;

        public bool InInterregnum => interregnumLeft > 0f;
        public float InterregnumLeft => Mathf.Max(0f, interregnumLeft);
        public bool IsGameOver { get; private set; }

        private static readonly List<HeroSuccession> all = new List<HeroSuccession>();
        /// <summary>Heroes on the map (the player's; NPC heroes later).</summary>
        public static IReadOnlyList<HeroSuccession> All => all;

        private HealthComponent health;
        private float interregnumLeft;

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() => all.Remove(this);

        private void Awake()
        {
            health = GetComponent<HealthComponent>();
            if (health != null) health.OnDeath += OnHeroDeath;
        }

        private void OnDestroy()
        {
            if (health != null) health.OnDeath -= OnHeroDeath;
        }

        private void OnHeroDeath()
        {
            if (InInterregnum || IsGameOver) return;
            interregnumLeft = GameBalance.InterregnumSeconds;
            SetControl(false);
            HeroDied?.Invoke(this, transform.position);
            Desert(OwnUnits(), TownHallPosition());
        }

        private void Update()
        {
            if (!InInterregnum) return;
            interregnumLeft -= Time.deltaTime;
            if (interregnumLeft <= 0f) EndInterregnum();
        }

        /// <summary>Ends the interregnum now: the strongest unit takes over, or the game is over.</summary>
        public void EndInterregnum()
        {
            interregnumLeft = 0f;
            RtsUnit heir = Strongest(OwnUnits());
            if (heir == null)
            {
                IsGameOver = true;
                GameOver?.Invoke(this);
                return;
            }
            TakeOver(heir);
            NewHero?.Invoke(this);
        }

        // ------------------------------------------------------------------ the steps

        /// <summary>Own living units (not the hero).</summary>
        public static List<RtsUnit> OwnUnits()
        {
            var list = new List<RtsUnit>();
            foreach (RtsUnit u in UnitRegistry.ActiveUnits)
            {
                if (u == null || u.GetComponent<HeroSuccession>() != null) continue;
                HealthComponent hp = u.GetComponent<HealthComponent>();
                if (hp != null && !hp.IsAlive) continue;
                list.Add(u);
            }
            return list;
        }

        /// <summary>
        /// About half of the units desert (rounded down); the ones farthest from the town hall go first, the garrison
        /// by the hall stays longest. Returns the deserter bands' members.
        /// </summary>
        public static List<GameObject> Desert(List<RtsUnit> units, Vector3? hall)
        {
            int count = Mathf.FloorToInt(units.Count * GameBalance.DesertionShare);
            if (hall.HasValue)
            {
                // Farther first, with a little shuffle so it is not always the same ring (keys drawn once: a stable sort)
                var key = new Dictionary<RtsUnit, float>();
                foreach (RtsUnit u in units) key[u] = Flat(u.transform.position, hall.Value) + UnityEngine.Random.value * 8f;
                units.Sort((a, b) => key[b].CompareTo(key[a]));
            }
            else Shuffle(units);

            var deserters = new List<GameObject>();
            for (int i = 0; i < count && i < units.Count; i++) deserters.Add(MakeDeserter(units[i]));
            return deserters;
        }

        /// <summary>Highest rank, then battle experience, then current HP.</summary>
        public static RtsUnit Strongest(List<RtsUnit> units)
        {
            RtsUnit best = null;
            long bestScore = -1;
            foreach (RtsUnit u in units)
            {
                if (u == null) continue;
                UnitProgress p = u.GetComponent<UnitProgress>();
                HealthComponent hp = u.GetComponent<HealthComponent>();
                long score = (long)(p != null ? (int)p.Rank : 0) * 1_000_000_000L
                           + (long)(p != null ? p.BattleXp : 0) * 100_000L
                           + (hp != null ? hp.CurrentHealth : 0);
                if (score > bestScore) { bestScore = score; best = u; }
            }
            return best;
        }

        /// <summary>The unit's body becomes the hero: its place, rank (x2 as a hero), gear; level 1, no class yet.</summary>
        private void TakeOver(RtsUnit heir)
        {
            UnitProgress heirRank = heir.GetComponent<UnitProgress>();
            UnitRank rank = heirRank != null ? heirRank.Rank : UnitRank.Person;
            Equipment heirGear = heir.GetComponent<Equipment>();
            Item[] gear = heirGear != null ? heirGear.SlotsCopy() : new Item[6];
            Vector3 pos = heir.transform.position;
            Quaternion rot = heir.transform.rotation;
            heir.gameObject.SetActive(false);
            Destroy(heir.gameObject);

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            if (cc != null) cc.enabled = true;

            Equipment myGear = GetComponent<Equipment>();
            if (myGear != null) myGear.LoadSlots(gear);
            UnitProgress.Setup(gameObject, rank, hero: true);
            HeroProgress hero = GetComponent<HeroProgress>();
            if (hero != null) hero.ResetForNewHero();
            HeroSkills skills = GetComponent<HeroSkills>();
            if (skills != null) skills.Restore(false, 0, 0, 0, false, false, 0);
            if (health != null) health.InitializeHealth(health.MaxHealth);
            SetControl(true);
        }

        /// <summary>A unit leaves the kingdom as a deserter: a hostile band member of the same rank, with its gear.</summary>
        public static GameObject MakeDeserter(RtsUnit unit)
        {
            UnitProgress p = unit.GetComponent<UnitProgress>();
            UnitRank rank = p != null ? p.Rank : UnitRank.Person;
            Equipment gear = unit.GetComponent<Equipment>();
            Item[] slots = gear != null ? gear.SlotsCopy() : new Item[6];
            Vector3 pos = unit.transform.position;
            unit.gameObject.SetActive(false);
            Destroy(unit.gameObject);

            GameObject mob = HostileCampBuilder.CreateHostileMob(HostileMobType.Bandit, "Deserter");
            CharacterController cc = mob.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            mob.transform.position = pos;
            if (cc != null) cc.enabled = true;
            mob.GetComponent<Equipment>()?.LoadSlots(slots);
            UnitProgress.Setup(mob, rank);
            HealthComponent hp = mob.GetComponent<HealthComponent>();
            if (hp != null) hp.InitializeHealth(hp.MaxHealth);
            mob.GetComponent<HostileMobAI>()?.SetGuardOrigin(pos);
            return mob;
        }

        private void SetControl(bool on)
        {
            foreach (MonoBehaviour b in GetComponents<MonoBehaviour>())
                if (b is Player.FirstPersonController) b.enabled = on;
        }

        private static Vector3? TownHallPosition()
        {
            var hall = UnityEngine.Object.FindFirstObjectByType<Building.TownHall>();
            return hall != null ? hall.transform.position : (Vector3?)null;
        }

        private static float Flat(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        private static void Shuffle(List<RtsUnit> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
