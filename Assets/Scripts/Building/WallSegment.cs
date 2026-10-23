using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// A segment of the settlement's wall (docs/GAME_DESIGN.md §10.5): its kind, the tier of the ore it is built of (its
    /// strength) and its two ends along local X, where the next segment snaps and a pier stands (WallPier, shared by the
    /// segments meeting there).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallSegment : MonoBehaviour
    {
        private static readonly List<WallSegment> active = new List<WallSegment>();
        public static IReadOnlyList<WallSegment> Active => active;

        /// <summary>Ends closer than this are joined.</summary>
        public const float JoinDistance = 0.3f;

        [SerializeField] private WallBuilder.Kind kind;
        [SerializeField] private int oreTier;

        public WallBuilder.Kind Kind { get => kind; set => kind = value; }
        public int OreTier { get => oreTier; set => oreTier = Mathf.Max(0, value); }
        public float Length => WallBuilder.LengthOf(kind);
        public Vector3 EndA => transform.TransformPoint(new Vector3(-Length * 0.5f, 0f, 0f));
        public Vector3 EndB => transform.TransformPoint(new Vector3(Length * 0.5f, 0f, 0f));

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

        private void Start()
        {
            WallPier.EnsureAt(EndA, transform.rotation);
            WallPier.EnsureAt(EndB, transform.rotation);
        }

        private void OnDestroy()
        {
            if (!gameObject.scene.isLoaded) return; // the scene is going: so are the piers
            WallPier.Prune(EndA);
            WallPier.Prune(EndB);
        }

        public static bool IsWall(BuildingType t) => t == BuildingType.CastleWall || t == BuildingType.WallStairs || t == BuildingType.WallGate;

        public static WallBuilder.Kind KindOf(BuildingType t) =>
            t == BuildingType.WallStairs ? WallBuilder.Kind.Stairs : t == BuildingType.WallGate ? WallBuilder.Kind.Gate : WallBuilder.Kind.Wall;

        /// <summary>HP of a wall of the type built of ore of the tier (better ore - stronger, as with gear).</summary>
        public static int MaxHealthFor(BuildingType type, int oreTier) =>
            Mathf.RoundToInt(BuildingData.GetDefinition(type).MaxHealth * (1f + GameBalance.WallHealthPerOreTier * Mathf.Max(0, oreTier)));

        /// <summary>Whether some segment has an end within JoinDistance of p (on the ground plane), other than skip.</summary>
        public static bool HasEndNear(Vector3 p, WallSegment skip = null)
        {
            foreach (WallSegment s in active)
            {
                if (s == null || s == skip) continue;
                if (Flat(s.EndA - p) < JoinDistance || Flat(s.EndB - p) < JoinDistance) return true;
            }
            return false;
        }

        private static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        /// <summary>
        /// The free end (no other segment joined to it) nearest to near within radius: the point, the direction out of the
        /// segment there, and the segment's outside.
        /// </summary>
        public static bool NearestFreeEnd(Vector3 near, float radius, out Vector3 end, out Vector3 away, out Vector3 outside)
        {
            end = away = outside = Vector3.zero;
            float best = radius;
            bool found = false;
            foreach (WallSegment s in active)
            {
                if (s == null) continue;
                for (int e = 0; e < 2; e++)
                {
                    Vector3 p = e == 0 ? s.EndA : s.EndB;
                    float d = Flat(p - near);
                    if (d >= best || HasEndNear(p, s)) continue;
                    best = d;
                    found = true;
                    end = p;
                    away = (e == 0 ? -1f : 1f) * s.transform.right;
                    outside = s.transform.forward;
                }
            }
            return found;
        }

        /// <summary>
        /// Where a new segment of the length goes when the cursor is near a free end: on from that end, turned toward the
        /// cursor in GameBalance.WallTurnStep steps (at most a right angle), its outside on the same side as the
        /// neighbour's. endY is the height of the end (the new segment keeps it where the ground lets it).
        /// </summary>
        public static bool Snap(Vector3 cursor, float length, out Vector3 center, out float yaw, out float endY)
        {
            center = cursor; yaw = 0f; endY = 0f;
            if (!NearestFreeEnd(cursor, GameBalance.WallSnapRadius, out Vector3 end, out Vector3 away, out Vector3 outside)) return false;
            Vector3 toCursor = cursor - end;
            toCursor.y = 0f;
            float turn = toCursor.sqrMagnitude > 0.25f ? Vector3.SignedAngle(away, toCursor, Vector3.up) : 0f;
            float step = Mathf.Max(1f, GameBalance.WallTurnStep);
            turn = Mathf.Clamp(Mathf.Round(turn / step) * step, -90f, 90f);
            Quaternion turned = Quaternion.Euler(0f, turn, 0f);
            Vector3 dir = turned * away;
            dir.y = 0f;
            dir.Normalize();
            center = end + dir * (length * 0.5f);
            // Local x along dir or against it, whichever puts the outside (local z = (-x.z, 0, x.x)) where the
            // neighbour's outside turns to
            Vector3 x = dir;
            if (Vector3.Dot(new Vector3(-x.z, 0f, x.x), turned * outside) < 0f) x = -x;
            yaw = Mathf.Atan2(-x.z, x.x) * Mathf.Rad2Deg;
            endY = end.y;
            center.y = end.y;
            return true;
        }
    }
}
