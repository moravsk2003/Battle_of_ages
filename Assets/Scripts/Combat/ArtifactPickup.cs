using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// An artifact (or a shard) lying on the ground (docs/GAME_DESIGN.md §9.3). A whole artifact keeps to its fallen
    /// hero's kingdom: nobody may take it during the interregnum, then only the new hero; if that hero dies without it
    /// (or the kingdom is over), nobody may take it any more and it turns into a shard, which anyone may pick up.
    /// </summary>
    public sealed class ArtifactPickup : MonoBehaviour
    {
        private const float PickupDistance = 2.5f, CheckInterval = 0.25f;
        private static readonly List<ArtifactPickup> active = new List<ArtifactPickup>();
        public static IReadOnlyList<ArtifactPickup> Active => active;

        [SerializeField] private ArtifactType type;
        [SerializeField] private bool isShard;
        private HeroSuccession owner; // the kingdom it keeps to (null: free)
        private float checkTimer, baseY;
        private Transform visual;

        public ArtifactType Type => type;
        public bool IsShard => isShard;
        public HeroSuccession Owner => owner;

        public static ArtifactPickup Spawn(ArtifactType type, Vector3 position, bool shard, HeroSuccession owner)
        {
            var go = new GameObject(shard ? "ArtifactShard" : "Artifact");
            go.transform.position = position;
            var p = go.AddComponent<ArtifactPickup>();
            p.type = type;
            p.owner = shard ? null : owner;
            p.baseY = position.y;
            p.SetShard(shard);
            return p;
        }

        /// <summary>Who may take it now.</summary>
        public bool MayTake(HeroSuccession taker)
        {
            if (isShard || owner == null) return true;
            if (owner.InInterregnum) return false;  // nobody during the interregnum
            return taker == owner;                  // only the new hero of that kingdom
        }

        private void OnEnable()
        {
            active.Add(this);
            HeroSuccession.HeroDied += OnHeroDied;
            HeroSuccession.GameOver += OnKingdomOver;
        }

        private void OnDisable()
        {
            active.Remove(this);
            HeroSuccession.HeroDied -= OnHeroDied;
            HeroSuccession.GameOver -= OnKingdomOver;
        }

        // The new hero died without taking it, or there is no hero left: nobody may take it whole any more
        private void OnHeroDied(HeroSuccession s, Vector3 where) { if (!isShard && s == owner && Time.time > spawnTime) SetShard(true); }
        private void OnKingdomOver(HeroSuccession s) { if (!isShard && s == owner) SetShard(true); }

        private float spawnTime;

        private void Start() => spawnTime = Time.time;

        private void SetShard(bool shard)
        {
            isShard = shard;
            if (shard) owner = null;
            name = shard ? "ArtifactShard" : "Artifact";
            if (visual != null) Destroy(visual.gameObject);
            Material look = RuntimeShaders.UnlitMaterial(Artifacts.Tier(type) == ArtifactTier.WorldChanging ? new Color(0.8f, 0.2f, 1f)
                                                       : Artifacts.Tier(type) == ArtifactTier.Legendary ? new Color(1f, 0.75f, 0.2f)
                                                       : new Color(0.6f, 0.9f, 1f));
            if (look == null) return; // still picked up, just not drawn
            GameObject v = GameObject.CreatePrimitive(shard ? PrimitiveType.Cube : PrimitiveType.Sphere);
            v.name = "Look";
            Collider c = v.GetComponent<Collider>();
            if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            v.transform.SetParent(transform, false);
            v.transform.localScale = Vector3.one * (shard ? 0.3f : 0.45f);
            var r = v.GetComponent<MeshRenderer>();
            r.sharedMaterial = look;
            visual = v.transform;
        }

        private void Update()
        {
            if (visual != null)
            {
                visual.localPosition = Vector3.up * (1f + 0.15f * Mathf.Sin(Time.time * 2f));
                visual.Rotate(0f, 90f * Time.deltaTime, 0f);
            }
            checkTimer -= Time.deltaTime;
            if (checkTimer > 0f) return;
            checkTimer = CheckInterval;

            // Heroes (the player's for now) pick it up by walking over it
            foreach (HeroSuccession hero in HeroSuccession.All)
            {
                if (hero == null || hero.InInterregnum || hero.IsGameOver) continue;
                Vector3 d = hero.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > PickupDistance * PickupDistance || !MayTake(hero)) continue;
                TakeBy(hero);
                return;
            }
        }

        public void TakeBy(HeroSuccession hero)
        {
            HeroArtifacts bag = hero.GetComponent<HeroArtifacts>();
            if (bag == null) return;
            if (isShard) bag.AddShard(type);
            else bag.Add(type);
            UI.StrategyHud.Notify(isShard ? $"Осколок: {Artifacts.Name(type)}" : $"Артефакт: {Artifacts.Name(type)}");
            Destroy(gameObject);
        }
    }
}
