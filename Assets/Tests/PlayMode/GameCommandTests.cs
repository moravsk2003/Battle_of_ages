using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Crafting;
using BattleOfAges.Net;
using BattleOfAges.RTS;
using BattleOfAges.UI;
using BattleOfAges.World;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>M8 step 1: orders as data: they name things by number, survive being sent as text, and can be taken away to run elsewhere.</summary>
    public class GameCommandTests
    {
        private static readonly Vector3 Far = new Vector3(5300f, 0f, 5300f);
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void Teardown()
        {
            CommandBus.Router = null;
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            ZoneMap.Clear();
        }

        private RtsUnit Worker(string name)
        {
            GameObject w = BuildingNode.CreateWorkerNpc(Far, name);
            spawned.Add(w);
            return w.GetComponent<RtsUnit>();
        }

        private static T Sent<T>(T command) where T : GameCommand => (T)CommandCodec.Decode(CommandCodec.Encode(command));

        [Test]
        public void ANumber_FindsItsObject_AndStaysItsOwn()
        {
            var a = new GameObject("EntityA");
            var b = new GameObject("EntityB");
            spawned.Add(a);
            spawned.Add(b);
            var idA = WorldEntity.IdOf(a);
            Assert.AreEqual(idA, WorldEntity.IdOf(a), "the same each time");
            Assert.AreNotEqual(idA, WorldEntity.IdOf(b));
            Assert.AreEqual(a, WorldEntity.Find(idA));
            Assert.IsFalse(idA.IsNone);
        }

        [UnityTest]
        public IEnumerator AMoveOrder_SentAsText_DoesWhatItSays()
        {
            RtsUnit unit = Worker("NPC_Cmd");
            yield return null;
            Vector3 to = Far + new Vector3(12f, 0f, 3f);
            var command = Sent(UnitOrderCommand.For(UnitOrderCommand.Kind.Move, new[] { unit }, null, to));
            CommandBus.Run(command);
            Assert.IsTrue(unit.HasMoveTarget, "it sets off");
            Assert.AreEqual(GatherDirective.Idle, unit.GetComponent<NpcResourceGatherer>().CurrentDirective, "sent to a place it stands by");
        }

        [UnityTest]
        public IEnumerator AnOrder_NamingWhatIsGone_DoesNothing()
        {
            RtsUnit unit = Worker("NPC_Gone");
            GameObject tree = Utils.SteppeTreeBuilder.CreateSteppeTree("Cmd_Tree", 1);
            tree.transform.position = Far + new Vector3(5f, 0f, 0f);
            yield return null;
            var order = UnitOrderCommand.For(UnitOrderCommand.Kind.Tree, new[] { unit }, tree.GetComponent<Resources.ResourceNodeTree>(), tree.transform.position);
            Object.Destroy(tree);
            yield return null;
            Assert.DoesNotThrow(() => CommandBus.Run(order), "the tree went while the order was on its way");

            var noUnits = UnitOrderCommand.For(UnitOrderCommand.Kind.Move, new[] { unit }, null, Far);
            Object.Destroy(unit.gameObject);
            yield return null;
            Assert.DoesNotThrow(() => CommandBus.Run(noUnits));
        }

        [UnityTest]
        public IEnumerator ARouter_TakesTheOrderAway_ToRunElsewhere()
        {
            RtsUnit unit = Worker("NPC_Routed");
            yield return null;
            GameCommand taken = null;
            CommandBus.Router = c => { taken = c; return true; };
            int ran = 0;
            System.Action<GameCommand> count = _ => ran++;
            CommandBus.Executed += count;
            CommandBus.LocalPlayer = 3;
            CommandBus.Submit(new SetDirectiveCommand { Units = new[] { WorldEntity.IdOf(unit).Value }, Directive = GatherDirective.WoodChopTreesOnly });
            CommandBus.LocalPlayer = 0;
            CommandBus.Executed -= count;
            Assert.IsNotNull(taken, "it went to the router");
            Assert.AreEqual(3, taken.Issuer, "saying who gave it");
            Assert.AreEqual(0, ran, "and did not run here");
            Assert.AreNotEqual(GatherDirective.WoodChopTreesOnly, unit.GetComponent<NpcResourceGatherer>().CurrentDirective);

            CommandBus.Router = null;
            CommandBus.Run(Sent((SetDirectiveCommand)taken));
            Assert.AreEqual(GatherDirective.WoodChopTreesOnly, unit.GetComponent<NpcResourceGatherer>().CurrentDirective, "where it does run, it works");
        }

        [UnityTest]
        public IEnumerator AZoneStroke_SentAsText_PaintsAndErases()
        {
            ZoneMap.Clear();
            GameObject hall = TownHall.CreatePrefab("Cmd_Hall"); // (zones are marked in a hall's reach)
            hall.transform.position = Far;
            spawned.Add(hall);
            yield return null;
            Vector3 at = Far + new Vector3(30f, 0f, 0f);
            CommandBus.Run(Sent(new PaintZoneCommand { From = at, To = at, Radius = 8f, Zone = ZoneType.Forest }));
            Assert.AreEqual(ZoneType.Forest, ZoneMap.At(at), "painted");
            CommandBus.Run(Sent(new PaintZoneCommand { From = at, To = at, Radius = 8f, Erase = true }));
            Assert.AreEqual(ZoneType.None, ZoneMap.At(at), "and gone");
        }

        [Test]
        public void EveryKindOfCommand_ComesThroughTheCodec()
        {
            GameCommand[] all =
            {
                new UnitOrderCommand { Order = UnitOrderCommand.Kind.Attack, Units = new[] { 1, 2 }, Target = 7, Point = Vector3.one },
                new UnitOrderCommand { Order = UnitOrderCommand.Kind.AttackMove, Units = new[] { 3 }, Point = Vector3.right },
                new UnitOrderCommand { Order = UnitOrderCommand.Kind.QueueMove, Units = new[] { 3 }, Point = Vector3.left },
                new UnitOrderCommand { Order = UnitOrderCommand.Kind.Stop, Units = new[] { 3 } },
                new SetDirectiveCommand { Units = new[] { 4 }, Directive = GatherDirective.Idle },
                new PaintZoneCommand { From = Vector3.one, To = Vector3.up, Radius = 4f, Zone = ZoneType.Mining },
                new PlaceBuildingCommand { Type = BuildingType.Mine, Position = Vector3.right, Rotation = Quaternion.Euler(0f, 90f, 0f), WallTier = 2 },
                new UpgradeHallCommand { Hall = 9 }, new UpgradeWallsCommand(), new ToggleAlarmCommand(),
                new TradeCommand { Give = TradeUI.Good.Ore, GiveAmount = 5, Take = TradeUI.Good.Wood, TakeAmount = 1 },
                new CraftCommand { Bench = 3, Item = ItemType.Bow, Level = 2 },
                new SmeltCommand { Bench = 3, Tier = 1, Count = 4 },
                new BenchWorkerCommand { Bench = 3, Call = true },
                new EquipCommand { Units = new[] { 5 }, Item = ItemType.Helmet, Level = 2 }
            };
            foreach (GameCommand c in all)
            {
                GameCommand back = CommandCodec.Decode(CommandCodec.Encode(c));
                Assert.IsNotNull(back, c.GetType().Name);
                Assert.AreEqual(c.GetType(), back.GetType());
                Assert.AreEqual(JsonUtility.ToJson(c), JsonUtility.ToJson(back), c.GetType().Name + " whole");
            }
            Assert.IsNull(CommandCodec.Decode("99|{}"), "a kind it does not know");
            Assert.IsNull(CommandCodec.Decode("not a command"));
        }
    }
}
