using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The one-shot artifacts in play (§9.1.1): the Lightning strikes the nearest enemy with magic.</summary>
    public class ArtifactPlayTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(4000f, 0f, -4000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        private static Vector3 OnGround(Vector3 p) { p.y = RTS.RtsUnit.GetTerrainHeight(p); return p; }

        [UnityTest]
        public IEnumerator Lightning_Deals250Magic_ThroughArmour_ToTheNearestEnemy()
        {
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(200f, 1f, 200f);
            spawned.Add(ground);

            var hero = new GameObject("LightningHero", typeof(HealthComponent), typeof(CombatUnit));
            hero.transform.position = OnGround(Far);
            hero.GetComponent<HealthComponent>().InitializeHealth(500);
            HeroArtifacts bag = hero.AddComponent<HeroArtifacts>();
            spawned.Add(hero);

            // An armoured general: plenty of HP, and armour that would stop most of a plain hit
            GameObject m = Utils.HostileCampBuilder.CreateHostileMob(AI.HostileMobType.WarlordGuard, "LightningTarget");
            m.GetComponent<AI.HostileMobAI>().SpeedCap = 0f;
            CharacterController cc = m.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            m.transform.position = OnGround(Far + new Vector3(6f, 0f, 0f));
            spawned.Add(m);
            HealthComponent target = m.GetComponent<HealthComponent>();
            target.InitializeHealth(5000);
            target.DamageReduction = 0.8f;
            yield return null;

            bag.Add(ArtifactType.Lightning);
            Assert.IsTrue(bag.Use(ArtifactType.Lightning), "used on the enemy near");
            Assert.AreEqual(5000 - GameBalance.LightningDamage, target.CurrentHealth, "250, all of it through the armour");
            Assert.AreEqual(250, GameBalance.LightningDamage);
            Assert.IsFalse(bag.Has(ArtifactType.Lightning), "gone after use");
            Assert.IsNotNull(Object.FindFirstObjectByType<LightningStrike>(), "a strike from the sky is seen");
            yield return new WaitForSeconds(LightningStrike.Duration + 0.1f);
            Assert.IsNull(Object.FindFirstObjectByType<LightningStrike>(), "and is gone");
        }
    }
}
