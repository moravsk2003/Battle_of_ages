using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Utils;

namespace BattleOfAges.RTS
{
    /// <summary>
    /// On the map every unit stands on a ring of its side's colour (own blue, camps red, each kingdom its own; wildlife
    /// none), the selected ones on a bigger bright one, and a warrior, knight, general or hero has a rank badge over its
    /// head. Rings and badges never shrink below a few screen pixels, so zoomed out a unit is still a coloured dot one
    /// can see and click near. Drawn instanced from the unit grid each frame: no objects per unit.
    /// </summary>
    public sealed class TeamMarkers : MonoBehaviour
    {
        /// <summary>The ring is at least this many pixels across, the badge this high.</summary>
        public const float RingPixels = 22f, BadgePixels = 12f;
        private const float RingMetres = 1.3f, BadgeMetres = 0.45f;

        public static readonly Color Own = new Color(0.25f, 0.55f, 1f), Selected = new Color(0.55f, 1f, 0.45f), Camp = new Color(0.9f, 0.18f, 0.15f);
        private static readonly Color[] Kingdoms =
        {
            new Color(1f, 0.55f, 0.1f), new Color(0.7f, 0.3f, 0.95f), new Color(1f, 0.9f, 0.2f),
            new Color(0.1f, 0.85f, 0.8f), new Color(0.95f, 0.4f, 0.7f), new Color(0.6f, 0.45f, 0.25f)
        };
        private static readonly Color[] RankColours = { Color.clear, new Color(0.8f, 0.82f, 0.88f), new Color(1f, 0.78f, 0.2f), new Color(0.75f, 0.35f, 1f) };
        private static readonly Color HeroBadge = new Color(1f, 0.95f, 0.6f);

        private static Mesh ring, badge;
        private readonly Dictionary<Color, List<Matrix4x4>> ringBatches = new Dictionary<Color, List<Matrix4x4>>();
        private readonly Dictionary<Color, List<Matrix4x4>> badgeBatches = new Dictionary<Color, List<Matrix4x4>>();
        private readonly Matrix4x4[] chunk = new Matrix4x4[1023];

        /// <summary>Rings, of them selected, and badges drawn in the last frame (tests).</summary>
        public int LastRings { get; private set; }
        public int LastSelected { get; private set; }
        public int LastBadges { get; private set; }

        // What each grid id is, looked up once per owner
        private Component[] seen = new Component[0];
        private UnitProgress[] progress = new UnitProgress[0];
        private RtsUnit[] rtsUnits = new RtsUnit[0];
        private NpcResourceGatherer[] gatherers = new NpcResourceGatherer[0];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<TeamMarkers>() != null) return;
            var go = new GameObject("TeamMarkers");
            DontDestroyOnLoad(go);
            go.AddComponent<TeamMarkers>();
        }

        /// <summary>The colour of a side's ring (clear: no ring).</summary>
        public static Color SideColour(int team) =>
            team == UnitSpatialGrid.TeamPlayer ? Own
            : team == UnitSpatialGrid.TeamHostile ? Camp
            : team >= UnitSpatialGrid.TeamKingdomFirst ? Kingdoms[(team - UnitSpatialGrid.TeamKingdomFirst) % Kingdoms.Length]
            : Color.clear;

        /// <summary>World size of a marker: its own size up close, never under the given pixels on screen.</summary>
        public static float WorldSize(float metres, float pixels, float orthoSize, float screenHeight) =>
            Mathf.Max(metres, pixels * 2f * orthoSize / Mathf.Max(1f, screenHeight));

        private void LateUpdate() => SubmitFor(Camera.main, null);

        /// <summary>Queues this frame's rings and badges as seen by the view camera: for every camera (target null), or only the target (a capture).</summary>
        public void SubmitFor(Camera cam, Camera target)
        {
            LastRings = LastSelected = LastBadges = 0;
            if (cam == null || !cam.orthographic) return; // the map only (first person has no rings)
            if (ring == null) { ring = MakeRing(0.55f, 1f, 32); badge = MakeDiamond(); }

            foreach (var list in ringBatches.Values) list.Clear();
            foreach (var list in badgeBatches.Values) list.Clear();
            Grow(UnitSpatialGrid.Capacity);

            float ringSize = WorldSize(RingMetres, RingPixels, cam.orthographicSize, Screen.height);
            float badgeSize = WorldSize(BadgeMetres, BadgePixels, cam.orthographicSize, Screen.height);
            Quaternion facing = cam.transform.rotation;
            UnitSpatialGrid.EnsureCurrent();
            for (int id = 0; id < UnitSpatialGrid.Capacity; id++)
            {
                if (!UnitSpatialGrid.IsAlive(id)) continue;
                Component owner = UnitSpatialGrid.Owner(id);
                if (owner == null || UnitSpatialGrid.Radius(id) > 1.5f) continue; // a kingdom's hall is in the grid too
                Color side = SideColour(UnitSpatialGrid.Team(id));
                if (side.a <= 0f) continue;
                if (seen[id] != owner) Learn(id, owner);
                else if (progress[id] == null && (Time.frameCount + id) % 30 == 0) progress[id] = owner.GetComponent<UnitProgress>(); // ranked later (a unit's Start)
                if (gatherers[id] != null && gatherers[id].IsInsideMine) continue; // out of sight

                Vector3 at = UnitSpatialGrid.Position(id);
                bool selected = rtsUnits[id] != null && rtsUnits[id].IsSelected;
                float size = selected ? ringSize * 1.25f : ringSize;
                LastRings++;
                if (selected) LastSelected++;
                Add(ringBatches, selected ? Selected : side, Matrix4x4.TRS(at + Vector3.up * 0.12f, Quaternion.identity, new Vector3(size, 1f, size) * 0.5f));

                UnitProgress p = progress[id];
                if (p == null || (!p.IsHero && p.Rank == UnitRank.Person)) continue;
                Color rank = p.IsHero ? HeroBadge : RankColours[(int)p.Rank];
                float b = p.IsHero ? badgeSize * 1.4f : badgeSize;
                LastBadges++;
                Add(badgeBatches, rank, Matrix4x4.TRS(at + Vector3.up * (2.1f + b), facing, Vector3.one * b));
            }

            Draw(ring, ringBatches, target);
            Draw(badge, badgeBatches, target);
        }

        private void Learn(int id, Component owner)
        {
            seen[id] = owner;
            progress[id] = owner.GetComponent<UnitProgress>();
            rtsUnits[id] = owner as RtsUnit ?? owner.GetComponent<RtsUnit>();
            gatherers[id] = owner.GetComponent<NpcResourceGatherer>();
        }

        private void Grow(int n)
        {
            if (seen.Length >= n) return;
            int size = Mathf.Max(n, seen.Length * 2);
            System.Array.Resize(ref seen, size);
            System.Array.Resize(ref progress, size);
            System.Array.Resize(ref rtsUnits, size);
            System.Array.Resize(ref gatherers, size);
        }

        private static void Add(Dictionary<Color, List<Matrix4x4>> batches, Color c, Matrix4x4 m)
        {
            if (!batches.TryGetValue(c, out List<Matrix4x4> list)) batches[c] = list = new List<Matrix4x4>();
            list.Add(m);
        }

        private void Draw(Mesh mesh, Dictionary<Color, List<Matrix4x4>> batches, Camera target)
        {
            foreach (KeyValuePair<Color, List<Matrix4x4>> kv in batches)
            {
                if (kv.Value.Count == 0) continue;
                Material mat = RuntimeShaders.UnlitMaterial(kv.Key);
                if (mat == null) return;
                mat.enableInstancing = true;
                var rp = new RenderParams(mat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false, camera = target };
                for (int start = 0; start < kv.Value.Count; start += chunk.Length)
                {
                    int count = Mathf.Min(chunk.Length, kv.Value.Count - start);
                    kv.Value.CopyTo(start, chunk, 0, count);
                    Graphics.RenderMeshInstanced(rp, mesh, 0, chunk, count);
                }
            }
        }

        /// <summary>A flat ring in the ground plane, outer radius 1 (scaled to the size).</summary>
        public static Mesh MakeRing(float inner, float outer, int segments)
        {
            var v = new Vector3[segments * 2];
            var t = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = dir * inner; v[i * 2 + 1] = dir * outer;
                int j = (i + 1) % segments, k = i * 6;
                t[k] = i * 2; t[k + 1] = j * 2; t[k + 2] = i * 2 + 1;
                t[k + 3] = i * 2 + 1; t[k + 4] = j * 2; t[k + 5] = j * 2 + 1;
            }
            var m = new Mesh { name = "TeamRing", vertices = v, triangles = t };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A diamond facing -Z (turned to the camera), 1 high.</summary>
        private static Mesh MakeDiamond()
        {
            var m = new Mesh
            {
                name = "RankBadge",
                vertices = new[] { new Vector3(0f, 0.5f, 0f), new Vector3(0.35f, 0f, 0f), new Vector3(0f, -0.5f, 0f), new Vector3(-0.35f, 0f, 0f) },
                triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 } // both faces
            };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
