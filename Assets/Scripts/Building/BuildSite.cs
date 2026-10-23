using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.World;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Where the world itself puts settlements (NPC kingdoms, raider camps): the spot nearest the wanted one where the
    /// ground under a square footprint is level enough and below the snow line - by the rule the player builds by
    /// (BuildingPlacementSystem.GroundRise), so a hall is not dropped on a hillside or a camp onto a ridge.
    /// </summary>
    public static class BuildSite
    {
        /// <summary>
        /// Rings every <paramref name="step"/> m out to <paramref name="radius"/> round <paramref name="near"/>: the first
        /// spot whose ground rises at most <paramref name="maxRise"/> across <paramref name="footprint"/> (true), else the
        /// most level one seen (false). The site's y is the height to build at.
        /// </summary>
        public static bool FindLevel(Vector3 near, float footprint, float radius, float maxRise, out Vector3 site, float step = 8f)
        {
            return FindLevel(near, footprint, radius, maxRise, RtsUnit.GetTerrainHeight, out site, step);
        }

        public static bool FindLevel(Vector3 near, float footprint, float radius, float maxRise, System.Func<Vector3, float> groundAt, out Vector3 site, float step = 8f)
        {
            var size = new Vector3(footprint, 0f, footprint);
            float bestRise = float.MaxValue;
            site = near;
            for (float r = 0f; r <= radius + 0.01f; r += step)
            {
                int around = r <= 0f ? 1 : Mathf.Max(6, Mathf.CeilToInt(2f * Mathf.PI * r / step));
                for (int k = 0; k < around; k++)
                {
                    float a = k * Mathf.PI * 2f / around;
                    Vector3 p = near + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    float rise = BuildingPlacementSystem.GroundRise(p, size, groundAt, out float h);
                    if (h > ProceduralBiomeChunk.SnowLineHeight - 10f) continue;
                    p.y = h;
                    if (rise <= maxRise) { site = p; return true; }
                    if (rise < bestRise) { bestRise = rise; site = p; }
                }
            }
            if (bestRise == float.MaxValue) site.y = groundAt(near);
            return false;
        }
    }
}
