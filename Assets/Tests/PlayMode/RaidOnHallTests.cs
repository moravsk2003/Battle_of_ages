using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// A tester's report: an enemy general ran in circles with the player's people after it, while its men beat at the hall
    /// and could not break it. The goal of a mob's approach turned round its target; and a sword's reach was counted to the
    /// middle of a building nobody may walk into.
    /// </summary>
    public class RaidOnHallTests
    {
        [Test]
        public void ABuildingsHealth_ShowsOnTheMap_AndInFirstPersonOnlyAfterAHit()
        {
            Assert.IsTrue(UI.HudWorldLabels.ShowsHealth(true, false, false, float.PositiveInfinity, 50f), "on the map, a house in sight");
            Assert.IsFalse(UI.HudWorldLabels.ShowsHealth(true, true, false, float.PositiveInfinity, 50f), "not every whole wall");
            Assert.IsTrue(UI.HudWorldLabels.ShowsHealth(true, true, true, float.PositiveInfinity, 50f), "a hurt wall does");
            Assert.IsFalse(UI.HudWorldLabels.ShowsHealth(false, false, true, 6f, 10f), "in first person, not long after a hit");
            Assert.IsTrue(UI.HudWorldLabels.ShowsHealth(false, false, true, 3f, 10f), "right after one it does");
        }

        [Test]
        public void TheFarTerrain_CoversOnlyWhatItReaches()
        {
            const float chunk = 64f;
            Vector2Int viewer = new Vector2Int(10, 10);
            Assert.IsTrue(World.FarTerrainRenderer.Covers(viewer, new Vector2Int(12, 10), chunk), "a settlement nearby lies on it");
            Assert.IsFalse(World.FarTerrainRenderer.Covers(viewer, new Vector2Int(10 + 30, 10), chunk), "one 1900 m away does not: it keeps its own ground");
        }

        private static readonly Vector3 Far = new Vector3(5400f, 0f, -5200f);
        private GameObject hall;

        [TearDown]
        public void Teardown()
        {
            Time.timeScale = 1f;
            NpcKingdom.RemoveAll();
            if (hall != null) Object.Destroy(hall);
        }

        private HealthComponent Hall()
        {
            Vector3 at = Far;
            at.y = RtsUnit.GetTerrainHeight(at);
            hall = TownHall.CreatePrefab("RaidTest_Hall");
            hall.transform.position = at;
            return hall.GetComponent<HealthComponent>();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator MeleeRaiders_BreakAHall_TheyCannotWalkInto()
        {
            HealthComponent target = Hall();
            yield return null;
            NpcKingdom k = NpcKingdom.Found(Far + new Vector3(30f, 0f, 0f), "Таран", 0);
            k.Economy.HallLevel = 0;
            int before = target.CurrentHealth;
            // Eight: where one stops depends on its place round the target
            var raiders = new HostileMobAI[8];
            for (int i = 0; i < raiders.Length; i++) raiders[i] = k.AddSubject(UnitRank.Warrior, k.Spot());
            foreach (HostileMobAI m in raiders) m.OrderAttack(target);
            yield return null;
            Time.timeScale = 4f;
            float end = Time.time + 60f;
            System.Func<int> striking = () => { int n = 0; foreach (HostileMobAI m in raiders) if (m != null && m.CurrentStateType == AIStateType.Attack) n++; return n; };
            while (Time.time < end && striking() < raiders.Length) yield return null;
            string where = "";
            foreach (HostileMobAI m in raiders) where += $" [{(m != null ? $"{m.CurrentStateType}, {target.DistanceFrom(m.transform.position):0.0} m from the wall, range {m.AttackRange:0.0}, target {(m.CurrentCombatTarget != null ? m.CurrentCombatTarget.name : "-")}, dest {m.HasDestination}, can {m.Combat.CanAttack}" : "gone")}]";
            Assert.AreEqual(raiders.Length, striking(), "every one of them gets to strike, none stops short:" + where);
            float blows = Time.time + 6f; // (all standing to strike, the first blow is still in its swing)
            while (Time.time < blows && target.CurrentHealth >= before) yield return null;
            Assert.Less(target.CurrentHealth, before, "swords reach a hall from its wall:" + where);
        }

        [Category("Slow")]
        [UnityTest, Timeout(120000)]
        public IEnumerator AMage_StandsAndCasts_NotRunningRoundItsTarget()
        {
            HealthComponent target = Hall();
            yield return null;
            NpcKingdom k = NpcKingdom.Found(Far + new Vector3(60f, 0f, 0f), "Маг", 0, -1, HeroClass.Mage);
            HostileMobAI mage = k.King;
            Assert.IsNotNull(mage);
            Assert.IsTrue(mage.Combat.IsRanged, "the king of this test strikes from afar");
            mage.OrderAttack(target);
            yield return null;
            Time.timeScale = 4f;
            float end = Time.time + 60f;
            while (Time.time < end && mage.CurrentStateType != AIStateType.Attack) yield return null;
            Assert.AreEqual(AIStateType.Attack, mage.CurrentStateType, "it came in reach");
            Vector3 from = mage.transform.position - target.transform.position;
            float furthest = 0f, turned = 0f;
            end = Time.time + 16f;
            while (Time.time < end)
            {
                Vector3 off = mage.transform.position - target.transform.position;
                furthest = Mathf.Max(furthest, Vector3.Distance(mage.transform.position, target.transform.position + from));
                turned = Mathf.Max(turned, Mathf.Abs(Vector3.SignedAngle(new Vector3(from.x, 0f, from.z), new Vector3(off.x, 0f, off.z), Vector3.up)));
                yield return null;
            }
            TestContext.WriteLine($"range {mage.AttackRange:0.0}, moved up to {furthest:0.0} m, turned round the target by {turned:0} degrees in 16 s");
            Assert.Less(turned, 15f, "it does not run round its target (at 20 degrees a second it went 320)");
            Assert.Less(target.CurrentHealth, target.MaxHealth, "and it strikes");
        }
    }
}
