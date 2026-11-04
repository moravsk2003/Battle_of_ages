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
        private const int Header = 1 + 4 + 2 + 2;
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
            if (rows > 0) Flush(link, rows, now);
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
            if (owner == null) return false;
            if (k.Owner != owner)
            {
                // A new unit (a number is used again when one dies)
                k = default;
                k.Owner = owner;
                UnitProgress p = owner.GetComponent<UnitProgress>();
                k.Rank = p != null ? (byte)p.Rank : (byte)0;
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
            link.SendAll(NetChannel.Fast, packet, Header + rows * UnitRow.Bytes);
        }

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
    /// A player's view of units it does not simulate (M8 step 3): a body for each, a little behind the snapshots so it moves between
    /// two known places. No AI, no collider, no combat: only what it looks like. A swing is not shown yet.
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
        }

        private readonly Dictionary<ushort, Body> bodies = new Dictionary<ushort, Body>();
        private readonly Dictionary<ushort, UnitRow> waiting = new Dictionary<ushort, UnitRow>();
        private readonly List<ushort> forget = new List<ushort>();
        private Action<UnitRow> onRow;

        /// <summary>Show the bodies (false: numbers only, for tests of the wire).</summary>
        public bool WithModels { get; set; } = true;
        public int Count => bodies.Count;

        /// <summary>A snapshot packet has come.</summary>
        public bool Apply(byte[] data, int length)
        {
            if (onRow == null) onRow = OnRow;
            return UnitSnapshotWriter.Read(data, length, onRow);
        }

        private void OnRow(UnitRow row)
        {
            if (row.Health == 0)
            {
                waiting.Remove(row.Id);
                Remove(row.Id);
                return;
            }
            if (bodies.TryGetValue(row.Id, out Body b) && b.Rank == row.Rank && b.Team == row.Team) Retarget(b, row);
            else
            {
                if (b != null) Remove(row.Id); // the number has passed to another unit
                waiting[row.Id] = row; // a few a frame (a sudden army was a hitch)
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
        }

        private static Vector3 PlaceOf(UnitRow row)
        {
            float x = UnitSnapshotWriter.Unquantize(row.X), z = UnitSnapshotWriter.Unquantize(row.Z);
            return new Vector3(x, RtsUnit.GetTerrainHeight(new Vector3(x, 0f, z)), z);
        }

        private void Spawn(UnitRow row)
        {
            // Each a root of its own: a thousand moving children of one parent all go through one hierarchy
            var go = new GameObject("Remote_" + row.Id);
            go.transform.SetPositionAndRotation(PlaceOf(row), Quaternion.Euler(0f, row.Yaw / 255f * 360f, 0f));
            if (WithModels)
            {
                GameObject model = HumanoidModelBuilder.CreateHumanoidModel(ModelFor((UnitRank)row.Rank), "VisualModel");
                model.transform.SetParent(go.transform, false);
            }
            var b = new Body { Root = go.transform, Rank = row.Rank, Team = row.Team, Health = row.Health, LastHeard = Time.time };
            b.To = b.From = go.transform.position;
            b.ToYaw = b.FromYaw = go.transform.eulerAngles.y;
            b.ToAt = b.FromAt = Time.time;
            bodies[row.Id] = b;
        }

        private static HumanoidModelBuilder.HumanType ModelFor(UnitRank rank) => rank switch
        {
            UnitRank.Person => HumanoidModelBuilder.HumanType.Villager,
            UnitRank.Warrior => HumanoidModelBuilder.HumanType.Villager,
            UnitRank.Knight => HumanoidModelBuilder.HumanType.KnightGuard,
            _ => HumanoidModelBuilder.HumanType.Warlord
        };

        private void Remove(ushort id)
        {
            if (!bodies.TryGetValue(id, out Body b)) return;
            bodies.Remove(id);
            if (b.Root != null) Destroy(b.Root.gameObject);
        }

        private void OnDestroy()
        {
            foreach (Body b in bodies.Values) if (b.Root != null) Destroy(b.Root.gameObject);
            bodies.Clear();
        }

        /// <summary>What is known of a unit now (tests).</summary>
        public bool TryGet(ushort id, out Vector3 at, out int healthShare)
        {
            at = default;
            healthShare = 0;
            if (!bodies.TryGetValue(id, out Body b)) return false;
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
                foreach (KeyValuePair<ushort, UnitRow> w in waiting)
                {
                    if (spawned++ >= SpawnsPerFrame) break;
                    Spawn(w.Value);
                    forget.Add(w.Key);
                }
                foreach (ushort id in forget) waiting.Remove(id);
            }
            float now = Time.time;
            forget.Clear();
            foreach (KeyValuePair<ushort, Body> kv in bodies)
            {
                Body b = kv.Value;
                if (now - b.LastHeard > ForgetAfter) { forget.Add(kv.Key); continue; }
                float t = b.ToAt > b.FromAt ? Mathf.Clamp01((now - b.FromAt) / (b.ToAt - b.FromAt)) : 1f;
                b.Root.SetPositionAndRotation(Vector3.Lerp(b.From, b.To, t), Quaternion.Euler(0f, Mathf.LerpAngle(b.FromYaw, b.ToYaw, t), 0f));
            }
            foreach (ushort id in forget) Remove(id);
        }
    }
}
