using System;
using System.Collections;
using System.Collections.Generic;
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
        /// <summary>The number it goes by in this game (kept between visits, unlike the connection's PlayerId).</summary>
        public int Number;
        public float WorldSize;
        public double GameHours;
        public Vector3 Site;
        /// <summary>Its own game from the last time (empty: it is new here).</summary>
        public string Save = "";
        /// <summary>The world as it stands: what has been felled and killed.</summary>
        public WorldSave World = new WorldSave();
        /// <summary>Everyone's marked cells: x, z, type in a row.</summary>
        public List<int> Zones = new List<int>();
    }

    [Serializable] public sealed class NetHello { public string Token, Version; }
    /// <summary>Who is in the game (by their numbers: 0 is the host), and what has just happened, to say on the screen.</summary>
    [Serializable] public sealed class NetRoster { public List<int> Players = new List<int>(); public string Said = ""; }
    [Serializable] public sealed class NetClock { public double Hours; }
    [Serializable] public sealed class NetTrees { public List<TreeSave> Trees = new List<TreeSave>(); }
    [Serializable] public sealed class NetZones { public List<int> Cells = new List<int>(); }
    [Serializable] public sealed class NetText { public string Text; }
    /// <summary>A chunk's life: asked for (to the host), granted (from it), or free for the taking.</summary>
    [Serializable] public sealed class NetLife { public int X, Z; public bool Granted, Free; }

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
    /// turn real by anyone's hero, on the host. The world is shared: what is felled and killed, marked cells, the clock; a chunk's
    /// animals and raiders have one owner, the first one there (LifeClaims). A guest's game is kept by the host (GuestBook) and comes
    /// back with it. Not yet: a swing shown, a player's kingdom going on without it.
    /// </summary>
    public sealed class NetSession : MonoBehaviour
    {
        public const byte WelcomeMessage = 2, HitsMessage = 3, BuildingMessage = 4, HelloMessage = 5, GuestSaveMessage = 6,
                          TreesMessage = 7, CampMessage = 8, AnimalMessage = 9, ZonesMessage = 10, ClockMessage = 11, LifeMessage = 12,
                          RefusedMessage = 13, RosterMessage = 14;
        /// <summary>How often a guest's game goes to its host, and the host's clock to its guests.</summary>
        public static float GuestSaveSeconds = 60f;
        private const float ClockSeconds = 5f, WorldSeconds = 1f;
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

        /// <summary>This game's version, which must be the same on every machine of a session.</summary>
        public static string GameVersion => VersionOverride ?? Application.version;
        /// <summary>Another version than the build's (a check of the refusal).</summary>
        public static string VersionOverride;
        public static bool SameVersion(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        /// <summary>Who is in the game, by number (0 is the host).</summary>
        public IReadOnlyList<int> Players => players;
        /// <summary>This machine's number among them.</summary>
        public int Number => link != null && link.IsHost ? 0 : Welcome != null ? Welcome.Number : -1;

        private readonly List<int> players = new List<int> { 0 };
        private string refusedWhy;

        private NetLink link;
        private int playerId;
        private RemoteUnits remote;
        private readonly UnitSnapshotWriter writer = new UnitSnapshotWriter();
        private byte[] relay = new byte[NetLink.MaxUnreliableBytes];
        private bool joined;
        private float connectDeadline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Current = null; Failed = null; VersionOverride = null; }

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
            s.Listen();
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
            s.Listen();
            return true;
        }

        public static void Stop()
        {
            if (Current == null) return;
            NetSession s = Current;
            s.SendOwnSave(); // the last of it, going away properly
            s.StopListening();
            ZoneMap.ClearForeign();
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
            if (!link.IsHost) Send(0, HelloMessage, JsonUtility.ToJson(new NetHello { Token = GuestBook.LocalToken, Version = GameVersion }));
        }

        /// <summary>A guest has said who it is: its place, its game from last time if it has been here, and the world as it stands.</summary>
        private void Greet(int peer, NetHello who)
        {
            if (!SameVersion(who.Version, GameVersion))
            {
                // Told, not thrown out: a connection cut at once could lose the reason. It goes away on hearing it.
                string theirs = string.IsNullOrEmpty(who.Version) ? "стара" : who.Version;
                Send(peer, RefusedMessage, JsonUtility.ToJson(new NetText { Text = $"Різні версії гри: у хоста {GameVersion}, у вас {theirs}" }));
                UI.StrategyHud.Notify($"Гравець не зайшов: у нього версія {theirs}, у вас {GameVersion}");
                return;
            }
            GuestRecord record = GuestBook.Of(who.Token);
            guestOf[peer] = record;
            var hello = new NetWelcome
            {
                PlayerId = peer, Number = record.Number, WorldSeed = WorldSettings.Seed, WorldSize = WorldSettings.Size, GameHours = WorldClock.GameHours,
                Site = SiteFor(record.Number, GameBalance.CampaignHeroes), Save = record.Save, World = WorldPersistence.Capture(), Zones = AllZones()
            };
            Send(peer, WelcomeMessage, JsonUtility.ToJson(hello));
            // What stands in the world already, its own and the others'
            foreach (NetBuilding b in sentBuildings.Values) Send(peer, BuildingMessage, JsonUtility.ToJson(b));
            foreach (NetBuilding b in shownBuildings.Keys) if (b.Owner != peer) Send(peer, BuildingMessage, JsonUtility.ToJson(b));
            TellRoster($"Гравець {record.Number} зайшов у гру");
        }

        /// <summary>The host: who is in the game now, to everyone, with what has just happened.</summary>
        private void TellRoster(string said)
        {
            players.Clear();
            players.Add(0);
            foreach (GuestRecord g in guestOf.Values) players.Add(g.Number);
            players.Sort();
            var roster = new NetRoster { Said = said };
            roster.Players.AddRange(players);
            BroadcastText(RosterMessage, JsonUtility.ToJson(roster));
            UI.StrategyHud.Notify(said);
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
                bool wasIn = guestOf.TryGetValue(peer, out GuestRecord left);
                guestOf.Remove(peer);
                foreach (Vector2Int freed in claims.ReleaseAll(peer)) TellFree(freed);
                if (wasIn) TellRoster($"Гравець {left.Number} вийшов із гри");
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
            if (data[0] >= HelloMessage && data[0] <= RosterMessage) { ReceiveWorld(peer, data, length); return; }
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
            bool back = !string.IsNullOrEmpty(hello.Save);
            if (back) SaveSystem.LoadFrom(JsonUtility.FromJson<SaveData>(hello.Save)); // its own game from last time
            else SaveSystem.NewGame(new CampaignSetup { Seed = hello.WorldSeed, Size = hello.WorldSize, Heroes = GameBalance.CampaignHeroes, KingShare = GameBalance.CampaignKingShare });
            // The world is the host's, as it stands now, whatever this machine last knew
            WorldPersistence.Load(hello.World);
            ZoneMap.ClearForeign();
            for (int i = 0; i + 2 < hello.Zones.Count; i += 3) ZoneMap.SetForeign(new Vector2Int(hello.Zones[i], hello.Zones[i + 1]), (ZoneType)hello.Zones[i + 2]);
            StartCoroutine(TakeItsPlace(hello, back));
        }

        private static IEnumerator TakeItsPlace(NetWelcome hello, bool back)
        {
            yield return null;
            yield return null;
            while (Managers.GameContext.Player == null) yield return null;
            yield return null;
            if (back) yield return new WaitForSeconds(1f); // (its save is applied when the scene is ready: it stands where it stood)
            WorldClock.GameHours = hello.GameHours;
            if (!back) MoveBase(hello.Site);
            if (Current != null) Current.basePlaced = true;
            UI.StrategyHud.Notify($"Ви в грі як гравець {hello.Number}. {UI.HudTips.Key(InputSystem.KeyAction.PlayerList)} — хто ще тут");
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
            if (refusedWhy != null)
            {
                string why = refusedWhy;
                refusedWhy = null;
                Failed?.Invoke(why);
                Stop();
                return;
            }
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
                nextBuildingCheck = Time.realtimeSinceStartup + WorldSeconds;
                CheckBuildings();
                TellWorld();
            }
            float now = Time.realtimeSinceStartup;
            if (link.IsHost && now >= nextClock)
            {
                nextClock = now + ClockSeconds;
                BroadcastText(ClockMessage, JsonUtility.ToJson(new NetClock { Hours = WorldClock.GameHours }));
            }
            if (!link.IsHost && basePlaced && now >= nextGuestSave)
            {
                nextGuestSave = now + GuestSaveSeconds;
                SendOwnSave();
            }
        }

        private void OnApplicationQuit() { if (Current == this) Stop(); }

        // ------------------------------------------------------------------ the shared world

        private readonly LifeClaims claims = new LifeClaims();
        private readonly Dictionary<int, GuestRecord> guestOf = new Dictionary<int, GuestRecord>();
        private readonly HashSet<Vector2Int> lifeAsked = new HashSet<Vector2Int>(), lifeGranted = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, ZoneType> toldZones = new Dictionary<Vector2Int, ZoneType>();
        private float nextClock, nextGuestSave;

        /// <summary>Chunks whose life is someone's (the host's count).</summary>
        public int LifeClaimCount => claims.Count;

        private void Listen()
        {
            WorldPersistence.ListeningToTrees = true;
            WorldPersistence.CampChanged = c => BroadcastText(CampMessage, JsonUtility.ToJson(c));
            WorldPersistence.AnimalKilled = key => BroadcastText(AnimalMessage, JsonUtility.ToJson(new NetText { Text = key }));
            nextGuestSave = Time.realtimeSinceStartup + GuestSaveSeconds;
        }

        private void StopListening()
        {
            WorldPersistence.ListeningToTrees = false;
            WorldPersistence.CampChanged = null;
            WorldPersistence.AnimalKilled = null;
        }

        private void BroadcastText(byte kind, string text)
        {
            if (link == null) return;
            byte[] utf = Encoding.UTF8.GetBytes(text);
            var message = new byte[utf.Length + 1];
            message[0] = kind;
            Array.Copy(utf, 0, message, 1, utf.Length);
            link.SendAll(NetChannel.Reliable, message, message.Length);
        }

        /// <summary>Whether this machine may bring a chunk's animals and raiders to life. A guest asks the first time and hears back: the chunk wakes then.</summary>
        public bool MayLive(Vector2Int chunk)
        {
            if (link == null) return true;
            if (link.IsHost) return claims.TryClaim(chunk, playerId);
            if (lifeGranted.Contains(chunk)) return true;
            if (joined && lifeAsked.Add(chunk)) Send(0, LifeMessage, JsonUtility.ToJson(new NetLife { X = chunk.x, Z = chunk.y }));
            return false;
        }

        /// <summary>This machine has let a chunk go.</summary>
        public void ReleaseLife(Vector2Int chunk)
        {
            if (link == null) return;
            if (link.IsHost) { if (claims.Release(chunk, playerId)) TellFree(chunk); return; }
            lifeAsked.Remove(chunk);
            lifeGranted.Remove(chunk);
            Send(0, LifeMessage, JsonUtility.ToJson(new NetLife { X = chunk.x, Z = chunk.y, Free = true }));
        }

        private void TellFree(Vector2Int chunk)
        {
            BroadcastText(LifeMessage, JsonUtility.ToJson(new NetLife { X = chunk.x, Z = chunk.y, Free = true }));
            InfiniteWorldManager.Instance?.WakeLife(chunk);
        }

        private static List<int> AllZones()
        {
            var all = new List<int>();
            foreach (KeyValuePair<Vector2Int, ZoneType> c in ZoneMap.Cells) { all.Add(c.Key.x); all.Add(c.Key.y); all.Add((int)c.Value); }
            foreach (KeyValuePair<Vector2Int, ZoneType> c in ZoneMap.ForeignCells) { all.Add(c.Key.x); all.Add(c.Key.y); all.Add((int)c.Value); }
            return all;
        }

        /// <summary>What has changed by hand here since the last look: trees, marked cells.</summary>
        private void TellWorld()
        {
            List<TreeSave> trees = WorldPersistence.TakeChangedTrees();
            if (trees.Count > 0) BroadcastText(TreesMessage, JsonUtility.ToJson(new NetTrees { Trees = trees }));

            var cells = new NetZones();
            foreach (KeyValuePair<Vector2Int, ZoneType> c in ZoneMap.Cells)
            {
                if (toldZones.TryGetValue(c.Key, out ZoneType was) && was == c.Value) continue;
                cells.Cells.Add(c.Key.x); cells.Cells.Add(c.Key.y); cells.Cells.Add((int)c.Value);
            }
            List<Vector2Int> erased = null;
            foreach (Vector2Int c in toldZones.Keys)
                if (!ZoneMap.Cells.ContainsKey(c)) (erased ?? (erased = new List<Vector2Int>())).Add(c);
            if (erased != null)
                foreach (Vector2Int c in erased)
                {
                    toldZones.Remove(c);
                    cells.Cells.Add(c.x); cells.Cells.Add(c.y); cells.Cells.Add((int)ZoneType.None);
                }
            for (int i = 0; i + 2 < cells.Cells.Count; i += 3)
                if ((ZoneType)cells.Cells[i + 2] != ZoneType.None) toldZones[new Vector2Int(cells.Cells[i], cells.Cells[i + 1])] = (ZoneType)cells.Cells[i + 2];
            if (cells.Cells.Count > 0) BroadcastText(ZonesMessage, JsonUtility.ToJson(cells));
        }

        /// <summary>A guest's whole game to its host, to come back to.</summary>
        private void SendOwnSave()
        {
            if (link == null || link.IsHost || !basePlaced || !SaveSystem.InGame) return;
            SaveData own = SaveSystem.Capture();
            own.Guests.Clear();
            own.KingdomStates.Clear();
            Send(0, GuestSaveMessage, JsonUtility.ToJson(own));
        }

        private void ReceiveWorld(int peer, byte[] data, int length)
        {
            byte kind = data[0];
            string text = Encoding.UTF8.GetString(data, 1, length - 1);
            switch (kind)
            {
                case HelloMessage:
                    if (link.IsHost) Greet(peer, JsonUtility.FromJson<NetHello>(text));
                    return;
                case GuestSaveMessage:
                    if (link.IsHost && guestOf.TryGetValue(peer, out GuestRecord record)) record.Save = text;
                    return;
                case RefusedMessage:
                    if (!link.IsHost) refusedWhy = JsonUtility.FromJson<NetText>(text).Text; // (seen to after the poll: it closes the link)
                    return;
                case RosterMessage:
                    if (link.IsHost) return;
                    NetRoster roster = JsonUtility.FromJson<NetRoster>(text);
                    players.Clear();
                    players.AddRange(roster.Players);
                    if (!string.IsNullOrEmpty(roster.Said) && basePlaced) UI.StrategyHud.Notify(roster.Said);
                    return;
                case ClockMessage:
                    if (!link.IsHost) WorldClock.GameHours = JsonUtility.FromJson<NetClock>(text).Hours;
                    return;
                case LifeMessage:
                    ReceiveLife(peer, JsonUtility.FromJson<NetLife>(text));
                    return;
                case TreesMessage:
                    foreach (TreeSave t in JsonUtility.FromJson<NetTrees>(text).Trees) WorldPersistence.ApplyTree(t);
                    break;
                case CampMessage:
                    WorldPersistence.ApplyCamp(JsonUtility.FromJson<CampSave>(text));
                    break;
                case AnimalMessage:
                    WorldPersistence.ApplyKilledAnimal(JsonUtility.FromJson<NetText>(text).Text);
                    break;
                case ZonesMessage:
                    List<int> cells = JsonUtility.FromJson<NetZones>(text).Cells;
                    for (int i = 0; i + 2 < cells.Count; i += 3) ZoneMap.SetForeign(new Vector2Int(cells[i], cells[i + 1]), (ZoneType)cells[i + 2]);
                    break;
                default:
                    return;
            }
            if (link.IsHost) link.SendOthers(peer, NetChannel.Reliable, data, length); // passed on to the rest
        }

        private void ReceiveLife(int peer, NetLife life)
        {
            var chunk = new Vector2Int(life.X, life.Z);
            if (link.IsHost)
            {
                if (life.Free) { if (claims.Release(chunk, peer)) TellFree(chunk); return; }
                if (claims.TryClaim(chunk, peer)) Send(peer, LifeMessage, JsonUtility.ToJson(new NetLife { X = life.X, Z = life.Z, Granted = true }));
                return;
            }
            if (life.Granted) lifeGranted.Add(chunk);
            else if (life.Free) lifeAsked.Remove(chunk); // come free: whoever is there may ask again
            InfiniteWorldManager.Instance?.WakeLife(chunk);
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
