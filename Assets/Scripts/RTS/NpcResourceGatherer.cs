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
        WorkingInsideMine
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

        private ResourceNodeTree targetTree;
        private OrePebbleNode targetPebble;
        private MineNode targetMine;
        private MineNode assignedMine;

        private int carriedWood = 0;
        private int carriedOreTier = -1;
        private int carriedOreAmount = 0;

        private float harvestTimer = 0f;
        private float searchTimer = 0f;

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
                              && !IsInsideMine && (rtsUnit == null || (!rtsUnit.WallBound && !rtsUnit.HasAttackTarget));
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

        /// <summary>Comes out of the mine at its entrance and goes back to the current directive.</summary>
        public void LeaveMine()
        {
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
            if (currentState == GathererState.WorkingInsideMine) return;

            // Always process MovingToMineToWork so selected units complete their journey into the Mine
            if (currentState == GathererState.MovingToMineToWork)
            {
                HandleMovingToMineToWorkState();
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
            searchTimer += Time.deltaTime;
            if (searchTimer < 0.8f) return; // Stagger searches across frames
            searchTimer = 0f;

            targetTree = null;
            targetPebble = null;
            targetMine = null;

            Vector3 myPos = transform.position;

            // 1. Check if there is an active Mine with accumulated ore to haul
            if (currentDirective == GatherDirective.Auto || currentDirective == GatherDirective.HaulOreFromMineToChest)
            {
                MineNode mine = MineNode.GetNearestMineWithOre(myPos);
                if (mine != null && mine.AccumulatedOreCount > 0)
                {
                    targetMine = mine;
                    currentState = GathererState.MovingToMine;
                    if (rtsUnit != null) rtsUnit.IssueMoveCommand(mine.GetOutputDepositPosition());
                    return;
                }
            }

            if (currentDirective == GatherDirective.HaulOreFromMineToChest)
            {
                WaitAtMine(myPos);
                return;
            }

            // 2. Search for wild wood / ore pebbles on map (a tree with WorkersPerTree workers already is taken)
            CountTreeCrowds();
            bool allowOre = currentDirective == GatherDirective.Auto || currentDirective == GatherDirective.OrePebblesOnly;
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
                int takenOre = targetMine.TakeOre(3, out int takenTier);
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
                harvestTimer += Time.deltaTime;

                if (currentDirective == GatherDirective.WoodGatherBranchesOnly)
                {
                    targetTree.GatherBranchesProgress(Time.deltaTime);
                }
                else if (currentDirective == GatherDirective.WoodChopTreesOnly)
                {
                    targetTree.ChopWoodProgress(Time.deltaTime);
                }
                else
                {
                    if (targetTree.CurrentState == TreeState.WholeTree) targetTree.GatherBranchesProgress(Time.deltaTime);
                    else targetTree.ChopWoodProgress(Time.deltaTime);
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
