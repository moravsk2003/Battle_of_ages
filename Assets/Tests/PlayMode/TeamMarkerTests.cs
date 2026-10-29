using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>The map's team rings and rank badges: on the map every unit has a ring, a knight a badge, the selected a bright ring; first person none.</summary>
    public class TeamMarkerTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(6000f, 0f, -6000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
        }

        [UnityTest]
        public IEnumerator OnTheMap_UnitsHaveRings_AKnightABadge_TheSelectedABrightRing()
        {
            TeamMarkers markers = Object.FindFirstObjectByType<TeamMarkers>();
            if (markers == null) { var m = new GameObject("TeamMarkers"); spawned.Add(m); markers = m.AddComponent<TeamMarkers>(); }

            // The game's map camera: not tagged MainCamera (0.1.7 drew no rings and played no world sounds on the map)
            var camObj = new GameObject("MapCam");
            camObj.transform.SetPositionAndRotation(Far + new Vector3(0f, 100f, -60f), Quaternion.Euler(60f, 0f, 0f));
            Camera cam = camObj.AddComponent<Camera>();
            camObj.AddComponent<BattleOfAges.CameraSystem.RtsMapCameraController>();
            spawned.Add(camObj);
            cam.orthographic = true;
            cam.orthographicSize = 60f;

            GameObject worker = BuildingNode.CreateWorkerNpc(Far, "NPC_Marker_Worker");
            GameObject knight = BuildingNode.CreateWorkerNpc(Far + Vector3.right * 4f, "NPC_Marker_Knight");
            spawned.Add(worker); spawned.Add(knight);
            yield return null;
            UnitProgress.Setup(knight, UnitRank.Knight);
            for (int i = 0; i < 32; i++) yield return null; // the rank is looked up again every 30 frames

            int baseRings = markers.LastRings - 2, baseBadges = markers.LastBadges - 1; // other units of the test run may be about
            Assert.GreaterOrEqual(baseRings, 0, "both units have rings");
            Assert.GreaterOrEqual(baseBadges, 0, "the knight has a badge");

            Assert.IsTrue(BattleOfAges.UI.GameSound.Hear(Far, out float gain, out _), "a sound in the middle of the map view is heard");
            Assert.Greater(gain, 0.1f);

            worker.GetComponent<RtsUnit>().SetSelected(true);
            yield return null;
            yield return null;
            Assert.GreaterOrEqual(markers.LastSelected, 1, "the selected one stands on a bright ring");
            Assert.IsFalse(worker.transform.Find("SelectionIndicator")?.gameObject.activeSelf ?? false, "the old small disc stays hidden");

            cam.orthographic = false; // first person
            yield return null;
            yield return null;
            Assert.AreEqual(0, markers.LastRings, "no rings in first person");
        }
    }
}
