using System;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.SaveLoad;
using BattleOfAges.UI;
using BattleOfAges.World;

namespace BattleOfAges.Net
{
    /// <summary>
    /// A stable number for anything an order can name (M8, docs/GAME_DESIGN.md §14.3): a unit, a building, a tree. Given the first
    /// time it is asked for; the object is found by it again while it lives.
    /// </summary>
    public sealed class WorldEntity : MonoBehaviour
    {
        private static readonly Dictionary<int, WorldEntity> byId = new Dictionary<int, WorldEntity>();

        public WorldId Id { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => byId.Clear();

        public static WorldId IdOf(Component c) => c != null ? IdOf(c.gameObject) : default;

        public static WorldId IdOf(GameObject go)
        {
            if (go == null) return default;
            if (!go.TryGetComponent(out WorldEntity e))
            {
                e = go.AddComponent<WorldEntity>();
                e.Id = WorldIds.New();
                byId[e.Id.Value] = e;
            }
            return e.Id;
        }

        public static GameObject Find(WorldId id) => byId.TryGetValue(id.Value, out WorldEntity e) && e != null ? e.gameObject : null;

        /// <summary>The component of the object with this number, null if it is gone (an order may arrive late).</summary>
        public static T Find<T>(int id) where T : Component
        {
            GameObject go = Find(new WorldId { Value = id });
            return go != null ? go.GetComponentInParent<T>() : null;
        }

        private void OnDestroy()
        {
            if (byId.TryGetValue(Id.Value, out WorldEntity e) && e == this) byId.Remove(Id.Value);
        }
    }

    /// <summary>
    /// A player's order as data (§14.2): the interface says what is wanted, the command does it. On one machine it runs at once;
    /// over a network the same object goes to whoever owns the world. It names things by number and carries no state of the
    /// interface, and whatever it names may be gone by the time it runs.
    /// </summary>
    [Serializable]
    public abstract class GameCommand
    {
        /// <summary>The player who gave it.</summary>
        public int Issuer;

        public abstract void Execute();

        protected bool ByLocalPlayer => Issuer == CommandBus.LocalPlayer;
        /// <summary>A word or a sound for the one who gave the order only.</summary>
        protected void Say(string message) { if (ByLocalPlayer) StrategyHud.Notify(message); }
        protected void Sound(GameSound.Kind kind) { if (ByLocalPlayer) GameSound.Play2D(kind); }
    }

    /// <summary>Where every order goes. Router: set when a network carries them (true: taken, it will not run here).</summary>
    public static class CommandBus
    {
        public static int LocalPlayer { get; set; }
        public static Func<GameCommand, bool> Router { get; set; }
        /// <summary>A command that has run here (for tests and, later, the hosts' log).</summary>
        public static event Action<GameCommand> Executed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { LocalPlayer = 0; Router = null; Executed = null; }

        /// <summary>An order from this machine.</summary>
        public static void Submit(GameCommand command)
        {
            command.Issuer = LocalPlayer;
            if (Router != null && Router(command)) return;
            Run(command);
        }

        /// <summary>Runs a command, from this machine or from another.</summary>
        public static void Run(GameCommand command)
        {
            command.Execute();
            Executed?.Invoke(command);
        }
    }

    /// <summary>Commands as bytes' worth of text: orders are few and must arrive, so plain JSON on the reliable channel is enough.</summary>
    public static class CommandCodec
    {
        private static readonly Type[] Types =
        {
            typeof(UnitOrderCommand), typeof(SetDirectiveCommand), typeof(PaintZoneCommand), typeof(PlaceBuildingCommand),
            typeof(UpgradeHallCommand), typeof(UpgradeWallsCommand), typeof(ToggleAlarmCommand), typeof(TradeCommand),
            typeof(CraftCommand), typeof(SmeltCommand), typeof(BenchWorkerCommand), typeof(EquipCommand),
            typeof(LaborCommand), typeof(DemolishCommand)
        };

        public static string Encode(GameCommand c) => Array.IndexOf(Types, c.GetType()) + "|" + JsonUtility.ToJson(c);

        public static GameCommand Decode(string text)
        {
            int bar = text.IndexOf('|');
            if (bar <= 0 || !int.TryParse(text.Substring(0, bar), out int kind) || kind < 0 || kind >= Types.Length) return null;
            return (GameCommand)JsonUtility.FromJson(text.Substring(bar + 1), Types[kind]);
        }
    }

    // ------------------------------------------------------------------ units

    /// <summary>A right-click: these units are sent at something or somewhere.</summary>
    [Serializable]
    public sealed class UnitOrderCommand : GameCommand
    {
        public enum Kind { Move, Attack, Wall, Mine, Bench, Site, Place, Tree, Pebble, Chest, AttackMove, QueueMove, Stop }

        public Kind Order;
        public int[] Units;
        public int Target;
        public Vector3 Point;

        public static UnitOrderCommand For(Kind order, IList<RtsUnit> units, Component target, Vector3 point)
        {
            var ids = new int[units.Count];
            for (int i = 0; i < units.Count; i++) ids[i] = WorldEntity.IdOf(units[i]).Value;
            return new UnitOrderCommand { Order = order, Units = ids, Target = WorldEntity.IdOf(target).Value, Point = point };
        }

        public override void Execute()
        {
            var units = new List<RtsUnit>(Units.Length);
            foreach (int id in Units)
            {
                RtsUnit u = WorldEntity.Find<RtsUnit>(id);
                if (u != null) units.Add(u);
            }
            if (units.Count == 0) return;
            WorkbenchNode toBench = Order == Kind.Bench ? WorldEntity.Find<WorkbenchNode>(Target) : null;
            WorkPlace toPlace = Order == Kind.Place ? WorldEntity.Find<WorkPlace>(Target) : null;
            // Any order but to its own bench calls a worker off the workbench (else it walked back once let go)
            foreach (RtsUnit unit in units) WorkbenchNode.CallOff(unit, toBench);
            Gatherers(units, g => { if (g.AtSite) g.LeaveSite(null); if (g.AtPlace && g.AssignedPlace != toPlace) g.LeavePlace(null); }); // any order takes it off the site or field it was at

            switch (Order)
            {
                case Kind.Place:
                    if (toPlace == null) return;
                    int hands = 0;
                    Gatherers(units, g => { if (g.AssignToPlace(toPlace, true)) hands++; });
                    if (hands == 0) Say("Тут усі місця зайняті");
                    else Sound(GameSound.Kind.OrderWork);
                    return;
                case Kind.Site:
                    ConstructionSite site = WorldEntity.Find<ConstructionSite>(Target);
                    if (site == null) return;
                    int builders = 0;
                    Gatherers(units, g => { if (g.AssignToSite(site)) builders++; });
                    if (builders == 0) Say($"Біля будівництва всі {ConstructionSite.MaxBuilders} місця зайняті");
                    else Sound(GameSound.Kind.OrderWork);
                    return;
                case Kind.Wall:
                    // Own wall: up onto it, to the free posts nearest the click (§10.5)
                    int sent = 0;
                    foreach (RtsUnit unit in units)
                        if (unit.OnWall || unit.SendToWall(WallWalk.FreePost(Point, unit))) sent++;
                    if (sent == 0) Say("На стіну не піднятися: потрібна стіна зі сходами (і вільні місця)");
                    else Sound(GameSound.Kind.OrderMove);
                    return;
                case Kind.Attack:
                    Combat.HealthComponent enemy = WorldEntity.Find<Combat.HealthComponent>(Target);
                    if (enemy == null || !enemy.IsAlive) return;
                    foreach (RtsUnit unit in units) unit.IssueAttackCommand(enemy);
                    Sound(GameSound.Kind.OrderAttack);
                    return;
                case Kind.Mine:
                    MineNode mine = WorldEntity.Find<MineNode>(Target);
                    if (mine == null) return;
                    Gatherers(units, g => { if (!mine.IsFullWorkers) g.AssignToWorkInMine(mine); });
                    Sound(GameSound.Kind.OrderWork);
                    return;
                case Kind.Bench:
                    if (toBench == null) return;
                    int called = 0;
                    foreach (RtsUnit unit in units) if (toBench.AssignWorker(unit)) called++;
                    if (called == 0 && toBench.IsWorkerFull) Say("Усі 4 місця біля верстака зайняті");
                    else if (called > 0) Sound(GameSound.Kind.OrderWork);
                    return;
                case Kind.Tree:
                    ResourceNodeTree tree = WorldEntity.Find<ResourceNodeTree>(Target);
                    if (tree == null) return;
                    Gatherers(units, g => g.CommandHarvestTree(tree));
                    Sound(GameSound.Kind.OrderWork);
                    return;
                case Kind.Pebble:
                    OrePebbleNode pebble = WorldEntity.Find<OrePebbleNode>(Target);
                    if (pebble == null) return;
                    Gatherers(units, g => g.CommandHarvestPebble(pebble));
                    Sound(GameSound.Kind.OrderWork);
                    return;
                case Kind.Chest:
                    Gatherers(units, g => g.CommandDeliverToChest());
                    Sound(GameSound.Kind.OrderWork);
                    return;
                case Kind.Stop:
                    foreach (RtsUnit unit in units)
                    {
                        NpcResourceGatherer job = unit.GetComponent<NpcResourceGatherer>();
                        if (job != null) job.SetGatherDirective(GatherDirective.Idle);
                        unit.StopHere();
                    }
                    Sound(GameSound.Kind.OrderMove);
                    return;
                case Kind.AttackMove:
                    MoveInFormation(units, Point, (u, at) => u.IssueAttackMove(at));
                    Sound(GameSound.Kind.OrderAttack);
                    return;
                case Kind.QueueMove:
                    MoveInFormation(units, Point, (u, at) => u.QueueMove(at));
                    Sound(GameSound.Kind.OrderMove);
                    return;
                default:
                    MoveInFormation(units, Point, (u, at) => u.IssueMoveCommand(at));
                    Sound(GameSound.Kind.OrderMove);
                    return;
            }
        }

        private static void Gatherers(List<RtsUnit> units, Action<NpcResourceGatherer> order)
        {
            foreach (RtsUnit unit in units)
                if (unit.TryGetComponent(out NpcResourceGatherer gatherer)) order(gatherer);
        }

        /// <summary>The group's places round the point (UnitFormation), and the walk to each.</summary>
        private static void MoveInFormation(List<RtsUnit> units, Vector3 point, Action<RtsUnit, Vector3> walk)
        {
            var at = new List<Vector3>(units.Count);
            var strength = new List<int>(units.Count);
            foreach (RtsUnit u in units)
            {
                at.Add(u.transform.position);
                strength.Add(FrontOf(u));
            }
            Vector3[] slots = UnitFormation.Slots(at, strength, point);
            for (int i = 0; i < units.Count; i++)
            {
                Vector3 target = units.Count > 1 ? UnitFormation.Standable(slots[i], point) : slots[i];
                target.y = RtsUnit.GetTerrainHeight(target);
                // Sent to a place it stays there, ready to fight, until sent to work (an army would walk back to the trees)
                NpcResourceGatherer job = units[i].GetComponent<NpcResourceGatherer>();
                if (job != null) job.SetGatherDirective(GatherDirective.Idle);
                walk(units[i], target);
            }
        }

        /// <summary>Who goes in front: the higher the rank the further, the ones who shoot behind them all.</summary>
        private static int FrontOf(RtsUnit u)
        {
            if (u.IsRanged) return -1;
            Combat.UnitProgress progress = u.GetComponent<Combat.UnitProgress>();
            return progress != null ? (int)progress.Rank : 0;
        }
    }

    /// <summary>The job buttons: what these units are to do by themselves.</summary>
    [Serializable]
    public sealed class SetDirectiveCommand : GameCommand
    {
        public int[] Units;
        public GatherDirective Directive;

        public override void Execute()
        {
            int n = 0;
            foreach (int id in Units)
            {
                RtsUnit u = WorldEntity.Find<RtsUnit>(id);
                if (u != null && u.TryGetComponent(out NpcResourceGatherer gatherer)) { gatherer.SetGatherDirective(Directive); n++; }
            }
            if (n == 0) Say("Серед обраних немає робітників");
        }
    }

    /// <summary>The people's window and a building's panel: one more or one less at a job, in a settlement or at one building.</summary>
    [Serializable]
    public sealed class LaborCommand : GameCommand
    {
        public enum Kind { Add, Remove, ClearLimits, AllOnTheirOwn }

        public Kind Order;
        public LaborJob Job;
        public int Hall;
        /// <summary>At this very building (0: wherever there is room).</summary>
        public int Building;

        public override void Execute()
        {
            TownHall hall = Hall != 0 ? WorldEntity.Find<TownHall>(Hall) : null;
            BuildingNode node = Building != 0 ? WorldEntity.Find<BuildingNode>(Building) : null;
            string no = null;
            switch (Order)
            {
                case Kind.Add: no = node != null ? SettlementLabor.AddTo(node.gameObject, hall, Job) : SettlementLabor.Add(Job, hall); break;
                case Kind.Remove: no = node != null ? SettlementLabor.RemoveFrom(node.gameObject, hall, Job) : SettlementLabor.Remove(Job, hall); break;
                case Kind.ClearLimits: SettlementLabor.ClearLimits(hall); break;
                case Kind.AllOnTheirOwn: SettlementLabor.AllOnTheirOwn(hall); break;
            }
            if (no == null) return;
            if (NpcGatherCommandUI.Instance != null && NpcGatherCommandUI.Instance.IsOpen) NpcGatherCommandUI.Instance.Say(no);
            else Say(no);
        }
    }

    /// <summary>Pulls a building down, or gives up a site.</summary>
    [Serializable]
    public sealed class DemolishCommand : GameCommand
    {
        public int Building;

        public override void Execute() => BuildingDemolition.Run(WorldEntity.Find<BuildingNode>(Building));
    }

    // ------------------------------------------------------------------ land and buildings

    /// <summary>One stroke of the zone brush, or of the eraser.</summary>
    [Serializable]
    public sealed class PaintZoneCommand : GameCommand
    {
        public Vector3 From, To;
        public float Radius;
        public ZoneType Zone;
        public bool Erase;

        /// <summary>Why cells were refused, for the brush (not sent).</summary>
        [NonSerialized] public string Refused;

        public override void Execute()
        {
            if (Erase) ZonePainter.EraseStroke(From, To, Radius);
            else ZonePainter.PaintStroke(From, To, Radius, Zone, out Refused);
        }
    }

    [Serializable]
    public sealed class PlaceBuildingCommand : GameCommand
    {
        public BuildingType Type;
        public Vector3 Position;
        public Quaternion Rotation;
        public int WallTier;

        public override void Execute()
        {
            BuildingPlacementSystem system = BuildingPlacementSystem.Instance;
            if (system != null) system.PlaceNow(Type, Position, Rotation, WallTier);
        }
    }

    [Serializable]
    public sealed class UpgradeHallCommand : GameCommand
    {
        public int Hall;

        public override void Execute()
        {
            TownHall hall = WorldEntity.Find<TownHall>(Hall);
            if (hall != null && hall.TryUpgrade()) Sound(GameSound.Kind.Fanfare);
        }
    }

    [Serializable]
    public sealed class UpgradeWallsCommand : GameCommand
    {
        public override void Execute() { if (Fortifications.TryUpgrade()) Sound(GameSound.Kind.Fanfare); }
    }

    [Serializable]
    public sealed class ToggleAlarmCommand : GameCommand
    {
        public override void Execute() => SettlementAlarm.Toggle();
    }

    // ------------------------------------------------------------------ stock and gear

    [Serializable]
    public sealed class TradeCommand : GameCommand
    {
        public TradeUI.Good Give, Take;
        public int GiveAmount, TakeAmount;

        public override void Execute() => TradeUI.Trade(Give, GiveAmount, Take, TakeAmount);
    }

    [Serializable]
    public sealed class CraftCommand : GameCommand
    {
        public int Bench;
        public ItemType Item;
        public int Level;

        public override void Execute()
        {
            WorkbenchNode bench = WorldEntity.Find<WorkbenchNode>(Bench);
            if (bench != null && !bench.TryEnqueueItem(Item, Level)) Say("Не вистачає ресурсів");
        }
    }

    [Serializable]
    public sealed class SmeltCommand : GameCommand
    {
        public int Bench, Tier, Count;

        public override void Execute()
        {
            WorkbenchNode bench = WorldEntity.Find<WorkbenchNode>(Bench);
            if (bench != null && (Count <= 0 || !bench.TryEnqueueSmelt(Tier, Count))) Say($"Не вистачає руди {CraftingTask.Tier(Tier)}: потрібно {Combat.GameBalance.SmeltRatio}");
        }
    }

    /// <summary>A worker called to a bench, or the last one let go.</summary>
    [Serializable]
    public sealed class BenchWorkerCommand : GameCommand
    {
        public int Bench;
        public bool Call;

        public override void Execute()
        {
            WorkbenchNode bench = WorldEntity.Find<WorkbenchNode>(Bench);
            if (bench == null) return;
            if (!Call) { bench.UnassignLastWorker(); return; }
            if (bench.AssignNearestFreeWorker() == null) Say(bench.IsWorkerFull ? "Усі місця біля верстака зайняті" : "Немає вільних робітників");
        }
    }

    /// <summary>A piece from the armoury put on these units, or a slot taken off one.</summary>
    [Serializable]
    public sealed class EquipCommand : GameCommand
    {
        public int[] Units;
        public ItemType Item;
        public int Level;
        public bool TakeOff;
        public EquipSlot Slot;

        public override void Execute()
        {
            var gear = new List<Equipment>(Units.Length);
            foreach (int id in Units)
            {
                Equipment e = WorldEntity.Find<Equipment>(id);
                if (e != null) gear.Add(e);
            }
            if (gear.Count == 0) return;
            if (TakeOff) { Armory.Instance.UnequipToStore(gear[0], Slot); return; }
            var item = new Item(Item, Level);
            if (gear.Count == 1) { Armory.Instance.EquipFromStore(gear[0], item); return; }
            int given = GearDispenser.GiveEach(gear, item);
            int left = 0;
            foreach (Equipment e in gear) if (e != null && !e.IsLocked(item.Slot) && !e[item.Slot].Equals(item)) left++;
            Say(given == 0 ? $"Усі обрані вже мають {item}" : $"{item}: видано {given}" + (left > 0 ? $", ще {left} без — на складі скінчилось" : ""));
        }
    }
}
