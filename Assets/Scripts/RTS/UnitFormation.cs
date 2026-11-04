using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.RTS
{
    /// <summary>
    /// Where a group stands when sent somewhere: ranks across the way it walks, centred on the point, the strongest in front and the
    /// archers behind, each to the place nearest its side of the group (a square grid in a fixed order had them cross each other's way).
    /// </summary>
    public static class UnitFormation
    {
        public const float Spacing = 2.2f;
        /// <summary>A walk shorter than this says nothing of which way is forward: north is.</summary>
        private const float NoWay = 1f;

        /// <summary>The place of each unit (by its index): a higher strength stands further forward.</summary>
        public static Vector3[] Slots(IList<Vector3> at, IList<int> strength, Vector3 point, float spacing = Spacing)
        {
            int n = at.Count;
            var slots = new Vector3[n];
            if (n == 0) return slots;
            if (n == 1) { slots[0] = point; return slots; }

            Vector3 middle = Vector3.zero;
            foreach (Vector3 p in at) middle += p;
            middle /= n;
            Vector3 forward = point - middle;
            forward.y = 0f;
            forward = forward.magnitude < NoWay ? Vector3.forward : forward.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            // A front wider than it is deep
            int columns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(n * 2f)), 1, n), ranks = Mathf.CeilToInt(n / (float)columns);

            var order = new List<int>(n);
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byStrength = strength[b].CompareTo(strength[a]);
                return byStrength != 0 ? byStrength : Vector3.Dot(at[b] - middle, forward).CompareTo(Vector3.Dot(at[a] - middle, forward));
            });

            for (int rank = 0, taken = 0; rank < ranks; rank++)
            {
                int inRank = Mathf.Min(columns, n - taken);
                List<int> line = order.GetRange(taken, inRank);
                line.Sort((a, b) => Vector3.Dot(at[a], right).CompareTo(Vector3.Dot(at[b], right))); // each to its own side: no crossing
                for (int c = 0; c < inRank; c++)
                {
                    float across = (c - (inRank - 1) * 0.5f) * spacing, along = ((ranks - 1) * 0.5f - rank) * spacing;
                    slots[line[c]] = point + right * across + forward * along;
                }
                taken += inRank;
            }
            return slots;
        }

        /// <summary>A place one can stand on: out of a trunk or a house, and a place in a lake is given up for the point itself.</summary>
        public static Vector3 Standable(Vector3 slot, Vector3 point, float radius = 0.5f)
        {
            if (World.Lakes.InWater(slot.x, slot.z, 1f)) return point;
            if (World.SolidProps.Inside(slot, radius, out Vector3 outOfProp)) slot += outOfProp;
            if (Building.Footprints.Inside(slot, radius, out Vector3 outOfHouse)) slot += outOfHouse;
            return slot;
        }
    }
}
