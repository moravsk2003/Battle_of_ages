using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.RTS;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// A hero's artifacts (docs/GAME_DESIGN.md §9): one at the start, any number later, effects add up. The sword and the
    /// armour take their gear slot (their numbers replace the worn piece); the rest work from the inventory. One-shot
    /// artifacts are used up. On the hero's death every other artifact falls where it died (ArtifactPickup) and waits
    /// for the new hero; one nobody may pick up any more turns into a shard, and any two shards make one of their two
    /// artifacts (50/50). A second world-changing artifact blows its bearer up.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HeroArtifacts : MonoBehaviour
    {
        [SerializeField] private List<ArtifactType> held = new List<ArtifactType>();
        [SerializeField] private List<ArtifactType> shards = new List<ArtifactType>();
        [SerializeField] private bool startChosen;

        public IReadOnlyList<ArtifactType> Held => held;
        public IReadOnlyList<ArtifactType> Shards => shards;
        /// <summary>The starting artifact has been picked (once, with the class).</summary>
        public bool StartChosen => startChosen;

        public event Action<HeroArtifacts> Changed;

        public bool Has(ArtifactType a) => held.Contains(a);
        public float LifeSteal => Has(ArtifactType.Vampiric) ? GameBalance.VampiricShare : 0f;
        public float XpBonus => Has(ArtifactType.Wisdom) ? GameBalance.WisdomXpBonus : 0f;

        /// <summary>The weapon multiplier with the artifact sword taking the weapon slot.</summary>
        public float WeaponMultiplier(float worn) => Has(ArtifactType.SwordX5) ? GameBalance.SwordArtifactMultiplier : worn;
        /// <summary>The damage reduction with the artifact armour taking the armour slot.</summary>
        public float DamageReduction(float worn) => Has(ArtifactType.Armor90) ? GameBalance.ArmorArtifactReduction : worn;

        private void OnEnable() => HeroSuccession.HeroDied += OnHeroDied;
        private void OnDisable() => HeroSuccession.HeroDied -= OnHeroDied;

        /// <summary>The starting artifact (once).</summary>
        public bool ChooseStart(ArtifactType a)
        {
            if (startChosen) return false;
            startChosen = true;
            Add(a);
            return true;
        }

        /// <summary>Takes an artifact; a second world-changing one blows the bearer up (§9.2).</summary>
        public void Add(ArtifactType a)
        {
            bool secondWorld = Artifacts.Tier(a) == ArtifactTier.WorldChanging && held.Exists(h => Artifacts.Tier(h) == ArtifactTier.WorldChanging);
            held.Add(a);
            ApplyEffects();
            Changed?.Invoke(this);
            if (secondWorld)
            {
                UI.StrategyHud.Notify("Два світозмінні артефакти — герой вибухнув!");
                GetComponent<HealthComponent>()?.TakeDamage(int.MaxValue / 2, ignoreArmor: true);
            }
        }

        public void AddShard(ArtifactType origin) { shards.Add(origin); Changed?.Invoke(this); }

        /// <summary>Two shards make one of the two artifacts they came from, at random (50/50).</summary>
        public bool CombineShards()
        {
            if (shards.Count < 2) return false;
            ArtifactType a = shards[0], b = shards[1];
            shards.RemoveRange(0, 2);
            Add(UnityEngine.Random.value < 0.5f ? a : b);
            return true;
        }

        /// <summary>Uses a one-shot artifact: Full heal (all HP) or Lightning (200 to the nearest enemy).</summary>
        public bool Use(ArtifactType a)
        {
            if (!Artifacts.IsConsumable(a) || !held.Contains(a)) return false;
            HealthComponent hp = GetComponent<HealthComponent>();
            if (hp == null || !hp.IsAlive) return false;
            if (a == ArtifactType.FullHeal) hp.Heal(hp.MaxHealth);
            else
            {
                int enemy = UnitSpatialGrid.FindNearest(transform.position, GameBalance.LightningRange, UnitSpatialGrid.TeamHostile);
                if (enemy < 0) { UI.StrategyHud.Notify("Немає ворога поблизу"); return false; }
                CombatUnit combat = GetComponent<CombatUnit>();
                HealthComponent target = UnitSpatialGrid.Health(enemy);
                if (combat != null) combat.DeliverHit(target, GameBalance.LightningDamage, ignoreArmor: true);
                else target.TakeDamage(GameBalance.LightningDamage, ignoreArmor: true);
            }
            held.Remove(a);
            ApplyEffects();
            Changed?.Invoke(this);
            return true;
        }

        /// <summary>Pushes the effects onto the hero (gear numbers, life steal, Central Empire).</summary>
        public void ApplyEffects()
        {
            CombatUnit combat = GetComponent<CombatUnit>();
            if (combat != null) combat.LifeSteal = LifeSteal;
            GetComponent<HeroProgress>()?.ApplyStats();
            if (GetComponent<HeroSuccession>() != null) CentralEmpire.SetActive(Has(ArtifactType.CentralEmpire), gameObject);
        }

        private void OnHeroDied(HeroSuccession s, Vector3 where)
        {
            if (s == null || s.gameObject != gameObject) return;
            int i = 0;
            foreach (ArtifactType a in held)
            {
                if (Artifacts.IsConsumable(a)) continue; // used up with the hero
                Vector3 p = where + Quaternion.Euler(0f, i++ * 72f, 0f) * Vector3.forward * 1.2f;
                // The Central Empire's artifact turns into a shard at once (§9.3)
                ArtifactPickup.Spawn(a, p, shard: a == ArtifactType.CentralEmpire, owner: a == ArtifactType.CentralEmpire ? null : s);
            }
            held.Clear();
            ApplyEffects();
            Changed?.Invoke(this);
        }

        /// <summary>A loaded save.</summary>
        public void Restore(List<int> savedHeld, List<int> savedShards, bool chosen)
        {
            held.Clear(); shards.Clear();
            if (savedHeld != null) foreach (int a in savedHeld) held.Add((ArtifactType)a);
            if (savedShards != null) foreach (int a in savedShards) shards.Add((ArtifactType)a);
            startChosen = chosen;
            ApplyEffects();
            Changed?.Invoke(this);
        }
    }
}
