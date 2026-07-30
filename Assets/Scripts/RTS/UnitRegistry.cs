using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.RTS
{
    /// <summary>
    /// High-performance spatial registry for tracking active RTS units.
    /// Eliminates scene-wide FindObjectsByType searches and Garbage Collection allocations.
    /// </summary>
    public static class UnitRegistry
    {
        private static readonly List<RtsUnit> activeUnits = new List<RtsUnit>(128);

        public static IReadOnlyList<RtsUnit> ActiveUnits => activeUnits;

        public static void Register(RtsUnit unit)
        {
            if (unit != null && !activeUnits.Contains(unit))
            {
                activeUnits.Add(unit);
            }
        }

        public static void Unregister(RtsUnit unit)
        {
            if (unit != null)
            {
                activeUnits.Remove(unit);
            }
        }

        /// <summary>
        /// Populates the target list with units located inside the GUI screen rectangle.
        /// Zero GC allocation.
        /// </summary>
        public static void GetUnitsInScreenRect(Rect selectionRect, Camera camera, List<RtsUnit> resultsBuffer)
        {
            resultsBuffer.Clear();
            if (camera == null) return;

            int count = activeUnits.Count;
            for (int i = 0; i < count; i++)
            {
                RtsUnit unit = activeUnits[i];
                if (unit == null || !unit.gameObject.activeInHierarchy) continue;

                Vector3 screenPoint = camera.WorldToScreenPoint(unit.transform.position);
                
                // Screen points behind camera (z < 0) are excluded
                if (screenPoint.z < 0) continue;

                // Convert screen Y (bottom-left 0,0) to GUI Y (top-left 0,0)
                screenPoint.y = Screen.height - screenPoint.y;

                if (selectionRect.Contains(screenPoint))
                {
                    resultsBuffer.Add(unit);
                }
            }
        }

        /// <summary>
        /// Finds the nearest unit within maxDistance from a world point.
        /// Uses squared distance calculations to avoid square root CPU costs.
        /// </summary>
        public static RtsUnit FindNearestUnit(Vector3 worldPoint, float maxDistance)
        {
            RtsUnit closest = null;
            float minSqrDst = maxDistance * maxDistance;

            int count = activeUnits.Count;
            for (int i = 0; i < count; i++)
            {
                RtsUnit unit = activeUnits[i];
                if (unit == null || !unit.gameObject.activeInHierarchy) continue;

                Vector3 unitPos = unit.transform.position;
                unitPos.y = worldPoint.y; // Ignore Y offset for ground distance check

                float sqrDst = (worldPoint - unitPos).sqrMagnitude;
                if (sqrDst < minSqrDst)
                {
                    minSqrDst = sqrDst;
                    closest = unit;
                }
            }

            return closest;
        }
    }
}
