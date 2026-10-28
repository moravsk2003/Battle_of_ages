using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BattleOfAges.Building;
using BattleOfAges.Combat;

namespace BattleOfAges.PlayModeTests
{
    /// <summary>Wall segments joining into a line (§10.5): snapping to free ends, shared piers, the gate opening for own.</summary>
    public class WallPlayTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private static readonly Vector3 Far = new Vector3(-5000f, 0f, 5000f);

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned) if (go != null) Object.Destroy(go);
            spawned.Clear();
            foreach (WallPier p in new List<WallPier>(WallPier.Active)) if (p != null) Object.Destroy(p.gameObject);
            Time.timeScale = 1f;
        }

        private WallSegment Segment(BuildingType type, Vector3 at, float yaw)
        {
            GameObject go = BuildingNode.CreateWallPrefab(type, "TestWall_" + spawned.Count);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            spawned.Add(go);
            return go.GetComponent<WallSegment>();
        }

        private static int PiersNear(Vector3 at, float radius)
        {
            int n = 0;
            foreach (WallPier p in WallPier.Active) if (p != null && (p.transform.position - at).magnitude < radius) n++;
            return n;
        }

        [UnityTest]
        public IEnumerator NextSegment_SnapsOn_KeepingTheOutside()
        {
            Segment(BuildingType.CastleWall, Far, 0f);
            yield return null;

            // Straight on past the +x end
            Assert.IsTrue(WallSegment.Snap(Far + new Vector3(4.6f, 0f, 0.2f), 4f, out Vector3 c, out float yaw, out _));
            Assert.Less((c - (Far + new Vector3(4f, 0f, 0f))).magnitude, 0.01f, "the next segment starts at the end");
            Assert.AreEqual(0f, Mathf.DeltaAngle(yaw, 0f), 0.5f, "facing the same way");

            // A right turn inward (outside = +z): it runs along -z, its outside now +x
            Assert.IsTrue(WallSegment.Snap(Far + new Vector3(2.2f, 0f, -2.5f), 4f, out c, out yaw, out _));
            Assert.Less((c - (Far + new Vector3(2f, 0f, -2f))).magnitude, 0.01f);
            Vector3 outside = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Assert.Greater(outside.x, 0.99f, "outside stays outside");

            // Far from any end: no snap
            Assert.IsFalse(WallSegment.Snap(Far + new Vector3(0f, 0f, 9f), 4f, out _, out _, out _));
        }

        [UnityTest]
        public IEnumerator JoinedSegments_SharePiers_AndAJointIsNotFree()
        {
            WallSegment a = Segment(BuildingType.CastleWall, Far, 0f);
            Segment(BuildingType.WallGate, Far + new Vector3(5f, 0f, 0f), 0f); // 6 m: from x 2 to 8
            yield return null;
            yield return null;
            Assert.AreEqual(3, PiersNear(Far, 20f), "two segments in a line: three piers");
            Assert.IsFalse(WallSegment.Snap(Far + new Vector3(2.3f, 0f, 1f), 4f, out _, out _, out _), "the joint is taken (and the free ends are far)");

            Object.Destroy(a.gameObject);
            yield return null;
            yield return null;
            Assert.AreEqual(2, PiersNear(Far, 20f), "the free end's pier went with its segment, the shared one stays");
        }

        [UnityTest]
        public IEnumerator Gate_OpensForThePlayer_AndCloses()
        {
            WallSegment gate = Segment(BuildingType.WallGate, Far, 0f);
            WallGateDoors doors = gate.GetComponent<WallGateDoors>();
            var player = new GameObject("Player");
            spawned.Add(player);
            player.transform.position = Far + new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0f, doors.Angle, 0.01f, "shut with nobody near");

            player.transform.position = Far + new Vector3(0f, 0f, GameBalance.GateOpenRadius * 0.5f);
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(WallGateDoors.OpenAngle, doors.Angle, 0.5f, "open for the player");

            player.transform.position = Far + new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(1.4f);
            Assert.AreEqual(0f, doors.Angle, 0.5f, "shut again");
        }

        private void Ground()
        {
            var ground = new GameObject("Ground");
            BoxCollider box = ground.AddComponent<BoxCollider>();
            box.center = new Vector3(Far.x, -0.5f, Far.z);
            box.size = new Vector3(200f, 1f, 200f);
            spawned.Add(ground);
        }

        /// <summary>p on the ground (the world of an earlier test may still be loaded: its terrain, not the test box).</summary>
        private static Vector3 OnGround(Vector3 p) { p.y = RTS.RtsUnit.GetTerrainHeight(p); return p; }

        /// <summary>A wall from x -2 to 2 and the gate from 2 to 8 (outside +z), on the ground.</summary>
        private IEnumerator WallWithGate()
        {
            Ground();
            Segment(BuildingType.CastleWall, OnGround(Far), 0f);
            Segment(BuildingType.WallGate, OnGround(Far + new Vector3(5f, 0f, 0f)), 0f);
            yield return null;
            yield return null;
        }

        /// <summary>Walks a stand-in from start straight at the target (or along the route) with the ground motor.</summary>
        private static Vector3 Walk(Vector3 start, Vector3 target, bool own, bool route, float seconds = 12f)
        {
            var t = new GameObject("Walker").transform;
            t.position = start;
            const float dt = 0.05f;
            for (float time = 0f; time < seconds; time += dt)
            {
                Vector3 aim = route ? WallBarriers.Route(t.position, target) : target;
                Vector3 d = aim - t.position;
                d.y = 0f;
                if (d.magnitude < 0.2f && aim == target) break;
                RTS.GroundMotor.Move(t, -1, d.normalized * 3f, 0.4f, dt, own);
            }
            Vector3 end = t.position;
            Object.Destroy(t.gameObject);
            return end;
        }

        [UnityTest]
        public IEnumerator Walls_StopEveryone_TheGateOnlyEnemies()
        {
            yield return WallWithGate();
            float face = Far.z + Utils.WallBuilder.Thickness * 0.5f + 0.4f - 0.05f;

            Vector3 enemyAtWall = Walk(Far + new Vector3(0f, 0f, 5f), Far + new Vector3(0f, 0f, -5f), false, false);
            Assert.Greater(enemyAtWall.z, face, "an enemy stops at the wall");
            Vector3 ownAtWall = Walk(Far + new Vector3(0f, 0f, 5f), Far + new Vector3(0f, 0f, -5f), true, false);
            Assert.Greater(ownAtWall.z, face, "so does an own unit going straight at it");

            Vector3 enemyAtGate = Walk(Far + new Vector3(5f, 0f, 5f), Far + new Vector3(5f, 0f, -5f), false, false);
            Assert.Greater(enemyAtGate.z, face, "the gate is shut to enemies");
            Vector3 ownAtGate = Walk(Far + new Vector3(5f, 0f, 5f), Far + new Vector3(5f, 0f, -5f), true, false);
            Assert.Less(ownAtGate.z, Far.z - 4f, "own go through the gate");
        }

        [UnityTest]
        public IEnumerator Villager_SentBehindTheWall_WalksThroughTheGate()
        {
            yield return WallWithGate();
            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(-1f, 0f, 6f)), "WallTestVillager");
            spawned.Add(v);
            yield return null;
            v.GetComponent<RTS.NpcResourceGatherer>().SetGatherDirective(RTS.GatherDirective.Idle);
            Vector3 target = OnGround(Far + new Vector3(0f, 0f, -5f));
            v.GetComponent<RTS.RtsUnit>().IssueMoveCommand(target);
            var track = new System.Text.StringBuilder();
            float mark = 0f;
            for (float t = 0f; t < 20f && Vector3.Distance(v.transform.position, target) > 1.2f; t += Time.deltaTime)
            {
                // never through the wall itself: past its middle only within the gate's opening or round its free end (x -2)
                Vector3 p = v.transform.position - Far;
                if (p.z < 0f && p.z > -0.9f) Assert.IsTrue(p.x > 3.9f - 0.01f || p.x < -2f, $"walked through the wall at {p}");
                if (t >= mark) { mark += 1f; track.Append($" {p.x:0.0},{p.z:0.0}"); }
                yield return null;
            }
            Assert.Less(Vector3.Distance(v.transform.position, target), 1.5f, $"arrived behind the wall (at {v.transform.position - Far}; track{track})");
        }

        [UnityTest]
        public IEnumerator Villager_GoesThroughTheGate_AndRoundAWallRightBehindIt()
        {
            // The tester's town: a gate (x 2..8) with a second wall 3.5 m behind it (x 1..9), the work beyond both. The
            // old route aimed 4 m straight on past the gate, into the second wall: the workers stood pressed against it.
            yield return WallWithGate();
            Segment(BuildingType.CastleWall, OnGround(Far + new Vector3(3f, 0f, -3.5f)), 0f);
            Segment(BuildingType.CastleWall, OnGround(Far + new Vector3(7f, 0f, -3.5f)), 0f);
            yield return null;
            yield return null;
            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(5f, 0f, 7f)), "WallTestVillager");
            spawned.Add(v);
            yield return null;
            v.GetComponent<RTS.NpcResourceGatherer>().SetGatherDirective(RTS.GatherDirective.Idle);
            Vector3 target = OnGround(Far + new Vector3(5f, 0f, -8f));
            v.GetComponent<RTS.RtsUnit>().IssueMoveCommand(target);
            var track = new System.Text.StringBuilder();
            float mark = 0f;
            for (float t = 0f; t < 30f && Vector3.Distance(v.transform.position, target) > 1.2f; t += Time.deltaTime)
            {
                if (t >= mark) { mark += 1f; Vector3 p = v.transform.position - Far; track.Append($" {p.x:0.0},{p.z:0.0}"); }
                yield return null;
            }
            Assert.Less(Vector3.Distance(v.transform.position, target), 1.5f, $"arrived behind both walls (at {v.transform.position - Far}; track{track})");
        }

        /// <summary>A mob outside (z +8) ordered at a target behind a plain wall (z -6), no gate near.</summary>
        private (AI.HostileMobAI mob, HealthComponent wall, HealthComponent target) Siege(AI.HostileMobType type)
        {
            Ground();
            WallSegment wall = Segment(BuildingType.CastleWall, OnGround(Far), 0f);
            Segment(BuildingType.CastleWall, OnGround(Far + new Vector3(-4f, 0f, 0f)), 0f);
            Segment(BuildingType.CastleWall, OnGround(Far + new Vector3(4f, 0f, 0f)), 0f);
            var target = new GameObject("SiegeTarget");
            target.transform.position = OnGround(Far + new Vector3(0f, 0f, -6f));
            HealthComponent targetHp = target.AddComponent<HealthComponent>();
            targetHp.InitializeHealth(100000);
            spawned.Add(target);
            GameObject m = Utils.HostileCampBuilder.CreateHostileMob(type, "Besieger");
            CharacterController cc = m.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            m.transform.position = OnGround(Far + new Vector3(0.5f, 0f, 8f));
            if (cc != null) cc.enabled = true;
            spawned.Add(m);
            return (m.GetComponent<AI.HostileMobAI>(), wall.GetComponent<HealthComponent>(), targetHp);
        }

        [UnityTest]
        public IEnumerator WeakAttackers_ClimbALadder_IntoTheSettlement()
        {
            var (mob, wall, target) = Siege(AI.HostileMobType.Bandit);
            yield return null;
            yield return null;
            mob.OrderAttack(target);
            bool sawLadder = false;
            float t = 0f;
            for (; t < 40f && mob.transform.position.z > Far.z - 1.8f; t += Time.deltaTime)
            {
                sawLadder |= SiegeLadder.Active.Count > 0;
                yield return null;
            }
            Assert.IsTrue(sawLadder, "a ladder was raised");
            Assert.Less(mob.transform.position.z, Far.z - 1.7f, $"the bandit got inside (at {mob.transform.position - Far} after {t:0} s)");
            Assert.AreEqual(wall.MaxHealth, wall.CurrentHealth, "and did not harm the wall");
            foreach (SiegeLadder l in new List<SiegeLadder>(SiegeLadder.Active)) if (l != null) Object.Destroy(l.gameObject);
        }

        /// <summary>Waits until the besieger is up its ladder (between the ground and the walkway); false if it never gets there.</summary>
        private IEnumerator UntilHalfwayUp(AI.HostileMobAI mob, HealthComponent wall, System.Action<bool> done)
        {
            for (float t = 0f; t < 40f; t += Time.deltaTime)
            {
                if (mob.IsClimbing && mob.transform.position.y > wall.transform.position.y + 1f) { done(true); yield break; }
                yield return null;
            }
            done(false);
        }

        [UnityTest]
        public IEnumerator WallFallsUnderAClimber_ItDropsToTheGround_AndGoesOn()
        {
            var (mob, wall, target) = Siege(AI.HostileMobType.Bandit);
            yield return null;
            yield return null;
            mob.OrderAttack(target);
            bool up = false;
            yield return UntilHalfwayUp(mob, wall, b => up = b);
            Assert.IsTrue(up, "it got onto a ladder");
            WallSegment under = SiegeLadder.Active.Count > 0 ? SiegeLadder.Active[0].Wall : null;
            Assert.IsNotNull(under);
            under.GetComponent<HealthComponent>().TakeDamage(1000000); // the wall it climbs falls
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(mob.IsClimbing, "no climbing a wall that is not there");
            Assert.Less(mob.transform.position.y - RTS.RtsUnit.GetTerrainHeight(mob.transform.position), 0.6f, "it is on the ground, not in the air");
            float t = 0f;
            for (; t < 30f && mob.transform.position.z > Far.z - 1.8f; t += Time.deltaTime) yield return null;
            Assert.Less(mob.transform.position.z, Far.z - 1.7f, $"and goes on through the gap (at {mob.transform.position - Far} after {t:0} s)");
        }

        [UnityTest]
        public IEnumerator ClimberKilledOnTheLadder_FallsDown_AndFreesTheLadder()
        {
            var (mob, wall, target) = Siege(AI.HostileMobType.Bandit);
            yield return null;
            yield return null;
            mob.OrderAttack(target);
            bool up = false;
            yield return UntilHalfwayUp(mob, wall, b => up = b);
            Assert.IsTrue(up, "it got onto a ladder");
            SiegeLadder ladder = SiegeLadder.Active[0];
            Assert.IsFalse(ladder.Free, "taken while it climbs");
            mob.GetComponent<HealthComponent>().TakeDamage(1000000); // shot off the ladder
            yield return null;
            yield return null;
            Assert.IsTrue(ladder.Free, "the next one may climb");
            Assert.Less(mob.transform.position.y - RTS.RtsUnit.GetTerrainHeight(mob.transform.position), 0.6f, "the body is on the ground");
            foreach (SiegeLadder l in new List<SiegeLadder>(SiegeLadder.Active)) if (l != null) Object.Destroy(l.gameObject);
        }

        /// <summary>
        /// A whole assault: six raiders of three kinds against a 20 m wall line — the strong break through, the rest take
        /// ladders in turns — and nobody stands stuck outside: each one gets in (or dies) and none waits still for long.
        /// </summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Assault_OnAWallLine_EveryoneGetsIn_NobodyStuck()
        {
            Ground();
            for (int i = -2; i <= 2; i++) Segment(BuildingType.CastleWall, OnGround(Far + new Vector3(i * 4f, 0f, 0f)), 0f);
            var target = new GameObject("AssaultTarget");
            target.transform.position = OnGround(Far + new Vector3(0f, 0f, -8f));
            HealthComponent targetHp = target.AddComponent<HealthComponent>();
            targetHp.InitializeHealth(1000000);
            spawned.Add(target);
            var kinds = new[] { AI.HostileMobType.Bandit, AI.HostileMobType.Bandit, AI.HostileMobType.Goblin, AI.HostileMobType.Bandit, AI.HostileMobType.WarlordGuard, AI.HostileMobType.Goblin };
            var mobs = new List<AI.HostileMobAI>();
            for (int i = 0; i < kinds.Length; i++)
            {
                GameObject m = Utils.HostileCampBuilder.CreateHostileMob(kinds[i], "Assault" + i);
                CharacterController cc = m.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                m.transform.position = OnGround(Far + new Vector3(-7.5f + i * 3f, 0f, 9f + (i % 2) * 2f));
                if (cc != null) cc.enabled = true;
                spawned.Add(m);
                mobs.Add(m.GetComponent<AI.HostileMobAI>());
            }
            yield return null;
            yield return null;
            foreach (AI.HostileMobAI m in mobs) m.OrderAttack(targetHp);

            Time.timeScale = 3f;
            var lastPos = new Vector3[mobs.Count];
            var stillFor = new float[mobs.Count];
            float t = 0f;
            bool AllIn()
            {
                foreach (AI.HostileMobAI m in mobs) if (m != null && m.Health != null && m.Health.IsAlive && m.transform.position.z > Far.z - 1.7f) return false;
                return true;
            }
            for (; t < 150f && !AllIn(); t += Time.deltaTime)
            {
                for (int i = 0; i < mobs.Count; i++)
                {
                    AI.HostileMobAI m = mobs[i];
                    if (m == null || m.Health == null || !m.Health.IsAlive || m.transform.position.z < Far.z - 1.7f) continue;
                    if ((m.transform.position - lastPos[i]).sqrMagnitude > 0.25f || m.IsClimbing) { lastPos[i] = m.transform.position; stillFor[i] = 0f; continue; }
                    stillFor[i] += Time.deltaTime;
                    // waiting its turn at a ladder is fine, standing still outside for half a minute is not
                    Assert.Less(stillFor[i], 30f, $"{m.name} ({kinds[i]}) stuck outside at {m.transform.position - Far} after {t:0} s");
                }
                yield return null;
            }
            Time.timeScale = 1f;
            Assert.IsTrue(AllIn(), $"everyone got in within {t:0} s of game time");
            foreach (SiegeLadder l in new List<SiegeLadder>(SiegeLadder.Active)) if (l != null) Object.Destroy(l.gameObject);
        }

        [UnityTest]
        public IEnumerator TwoClimbers_TakeTurns_OnOneLadder()
        {
            var (first, wall, target) = Siege(AI.HostileMobType.Bandit);
            GameObject m = Utils.HostileCampBuilder.CreateHostileMob(AI.HostileMobType.Bandit, "SecondBesieger");
            CharacterController cc = m.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            m.transform.position = OnGround(Far + new Vector3(-0.5f, 0f, 9f));
            if (cc != null) cc.enabled = true;
            spawned.Add(m);
            AI.HostileMobAI second = m.GetComponent<AI.HostileMobAI>();
            yield return null;
            yield return null;
            first.OrderAttack(target);
            second.OrderAttack(target);
            int mostLadders = 0;
            bool bothOn = false;
            float t = 0f;
            for (; t < 60f && (first.transform.position.z > Far.z - 1.8f || second.transform.position.z > Far.z - 1.8f); t += Time.deltaTime)
            {
                mostLadders = Mathf.Max(mostLadders, SiegeLadder.Active.Count);
                bothOn |= first.IsClimbing && second.IsClimbing && first.transform.position.y < wall.transform.position.y + 3f && second.transform.position.y < wall.transform.position.y + 3f
                          && first.transform.position.y > wall.transform.position.y + 0.3f && second.transform.position.y > wall.transform.position.y + 0.3f
                          && first.transform.position.z > Far.z + 1f && second.transform.position.z > Far.z + 1f; // both on the ladder (outside), not one coming down inside
                yield return null;
            }
            Assert.Less(first.transform.position.z, Far.z - 1.7f, $"the first got in (at {first.transform.position - Far})");
            Assert.Less(second.transform.position.z, Far.z - 1.7f, $"and the second after it (at {second.transform.position - Far}, {t:0} s)");
            Assert.AreEqual(1, mostLadders, "side by side they share one ladder");
            Assert.IsFalse(bothOn, "one at a time on it");
            foreach (SiegeLadder l in new List<SiegeLadder>(SiegeLadder.Active)) if (l != null) Object.Destroy(l.gameObject);
        }

        [UnityTest]
        public IEnumerator StrongAttackers_BreakTheWall()
        {
            var (mob, wall, target) = Siege(AI.HostileMobType.WarlordGuard);
            yield return null;
            yield return null;
            mob.OrderAttack(target);
            for (float t = 0f; t < 25f && wall.CurrentHealth == wall.MaxHealth; t += Time.deltaTime) yield return null;
            Assert.Less(wall.CurrentHealth, wall.MaxHealth, $"the warlord strikes the wall (at {mob.transform.position - Far}, {mob.CurrentStateType}, strength {mob.Combat.Strength}, ranged {mob.Combat.IsRanged}, target {(mob.CurrentCombatTarget != null ? mob.CurrentCombatTarget.name : "none")})");
            Assert.AreEqual(0, SiegeLadder.Active.Count, "no ladder for the strong");
        }

        [UnityTest]
        public IEnumerator Defender_GoesUpTheStairsToAPost_AndComesDown()
        {
            Ground();
            Vector3 at = OnGround(Far);
            Segment(BuildingType.WallStairs, at, 0f);
            WallSegment wall = Segment(BuildingType.CastleWall, at + new Vector3(4f, 0f, 0f), 0f);
            yield return null;
            yield return null;
            WallWalk.Post post = WallWalk.FreePost(wall.transform.position + Vector3.right);
            Assert.IsTrue(post.Valid, "a post up there");

            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(2f, 0f, -6f)), "WallDefender");
            spawned.Add(v);
            yield return null;
            v.GetComponent<RTS.NpcResourceGatherer>().SetGatherDirective(RTS.GatherDirective.Idle);
            RTS.RtsUnit unit = v.GetComponent<RTS.RtsUnit>();
            Assert.IsTrue(unit.SendToWall(post));
            for (float t = 0f; t < 25f && !unit.IsPostedOnWall; t += Time.deltaTime) yield return null;
            Assert.IsTrue(unit.IsPostedOnWall, $"posted (at {v.transform.position - Far})");
            Assert.Less(Vector3.Distance(v.transform.position, WallWalk.PostPoint(post)), 0.1f, "at its post on the walkway");
            Assert.IsTrue(v.GetComponent<CombatUnit>().OnWall);

            Vector3 down = OnGround(Far + new Vector3(0f, 0f, -7f));
            unit.IssueMoveCommand(down);
            for (float t = 0f; t < 25f && (unit.WallBound || FlatDist(v.transform.position, down) > 1.3f); t += Time.deltaTime) yield return null;
            Assert.IsFalse(unit.WallBound, "came down");
            Assert.Less(FlatDist(v.transform.position, down), 1.3f, "and went where it was sent");
            Assert.Less(Mathf.Abs(v.transform.position.y - down.y), 0.5f, "on the ground");
        }

        private static float FlatDist(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        [UnityTest]
        public IEnumerator NoStairs_NoPosts()
        {
            Ground();
            Segment(BuildingType.CastleWall, OnGround(Far), 0f);
            yield return null;
            Assert.IsFalse(WallWalk.FreePost(Far).Valid);
        }

        [UnityTest]
        public IEnumerator OnTheWalkway_AClimberFightsTheDefender()
        {
            var (mob, wall, target) = Siege(AI.HostileMobType.Bandit);
            Segment(BuildingType.WallStairs, OnGround(Far + new Vector3(-8f, 0f, 0f)), 0f);
            yield return null;
            yield return null;
            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(-4f, 0f, -4f)), "WalkwayDefender");
            spawned.Add(v);
            yield return null;
            v.GetComponent<RTS.NpcResourceGatherer>().SetGatherDirective(RTS.GatherDirective.Idle);
            RTS.RtsUnit unit = v.GetComponent<RTS.RtsUnit>();
            HealthComponent defender = v.GetComponent<HealthComponent>();
            WallWalk.Post post = WallWalk.FreePost(wall.transform.position);
            Assert.IsTrue(unit.SendToWall(post));
            for (float t = 0f; t < 25f && !unit.IsPostedOnWall; t += Time.deltaTime) yield return null;
            Assert.IsTrue(unit.IsPostedOnWall, "the defender is up");

            mob.OrderAttack(target);
            HealthComponent mobHp = mob.GetComponent<HealthComponent>();
            bool fought = false;
            var track = new System.Text.StringBuilder();
            float mark = 0f;
            for (float t = 0f; t < 45f && !fought; t += Time.deltaTime)
            {
                fought = defender.CurrentHealth < defender.MaxHealth || !mobHp.IsAlive || mobHp.CurrentHealth < mobHp.MaxHealth;
                if (t >= mark) { mark += 3f; Vector3 p = mob.transform.position - Far; track.Append($" [{p.x:0.0} {p.y:0.0} {p.z:0.0} {mob.CurrentStateType} {(mob.CurrentCombatTarget != null ? mob.CurrentCombatTarget.name : "-")} L{SiegeLadder.Active.Count}]"); }
                yield return null;
            }
            Assert.IsTrue(fought, $"they fought on the walls (defender at {v.transform.position - Far}; bandit{track})");
            foreach (SiegeLadder l in new List<SiegeLadder>(SiegeLadder.Active)) if (l != null) Object.Destroy(l.gameObject);
        }

        [UnityTest]
        public IEnumerator ABrokenWall_OpensTheWay_AndTakesItsLadderAndPiers()
        {
            Ground();
            WallSegment wall = Segment(BuildingType.CastleWall, OnGround(Far), 0f);
            yield return null;
            yield return null;
            SiegeLadder ladder = SiegeLadder.Raise(wall, wall.transform.position, Vector3.forward);
            Assert.Greater(Walk(Far + new Vector3(0f, 0f, 5f), Far + new Vector3(0f, 0f, -5f), false, false).z, Far.z + 0.9f, "shut");

            wall.GetComponent<HealthComponent>().TakeDamage(1000000);
            for (int i = 0; i < 4; i++) yield return null;
            Assert.IsTrue(wall == null, "the wall is gone");
            Assert.IsTrue(ladder == null, "its ladder with it");
            Assert.AreEqual(0, PiersNear(Far, 10f), "and its piers");
            Assert.Less(Walk(Far + new Vector3(0f, 0f, 5f), Far + new Vector3(0f, 0f, -5f), false, false).z, Far.z - 4f, "open now");
        }

        [UnityTest]
        public IEnumerator Gate_OpensForAnOwnUnit_NotForAnEnemy()
        {
            Ground();
            WallSegment gate = Segment(BuildingType.WallGate, OnGround(Far), 0f);
            WallGateDoors doors = gate.GetComponent<WallGateDoors>();
            GameObject m = Utils.HostileCampBuilder.CreateHostileMob(AI.HostileMobType.Bandit, "GateEnemy");
            m.GetComponent<AI.HostileMobAI>().enabled = false;
            CharacterController cc = m.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            m.transform.position = OnGround(Far + new Vector3(0f, 0f, 2.5f));
            spawned.Add(m);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(0f, doors.Angle, 0.01f, "shut to an enemy at the gate");

            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(0f, 0f, -2.5f)), "GateFriend");
            spawned.Add(v);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(WallGateDoors.OpenAngle, doors.Angle, 0.5f, "open for an own unit");
        }

        [UnityTest]
        public IEnumerator Route_TakesTheGateWithTheShorterWay()
        {
            Ground();
            // gate A at x 0, a long wall, gate B at x 30
            Segment(BuildingType.WallGate, OnGround(Far), 0f);
            for (int i = 0; i < 6; i++) Segment(BuildingType.CastleWall, OnGround(Far + new Vector3(5f + i * 4f, 0f, 0f)), 0f);
            Segment(BuildingType.WallGate, OnGround(Far + new Vector3(30f, 0f, 0f)), 0f);
            yield return null;
            yield return null;
            Vector3 via = WallBarriers.Route(Far + new Vector3(24f, 0f, 6f), Far + new Vector3(25f, 0f, -6f));
            Assert.Less(Mathf.Abs(via.x - (Far.x + 30f)), 1.6f, $"near gate B (heading {via - Far})");
            via = WallBarriers.Route(Far + new Vector3(4f, 0f, 6f), Far + new Vector3(6f, 0f, -6f));
            Assert.Less(Mathf.Abs(via.x - Far.x), 1.6f, $"near gate A (heading {via - Far})");
            Vector3 straight = Far + new Vector3(10f, 0f, 9f);
            Assert.AreEqual(straight, WallBarriers.Route(Far + new Vector3(10f, 0f, 6f), straight), "no wall in the way: straight there");
        }

        [UnityTest]
        public IEnumerator Snap_TurnsLeftAndRight_NoFurtherThanARightAngle()
        {
            Ground();
            Segment(BuildingType.CastleWall, OnGround(Far), 0f);
            yield return null;
            // A turn outward (+z) off the +x end: the new segment runs along +z, its outside turns with it to -x
            Assert.IsTrue(WallSegment.Snap(Far + new Vector3(2.2f, 0f, 2.5f), 4f, out Vector3 c, out float yaw, out _));
            Assert.Less((c - (OnGround(Far) + new Vector3(2f, 0f, 2f))).magnitude, 0.05f);
            Vector3 outside = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Assert.Less(outside.x, -0.99f, "turning out, the outside turns to -x");
            // Behind the end (over the wall itself): clamped to a right angle, not folded back
            Assert.IsTrue(WallSegment.Snap(Far + new Vector3(1f, 0f, -0.5f), 4f, out c, out _, out _));
            Assert.Less(Mathf.Abs(c.x - (Far.x + 2f)), 0.05f, "a right angle at most");
        }

        [UnityTest]
        public IEnumerator Archer_OnThePost_ReachesFurtherThanOnTheGround()
        {
            Ground();
            Vector3 at = OnGround(Far);
            Segment(BuildingType.WallStairs, at, 0f);
            WallSegment wall = Segment(BuildingType.CastleWall, at + new Vector3(4f, 0f, 0f), 0f);
            yield return null;
            yield return null;
            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(3f, 0f, -5f)), "WallArcher");
            spawned.Add(v);
            yield return null;
            v.GetComponent<RTS.NpcResourceGatherer>().SetGatherDirective(RTS.GatherDirective.Idle);
            v.GetComponent<Crafting.Equipment>().Equip(new Crafting.Item(Crafting.ItemType.Bow, 1));
            CombatUnit bow = v.GetComponent<CombatUnit>();
            Assert.IsTrue(bow.IsRanged, "armed with a bow");
            float groundRange = bow.AttackRange;
            RTS.RtsUnit unit = v.GetComponent<RTS.RtsUnit>();
            Assert.IsTrue(unit.SendToWall(WallWalk.FreePost(wall.transform.position)));
            for (float t = 0f; t < 25f && !unit.IsPostedOnWall; t += Time.deltaTime) yield return null;
            Assert.IsTrue(unit.IsPostedOnWall);

            // An enemy outside, past the bow's reach on the ground but within it from the walls
            float d = groundRange + 1.5f;
            GameObject m = Utils.HostileCampBuilder.CreateHostileMob(AI.HostileMobType.Bandit, "Target");
            m.GetComponent<AI.HostileMobAI>().SpeedCap = 0f; // stays where it is, still a target in the grid
            CharacterController cc = m.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Vector3 flat = v.transform.position + Vector3.forward * d;
            m.transform.position = OnGround(flat);
            spawned.Add(m);
            Assert.Greater(Vector3.Distance(v.transform.position, m.transform.position), groundRange + 1f, "out of reach from the ground");
            HealthComponent hp = m.GetComponent<HealthComponent>();
            for (float t = 0f; t < 8f && hp.CurrentHealth == hp.MaxHealth; t += Time.deltaTime) yield return null;
            Assert.Less(hp.CurrentHealth, hp.MaxHealth, "shot from the walls");
            Assert.IsTrue(unit.IsPostedOnWall, "and stayed up");
        }

        [UnityTest]
        public IEnumerator Alarm_SendsTheArcherUp_AndDownAgain()
        {
            Ground();
            Vector3 at = OnGround(Far);
            Segment(BuildingType.WallStairs, at, 0f);
            Segment(BuildingType.CastleWall, at + new Vector3(4f, 0f, 0f), 0f);
            GameObject hall = TownHall.CreatePrefab("AlarmHall"); // the settlement's home (the scene has none)
            hall.transform.position = OnGround(Far + new Vector3(0f, 0f, -12f));
            spawned.Add(hall);
            GameObject v = BuildingNode.CreateWorkerNpc(OnGround(Far + new Vector3(3f, 0f, -6f)), "AlarmArcher");
            spawned.Add(v);
            yield return null;
            v.GetComponent<RTS.NpcResourceGatherer>().SetGatherDirective(RTS.GatherDirective.Idle);
            v.GetComponent<Crafting.Equipment>().Equip(new Crafting.Item(Crafting.ItemType.Bow, 1));
            GameObject m = Utils.HostileCampBuilder.CreateHostileMob(AI.HostileMobType.Bandit, "Raider");
            m.GetComponent<AI.HostileMobAI>().SpeedCap = 0f; // stays where it is, still a target in the grid
            CharacterController cc = m.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            m.transform.position = OnGround(Far + new Vector3(2f, 0f, 30f));
            spawned.Add(m);
            yield return null;

            RTS.SettlementAlarm alarm = Object.FindFirstObjectByType<RTS.SettlementAlarm>();
            Assert.IsNotNull(alarm);
            RTS.SettlementAlarm.Raise();
            alarm.Tick();
            RTS.RtsUnit unit = v.GetComponent<RTS.RtsUnit>();
            Assert.IsTrue(unit.WallBound && unit.PostedByAlarm, "the archer goes to the walls");
            for (float t = 0f; t < 25f && !unit.IsPostedOnWall; t += Time.deltaTime) yield return null;
            Assert.IsTrue(unit.IsPostedOnWall);

            Object.Destroy(m);
            RTS.SettlementAlarm.StandDown();
            for (float t = 0f; t < 25f && unit.WallBound; t += Time.deltaTime) yield return null;
            Assert.IsFalse(unit.WallBound, "down again when it is over");
        }

        [UnityTest]
        public IEnumerator OnlyTheStrong_PickAWallToAttack()
        {
            Ground();
            Segment(BuildingType.CastleWall, OnGround(Far), 0f);
            AI.HostileMobAI Mob(AI.HostileMobType type)
            {
                GameObject m = Utils.HostileCampBuilder.CreateHostileMob(type, "WallLooker");
                m.GetComponent<AI.HostileMobAI>().enabled = false;
                CharacterController cc = m.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                m.transform.position = OnGround(Far + new Vector3(0f, 0f, 4f));
                spawned.Add(m);
                return m.GetComponent<AI.HostileMobAI>();
            }
            AI.HostileMobAI bandit = Mob(AI.HostileMobType.Bandit), warlord = Mob(AI.HostileMobType.WarlordGuard);
            yield return null;
            HealthComponent b = bandit.FindNearestTarget(), w = warlord.FindNearestTarget();
            Assert.IsTrue(b == null || b.GetComponent<WallSegment>() == null, "a bandit does not go for a wall it cannot harm");
            Assert.IsNotNull(w, "a warlord does");
            Assert.IsNotNull(w.GetComponent<WallSegment>());
        }

        [UnityTest]
        public IEnumerator OwnUnits_GoRoundThroughTheGate()
        {
            yield return WallWithGate();
            Vector3 target = Far + new Vector3(-1f, 0f, -5f);
            Assert.IsTrue(WallBarriers.Blocked(Far + new Vector3(0f, 0f, 5f), target, true));
            Vector3 end = Walk(Far + new Vector3(0f, 0f, 5f), target, true, true, 20f);
            Assert.Less(Vector3.Distance(new Vector3(end.x, 0f, end.z), new Vector3(target.x, 0f, target.z)), 0.5f, $"behind the wall by way of the gate: ended at {end - Far}, heading {WallBarriers.Route(end, target) - Far}, blocked {WallBarriers.Blocked(end, target, true)}");
        }
    }
}
