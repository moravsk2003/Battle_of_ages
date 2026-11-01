using BattleOfAges.Crafting;
﻿using UnityEngine;
using BattleOfAges.RTS;
using BattleOfAges.Utils;
using BattleOfAges.Resources;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Runtime component attached to each placed building on the map.
    /// Handles procedural 3D visuals and NPC spawning on construction.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingNode : MonoBehaviour
    {
        private static Shader cachedUnlitShader;

        [SerializeField] private BuildingType buildingType = BuildingType.WorkerHouse;

        /// <summary>The building's type; mines, workbenches and chests built before the type was set are recognised by their component.</summary>
        public void SetType(BuildingType type) => buildingType = type;

        /// <summary>Set for buildings that come with the scene (name + place at load): a save restores their state instead of rebuilding them.</summary>
        public string SceneKey { get; set; }

        public BuildingType Type
        {
            get
            {
                if (buildingType != BuildingType.WorkerHouse) return buildingType;
                if (GetComponent<MineNode>() != null) return BuildingType.Mine;
                if (GetComponent<WorkbenchNode>() != null) return BuildingType.Workbench;
                if (GetComponent<StorageChestNode>() != null) return BuildingType.StorageChest;
                return buildingType;
            }
        }

        private static readonly System.Collections.Generic.List<BuildingNode> active = new System.Collections.Generic.List<BuildingNode>();

        /// <summary>Placed buildings (raiders look for walls to attack here instead of physics overlap queries).</summary>
        public static System.Collections.Generic.IReadOnlyList<BuildingNode> Active => active;

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

        private void Awake()
        {
            Combat.HealthComponent hp = GetComponent<Combat.HealthComponent>();
            if (hp != null)
            {
                hp.OnDeath += HandleBuildingDestroyed;
            }
        }

        private void Start()
        {
            Combat.HealthComponent hp = GetComponent<Combat.HealthComponent>();
            if (hp != null)
            {
                hp.OnDeath -= HandleBuildingDestroyed;
                hp.OnDeath += HandleBuildingDestroyed;
            }
        }

        private void OnDestroy()
        {
            Combat.HealthComponent hp = GetComponent<Combat.HealthComponent>();
            if (hp != null)
            {
                hp.OnDeath -= HandleBuildingDestroyed;
            }
        }

        private void HandleBuildingDestroyed()
        {
            Destroy(gameObject);
        }

        public void OnBuilt()
        {
            BuildingDefinition def = BuildingData.GetDefinition(Type); // (a mine or workbench used to spawn a house's worker)

            if (World.WorldSettings.Bounded && Type == BuildingType.WorkerHouse) ApplyEpochLook(TownHall.Current != null ? TownHall.Current.Level : 1);

            Combat.HealthComponent hp = GetComponent<Combat.HealthComponent>();
            WallSegment wall = GetComponent<WallSegment>();
            if (hp != null && def.MaxHealth > 0)
            {
                hp.InitializeHealth(wall != null ? WallSegment.MaxHealthFor(Type, wall.OreTier) : def.MaxHealth);
            }

            // (in a campaign a house quickens births instead: TownHall.GrowthPerHour)
            for (int i = 0; !World.WorldSettings.Bounded && i < def.NpcSpawnCount && TownHall.HasRoom(); i++) // the town hall caps the population
            {
                SpawnWorkerNpc(i);
            }

        }

        private static MaterialPropertyBlock epochTint;

        /// <summary>A house's size for a hall level: a hut under hall II, a house, a stone house from hall IV (§10.1).</summary>
        public static float HouseScale(int hallLevel) => 0.8f + 0.12f * (Mathf.Clamp(hallLevel, 1, TownHall.MaxLevel) - 1);

        /// <summary>The look of the epoch it is built in: hut → house → stone house (§10.1, M5 step 5).</summary>
        public void ApplyEpochLook(int hallLevel)
        {
            transform.localScale = Vector3.one * HouseScale(hallLevel);
            if (hallLevel < 4) return;
            if (epochTint == null)
            {
                epochTint = new MaterialPropertyBlock();
                epochTint.SetColor("_BaseColor", new Color(0.78f, 0.78f, 0.82f)); // the plaster goes to stone
            }
            foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(epochTint);
        }

        private void SpawnWorkerNpc(int index)
        {
            Vector3 spawnPos = transform.position + transform.forward * 3.5f + transform.right * (index * 1.5f);
            spawnPos.y = RtsUnit.GetTerrainHeight(spawnPos);

            CreateWorkerNpc(spawnPos, NextVillagerName());
        }

        private static int nextVillagerNumber = 1;

        /// <summary>
        /// A name no villager has: the numbers only grow (names stay unique after deaths), and one placed in the scene
        /// beforehand is skipped too.
        /// </summary>
        public static string NextVillagerName()
        {
            while (VillagerNamed($"NPC_Villager_{nextVillagerNumber}")) nextVillagerNumber++;
            return $"NPC_Villager_{nextVillagerNumber}";
        }

        private static bool VillagerNamed(string name)
        {
            foreach (NpcResourceGatherer w in WorkerRegistry.ActiveWorkers) if (w != null && w.name == name) return true;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetVillagerNumbers() => nextVillagerNumber = 1;

        /// <summary>A villager worker (gatherer) at the position; also used when a save is loaded.</summary>
        public static GameObject CreateWorkerNpc(Vector3 spawnPos, string npcName)
        {
            // the next new villager gets a number above every one made so far (a loaded save brings its own numbers)
            int dash = npcName.LastIndexOf('_');
            if (dash >= 0 && int.TryParse(npcName.Substring(dash + 1), out int number) && number >= nextVillagerNumber) nextVillagerNumber = number + 1;
            GameObject npcObj = new GameObject(npcName);
            npcObj.transform.position = spawnPos;

            CharacterController charCtrl = npcObj.AddComponent<CharacterController>();
            charCtrl.height = 2.0f;
            charCtrl.center = new Vector3(0, 1.0f, 0);
            charCtrl.radius = 0.45f;
            charCtrl.stepOffset = 0.4f;
            charCtrl.slopeLimit = 50.0f;

            // Every unit can fight (docs/GAME_DESIGN.md §11.1): a villager is a person, 20 HP and strength 2 (RtsUnit applies the rank)
            npcObj.AddComponent<Combat.HealthComponent>();
            npcObj.AddComponent<Combat.CombatUnit>();
            RtsUnit unitComp = npcObj.AddComponent<RtsUnit>();
            unitComp.IsVillager = true;
            npcObj.AddComponent<NpcResourceGatherer>();

            GameObject npcModel = HumanoidModelBuilder.CreateHumanoidModel(
                HumanoidModelBuilder.HumanType.Villager, "3DModel");
            npcModel.transform.SetParent(npcObj.transform, false);
            npcModel.transform.localPosition = Vector3.zero;

            GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            indicator.name = "SelectionIndicator";
            indicator.transform.SetParent(npcObj.transform, false);
            indicator.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            indicator.transform.localScale = new Vector3(1.8f, 0.02f, 1.8f);

            MeshCollider indCol = indicator.GetComponent<MeshCollider>();
            if (indCol != null) Destroy(indCol);

            MeshRenderer indRenderer = indicator.GetComponent<MeshRenderer>();
            if (indRenderer != null)
            {
                indRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                indRenderer.receiveShadows = false;

                if (cachedUnlitShader == null)
                {
                    cachedUnlitShader = Shader.Find("Sprites/Default")
                        ?? Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Transparent");
                }

                Material indMat = new Material(cachedUnlitShader);
                indMat.color = new Color(0.2f, 0.85f, 1.0f, 0.85f);
                indMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                indMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
                indRenderer.material = indMat;
            }

            indicator.SetActive(false);
            unitComp.SetSelectionIndicator(indicator);

            return npcObj;
        }

        public static GameObject CreateWorkerHousePrefab(string name = "WorkerHouse")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<BuildingNode>();

            // Timber-framed cottage with a thatched roof (one shared mesh, see WorkerHouseBuilder)
            WorkerHouseBuilder.Attach(root.transform);

            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 2.1f, 0f);
            col.size = new Vector3(3.4f, 4.2f, 3.4f);
            return root;
        }

        public static GameObject CreateCastleWallPrefab(string name = "CastleWall") => CreateWallPrefab(BuildingType.CastleWall, name);

        /// <summary>
        /// A wall segment, the one with stairs or the gate (WallBuilder; §10.5): HP of ore tier 0 until it is built of
        /// better ore (WallSegment.OreTier, applied in OnBuilt). Colliders: the solid body (its top is the walkway), the
        /// breastwork, for the stairs a ramp with the climb assist, for the gate the jambs, the work over the arch and the
        /// leaves on their hinges.
        /// </summary>
        public static GameObject CreateWallPrefab(BuildingType type, string name)
        {
            GameObject root = new GameObject(name);
            BuildingNode bNode = root.AddComponent<BuildingNode>();
            bNode.buildingType = type;
            root.AddComponent<Combat.HealthComponent>().InitializeHealth(BuildingData.GetDefinition(type).MaxHealth);
            WallBuilder.Kind kind = WallSegment.KindOf(type);
            root.AddComponent<WallSegment>().Kind = kind;
            WallBuilder.Attach(root.transform, kind);

            float half = WallBuilder.LengthOf(kind) * 0.5f, t = WallBuilder.Thickness, walk = WallBuilder.WalkY;
            void Box(string partName, Vector3 center, Vector3 size, Quaternion? rot = null, bool trigger = false)
            {
                var part = new GameObject(partName);
                part.transform.SetParent(root.transform, false);
                part.transform.SetLocalPositionAndRotation(center, rot ?? Quaternion.identity);
                BoxCollider c = part.AddComponent<BoxCollider>();
                c.size = size;
                c.isTrigger = trigger;
                if (trigger) part.AddComponent<CastleWallClimbTrigger>();
            }

            if (kind == WallBuilder.Kind.Gate)
            {
                float w = WallBuilder.GateHalfW, crown = WallBuilder.GateSpring + w, side = half - w;
                Box("JambLeft", new Vector3(-(half + w) * 0.5f, walk * 0.5f, 0f), new Vector3(side, walk, t));
                Box("JambRight", new Vector3((half + w) * 0.5f, walk * 0.5f, 0f), new Vector3(side, walk, t));
                Box("OverArch", new Vector3(0f, (crown + walk) * 0.5f, 0f), new Vector3(w * 2f, walk - crown, t));
                Transform Leaf(string leafName, float x, float mirror)
                {
                    var leaf = new GameObject(leafName);
                    leaf.transform.SetParent(root.transform, false);
                    leaf.transform.localPosition = new Vector3(x, 0f, WallBuilder.GateLeafZ);
                    var model = new GameObject("Model");
                    model.transform.SetParent(leaf.transform, false);
                    model.transform.localScale = new Vector3(mirror, 1f, 1f);
                    model.AddComponent<MeshFilter>().sharedMesh = WallBuilder.LeafMesh;
                    model.AddComponent<MeshRenderer>().sharedMaterial = MeshKit.PropMaterial();
                    BoxCollider c = leaf.AddComponent<BoxCollider>();
                    c.center = new Vector3(mirror * w * 0.5f, WallBuilder.GateSpring * 0.6f, 0f);
                    c.size = new Vector3(w, WallBuilder.GateSpring * 1.2f, 0.14f);
                    return leaf.transform;
                }
                root.AddComponent<WallGateDoors>().SetLeaves(Leaf("LeafLeft", -w, 1f), Leaf("LeafRight", w, -1f));

                GameObject fireLightObj = new GameObject("TorchLight");
                fireLightObj.transform.SetParent(root.transform, false);
                fireLightObj.transform.localPosition = new Vector3(-w - 0.55f, 2.62f, -t * 0.5f - 0.25f);
                Light torchLight = fireLightObj.AddComponent<Light>();
                torchLight.type = LightType.Point;
                torchLight.color = new Color(1.0f, 0.72f, 0.28f);
                torchLight.intensity = 1.4f;
                torchLight.range = 7.0f;
                torchLight.shadows = LightShadows.None; // a shadowed point light renders the scene six more times (BuildingProbe)
            }
            else Box("Body", new Vector3(0f, walk * 0.5f, 0f), new Vector3(half * 2f, walk, t));

            float parapetH = WallBuilder.BreastH + WallBuilder.MerlonH + 0.12f;
            Box("Parapet", new Vector3(0f, walk + parapetH * 0.5f, t * 0.5f - WallBuilder.ParapetT * 0.5f), new Vector3(half * 2f, parapetH, WallBuilder.ParapetT));

            if (kind == WallBuilder.Kind.Stairs)
            {
                float run = (WallBuilder.StairsLowX - WallBuilder.StairsTopX) / WallBuilder.StairSteps;
                float topX = WallBuilder.StairsTopX + run, zc = (WallBuilder.StairsInnerZ + WallBuilder.StairsOuterZ) * 0.5f, width = WallBuilder.StairsInnerZ - WallBuilder.StairsOuterZ;
                Box("Landing", new Vector3((topX - half) * 0.5f, walk * 0.5f, zc), new Vector3(topX + half, walk, width));
                // The flight: a ramp along the step noses (from the bottom step up to the landing), and the climb assist on it
                Vector3 low = new Vector3(WallBuilder.StairsLowX, 0f, zc), high = new Vector3(topX, walk, zc);
                Quaternion up = Quaternion.LookRotation(high - low, Vector3.up);
                Vector3 normal = up * Vector3.up;
                float slope = (high - low).magnitude;
                Box("StairsRampCollider", (low + high) * 0.5f - normal * 0.1f, new Vector3(width, 0.2f, slope), up);
                Box("ClimbAssistTrigger", (low + high) * 0.5f + normal * 0.6f, new Vector3(width + 0.1f, 1.4f, slope), up, true);
            }
            return root;
        }

        public static GameObject CreateGhostPreview(BuildingType type)
        {
            GameObject ghost = null;

            if (type == BuildingType.WorkerHouse)
            {
                ghost = CreateWorkerHousePrefab("BuildGhost_WorkerHouse");
            }
            else if (type == BuildingType.StorageChest)
            {
                ghost = StorageChestNode.CreateStorageChestPrefab("BuildGhost_StorageChest");
            }
            else if (type == BuildingType.Mine)
            {
                ghost = MineNode.CreateMinePrefab("BuildGhost_Mine");
            }
            else if (type == BuildingType.Workbench)
            {
                ghost = WorkbenchNode.CreateWorkbenchPrefab("BuildGhost_Workbench");
            }
            else if (WallSegment.IsWall(type))
            {
                ghost = CreateWallPrefab(type, "BuildGhost_" + type);
                Object.DestroyImmediate(ghost.GetComponent<WallSegment>()); // not a wall yet: no end to snap to, no piers
                Object.DestroyImmediate(ghost.GetComponent<WallGateDoors>());
                float half = WallBuilder.LengthOf(WallSegment.KindOf(type)) * 0.5f;
                foreach (float x in new[] { -half, half }) WallBuilder.AttachPier(ghost.transform).transform.localPosition = new Vector3(x, 0f, 0f);
            }
            else if (type == BuildingType.TownHall)
            {
                ghost = TownHall.CreatePrefab("BuildGhost_TownHall");
            }

            if (ghost == null) return null;

            BuildingNode bNode = ghost.GetComponent<BuildingNode>();
            if (bNode != null) DestroyImmediate(bNode);

            StorageChestNode cNode = ghost.GetComponent<StorageChestNode>();
            if (cNode != null) DestroyImmediate(cNode);

            MineNode mNode = ghost.GetComponent<MineNode>();
            if (mNode != null) DestroyImmediate(mNode);

            WorkbenchNode wNode = ghost.GetComponent<WorkbenchNode>();
            if (wNode != null) DestroyImmediate(wNode);

            TownHall hNode = ghost.GetComponent<TownHall>();
            if (hNode != null) DestroyImmediate(hNode);

            foreach (var col in ghost.GetComponentsInChildren<Collider>())
            {
                DestroyImmediate(col);
            }

            // Remove lights from ghost preview
            foreach (var l in ghost.GetComponentsInChildren<Light>())
            {
                DestroyImmediate(l.gameObject);
            }

            if (cachedUnlitShader == null)
            {
                cachedUnlitShader = Shader.Find("Sprites/Default")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color");
            }

            foreach (var mr in ghost.GetComponentsInChildren<MeshRenderer>())
            {
                Material ghostMat = new Material(cachedUnlitShader);
                ghostMat.color = new Color(0.2f, 0.9f, 0.4f, 0.4f);
                ghostMat.renderQueue = 3100;
                mr.material = ghostMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            if (ghost != null) ghost.AddComponent<BuildGhost>();
            return ghost;
        }
    }
}
