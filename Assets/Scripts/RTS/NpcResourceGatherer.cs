using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.Building;
using BattleOfAges.Combat;
using BattleOfAges.Utils;

namespace BattleOfAges.RTS
{
    public enum GatherDirective
    {
        Auto,
        WoodGatherBranchesOnly,
        WoodChopTreesOnly,
        OrePebblesOnly,
        HaulOreFromMineToChest,
        /// <summary>No job: the unit stands by as a soldier until it is told to work.</summary>
        Idle
    }

    public enum GathererState
    {
        FindingResource,
        MovingToResource,
        Harvesting,
        DeliveringToChest,
        MovingToMine,
        HaulingFromMine,
        MovingToMineToWork,
        WorkingInsideMine,
        /// <summary>Called to a workbench: goes to its place there and crafts until called off.</summary>
        WorkingAtBench,
        Building,
        /// <summary>At a field or a sawmill (WorkPlace).</summary>
        WorkingAtPlace
    }

    /// <summary>
    /// Autonomous worker AI for harvesting wood, ore pebbles, working inside mines, and hauling ore to Storage Chests.
    /// Utilizes WorkerRegistry for zero-GC worker tracking and ResourceGrid for finding the nearest tree or pebble.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RtsUnit))]
    public class NpcResourceGatherer : MonoBehaviour
    {

        [Header("Gatherer AI Configuration")]
        [SerializeField] private GatherDirective currentDirective = GatherDirective.Auto;
        [SerializeField] private float searchRadius = 150.0f;
        [SerializeField] private float harvestDistance = 3.2f;
        [SerializeField] private float chestDepositDistance = 3.6f;

        private RtsUnit rtsUnit;
        private GathererState currentState = GathererState.FindingResource;
        private float stateWatchdogTimer = 0f;
        private Vector3 watchdogFrom;

        private ResourceNodeTree targetTree;
        private OrePebbleNode targetPebble;
        private MineNode targetMine;
        private MineNode assignedMine;

        private int carriedWood = 0;
        private int carriedOreTier = -1;
        private int carriedOreAmount = 0;

        private float harvestTimer = 0f;
        private float workSoundTimer; // the axe's next blow, the twigs' next snap
        private Crafting.Equipment gear;
        private Crafting.Equipment Gear => gear != null ? gear : gear = GetComponent<Crafting.Equipment>();

        /// <summary>How much faster the worn axe fells (ItemStatCalculator.CalculateToolEfficiency: ×1.25 for an axe I), 1 without one.</summary>
        public float AxeEfficiency()
        {
            int level = Gear != null ? Gear.LevelOf(Crafting.ItemType.Axe) : 0;
            return level > 0 ? Crafting.ItemStatCalculator.CalculateToolEfficiency(level) : 1f;
        }
        private float searchTimer = 0f;
        /// <summary>An Auto worker that last chose to haul mine ore (the split by need, AutoShouldHaul).</summary>
        private bool autoHauling;
        private Crafting.WorkbenchNode assignedBench;
        private float benchMoveTimer;

        public GatherDirective CurrentDirective => currentDirective;
        public GathererState CurrentState => currentState;
        /// <summary>Has a job (any directive but Idle); a direct work order gives an idle unit the Auto job.</summary>
        public bool IsWorking => currentDirective != GatherDirective.Idle;

        /// <summary>
        /// Doing nothing (for the idle-worker button): told to work but finding none for a few seconds. A unit told to
        /// stand by (GatherDirective.Idle) is a soldier waiting for orders, not idle. Not while fighting, on the wall,
        /// in a mine or as a hauler waiting at its mine.
        /// </summary>
        public bool IsIdle => IsWorking && searchingFor > IdleAfterSeconds && currentDirective != GatherDirective.HaulOreFromMineToChest
                              && !IsInsideMine && !AtBench && (rtsUnit == null || (!rtsUnit.WallBound && !rtsUnit.HasAttackTarget));
        public const float IdleAfterSeconds = 4f;
        private float searchingFor;

        private HealthComponent healthComponent;
        private bool interrupted;

        private void Awake()
        {
            rtsUnit = GetComponent<RtsUnit>();
            healthComponent = GetComponent<HealthComponent>();
            if (healthComponent != null)
            {
                healthComponent.OnTakeDamage += HandleDamageTaken;
            }
        }

        private void OnDestroy()
        {
            if (healthComponent != null)
            {
                healthComponent.OnTakeDamage -= HandleDamageTaken;
            }
        }

        private void HandleDamageTaken(int damage)
        {
            // A soldier (no job) or a unit ordered to fight holds its ground
            if (!IsWorking || rtsUnit == null || rtsUnit.HasAttackTarget || healthComponent == null || !healthComponent.IsAlive) return;

            // One kind of unit: a worker fights back whoever hit it, while it is not badly hurt (docs/GAME_DESIGN.md §11.1)
            CombatUnit attacker = healthComponent.LastAttacker;
            HealthComponent attackerHp = attacker != null ? attacker.GetComponent<HealthComponent>() : null;
            bool hurt = healthComponent.CurrentHealth < healthComponent.MaxHealth * GameBalance.WorkerFleeShare;
            if (!hurt && attackerHp != null && attackerHp.IsAlive && !IsInsideMine)
            {
                rtsUnit.IssueAttackCommand(attackerHp, ordered: false);
                return;
            }

            // Badly hurt: it drops its job and flees towards the base chest
            ClearCurrentTargets();
            Vector3 safePos = StorageChestNode.GetNearestChestDepositPosition(transform.position);
            if (rtsUnit != null)
            {
                rtsUnit.IssueMoveCommand(safePos);
            }
        }

        private void OnEnable()
        {
            WorkerRegistry.Register(this);
        }

        private void OnDisable()
        {
            WorkerRegistry.Unregister(this);
        }

        public void SetGatherDirective(GatherDirective newDirective)
        {
            currentDirective = newDirective;
            ClearCurrentTargets();
            currentState = GathererState.FindingResource;
            searchingFor = 0f; // a new order: give it time to find work before it counts as idle
        }

        public bool IsInsideMine => currentState == GathererState.WorkingInsideMine;
        /// <summary>Called to a workbench (on the way or at its place): the job window leaves it there; the bench's button lets it go.</summary>
        public bool AtBench => assignedBench != null;
        /// <summary>Building a site (ConstructionSite): the job window leaves it there.</summary>
        public bool AtSite => assignedSite != null;
        private ConstructionSite assignedSite;
        /// <summary>At a field or sawmill (WorkPlace); ordered there it stays, come by itself it leaves when there is enough.</summary>
        public bool AtPlace => assignedPlace != null;
        public WorkPlace AssignedPlace => assignedPlace;
        private WorkPlace assignedPlace;
        private bool placeOrdered;
        private float siteMoveTimer;
        /// <summary>In a mine or at a bench: the job distribution and "all to this job" leave it there.</summary>
        public bool IsStationed => IsMiner || AtBench || AtSite || (AtPlace && placeOrdered);
        public Crafting.WorkbenchNode AssignedBench => assignedBench;

        /// <summary>Goes to its place at the bench (WorkbenchNode.SlotOf) and works there until another order or "Відпустити".</summary>
        public void AssignToBench(Crafting.WorkbenchNode bench)
        {
            if (bench == null || CentralEmpire.Active) return;
            StartWorking();
            ClearCurrentTargets();
            assignedBench = bench;
            currentState = GathererState.WorkingAtBench;
            benchMoveTimer = 0f;
        }

        /// <summary>Goes to work at this field or sawmill (false when it is full).</summary>
        public bool AssignToPlace(WorkPlace place, bool ordered)
        {
            if (place == null || CentralEmpire.Active) return false;
            if (assignedPlace == place) { placeOrdered |= ordered; return true; }
            StartWorking();
            ClearCurrentTargets();
            if (!place.Join(this)) return false;
            assignedPlace = place;
            placeOrdered = ordered;
            currentState = GathererState.WorkingAtPlace;
            siteMoveTimer = 0f;
            return true;
        }

        /// <summary>Done there (or called away, or the place is gone): back to its job.</summary>
        public void LeavePlace(WorkPlace place)
        {
            if (assignedPlace == null || (place != null && assignedPlace != place)) return;
            assignedPlace.Leave(this);
            assignedPlace = null;
            placeOrdered = false;
            if (currentState == GathererState.WorkingAtPlace) currentState = GathererState.FindingResource;
        }

        private void HandleWorkingAtPlaceState()
        {
            if (assignedPlace == null) { currentState = GathererState.FindingResource; return; }
            if (!placeOrdered && !assignedPlace.Wanted) { LeavePlace(assignedPlace); return; } // enough in stock: other work
            Vector3 spot = assignedPlace.SpotFor(this);
            Vector3 off = transform.position - spot;
            off.y = 0f;
            if (off.sqrMagnitude <= 1f) return;
            siteMoveTimer -= Time.deltaTime;
            if (siteMoveTimer <= 0f && rtsUnit != null) { siteMoveTimer = 1f; rtsUnit.IssueMoveCommand(spot); }
        }

        /// <summary>Goes to build this site (false when it is full).</summary>
        public bool AssignToSite(ConstructionSite site)
        {
            if (site == null || CentralEmpire.Active) return false;
            if (assignedSite == site) return true;
            if (!site.NeedsHands) return false;
            StartWorking();
            ClearCurrentTargets();
            site.Join(this);
            assignedSite = site;
            currentState = GathererState.Building;
            siteMoveTimer = 0f;
            return true;
        }

        /// <summary>The site is done (or the worker is called away): back to its job.</summary>
        public void LeaveSite(ConstructionSite site)
        {
            if (assignedSite == null || (site != null && assignedSite != site)) return;
            assignedSite.Leave(this);
            assignedSite = null;
            if (currentState == GathererState.Building) currentState = GathererState.FindingResource;
        }

        /// <summary>Walks to its place round the site and works there (the site counts it once it is there).</summary>
        private void HandleBuildingState()
        {
            if (assignedSite == null) { LeaveSite(null); currentState = GathererState.FindingResource; return; }
            Vector3 spot = assignedSite.SpotFor(this);
            Vector3 off = transform.position - spot;
            off.y = 0f;
            if (off.sqrMagnitude > 1f)
            {
                siteMoveTimer -= Time.deltaTime;
                if (siteMoveTimer <= 0f && rtsUnit != null) { siteMoveTimer = 1f; rtsUnit.IssueMoveCommand(spot); }
                return;
            }
            Vector3 look = assignedSite.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 360f * Time.deltaTime);
        }

        /// <summary>The bench let it go (WorkbenchNode.UnassignWorker): back to its job.</summary>
        public void LeaveBench(Crafting.WorkbenchNode bench)
        {
            if (assignedBench == null || assignedBench != bench) return;
            assignedBench = null;
            if (currentState == GathererState.WorkingAtBench) currentState = GathererState.FindingResource;
        }

        /// <summary>Wood the next town hall step takes, at least AutoWoodReserve: below it most Auto workers fetch wood.</summary>
        public static int WoodWanted()
        {
            TownHall hall = TownHall.Current;
            int next = hall == null ? GameBalance.TownHallWood[1] : hall.Level < TownHall.MaxLevel ? GameBalance.TownHallWood[hall.Level + 1] : 0;
            return Mathf.Max(GameBalance.AutoWoodReserve, next);
        }

        /// <summary>Whether one more Auto worker should haul mine ore: those already hauling against the share of all Auto workers.</summary>
        public static bool AutoShouldHaul(int autoWorkers, int othersHauling, bool woodShort)
        {
            float share = woodShort ? GameBalance.AutoHaulShareWoodShort : GameBalance.AutoHaulShare;
            return othersHauling < Mathf.RoundToInt(autoWorkers * share);
        }

        private bool AutoHaulNow(bool woodShort)
        {
            int auto = 0, hauling = 0;
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
            {
                if (w == null || w.currentDirective != GatherDirective.Auto || w.IsMiner || w.AtBench) continue;
                auto++;
                if (w != this && w.autoHauling) hauling++;
            }
            return AutoShouldHaul(auto, hauling, woodShort);
        }
        /// <summary>Works in a mine (inside, or on the way in): the job window leaves it there; the mine's button brings it out.</summary>
        public bool IsMiner => assignedMine != null;
        public MineNode AssignedMine => assignedMine;

        /// <summary>Puts the worker straight inside the mine (a loaded save).</summary>
        public void EnterMineNow(MineNode mine)
        {
            if (mine == null || !mine.AssignWorker(this)) return;
            assignedMine = mine;
            currentState = GathererState.WorkingInsideMine;
            SetRenderersActive(false);
        }

        /// <summary>Went down on its own, while ore ran short: comes out once there is plenty (one sent stays).</summary>
        private bool autoMiner;
        public bool AutoMiner => autoMiner;

        /// <summary>Comes out of the mine at its entrance and goes back to the current directive.</summary>
        public void LeaveMine()
        {
            autoMiner = false;
            if (assignedMine != null)
            {
                Vector3 exit = assignedMine.GetOutputDepositPosition() + Random.insideUnitSphere.normalized * 0.8f;
                exit.y = RtsUnit.GetTerrainHeight(exit);
                assignedMine.UnassignWorker(this);
                assignedMine = null;
                CharacterController cc = GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                transform.position = exit;
                if (cc != null) cc.enabled = true;
                if (rtsUnit != null) rtsUnit.IssueMoveCommand(exit);
            }
            SetRenderersActive(true);
            currentState = GathererState.FindingResource;
        }

        public void AssignToWorkInMine(MineNode mine)
        {
            if (mine == null) return;
            StartWorking();
            autoMiner = false;

            if (assignedMine != null && assignedMine != mine)
            {
                assignedMine.UnassignWorker(this);
            }

            assignedMine = mine;
            currentState = GathererState.MovingToMineToWork;
            SetRenderersActive(true);
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(mine.GetOutputDepositPosition());
        }

        public void CommandHarvestTree(ResourceNodeTree tree)
        {
            if (tree == null || tree.CurrentState == TreeState.Depleted) return;
            StartWorking();

            ClearCurrentTargets();
            targetTree = tree;
            currentState = GathererState.MovingToResource;
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(tree.transform.position);
        }

        public void CommandHarvestPebble(OrePebbleNode pebble)
        {
            if (pebble == null || pebble.IsPickedUp) return;
            StartWorking();

            ClearCurrentTargets();
            targetPebble = pebble;
            targetPebble.TargetedBy = this;
            currentState = GathererState.MovingToResource;
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(pebble.transform.position);
        }

        public void CommandDeliverToChest()
        {
            StartWorking();
            ClearCurrentTargets();
            currentState = GathererState.DeliveringToChest;
            Vector3 chestPos = StorageChestNode.GetNearestChestDepositPosition(transform.position);
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(chestPos);
        }

        /// <summary>A direct work order turns a soldier (Idle) into a worker with the Auto job.</summary>
        private void StartWorking()
        {
            if (CentralEmpire.Active) return; // the Central Empire's units do not work (docs/GAME_DESIGN.md §9.1.1)
            if (currentDirective == GatherDirective.Idle) currentDirective = GatherDirective.Auto;
        }

        /// <summary>Has wood or ore in hand (on its way to the chest): the job distribution leaves it be.</summary>
        public bool IsCarrying => carriedWood > 0 || carriedOreAmount > 0;
        public ResourceNodeTree TargetTree => targetTree;

        /// <summary>
        /// How far this worker is from where the job is done (a mine for hauling, a tree, a pebble on the ground), for
        /// handing jobs to the nearest workers; float.MaxValue when there is none within its search radius.
        /// </summary>
        public float DistanceToWork(GatherDirective d)
        {
            Vector3 me = transform.position;
            if (d == GatherDirective.Auto || d == GatherDirective.Idle) return 0f;
            if (d == GatherDirective.HaulOreFromMineToChest)
            {
                float best = float.MaxValue;
                foreach (MineNode m in MineNode.ActiveMines) if (m != null) best = Mathf.Min(best, Vector3.Distance(me, m.GetOutputDepositPosition()));
                return best;
            }
            bool wood = d == GatherDirective.WoodChopTreesOnly || d == GatherDirective.WoodGatherBranchesOnly;
            FindNearest(d == GatherDirective.OrePebblesOnly, wood, d == GatherDirective.WoodGatherBranchesOnly, false, out _, out _, out float nearest);
            return nearest;
        }

        private static readonly List<Vector2Int> ringCells = new List<Vector2Int>(96);

        /// <summary>
        /// The nearest pebble and tree within the search radius, from ResourceGrid ring by ring (stops once nothing
        /// further out can be nearer). reserve: skip a pebble another worker has taken and a tree with WorkersPerTree
        /// workers already (CountTreeCrowds first). Only one of pebble / tree is set: the nearer.
        /// </summary>
        private void FindNearest(bool ore, bool wood, bool wholeTreesOnly, bool reserve, out OrePebbleNode pebble, out ResourceNodeTree tree, out float distance)
        {
            Vector3 me = transform.position;
            Vector2Int centre = ResourceGrid.CellOf(me);
            float best = searchRadius * searchRadius;
            pebble = null;
            tree = null;
            int rings = ResourceGrid.RingsFor(searchRadius);
            for (int k = 0; k < rings; k++)
            {
                float ringNear = (k - 1) * ResourceGrid.CellSize;
                if (ringNear > 0f && ringNear * ringNear > best) break;
                ringCells.Clear();
                ResourceGrid.Ring(centre, k, ringCells);
                foreach (Vector2Int cell in ringCells)
                {
                    List<OrePebbleNode> ps = ore ? ResourceGrid.PebblesIn(cell) : null;
                    if (ps != null)
                        foreach (OrePebbleNode p in ps)
                        {
                            if (p.IsPickedUp || (reserve && p.TargetedBy != null && p.TargetedBy != this)) continue;
                            float d = (me - p.transform.position).sqrMagnitude;
                            if (d < best) { best = d; pebble = p; tree = null; }
                        }
                    List<ResourceNodeTree> ts = wood ? ResourceGrid.TreesIn(cell) : null;
                    if (ts != null)
                        foreach (ResourceNodeTree t in ts)
                        {
                            if (t.CurrentState == TreeState.Depleted || (wholeTreesOnly && t.CurrentState != TreeState.WholeTree)) continue;
                            if (reserve && treeCrowd.TryGetValue(t, out int crowd) && crowd >= GameBalance.WorkersPerTree) continue;
                            float d = (me - t.transform.position).sqrMagnitude;
                            if (d < best) { best = d; tree = t; pebble = null; }
                        }
                }
            }
            distance = pebble != null || tree != null ? Mathf.Sqrt(best) : float.MaxValue;
        }

        private static readonly Dictionary<ResourceNodeTree, int> treeCrowd = new Dictionary<ResourceNodeTree, int>();

        /// <summary>How many other workers are already going to or working at each tree (a tree takes WorkersPerTree).</summary>
        private void CountTreeCrowds()
        {
            treeCrowd.Clear();
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers)
            {
                if (w == null || w == this || w.targetTree == null) continue;
                treeCrowd.TryGetValue(w.targetTree, out int k);
                treeCrowd[w.targetTree] = k + 1;
            }
        }

        private void ClearCurrentTargets()
        {
            autoHauling = false;
            if (assignedSite != null) LeaveSite(assignedSite);
            if (assignedPlace != null) LeavePlace(assignedPlace);
            if (assignedBench != null)
            {
                Crafting.WorkbenchNode bench = assignedBench;
                assignedBench = null;
                bench.UnassignWorker(rtsUnit);
            }
            if (assignedMine != null)
            {
                assignedMine.UnassignWorker(this);
                assignedMine = null;
                SetRenderersActive(true);
            }
            if (targetPebble != null && targetPebble.TargetedBy == this)
            {
                targetPebble.TargetedBy = null;
            }
            targetTree = null;
            targetPebble = null;
            targetMine = null;
        }

        private void Update()
        {
            // Inside a mine the worker stays hidden until the mine's "Вивести" button sends them out (LeaveMine)
            if (currentState == GathererState.WorkingInsideMine)
            {
                if (autoMiner && MineNode.OrePlenty) LeaveMine();
                return;
            }

            // Always process MovingToMineToWork so selected units complete their journey into the Mine
            if (currentState == GathererState.MovingToMineToWork)
            {
                HandleMovingToMineToWorkState();
                return;
            }

            if (currentState == GathererState.WorkingAtBench)
            {
                if (rtsUnit == null || !(rtsUnit.IsSelected || rtsUnit.HasAttackTarget || SettlementAlarm.Active || rtsUnit.WallBound)) HandleWorkingAtBenchState();
                return;
            }
            if (currentState == GathererState.Building)
            {
                if (rtsUnit == null || !(rtsUnit.IsSelected || rtsUnit.HasAttackTarget || SettlementAlarm.Active || rtsUnit.WallBound)) HandleBuildingState();
                return;
            }
            if (currentState == GathererState.WorkingAtPlace)
            {
                if (rtsUnit == null || !(rtsUnit.IsSelected || rtsUnit.HasAttackTarget || SettlementAlarm.Active || rtsUnit.WallBound)) HandleWorkingAtPlaceState();
                return;
            }

            // Pause AI logic if worker is selected by player in RTS mode, while it fights or the alarm sounds;
            // afterwards it looks for its work again (the fight took it away from where it was going)
            if (rtsUnit != null && (rtsUnit.IsSelected || rtsUnit.HasAttackTarget || SettlementAlarm.Active || rtsUnit.WallBound))
            {
                if (!rtsUnit.IsSelected) interrupted = true;
                return;
            }
            if (interrupted)
            {
                interrupted = false;
                ClearCurrentTargets();
                currentState = GathererState.FindingResource;
            }

            if (currentState == GathererState.MovingToResource || currentState == GathererState.DeliveringToChest)
            {
                // Stuck, not merely far: the time counts only while it does not get anywhere
                if ((transform.position - watchdogFrom).sqrMagnitude > 1f)
                {
                    watchdogFrom = transform.position;
                    stateWatchdogTimer = 0f;
                }
                stateWatchdogTimer += Time.deltaTime;
                if (stateWatchdogTimer > 10.0f)
                {
                    stateWatchdogTimer = 0f;
                    ClearCurrentTargets();
                    currentState = GathererState.FindingResource;
                }
            }
            else
            {
                stateWatchdogTimer = 0f;
            }

            searchingFor = currentState == GathererState.FindingResource ? searchingFor + Time.deltaTime : 0f;
            switch (currentState)
            {
                case GathererState.FindingResource:
                    HandleFindingResourceState();
                    break;

                case GathererState.MovingToResource:
                    HandleMovingToResourceState();
                    break;

                case GathererState.Harvesting:
                    HandleHarvestingState();
                    break;

                case GathererState.MovingToMine:
                    HandleMovingToMineState();
                    break;

                case GathererState.DeliveringToChest:
                    HandleDeliveringToChestState();
                    break;
            }
        }

        /// <summary>Walks to its place at the bench and stands there facing it (the bench counts it once it is there).</summary>
        private void HandleWorkingAtBenchState()
        {
            if (assignedBench == null || !assignedBench.isActiveAndEnabled)
            {
                ClearCurrentTargets();
                currentState = GathererState.FindingResource;
                return;
            }
            Vector3 slot = assignedBench.SlotOf(rtsUnit);
            Vector3 off = transform.position - slot;
            off.y = 0f;
            if (off.sqrMagnitude > Crafting.WorkbenchNode.SlotReach * Crafting.WorkbenchNode.SlotReach * 0.25f)
            {
                benchMoveTimer -= Time.deltaTime;
                if (benchMoveTimer <= 0f && rtsUnit != null) { benchMoveTimer = 1f; rtsUnit.IssueMoveCommand(slot); }
                return;
            }
            Vector3 look = assignedBench.transform.position - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), 6f * Time.deltaTime);
        }

        private void HandleMovingToMineToWorkState()
        {
            if (assignedMine == null || !assignedMine.gameObject.activeInHierarchy)
            {
                currentState = GathererState.FindingResource;
                return;
            }

            Vector3 entrancePos = assignedMine.GetOutputDepositPosition();
            float sqrDst = (transform.position - entrancePos).sqrMagnitude;

            if (sqrDst <= 3.8f * 3.8f)
            {
                if (!assignedMine.IsFullWorkers && assignedMine.AssignWorker(this))
                {
                    currentState = GathererState.WorkingInsideMine;
                    if (rtsUnit != null)
                    {
                        Managers.GameContext.Commander?.Deselect(rtsUnit);
                        rtsUnit.SetSelected(false);
                        rtsUnit.IssueMoveCommand(transform.position);
                    }
                    SetRenderersActive(false);
                }
                else
                {
                    assignedMine = null;
                    currentState = GathererState.FindingResource;
                }
            }
            else
            {
                if (rtsUnit != null) rtsUnit.IssueMoveCommand(entrancePos);
            }
        }

        private void SetRenderersActive(bool active)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].enabled = active;
            }
            GpuSkinnedCharacter gpu = GetComponentInChildren<GpuSkinnedCharacter>();
            if (gpu != null) gpu.SetHidden(!active); // only one of its two bodies is drawn
        }

        private void HandleFindingResourceState()
        {
            if (!IsWorking) return; // a soldier waits for orders
            if (carriedWood > 0 || carriedOreAmount > 0) { currentState = GathererState.DeliveringToChest; return; } // a load from before a fight goes in first
            searchTimer += Time.deltaTime;
            if (searchTimer < 0.8f) return; // Stagger searches across frames
            searchTimer = 0f;

            targetTree = null;
            targetPebble = null;
            targetMine = null;
            autoHauling = false;

            Vector3 myPos = transform.position;

            // 0. A worker on its own lends a hand where something is being built
            if (currentDirective == GatherDirective.Auto && AssignToSite(ConstructionSite.NearestNeedingHands(myPos, GameBalance.BuildersComeFrom))) return;
            // and to the fields or the sawmill while food or wood runs short
            if (currentDirective == GatherDirective.Auto && AssignToPlace(WorkPlace.NearestWanting(myPos, GameBalance.BuildersComeFrom), false)) return;
            // and down a mine while ore runs short
            if (currentDirective == GatherDirective.Auto)
            {
                MineNode wanting = MineNode.NearestWantingHands(myPos, GameBalance.BuildersComeFrom);
                if (wanting != null) { AssignToWorkInMine(wanting); autoMiner = true; return; }
            }

            // 1. Haul ore from a mine: a hauler always, an Auto worker by need (wood short of the next hall step: most go for wood)
            bool auto = currentDirective == GatherDirective.Auto;
            ResourceManager rm = ResourceManager.Instance;
            bool woodShort = auto && rm != null && rm.WoodCount < WoodWanted();
            if (currentDirective == GatherDirective.HaulOreFromMineToChest || (auto && AutoHaulNow(woodShort)))
            {
                if (TryHaulFromMine(myPos)) return;
            }

            if (currentDirective == GatherDirective.HaulOreFromMineToChest)
            {
                WaitAtMine(myPos);
                return;
            }

            // 2. Search for wild wood / ore pebbles on map (a tree with WorkersPerTree workers already is taken)
            CountTreeCrowds();
            bool allowOre = (auto && !woodShort) || currentDirective == GatherDirective.OrePebblesOnly;
            bool allowWood = currentDirective == GatherDirective.Auto || currentDirective == GatherDirective.WoodGatherBranchesOnly
                             || currentDirective == GatherDirective.WoodChopTreesOnly;
            FindNearest(allowOre, allowWood, currentDirective == GatherDirective.WoodGatherBranchesOnly, true, out targetPebble, out targetTree, out _);

            if (targetPebble != null)
            {
                targetPebble.TargetedBy = this;
                currentState = GathererState.MovingToResource;
                if (rtsUnit != null) rtsUnit.IssueMoveCommand(targetPebble.transform.position);
            }
            else if (targetTree != null)
            {
                currentState = GathererState.MovingToResource;
                if (rtsUnit != null) rtsUnit.IssueMoveCommand(targetTree.transform.position);
            }
            else if (auto)
            {
                TryHaulFromMine(myPos); // no wood or pebble in reach: a mine's ore rather than standing about
            }
        }

        private bool TryHaulFromMine(Vector3 myPos)
        {
            MineNode mine = MineNode.GetNearestMineWithOre(myPos);
            if (mine == null || mine.AccumulatedOreCount <= 0) return false;
            targetMine = mine;
            autoHauling = currentDirective == GatherDirective.Auto;
            currentState = GathererState.MovingToMine;
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(mine.GetOutputDepositPosition());
            return true;
        }

        private void HandleMovingToResourceState()
        {
            Vector3 myPos = transform.position;

            if (targetPebble != null)
            {
                if (targetPebble.IsPickedUp)
                {
                    currentState = GathererState.FindingResource;
                    return;
                }

                float sqrDst = (myPos - targetPebble.transform.position).sqrMagnitude;
                if (sqrDst <= harvestDistance * harvestDistance)
                {
                    currentState = GathererState.Harvesting;
                    harvestTimer = 0f;
                }
                else
                {
                    if (rtsUnit != null) rtsUnit.IssueMoveCommand(targetPebble.transform.position);
                }
            }
            else if (targetTree != null)
            {
                if (targetTree.CurrentState == TreeState.Depleted)
                {
                    currentState = GathererState.FindingResource;
                    return;
                }

                float maxHarvestDst = harvestDistance + 1.0f;
                float sqrDst = (myPos - targetTree.transform.position).sqrMagnitude;
                if (sqrDst <= maxHarvestDst * maxHarvestDst)
                {
                    currentState = GathererState.Harvesting;
                    harvestTimer = 0f;
                    // Going to fell it: the best axe from the store (an own villager; its sword stays in its hands)
                    bool fells = currentDirective == GatherDirective.WoodChopTreesOnly
                                 || currentDirective != GatherDirective.WoodGatherBranchesOnly && targetTree.CurrentState != TreeState.WholeTree;
                    if (fells && rtsUnit != null) Crafting.GearDispenser.GiveTool(Gear, Crafting.ItemType.Axe);
                }
                else
                {
                    if (rtsUnit != null) rtsUnit.IssueMoveCommand(targetTree.transform.position);
                }
            }
            else
            {
                currentState = GathererState.FindingResource;
            }
        }

        private float haulWaitTimer;

        /// <summary>
        /// A hauler with no ore anywhere waits by the nearest mine's door (a player report: the haulers stood wherever
        /// they were, as if gone), each at its own spot, and takes the ore as soon as there is some.
        /// </summary>
        private void WaitAtMine(Vector3 myPos)
        {
            haulWaitTimer -= Time.deltaTime;
            if (haulWaitTimer > 0f) return;
            haulWaitTimer = 1f;
            MineNode nearest = null;
            float best = float.MaxValue;
            foreach (MineNode m in MineNode.ActiveMines)
            {
                if (m == null || !m.gameObject.activeInHierarchy) continue;
                float d = (m.transform.position - myPos).sqrMagnitude;
                if (d < best) { best = d; nearest = m; }
            }
            if (nearest == null || rtsUnit == null) return;
            float a = (GetInstanceID() & 1023) * 0.37f;
            Vector3 spot = nearest.GetOutputDepositPosition() + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.5f;
            if ((spot - myPos).sqrMagnitude > 2.5f * 2.5f) rtsUnit.IssueMoveCommand(spot);
        }

        private void HandleMovingToMineState()
        {
            if (targetMine == null || !targetMine.gameObject.activeInHierarchy)
            {
                currentState = GathererState.FindingResource;
                return;
            }

            Vector3 depositPos = targetMine.GetOutputDepositPosition();
            float sqrDst = (transform.position - depositPos).sqrMagnitude;

            if (sqrDst <= 3.0f * 3.0f)
            {
                // Arrived at Mine output: pick up ore from buffer
                int takenOre = targetMine.TakeOre(GameBalance.HaulerCarry, out int takenTier);
                if (takenOre > 0)
                {
                    carriedOreAmount = takenOre;
                    carriedOreTier = takenTier; // mines yield tiers 0..2 like pebbles
                    currentState = GathererState.DeliveringToChest;
                }
                else
                {
                    currentState = GathererState.FindingResource;
                }
            }
            else
            {
                if (rtsUnit != null) rtsUnit.IssueMoveCommand(depositPos);
            }
        }

        private void HandleHarvestingState()
        {
            if (targetPebble != null)
            {
                if (!targetPebble.IsPickedUp)
                {
                    carriedOreTier = targetPebble.PickupPebble();
                    carriedOreAmount = 1;
                }
                targetPebble = null;
                currentState = GathererState.DeliveringToChest;
            }
            else if (targetTree != null)
            {
                bool chopping = currentDirective == GatherDirective.WoodChopTreesOnly
                                || currentDirective != GatherDirective.WoodGatherBranchesOnly && targetTree.CurrentState != TreeState.WholeTree;
                float axe = chopping ? AxeEfficiency() : 1f; // an axe chops faster, as a pickaxe digs faster (branches are picked by hand)
                harvestTimer += Time.deltaTime * axe;
                workSoundTimer -= Time.deltaTime;
                if (workSoundTimer <= 0f)
                {
                    workSoundTimer = chopping ? 0.75f : 0.9f;
                    UI.GameSound.At(chopping ? UI.GameSound.Kind.Chop : UI.GameSound.Kind.Branches, transform.position);
                }

                if (currentDirective == GatherDirective.WoodGatherBranchesOnly)
                {
                    targetTree.GatherBranchesProgress(Time.deltaTime);
                }
                else if (currentDirective == GatherDirective.WoodChopTreesOnly)
                {
                    targetTree.ChopWoodProgress(Time.deltaTime * axe);
                }
                else
                {
                    if (targetTree.CurrentState == TreeState.WholeTree) targetTree.GatherBranchesProgress(Time.deltaTime);
                    else targetTree.ChopWoodProgress(Time.deltaTime * axe);
                }

                if (harvestTimer >= 2.5f)
                {
                    carriedWood = 2;
                    targetTree = null;
                    currentState = GathererState.DeliveringToChest;
                }
            }
            else
            {
                currentState = GathererState.FindingResource;
            }
        }

        private void HandleDeliveringToChestState()
        {
            // Fix remote deposit bug: Ensure active storage chests exist on map
            if (StorageChestNode.ActiveChests.Count == 0)
            {
                // No storage chest available — wait for player to build one
                return;
            }

            Vector3 chestPos = StorageChestNode.GetNearestChestDepositPosition(transform.position);

            // Require physical proximity: worker MUST walk right up to the chest (<= 2.2m) before depositing
            float sqrDst = (transform.position - chestPos).sqrMagnitude;
            if (sqrDst <= chestDepositDistance * chestDepositDistance)
            {
                if (ResourceManager.Instance != null)
                {
                    if (carriedWood > 0)
                    {
                        ResourceManager.Instance.AddResource(ResourceType.Wood, carriedWood);
                        carriedWood = 0;
                    }

                    if (carriedOreAmount > 0 && carriedOreTier >= 0)
                    {
                        ResourceManager.Instance.AddOre(carriedOreTier, carriedOreAmount);
                        carriedOreAmount = 0;
                        carriedOreTier = -1;
                    }
                }

                currentState = GathererState.FindingResource;
            }
            else
            {
                // Continuously issue move command to ensure worker travels all the way to chest
                if (rtsUnit != null) rtsUnit.IssueMoveCommand(chestPos);
            }
        }
    }
}
