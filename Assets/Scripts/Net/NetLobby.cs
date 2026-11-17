using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Net
{
    /// <summary>One place in the lobby, as everyone is told of it.</summary>
    [Serializable]
    public sealed class LobbySeat
    {
        public int Number;
        public string Name = "";
        public bool IsHost;
        /// <summary>In the lobby now (false: a player of the save who is not here, its kingdom waiting).</summary>
        public bool Here;
        public bool Ready;
        /// <summary>It has a kingdom in this campaign already: it comes back to it and chooses nothing.</summary>
        public bool Returning;
        public int Class;
        public bool HasArtifact;
        public int Artifact;
        /// <summary>A returning hero in a few words (its level and class).</summary>
        public string Hero = "";
    }

    /// <summary>The lobby as the host tells it: the campaign to be, everyone's place, and which of them is the one told.</summary>
    [Serializable]
    public sealed class NetLobby
    {
        public string Campaign = "";
        public bool FromSave;
        /// <summary>The host has pressed Start: the world is on its way.</summary>
        public bool Starting;
        /// <summary>The number of the one this copy is for (0: the host).</summary>
        public int You = -1;
        public List<LobbySeat> Seats = new List<LobbySeat>();
        /// <summary>A word to the one told (what of its choice was not taken).</summary>
        public string Said = "";

        public LobbySeat SeatOf(int number)
        {
            foreach (LobbySeat s in Seats) if (s.Number == number) return s;
            return null;
        }
    }

    /// <summary>What a player has chosen in the lobby: its name, hero and whether it is ready.</summary>
    [Serializable]
    public sealed class NetLobbyChoice
    {
        public string Name = "";
        public int Class = (int)HeroClass.Tank;
        public bool HasArtifact = true;
        public int Artifact;
        public bool Ready;
    }

    /// <summary>
    /// The host's lobby before the game (docs/GAME_DESIGN.md §14.4): who is in it, what each has chosen, who may start. No
    /// network here: NetSession feeds it what comes and sends what it says. A player is known by its lasting token
    /// (GuestBook): one with a kingdom in the save comes back to it and chooses nothing; a new one chooses a class and a
    /// starting artifact. The Central Empire is the host's alone for now: the host's world keeps the one empire and its epochs.
    /// </summary>
    public sealed class LobbyHost
    {
        public const int MaxPlayers = 10;
        public const int MaxNameLength = 20;

        public static readonly HeroClass[] Classes = { HeroClass.Tank, HeroClass.Archer, HeroClass.Mage, HeroClass.Dd };

        public string Campaign = "";
        public bool FromSave;
        public bool Starting;

        /// <summary>The host's own place: always here and ready.</summary>
        public readonly LobbySeat Host = new LobbySeat { Number = 0, IsHost = true, Here = true, Ready = true, Class = (int)HeroClass.Tank, HasArtifact = true, Artifact = (int)Artifacts.All[0] };

        private readonly Dictionary<int, GuestRecord> here = new Dictionary<int, GuestRecord>();
        /// <summary>Tokens first written into the book by this lobby: rubbed out again if they go before the game.</summary>
        private readonly HashSet<string> newHere = new HashSet<string>();

        /// <summary>Who is in the lobby, by connection.</summary>
        public IReadOnlyDictionary<int, GuestRecord> Here => here;

        public static bool IsReturning(GuestRecord r) => r != null && !string.IsNullOrEmpty(r.Save);

        /// <summary>Whether a guest may take this artifact at the start (the empire: only the host, see above).</summary>
        public static bool GuestMayTake(ArtifactType a) => a != ArtifactType.CentralEmpire;

        public static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var b = new System.Text.StringBuilder();
            foreach (char c in name.Trim())
            {
                if (c == '<' || c == '>' || char.IsControl(c)) continue; // (no rich text in others' lists)
                b.Append(c);
                if (b.Length >= MaxNameLength) break;
            }
            return b.ToString().Trim();
        }

        public static string NameOf(GuestRecord r) => r == null ? "" : !string.IsNullOrEmpty(r.Name) ? r.Name : "Гравець " + r.Number;

        /// <summary>A guest comes in; null if it may, else why not (for its screen).</summary>
        public string Join(int peer, string token, string name)
        {
            if (Starting) return "Гра вже починається";
            if (string.IsNullOrEmpty(token)) return "Гравець без імені";
            GuestRecord known = GuestBook.Find(token);
            foreach (GuestRecord r in here.Values) if (r.Token == token) return "Цей гравець уже в лобі (той самий комп'ютер)";
            if (known == null && SeatCount() >= MaxPlayers) return $"Лобі повне ({MaxPlayers} гравців)";
            GuestRecord record = known ?? GuestBook.Of(token);
            if (known == null) newHere.Add(token);
            string clean = CleanName(name);
            if (clean.Length > 0) record.Name = clean;
            if (!IsReturning(record) && record.Class == (int)HeroClass.None)
            {
                record.Class = (int)HeroClass.Tank;
                record.HasArtifact = true;
                record.Artifact = (int)Artifacts.All[0];
            }
            record.Ready = false;
            here[peer] = record;
            return null;
        }

        /// <summary>
        /// A guest has gone from the lobby: a new one is forgotten, one of the save waits again. Once the game is starting it
        /// is one of the game: it may come back to it.
        /// </summary>
        public void Leave(int peer)
        {
            if (!here.TryGetValue(peer, out GuestRecord record)) return;
            here.Remove(peer);
            record.Ready = false;
            if (!Starting && newHere.Remove(record.Token) && !IsReturning(record)) GuestBook.Remove(record.Token);
        }

        /// <summary>A guest's choice; null if all of it holds, else what was not taken.</summary>
        public string Choose(int peer, NetLobbyChoice c)
        {
            if (c == null || !here.TryGetValue(peer, out GuestRecord record)) return null;
            string clean = CleanName(c.Name);
            if (clean.Length > 0) record.Name = clean;
            record.Ready = c.Ready;
            if (IsReturning(record)) return null; // its hero is its own already
            string refused = null;
            if (Array.IndexOf(Classes, (HeroClass)c.Class) >= 0) record.Class = c.Class;
            else refused = "Такого класу немає";
            if (c.HasArtifact)
            {
                var a = (ArtifactType)c.Artifact;
                if (Array.IndexOf(Artifacts.All, a) < 0) refused = "Такого артефакту немає";
                else if (!GuestMayTake(a)) refused = "Центральну імперію в мережевій грі може взяти лише хост";
                else { record.HasArtifact = true; record.Artifact = c.Artifact; }
            }
            return refused;
        }

        /// <summary>The host's own choice (it may take anything; it is always ready).</summary>
        public void ChooseHost(NetLobbyChoice c)
        {
            if (c == null) return;
            string clean = CleanName(c.Name);
            if (clean.Length > 0) Host.Name = clean;
            if (FromSave) return;
            if (Array.IndexOf(Classes, (HeroClass)c.Class) >= 0) Host.Class = c.Class;
            if (c.HasArtifact && Array.IndexOf(Artifacts.All, (ArtifactType)c.Artifact) >= 0) { Host.HasArtifact = true; Host.Artifact = c.Artifact; }
        }

        /// <summary>The guests in the lobby who are not ready yet, by name.</summary>
        public List<string> NotReady()
        {
            var names = new List<string>();
            foreach (GuestRecord r in here.Values) if (!r.Ready) names.Add(NameOf(r));
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>The connection of the guest with this number in the lobby, or -1.</summary>
        public int PeerOf(int number)
        {
            foreach (KeyValuePair<int, GuestRecord> p in here) if (p.Value.Number == number) return p.Key;
            return -1;
        }

        /// <summary>Places taken: the host, those here, and those of the save who may come back.</summary>
        public int SeatCount()
        {
            int n = 1 + here.Count;
            foreach (GuestRecord r in GuestBook.All) if (IsReturning(r) && !IsHere(r)) n++;
            return n;
        }

        private bool IsHere(GuestRecord r)
        {
            foreach (GuestRecord h in here.Values) if (h == r) return true;
            return false;
        }

        /// <summary>The lobby as told to the one with this number.</summary>
        public NetLobby View(int you)
        {
            var lobby = new NetLobby { Campaign = Campaign, FromSave = FromSave, Starting = Starting, You = you };
            LobbySeat host = Copy(Host);
            if (string.IsNullOrEmpty(host.Name)) host.Name = "Хост";
            host.Returning = FromSave;
            lobby.Seats.Add(host);
            var guests = new List<GuestRecord>(here.Values);
            guests.Sort((a, b) => a.Number.CompareTo(b.Number));
            foreach (GuestRecord r in guests) lobby.Seats.Add(SeatFor(r, true));
            var away = new List<GuestRecord>();
            foreach (GuestRecord r in GuestBook.All) if (IsReturning(r) && !IsHere(r)) away.Add(r);
            away.Sort((a, b) => a.Number.CompareTo(b.Number));
            foreach (GuestRecord r in away) lobby.Seats.Add(SeatFor(r, false));
            return lobby;
        }

        private static LobbySeat SeatFor(GuestRecord r, bool here)
        {
            bool back = IsReturning(r);
            return new LobbySeat
            {
                Number = r.Number, Name = NameOf(r), Here = here, Ready = here && r.Ready, Returning = back,
                Class = r.Class, HasArtifact = r.HasArtifact, Artifact = r.Artifact, Hero = back ? HeroOf(r) : ""
            };
        }

        private static LobbySeat Copy(LobbySeat s) => JsonUtility.FromJson<LobbySeat>(JsonUtility.ToJson(s));

        /// <summary>A returning guest's hero in a few words, read once from its save.</summary>
        public static string HeroOf(GuestRecord r)
        {
            if (r.Hero != null) return r.Hero;
            SaveData d = null;
            try { d = JsonUtility.FromJson<SaveData>(r.Save); } catch (Exception) { }
            r.Hero = HeroLine(d);
            return r.Hero;
        }

        /// <summary>"рівень 5 · Вартовий" of a save's hero.</summary>
        public static string HeroLine(SaveData d)
        {
            if (d == null || d.Player == null) return "";
            var cls = (HeroClass)d.Player.Class;
            string line = "рівень " + Mathf.Max(1, d.Player.Level);
            if (cls != HeroClass.None) line += " · " + HeroClassRules.Name(cls);
            return line;
        }

        /// <summary>The campaign line of a new campaign's choices.</summary>
        public static string CampaignLine(CampaignSetup s)
        {
            float km = s.Size / 1000f;
            return $"Нова кампанія · світ {km:0} × {km:0} км · героїв {s.Heroes}";
        }

        /// <summary>The campaign line of a save.</summary>
        public static string CampaignLine(SaveData d, string slotName)
        {
            if (d == null) return slotName;
            int day = (int)(d.GameHours / 24.0) + 1;
            string title = string.IsNullOrEmpty(d.Title) ? slotName : $"{slotName}: {d.Title}";
            return $"Зі збереження · {title} · день {day}";
        }
    }
}
