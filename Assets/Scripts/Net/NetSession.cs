using System;
using System.Collections;
using System.Text;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.World;

namespace BattleOfAges.Net
{
    /// <summary>What a host tells a player who has just joined: the world to generate, and its place in it.</summary>
    [Serializable]
    public sealed class NetWelcome
    {
        public int PlayerId, WorldSeed;
        public float WorldSize;
        public double GameHours;
        public Vector3 Site;
    }

    /// <summary>A building of another player, as the others are told of it; Gone: it has fallen.</summary>
    [Serializable]
    public sealed class NetBuilding
    {
        public int Owner, Id, Type;
        public Vector3 Position;
        public float Yaw;
        public bool Gone;
    }

    /// <summary>
    /// A game with others (M8 step 4, docs/GAME_DESIGN.md §14.2, §18.4): the host owns the campaign, a player joins by IP and gets the
    /// same world from its seed and a kingdom of its own on a free site. Each simulates what is its own and sends it; the host
    /// passes everyone's on to everyone else. Nobody else's units are simulated here, only shown (RemoteUnits).
    /// A blow lands on a body here and goes to the owner of what it stands for, who applies it. NPC kingdoms are the host's: they
    /// turn real by anyone's hero, on the host. Not yet: a swing shown, zones and resources of others, a player's kingdom going on without it.
    /// </summary>
    public sealed class NetSession : MonoBehaviour
    {
        public const byte WelcomeMessage = 2, HitsMessage = 3, BuildingMessage = 4;
        /// <summary>A player's site is at least this far from every hero of the campaign.</summary>
        private const float MinSiteDistance = 600f;

        public static NetSession Current { get; private set; }
        public static bool Active => Current != null;
        public static bool IsHost => Current != null && Current.link != null && Current.link.IsHost;
        /// <summary>This machine is a guest: the world, its kingdoms and its time are the host's.</summary>
        public static bool IsGuest => Current != null && !IsHost;
        public static int PlayerId => Current != null ? Current.playerId : 0;

        /// <summary>Said once a guest has its world, or could not get in (a line for the screen).</summary>
        public static event Action<string> Failed;

        public int PeerCount => link != null ? link.PeerCount : 0;
        public RemoteUnits Remote => remote;
        public long BytesIn => link != null ? link.BytesReceived : 0;
        public long BytesOut => link != null ? link.BytesSent : 0;
        public NetWelcome Welcome { get; private set; }

        private NetLink link;
        private int playerId;
        private RemoteUnits remote;
        private readonly UnitSnapshotWriter writer = new UnitSnapshotWriter();
        private byte[] relay = new byte[NetLink.MaxUnreliableBytes];
        private bool joined;
        private float connectDeadline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = null; Failed = null; }

        /// <summary>What to tell the others: this machine's address on the local network and the port.</summary>
        public static string AddressLine()
        {
            string ip = "";
            try
            {
                foreach (System.Net.IPAddress a in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a)) { ip = a.ToString(); break; }
            }
            catch (Exception) { }
            return string.IsNullOrEmpty(ip) ? $"порт {NetLink.DefaultPort}" : $"адреса {ip}, порт {NetLink.DefaultPort}";
        }

        private static NetSession Create()
        {
            Stop();
            var go = new GameObject("NetSession");
            DontDestroyOnLoad(go);
            Current = go.AddComponent<NetSession>();
            return Current;
        }

        /// <summary>Opens the game in progress to others; false if the port cannot be taken.</summary>
        public static bool Host(ushort port = NetLink.DefaultPort)
        {
            NetSession s = Create();
            s.link = NetLink.Host(port);
            if (s.link == null) { Stop(); return false; }
            s.playerId = 0;
            s.joined = true;
            s.basePlaced = true;
            s.Wire();
            return true;
        }

        /// <summary>Goes to a host: the world comes once it answers.</summary>
        public static bool Join(string address, ushort port = NetLink.DefaultPort)
        {
            NetSession s = Create();
            s.link = NetLink.Join(address, port);
            if (s.link == null) { Stop(); return false; }
            s.connectDeadline = Time.realtimeSinceStartup + 15f;
            s.Wire();
            return true;
        }

        public static void Stop()
        {
            if (Current == null) return;
            NetSession s = Current;
            Current = null;
            s.link?.Dispose();
            s.link = null;
            if (s.remote != null) Destroy(s.remote.gameObject);
            foreach (GameObject shown in s.shownBuildings.Values) if (shown != null) Destroy(shown);
            Destroy(s.gameObject);
        }

        private void OnDestroy()
        {
            link?.Dispose();
            link = null;
            if (Current == this) Current = null;
        }

        private void Wire()
        {
            remote = new GameObject("RemoteUnits").AddComponent<RemoteUnits>();
            DontDestroyOnLoad(remote.gameObject);
            link.Connected += OnConnected;
            link.Disconnected += OnDisconnected;
            link.Received += OnReceived;
        }

        // ------------------------------------------------------------------ joining

        /// <summary>A free place for this player's kingdom: past the sites the campaign's own heroes took, the same for a seed.</summary>
        public static Vector3 SiteFor(int playerId, int heroes)
        {
            // Choose picks differently for another count: the places are compared, not counted
            System.Collections.Generic.List<Vector3> taken = StartSites.Choose(Mathf.Max(0, heroes - 1));
            int left = playerId;
            foreach (Vector3 site in StartSites.Choose(Mathf.Max(0, heroes - 1) + playerId + 12))
            {
                bool free = true;
                foreach (Vector3 t in taken)
                    if (new Vector2(site.x - t.x, site.z - t.z).magnitude < MinSiteDistance) { free = false; break; }
                if (free && --left == 0) return site;
            }
            // A crowded world: on a ring round the base, for lack of better
            float angle = playerId * 2.399f; // (the golden angle: no two together)
            float reach = StartSites.BaseClearance + playerId * 200f;
            return new Vector3(Mathf.Cos(angle) * reach, 0f, Mathf.Sin(angle) * reach);
        }

        private void OnConnected(int peer)
        {
            if (!link.IsHost) return;
            var hello = new NetWelcome
            {
                PlayerId = peer, WorldSeed = WorldSettings.Seed, WorldSize = WorldSettings.Size, GameHours = WorldClock.GameHours,
                Site = SiteFor(peer, GameBalance.CampaignHeroes)
            };
            Send(peer, WelcomeMessage, JsonUtility.ToJson(hello));
            // What stands in the world already, its own and the others'
            foreach (NetBuilding b in sentBuildings.Values) Send(peer, BuildingMessage, JsonUtility.ToJson(b));
            foreach (NetBuilding b in shownBuildings.Keys) if (b.Owner != peer) Send(peer, BuildingMessage, JsonUtility.ToJson(b));
        }

        private void Send(int peer, byte kind, string text)
        {
            byte[] utf = Encoding.UTF8.GetBytes(text);
            var message = new byte[utf.Length + 1];
            message[0] = kind;
            Array.Copy(utf, 0, message, 1, utf.Length);
            link.Send(peer, NetChannel.Reliable, message, message.Length);
        }

        private void OnDisconnected(int peer)
        {
            if (link.IsHost)
            {
                remote.ForgetOwner(peer);
                ForgetBuildingsOf(peer);
                UI.StrategyHud.Notify($"Гравець {peer} вийшов");
                return;
            }
            string why = joined ? "Зв'язок із хостом втрачено" : "Не вдалося приєднатися";
            UI.StrategyHud.Notify(why);
            Failed?.Invoke(why);
            Stop();
        }

        private void OnReceived(int peer, NetChannel channel, byte[] data, int length)
        {
            if (length == 0) return;
            if (data[0] == UnitSnapshotWriter.Message)
            {
                remote.Apply(data, length);
                // The host passes each one's units on to the others
                if (link.IsHost) link.SendOthers(peer, NetChannel.Fast, data, length);
                return;
            }
            if (data[0] == HitsMessage) { ReceiveHits(data, length); return; }
            if (data[0] == BuildingMessage)
            {
                ReceiveBuilding(JsonUtility.FromJson<NetBuilding>(Encoding.UTF8.GetString(data, 1, length - 1)));
                if (link.IsHost) link.SendOthers(peer, NetChannel.Reliable, data, length);
                return;
            }
            if (data[0] == WelcomeMessage && !link.IsHost && !joined) Enter(JsonUtility.FromJson<NetWelcome>(Encoding.UTF8.GetString(data, 1, length - 1)));
        }

        /// <summary>A guest has its answer: the same world, its own place.</summary>
        private void Enter(NetWelcome hello)
        {
            joined = true;
            Welcome = hello;
            playerId = hello.PlayerId;
            writer.Owner = (byte)playerId;
            remote.Self = (byte)playerId;
            SaveSystem.NewGame(new CampaignSetup { Seed = hello.WorldSeed, Size = hello.WorldSize, Heroes = GameBalance.CampaignHeroes, KingShare = GameBalance.CampaignKingShare });
            StartCoroutine(TakeItsPlace(hello));
        }

        private static IEnumerator TakeItsPlace(NetWelcome hello)
        {
            yield return null;
            yield return null;
            while (Managers.GameContext.Player == null) yield return null;
            yield return null;
            WorldClock.GameHours = hello.GameHours;
            MoveBase(hello.Site);
            if (Current != null) Current.basePlaced = true;
        }

        /// <summary>The start the scene gives (hero, people, chests, buildings at the origin) moved to this player's site.</summary>
        public static void MoveBase(Vector3 site)
        {
            site.y = InfiniteWorldManager.GetSampledTerrainHeight(site.x, site.z);
            GameObject player = Managers.GameContext.Player;
            Vector3 from = player != null ? player.transform.position : Vector3.zero;
            from.y = 0f;
            Vector3 Moved(Vector3 p)
            {
                Vector3 to = p - from + site;
                to.y = InfiniteWorldManager.GetSampledTerrainHeight(to.x, to.z);
                return to;
            }
            foreach (BuildingNode b in BuildingNode.Active) if (b != null) b.transform.position = Moved(b.transform.position);
            foreach (StorageChestNode c in StorageChestNode.ActiveChests) if (c != null) c.transform.position = Moved(c.transform.position);
            foreach (RtsUnit u in UnityEngine.Object.FindObjectsByType<RtsUnit>(FindObjectsSortMode.None))
            {
                if (u == null || u.gameObject == player) continue;
                u.transform.position = Moved(u.transform.position);
                u.SnapToTerrain();
            }
            GameObject fire = GameObject.Find("Campfire");
            if (fire != null) fire.transform.position = Moved(fire.transform.position);
            if (player != null)
            {
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                Vector3 at = site;
                at.y += 1f;
                player.transform.position = at;
                if (cc != null) cc.enabled = true;
            }
        }

        // ------------------------------------------------------------------ the running game

        private void Update()
        {
            if (link == null) return;
            link.Poll();
            if (link == null) return; // (it may have ended in Poll)
            if (!joined)
            {
                if (Time.realtimeSinceStartup < connectDeadline) return;
                Failed?.Invoke("Хост не відповідає");
                Stop();
                return;
            }
            GameObject player = Managers.GameContext.Player;
            writer.Hero = player != null ? player.GetComponent<HealthComponent>() : null;
            writer.Tick(link, Time.realtimeSinceStartup);
            FlushHits();
            if (basePlaced && Time.realtimeSinceStartup >= nextBuildingCheck)
            {
                nextBuildingCheck = Time.realtimeSinceStartup + 1f;
                CheckBuildings();
            }
        }

        // ------------------------------------------------------------------ blows across

        private const int HitBytes = 8;
        private byte[] hitBatch = new byte[3 + HitBytes * 256];
        private int hitsWaiting;

        /// <summary>A blow on a body here: it goes to the one who owns what it stands for, in the next batch.</summary>
        public void SendHit(int owner, int id, bool building, int damage)
        {
            if (link == null || damage <= 0) return;
            int at = 3 + hitsWaiting * HitBytes;
            if (at + HitBytes > hitBatch.Length) { FlushHits(); at = 3; }
            hitBatch[at] = (byte)owner;
            hitBatch[at + 1] = building ? (byte)1 : (byte)0;
            hitBatch[at + 2] = (byte)id; hitBatch[at + 3] = (byte)(id >> 8); hitBatch[at + 4] = (byte)(id >> 16); hitBatch[at + 5] = (byte)(id >> 24);
            int d = Mathf.Min(damage, 65535);
            hitBatch[at + 6] = (byte)d; hitBatch[at + 7] = (byte)(d >> 8);
            hitsWaiting++;
        }

        private void FlushHits()
        {
            if (hitsWaiting == 0) return;
            hitBatch[0] = HitsMessage;
            hitBatch[1] = (byte)hitsWaiting; hitBatch[2] = (byte)(hitsWaiting >> 8);
            int length = 3 + hitsWaiting * HitBytes;
            hitsWaiting = 0;
            if (link.IsHost) RouteFromHost(hitBatch, length, applyHere: false);
            else link.Send(0, NetChannel.Reliable, hitBatch, length);
        }

        private void ReceiveHits(byte[] data, int length)
        {
            if (link.IsHost) RouteFromHost(data, length, applyHere: true);
            else ApplyHits(data, length);
        }

        /// <summary>The host's own blows and those it passes on: each row to its owner, its own applied here.</summary>
        private void RouteFromHost(byte[] data, int length, bool applyHere)
        {
            int rows = data[1] | data[2] << 8;
            var one = new byte[3 + HitBytes];
            one[0] = HitsMessage;
            one[1] = 1;
            for (int i = 0; i < rows; i++)
            {
                int at = 3 + i * HitBytes;
                if (at + HitBytes > length) return;
                int owner = data[at];
                if (owner == playerId) { ApplyRow(data, at); continue; }
                Array.Copy(data, at, one, 3, HitBytes);
                link.Send(owner, NetChannel.Reliable, one, one.Length);
            }
        }

        private void ApplyHits(byte[] data, int length)
        {
            int rows = data[1] | data[2] << 8;
            for (int i = 0; i < rows && 3 + (i + 1) * HitBytes <= length; i++) ApplyRow(data, 3 + i * HitBytes);
        }

        private void ApplyRow(byte[] b, int at)
        {
            bool building = b[at + 1] != 0;
            int id = b[at + 2] | b[at + 3] << 8 | b[at + 4] << 16 | b[at + 5] << 24;
            int damage = b[at + 6] | b[at + 7] << 8;
            HealthComponent target;
            if (building) target = WorldEntity.Find<HealthComponent>(id);
            else if (id == UnitSnapshotWriter.HeroId) target = Managers.GameContext.PlayerHealth;
            else target = id >= 0 && id < UnitSpatialGrid.Capacity && UnitSpatialGrid.IsAlive(id) ? UnitSpatialGrid.Health(id) : null;
            if (target != null && target.IsAlive) target.TakeDamage(damage);
        }

        // ------------------------------------------------------------------ buildings across

        private readonly System.Collections.Generic.Dictionary<int, NetBuilding> sentBuildings = new System.Collections.Generic.Dictionary<int, NetBuilding>();
        private readonly System.Collections.Generic.Dictionary<NetBuilding, GameObject> shownBuildings = new System.Collections.Generic.Dictionary<NetBuilding, GameObject>();
        private readonly System.Collections.Generic.List<int> goneBuildings = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.HashSet<int> seenBuildings = new System.Collections.Generic.HashSet<int>();
        private float nextBuildingCheck;
        private bool basePlaced;

        /// <summary>Others' buildings shown here.</summary>
        public int RemoteBuildingCount => shownBuildings.Count;

        /// <summary>Whatever has gone up or come down here since the last look, however it happened, is told to the others.</summary>
        private void CheckBuildings()
        {
            seenBuildings.Clear();
            foreach (BuildingNode node in BuildingNode.Active)
            {
                if (node == null || BuildGhost.Is(node)) continue;
                int id = WorldEntity.IdOf(node).Value;
                seenBuildings.Add(id);
                if (sentBuildings.ContainsKey(id)) continue;
                var b = new NetBuilding { Owner = playerId, Id = id, Type = (int)node.Type, Position = node.transform.position, Yaw = node.transform.eulerAngles.y };
                sentBuildings[id] = b;
                Broadcast(b);
            }
            goneBuildings.Clear();
            foreach (int id in sentBuildings.Keys) if (!seenBuildings.Contains(id)) goneBuildings.Add(id);
            foreach (int id in goneBuildings)
            {
                NetBuilding b = sentBuildings[id];
                sentBuildings.Remove(id);
                b.Gone = true;
                Broadcast(b);
            }
        }

        private void Broadcast(NetBuilding b)
        {
            byte[] utf = Encoding.UTF8.GetBytes(JsonUtility.ToJson(b));
            var message = new byte[utf.Length + 1];
            message[0] = BuildingMessage;
            Array.Copy(utf, 0, message, 1, utf.Length);
            link.SendAll(NetChannel.Reliable, message, message.Length);
        }

        private NetBuilding FindShown(int owner, int id)
        {
            foreach (NetBuilding known in shownBuildings.Keys) if (known.Owner == owner && known.Id == id) return known;
            return null;
        }

        private void ReceiveBuilding(NetBuilding b)
        {
            if (b.Owner == playerId) return;
            NetBuilding known = FindShown(b.Owner, b.Id);
            if (b.Gone)
            {
                if (known == null) return;
                Destroy(shownBuildings[known]);
                shownBuildings.Remove(known);
                return;
            }
            if (known == null) shownBuildings[b] = ShowBuilding(b);
        }

        private void ForgetBuildingsOf(int owner)
        {
            var leaving = new System.Collections.Generic.List<NetBuilding>();
            foreach (NetBuilding b in shownBuildings.Keys) if (b.Owner == owner) leaving.Add(b);
            foreach (NetBuilding b in leaving)
            {
                Destroy(shownBuildings[b]);
                shownBuildings.Remove(b);
            }
        }

        /// <summary>The look of a building and something to strike: its models from a real one, whose works go at once (nothing of it counts here).</summary>
        private static GameObject ShowBuilding(NetBuilding b)
        {
            var type = (BuildingType)b.Type;
            var shown = new GameObject("RemoteBuilding_" + type);
            DontDestroyOnLoad(shown);
            shown.transform.SetPositionAndRotation(b.Position, Quaternion.Euler(0f, b.Yaw, 0f));
            GameObject real = BuildingPlacementSystem.CreateBuilding(type);
            BuildingDefinition def = BuildingData.GetDefinition(type);
            if (real != null)
            {
                while (real.transform.childCount > 0) real.transform.GetChild(0).SetParent(shown.transform, false);
                DestroyImmediate(real);
            }
            BoxCollider box = shown.AddComponent<BoxCollider>();
            box.size = def.Size;
            box.center = new Vector3(0f, def.Size.y * 0.5f, 0f);
            shown.AddComponent<RemoteBody>().Setup(b.Owner, b.Id, true, Mathf.Max(1, def.MaxHealth));
            return shown;
        }
    }
}
