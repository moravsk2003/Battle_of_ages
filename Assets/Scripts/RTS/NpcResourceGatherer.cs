using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.Building;
using BattleOfAges.Combat;

namespace BattleOfAges.RTS
{
    public enum GatherDirective
    {
        Auto,
        WoodGatherBranchesOnly,
        WoodChopTreesOnly,
        OrePebblesOnly,
        HaulOreFromMineToChest
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
    /// Utilizes WorkerRegistry for zero-GC worker tracking and OverlapSphereNonAlloc for spatial searches.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RtsUnit))]
    public class NpcResourceGatherer : MonoBehaviour
    {
        private static readonly Collider[] searchOverlapBuffer = new Collider[128];

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

        private HealthComponent healthComponent;

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
            // Self-preservation: Immediately drop current target and flee towards base chest
            ClearCurrentTargets();
            Vector3 safePos = StorageChestNode.GetNearestChestDepositPosition(transform.position);
            if (rtsUnit != null)
            {
                rtsUnit.IssueMoveCommand(safePos);
            }
            Debug.LogWarning($"[NPC Worker] ⚠️ {gameObject.name} атакований ({damage} урону)! Тікає під захист бази.");
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
        }

        public bool IsInsideMine => currentState == GathererState.WorkingInsideMine;
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

            ClearCurrentTargets();
            targetTree = tree;
            currentState = GathererState.MovingToResource;
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(tree.transform.position);
        }

        public void CommandHarvestPebble(OrePebbleNode pebble)
        {
            if (pebble == null || pebble.IsPickedUp) return;

            ClearCurrentTargets();
            targetPebble = pebble;
            targetPebble.TargetedBy = this;
            currentState = GathererState.MovingToResource;
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(pebble.transform.position);
        }

        public void CommandDeliverToChest()
        {
            ClearCurrentTargets();
            currentState = GathererState.DeliveringToChest;
            Vector3 chestPos = StorageChestNode.GetNearestChestDepositPosition(transform.position);
            if (rtsUnit != null) rtsUnit.IssueMoveCommand(chestPos);
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

            // Pause AI logic if worker is selected by player in RTS mode
            if (rtsUnit != null && rtsUnit.IsSelected) return;

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
                        FindFirstObjectByType<RtsUnitCommander>()?.Deselect(rtsUnit);
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
        }

        private void HandleFindingResourceState()
        {
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
                // Directive is strictly hauling from mine, stay idle if no mine has ore
                return;
            }

            // 2. Search for wild wood / ore pebbles on map
            int hitCount = Physics.OverlapSphereNonAlloc(myPos, searchRadius, searchOverlapBuffer);
            float nearestSqrDst = searchRadius * searchRadius;

            bool allowOre = (currentDirective == GatherDirective.Auto || currentDirective == GatherDirective.OrePebblesOnly);
            bool allowWoodBranches = (currentDirective == GatherDirective.Auto || currentDirective == GatherDirective.WoodGatherBranchesOnly);
            bool allowWoodChop = (currentDirective == GatherDirective.Auto || currentDirective == GatherDirective.WoodChopTreesOnly);

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = searchOverlapBuffer[i];
                if (col == null) continue;

                if (allowOre)
                {
                    OrePebbleNode pebble = col.GetComponentInParent<OrePebbleNode>();
                    if (pebble != null && !pebble.IsPickedUp)
                    {
                        if (pebble.TargetedBy != null && pebble.TargetedBy != this)
                        {
                            continue;
                        }

                        float sqrDst = (myPos - pebble.transform.position).sqrMagnitude;
                        if (sqrDst < nearestSqrDst)
                        {
                            nearestSqrDst = sqrDst;
                            targetPebble = pebble;
                            targetTree = null;
                        }
                    }
                }

                if (allowWoodBranches || allowWoodChop)
                {
                    ResourceNodeTree tree = col.GetComponentInParent<ResourceNodeTree>();
                    if (tree != null && tree.CurrentState != TreeState.Depleted && targetPebble == null)
                    {
                        if (currentDirective == GatherDirective.WoodGatherBranchesOnly && tree.CurrentState != TreeState.WholeTree)
                        {
                            continue;
                        }

                        float sqrDst = (myPos - tree.transform.position).sqrMagnitude;
                        if (sqrDst < nearestSqrDst)
                        {
                            nearestSqrDst = sqrDst;
                            targetTree = tree;
                        }
                    }
                }
            }

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
