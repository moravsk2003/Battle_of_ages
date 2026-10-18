using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.UI;
using BattleOfAges.InputSystem;
using BattleOfAges.Crafting;
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

        private Equipment gear;

        // Tools work while they are in the weapon slot
        public int EquippedAxeLevel => Gear != null ? Gear.LevelOf(ItemType.Axe) : 0;
        public int EquippedPickaxeLevel => Gear != null ? Gear.LevelOf(ItemType.Pickaxe) : 0;

        private Equipment Gear
        {
            get
            {
                if (gear == null) gear = GetComponent<Equipment>();
                return gear;
            }
        }

        private ResourceUI resourceUI;
        private Camera playerCamera;
        private ResourceNodeTree activeTreeTarget;
        private ResourceNodeTree previousTreeTarget;
        private OrePebbleNode activePebbleTarget;
        private StorageChestNode activeChestTarget;
        private WorkbenchNode activeWorkbenchTarget;

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

            if (GameWindows.BlockGameplay)
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

            // 1. What the camera aims at (a fat ray), nearest pebble and nearest tree separately
            if (playerCamera != null && playerCamera.enabled)
            {
                Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
                int hitCount = Physics.SphereCastNonAlloc(ray, 0.4f, sphereHitsBuffer, harvestRange);
                float nearestPebble = harvestRange, nearestTree = harvestRange;
                for (int i = 0; i < hitCount; i++)
                {
                    RaycastHit hit = sphereHitsBuffer[i];
                    if (hit.collider != null && Consider(hit.collider, hit.distance, ref nearestPebble, ref nearestTree, false)) return;
                }
                if (activePebbleTarget != null || activeTreeTarget != null) return;
            }

            // 2. Fallback: whatever is around the player's feet; a pebble wins over a tree
            int overlapCount = Physics.OverlapSphereNonAlloc(transform.position, proximityRadius, overlapHitsBuffer);
            float nearest = proximityRadius;
            for (int i = 0; i < overlapCount; i++)
            {
                Collider col = overlapHitsBuffer[i];
                if (col != null && Consider(col, -1f, ref nearest, ref nearest, true)) return;
            }
        }

        /// <summary>
        /// Takes col's workbench or chest at once (returns true), or keeps its pebble / tree if nearer than the best so far.
        /// distance &lt; 0: measure from the player to the object. pebblesFirst: no tree once a pebble was found.
        /// </summary>
        private bool Consider(Collider col, float distance, ref float bestPebble, ref float bestTree, bool pebblesFirst)
        {
            WorkbenchNode wb = col.GetComponentInParent<WorkbenchNode>();
            if (wb != null) { activeWorkbenchTarget = wb; return true; }

            StorageChestNode chest = col.GetComponentInParent<StorageChestNode>();
            if (chest != null) { activeChestTarget = chest; return true; }

            OrePebbleNode pebble = col.GetComponentInParent<OrePebbleNode>();
            if (pebble != null && !pebble.IsPickedUp)
            {
                float d = distance >= 0f ? distance : Vector3.Distance(transform.position, pebble.transform.position);
                if (d < bestPebble) { bestPebble = d; activePebbleTarget = pebble; }
            }

            ResourceNodeTree tree = col.GetComponentInParent<ResourceNodeTree>();
            if (tree != null && tree.CurrentState != TreeState.Depleted && !(pebblesFirst && activePebbleTarget != null))
            {
                float d = distance >= 0f ? distance : Vector3.Distance(transform.position, tree.transform.position);
                if (d < bestTree) { bestTree = d; activeTreeTarget = tree; }
            }
            return false;
        }

        private void UpdateWorkbenchPromptUI()
        {
            if (resourceUI == null || activeWorkbenchTarget == null) return;
            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.InteractGather));
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

            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.InteractGather));
            string keyFStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.ChopTree));

            if (activeTreeTarget.CurrentState == TreeState.WholeTree)
            {
                int branchPercent = Mathf.RoundToInt(activeTreeTarget.BranchProgress * 100f);
                int fellPercent = Mathf.RoundToInt(activeTreeTarget.FellingProgress * 100f);

                string branchStr = (branchPercent > 0) ? $"[Утримуйте {keyEStr}] Збирання гілок ({branchPercent}%)" : $"[Утримуйте {keyEStr}] Збирати гілки (+1 Дерево)";
                string chopStr = (fellPercent > 0) ? $"[Утримуйте {keyFStr}] Рубати стовбур ({fellPercent}%)" : $"[Утримуйте {keyFStr}] Зрубати дерево (+4 Дерева)";

                if (EquippedAxeLevel > 0)
                {
                    chopStr += $" [Сокира {ItemTypeNames.Roman(EquippedAxeLevel)}]";
                }

                resourceUI.ShowTreePrompt($"{branchStr}\n{chopStr}");
            }
            else if (activeTreeTarget.CurrentState == TreeState.WoodPile)
            {
                string axeStr = (EquippedAxeLevel > 0) ? $" [Сокира {ItemTypeNames.Roman(EquippedAxeLevel)}]" : "";
                resourceUI.ShowTreePrompt($"🪓 [Утримуйте {keyFStr}] Рубати колоди ({activeTreeTarget.WoodRemaining} / {activeTreeTarget.MaxWoodCapacity} Деревини){axeStr}");
            }
        }

        private void UpdatePebblePromptUI()
        {
            if (resourceUI == null || activePebbleTarget == null) return;
            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.InteractGather));
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
                float axeEff = (EquippedAxeLevel > 0) ? ItemStatCalculator.CalculateToolEfficiency(EquippedAxeLevel) : 1.0f;
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

        private bool IsKeyEHeld() => KeybindsManager.GetKeyHeld(KeyAction.InteractGather);
        private bool IsKeyEDown() => KeybindsManager.GetKeyDown(KeyAction.InteractGather);
        private bool IsKeyFHeld() => KeybindsManager.GetKeyHeld(KeyAction.ChopTree);
        private bool IsKeyFDown() => KeybindsManager.GetKeyDown(KeyAction.ChopTree);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, proximityRadius);
        }
    }
}
