using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// A ladder set against a wall by attackers too weak to break it (§10.5): the first one at the spot raises it
    /// (GameBalance.LadderSetupSeconds), then they go up one at a time, over the breastwork onto the walkway and down
    /// inside. The way up: Foot, Top (on the breastwork), Walk (the walkway), Inside (the ground behind the wall).
    /// Taken away when its wall falls or nobody used it for GameBalance.LadderLingerSeconds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SiegeLadder : MonoBehaviour
    {
        private static readonly List<SiegeLadder> active = new List<SiegeLadder>();
        public static IReadOnlyList<SiegeLadder> Active => active;

        /// <summary>The foot stands this far out from the wall's face.</summary>
        public const float FootOut = 1.3f;
        /// <summary>Attackers at the same wall within this of a ladder use it instead of raising another.</summary>
        public const float ShareRadius = 5f;

        public WallSegment Wall { get; private set; }
        public Vector3 Foot { get; private set; }
        public Vector3 Top { get; private set; }
        public Vector3 Walk { get; private set; }
        public Vector3 Inside { get; private set; }
        /// <summary>The side of the wall it stands on (horizontal unit vector, away from the wall).</summary>
        public Vector3 Side { get; private set; }

        private float raisedAt, lastUsed, lean;
        private bool upright;
        private HealthComponent climber;
        private Transform model;

        public bool Ready => Time.time >= raisedAt;
        public bool Free => climber == null || !climber.IsAlive;

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

        /// <summary>The ladder on that side of the wall near point, if there is one.</summary>
        public static SiegeLadder Near(WallSegment wall, Vector3 point, Vector3 side)
        {
            foreach (SiegeLadder l in active)
                if (l != null && l.Wall == wall && Vector3.Dot(l.Side, side) > 0.5f && Vector3.Distance(l.Foot, point + side * (WallBuilder.Thickness * 0.5f + FootOut)) < ShareRadius)
                    return l;
            return null;
        }

        /// <summary>Raises a ladder against the wall at point (on its middle line) from the side.</summary>
        public static SiegeLadder Raise(WallSegment wall, Vector3 point, Vector3 side)
        {
            float half = WallBuilder.Thickness * 0.5f, wallY = wall.transform.position.y;
            Vector3 foot = point + side * (half + FootOut);
            foot.y = RtsUnit.GetTerrainHeight(foot);
            var go = new GameObject("SiegeLadder");
            go.transform.position = foot;
            var l = go.AddComponent<SiegeLadder>();
            l.Wall = wall;
            l.Side = side;
            l.Foot = foot;
            float topY = wallY + WallBuilder.WalkY + WallBuilder.BreastH;
            l.Top = new Vector3(point.x, topY, point.z) + side * (half - WallBuilder.ParapetT * 0.5f);
            l.Walk = new Vector3(point.x, wallY + WallBuilder.WalkY, point.z) - side * 0.25f;
            Vector3 inside = point - side * (half + 1.2f);
            inside.y = RtsUnit.GetTerrainHeight(inside);
            l.Inside = inside;
            l.lean = Mathf.Atan2(FootOut + 0.1f, topY - foot.y) * Mathf.Rad2Deg;
            l.raisedAt = Time.time + GameBalance.LadderSetupSeconds;
            l.lastUsed = Time.time;

            var m = new GameObject("Model");
            m.transform.SetParent(go.transform, false);
            m.AddComponent<MeshFilter>().sharedMesh = WallBuilder.LadderMesh;
            MeshRenderer r = m.AddComponent<MeshRenderer>();
            r.sharedMaterial = MeshKit.PropMaterial();
            l.model = m.transform;
            l.Pose(0f);
            return l;
        }

        /// <summary>The ladder raised by share t (0 lying on the ground toward the wall, 1 leaning on it).</summary>
        private void Pose(float t)
        {
            float angle = Mathf.Lerp(86f, lean, Mathf.SmoothStep(0f, 1f, t));
            model.rotation = Quaternion.LookRotation(-Side, Vector3.up) * Quaternion.Euler(angle, 0f, 0f);
        }

        /// <summary>Stands it against the wall at once (visual review).</summary>
        public void RaiseNow() { raisedAt = Time.time; upright = true; Pose(1f); }

        /// <summary>Takes the ladder for a climb, if it stands and nobody is on it.</summary>
        public bool TryStartClimb(HealthComponent who)
        {
            if (!Ready || !Free || Wall == null) return false;
            climber = who;
            lastUsed = Time.time;
            return true;
        }

        /// <summary>The climber is up (or fell): the next may go.</summary>
        public void EndClimb(HealthComponent who)
        {
            if (climber == who) climber = null;
            lastUsed = Time.time;
        }

        private void Update()
        {
            if (Wall == null) { Destroy(gameObject); return; } // the wall fell: the ladder with it
            if (!Ready) Pose(1f - (raisedAt - Time.time) / Mathf.Max(0.01f, GameBalance.LadderSetupSeconds));
            else if (!upright) { Pose(1f); upright = true; }
            if (Free && Time.time - lastUsed > GameBalance.LadderLingerSeconds) Destroy(gameObject);
        }
    }
}
