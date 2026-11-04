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

    /// <summary>
    /// A game with others (M8 step 4, docs/GAME_DESIGN.md §14.2, §18.4): the host owns the campaign, a player joins by IP and gets the
    /// same world from its seed and a kingdom of its own on a free site. Each simulates what is its own and sends it; the host
    /// passes everyone's on to everyone else. Nobody else's units are simulated here, only shown (RemoteUnits).
    /// Not yet: other players' buildings, fighting across the wire, the NPC kingdoms by a player far from the host.
    /// </summary>
    public sealed class NetSession : MonoBehaviour
    {
        public const byte WelcomeMessage = 2;
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
        }
    }
}
