using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// The octagonal buttress at a joint of the wall: one per joint, however many segments meet there and at whatever
    /// angle (each segment drawing its own would flicker against its neighbour's). Made when a segment ends there, gone
    /// with the last one. Not a building: no HP of its own, not saved (the segments remake them).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallPier : MonoBehaviour
    {
        private static readonly List<WallPier> active = new List<WallPier>();
        public static IReadOnlyList<WallPier> Active => active;

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

        public static WallPier At(Vector3 p)
        {
            foreach (WallPier w in active)
            {
                if (w == null) continue;
                Vector3 d = w.transform.position - p;
                d.y = 0f;
                if (d.magnitude < WallSegment.JoinDistance) return w;
            }
            return null;
        }

        /// <summary>The pier at p, made (turned with the segment's rotation: its merlon outside) if there is none.</summary>
        public static WallPier EnsureAt(Vector3 p, Quaternion rotation)
        {
            WallPier w = At(p);
            if (w != null) return w;
            var go = new GameObject("WallPier");
            go.transform.SetPositionAndRotation(p, rotation);
            WallBuilder.AttachPier(go.transform);
            BoxCollider c = go.AddComponent<BoxCollider>();
            float a = WallBuilder.PierApothem * 2f, h = WallBuilder.WalkY + WallBuilder.PierTopRise;
            c.center = new Vector3(0f, h * 0.5f, 0f);
            c.size = new Vector3(a, h, a);
            return go.AddComponent<WallPier>();
        }

        /// <summary>Removes the pier at p once no segment ends there.</summary>
        public static void Prune(Vector3 p)
        {
            WallPier w = At(p);
            if (w != null && !WallSegment.HasEndNear(p)) Destroy(w.gameObject);
        }
    }
}
