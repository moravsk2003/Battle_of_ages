using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.World
{
    /// <summary>
    /// The campaign's world (docs/GAME_DESIGN.md §5.1, M1): its seed — the noise of the relief, the biomes and every
    /// chunk's content shifts with it — and its size, a square with a mountain wall along the edge that nobody walks
    /// past. Seed 0 and size 0 are the old fixed, endless world (the editor, tests, saves from before 0.2.0).
    /// </summary>
    public static class WorldSettings
    {
        /// <summary>A new campaign's side in metres (the setup screen and tests choose; §5.1).</summary>
        public const float DefaultSize = 16000f;
        /// <summary>The mountain wall: it rises over this band inside the edge, to EdgeRise metres.</summary>
        public const float EdgeBand = 400f, EdgeRise = 150f;

        public static int Seed { get; private set; }
        /// <summary>The world's side in metres, the mountain wall included; 0 = endless.</summary>
        public static float Size { get; private set; }
        public static bool Bounded => Size > 0f;
        /// <summary>Half the side of the square one can walk in (the wall starts here).</summary>
        public static float PlayableHalf => Mathf.Max(0f, Size * 0.5f - EdgeBand);
        /// <summary>Added to world x/z before sampling the world's noise.</summary>
        public static Vector2 Shift { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Set(0, 0f);

        public static void Set(int seed, float size)
        {
            Seed = seed;
            Size = Mathf.Max(0f, size);
            Shift = ShiftOf(seed);
            InfiniteWorldManager.ResetHeightCaches();
            Lakes.Rebuild();
        }

        /// <summary>A seed for a new campaign (never 0, which is the old fixed world).</summary>
        public static int NewSeed() => new System.Random().Next(1, int.MaxValue);

        public static Vector2 ShiftOf(int seed)
        {
            if (seed == 0) return Vector2.zero;
            var r = new System.Random(seed);
            return new Vector2((float)r.NextDouble() * 4000f, (float)r.NextDouble() * 4000f);
        }

        /// <summary>A chunk's own seed for its trees, ore, camps and animals, different in every world.</summary>
        public static int ChunkSeed(Vector2Int coord) => Mathf.Abs((coord.x * 73856093) ^ (coord.y * 19349663) ^ (Seed * 83492791));

        /// <summary>How far past the playable square's edge (negative inside; -infinity in an endless world).</summary>
        public static float OutsideBy(float x, float z) => Bounded ? Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - PlayableHalf : float.NegativeInfinity;

        public static bool Inside(Vector3 p, float margin = 0f) => OutsideBy(p.x, p.z) <= -margin;

        /// <summary>The point moved inside the playable square (by margin from its edge).</summary>
        public static Vector3 ClampInside(Vector3 p, float margin = 2f)
        {
            if (!Bounded) return p;
            float h = Mathf.Max(0f, PlayableHalf - margin);
            p.x = Mathf.Clamp(p.x, -h, h);
            p.z = Mathf.Clamp(p.z, -h, h);
            return p;
        }

        /// <summary>The wall's height at a point: nothing inside, rising steeply (and ragged) over the edge band.</summary>
        public static float EdgeHeight(float x, float z)
        {
            float out_ = OutsideBy(x, z);
            if (out_ <= 0f) return 0f;
            float t = Mathf.Clamp01(out_ / EdgeBand);
            float rise = t * t * (3f - 2f * t);
            float ragged = 0.75f + 0.5f * Mathf.PerlinNoise((x + Shift.x) * 0.011f + 31f, (z + Shift.y) * 0.011f + 17f);
            return rise * EdgeRise * ragged;
        }

        /// <summary>What moves as one: an object with anything on it; a bare folder ("Trees") hands down to its children.</summary>
        private static void Collect(Transform t, List<Transform> into, int depth)
        {
            if (t == null) return;
            bool ground = t.name == BaseGroundName; // its mesh is remade (Reground), what stands on it moves
            bool bare = t.childCount > 0 && t.GetComponents<Component>().Length == 1;
            if (ground) into.Add(t);
            if (!ground && (!bare || depth >= 3)) { into.Add(t); return; }
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), into, depth + 1);
        }

        /// <summary>The scene's baked ground under the base (SteppeTerrainGenerator, 192 m).</summary>
        public const string BaseGroundName = "SteppeTerrain";

        /// <summary>Remakes a baked ground mesh on this world's relief and colours (its material stays; not its children's).</summary>
        public static void RebuildGround(Transform ground)
        {
            foreach (MeshFilter mf in ground.GetComponents<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Mesh mesh = Object.Instantiate(mf.sharedMesh);
                mesh.name = mf.sharedMesh.name;
                Vector3[] v = mesh.vertices;
                Color[] c = mesh.colors.Length == v.Length ? mesh.colors : new Color[v.Length];
                Transform t = mf.transform;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 w = t.TransformPoint(v[i]);
                    w.y = InfiniteWorldManager.GetSampledTerrainHeight(w.x, w.z);
                    v[i] = t.InverseTransformPoint(w);
                    c[i] = ProceduralBiomeChunk.GetBlendedBiomeColor(w.x, w.z, w.y);
                }
                mesh.vertices = v;
                mesh.colors = c;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mf.sharedMesh = mesh;
                MeshCollider mc = mf.GetComponent<MeshCollider>();
                if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = mesh; }
            }
        }

        /// <summary>
        /// Puts the scene's own objects (trees, rocks, units, props baked on the seed-0 relief) onto this world's ground,
        /// each keeping its height above it.
        /// </summary>
        public static void Reground(IList<Transform> sceneRoots)
        {
            if (Seed == 0 && !Bounded) return;
            var roots = new List<Transform>();
            foreach (Transform r in sceneRoots) Collect(r, roots, 0);
            int seed = Seed; float size = Size;
            var old = new float[roots.Count];
            Set(0, 0f);
            for (int i = 0; i < roots.Count; i++)
                if (roots[i] != null) old[i] = InfiniteWorldManager.GetSampledTerrainHeight(roots[i].position.x, roots[i].position.z);
            Set(seed, size);
            for (int i = 0; i < roots.Count; i++)
            {
                Transform t = roots[i];
                if (t == null || t is RectTransform) continue;
                if (t.name == BaseGroundName) { RebuildGround(t); continue; } // the base's own ground is remade, not moved
                float dy = InfiniteWorldManager.GetSampledTerrainHeight(t.position.x, t.position.z) - old[i];
                if (Mathf.Abs(dy) < 1e-4f) continue;
                var cc = t.GetComponent<CharacterController>();
                bool was = cc != null && cc.enabled;
                if (was) cc.enabled = false;
                t.position += Vector3.up * dy;
                if (was) cc.enabled = true;
            }
        }
    }
}
