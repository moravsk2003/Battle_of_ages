using UnityEngine;

namespace BattleOfAges.RTS
{
    /// <summary>
    /// Cheap kinematic locomotion for AI and RTS units (instead of CharacterController.Move, which dominated the frame
    /// with hundreds of units): desired velocity plus crowd separation from UnitSpatialGrid, then the unit follows the
    /// sampled terrain height. Too-steep climbs are refused. Static obstacles are handled by the callers' steering
    /// probes; the CharacterController stays on the unit as its collider (player collisions, casts, selection).
    /// </summary>
    public static class GroundMotor
    {
        /// <summary>Max rise per horizontal metre (~55 degrees), like the units' CharacterController slope limit.</summary>
        private const float MaxClimb = 1.43f;
        private const float SeparationStrength = 6f;

        /// <summary>Moves the unit; returns the horizontal velocity actually applied.</summary>
        public static Vector3 Move(Transform t, int gridId, Vector3 desiredVelocity, float radius, float dt)
        {
            Vector3 p = t.position;
            Vector3 v = desiredVelocity;
            v.y = 0f;
            if (gridId >= 0) v += UnitSpatialGrid.Separation(gridId, p, radius, SeparationStrength);
            if (v.sqrMagnitude < 1e-6f || dt <= 0f) return Vector3.zero;

            float maxSpeed = Mathf.Max(desiredVelocity.magnitude * 1.25f, 2f);
            if (v.sqrMagnitude > maxSpeed * maxSpeed) v = v.normalized * maxSpeed;

            Vector3 np = p + v * dt;
            float ground = RtsUnit.GetTerrainHeight(np);
            float run = v.magnitude * dt;
            if (ground - p.y > run * MaxClimb + 0.05f)
            {
                // Cliff ahead: stay (but keep feet on the ground below)
                np = p;
                ground = RtsUnit.GetTerrainHeight(p);
                v = Vector3.zero;
            }
            np.y = ground;
            t.position = np;
            return v;
        }

        /// <summary>Keeps an idle unit on the ground (terrain may have been regenerated under it).</summary>
        public static void Settle(Transform t)
        {
            Vector3 p = t.position;
            float ground = RtsUnit.GetTerrainHeight(p);
            if (Mathf.Abs(p.y - ground) > 0.02f) t.position = new Vector3(p.x, ground, p.z);
        }
    }
}
