using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.AI
{
    /// <summary>
    /// What an NPC kingdom's hall has round it: houses, fields, a sawmill, mines and from hall III a ring of walls with four
    /// ways through. How much stands follows its people and its hall (Wanted), where it stands follows its name, so the same
    /// town comes back each time the kingdom turns real and nothing needs saving. For now they are its look and things in the
    /// way; the kingdom's numbers still come from KingdomEconomy (docs/GAME_DESIGN.md §18.4).
    /// </summary>
    public sealed class KingdomTown : MonoBehaviour
    {
        public enum Kind { House, Field, Sawmill, Mine, Wall, Pier }

        private struct Spot { public Kind Kind; public Vector3 At; public float Yaw, Half; public int Segment; }

        private const int MaxHouses = 24, MaxFields = 8, MaxSawmills = 2, MaxMines = 2, RaisedPerFrame = 3;
        /// <summary>The ring: whole wall segments from pier to pier, a way through on each of the four axes, GateSegments each side of it.</summary>
        private const int RingSegments = 96, GateSegments = 2;
        private const float RingStep = 360f / RingSegments;
        /// <summary>From the hall to the piers; the segments are WallBuilder.Length long, so nothing shows between them.</summary>
        public static readonly float WallRingRadius = WallBuilder.Length * 0.5f / Mathf.Sin(RingStep * 0.5f * Mathf.Deg2Rad);
        /// <summary>How far from the wall's line a unit heading through a way lines up, and half the way's width it may be off its axis.</summary>
        private const float GateApproach = 4f;
        private static readonly float GateHalfWidth = GateSegments * WallBuilder.Length;

        /// <summary>Ground every standing town holds: no tree grows on it (ProceduralBiomeChunk).</summary>
        private static readonly List<KingdomTown> towns = new List<KingdomTown>();

        private NpcKingdom kingdom;
        private readonly List<Spot>[] plan = new List<Spot>[6];
        private readonly int[] raised = new int[6];
        private readonly List<Spot> claimed = new List<Spot>();
        private Vector2 centre;
        private readonly bool[] segmentUp = new bool[RingSegments];
        private readonly List<Mesh> ownMeshes = new List<Mesh>();
        private int lastHall = -1, lastPeople = -1;

        /// <summary>How many of a kind stand for this many people under a hall of this level.</summary>
        public static int Wanted(Kind kind, int hall, int people)
        {
            if (hall <= 0) return 0;
            switch (kind)
            {
                case Kind.House: return Mathf.Clamp(people / Mathf.Max(1, GameBalance.TownPeoplePerHouse), 1, MaxHouses);
                case Kind.Field: return Mathf.Clamp(people / Mathf.Max(1, GameBalance.TownPeoplePerField), 1, MaxFields);
                case Kind.Sawmill: return Mathf.Clamp(people / 25 + 1, 1, MaxSawmills);
                case Kind.Mine: return hall >= 2 ? Mathf.Clamp(hall - 1, 1, MaxMines) : 0;
                case Kind.Wall:
                case Kind.Pier: return hall >= GameBalance.TownWallHall ? int.MaxValue : 0;
                default: return 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => towns.Clear();

        /// <summary>A town holds this ground.</summary>
        public static bool Holds(float x, float z)
        {
            foreach (KingdomTown t in towns)
            {
                if (t == null) continue;
                float dx = x - t.centre.x, dz = z - t.centre.y, reach = WallRingRadius + 6f;
                if (dx * dx + dz * dz > reach * reach) continue;
                foreach (Spot s in t.claimed)
                    if (Mathf.Abs(s.At.x - x) < s.Half + 1.5f && Mathf.Abs(s.At.z - z) < s.Half + 1.5f) return true;
            }
            return false;
        }

        public static KingdomTown Attach(NpcKingdom kingdom)
        {
            KingdomTown town = kingdom.gameObject.AddComponent<KingdomTown>();
            town.kingdom = kingdom;
            town.MakePlan();
            return town;
        }

        private void OnEnable() { if (!towns.Contains(this)) towns.Add(this); }
        private void OnDisable() => towns.Remove(this);

        private void OnDestroy()
        {
            foreach (Mesh m in ownMeshes) if (m != null) Destroy(m);
        }

        // ------------------------------------------------------------------ the ring in the way of the kinematic units

        private bool Walled => raised[(int)Kind.Wall] > 0;

        private static bool IsWay(float degrees)
        {
            float axis = Mathf.Round(degrees / 90f) * 90f;
            return Mathf.Abs(Mathf.DeltaAngle(degrees, axis)) < GateSegments * RingStep;
        }

        /// <summary>
        /// Where a unit at from should head for to, with the kingdoms' rings round their halls: to itself when no ring is in
        /// the way, else the nearest way through it, or round it when it only lies across the road (a ring has four ways
        /// and is a plain circle, so this needs no graph, unlike the player's own walls).
        /// </summary>
        public static Vector3 Route(Vector3 from, Vector3 to)
        {
            for (int i = 0; i < towns.Count; i++)
                if (towns[i] != null && towns[i].Walled && towns[i].RouteRound(from, to, out Vector3 via)) return via;
            return to;
        }

        private bool RouteRound(Vector3 from, Vector3 to, out Vector3 via)
        {
            via = to;
            Vector2 a = new Vector2(from.x - centre.x, from.z - centre.y), b = new Vector2(to.x - centre.x, to.z - centre.y), ab = b - a;
            float R = WallRingRadius, ra = a.magnitude, rb = b.magnitude;
            bool aIn = ra < R, bIn = rb < R;
            if (aIn && bIn) return false;
            if (!aIn && !bIn)
            {
                // Both outside: only a road that cuts through the ring goes round it
                float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(-Vector2.Dot(a, ab) / ab.sqrMagnitude) : 0f;
                Vector2 nearest = a + ab * u;
                if (nearest.sqrMagnitude >= (R + 1.5f) * (R + 1.5f)) return false;
                Vector2 side = nearest.sqrMagnitude > 1f ? nearest.normalized : new Vector2(-ab.y, ab.x).normalized;
                via = To3(side * (R + GateApproach + 1f), from.y);
                return true;
            }
            // Across the ring: where the road meets it
            float qa = ab.sqrMagnitude, qb = 2f * Vector2.Dot(a, ab), qc = a.sqrMagnitude - R * R;
            float t = qa > 1e-6f ? (-qb + (aIn ? 1f : -1f) * Mathf.Sqrt(Mathf.Max(0f, qb * qb - 4f * qa * qc))) / (2f * qa) : 0f;
            Vector2 meet = a + ab * Mathf.Clamp01(t);
            float meetAngle = Mathf.Atan2(meet.y, meet.x) * Mathf.Rad2Deg;
            if (IsWay(meetAngle) || !UpAt(meetAngle)) return false;
            // The way that makes the shortest road
            Vector2 best = Vector2.zero;
            float bestLength = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                float rad = k * Mathf.PI * 0.5f;
                Vector2 axis = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                float length = (a - axis * R).magnitude + (b - axis * R).magnitude;
                if (length < bestLength) { bestLength = length; best = axis; }
            }
            Vector2 across = new Vector2(-best.y, best.x);
            bool inCorridor = Mathf.Abs(Vector2.Dot(a, across)) < GateHalfWidth - 1f && Mathf.Abs(ra - R) < GateApproach + 1f;
            // In front of the way, then out behind it
            via = To3(best * (R + (aIn == inCorridor ? GateApproach : -GateApproach)), from.y);
            return true;
        }

        private Vector3 To3(Vector2 local, float y) => new Vector3(centre.x + local.x, y, centre.y + local.y);

        private bool UpAt(float degrees) => segmentUp[Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(degrees, 360f) / RingStep), 0, RingSegments - 1)];

        /// <summary>The step from p against the rings: one deeper into a standing wall slides along it, one out of it is free.</summary>
        public static Vector3 Constrain(Vector3 p, Vector3 step, float radius)
        {
            for (int i = 0; i < towns.Count; i++)
            {
                KingdomTown t = towns[i];
                if (t == null || !t.Walled) continue;
                Vector2 to = new Vector2(p.x + step.x - t.centre.x, p.z + step.z - t.centre.y);
                float r = to.magnitude, room = WallBuilder.Thickness * 0.5f + radius;
                if (Mathf.Abs(r - WallRingRadius) >= room || r < 1f) continue;
                float degrees = Mathf.Atan2(to.y, to.x) * Mathf.Rad2Deg;
                if (!t.UpAt(degrees)) continue;
                Vector2 from = new Vector2(p.x - t.centre.x, p.z - t.centre.y);
                if (Mathf.Abs(from.magnitude - WallRingRadius) <= Mathf.Abs(r - WallRingRadius)) continue; // on its way out
                Vector2 outward = from.magnitude > 1e-3f ? from.normalized : to / r;
                float into = step.x * outward.x + step.z * outward.y;
                step -= new Vector3(outward.x, 0f, outward.y) * into;
            }
            return step;
        }

        /// <summary>How many stand now, of a kind.</summary>
        public int Standing(Kind kind) => raised[(int)kind];

        /// <summary>A stable number from a name (string.GetHashCode changes between runs).</summary>
        public static int Seed(string name)
        {
            int h = 17;
            foreach (char c in name) h = unchecked(h * 31 + c);
            return h;
        }

        private static bool Buildable(Vector3 at, float half)
        {
            if (Lakes.InWater(at.x, at.z, half + 1f) || (WorldSettings.Bounded && !WorldSettings.Inside(at, half + 10f))) return false;
            float low = float.MaxValue, high = float.MinValue;
            foreach (float dx in new[] { -half, half })
                foreach (float dz in new[] { -half, half })
                {
                    float h = InfiniteWorldManager.GetSampledTerrainHeight(at.x + dx, at.z + dz);
                    low = Mathf.Min(low, h);
                    high = Mathf.Max(high, h);
                }
            return high - low <= half * 0.6f + 0.5f;
        }

        /// <summary>The places for all it can ever have, nearest the hall first for houses, further out for fields; the same for the same name.</summary>
        private void MakePlan()
        {
            for (int i = 0; i < plan.Length; i++) plan[i] = new List<Spot>();
            var rng = new System.Random(Seed(kingdom.KingdomName));
            Vector3 home = kingdom.Home;
            centre = new Vector2(home.x, home.z);
            var taken = new List<Spot>();

            void Take(Kind kind, int count, float half, float from, float to)
            {
                int left = count, tries = count * 24;
                float r = from;
                while (left > 0 && tries-- > 0)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, ring = r + (float)rng.NextDouble() * 6f;
                    r = Mathf.Min(to, r + 0.7f);
                    Vector3 at = home + new Vector3(Mathf.Cos(a) * ring, 0f, Mathf.Sin(a) * ring);
                    bool free = Buildable(at, half);
                    foreach (Spot s in taken)
                        if (Mathf.Abs(s.At.x - at.x) < s.Half + half + 1.5f && Mathf.Abs(s.At.z - at.z) < s.Half + half + 1.5f) free = false;
                    if (!free) continue;
                    at.y = InfiniteWorldManager.GetSampledTerrainHeight(at.x, at.z);
                    var spot = new Spot { Kind = kind, At = at, Yaw = Mathf.Round(a * Mathf.Rad2Deg / 45f) * 45f, Half = half };
                    taken.Add(spot);
                    plan[(int)kind].Add(spot);
                    left--;
                }
            }

            // All inside the ring, corners too
            Take(Kind.House, MaxHouses, 2f, 13f, 34f);
            Take(Kind.Sawmill, MaxSawmills, 4f, 22f, 42f);
            Take(Kind.Mine, MaxMines, 3f, 25f, 44f);
            Take(Kind.Field, MaxFields, 7f, 28f, 42f);

            // The wall's ring: whole segments round the hall, four ways through
            for (int i = 0; i < RingSegments; i++)
            {
                float a0 = i * RingStep, mid = a0 + RingStep * 0.5f;
                if (IsWay(mid)) continue;
                Vector3 at = RingPoint(mid, WallRingRadius * Mathf.Cos(RingStep * 0.5f * Mathf.Deg2Rad)), pier = RingPoint(a0, WallRingRadius);
                if (Lakes.InWater(at.x, at.z, 1f)) continue;
                // Length along the ring's tangent (a yaw turns +X to (cos, 0, -sin))
                plan[(int)Kind.Wall].Add(new Spot { Kind = Kind.Wall, At = at, Yaw = 90f - mid, Half = WallBuilder.Length * 0.5f, Segment = i });
                plan[(int)Kind.Pier].Add(new Spot { Kind = Kind.Pier, At = pier, Yaw = -a0, Half = 1.1f });
                if (IsWay(mid + RingStep))
                    plan[(int)Kind.Pier].Add(new Spot { Kind = Kind.Pier, At = RingPoint(a0 + RingStep, WallRingRadius), Yaw = -a0, Half = 1.1f });
            }
            claimed.AddRange(taken);
            claimed.AddRange(plan[(int)Kind.Wall]);
            claimed.AddRange(plan[(int)Kind.Pier]);

            Vector3 RingPoint(float degrees, float radius)
            {
                float rad = degrees * Mathf.Deg2Rad;
                Vector3 p = home + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
                p.y = InfiniteWorldManager.GetSampledTerrainHeight(p.x, p.z);
                return p;
            }
        }

        private void Update()
        {
            if (kingdom == null) return;
            int hall = kingdom.Economy.HallLevel, people = kingdom.Population;
            if (hall == lastHall && people == lastPeople && !Unfinished) return;
            lastHall = hall;
            lastPeople = people;
            int budget = RaisedPerFrame;
            foreach (Kind kind in new[] { Kind.House, Kind.Field, Kind.Sawmill, Kind.Mine, Kind.Wall, Kind.Pier })
            {
                int want = Mathf.Min(Wanted(kind, hall, people), plan[(int)kind].Count);
                while (raised[(int)kind] < want && budget > 0)
                {
                    Raise(plan[(int)kind][raised[(int)kind]]);
                    raised[(int)kind]++;
                    budget--;
                }
            }
            unfinished = budget == 0;
        }

        private bool unfinished;
        private bool Unfinished => unfinished;

        /// <summary>Puts one up at once (a test's way to see all of it; Update does a few a frame).</summary>
        public void RaiseAllNow()
        {
            int hall = kingdom.Economy.HallLevel, people = kingdom.Population;
            foreach (Kind kind in new[] { Kind.House, Kind.Field, Kind.Sawmill, Kind.Mine, Kind.Wall, Kind.Pier })
            {
                int want = Mathf.Min(Wanted(kind, hall, people), plan[(int)kind].Count);
                while (raised[(int)kind] < want) { Raise(plan[(int)kind][raised[(int)kind]]); raised[(int)kind]++; }
            }
        }

        private void Raise(Spot s)
        {
            var go = new GameObject("Town" + s.Kind);
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(s.At, Quaternion.Euler(0f, s.Yaw, 0f));
            Vector3 colliderSize = Vector3.zero, colliderCentre = Vector3.zero;
            switch (s.Kind)
            {
                case Kind.House:
                    WorkerHouseBuilder.Attach(go.transform);
                    colliderSize = new Vector3(3.6f, 3f, 3.6f);
                    colliderCentre = new Vector3(0f, 1.5f, 0f);
                    ChimneySmoke.Ensure(go);
                    break;
                case Kind.Field:
                    Mesh draped = ZoneBuildingBuilder.Draped(ZoneBuildingBuilder.FieldModel, go.transform);
                    ownMeshes.Add(draped);
                    Model(go, draped);
                    break;
                case Kind.Sawmill:
                    Model(go, ZoneBuildingBuilder.SawmillModel);
                    colliderSize = new Vector3(7f, 3.4f, 6f);
                    colliderCentre = new Vector3(0f, 1.7f, 0f);
                    break;
                case Kind.Mine:
                    MineBuilder.Attach(go.transform);
                    colliderSize = new Vector3(4.2f, 2.8f, 3.5f);
                    colliderCentre = new Vector3(0f, 1.3f, -0.45f);
                    break;
                case Kind.Wall:
                    WallBuilder.Attach(go.transform, WallBuilder.Kind.Wall);
                    colliderSize = new Vector3(WallBuilder.Length, WallBuilder.WalkY + 0.6f, WallBuilder.Thickness);
                    colliderCentre = new Vector3(0f, (WallBuilder.WalkY + 0.6f) * 0.5f, 0f);
                    segmentUp[s.Segment] = true;
                    break;
                case Kind.Pier:
                    WallBuilder.AttachPier(go.transform);
                    break;
            }
            if (colliderSize != Vector3.zero)
            {
                BoxCollider col = go.AddComponent<BoxCollider>();
                col.size = colliderSize;
                col.center = colliderCentre;
            }
        }

        private static void Model(GameObject go, Mesh mesh)
        {
            Material look = MeshKit.PropMaterial();
            if (look == null) return;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = look;
        }
    }
}
