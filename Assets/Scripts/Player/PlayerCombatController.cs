using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;
using BattleOfAges.UI;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;

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

        // Unarmed player: the hero is a general (docs/GAME_DESIGN.md §7.1), x2 its base; the worn gear (Equipment) scales these
        public static int BaseHealth => UnitRanks.Health(UnitRank.General, hero: true);
        public static int BaseDamage => UnitRanks.Strength(UnitRank.General, hero: true);
        public static float BaseCooldown => GameBalance.BaseAttackCooldown;

        private Equipment gear;
        private bool gearReady;

        public Equipment Gear
        {
            get
            {
                if (gear == null) gear = GetComponent<Equipment>();
                if (gear == null) gear = gameObject.AddComponent<Equipment>();
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

        public int EquippedSwordLevel => Gear.LevelOf(ItemType.Sword);
        public int EquippedArmorLevel => Gear.LevelOf(ItemType.Armor);
        public int EquippedBowLevel => Gear.LevelOf(ItemType.Bow);

        private void Awake()
        {
            if (gameObject.name == "Player") Managers.GameContext.RegisterPlayer(gameObject);
            healthComponent = GetComponent<HealthComponent>();
            combatUnit = GetComponent<CombatUnit>();
            if (combatUnit != null) combatUnit.SpeedModifier = CharacterSpeed.HeroModifier;

            // Starting kit: a plain level-0 sword and a shield
            Equipment g = Gear;
            if (g[EquipSlot.Weapon].IsEmpty) g.Equip(new Item(ItemType.Sword, 0));
            if (g[EquipSlot.Shield].IsEmpty) g.Equip(new Item(ItemType.Shield, 1));

            // The hero is a general with x2 stats; its rank never changes (docs/GAME_DESIGN.md §7.1)
            UnitProgress.Setup(gameObject, UnitRank.General, hero: true);
            if (GetComponent<HeroProgress>() == null) gameObject.AddComponent<HeroProgress>();
            if (GetComponent<HeroSkills>() == null) gameObject.AddComponent<HeroSkills>();
            if (GetComponent<HeroSuccession>() == null) gameObject.AddComponent<HeroSuccession>();
            if (GetComponent<HeroArtifacts>() == null) gameObject.AddComponent<HeroArtifacts>();
        }

        /// <summary>Wears the item (no store involved); returns what was worn in that slot.</summary>
        public Item EquipItem(ItemType type, int level)
        {
            if (combatUnit == null) combatUnit = GetComponent<CombatUnit>();
            return Gear.Equip(new Item(type, level));
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
            if (cachedModeManager == null) cachedModeManager = Managers.GameContext.Modes; // was a full scene search every frame
            GameModeManager gmm = cachedModeManager;
            if (gmm != null && gmm.CurrentMode != GameMode.FirstPerson) return;
            if (GameWindows.BlockGameplay) return;
            if (healthComponent != null && !healthComponent.IsAlive) return; // fallen: the interregnum (HeroSuccession)

            // Attack on Left Mouse Button (LMB)
            if (UniversalInput.GetMouseButtonDown(0))
            {
                PerformPlayerAttack();
            }

            // The class's active skills: the first on Q, the second on R (fire fan / meteor, dash / critical hit, heal)
            if (KeybindsManager.GetKeyDown(KeyAction.Ability)) UseSkill(0);
            if (KeybindsManager.GetKeyDown(KeyAction.Ability2)) UseSkill(1);
            // The one-shot artifacts on their own keys (1, 2)
            if (KeybindsManager.GetKeyDown(KeyAction.UseFullHeal)) UseArtifact(ArtifactType.FullHeal);
            if (KeybindsManager.GetKeyDown(KeyAction.UseLightning)) UseArtifact(ArtifactType.Lightning);
        }

        private void UseArtifact(ArtifactType a)
        {
            HeroArtifacts bag = GetComponent<HeroArtifacts>();
            if (bag == null || !bag.Has(a)) return;
            if (!bag.Use(a)) StrategyHud.Notify(a == ArtifactType.Lightning ? "Блискавці нема в кого влучити" : "Не вдалося");
        }

        private void UseSkill(int slot)
        {
            EnsureCameraReference();
            HeroSkills skills = GetComponent<HeroSkills>();
            if (skills == null || !skills.HasActiveIn(slot)) return;
            if (!skills.UseActive(slot, fpsCamera != null ? fpsCamera.transform.forward : transform.forward) && skills.CooldownLeftIn(slot) > 0f)
                StrategyHud.Notify($"Уміння перезаряджається: {skills.CooldownLeftIn(slot):0} с");
        }

        private void PerformPlayerAttack()
        {
            if (combatUnit == null || !combatUnit.CanAttack) return;

            EnsureCameraReference();

            // First-person: a swing, or with a bow a shot drawn over the wind-up
            if (viewmodelAnimation != null)
            {
                if (!Gear[EquipSlot.Weapon].IsEmpty && Gear[EquipSlot.Weapon].Type == ItemType.Bow) viewmodelAnimation.PlayShotAnimation(combatUnit.Windup);
                else { viewmodelAnimation.PlayPunchAnimation(); GameSound.Play2D(GameSound.Kind.Swing); }
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
                    if (combatUnit.NeedsStaff) { StrategyHud.Notify("Маг атакує лише з посохом (зробіть на верстаті)"); return; }
                    combatUnit.TryAttack(targetHp);
                    return;
                }

                ResourceNodeTree tree = hit.collider.GetComponentInParent<ResourceNodeTree>();
                if (tree != null && tree.CurrentState != TreeState.Depleted)
                {
                    combatUnit.TryAttack(null);
                    PlayerResourceHarvester harvester = GetComponent<PlayerResourceHarvester>();
                    float eff = (harvester != null && harvester.EquippedAxeLevel > 0)
                        ? ItemStatCalculator.CalculateToolEfficiency(harvester.EquippedAxeLevel)
                        : 1.0f;

                    TreeState priorState = tree.CurrentState;
                    GameSound.At(GameSound.Kind.Chop, hit.point);
                    if (tree.ChopWoodProgress(0.95f * eff))
                    {
                        int woodAmount = (priorState == TreeState.WholeTree) ? 4 : 2;
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
