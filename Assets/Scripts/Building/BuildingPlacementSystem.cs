using UnityEngine;
using BattleOfAges.Managers;
using BattleOfAges.Resources;
using BattleOfAges.RTS;
using BattleOfAges.CameraSystem;
using BattleOfAges.InputSystem;
using BattleOfAges.Crafting;
using BattleOfAges.UI;
using BattleOfAges.World;
using BattleOfAges.Utils;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Handles the placement of buildings on the map in RTS mode.
    /// Uses MaterialPropertyBlock for zero-memory-leak ghost preview tinting and OverlapBoxNonAlloc for zero-GC placement validation.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingPlacementSystem : MonoBehaviour
    {
        public static BuildingPlacementSystem Instance { get; private set; }

        private static readonly Collider[] overlapBuffer = new Collider[64];
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");

        private GameModeManager gameModeManager;
        private Camera mapCamera;
        private MaterialPropertyBlock ghostPropertyBlock;

        private GameObject ghostPreview;
        private BuildingType currentBuildingType;
        private bool isPlacing = false;
        /// <summary>The ghost's turn (Z / X) when it is not snapped to a wall's end.</summary>
        private float ghostYaw;
        /// <summary>The ore tier walls are built of (Tab picks another the player has enough of).</summary>
        private int wallTier;
        private bool snapped;

        public bool IsPlacing => isPlacing;
        public const float TurnStep = 15f;

        /// <summary>What the keys do now (the HUD's hint line while placing).</summary>
        private string hint;
        private long hintKey = long.MinValue;

        /// <summary>The HUD line while placing; made again only when the building, the ore or the snap changes.</summary>
        public string Hint
        {
            get
            {
                if (!isPlacing) return "";
                long key = ((long)currentBuildingType * 1000 + wallTier) * 2 + (snapped ? 1 : 0);
                if (key == hintKey && hint != null) return hint;
                hintKey = key;
                return hint = MakeHint();
            }
        }

        private string MakeHint()
        {
            BuildingDefinition def = BuildingData.GetDefinition(currentBuildingType);
            if (!WallSegment.IsWall(currentBuildingType)) return $"{def.DisplayName}\nZ / X — поворот  ·  ЛКМ — збудувати  ·  ПКМ — скасувати";
            return $"{def.DisplayName}: {OreTierHelper.GetTierName(wallTier)}, HP {WallSegment.MaxHealthFor(currentBuildingType, wallTier)}\nTab — руда  ·  "
                 + (snapped ? "прилипла до стіни" : "Z / X — поворот, біля краю стіни прилипне") + "  ·  ПКМ — скасувати";
        }

        /// <summary>The frame a placement was last cancelled (that Escape is not also the pause or the way out of the map).</summary>
        public int CancelledFrame { get; private set; } = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ghostPropertyBlock = new MaterialPropertyBlock();
        }

        private void Start()
        {
            gameModeManager = Managers.GameContext.Modes;

            RtsMapCameraController mapController = Managers.GameContext.MapCamera;
            if (mapController != null)
            {
                mapCamera = mapController.GetComponent<Camera>();
            }
        }

        private void Update()
        {
            if (!isPlacing) return;

            // Cancel if we leave RTS mode
            if (gameModeManager != null && gameModeManager.CurrentMode != GameMode.RtsMap)
            {
                CancelPlacement();
                return;
            }

            // Ensure camera reference
            if (mapCamera == null || !mapCamera.enabled)
            {
                RtsMapCameraController mapController = Managers.GameContext.MapCamera;
                if (mapController != null) mapCamera = mapController.GetComponent<Camera>();
                if (mapCamera == null) return;
            }

            if (UniversalInput.IsKeyDown(KeyCode.Z)) ghostYaw -= TurnStep;
            if (UniversalInput.IsKeyDown(KeyCode.X)) ghostYaw += TurnStep;
            if (WallSegment.IsWall(currentBuildingType) && UniversalInput.IsKeyDown(KeyCode.Tab)) wallTier = NextWallTier(wallTier, BuildingData.GetDefinition(currentBuildingType).OreCost);

            UpdateGhostPosition();

            // Confirm placement on Left Mouse Button
            if (UniversalInput.GetMouseButtonDown(0))
            {
                TryConfirmPlacement();
            }

            // Cancel placement on Right Mouse Button or Escape
            if (UniversalInput.GetMouseButtonDown(1) || UniversalInput.GetKeyDownEscape())
            {
                CancelPlacement();
            }
        }

        public void StartPlacement(BuildingType type)
        {
            if (isPlacing)
            {
                CancelPlacement();
            }

            BuildingDefinition def = BuildingData.GetDefinition(type);
            if (!CanAfford(def))
            {
                Debug.Log($"[Building] Недостатньо ресурсів для {def.DisplayName}!");
                return;
            }
            if (!IsAllowed(type, out string why))
            {
                StrategyHud.Notify(why);
                return;
            }

            currentBuildingType = type;
            isPlacing = true;
            if (WallSegment.IsWall(type) && !CanAffordTier(def, wallTier)) wallTier = NextWallTier(wallTier, def.OreCost);

            ghostPreview = BuildingNode.CreateGhostPreview(type);
            if (ghostPreview != null)
            {
                ghostPreview.SetActive(true);
            }

        }

        public void CancelPlacement()
        {
            if (isPlacing) CancelledFrame = Time.frameCount;
            isPlacing = false;
            if (ghostPreview != null)
            {
                Destroy(ghostPreview);
                ghostPreview = null;
            }
        }

        private void UpdateGhostPosition()
        {
            if (ghostPreview == null || mapCamera == null) return;

            Vector3 mousePos = UniversalInput.GetMousePosition();
            Ray ray = mapCamera.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out RaycastHit hit, 1000f))
            {
                Vector3 pos = hit.point;
                BuildingDefinition def = BuildingData.GetDefinition(currentBuildingType);
                Quaternion rot = Quaternion.Euler(0f, ghostYaw, 0f);
                float yaw = 0f, endY = 0f;
                snapped = WallSegment.IsWall(currentBuildingType) && WallSegment.Snap(pos, def.Size.x, out pos, out yaw, out endY);
                if (snapped) rot = Quaternion.Euler(0f, yaw, 0f);
                bool level = FootprintLevel(currentBuildingType, pos, rot, out float h);
                // A snapped segment keeps its neighbour's height (one walkway) while its foundation still reaches the ground
                pos.y = snapped && Mathf.Abs(h - endY) <= SnapKeepHeight ? endY : h;
                ghostPreview.transform.SetPositionAndRotation(pos, rot);

                bool validLoc = level && CheckLocationValid(pos, rot, def);
                bool affordable = CanAfford(def) && IsAllowed(currentBuildingType, out _);

                Color ghostColor = (validLoc && affordable)
                    ? new Color(0.2f, 0.9f, 0.4f, 0.45f)  // Green
                    : new Color(0.95f, 0.2f, 0.2f, 0.45f); // Red

                UpdateGhostColor(ghostColor);
            }
        }

        private void UpdateGhostColor(Color color)
        {
            if (ghostPreview == null || ghostPropertyBlock == null) return;

            ghostPropertyBlock.SetColor(ColorPropertyId, color);
            ghostPropertyBlock.SetColor(BaseColorPropertyId, color);

            MeshRenderer[] renderers = ghostPreview.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].SetPropertyBlock(ghostPropertyBlock);
                }
            }
        }

        /// <summary>Most a footprint's ground may rise across it (a house on a hillside sank into it on one side and hung over it on the other).</summary>
        public const float MaxGroundRise = 0.9f;

        /// <summary>
        /// Whether the ground under the footprint is level enough to build on, and the height to build at: a bit above
        /// the middle of the ground's range, so the uphill side does not bury the walls (the foundation under the
        /// plinth covers the downhill side).
        /// </summary>
        public static bool GroundLevel(Vector3 position, Vector3 size, out float height) => GroundLevel(position, size, RtsUnit.GetTerrainHeight, out height);

        public static bool GroundLevel(Vector3 position, Vector3 size, System.Func<Vector3, float> groundAt, out float height) =>
            GroundRise(position, size, groundAt, out height) <= MaxGroundRise;

        /// <summary>A snapped wall segment keeps the height of the end it joins if its own ground is within this of it.</summary>
        public const float SnapKeepHeight = 0.35f;

        /// <summary>Most the ground may rise across the type's footprint (walls have deeper foundations than houses).</summary>
        public static float MaxRiseFor(BuildingType type) => WallSegment.IsWall(type) ? Combat.GameBalance.WallMaxGroundRise : MaxGroundRise;

        /// <summary>GroundLevel for the type's footprint turned by rot (and offset by its FootprintCenter).</summary>
        public static bool FootprintLevel(BuildingType type, Vector3 position, Quaternion rot, out float height) =>
            FootprintLevel(type, position, rot, RtsUnit.GetTerrainHeight, out height);

        public static bool FootprintLevel(BuildingType type, Vector3 position, Quaternion rot, System.Func<Vector3, float> groundAt, out float height)
        {
            BuildingDefinition def = BuildingData.GetDefinition(type);
            return GroundRise(position, def.Size, rot, def.FootprintCenter, groundAt, out height) <= MaxRiseFor(type);
        }

        /// <summary>How much the ground rises across the footprint, and the height to build at (see GroundLevel).</summary>
        public static float GroundRise(Vector3 position, Vector3 size, System.Func<Vector3, float> groundAt, out float height) =>
            GroundRise(position, size, Quaternion.identity, Vector3.zero, groundAt, out height);

        public static float GroundRise(Vector3 position, Vector3 size, Quaternion rot, Vector3 offset, System.Func<Vector3, float> groundAt, out float height)
        {
            float low = float.MaxValue, high = float.MinValue;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    if (Mathf.Abs(i) < 2 && Mathf.Abs(j) < 2 && (i != 0 || j != 0)) continue; // the rim and the middle
                    float h = groundAt(position + rot * (offset + new Vector3(size.x * 0.25f * i, 0f, size.z * 0.25f * j)));
                    low = Mathf.Min(low, h); high = Mathf.Max(high, h);
                }
            height = Mathf.Lerp(low, high, 0.6f);
            return high - low;
        }

        /// <summary>
        /// The box checked for things in the way: the footprint with room round it for NPCs to pass; a wall segment's
        /// without its ends (the piers and the neighbours it joins stand there) and with little room (walls are joined).
        /// </summary>
        public static void CheckBox(BuildingDefinition def, out Vector3 localCenter, out Vector3 size)
        {
            if (WallSegment.IsWall(def.Type))
            {
                size = new Vector3(Mathf.Max(0.5f, def.Size.x - WallBuilder.PierApothem * 2f - 0.1f), def.Size.y, def.Size.z + 0.2f);
            }
            else size = def.Size + new Vector3(0.8f, 0f, 0.8f);
            localCenter = def.FootprintCenter + new Vector3(0f, size.y * 0.5f + 0.1f, 0f);
        }

        private bool CheckLocationValid(Vector3 position, Quaternion rot, BuildingDefinition def)
        {
            if (World.Lakes.InWater(position.x, position.z, 3f)) return false; // the ghost turns red over water
            if (def.Type == BuildingType.TownHall && World.WorldSettings.Bounded && TownHall.FoundingForbidden(position) != null) return false;
            CheckBox(def, out Vector3 local, out Vector3 size);
            Vector3 center = position + rot * local;
            Vector3 halfExtents = size * 0.5f;

            int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, overlapBuffer, rot);

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = overlapBuffer[i];
                if (col == null || col.isTrigger) continue;

                if (ghostPreview != null && (col.gameObject == ghostPreview || col.transform.IsChildOf(ghostPreview.transform))) continue;

                // Ignore ground/terrain colliders (procedural chunks, steppe terrain, etc.)
                if (IsTerrainCollider(col)) continue;

                // Ignore mobile units / NPCs or UI indicators (NPCs steer around new buildings)
                if (col.GetComponent<CharacterController>() != null || 
                    col.GetComponent<RtsUnit>() != null || 
                    col.gameObject.name.Contains("SelectionIndicator"))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        private static bool IsTerrainCollider(Collider col)
        {
            if (col == null) return false;
            if (col is TerrainCollider) return true;
            if (col.GetComponent<ProceduralBiomeChunk>() != null || 
                col.GetComponentInParent<ProceduralBiomeChunk>() != null)
            {
                return true;
            }

            string colName = col.gameObject.name;
            if (colName.Contains("Terrain") || colName.Contains("Ground") || 
                colName.Contains("Chunk") || colName.Contains("Steppe"))
            {
                return true;
            }

            return false;
        }

        private void TryConfirmPlacement()
        {
            BuildingDefinition def = BuildingData.GetDefinition(currentBuildingType);
            Vector3 buildPos = (ghostPreview != null) ? ghostPreview.transform.position : transform.position;
            Quaternion buildRot = (ghostPreview != null) ? ghostPreview.transform.rotation : Quaternion.identity;

            if (!IsAllowed(currentBuildingType, out string notAllowed))
            {
                StrategyHud.Notify(notAllowed);
                CancelPlacement();
                return;
            }

            if (!FootprintLevel(currentBuildingType, buildPos, buildRot, out _))
            {
                StrategyHud.Notify("Тут надто круто для будівлі");
                return;
            }
            if (World.Lakes.InWater(buildPos.x, buildPos.z, 3f))
            {
                StrategyHud.Notify("На воді не збудувати");
                return;
            }

            if (def.Type == BuildingType.TownHall && World.WorldSettings.Bounded && TownHall.FoundingForbidden(buildPos) is string foundingNo)
            {
                StrategyHud.Notify(foundingNo);
                return;
            }
            if (!CheckLocationValid(buildPos, buildRot, def))
            {
                Debug.Log($"[Building] Неможливо побудувати тут: занадто близько до іншої будівлі або об'єкта! Потрібен прохід для NPC.");
                return;
            }

            if (!CanAfford(def))
            {
                Debug.Log($"[Building] Недостатньо ресурсів для {def.DisplayName}!");
                CancelPlacement();
                return;
            }

            ResourceManager rm = ResourceManager.Instance;
            if (rm == null)
            {
                CancelPlacement();
                return;
            }

            if (!Combat.CentralEmpire.Active) // building is free for the Central Empire
            {
                if (def.WoodCost > 0) rm.ConsumeResource(ResourceType.Wood, def.WoodCost);
                if (def.OreCost > 0)
                {
                    if (WallSegment.IsWall(def.Type)) rm.ConsumeOre(OreTierOf(def), def.OreCost); // a wall is as strong as its ore
                    else rm.ConsumeOreAtLeast(OreTierOf(def), def.OreCost); // others take its tier or better, the commonest first
                }
            }

            GameObject building = CreateBuilding(currentBuildingType);

            if (building != null)
            {
                building.transform.SetPositionAndRotation(buildPos, buildRot);
                GameSound.At(GameSound.Kind.Place, buildPos);
                WallSegment wall = building.GetComponent<WallSegment>();
                if (wall != null) wall.OreTier = wallTier;

                BuildingNode node = building.GetComponent<BuildingNode>();
                if (node != null)
                {
                    if (World.WorldSettings.Bounded) ConstructionSite.Begin(building, currentBuildingType); // a campaign: workers build it (M5)
                    else node.OnBuilt();
                }
            }

            if (WallSegment.IsWall(def.Type) && !CanAffordTier(def, wallTier)) wallTier = NextWallTier(wallTier, def.OreCost); // that ore ran out: go on with other
            if (!CanAfford(def))
            {
                CancelPlacement();
            }
        }

        /// <summary>Settlement rules: one town hall; a worker house only while the population is under the town hall's cap.</summary>
        public static bool IsAllowed(BuildingType type, out string reason)
        {
            reason = null;
            if (type == BuildingType.TownHall && TownHall.Current != null && !World.WorldSettings.Bounded) { reason = "Ратуша вже є: її можна покращити"; return false; }
            if (type == BuildingType.TownHall && TownHall.Current != null && TownHall.Current.Level < Combat.GameBalance.FoundingHallLevel) { reason = $"Нове поселення — від ратуші {ItemTypeNames.Roman(Combat.GameBalance.FoundingHallLevel)}"; return false; }
            if (type == BuildingType.TownHall && ConstructionSite.IsBuilding(type)) { reason = "Ратуша вже будується"; return false; }
            if (type == BuildingType.WorkerHouse && !TownHall.HasRoom(BuildingData.GetDefinition(type).NpcSpawnCount))
            {
                reason = TownHall.Current == null
                    ? $"Населення {TownHall.Population}/{TownHall.PopulationCap}: збудуйте Ратушу"
                    : $"Населення {TownHall.Population}/{TownHall.PopulationCap}: покращте Ратушу";
                return false;
            }
            return true;
        }

        /// <summary>A finished building of the type (not yet placed; OnBuilt not called). Also used to rebuild a save.</summary>
        public static GameObject CreateBuilding(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.WorkerHouse: return BuildingNode.CreateWorkerHousePrefab("WorkerHouse");
                case BuildingType.StorageChest: return StorageChestNode.CreateStorageChestPrefab("StorageChest");
                case BuildingType.Mine: return MineNode.CreateMinePrefab("Mine");
                case BuildingType.Workbench: return WorkbenchNode.CreateWorkbenchPrefab("Workbench");
                case BuildingType.CastleWall:
                case BuildingType.WallStairs:
                case BuildingType.WallGate: return BuildingNode.CreateWallPrefab(type, type.ToString());
                case BuildingType.TownHall: return TownHall.CreatePrefab("TownHall");
                case BuildingType.Field: return Utils.ZoneBuildingBuilder.CreateField();
                case BuildingType.Sawmill: return Utils.ZoneBuildingBuilder.CreateSawmill();
                case BuildingType.Market: return Utils.ZoneBuildingBuilder.CreateMarket();
                default: return null;
            }
        }

        /// <summary>The ore tier the building takes: a wall's is the one picked for it (its strength).</summary>
        private int OreTierOf(BuildingDefinition def) => WallSegment.IsWall(def.Type) ? wallTier : def.OreTierRequired;

        /// <summary>The stock has what the building costs now (a wall: in some ore tier, see NextWallTier).</summary>
        public bool CanAfford(BuildingType type) => CanAfford(BuildingData.GetDefinition(type));

        private bool CanAfford(BuildingDefinition def)
        {
            if (WallSegment.IsWall(def.Type) && !isPlacing) return CanAffordTier(def, NextWallTier(wallTier, def.OreCost, true));
            return CanAffordTier(def, OreTierOf(def));
        }

        private static bool CanAffordTier(BuildingDefinition def, int tier)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return false;
            if (Combat.CentralEmpire.Active) return true;

            if (def.WoodCost > 0 && rm.WoodCount < def.WoodCost) return false;
            int have = WallSegment.IsWall(def.Type) ? rm.GetOreTierCount(tier) : rm.GetOreCountAtLeast(tier);
            if (def.OreCost > 0 && have < def.OreCost) return false;

            return true;
        }

        /// <summary>
        /// The next ore tier above current (round to the lowest) that the player has at least cost pieces of; current
        /// itself if there is no other; with keep, current when it is still enough.
        /// </summary>
        public static int NextWallTier(int current, int cost, bool keep = false)
        {
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return current;
            if (keep && rm.GetOreTierCount(current) >= cost) return current;
            int first = -1, next = -1;
            foreach (var pair in rm.OreTiers())
            {
                if (pair.Value < cost) continue;
                if (first < 0 || pair.Key < first) first = pair.Key;
                if (pair.Key > current && (next < 0 || pair.Key < next)) next = pair.Key;
            }
            return next >= 0 ? next : first >= 0 ? first : current;
        }
    }
}
