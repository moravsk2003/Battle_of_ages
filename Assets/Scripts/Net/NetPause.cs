using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Net
{
    /// <summary>The game's pause as the host tells it to everyone (§14.1, §14.4).</summary>
    [Serializable]
    public sealed class NetPauseState
    {
        public bool On;
        /// <summary>A player has left: the game waits for the host's word (a bot, the lobby, or waiting on).</summary>
        public bool AwaitingHost;
        /// <summary>The host has said it waits for those who left.</summary>
        public bool HostWaits;
        /// <summary>Who set it: a player's number (0 the host), -1 nobody (a player left).</summary>
        public int By = -1;
        public string Why = "";
        /// <summary>Seconds it has lasted when told (each machine counts on from there).</summary>
        public float Elapsed;
    }

    [Serializable] public sealed class NetPauseRequest { public bool Pause; }

    /// <summary>
    /// The network pause (docs/GAME_DESIGN.md §14.1, §14.4): the host keeps it and tells everyone, and time stands on every
    /// machine while it is on. A player pauses a few times a game (PausesPerPlayer) and lifts its own pause; after
    /// AnyoneUnpausesAfter seconds anyone may; the host always may. A player leaving pauses the game for everyone until the
    /// host says what now: a bot keeps its kingdom, everyone goes back to the lobby (an autosave), or the game waits for it
    /// (its return lifts the pause). With AlwaysBot the bot is put at once and nothing stops.
    /// </summary>
    public sealed partial class NetSession
    {
        public const int PausesPerPlayer = 3;
        public const float AnyoneUnpausesAfter = 30f;
        public const byte PauseMessage = 19, PauseRequestMessage = 20, LobbyReturnMessage = 21;

        /// <summary>The host's session setting: a player who leaves gets a bot at once, with no pause (§14.4; off by default).</summary>
        public static bool AlwaysBot;

        /// <summary>The host's address this machine last joined (to come back to its lobby).</summary>
        public static string LastAddress { get; private set; }
        /// <summary>Set on the way back to the lobby: the host's save to open it with, a guest's host to join again (MainMenuUI).</summary>
        public static SaveData PendingLobbySave;
        public static string PendingLobbyName, PendingRejoin;

        private NetPauseState pause = new NetPauseState();
        private float pauseSince;
        private bool timeHeld, lobbyReturnAsked;

        /// <summary>A guest told to go back to the lobby goes, once the link's poll is over; true if it has.</summary>
        private bool LeaveForLobbyIfAsked()
        {
            if (!lobbyReturnAsked) return false;
            lobbyReturnAsked = false;
            PendingRejoin = LastAddress;
            UI.StrategyHud.Notify("Хост повертає всіх у лобі");
            SaveSystem.ToMainMenu(); // (Stop sends this machine's last save first)
            return true;
        }
        private int ownPausesUsed;
        private readonly Dictionary<int, int> pausesUsed = new Dictionary<int, int>();
        private readonly List<GuestRecord> waitingFor = new List<GuestRecord>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPauseStatics() { AlwaysBot = false; PendingLobbySave = null; PendingLobbyName = PendingRejoin = null; }

        /// <summary>The pause everyone shares now (null: no game with others).</summary>
        public static NetPauseState PauseState => Current != null && Current.basePlaced ? Current.pause : null;
        public static bool NetPaused => PauseState != null && PauseState.On;
        public static float PauseSeconds => Current != null ? Time.realtimeSinceStartup - Current.pauseSince : 0f;
        /// <summary>Pauses this machine's player may still set (the host: as many as it likes).</summary>
        public static int PausesLeft => Current == null ? 0 : IsHost ? int.MaxValue : Mathf.Max(0, PausesPerPlayer - Current.ownPausesUsed);
        public static bool CanPause => PauseState != null && !PauseState.On && PausesLeft > 0;
        public static bool CanUnpause
        {
            get
            {
                NetPauseState p = PauseState;
                if (p == null || !p.On || p.AwaitingHost) return false;
                return IsHost || p.By == Current.Number || PauseSeconds >= AnyoneUnpausesAfter;
            }
        }
        /// <summary>The names of those the host's word is awaited for.</summary>
        public static string WaitingNames
        {
            get
            {
                if (Current == null) return "";
                var names = new List<string>();
                foreach (GuestRecord g in Current.waitingFor) names.Add(LobbyHost.NameOf(g));
                return string.Join(", ", names);
            }
        }

        /// <summary>This machine's player asks to pause the game for everyone, or to lift the pause.</summary>
        public static void RequestPause(bool on)
        {
            NetSession s = Current;
            if (s == null || s.link == null || !s.basePlaced) return;
            if (s.link.IsHost) s.HostPause(0, on);
            else s.Send(0, PauseRequestMessage, JsonUtility.ToJson(new NetPauseRequest { Pause = on }));
        }

        private bool HostPause(int number, bool on)
        {
            float now = Time.realtimeSinceStartup;
            if (on)
            {
                if (pause.On) return false;
                pausesUsed.TryGetValue(number, out int used);
                if (number != 0 && used >= PausesPerPlayer) return false;
                pausesUsed[number] = used + 1;
                pause = new NetPauseState { On = true, By = number, Why = $"{PlayerName(number)} поставив паузу" };
                pauseSince = now;
            }
            else
            {
                if (!pause.On || pause.AwaitingHost) return false;
                if (number != 0 && pause.By != number && now - pauseSince < AnyoneUnpausesAfter) return false;
                pause = new NetPauseState();
            }
            BroadcastPause();
            return true;
        }

        private string PlayerName(int number)
        {
            if (number == 0) return string.IsNullOrEmpty(LocalName) ? "Хост" : LocalName;
            foreach (GuestRecord g in guestOf.Values) if (g.Number == number) return LobbyHost.NameOf(g);
            return "Гравець " + number;
        }

        private void BroadcastPause()
        {
            pause.Elapsed = Time.realtimeSinceStartup - pauseSince;
            BroadcastText(PauseMessage, JsonUtility.ToJson(pause));
        }

        /// <summary>The host: a guest has gone from a game on. A bot at once (AlwaysBot), or everyone waits for the host's word.</summary>
        private void OnGuestLeft(GuestRecord left)
        {
            if (AlwaysBot)
            {
                bool kept = RaiseBot(left);
                TellRoster(kept ? $"{LobbyHost.NameOf(left)} вийшов із гри: його королівство стереже бот" : $"{LobbyHost.NameOf(left)} вийшов із гри");
                if (kept) TellWorldMap();
                return;
            }
            TellRoster($"{LobbyHost.NameOf(left)} вийшов із гри");
            if (!waitingFor.Contains(left)) waitingFor.Add(left);
            if (!pause.AwaitingHost) pauseSince = Time.realtimeSinceStartup;
            pause = new NetPauseState { On = true, AwaitingHost = true, Why = $"Вийшов: {WaitingNames}" };
            BroadcastPause();
        }

        /// <summary>The host: a guest is back. If the game waited for it (and no one else), it goes on.</summary>
        private void OnGuestBack(GuestRecord back)
        {
            if (!waitingFor.Remove(back))
            {
                if (pause.On) BroadcastPause(); // (the one come in sees the pause too)
                return;
            }
            if (waitingFor.Count == 0) pause = new NetPauseState();
            else pause.Why = $"Вийшов: {WaitingNames}";
            BroadcastPause();
        }

        /// <summary>The host's word: bots keep the kingdoms of those who left, and the game goes on.</summary>
        public static void HostPutBots()
        {
            NetSession s = Current;
            if (s == null || !IsHost || !s.pause.AwaitingHost) return;
            var kept = new List<string>();
            foreach (GuestRecord g in s.waitingFor) if (s.RaiseBot(g)) kept.Add(LobbyHost.NameOf(g));
            s.waitingFor.Clear();
            s.pause = new NetPauseState();
            s.BroadcastPause();
            s.TellRoster(kept.Count > 0 ? $"Королівства стереже бот: {string.Join(", ", kept)}" : "Гра триває (стерегти нічого: ці гравці ще не зберігались)");
            s.TellWorldMap();
        }

        /// <summary>The host's word: the game waits for those who left (their return lifts the pause).</summary>
        public static void HostWait()
        {
            NetSession s = Current;
            if (s == null || !IsHost || !s.pause.AwaitingHost) return;
            s.pause.HostWaits = true;
            s.BroadcastPause();
        }

        /// <summary>The host's word: an autosave, and everyone back to the lobby of it (the guests join it again by themselves).</summary>
        public static void HostBackToLobby()
        {
            NetSession s = Current;
            if (s == null || !IsHost || !s.basePlaced) return;
            s.StartCoroutine(s.BackToLobby());
        }

        private IEnumerator BackToLobby()
        {
            BroadcastText(LobbyReturnMessage, "{}");
            // The guests send their last saves as they go (Stop): they are in the host's save then
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until) yield return null;
            SaveSystem.Save(SaveSystem.AutoSlot);
            PendingLobbySave = SaveSystem.ReadSlot(SaveSystem.AutoSlot);
            PendingLobbyName = SaveSystem.SlotName(SaveSystem.AutoSlot);
            SaveSystem.ToMainMenu();
        }

        private void ReceivePause(int peer, byte kind, string text)
        {
            switch (kind)
            {
                case PauseMessage:
                    if (link.IsHost) return;
                    NetPauseState told = JsonUtility.FromJson<NetPauseState>(text);
                    if (told == null) return;
                    bool mineNew = told.On && told.By == Number && !(pause.On && pause.By == Number);
                    if (mineNew) ownPausesUsed++;
                    pause = told;
                    pauseSince = Time.realtimeSinceStartup - told.Elapsed;
                    return;
                case PauseRequestMessage:
                    if (!link.IsHost || !guestOf.TryGetValue(peer, out GuestRecord who)) return;
                    NetPauseRequest asked = JsonUtility.FromJson<NetPauseRequest>(text);
                    if (asked != null) HostPause(who.Number, asked.Pause); // (refused: the pause as it is stays, nothing to say)
                    return;
                case LobbyReturnMessage:
                    if (link.IsHost) return;
                    lobbyReturnAsked = true; // (after the link's poll: stopping inside it would break its loop)
                    return;
            }
        }

        /// <summary>Time stands on every machine while the pause is on; after it, as the local pause menu has it.</summary>
        private void HoldTime()
        {
            if (!basePlaced) return;
            if (pause.On)
            {
                Time.timeScale = 0f;
                timeHeld = true;
                return;
            }
            if (!timeHeld) return;
            timeHeld = false;
            UI.PauseMenuController menu = UI.PauseMenuController.Instance;
            Time.timeScale = menu != null && menu.IsPaused() ? 0f : 1f;
        }
    }
}
