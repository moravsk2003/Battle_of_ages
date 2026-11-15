using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.Net
{
    /// <summary>
    /// The bot that keeps an absent player's kingdom on the host (docs/GAME_DESIGN.md §14.4): a "guardian". The host raises
    /// the kingdom from the guest's last save as an NPC kingdom that only guards: far from everyone in numbers (FarWorld),
    /// near someone as real units (KingdomRealm.Sync). Its people work on and defend their home, its hero stands by the hall
    /// and fights only when attacked; it never grows, builds, raids, spends or falls. When the player comes back the bot
    /// gives the kingdom up, and the player's save loses whom the bot lost.
    /// </summary>
    public static class GuestBot
    {
        /// <summary>The bot's kingdom of this guest, far or real (null: none).</summary>
        public static KingdomState FarOf(string token)
        {
            foreach (KingdomState s in KingdomRealm.Far) if (s.GuestToken == token) return s;
            return null;
        }

        public static NpcKingdom RealOf(string token)
        {
            foreach (NpcKingdom k in NpcKingdom.All) if (k != null && k.GuestToken == token) return k;
            return null;
        }

        /// <summary>A bot's side: hostile to everyone else, apart from the live players' sides (RemoteUnits.TeamOf goes by connection).</summary>
        public static int TeamOf(int guestNumber) => RemoteUnits.RemotePlayerTeam + 500 + Mathf.Max(0, guestNumber);

        /// <summary>The hit owner of a bot's shown buildings: blows at them go nowhere (an absent player's base stands, §14.4).</summary>
        public static int BuildingOwner(int guestNumber) => BotOwnerFirst + Mathf.Clamp(guestNumber, 0, 255 - BotOwnerFirst);
        public const int BotOwnerFirst = 200;

        public static bool Has(string token) => !string.IsNullOrEmpty(token) && (FarOf(token) != null || RealOf(token) != null);

        /// <summary>The guest's kingdom now kept by a bot (none if it is kept already or there is no save to raise it from).</summary>
        public static KingdomState Raise(GuestRecord guest)
        {
            if (guest == null || string.IsNullOrEmpty(guest.Save) || Has(guest.Token)) return null;
            SaveData save = JsonUtility.FromJson<SaveData>(guest.Save);
            if (save == null) return null;
            KingdomState s = FromSave(save, guest);
            KingdomRealm.AddFar(s);
            return s;
        }

        /// <summary>The bot gives the kingdom back: it leaves the world, and the guest's save loses whom it lost. False: no bot kept it.</summary>
        public static bool Retire(GuestRecord guest)
        {
            if (guest == null) return false;
            KingdomState kept = FarOf(guest.Token);
            if (kept != null) KingdomRealm.RemoveFar(kept);
            else
            {
                NpcKingdom real = RealOf(guest.Token);
                if (real == null) return false;
                kept = real.ToState();
                real.Dissolve();
            }
            if (string.IsNullOrEmpty(guest.Save)) return true;
            SaveData save = JsonUtility.FromJson<SaveData>(guest.Save);
            if (save == null) return true;
            Patch(save, kept);
            guest.Save = JsonUtility.ToJson(save);
            return true;
        }

        /// <summary>The kingdom as a guardian's state: its hall where the guest's stands, its people as they were, its hero at home.</summary>
        public static KingdomState FromSave(SaveData save, GuestRecord guest)
        {
            var s = new KingdomState
            {
                Name = LobbyHost.NameOf(guest) + " (бот)", GuestToken = guest.Token,
                Team = TeamOf(guest.Number),
                Age = 0f, LastRaidEnd = -1000f
            };
            Vector3 home = save.Player.Position;
            bool hall = false;
            foreach (BuildingSave b in save.Buildings)
                if (b.Type == (int)BuildingType.TownHall && !b.UnderConstruction) { home = b.Position; s.Hall = Mathf.Max(1, b.Level); s.HallHealth = b.Health; hall = true; break; }
            if (!hall && save.Units.Count > 0)
            {
                Vector3 sum = Vector3.zero;
                foreach (UnitSave u in save.Units) sum += u.Position;
                home = sum / save.Units.Count;
            }
            s.Home = home;
            for (int i = 0; i < save.Units.Count; i++)
            {
                UnitSave u = save.Units[i];
                s.Subjects.Add(new UnitState { Rank = Mathf.Max(0, u.Rank), BattleXp = u.BattleXp, Health = u.Health, Position = u.Position, Origin = i + 1 });
            }
            // Its hero: a general (§11.1, the player's hero), its class and experience, waiting by the hall
            s.HasKing = save.Player.Health > 0;
            s.King = new UnitState { Rank = (int)UnitRank.General, Health = save.Player.Health, Position = home + new Vector3(3f, 0f, 3f) };
            s.KingClass = save.Player.Class;
            int xp = save.Player.Xp;
            for (int l = 1; l < save.Player.Level; l++) xp += HeroProgress.XpForLevelUp(l);
            s.KingTotalXp = xp;
            return s.EnsureIds();
        }

        /// <summary>The guest's save after its bot: the fallen are gone, the hurt are hurt; its buildings and stores are as it left them.</summary>
        public static void Patch(SaveData save, KingdomState kept)
        {
            var alive = new Dictionary<int, UnitState>();
            foreach (UnitState u in kept.Subjects) if (u.Origin > 0) alive[u.Origin] = u;
            foreach (ArmyState a in kept.Armies) foreach (UnitState u in a.Units) if (u.Origin > 0) alive[u.Origin] = u;
            var units = new List<UnitSave>();
            for (int i = 0; i < save.Units.Count; i++)
            {
                if (!alive.TryGetValue(i + 1, out UnitState u)) continue;
                UnitSave saved = save.Units[i];
                if (u.Health > 0) saved.Health = saved.Health > 0 ? Mathf.Min(saved.Health, u.Health) : u.Health;
                units.Add(saved);
            }
            save.Units = units;
            // The hero: hurt as the bot's was; fallen, it comes back with its player barely alive (no heir takes it: §14.4)
            if (kept.HasKing && kept.King.Health > 0) save.Player.Health = save.Player.Health > 0 ? Mathf.Min(save.Player.Health, kept.King.Health) : kept.King.Health;
            else if (!kept.HasKing) save.Player.Health = 1;
        }
    }
}
