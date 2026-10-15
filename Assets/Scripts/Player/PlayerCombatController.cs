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

        public int EquippedSwordLevel { get; private set; } = 0;
        public int EquippedArmorLevel { get; private set; } = 0;
        public int EquippedBowLevel { get; private set; } = 0;

        private void Awake()
        {
            healthComponent = GetComponent<HealthComponent>();
            combatUnit = GetComponent<CombatUnit>();

            // Configure Player stats: 500 HP, 50 Damage, 2.0s cooldown
            if (healthComponent != null) healthComponent.InitializeHealth(500);
            if (combatUnit != null) combatUnit.InitializeCombatStats(50, 1.8f, attackRange);
        }

        public void EquipItem(BattleOfAges.Crafting.ItemType type, int level)
        {
            if (combatUnit == null) combatUnit = GetComponent<CombatUnit>();
            if (healthComponent == null) healthComponent = GetComponent<HealthComponent>();

            if (type == BattleOfAges.Crafting.ItemType.Sword)
            {
                EquippedSwordLevel = level;
                EquippedBowLevel = 0;
                int dmg = 50 + Mathf.RoundToInt(BattleOfAges.Crafting.ItemStatCalculator.CalculateSwordDamage(level));
                combatUnit.InitializeCombatStats(dmg, 1.4f, attackRange);
                Debug.Log($"[PlayerCombat] ⚔️ Гравець екіпірував Меч (Рівень {level})! Новий урон: {dmg}");
            }
            else if (type == BattleOfAges.Crafting.ItemType.Armor)
            {
                EquippedArmorLevel = level;
                int bonusHp = Mathf.RoundToInt(BattleOfAges.Crafting.ItemStatCalculator.CalculateArmorDefense(level) * 15f);
                int totalHp = 500 + bonusHp;
                healthComponent.ArmorLevel = level;
                healthComponent.SetMaxHealthKeepDamage(totalHp);
                Debug.Log($"[PlayerCombat] 🛡️ Гравець екіпірував Броню (Рівень {level})! Здоров'я: {totalHp} HP, поглинання {level * 10}%");
            }
            else if (type == BattleOfAges.Crafting.ItemType.Bow)
            {
                EquippedBowLevel = level;
                EquippedSwordLevel = 0;
                int dmg = 40 + level * 15;
                combatUnit.InitializeCombatStats(dmg, 1.2f, 12.0f);
                Debug.Log($"[PlayerCombat] 🏹 Гравець екіпірував Лук (Рівень {level})! Дальність: 12м, Урон: {dmg}");
            }
            else if (type == BattleOfAges.Crafting.ItemType.Axe || type == BattleOfAges.Crafting.ItemType.Pickaxe)
            {
                PlayerResourceHarvester harvester = GetComponent<PlayerResourceHarvester>();
                if (harvester != null)
                {
                    harvester.EquipHarvestTool(type, level);
                }
            }
        }

        private void Start()
        {
            EnsureCameraReference();
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
