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
        private static Shader cachedLitShader;
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

            Combat.HealthComponent hp = GetComponent<Combat.HealthComponent>();
            if (hp != null && def.MaxHealth > 0)
            {
                hp.InitializeHealth(def.MaxHealth);
            }

            for (int i = 0; i < def.NpcSpawnCount && TownHall.HasRoom(); i++) // the town hall caps the population
            {
                SpawnWorkerNpc(i);
            }

        }

        private void SpawnWorkerNpc(int index)
        {
            Vector3 spawnPos = transform.position + transform.forward * 3.5f + transform.right * (index * 1.5f);
            spawnPos.y = RtsUnit.GetTerrainHeight(spawnPos);

            int workerNumber = WorkerRegistry.WorkerCount + 1;
            while (GameObject.Find($"NPC_Villager_{workerNumber}") != null) workerNumber++; // names stay unique after deaths
            CreateWorkerNpc(spawnPos, $"NPC_Villager_{workerNumber}");
        }

        /// <summary>A villager worker (gatherer) at the position; also used when a save is loaded.</summary>
        public static GameObject CreateWorkerNpc(Vector3 spawnPos, string npcName)
        {
            GameObject npcObj = new GameObject(npcName);
            npcObj.transform.position = spawnPos;

            CharacterController charCtrl = npcObj.AddComponent<CharacterController>();
            charCtrl.height = 2.0f;
            charCtrl.center = new Vector3(0, 1.0f, 0);
            charCtrl.radius = 0.45f;
            charCtrl.stepOffset = 0.4f;
            charCtrl.slopeLimit = 50.0f;

            RtsUnit unitComp = npcObj.AddComponent<RtsUnit>();
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

        public static GameObject CreateCastleWallPrefab(string name = "CastleWall")
        {
            GameObject root = new GameObject(name);
            BuildingNode bNode = root.AddComponent<BuildingNode>();
            bNode.buildingType = BuildingType.CastleWall;

            Combat.HealthComponent hp = root.AddComponent<Combat.HealthComponent>();
            hp.InitializeHealth(600);

            if (cachedLitShader == null)
            {
                cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }

            Material stoneMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.42f, 0.44f, 0.46f), 0.05f, 0.22f);
            Material darkStoneMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.30f, 0.32f, 0.34f), 0.08f, 0.25f);
            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.35f, 0.22f, 0.10f), 0f, 0.25f);
            Material ironMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Metal, new Color(0.24f, 0.25f, 0.27f), 0.8f, 0.6f);

            // 1. Main Stone Wall Section (Outer Front Face)
            GameObject mainWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mainWall.name = "WallBody";
            mainWall.transform.SetParent(root.transform, false);
            mainWall.transform.localPosition = new Vector3(0f, 1.5f, 0.15f);
            mainWall.transform.localScale = new Vector3(4.0f, 3.0f, 0.75f);
            mainWall.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;

            // 2. Left and Right Reinforcing Stone Pillars (Buttresses)
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pillar.name = (i < 0) ? "PillarLeft" : "PillarRight";
                pillar.transform.SetParent(root.transform, false);
                pillar.transform.localPosition = new Vector3(i * 1.95f, 1.7f, 0.1f);
                pillar.transform.localScale = new Vector3(0.65f, 3.4f, 1.05f);
                pillar.GetComponent<MeshRenderer>().sharedMaterial = darkStoneMat;

                // Pillar stone cap
                GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "PillarCap";
                cap.transform.SetParent(pillar.transform, false);
                cap.transform.localPosition = new Vector3(0f, 0.52f, 0f);
                cap.transform.localScale = new Vector3(1.15f, 0.10f, 1.15f);
                cap.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
            }

            // 3. Battlements / Merlons along top walkway (Outer Edge)
            for (int i = -1; i <= 1; i++)
            {
                GameObject merlon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                merlon.name = $"Merlon_{i + 2}";
                merlon.transform.SetParent(root.transform, false);
                merlon.transform.localPosition = new Vector3(i * 1.25f, 3.35f, 0.42f);
                merlon.transform.localScale = new Vector3(0.75f, 0.65f, 0.28f);
                merlon.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
            }

            // 4. Wooden Parapet Walkway Platform along inner side
            GameObject walkway = GameObject.CreatePrimitive(PrimitiveType.Cube);
            walkway.name = "InnerWalkway";
            walkway.transform.SetParent(root.transform, false);
            walkway.transform.localPosition = new Vector3(0f, 2.65f, -0.35f);
            walkway.transform.localScale = new Vector3(3.95f, 0.22f, 0.85f);
            walkway.GetComponent<MeshRenderer>().sharedMaterial = woodMat;

            // Decorative Wooden Support Brackets underneath walkway
            for (int b = -1; b <= 1; b++)
            {
                GameObject bracket = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bracket.name = $"SupportBracket_{b + 2}";
                bracket.transform.SetParent(root.transform, false);
                bracket.transform.localPosition = new Vector3(b * 1.35f, 2.3f, -0.35f);
                bracket.transform.localScale = new Vector3(0.18f, 0.48f, 0.55f);
                bracket.transform.localRotation = Quaternion.Euler(-30f, 0f, 0f);
                bracket.GetComponent<MeshRenderer>().sharedMaterial = woodMat;
            }

            // 5. Compact Wooden Ladder / Stairs along the inner wall face
            GameObject stairsRoot = new GameObject("Stairs");
            stairsRoot.transform.SetParent(root.transform, false);
            stairsRoot.transform.localPosition = new Vector3(1.15f, 0f, -0.45f);

            int rungs = 7;
            for (int r = 0; r < rungs; r++)
            {
                float t = (float)r / (rungs - 1);
                GameObject step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.name = $"LadderRung_{r + 1}";
                step.transform.SetParent(stairsRoot.transform, false);
                step.transform.localPosition = new Vector3(0f, t * 2.5f + 0.15f, -(1.0f - t) * 0.45f);
                step.transform.localScale = new Vector3(0.95f, 0.12f, 0.22f);
                step.GetComponent<MeshRenderer>().sharedMaterial = woodMat;
            }

            // 6. Smooth Gentle Walkable Incline Ramp Collider (Fits cleanly within wall footprint)
            GameObject rampObj = new GameObject("StairsRampCollider");
            rampObj.transform.SetParent(stairsRoot.transform, false);
            rampObj.transform.localPosition = new Vector3(0f, 1.35f, -0.22f);
            rampObj.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);
            BoxCollider rampCol = rampObj.AddComponent<BoxCollider>();
            rampCol.size = new Vector3(1.05f, 0.20f, 2.85f);

            // 7. Climb Assist Trigger for effortless player ascent
            GameObject triggerObj = new GameObject("ClimbAssistTrigger");
            triggerObj.transform.SetParent(stairsRoot.transform, false);
            triggerObj.transform.localPosition = new Vector3(0f, 1.45f, -0.22f);
            triggerObj.transform.localRotation = Quaternion.Euler(65f, 0f, 0f);
            BoxCollider triggerCol = triggerObj.AddComponent<BoxCollider>();
            triggerCol.isTrigger = true;
            triggerCol.size = new Vector3(1.15f, 1.6f, 3.0f);
            triggerObj.AddComponent<CastleWallClimbTrigger>();

            // 8. Decorative Iron Wall Torch with Warm Ambient Glow
            GameObject torchObj = new GameObject("WallTorch");
            torchObj.transform.SetParent(root.transform, false);
            torchObj.transform.localPosition = new Vector3(-1.95f, 2.2f, 0.65f);

            GameObject sconce = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sconce.name = "TorchSconce";
            sconce.transform.SetParent(torchObj.transform, false);
            sconce.transform.localScale = new Vector3(0.08f, 0.22f, 0.08f);
            sconce.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            sconce.GetComponent<MeshRenderer>().sharedMaterial = ironMat;
            Collider sc = sconce.GetComponent<Collider>();
            if (sc != null) Object.DestroyImmediate(sc);

            GameObject fireLightObj = new GameObject("TorchLight");
            fireLightObj.transform.SetParent(torchObj.transform, false);
            fireLightObj.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            Light torchLight = fireLightObj.AddComponent<Light>();
            torchLight.type = LightType.Point;
            torchLight.color = new Color(1.0f, 0.72f, 0.28f);
            torchLight.intensity = 1.4f;
            torchLight.range = 6.0f;
            torchLight.shadows = LightShadows.Soft;

            // Remove any unwanted loose child colliders except the main structural and ramp colliders
            BoxCollider wallCol = root.AddComponent<BoxCollider>();
            wallCol.center = new Vector3(0f, 1.6f, 0.05f);
            wallCol.size = new Vector3(4.0f, 3.2f, 1.25f);

            foreach (var col in root.GetComponentsInChildren<Collider>())
            {
                if (col != wallCol && col != rampCol && col != triggerCol)
                {
                    DestroyImmediate(col);
                }
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
            else if (type == BuildingType.CastleWall)
            {
                ghost = CreateCastleWallPrefab("BuildGhost_CastleWall");
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

            return ghost;
        }
    }
}
