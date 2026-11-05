using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Utils;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>A promotion shows: each rank wears its model (gambeson, plate, the general's colours), the gear goes on the new body, a hidden unit stays hidden, a hero keeps his.</summary>
    public class RankModelTests
    {
        private GameObject unit;

        [TearDown]
        public void Teardown()
        {
            if (unit != null) Object.Destroy(unit);
        }

        private static HumanoidModelBuilder.HumanType Look(GameObject u) => u.GetComponentInChildren<CharacterGear>(true).Type;

        private GameObject Make(HumanoidModelBuilder.HumanType model, bool hero = false)
        {
            unit = new GameObject("RankUnit");
            unit.transform.position = new Vector3(0f, -500f, 0f);
            unit.AddComponent<HealthComponent>();
            unit.AddComponent<CombatUnit>();
            unit.AddComponent<Equipment>().Equip(new Item(ItemType.Sword, 2));
            UnitProgress.Setup(unit, RankLooks.RankOf(model), hero);
            GameObject visual = HumanoidModelBuilder.CreateHumanoidModel(model, "3DModel");
            visual.transform.SetParent(unit.transform, false);
            unit.AddComponent<RankModel>();
            return unit;
        }

        [UnityTest]
        public IEnumerator EachPromotion_PutsOnTheRanksModel()
        {
            Make(HumanoidModelBuilder.HumanType.Villager);
            yield return null;
            UnitProgress p = unit.GetComponent<UnitProgress>();
            Assert.AreEqual(HumanoidModelBuilder.HumanType.Villager, Look(unit), "a person");
            var expected = new[] { HumanoidModelBuilder.HumanType.Soldier, HumanoidModelBuilder.HumanType.KnightGuard, HumanoidModelBuilder.HumanType.General };
            foreach (HumanoidModelBuilder.HumanType want in expected)
            {
                p.Promote();
                yield return null; // the old model goes at the end of the frame
                Assert.AreEqual(want, Look(unit), p.Rank.ToString());
                Assert.AreEqual(1, unit.GetComponentsInChildren<CharacterGear>(true).Length, "one model, the old one gone");
                Assert.AreEqual("3DModel", unit.GetComponentInChildren<CharacterGear>(true).name, "the same name: lookups by it still find it");
                Assert.IsNotNull(unit.GetComponentInChildren<Animator>(), "it animates");
            }
            Assert.IsTrue(unit.GetComponent<Equipment>()[EquipSlot.Weapon].Equals(new Item(ItemType.Sword, 2)), "the gear stays");
        }

        [UnityTest]
        public IEnumerator AHiddenUnit_StaysHidden_AHero_KeepsHisLook()
        {
            Make(HumanoidModelBuilder.HumanType.Villager);
            yield return null;
            foreach (Renderer r in unit.GetComponentsInChildren<Renderer>(true)) r.enabled = false; // in a mine
            unit.GetComponent<UnitProgress>().Promote();
            yield return null;
            foreach (Renderer r in unit.GetComponentsInChildren<Renderer>(true)) Assert.IsFalse(r.enabled, "seen through the mine's roof");
            Object.Destroy(unit);
            yield return null;

            Make(HumanoidModelBuilder.HumanType.Villager, hero: true);
            yield return null;
            Assert.IsFalse(unit.GetComponent<RankModel>().Sync(), "a hero's rank changes only by death, his look with it");
            Assert.AreEqual(HumanoidModelBuilder.HumanType.Villager, Look(unit));
        }

        [UnityTest]
        public IEnumerator AKnightOrGeneral_TakingABow_IsSculptedWithIt([Values(HumanoidModelBuilder.HumanType.KnightGuard, HumanoidModelBuilder.HumanType.General)] HumanoidModelBuilder.HumanType rank)
        {
            Make(rank);
            yield return null;
            CharacterGear Gear() => unit.GetComponentInChildren<CharacterGear>(true);
            Assert.IsFalse(Gear().SculptedBow);
            Equipment e = unit.GetComponent<Equipment>();
            e.Equip(new Item(ItemType.Bow, 3));
            Assert.AreEqual(1, unit.GetComponentsInChildren<CharacterGear>(true).Length, "the old body is out at once");
            Assert.AreEqual(rank, Gear().Type);
            Assert.IsTrue(Gear().SculptedBow, "a bow in his hands");
            Assert.IsTrue(Gear().HoldsBow, "and he shoots with it");
            Assert.IsFalse(Gear().WrongSculpt);
            e.Equip(new Item(ItemType.Sword, 3));
            Assert.IsFalse(Gear().SculptedBow, "the sword and shield back");
            yield return null;
            Assert.IsNotNull(unit.GetComponentInChildren<Animator>());
        }

        [UnityTest]
        public IEnumerator AUnitBuiltWithAnotherModel_GetsItsRanksOne()
        {
            Make(HumanoidModelBuilder.HumanType.Villager);
            UnitProgress.Setup(unit, UnitRank.Knight); // a loaded save
            yield return null; // RankModel's Start
            Assert.AreEqual(HumanoidModelBuilder.HumanType.KnightGuard, Look(unit));
        }
    }
}
