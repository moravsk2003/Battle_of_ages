using UnityEngine;
using BattleOfAges.Resources;
using BattleOfAges.UI;
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

        private ResourceUI resourceUI;
        private Camera playerCamera;
        private ResourceNodeTree activeTreeTarget;
        private ResourceNodeTree previousTreeTarget;
        private OrePebbleNode activePebbleTarget;
        private StorageChestNode activeChestTarget;

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
            FindBestInteractionTarget();

            // Cancel branch gathering on previous tree if player moved away or changed target
            if (previousTreeTarget != null && previousTreeTarget != activeTreeTarget)
            {
                previousTreeTarget.CancelBranchGathering();
            }
            previousTreeTarget = activeTreeTarget;

            if (activeChestTarget != null)
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

        private void FindBestInteractionTarget()
        {
            activeTreeTarget = null;
            activePebbleTarget = null;
            activeChestTarget = null;

            // 1. Raycast / SphereCast Forward from Camera for precise aiming
            if (playerCamera != null && playerCamera.enabled)
            {
                Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
                RaycastHit[] sphereHits = Physics.SphereCastAll(ray, 0.4f, harvestRange);
                
                float nearestPebbleDst = harvestRange;
                float nearestTreeDst = harvestRange;

                foreach (var hit in sphereHits)
                {
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
            Collider[] hits = Physics.OverlapSphere(transform.position, proximityRadius);
            float minDst = proximityRadius;

            foreach (var col in hits)
            {
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

        private void UpdateChestPromptUI()
        {
            if (resourceUI == null || activeChestTarget == null) return;
            resourceUI.ShowTreePrompt("📦 [Press E or F] Open Storage Chest (Склад Ресурсів)");
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

            if (activeTreeTarget.CurrentState == TreeState.WholeTree)
            {
                float progressPercent = Mathf.RoundToInt(activeTreeTarget.BranchProgress * 100f);
                string branchStr = (progressPercent > 0) ? $"[Hold E] Gathering Branches ({progressPercent}%)" : "[Hold E] Gather Branches (1 Wood / 5s)";
                resourceUI.ShowTreePrompt($"{branchStr}\n[Hold F] Chop Down Tree (2 Wood / 1s)");
            }
            else if (activeTreeTarget.CurrentState == TreeState.WoodPile)
            {
                resourceUI.ShowTreePrompt($"[Hold F] Chop Wood Pile ({activeTreeTarget.WoodRemaining} / {activeTreeTarget.MaxWoodCapacity} Wood Remaining)");
            }
        }

        private void UpdatePebblePromptUI()
        {
            if (resourceUI == null || activePebbleTarget == null) return;
            resourceUI.ShowTreePrompt("🪨 [Press E or F] Pick Up Ore Pebble");
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
                if (activeTreeTarget.ChopWoodProgress(Time.deltaTime))
                {
                    if (ResourceManager.Instance != null) ResourceManager.Instance.AddResource(ResourceType.Wood, 1);
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
                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.AddOre(rolledTier, 1);
                }
                if (resourceUI != null)
                {
                    resourceUI.ShowTreePrompt($"✨ Picked up {OreTierHelper.GetTierName(rolledTier)}! (+1 Ore)");
                }
            }
        }

        private bool IsKeyEHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.eKey.isPressed) return true;
#endif
            try { return Input.GetKey(KeyCode.E); } catch { return false; }
        }

        private bool IsKeyFHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.fKey.isPressed) return true;
#endif
            try { return Input.GetKey(KeyCode.F); } catch { return false; }
        }

        private bool IsKeyEDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
#endif
            try { return Input.GetKeyDown(KeyCode.E); } catch { return false; }
        }

        private bool IsKeyFDown()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) return true;
#endif
            try { return Input.GetKeyDown(KeyCode.F); } catch { return false; }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, proximityRadius);
        }
    }
}
