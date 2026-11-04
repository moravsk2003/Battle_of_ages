using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.Utils;

namespace BattleOfAges.Net
{
    /// <summary>One unit in a snapshot, as it goes on the wire: position and heading quantized, health a share. 10 bytes (§14.2).</summary>
    public struct UnitRow
    {
        public const int Bytes = 10;
        public ushort Id, X, Z;
        public byte Yaw, Health, Rank, Team;

        public void Write(byte[] b, int at)
        {
            b[at] = (byte)Id; b[at + 1] = (byte)(Id >> 8);
            b[at + 2] = (byte)X; b[at + 3] = (byte)(X >> 8);
            b[at + 4] = (byte)Z; b[at + 5] = (byte)(Z >> 8);
            b[at + 6] = Yaw; b[at + 7] = Health; b[at + 8] = Rank; b[at + 9] = Team;
        }

        public static UnitRow Read(byte[] b, int at) => new UnitRow
        {
            Id = (ushort)(b[at] | b[at + 1] << 8),
            X = (ushort)(b[at + 2] | b[at + 3] << 8),
            Z = (ushort)(b[at + 4] | b[at + 5] << 8),
            Yaw = b[at + 6], Health = b[at + 7], Rank = b[at + 8], Team = b[at + 9]
        };
    }

    /// <summary>
    /// The owner's side of the snapshots (M8 step 3): ten times a second, every unit that has moved, turned or been hurt, and each
    /// of the others every RefreshSeconds (a packet may be lost, a player may have just joined), in packets that fit the fast
    /// channel. A unit gone says so a few times, with health 0.
    /// </summary>
    public sealed class UnitSnapshotWriter
    {
        public const byte Message = 1;
        public const float Interval = 0.1f, RefreshSeconds = 2f, MovedMetres = 0.2f;
        private const int Header = 1 + 4 + 2 + 2 + 1;
        /// <summary>The number a player's hero goes by (it has no place in the unit grid).</summary>
        public const ushort HeroId = 65535;
        private const int GoneRepeats = 4;
        public static readonly int RowsPerPacket = (NetLink.MaxUnreliableBytes - Header) / UnitRow.Bytes;

        /// <summary>Half the side of the square coordinates are quantized over: 16 km at 0.25 m a step.</summary>
        public const float HalfExtent = 8192f;

        private struct Known
        {
            public Component Owner;
            public ushort X, Z;
            public byte Yaw, Health, Rank, Team;
            public float SentAt;
            public int GoneLeft;
        }

        private Known[] known = new Known[512];
        private readonly byte[] packet = new byte[NetLink.MaxUnreliableBytes];
        private float nextAt;
        private uint tick;

        public int RowsSent { get; private set; }
        /// <summary>The player whose units these are: their numbers mean something only with this.</summary>
        public byte Owner { get; set; }
        /// <summary>This player's hero, sent as well.</summary>
        public HealthComponent Hero { get; set; }
        private float heroSentAt = float.NegativeInfinity;
        private UnitRow heroLast;

        public static ushort Quantize(float v) => (ushort)Mathf.Clamp(Mathf.RoundToInt((v + HalfExtent) / (2f * HalfExtent) * 65535f), 0, 65535);
        public static float Unquantize(ushort q) => q / 65535f * 2f * HalfExtent - HalfExtent;

        /// <summary>Sends what is due to everyone on the link, when it is due.</summary>
        public void Tick(NetLink link, float now)
        {
            if (now < nextAt) return;
            nextAt = now + Interval;
            tick++;
            if (known.Length < UnitSpatialGrid.Capacity) Array.Resize(ref known, Mathf.Max(UnitSpatialGrid.Capacity, known.Length * 2));
            int rows = 0;
            for (int id = 0; id < UnitSpatialGrid.Capacity; id++)
            {
                if (!Due(id, now, out UnitRow row)) continue;
                row.Write(packet, Header + rows * UnitRow.Bytes);
                RowsSent++;
                if (++rows == RowsPerPacket) { Flush(link, rows, now); rows = 0; }
            }
            if (Hero != null && HeroDue(now, out UnitRow hero))
            {
                hero.Write(packet, Header + rows * UnitRow.Bytes);
                RowsSent++;
                rows++;
            }
            if (rows > 0) Flush(link, rows, now);
        }

        private bool HeroDue(float now, out UnitRow row)
        {
            Vector3 p = Hero.transform.position;
            row = new UnitRow
            {
                Id = HeroId, X = Quantize(p.x), Z = Quantize(p.z), Yaw = (byte)Mathf.RoundToInt(Mathf.Repeat(Hero.transform.eulerAngles.y, 360f) / 360f * 255f),
                Health = Hero.IsAlive ? (byte)Mathf.Clamp(Mathf.CeilToInt((float)Hero.CurrentHealth / Mathf.Max(1, Hero.MaxHealth) * 200f), 1, 200) : (byte)0,
                Rank = (byte)UnitRank.General, Team = UnitSpatialGrid.TeamPlayer
            };
            bool same = row.X == heroLast.X && row.Z == heroLast.Z && row.Yaw == heroLast.Yaw && row.Health == heroLast.Health;
            if (same && now - heroSentAt < RefreshSeconds) return false;
            heroLast = row;
            heroSentAt = now;
            return true;
        }

        /// <summary>What a row's Rank byte says beyond a rank: a camp's raider or an animal has a look of its own.</summary>
        public const byte MobLook = 16, AnimalLook = 64;

        private static byte Look(Component owner)
        {
            if (owner is AI.PeacefulAnimalAI animal) return (byte)(AnimalLook + (int)animal.Type);
            if (owner is AI.HostileMobAI mob && mob.Kingdom == null) return (byte)(MobLook + (int)mob.MobType);
            UnitProgress p = owner.GetComponent<UnitProgress>();
            return p != null ? (byte)p.Rank : (byte)0;
        }

        private bool Due(int id, float now, out UnitRow row)
        {
            row = default;
            ref Known k = ref known[id];
            bool alive = UnitSpatialGrid.IsAlive(id);
            if (!alive)
            {
                if (k.GoneLeft <= 0) return false;
                k.GoneLeft--;
                if (k.GoneLeft == 0) k.Owner = null;
                row = new UnitRow { Id = (ushort)id, X = k.X, Z = k.Z, Yaw = k.Yaw, Health = 0, Rank = k.Rank, Team = k.Team };
                return true;
            }
            Component owner = UnitSpatialGrid.Owner(id);
            if (owner == null || owner is RemoteBody) return false; // only what is simulated here
            if (k.Owner != owner)
            {
                // A new unit (a number is used again when one dies)
                k = default;
                k.Owner = owner;
                k.Rank = Look(owner);
                k.Team = (byte)Mathf.Clamp(UnitSpatialGrid.Team(id), 0, 255);
                k.SentAt = float.NegativeInfinity;
                k.Health = 255;
            }
            Vector3 p3 = UnitSpatialGrid.Position(id);
            ushort x = Quantize(p3.x), z = Quantize(p3.z);
            byte yaw = (byte)Mathf.RoundToInt(Mathf.Repeat(owner.transform.eulerAngles.y, 360f) / 360f * 255f);
            HealthComponent hp = UnitSpatialGrid.Health(id);
            byte health = (byte)Mathf.Clamp(Mathf.CeilToInt((float)hp.CurrentHealth / Mathf.Max(1, hp.MaxHealth) * 200f), 1, 200);
            float step = MovedMetres / (2f * HalfExtent) * 65535f;
            bool changed = Mathf.Abs(x - k.X) > step || Mathf.Abs(z - k.Z) > step || Mathf.Abs(yaw - k.Yaw) > 4 || health != k.Health;
            if (!changed && now - k.SentAt < RefreshSeconds) return false;
            k.X = x; k.Z = z; k.Yaw = yaw; k.Health = health; k.SentAt = now; k.GoneLeft = GoneRepeats;
            row = new UnitRow { Id = (ushort)id, X = x, Z = z, Yaw = yaw, Health = health, Rank = k.Rank, Team = k.Team };
            return true;
        }

        private void Flush(NetLink link, int rows, float now)
        {
            packet[0] = Message;
            packet[1] = (byte)tick; packet[2] = (byte)(tick >> 8); packet[3] = (byte)(tick >> 16); packet[4] = (byte)(tick >> 24);
            ushort millis = (ushort)Mathf.RoundToInt(Mathf.Repeat(now, 60f) * 1000f);
            packet[5] = (byte)millis; packet[6] = (byte)(millis >> 8);
            packet[7] = (byte)rows; packet[8] = (byte)(rows >> 8);
            packet[9] = Owner;
            link.SendAll(NetChannel.Fast, packet, Header + rows * UnitRow.Bytes);
        }

        /// <summary>Whose units a snapshot packet tells of.</summary>
        public static byte OwnerOf(byte[] b) => b[9];

        /// <summary>The rows of a snapshot packet into the visitor; false if it is not one or is cut short.</summary>
        public static bool Read(byte[] b, int length, Action<UnitRow> visit)
        {
            if (length < Header || b[0] != Message) return false;
            int rows = b[7] | b[8] << 8;
            if (Header + rows * UnitRow.Bytes > length) return false;
            for (int i = 0; i < rows; i++) visit(UnitRow.Read(b, Header + i * UnitRow.Bytes));
            return true;
        }
    }

    /// <summary>
    /// Someone else's unit or building here: it can be struck, by units through the grid or by a hero's blow, but its health is its
    /// owner's. A hit goes to the owner (NetSession.SendHit); what comes back in the snapshots is the truth. It never dies here.
    /// </summary>
    public sealed class RemoteBody : MonoBehaviour
    {
        public int Owner;
        public int Id;
        public bool IsBuilding;
        public HealthComponent Health { get; private set; }

        public void Setup(int owner, int id, bool building, int health)
        {
            Owner = owner;
            Id = id;
            IsBuilding = building;
            Health = gameObject.AddComponent<HealthComponent>();
            Health.InitializeHealth(health);
            Health.OnTakeDamage += OnHit;
        }

        private void OnHit(int damage)
        {
            NetSession.Current?.SendHit(Owner, Id, IsBuilding, damage);
            if (Health.CurrentHealth < 1) Health.SetCurrentHealth(1); // its owner decides when it falls
        }

        private void OnDestroy() { if (Health != null) Health.OnTakeDamage -= OnHit; }
    }

    /// <summary>
    /// A player's view of units it does not simulate (M8 step 3): a body for each, a little behind the snapshots so it moves between
    /// two known places. No AI: only what it looks like and something to strike. A swing is not shown yet.
    /// </summary>
    public sealed class RemoteUnits : MonoBehaviour
    {
        private const float RenderDelay = 0.15f, ForgetAfter = 6f;
        private const int SpawnsPerFrame = 40;

        private sealed class Body
        {
            public Transform Root;
            public Vector3 From, To;
            public float FromAt, ToAt, FromYaw, ToYaw, LastHeard;
            public byte Rank, Team, Health;
            public RemoteBody Mark;
            public int GridId = -1;
        }

        /// <summary>The side of another player's units here (each player's own are TeamPlayer on its machine).</summary>
        public static int TeamOf(int rowTeam, int owner) => rowTeam == UnitSpatialGrid.TeamPlayer ? RemotePlayerTeam + owner : rowTeam;
        public const int RemotePlayerTeam = 1000;

        /// <summary>Other players' heroes as they are here.</summary>
        public void ForEachHero(System.Action<RemoteBody> visit)
        {
            foreach (KeyValuePair<int, Body> kv in bodies)
                if ((kv.Key & 0xFFFF) == UnitSnapshotWriter.HeroId && kv.Value.Mark != null) visit(kv.Value.Mark);
        }

        private readonly Dictionary<int, Body> bodies = new Dictionary<int, Body>();
        private readonly Dictionary<int, UnitRow> waiting = new Dictionary<int, UnitRow>();
        private readonly List<int> forget = new List<int>();
        private Action<UnitRow> onRow;
        private int owner;

        /// <summary>A body's key: a unit's number means something only with its owner.</summary>
        public static int Key(int owner, ushort id) => owner << 16 | id;

        /// <summary>This player: its own units, should they come back to it, are not shown twice.</summary>
        public byte? Self { get; set; }

        /// <summary>Show the bodies (false: numbers only, for tests of the wire).</summary>
        public bool WithModels { get; set; } = true;
        public int Count => bodies.Count;

        /// <summary>A snapshot packet has come.</summary>
        public bool Apply(byte[] data, int length)
        {
            if (onRow == null) onRow = OnRow;
            if (length < 10) return false;
            owner = UnitSnapshotWriter.OwnerOf(data);
            if (Self.HasValue && owner == Self.Value) return true;
            return UnitSnapshotWriter.Read(data, length, onRow);
        }

        private void OnRow(UnitRow row)
        {
            int key = Key(owner, row.Id);
            if (row.Health == 0)
            {
                waiting.Remove(key);
                Remove(key);
                return;
            }
            if (bodies.TryGetValue(key, out Body b) && b.Rank == row.Rank && b.Team == row.Team) Retarget(b, row);
            else
            {
                if (b != null) Remove(key); // the number has passed to another unit
                waiting[key] = row; // a few a frame (a sudden army was a hitch)
            }
        }

        private void Retarget(Body b, UnitRow row)
        {
            float now = Time.time;
            b.From = b.Root.position;
            b.FromYaw = b.Root.eulerAngles.y;
            b.FromAt = now;
            b.To = PlaceOf(row);
            b.ToYaw = row.Yaw / 255f * 360f;
            b.ToAt = now + RenderDelay;
            b.LastHeard = now;
            b.Health = row.Health;
            HealthComponent hp = b.Mark.Health;
            hp.SetCurrentHealth(Mathf.Max(1, Mathf.RoundToInt(row.Health / 200f * hp.MaxHealth)));
        }

        private static Vector3 PlaceOf(UnitRow row)
        {
            float x = UnitSnapshotWriter.Unquantize(row.X), z = UnitSnapshotWriter.Unquantize(row.Z);
            return new Vector3(x, RtsUnit.GetTerrainHeight(new Vector3(x, 0f, z)), z);
        }

        private void Spawn(int key, UnitRow row)
        {
            // Each a root of its own: a thousand moving children of one parent all go through one hierarchy
            var go = new GameObject("Remote_" + row.Id);
            go.transform.SetPositionAndRotation(PlaceOf(row), Quaternion.Euler(0f, row.Yaw / 255f * 360f, 0f));
            if (WithModels) DressBody(go, row);
            var b = new Body { Root = go.transform, Rank = row.Rank, Team = row.Team, Health = row.Health, LastHeard = Time.time };
            int who = key >> 16;
            int rank = row.Rank < UnitSnapshotWriter.MobLook ? Mathf.Clamp(row.Rank, 0, GameBalance.RankHealth.Length - 1) : 1;
            b.Mark = go.AddComponent<RemoteBody>();
            b.Mark.Setup(who, row.Id, false, GameBalance.RankHealth[rank] * (row.Id == UnitSnapshotWriter.HeroId ? 3 : 1));
            if (WithModels)
            {
                // Solid and hittable the way units are: a capsule the probes pass by and a blow finds
                CharacterController cc = go.AddComponent<CharacterController>();
                cc.height = 2f; cc.radius = 0.45f; cc.center = new Vector3(0f, 1f, 0f);
            }
            b.GridId = UnitSpatialGrid.Register(b.Mark, b.Mark.Health, TeamOf(row.Team, who), 0.45f);
            b.To = b.From = go.transform.position;
            b.ToYaw = b.FromYaw = go.transform.eulerAngles.y;
            b.ToAt = b.FromAt = Time.time;
            bodies[key] = b;
        }

        private static void DressBody(GameObject go, UnitRow row)
        {
            if (row.Rank >= UnitSnapshotWriter.AnimalLook)
            {
                // Its model off a real animal, whose works go at once
                GameObject real = AnimalModelBuilder.CreateAnimalPrefab((AI.AnimalType)(row.Rank - UnitSnapshotWriter.AnimalLook), "RemoteAnimal");
                while (real.transform.childCount > 0) real.transform.GetChild(0).SetParent(go.transform, false);
                DestroyImmediate(real);
                return;
            }
            HumanoidModelBuilder.HumanType look;
            if (row.Id == UnitSnapshotWriter.HeroId) look = HumanoidModelBuilder.HumanType.HeroPlayer;
            else if (row.Rank >= UnitSnapshotWriter.MobLook)
            {
                var mob = (AI.HostileMobType)(row.Rank - UnitSnapshotWriter.MobLook);
                look = mob == AI.HostileMobType.WarlordGuard ? HumanoidModelBuilder.HumanType.Warlord : mob == AI.HostileMobType.Goblin ? HumanoidModelBuilder.HumanType.Goblin : HumanoidModelBuilder.HumanType.Bandit;
            }
            else look = ModelFor((UnitRank)row.Rank);
            GameObject model = HumanoidModelBuilder.CreateHumanoidModel(look, "VisualModel");
            model.transform.SetParent(go.transform, false);
        }

        private static HumanoidModelBuilder.HumanType ModelFor(UnitRank rank) => rank switch
        {
            UnitRank.Person => HumanoidModelBuilder.HumanType.Villager,
            UnitRank.Warrior => HumanoidModelBuilder.HumanType.Villager,
            UnitRank.Knight => HumanoidModelBuilder.HumanType.KnightGuard,
            _ => HumanoidModelBuilder.HumanType.Warlord
        };

        /// <summary>All of a player who has left.</summary>
        public void ForgetOwner(int who)
        {
            forget.Clear();
            foreach (int key in bodies.Keys) if (key >> 16 == who) forget.Add(key);
            foreach (int key in forget) Remove(key);
            forget.Clear();
            foreach (int key in waiting.Keys) if (key >> 16 == who) forget.Add(key);
            foreach (int key in forget) waiting.Remove(key);
        }

        private void Remove(int id)
        {
            if (!bodies.TryGetValue(id, out Body b)) return;
            bodies.Remove(id);
            if (b.GridId >= 0) UnitSpatialGrid.Unregister(b.GridId);
            if (b.Root != null) Destroy(b.Root.gameObject);
        }

        private void OnDestroy()
        {
            foreach (Body b in bodies.Values)
            {
                if (b.GridId >= 0) UnitSpatialGrid.Unregister(b.GridId);
                if (b.Root != null) Destroy(b.Root.gameObject);
            }
            bodies.Clear();
        }

        /// <summary>What is known of a unit now (tests).</summary>
        public bool TryGet(int owner, ushort id, out Vector3 at, out int healthShare)
        {
            at = default;
            healthShare = 0;
            if (!bodies.TryGetValue(Key(owner, id), out Body b)) return false;
            at = b.Root.position;
            healthShare = b.Health;
            return true;
        }

        private void LateUpdate()
        {
            if (waiting.Count > 0)
            {
                int spawned = 0;
                forget.Clear();
                foreach (KeyValuePair<int, UnitRow> w in waiting)
                {
                    if (spawned++ >= SpawnsPerFrame) break;
                    Spawn(w.Key, w.Value);
                    forget.Add(w.Key);
                }
                foreach (int id in forget) waiting.Remove(id);
            }
            float now = Time.time;
            forget.Clear();
            foreach (KeyValuePair<int, Body> kv in bodies)
            {
                Body b = kv.Value;
                if (now - b.LastHeard > ForgetAfter) { forget.Add(kv.Key); continue; }
                float t = b.ToAt > b.FromAt ? Mathf.Clamp01((now - b.FromAt) / (b.ToAt - b.FromAt)) : 1f;
                b.Root.SetPositionAndRotation(Vector3.Lerp(b.From, b.To, t), Quaternion.Euler(0f, Mathf.LerpAngle(b.FromYaw, b.ToYaw, t), 0f));
            }
            foreach (int id in forget) Remove(id);
        }
    }
}
