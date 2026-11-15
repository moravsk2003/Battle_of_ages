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
        private float workSoundTimer;
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
        private float searchTimer;
        private Vector3 lastSearchPos = new Vector3(1e9f, 1e9f, 1e9f);
        private Quaternion lastSearchRot = Quaternion.identity;
        private bool promptHidden;
        private string lastPrompt;
        private long lastTreePromptKey = long.MinValue;

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

        private void ShowPromptCached(string text)
        {
            if (resourceUI == null) return;
            if (!promptHidden && lastPrompt == text) return;
            promptHidden = false;
            lastPrompt = text;
            resourceUI.ShowTreePrompt(text);
        }

        private void HidePromptCached()
        {
            if (resourceUI == null || promptHidden) return;
            promptHidden = true;
            lastPrompt = null;
            lastTreePromptKey = long.MinValue;
            resourceUI.HideTreePrompt();
        }

        private void Update()
        {
            EnsureCameraReference();

            if (GameWindows.BlockGameplay)
            {
                HidePromptCached();
                return;
            }

            searchTimer -= Time.unscaledDeltaTime;
            Transform camTr = playerCamera != null ? playerCamera.transform : transform;
            Vector3 curPos = camTr.position;
            Quaternion curRot = camTr.rotation;
            bool interacting = IsKeyEDown() || IsKeyFDown() || IsKeyEHeld() || IsKeyFHeld();
            bool targetInvalid = (activeTreeTarget != null && (!activeTreeTarget.isActiveAndEnabled || activeTreeTarget.CurrentState == TreeState.Depleted)) ||
                                 (activePebbleTarget != null && (!activePebbleTarget.isActiveAndEnabled || activePebbleTarget.IsPickedUp));

            if (interacting || targetInvalid || searchTimer <= 0f ||
                (curPos - lastSearchPos).sqrMagnitude > 0.0025f ||
                Quaternion.Angle(curRot, lastSearchRot) > 0.25f)
            {
                searchTimer = 0.08f;
                lastSearchPos = curPos;
                lastSearchRot = curRot;
                FindBestInteractionTarget();
            }

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
                HidePromptCached();
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
            if (col is TerrainCollider || col is MeshCollider || col is CharacterController) return false;

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
            ShowPromptCached(UI.Loc.Pick($"[Натисніть {keyEStr}] Відкрити верстак", $"[Press {keyEStr}] Open workbench"));
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
            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.InteractGather));
            ShowPromptCached(UI.Loc.Pick($"[Натисніть {keyEStr}] Відкрити скриню-склад", $"[Press {keyEStr}] Open storage chest"));
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

            int branchPercent = activeTreeTarget.CurrentState == TreeState.WholeTree ? Mathf.RoundToInt(activeTreeTarget.BranchProgress * 100f) : 0;
            int fellPercent = activeTreeTarget.CurrentState == TreeState.WholeTree ? Mathf.RoundToInt(activeTreeTarget.FellingProgress * 100f) : 0;
            int axeLvl = EquippedAxeLevel;
            KeyCode kE = KeybindsManager.GetKey(KeyAction.InteractGather);
            KeyCode kF = KeybindsManager.GetKey(KeyAction.ChopTree);
            long key = (((long)activeTreeTarget.CurrentState << 48) ^
                       ((long)branchPercent << 36) ^
                       ((long)fellPercent << 24) ^
                       ((long)activeTreeTarget.WoodRemaining << 12) ^
                       ((long)axeLvl << 8) ^
                       ((long)kE << 4) ^ (long)kF) ^
                       (UI.Loc.En ? (1L << 62) : 0L);
            if (!promptHidden && key == lastTreePromptKey) return;
            lastTreePromptKey = key;

            string keyEStr = KeybindsManager.GetKeyDisplayString(kE);
            string keyFStr = KeybindsManager.GetKeyDisplayString(kF);

            if (activeTreeTarget.CurrentState == TreeState.WholeTree)
            {
                string branchStr = (branchPercent > 0)
                    ? UI.Loc.Pick($"[Утримуйте {keyEStr}] Збирання гілок ({branchPercent}%)", $"[Hold {keyEStr}] Gathering branches ({branchPercent}%)")
                    : UI.Loc.Pick($"[Утримуйте {keyEStr}] Збирати гілки (+1 деревина)", $"[Hold {keyEStr}] Gather branches (+1 wood)");
                string chopStr = (fellPercent > 0)
                    ? UI.Loc.Pick($"[Утримуйте {keyFStr}] Рубати стовбур ({fellPercent}%)", $"[Hold {keyFStr}] Chopping trunk ({fellPercent}%)")
                    : UI.Loc.Pick($"[Утримуйте {keyFStr}] Зрубати дерево (+4 деревини)", $"[Hold {keyFStr}] Fell tree (+4 wood)");

                if (axeLvl > 0)
                {
                    chopStr += UI.Loc.Pick($" [Сокира {ItemTypeNames.Roman(axeLvl)}]", $" [Axe {ItemTypeNames.Roman(axeLvl)}]");
                }

                ShowPromptCached($"{branchStr}\n{chopStr}");
            }
            else if (activeTreeTarget.CurrentState == TreeState.WoodPile)
            {
                string axeStr = (axeLvl > 0) ? UI.Loc.Pick($" [Сокира {ItemTypeNames.Roman(axeLvl)}]", $" [Axe {ItemTypeNames.Roman(axeLvl)}]") : "";
                ShowPromptCached(UI.Loc.Pick(
                    $"[Утримуйте {keyFStr}] Рубати колоди ({activeTreeTarget.WoodRemaining} / {activeTreeTarget.MaxWoodCapacity} деревини){axeStr}",
                    $"[Hold {keyFStr}] Chop logs ({activeTreeTarget.WoodRemaining} / {activeTreeTarget.MaxWoodCapacity} wood){axeStr}"));
            }
        }

        private void UpdatePebblePromptUI()
        {
            if (resourceUI == null || activePebbleTarget == null) return;
            string keyEStr = KeybindsManager.GetKeyDisplayString(KeybindsManager.GetKey(KeyAction.InteractGather));
            ShowPromptCached(UI.Loc.Pick($"[Натисніть {keyEStr}] Підібрати руду", $"[Press {keyEStr}] Pick up ore"));
        }

        private void HandleTreeHarvestInput()
        {
            if (activeTreeTarget == null) return;

            workSoundTimer -= Time.deltaTime;
            if (workSoundTimer <= 0f && (IsKeyEHeld() || IsKeyFHeld()))
            {
                workSoundTimer = IsKeyFHeld() ? 0.6f : 0.8f;
                GameSound.At(IsKeyFHeld() ? GameSound.Kind.Chop : GameSound.Kind.Branches, activeTreeTarget.transform.position);
            }

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
                Vector3 pebblePos = activePebbleTarget.transform.position;
                int rolledTier = activePebbleTarget.PickupPebble();
                activePebbleTarget = null;
                GameSound.At(GameSound.Kind.OrePickup, pebblePos);
                int yieldAmount = (EquippedPickaxeLevel >= 3) ? 2 : 1;
                if (ResourceManager.Instance != null)
                {
                    ResourceManager.Instance.AddOre(rolledTier, yieldAmount);
                }
                if (resourceUI != null)
                {
                    string bonusStr = (yieldAmount > 1)
                        ? UI.Loc.Pick(" (+2 руди — бонус!)", " (+2 Ore Bonus!)")
                        : UI.Loc.Pick(" (+1 руда)", " (+1 Ore)");
                    resourceUI.ShowTreePrompt(UI.Loc.Pick(
                        $"Підібрано: {OreTierHelper.GetTierName(rolledTier)}{bonusStr}",
                        $"Picked up: {OreTierHelper.GetTierName(rolledTier)}{bonusStr}"));
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
