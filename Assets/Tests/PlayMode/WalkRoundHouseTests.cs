using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>
    /// A tester's knight stood against a house for good: sent somewhere round its corner, it took touching the wall within 4 m
    /// of the destination for arriving (FieldReportProbe.UnitsRoundTheHouse: 4 m short from 248 to 158 degrees).
    /// </summary>
    public class WalkRoundHouseTests
    {
        private static readonly Vector3 Far = new Vector3(5000f, 0f, 5000f);

        [UnityTest, Timeout(180000)]
        public IEnumerator SentRoundACorner_AUnitGetsThere_NotJustToTheWall()
        {
            Vector3 c = Far;
            c.y = RtsUnit.GetTerrainHeight(c);
            GameObject house = BuildingNode.CreateWorkerHousePrefab("WalkRound_House");
            house.transform.SetPositionAndRotation(c, Quaternion.Euler(0f, 30f, 0f));
            GameObject unit = BuildingNode.CreateWorkerNpc(c + new Vector3(9f, 0f, 0f), "NPC_WalkRound");
            unit.GetComponent<NpcResourceGatherer>().SetGatherDirective(GatherDirective.Idle);
            RtsUnit walker = unit.GetComponent<RtsUnit>();
            yield return new WaitForSeconds(0.5f);

            Time.timeScale = 4f;
            string missed = "";
            try
            {
                for (int from = 0; from < 16; from++)
                {
                    float a = from * 22.5f * Mathf.Deg2Rad, b = a - 90f * Mathf.Deg2Rad;
                    Vector3 start = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 8f, to = c + new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b)) * 4.5f;
                    start.y = RtsUnit.GetTerrainHeight(start);
                    to.y = RtsUnit.GetTerrainHeight(to);
                    unit.transform.position = start;
                    walker.IssueMoveCommand(to);
                    float t0 = Time.time;
                    while (Time.time < t0 + 16f && walker.HasMoveTarget) yield return null;
                    Vector3 left = unit.transform.position - to;
                    left.y = 0f;
                    if (left.magnitude > 2f) missed += $" from {from * 22.5f:0} deg: {left.magnitude:0.0} m short;";
                }
            }
            finally
            {
                Time.timeScale = 1f;
                Object.Destroy(unit);
                Object.Destroy(house);
            }
            Assert.IsEmpty(missed, "every walk ends at its destination");
        }
    }
}
