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
        private const float OutOfBuildingSpeed = 3f;
        /// <summary>A unit at a prop's edge is not in it: only a good way in counts as inside.</summary>
        private const float InsidePropShare = 0.8f;
        /// <summary>Max rise per horizontal metre (~55 degrees), like the units' CharacterController slope limit.</summary>
        private const float MaxClimb = 1.43f;

        /// <summary>
        /// Moves the unit; returns the horizontal velocity actually applied. Walls stop it (it slides along them); own:
        /// the player's side, for whom the gates are open.
        /// </summary>
        public static Vector3 Move(Transform t, int gridId, Vector3 desiredVelocity, float radius, float dt, bool own = false)
        {
            Vector3 p = t.position;
            if (dt > 0f && Building.Footprints.Inside(p, radius, out Vector3 outWay))
            {
                // Inside a building, whatever it wants: straight out
                Vector3 outStep = Vector3.ClampMagnitude(outWay, Mathf.Max(desiredVelocity.magnitude, OutOfBuildingSpeed) * dt);
                Vector3 outAt = p + outStep;
                outAt.y = RtsUnit.GetTerrainHeight(outAt);
                t.position = outAt;
                return outStep / dt;
            }
            if (dt > 0f && World.SolidProps.Inside(p, radius * InsidePropShare, out Vector3 outOfProp))
            {
                // In a trunk or a rock (shoved there, or it grew round it): out of it first
                Vector3 outStep = Vector3.ClampMagnitude(outOfProp, Mathf.Max(desiredVelocity.magnitude, OutOfBuildingSpeed) * dt);
                Vector3 outAt = p + outStep;
                outAt.y = RtsUnit.GetTerrainHeight(outAt);
                t.position = outAt;
                return outStep / dt;
            }
            Vector3 v = desiredVelocity;
            v.y = 0f;
            if (gridId >= 0) v += UnitSpatialGrid.Separation(gridId, p, radius, v.sqrMagnitude > 0.01f ? Combat.GameBalance.WalkerSeparation : Combat.GameBalance.StandingSeparation);
            if (v.sqrMagnitude < 1e-6f || dt <= 0f) return Vector3.zero;

            float maxSpeed = Mathf.Max(desiredVelocity.magnitude * 1.25f, 2f);
            if (v.sqrMagnitude > maxSpeed * maxSpeed) v = v.normalized * maxSpeed;

            Vector3 step = Building.WallBarriers.Constrain(p, v * dt, radius, own);
            step = AI.KingdomTown.Constrain(p, step, radius);
            step = World.Lakes.Constrain(p, step, radius); // nobody swims: along the shore instead
            step = World.SolidProps.Constrain(p, step, radius); // nobody walks into a tree or a rock: along its edge instead
            if (step.sqrMagnitude < 1e-10f) return Vector3.zero;
            v = step / dt;
            Vector3 np = p + step;
            // Not through a building: along the wall, or it stays
            if (Building.Footprints.Inside(np, radius, out _))
            {
                Vector3 alongX = new Vector3(p.x + step.x, p.y, p.z), alongZ = new Vector3(p.x, p.y, p.z + step.z);
                if (!Building.Footprints.Inside(alongX, radius, out _)) np = alongX;
                else if (!Building.Footprints.Inside(alongZ, radius, out _)) np = alongZ;
                else return Vector3.zero;
                v = (np - p) / dt;
            }
            float ground = RtsUnit.GetTerrainHeight(np);
            float run = v.magnitude * dt;
            if (ground - p.y > run * MaxClimb + 0.05f)
            {
                // Cliff ahead: stay (but keep feet on the ground below)
                np = p;
                ground = RtsUnit.GetTerrainHeight(p);
                v = Vector3.zero;
            }
            np = World.WorldSettings.ClampInside(np); // not up the mountains round the world
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
