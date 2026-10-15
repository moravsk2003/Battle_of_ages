using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.UI;
using BattleOfAges.InputSystem;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BattleOfAges.Player
{
    [DisallowMultipleComponent]
    public class PlayerResourceHarvester : MonoBehaviour
    {
        [Header("Harvesting Configuration")]
        [SerializeField] private float harvestRange = 6.0f;
        [SerializeField] private float proximityRadius = 5.0f;

        private BattleOfAges.Crafting.Equipment gear;

        // Tools work while they are in the weapon slot
        public int EquippedAxeLevel => Gear != null ? Gear.LevelOf(BattleOfAges.Crafting.ItemType.Axe) : 0;
        public int EquippedPickaxeLevel => Gear != null ? Gear.LevelOf(BattleOfAges.Crafting.ItemType.Pickaxe) : 0;

        private BattleOfAges.Crafting.Equipment Gear
        {
            get
            {
                if (gear == null) gear = GetComponent<BattleOfAges.Crafting.Equipment>();
                return gear;
            }
        }

        private ResourceUI resourceUI;
        private Camera playerCamera;
        private ResourceNodeTree activeTreeTarget;
        private ResourceNodeTree previousTreeTarget;
        private OrePebbleNode activePebbleTarget;
        private StorageChestNode activeChestTarget;
        private BattleOfAges.Crafting.WorkbenchNode activeWorkbenchTarget;

        private void Start()
        {
            resourceUI = FindFirstObjectByType<ResourceUI>();
            EnsureCameraReference();
        }

        private void EnsureCameraReference()
        {
            if (playerCamera == null)
            {
                playerCamera = GetComponentInChildren<Camera>();
                if (playerCamera == null && Camera.main != null)
                {
                    playerCamera = Camera.main;
                }
            }
        }

        private void Update()
        {
            EnsureCameraReference();

            if ((NpcGatherCommandUI.Instance != null && NpcGatherCommandUI.Instance.IsOpen) ||
                (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) ||
                (BuildMenuUI.Instance != null && BuildMenuUI.Instance.IsOpen) ||
                (WorkbenchUI.Instance != null && WorkbenchUI.Instance.IsOpen) ||
                EquipmentUI.IsVisible ||
                (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused()))
            {
                if (resourceUI != null) resourceUI.HideTreePrompt();
                return;
            }

            FindBestInteractionTarget();

            // Cancel branch gathering on previous tree if player moved away or changed target
            if (previousTreeTarget != null && previousTreeTarget != activeTreeTarget)
            {
                previousTreeTarget.CancelBranchGathering();
            }
            previousTreeTarget = activeTreeTarget;

            if (activeWorkbenchTarget != null)
            {
                UpdateWorkbenchPromptUI();
                HandleWorkbenchInput();
            }
            else if (activeChestTarget != null)
            {
                UpdateChestPromptUI();
                HandleChestInput();
            }
            else if (activePebbleTarget != null)
            {
                UpdatePebblePromptUI();
                HandlePebblePickupInput();
            }
            else if (activeTreeTarget != null)
            {
                UpdateTreePromptUI();
                HandleTreeHarvestInput();
            }
            else
            {
                if (resourceUI != null) resourceUI.HideTreePrompt();
            }
        }

        private static readonly RaycastHit[] sphereHitsBuffer = new RaycastHit[16];
        private static readonly Collider[] overlapHitsBuffer = new Collider[16];

        private void FindBestInteractionTarget()
        {
            activeTreeTarget = null;
            activePebbleTarget = null;
            activeChestTarget = null;
            activeWorkbenchTarget = null;

            // 1. Raycast / SphereCast Forward from Camera for precise aiming
            if (playerCamera != null && playerCamera.enabled)
            {
                Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
                int hitCount = Physics.SphereCastNonAlloc(ray, 0.4f, sphereHitsBuffer, harvestRange);
                
                float nearestPebbleDst = harvestRange;
                float nearestTreeDst = harvestRange;

                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = sphereHitsBuffer[i];
                    if (hit.collider == null) continue;

                    BattleOfAges.Crafting.WorkbenchNode wb = hit.collider.GetComponentInParent<BattleOfAges.Crafting.WorkbenchNode>();
                    if (wb != null)
                    {
                        activeWorkbenchTarget = wb;
                        return;
                    }

                    StorageChestNode chest = hit.collider.GetComponentInParent<StorageChestNode>();
                    if (chest != null)
                    {
                        activeChestTarget = chest;
                        return;
                    }

                    OrePebbleNode pebble = hit.collider.GetComponentInParent<OrePebbleNode>();
                    if (pebble != null && !pebble.IsPickedUp && hit.distance < nearestPebbleDst)
                    {
                        nearestPebbleDst = hit.distance;
                        activePebbleTarget = pebble;
                    }

                    ResourceNodeTree tree = hit.collider.GetComponentInParent<ResourceNodeTree>();
                    if (tree != null && tree.CurrentState != TreeState.Depleted && hit.distance < nearestTreeDst)
                    {
                        nearestTreeDst = hit.distance;
                        activeTreeTarget = tree;
                    }
                }

                if (activePebbleTarget != null) return;
                if (activeTreeTarget != null) return;
            }

            // 2. Proximity OverlapSphere Fallback around player feet
            int overlapCount = Physics.OverlapSphereNonAlloc(transform.position, proximityRadius, overlapHitsBuffer);
            float minDst = proximityRadius;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider col = overlapHitsBuffer[i];
                if (col == null) continue;

                BattleOfAges.Crafting.WorkbenchNode wb = col.GetComponentInParent<BattleOfAges.Crafting.WorkbenchNode>();
                if (wb != null)
                {
                    activeWorkbenchTarget = wb;
                    return;
                }

                StorageChestNode chest = col.GetComponentInParent<StorageChestNode>();
                if (chest != null)
                {
                    activeChestTarget = chest;
                    return;
                }

                OrePebbleNode pebble = col.GetComponentInParent<OrePebbleNode>();
                if (pebble != null && !pebble.IsPickedUp)
                {
                    float dst = Vector3.Distance(transform.position, pebble.transform.position);
                    if (dst < minDst)
                    {
                        minDst = dst;
                        activePebbleTarget = pebble;
                    }
                }

                ResourceNodeTree tree = col.GetComponentInParent<ResourceNodeTree>();
                if (tree != null && tree.CurrentState != TreeState.Depleted && activePebbleTarget == null)
                {
                    float dst = Vector3.Distance(transform.position, tree.transform.position);
                    if (dst < minDst)
                    {
                        minDst = dst;
                        activeTreeTarget = tree;
                    }
                }
            }
        }

        private void UpdateWorkbenchPromptUI()
        {
            if (resourceUI == null || activeWorkbenchTarget == null) return;
            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(BattleOfAges.InputSystem.KeyAction.InteractGather));
            resourceUI.ShowTreePrompt($"⚒️ [Натисніть {keyEStr}] Відкрити Верстак (Крафт зброї/броні)");
        }

        private void HandleWorkbenchInput()
        {
            if (activeWorkbenchTarget == null) return;

            if (IsKeyEDown() || IsKeyFDown())
            {
                WorkbenchUI ui = WorkbenchUI.EnsureInstance();
                if (ui != null)
                {
                    if (ui.IsOpen) ui.CloseUI();
                    else ui.OpenUI(activeWorkbenchTarget);
                }
            }
        }

        private void UpdateChestPromptUI()
        {
            if (resourceUI == null || activeChestTarget == null) return;
            resourceUI.ShowTreePrompt("[E] Відкрити скриню-склад");
        }

        private void HandleChestInput()
        {
            if (activeChestTarget == null) return;

            if (IsKeyEDown() || IsKeyFDown())
            {
                if (InventoryUI.Instance != null)
                {
                    InventoryUI.Instance.ToggleInventory();
                }
            }
        }

        private void UpdateTreePromptUI()
        {
            if (resourceUI == null || activeTreeTarget == null) return;

            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(BattleOfAges.InputSystem.KeyAction.InteractGather));
            string keyFStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(BattleOfAges.InputSystem.KeyAction.ChopTree));

            if (activeTreeTarget.CurrentState == TreeState.WholeTree)
            {
                int branchPercent = Mathf.RoundToInt(activeTreeTarget.BranchProgress * 100f);
                int fellPercent = Mathf.RoundToInt(activeTreeTarget.FellingProgress * 100f);

                string branchStr = (branchPercent > 0) ? $"[Утримуйте {keyEStr}] Збирання гілок ({branchPercent}%)" : $"[Утримуйте {keyEStr}] Збирати гілки (+1 Дерево)";
                string chopStr = (fellPercent > 0) ? $"[Утримуйте {keyFStr}] Рубати стовбур ({fellPercent}%)" : $"[Утримуйте {keyFStr}] Зрубати дерево (+4 Дерева)";

                if (EquippedAxeLevel > 0)
                {
                    chopStr += $" [Сокира {BattleOfAges.Crafting.ItemTypeNames.Roman(EquippedAxeLevel)}]";
                }

                resourceUI.ShowTreePrompt($"{branchStr}\n{chopStr}");
            }
            else if (activeTreeTarget.CurrentState == TreeState.WoodPile)
            {
                string axeStr = (EquippedAxeLevel > 0) ? $" [Сокира {BattleOfAges.Crafting.ItemTypeNames.Roman(EquippedAxeLevel)}]" : "";
                resourceUI.ShowTreePrompt($"🪓 [Утримуйте {keyFStr}] Рубати колоди ({activeTreeTarget.WoodRemaining} / {activeTreeTarget.MaxWoodCapacity} Деревини){axeStr}");
            }
        }

        private void UpdatePebblePromptUI()
        {
            if (resourceUI == null || activePebbleTarget == null) return;
            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(BattleOfAges.InputSystem.KeyAction.InteractGather));
            resourceUI.ShowTreePrompt($"🪨 [Натисніть {keyEStr}] Підібрати руду");
        }

        private void HandleTreeHarvestInput()
        {
            if (activeTreeTarget == null) return;

            if (IsKeyEHeld())
            {
                if (activeTreeTarget.GatherBranchesProgress(Time.deltaTime))
                {
                    if (ResourceManager.Instance != null) ResourceManager.Instance.AddResource(ResourceType.Wood, 1);
                }
            }
            else
            {
                // Reset branch gathering progress if key E is not held
                activeTreeTarget.CancelBranchGathering();
            }

            if (IsKeyFHeld())
            {
                float axeEff = (EquippedAxeLevel > 0) ? BattleOfAges.Crafting.ItemStatCalculator.CalculateToolEfficiency(EquippedAxeLevel) : 1.0f;
                TreeState priorState = activeTreeTarget.CurrentState;
                if (activeTreeTarget.ChopWoodProgress(Time.deltaTime * axeEff))
                {
                    int woodAmount = (priorState == TreeState.WholeTree) ? 4 : 2;
                    if (ResourceManager.Instance != null) ResourceManager.Instance.AddResource(ResourceType.Wood, woodAmount);
                }
            }
        }

        private void HandlePebblePickupInput()
        {
            if (activePebbleTarget == null) return;

            if (IsKeyEDown() || IsKeyFDown())
            {
                int rolledTier = activePebbleTarget.PickupPebble();
                activePebbleTarget = null;
                int yieldAmount = (EquippedPickaxeLevel >= 3) ? 2 : 1;
                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.AddOre(rolledTier, yieldAmount);
                }
                if (resourceUI != null)
                {
                    string bonusStr = (yieldAmount > 1) ? " (+2 Ore Bonus!)" : " (+1 Ore)";
                    resourceUI.ShowTreePrompt($"✨ Picked up {OreTierHelper.GetTierName(rolledTier)}!{bonusStr}");
                }
            }
        }

        private bool IsKeyEHeld() => KeybindsManager.GetKeyHeld(BattleOfAges.InputSystem.KeyAction.InteractGather);
        private bool IsKeyEDown() => KeybindsManager.GetKeyDown(BattleOfAges.InputSystem.KeyAction.InteractGather);
        private bool IsKeyFHeld() => KeybindsManager.GetKeyHeld(BattleOfAges.InputSystem.KeyAction.ChopTree);
        private bool IsKeyFDown() => KeybindsManager.GetKeyDown(BattleOfAges.InputSystem.KeyAction.ChopTree);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, proximityRadius);
        }
    }
}
