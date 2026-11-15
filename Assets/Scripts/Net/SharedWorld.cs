using System;
using System.Collections.Generic;
using UnityEngine;

namespace BattleOfAges.Net
{
    /// <summary>
    /// Who has the life of each chunk, its animals and its camp's raiders (docs/GAME_DESIGN.md §14.2: a shared stretch has one owner, the
    /// first one there). Kept by the host. The others see them through the owner's snapshots: otherwise each machine would have
    /// its own bandits and deer on the same ground.
    /// </summary>
    public sealed class LifeClaims
    {
        private readonly Dictionary<Vector2Int, int> owners = new Dictionary<Vector2Int, int>();

        /// <summary>Takes the chunk for this player if nobody has it; true if it is this player's now.</summary>
        public bool TryClaim(Vector2Int chunk, int player)
        {
            if (owners.TryGetValue(chunk, out int owner)) return owner == player;
            owners[chunk] = player;
            return true;
        }

        /// <summary>This player has left the chunk; true if it was its to give up.</summary>
        public bool Release(Vector2Int chunk, int player)
        {
            if (!owners.TryGetValue(chunk, out int owner) || owner != player) return false;
            owners.Remove(chunk);
            return true;
        }

        /// <summary>A player has gone: all it held, for the others to take.</summary>
        public List<Vector2Int> ReleaseAll(int player)
        {
            var freed = new List<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, int> kv in owners) if (kv.Value == player) freed.Add(kv.Key);
            foreach (Vector2Int c in freed) owners.Remove(c);
            return freed;
        }

        public int Count => owners.Count;
    }

    /// <summary>A guest of this campaign as its host remembers it: who, where its kingdom is, and its game when last heard from.</summary>
    [Serializable]
    public sealed class GuestRecord
    {
        public string Token;
        /// <summary>Which guest it was to come: its site follows from this, so it is the same each time.</summary>
        public int Number;
        /// <summary>Its whole save as text (empty: it has not saved yet).</summary>
        public string Save = "";
        /// <summary>The name it goes by (empty: "Гравець N").</summary>
        public string Name = "";
        /// <summary>What it chose in the lobby for its first hero (HeroClass, ArtifactType), kept till it has a save.</summary>
        public int Class;
        public bool HasArtifact;
        public int Artifact;
        /// <summary>Ready in the lobby (not kept).</summary>
        [NonSerialized] public bool Ready;
        /// <summary>Its hero in a few words, read from its save (null: not read yet).</summary>
        [NonSerialized] public string Hero;
    }

    /// <summary>The host's book of guests: saved with its game, so a guest comes back to its kingdom whenever it comes back.</summary>
    public static class GuestBook
    {
        private static readonly List<GuestRecord> records = new List<GuestRecord>();

        public static IReadOnlyList<GuestRecord> All => records;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => records.Clear();

        public static void Clear() => records.Clear();

        public static void Load(List<GuestRecord> saved)
        {
            records.Clear();
            if (saved != null) records.AddRange(saved);
        }

        /// <summary>The guest with this token, or null if it has not been here.</summary>
        public static GuestRecord Find(string token)
        {
            foreach (GuestRecord r in records) if (r.Token == token) return r;
            return null;
        }

        public static void Remove(string token) => records.RemoveAll(r => r.Token == token);

        /// <summary>The guest with this token, written in if it is new.</summary>
        public static GuestRecord Of(string token)
        {
            foreach (GuestRecord r in records) if (r.Token == token) return r;
            int number = 1;
            foreach (GuestRecord r in records) number = Mathf.Max(number, r.Number + 1);
            var fresh = new GuestRecord { Token = token, Number = number };
            records.Add(fresh);
            return fresh;
        }

        /// <summary>This machine's own lasting name as a guest.</summary>
        public static string LocalToken
        {
            get
            {
                const string key = "NetGuestToken";
                string token = PlayerPrefs.GetString(key, "");
                if (string.IsNullOrEmpty(token))
                {
                    token = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(key, token);
                    PlayerPrefs.Save();
                }
                return token;
            }
        }
    }
}
