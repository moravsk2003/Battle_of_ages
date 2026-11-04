using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Net
{
    /// <summary>
    /// M8's check in real players, on the wire between two processes (§18, M8: two see the same battle of 500 on 500 within the budget):
    /// "-netdemo host [-netdemounits N] [-netdemolog path]" fights the battle and sends it;
    /// "-netdemo join [ip] [-netdemolog path]" only shows what comes. Each writes what it measured and quits after -netdemoseconds.
    /// Nobody plays this: it is a measuring tool, as -smokeshot is.
    /// </summary>
    public sealed class NetDemo : MonoBehaviour
    {
        private static readonly Vector3 Arena = new Vector3(150f, 0f, -40f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartIfAsked()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-netdemo");
            if (i < 0 || i + 1 >= args.Length) return;
            var go = new GameObject("NetDemo");
            DontDestroyOnLoad(go);
            NetDemo demo = go.AddComponent<NetDemo>();
            string address = i + 2 < args.Length && !args[i + 2].StartsWith("-") ? args[i + 2] : "127.0.0.1";
            if (args[i + 1] == "sessionhost" || args[i + 1] == "sessionjoin") demo.StartCoroutine(demo.RunSession(args, args[i + 1] == "sessionhost", address));
            else demo.StartCoroutine(demo.Run(args, args[i + 1] == "host", address));
        }

        private static string Arg(string[] args, string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        private IEnumerator Run(string[] args, bool host, string address)
        {
            string log = Arg(args, "-netdemolog", Path.Combine(Application.persistentDataPath, host ? "netdemo_host.md" : "netdemo_join.md"));
            float seconds = float.Parse(Arg(args, "-netdemoseconds", "60"), System.Globalization.CultureInfo.InvariantCulture);
            int units = int.Parse(Arg(args, "-netdemounits", "1000"));
            ushort port = ushort.Parse(Arg(args, "-netdemoport", NetLink.DefaultPort.ToString()));

            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Application.runInBackground = true;
            yield return SceneManager.LoadSceneAsync(SaveSystem.GameScene, LoadSceneMode.Single);
            yield return new WaitForSeconds(2f);
            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                player.GetComponent<HealthComponent>()?.InitializeHealth(100000000);
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                Vector3 stand = Arena + new Vector3(-45f, 0f, 0f);
                stand.y = RtsUnit.GetTerrainHeight(stand) + 1f;
                player.transform.SetPositionAndRotation(stand, Quaternion.LookRotation(Vector3.right));
                if (cc != null) cc.enabled = true;
            }
            yield return new WaitForSeconds(3f);

            NetLink link = host ? NetLink.Host(port) : NetLink.Join(address, port);
            var report = new StringBuilder();
            report.AppendLine($"# NetDemo {(host ? "host" : $"join {address}")}: {SystemInfo.processorType}");
            if (link == null)
            {
                report.AppendLine("could not open the link");
                File.WriteAllText(log, report.ToString());
                Application.Quit(2);
                yield break;
            }

            var writer = new UnitSnapshotWriter();
            RemoteUnits remote = host ? null : new GameObject("RemoteUnits").AddComponent<RemoteUnits>();
            bool peer = false;
            link.Connected += _ => peer = true;
            link.Received += (_, channel, data, length) => { if (remote != null && channel == NetChannel.Fast) remote.Apply(data, length); };

            if (host) yield return Muster(units);

            // Wait for the other side, a while at most
            float waitEnd = Time.realtimeSinceStartup + 40f;
            while (!peer && Time.realtimeSinceStartup < waitEnd)
            {
                link.Poll();
                yield return null;
            }
            report.AppendLine($"peer: {peer}, units on the host: {(host ? UnitSpatialGrid.Count.ToString() : "-")}");
            report.AppendLine();
            report.AppendLine("| s | FPS | worst ms | net in KB/s | net out KB/s | units shown / alive |");
            report.AppendLine("|---|---|---|---|---|---|");

            float start = Time.realtimeSinceStartup, nextRow = start + 5f;
            long lastIn = link.BytesReceived, lastOut = link.BytesSent;
            int frames = 0, rowFrames = 0;
            float worst = 0f, sumMs = 0f;
            float last = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup < start + seconds)
            {
                link.Poll();
                if (host) writer.Tick(link, Time.realtimeSinceStartup);
                yield return null;
                float t = Time.realtimeSinceStartup, ms = (t - last) * 1000f;
                last = t;
                frames++;
                rowFrames++;
                sumMs += ms;
                worst = Mathf.Max(worst, ms);
                if (t < nextRow) continue;
                nextRow = t + 5f;
                report.AppendLine($"| {t - start:0} | {1000f / (sumMs / rowFrames):0} | {worst:0.0} | {(link.BytesReceived - lastIn) / 5f / 1024f:0.0} | {(link.BytesSent - lastOut) / 5f / 1024f:0.0} | {(host ? UnitSpatialGrid.Count : remote.Count)} |");
                lastIn = link.BytesReceived;
                lastOut = link.BytesSent;
                sumMs = 0f;
                worst = 0f;
                rowFrames = 0;
            }
            report.AppendLine();
            report.AppendLine($"frames {frames}, rows sent {writer.RowsSent}, total in {link.BytesReceived / 1024} KB, out {link.BytesSent / 1024} KB");
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            File.WriteAllText(log, report.ToString());
            link.Dispose();
            Application.Quit(0);
        }

        /// <summary>Two players in the same world, as the menu starts them: what each sees of the other goes in the log.</summary>
        private IEnumerator RunSession(string[] args, bool host, string address)
        {
            string log = Arg(args, "-netdemolog", Path.Combine(Application.persistentDataPath, host ? "session_host.md" : "session_join.md"));
            float seconds = float.Parse(Arg(args, "-netdemoseconds", "40"), System.Globalization.CultureInfo.InvariantCulture);
            ushort port = ushort.Parse(Arg(args, "-netdemoport", NetLink.DefaultPort.ToString()));
            Application.runInBackground = true;
            var report = new StringBuilder();
            string asVersion = Arg(args, "-netdemoversion", "");
            if (asVersion.Length > 0) NetSession.VersionOverride = asVersion;
            report.AppendLine($"version {NetSession.GameVersion}");
            if (host)
            {
                SaveSystem.NewGame(new CampaignSetup { Seed = 777, Size = World.WorldSettings.DefaultSize, Heroes = 16, KingShare = 0.75f, Class = HeroClass.Tank });
                while (GameObject.Find("Player") == null) yield return null;
                yield return new WaitForSeconds(2f);
                report.AppendLine($"host opened: {NetSession.Host(port)}");
            }
            else
            {
                yield return new WaitForSeconds(1f);
                NetSession.Failed += why => report.AppendLine("failed: " + why);
                report.AppendLine($"joining {address}: {NetSession.Join(address, port)}");
            }
            float end = Time.realtimeSinceStartup + seconds;
            float nextRow = Time.realtimeSinceStartup + 5f;
            bool came = false, built = false;
            float nextBlow = 0f;
            int blows = 0;
            // "-netdemostep leave": a guest changes its world and goes; "back": it comes back to see what it finds
            string step = Arg(args, "-netdemostep", "fight");
            NetSession.GuestSaveSeconds = 3f;
            bool stepDone = false;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                NetSession now = NetSession.Current;
                if (!host && step != "fight" && now != null && now.Welcome != null && Time.realtimeSinceStartup > 14f && !stepDone)
                {
                    stepDone = true;
                    came = true;
                    GameObject mine = GameObject.Find("Player");
                    if (step == "leave")
                    {
                        Resources.ResourceManager.Instance.AddResource(Resources.ResourceType.Wood, 777 - Resources.ResourceManager.Instance.WoodCount);
                        Resources.ResourceNodeTree tree = UnityEngine.Object.FindFirstObjectByType<Resources.ResourceNodeTree>();
                        string key = "";
                        foreach (Resources.ResourceNodeTree t in UnityEngine.Object.FindObjectsByType<Resources.ResourceNodeTree>(FindObjectsSortMode.None))
                            if (!string.IsNullOrEmpty(t.PersistentKey) && !t.PersistentKey.StartsWith("scene:") && t.CurrentState == Resources.TreeState.WholeTree) { tree = t; key = t.PersistentKey; break; }
                        if (tree != null) tree.ConvertToWoodPile();
                        PlayerPrefs.SetString("NetDemoTree", key);
                        PlayerPrefs.Save();
                        World.ZoneMap.SetRaw(World.ZoneMap.CellOf(mine.transform.position), World.ZoneType.Fields);
                        Vector3 to = mine.transform.position + new Vector3(40f, 0f, 25f);
                        to.y = RtsUnit.GetTerrainHeight(to) + 1f;
                        CharacterController cc = mine.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        mine.transform.position = to;
                        if (cc != null) cc.enabled = true;
                        report.AppendLine($"t {Time.realtimeSinceStartup:0}: wood 777, felled {key}, a cell marked, standing at {to.x:0},{to.z:0}");
                    }
                    else
                    {
                        string key = PlayerPrefs.GetString("NetDemoTree", "");
                        bool known = SaveLoad.WorldPersistence.TryGetTree(key, out SaveLoad.TreeSave seen);
                        report.AppendLine($"t {Time.realtimeSinceStartup:0}: back: wood {Resources.ResourceManager.Instance.WoodCount}, at {mine.transform.position.x:0},{mine.transform.position.z:0}, its tree {key} known {known} state {(known ? seen.State : -1)}, its zones {World.ZoneMap.Cells.Count}");
                    }
                }
                // The guest walks up to the host's hero and puts up a house at home; the host strikes it when it is near
                if (!host && step == "fight" && now != null && now.Welcome != null && Time.realtimeSinceStartup > 12f && !came)
                {
                    came = true;
                    GameObject mine = GameObject.Find("Player");
                    GameObject house = Building.BuildingPlacementSystem.CreateBuilding(Building.BuildingType.WorkerHouse);
                    if (house != null) { Vector3 at = mine.transform.position + new Vector3(12f, 0f, 12f); at.y = RtsUnit.GetTerrainHeight(at); house.transform.position = at; built = true; }
                    if (now.Remote.TryGet(0, UnitSnapshotWriter.HeroId, out Vector3 hostAt, out _))
                    {
                        CharacterController cc = mine.GetComponent<CharacterController>();
                        if (cc != null) cc.enabled = false;
                        hostAt.y = RtsUnit.GetTerrainHeight(hostAt) + 1f;
                        mine.transform.position = hostAt + new Vector3(1.5f, 0f, 0f);
                        if (cc != null) cc.enabled = true;
                        report.AppendLine($"t {Time.realtimeSinceStartup:0}: walked up to the host's hero, house built {built}");
                    }
                }
                if (host && step == "fight" && now != null && Time.realtimeSinceStartup >= nextBlow)
                {
                    nextBlow = Time.realtimeSinceStartup + 1f;
                    GameObject me2 = GameObject.Find("Player");
                    now.Remote.ForEachHero(body =>
                    {
                        Vector3 off = body.transform.position - me2.transform.position;
                        off.y = 0f;
                        if (off.magnitude < 6f) { body.Health.TakeDamage(40); blows++; }
                    });
                }
                if (Time.realtimeSinceStartup < nextRow) continue;
                nextRow = Time.realtimeSinceStartup + 5f;
                NetSession s = NetSession.Current;
                GameObject me = GameObject.Find("Player");
                string other = "not seen";
                if (s != null && s.Remote.TryGet(host ? 1 : 0, UnitSnapshotWriter.HeroId, out Vector3 otherAt, out int share)) other = $"at {otherAt.x:0},{otherAt.z:0} health {share}";
                report.AppendLine($"t {Time.realtimeSinceStartup:0}: player {NetSession.PlayerId}, number {(s != null ? s.Number : -1)}, in the game [{(s != null ? string.Join(",", s.Players) : "")}], on the world map {WorldMapData.Now().Count} kingdoms {(s != null ? s.PlayersOnMap().Count : 0)} players, seed {World.WorldSettings.Seed}, size {World.WorldSettings.Size}, kingdoms real {NpcKingdom.All.Count} far {KingdomRealm.Far.Count}, " +
                                  $"me at {(me != null ? $"{me.transform.position.x:0},{me.transform.position.z:0}" : "-")}, other hero {other}, my health {(me != null ? me.GetComponent<HealthComponent>().CurrentHealth : -1)}, blows struck {blows}, others' buildings {(s != null ? s.RemoteBuildingCount : -1)}, trees told {SaveLoad.WorldPersistence.Capture().Trees.Count}, others' zones {World.ZoneMap.ForeignCount}, guests {GuestBook.All.Count} save {(GuestBook.All.Count > 0 ? GuestBook.All[0].Save.Length : 0)}, life claims {(s != null ? s.LifeClaimCount : -1)}, clock {WorldClock.GameHours:0.00}, bodies {(s != null ? s.Remote.Count : -1)}, in {(s != null ? s.BytesIn / 1024 : 0)} KB out {(s != null ? s.BytesOut / 1024 : 0)} KB");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            File.WriteAllText(log, report.ToString());
            NetSession.Stop();
            Application.Quit(0);
        }

        /// <summary>Two armies facing each other, and they mean it.</summary>
        private static IEnumerator Muster(int units)
        {
            NpcKingdom a = NpcKingdom.Found(Arena + new Vector3(0f, 0f, 45f), "Північ", 0);
            NpcKingdom b = NpcKingdom.Found(Arena + new Vector3(0f, 0f, -45f), "Південь", 0);
            a.enabled = b.enabled = false;
            for (int i = 0; i < units / 2; i++)
            {
                a.AddSubject(UnitRank.Warrior, Row(a.Home, i, units / 2, 0.4f));
                b.AddSubject(UnitRank.Warrior, Row(b.Home, i, units / 2, -0.4f));
                if (i % 25 == 24) yield return null;
            }
            yield return null;
            foreach (HostileMobAI m in a.Subjects) m.OrderAttack(b.Hall);
            foreach (HostileMobAI m in b.Subjects) m.OrderAttack(a.Hall);
        }

        private static Vector3 Row(Vector3 centre, int i, int count, float back)
        {
            int side = Mathf.CeilToInt(Mathf.Sqrt(count));
            Vector3 p = centre + new Vector3((i % side - side * 0.5f) * 1.6f, 0f, (i / side) * 1.6f * Mathf.Sign(back));
            p.y = RtsUnit.GetTerrainHeight(p);
            return p;
        }
    }
}
