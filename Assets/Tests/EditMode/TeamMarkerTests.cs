using NUnit.Framework;
using UnityEngine;
using BattleOfAges.RTS;

namespace BattleOfAges.Tests
{
    /// <summary>Team rings: every side its own colour (wildlife none), and a ring never shrinks below its pixels on screen.</summary>
    [TestFixture]
    public class TeamMarkerTests
    {
        [Test]
        public void EverySide_HasItsOwnColour_WildlifeNone()
        {
            Assert.AreEqual(0f, TeamMarkers.SideColour(UnitSpatialGrid.TeamNeutral).a, "animals have no ring");
            var seen = new System.Collections.Generic.HashSet<Color>();
            int[] sides = { UnitSpatialGrid.TeamPlayer, UnitSpatialGrid.TeamHostile, 3, 4, 5, 6 };
            foreach (int t in sides) Assert.IsTrue(seen.Add(TeamMarkers.SideColour(t)), "side " + t + " shares a colour");
            Assert.IsFalse(seen.Contains(TeamMarkers.Selected), "the selection's colour is no side's");
        }

        [Test]
        public void NoForeignSide_LooksLikeOwn_OrTheSelection()
        {
            Color.RGBToHSV(TeamMarkers.Own, out float own, out _, out _);
            Color.RGBToHSV(TeamMarkers.Selected, out float chosen, out _, out _);
            float Apart(float a, float b) { float d = Mathf.Abs(a - b); return Mathf.Min(d, 1f - d); }
            var foreign = new System.Collections.Generic.List<int> { UnitSpatialGrid.TeamHostile };
            for (int t = UnitSpatialGrid.TeamKingdomFirst; t < UnitSpatialGrid.TeamKingdomFirst + 6; t++) foreign.Add(t);
            foreach (int t in foreign)
            {
                Color.RGBToHSV(TeamMarkers.SideColour(t), out float hue, out _, out _);
                Assert.Greater(Apart(hue, own), 0.2f, $"side {t} is far from the own blue");
                Assert.Greater(Apart(hue, chosen), 0.12f, $"side {t} is far from the selection green");
            }
        }

        [Test]
        public void ARing_KeepsItsPixels_ZoomedOut_AndItsMetres_Close()
        {
            Assert.AreEqual(1.3f, TeamMarkers.WorldSize(1.3f, TeamMarkers.RingPixels, 10f, 1080f), 1e-4f, "close up: its own size");
            float far = TeamMarkers.WorldSize(1.3f, TeamMarkers.RingPixels, 100f, 1080f);
            Assert.AreEqual(TeamMarkers.RingPixels, far / (2f * 100f / 1080f), 1e-3f, "zoomed out: its pixels across");
        }

        [Test]
        public void TheRingMesh_IsFlat_AndOneAcross()
        {
            Mesh m = TeamMarkers.MakeRing(0.72f, 1f, 32);
            Assert.AreEqual(0f, m.bounds.size.y, 1e-5f);
            Assert.AreEqual(2f, m.bounds.size.x, 0.01f);
            foreach (Vector3 n in m.normals) Assert.Greater(n.y, 0.99f, "faces up, to the camera");
        }
    }
}
