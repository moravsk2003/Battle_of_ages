using UnityEngine;
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

        public BuildingType Type => buildingType;

        public void OnBuilt()
        {
            BuildingDefinition def = BuildingData.GetDefinition(buildingType);

            for (int i = 0; i < def.NpcSpawnCount; i++)
            {
                SpawnWorkerNpc(i);
            }

            Debug.Log($"[Building] {def.DisplayName} built at {transform.position}! Spawned {def.NpcSpawnCount} worker(s).");
        }

        private void SpawnWorkerNpc(int index)
        {
            Vector3 spawnPos = transform.position + transform.forward * 3.5f + transform.right * (index * 1.5f);
            spawnPos.y = RtsUnit.GetTerrainHeight(spawnPos);

            int workerNumber = WorkerRegistry.WorkerCount + 1;
            string npcName = $"NPC_Villager_{workerNumber}";

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

            Debug.Log($"[Building] Spawned worker: {npcName} at {spawnPos}");
        }

        public static GameObject CreateWorkerHousePrefab(string name = "WorkerHouse")
        {
            GameObject root = new GameObject(name);
            root.AddComponent<BuildingNode>();

            if (cachedLitShader == null)
            {
                cachedLitShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            }

            // 1. Floor
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            floor.transform.localScale = new Vector3(3.2f, 0.1f, 3.2f);
            Material floorMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.34f, 0.27f, 0.16f), 0f, 0.18f);
            floor.GetComponent<MeshRenderer>().material = floorMat;

            // 2. Walls
            Material wallMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.64f, 0.47f, 0.24f), 0f, 0.25f);
            Material beamMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Bark, new Color(0.22f, 0.105f, 0.035f), 0f, 0.18f);

            GameObject backWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backWall.name = "WallBack";
            backWall.transform.SetParent(root.transform, false);
            backWall.transform.localPosition = new Vector3(0f, 1.25f, -1.5f);
            backWall.transform.localScale = new Vector3(3.0f, 2.5f, 0.15f);
            backWall.GetComponent<MeshRenderer>().material = wallMat;

            GameObject leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWall.name = "WallLeft";
            leftWall.transform.SetParent(root.transform, false);
            leftWall.transform.localPosition = new Vector3(-1.425f, 1.25f, 0f);
            leftWall.transform.localScale = new Vector3(0.15f, 2.5f, 3.0f);
            leftWall.GetComponent<MeshRenderer>().material = wallMat;

            GameObject rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWall.name = "WallRight";
            rightWall.transform.SetParent(root.transform, false);
            rightWall.transform.localPosition = new Vector3(1.425f, 1.25f, 0f);
            rightWall.transform.localScale = new Vector3(0.15f, 2.5f, 3.0f);
            rightWall.GetComponent<MeshRenderer>().material = wallMat;

            GameObject frontLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frontLeft.name = "WallFrontLeft";
            frontLeft.transform.SetParent(root.transform, false);
            frontLeft.transform.localPosition = new Vector3(-0.85f, 1.25f, 1.5f);
            frontLeft.transform.localScale = new Vector3(1.3f, 2.5f, 0.15f);
            frontLeft.GetComponent<MeshRenderer>().material = wallMat;

            GameObject frontRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frontRight.name = "WallFrontRight";
            frontRight.transform.SetParent(root.transform, false);
            frontRight.transform.localPosition = new Vector3(0.85f, 1.25f, 1.5f);
            frontRight.transform.localScale = new Vector3(1.3f, 2.5f, 0.15f);
            frontRight.GetComponent<MeshRenderer>().material = wallMat;

            GameObject frontTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frontTop.name = "WallFrontTop";
            frontTop.transform.SetParent(root.transform, false);
            frontTop.transform.localPosition = new Vector3(0f, 2.1f, 1.5f);
            frontTop.transform.localScale = new Vector3(0.4f, 0.8f, 0.15f);
            frontTop.GetComponent<MeshRenderer>().material = wallMat;

            // Visible structural beams make the cottage read as timber-frame rather than a plain box.
            GameObject beamFrontLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beamFrontLeft.name = "FrameFrontLeft";
            beamFrontLeft.transform.SetParent(root.transform, false);
            beamFrontLeft.transform.localPosition = new Vector3(-1.38f, 1.28f, 1.59f);
            beamFrontLeft.transform.localScale = new Vector3(0.14f, 2.55f, 0.12f);
            beamFrontLeft.GetComponent<MeshRenderer>().sharedMaterial = beamMat;

            GameObject beamFrontRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beamFrontRight.name = "FrameFrontRight";
            beamFrontRight.transform.SetParent(root.transform, false);
            beamFrontRight.transform.localPosition = new Vector3(1.38f, 1.28f, 1.59f);
            beamFrontRight.transform.localScale = new Vector3(0.14f, 2.55f, 0.12f);
            beamFrontRight.GetComponent<MeshRenderer>().sharedMaterial = beamMat;

            GameObject beamTop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beamTop.name = "FrameFrontTop";
            beamTop.transform.SetParent(root.transform, false);
            beamTop.transform.localPosition = new Vector3(0f, 2.42f, 1.59f);
            beamTop.transform.localScale = new Vector3(2.95f, 0.14f, 0.12f);
            beamTop.GetComponent<MeshRenderer>().sharedMaterial = beamMat;

            // 3. Door frame
            GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "Door";
            door.transform.SetParent(root.transform, false);
            door.transform.localPosition = new Vector3(0f, 0.85f, 1.52f);
            door.transform.localScale = new Vector3(0.7f, 1.7f, 0.08f);
            Material doorMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.18f, 0.075f, 0.025f), 0f, 0.22f);
            door.GetComponent<MeshRenderer>().material = doorMat;

            // 4. Roof
            Material roofMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.RoofTile, new Color(0.50f, 0.09f, 0.045f), 0f, 0.26f);

            GameObject roofLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roofLeft.name = "RoofLeft";
            roofLeft.transform.SetParent(root.transform, false);
            roofLeft.transform.localPosition = new Vector3(-0.85f, 2.85f, 0f);
            roofLeft.transform.localScale = new Vector3(2.0f, 0.12f, 3.6f);
            roofLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 25f);
            roofLeft.GetComponent<MeshRenderer>().material = roofMat;

            GameObject roofRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roofRight.name = "RoofRight";
            roofRight.transform.SetParent(root.transform, false);
            roofRight.transform.localPosition = new Vector3(0.85f, 2.85f, 0f);
            roofRight.transform.localScale = new Vector3(2.0f, 0.12f, 3.6f);
            roofRight.transform.localRotation = Quaternion.Euler(0f, 0f, -25f);
            roofRight.GetComponent<MeshRenderer>().material = roofMat;

            // 5. Chimney
            GameObject chimney = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chimney.name = "Chimney";
            chimney.transform.SetParent(root.transform, false);
            chimney.transform.localPosition = new Vector3(0.8f, 3.4f, -0.8f);
            chimney.transform.localScale = new Vector3(0.35f, 0.8f, 0.35f);
            Material chimneyMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.34f, 0.25f, 0.20f), 0f, 0.18f);
            chimney.GetComponent<MeshRenderer>().material = chimneyMat;

            // 6. Root collider
            BoxCollider col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 1.5f, 0f);
            col.size = new Vector3(3.5f, 3.2f, 3.5f);

            foreach (var childCol in root.GetComponentsInChildren<Collider>())
            {
                if (childCol != col) DestroyImmediate(childCol);
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

            if (ghost == null) return null;

            BuildingNode bNode = ghost.GetComponent<BuildingNode>();
            if (bNode != null) DestroyImmediate(bNode);

            StorageChestNode cNode = ghost.GetComponent<StorageChestNode>();
            if (cNode != null) DestroyImmediate(cNode);

            MineNode mNode = ghost.GetComponent<MineNode>();
            if (mNode != null) DestroyImmediate(mNode);

            foreach (var col in ghost.GetComponentsInChildren<Collider>())
            {
                DestroyImmediate(col);
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
