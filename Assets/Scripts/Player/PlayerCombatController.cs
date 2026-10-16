using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.UI;

namespace BattleOfAges.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HealthComponent))]
    [RequireComponent(typeof(CombatUnit))]
    public class PlayerCombatController : MonoBehaviour
    {
        [Header("Attack Settings")]
        [SerializeField] private float attackRange = 3.5f;

        private HealthComponent healthComponent;
        private CombatUnit combatUnit;
        private Camera fpsCamera;
        private FpsViewmodelAnimation viewmodelAnimation;

        // Unarmed player (BaseDamage is the hero's strength, 50): the worn gear (Equipment) scales these
        public const int BaseHealth = 500, BaseDamage = 50;
        public const float BaseCooldown = 1.8f;

        private BattleOfAges.Crafting.Equipment gear;
        private bool gearReady;

        public BattleOfAges.Crafting.Equipment Gear
        {
            get
            {
                if (gear == null) gear = GetComponent<BattleOfAges.Crafting.Equipment>();
                if (gear == null) gear = gameObject.AddComponent<BattleOfAges.Crafting.Equipment>();
                if (!gearReady)
                {
                    gearReady = true;
                    if (healthComponent == null) healthComponent = GetComponent<HealthComponent>();
                    if (healthComponent != null) healthComponent.InitializeHealth(BaseHealth);
                    gear.SetBaseStats(BaseHealth, BaseDamage, BaseCooldown, attackRange);
                }
                return gear;
            }
        }

        public int EquippedSwordLevel => Gear.LevelOf(BattleOfAges.Crafting.ItemType.Sword);
        public int EquippedArmorLevel => Gear.LevelOf(BattleOfAges.Crafting.ItemType.Armor);
        public int EquippedBowLevel => Gear.LevelOf(BattleOfAges.Crafting.ItemType.Bow);

        private void Awake()
        {
            healthComponent = GetComponent<HealthComponent>();
            combatUnit = GetComponent<CombatUnit>();
            if (combatUnit != null) combatUnit.SpeedModifier = CharacterSpeed.HeroModifier;

            // Starting kit: a plain level-0 sword and a shield
            BattleOfAges.Crafting.Equipment g = Gear;
            if (g[BattleOfAges.Crafting.EquipSlot.Weapon].IsEmpty) g.Equip(new BattleOfAges.Crafting.Item(BattleOfAges.Crafting.ItemType.Sword, 0));
            if (g[BattleOfAges.Crafting.EquipSlot.Shield].IsEmpty) g.Equip(new BattleOfAges.Crafting.Item(BattleOfAges.Crafting.ItemType.Shield, 1));
        }

        /// <summary>Wears the item (no store involved); returns what was worn in that slot.</summary>
        public BattleOfAges.Crafting.Item EquipItem(BattleOfAges.Crafting.ItemType type, int level)
        {
            if (combatUnit == null) combatUnit = GetComponent<CombatUnit>();
            return Gear.Equip(new BattleOfAges.Crafting.Item(type, level));
        }

        private void Start()
        {
            EnsureCameraReference();

            // The first-person fists hold the worn weapon and shield
            FpsViewmodelSetup viewmodel = FindFirstObjectByType<FpsViewmodelSetup>();
            if (viewmodel != null && viewmodel.ViewmodelRoot != null)
            {
                FpsViewmodelGear held = viewmodel.ViewmodelRoot.GetComponent<FpsViewmodelGear>();
                if (held == null) held = viewmodel.ViewmodelRoot.gameObject.AddComponent<FpsViewmodelGear>();
                held.Bind(Gear);
            }
        }

        private void EnsureCameraReference()
        {
            if (fpsCamera == null)
            {
                fpsCamera = GetComponentInChildren<Camera>();
                if (fpsCamera == null && Camera.main != null) fpsCamera = Camera.main;
            }

            if (viewmodelAnimation == null)
            {
                viewmodelAnimation = GetComponentInChildren<FpsViewmodelAnimation>();
            }
        }

        private GameModeManager cachedModeManager;

        private void Update()
        {
            if (cachedModeManager == null) cachedModeManager = FindFirstObjectByType<GameModeManager>(); // was a full scene search every frame
            GameModeManager gmm = cachedModeManager;
            if (gmm != null && gmm.CurrentMode != GameMode.FirstPerson) return;
            if (IsAnyUIOpen()) return;

            // Attack on Left Mouse Button (LMB)
            if (UniversalInput.GetMouseButtonDown(0))
            {
                PerformPlayerAttack();
            }
        }

        private bool IsAnyUIOpen()
        {
            return (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) ||
                   (WorkbenchUI.Instance != null && WorkbenchUI.Instance.IsOpen) || EquipmentUI.IsVisible ||
                   (NpcGatherCommandUI.Instance != null && NpcGatherCommandUI.Instance.IsOpen) ||
                   (BuildMenuUI.Instance != null && BuildMenuUI.Instance.IsOpen) ||
                   (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused());
        }

        private void PerformPlayerAttack()
        {
            if (combatUnit == null || !combatUnit.CanAttack) return;

            EnsureCameraReference();

            // Trigger 1st person punch animation
            if (viewmodelAnimation != null)
            {
                viewmodelAnimation.PlayPunchAnimation();
            }

            // Raycast forward from FPS camera
            Ray ray = (fpsCamera != null)
                ? new Ray(fpsCamera.transform.position, fpsCamera.transform.forward)
                : new Ray(transform.position + Vector3.up * 1.5f, transform.forward);

            // The bow reaches further than the fist/sword: use the combat unit's current range.
            float reach = Mathf.Max(attackRange, combatUnit.AttackRange);
            if (Physics.SphereCast(ray, 0.4f, out RaycastHit hit, reach))
            {
                HealthComponent targetHp = hit.collider.GetComponentInParent<HealthComponent>();
                if (targetHp != null && targetHp != healthComponent && targetHp.IsAlive)
                {
                    combatUnit.TryAttack(targetHp);
                    Debug.Log($"[PlayerCombat] Гравець атакував {hit.collider.gameObject.name} на {combatUnit.AttackDamage} урону!");
                    return;
                }

                BattleOfAges.Resources.ResourceNodeTree tree = hit.collider.GetComponentInParent<BattleOfAges.Resources.ResourceNodeTree>();
                if (tree != null && tree.CurrentState != BattleOfAges.Resources.TreeState.Depleted)
                {
                    combatUnit.TryAttack(null);
                    PlayerResourceHarvester harvester = GetComponent<PlayerResourceHarvester>();
                    float eff = (harvester != null && harvester.EquippedAxeLevel > 0)
                        ? BattleOfAges.Crafting.ItemStatCalculator.CalculateToolEfficiency(harvester.EquippedAxeLevel)
                        : 1.0f;

                    BattleOfAges.Resources.TreeState priorState = tree.CurrentState;
                    if (tree.ChopWoodProgress(0.95f * eff))
                    {
                        int woodAmount = (priorState == BattleOfAges.Resources.TreeState.WholeTree) ? 4 : 2;
                        if (Resources.ResourceManager.Instance != null)
                        {
                            Resources.ResourceManager.Instance.AddResource(Resources.ResourceType.Wood, woodAmount);
                        }
                    }
                    return;
                }
            }

            // Trigger cooldown and punch even if swing misses
            combatUnit.TryAttack(null);
        }
    }
}
