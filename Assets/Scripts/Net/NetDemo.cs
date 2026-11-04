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
            demo.StartCoroutine(demo.Run(args, args[i + 1] == "host", i + 2 < args.Length && !args[i + 2].StartsWith("-") ? args[i + 2] : "127.0.0.1"));
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
