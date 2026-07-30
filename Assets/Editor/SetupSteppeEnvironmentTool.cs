#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using BattleOfAges.Utils;
using BattleOfAges.Resources;
using BattleOfAges.UI;
using BattleOfAges.Player;
using BattleOfAges.RTS;
using BattleOfAges.Building;

namespace BattleOfAges.EditorTools
{
    public class SetupSteppeEnvironmentTool
    {
        [MenuItem("Battle of Ages/Setup Steppe Terrain & Trees")]
        public static void SetupSteppeEnvironment()
        {
            // 1. Setup ResourceManager, ResourceUI, InventoryUI & NpcGatherCommandUI
            GameObject mgrObj = GameObject.Find("GameManager");
            if (mgrObj == null)
            {
                mgrObj = new GameObject("GameManager");
            }
            if (mgrObj.GetComponent<ResourceManager>() == null)
            {
                mgrObj.AddComponent<ResourceManager>();
            }

            GameObject resUI = GameObject.Find("ResourceUI");
            if (resUI != null) Object.DestroyImmediate(resUI);
            resUI = new GameObject("ResourceUI");
            resUI.AddComponent<ResourceUI>();

            GameObject invUI = GameObject.Find("InventoryUI");
            if (invUI != null) Object.DestroyImmediate(invUI);
            invUI = new GameObject("InventoryUI");
            invUI.AddComponent<InventoryUI>();

            GameObject cmdUI = GameObject.Find("NpcGatherCommandUI");
            if (cmdUI != null) Object.DestroyImmediate(cmdUI);
            cmdUI = new GameObject("NpcGatherCommandUI");
            cmdUI.AddComponent<NpcGatherCommandUI>();

            // Setup Building System
            GameObject buildUI = GameObject.Find("BuildMenuUI");
            if (buildUI != null) Object.DestroyImmediate(buildUI);
            buildUI = new GameObject("BuildMenuUI");
            buildUI.AddComponent<BuildMenuUI>();

            if (mgrObj.GetComponent<BuildingPlacementSystem>() == null)
            {
                mgrObj.AddComponent<BuildingPlacementSystem>();
            }

            // 2. Setup Player Harvester
            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                if (player.GetComponent<PlayerResourceHarvester>() == null)
                {
                    player.AddComponent<PlayerResourceHarvester>();
                }
            }

            // 3. Remove old flat Ground plane & old SteppeTerrain
            GameObject oldGround = GameObject.Find("Ground");
            if (oldGround != null) Object.DestroyImmediate(oldGround);

            GameObject oldSteppe = GameObject.Find("SteppeTerrain");
            if (oldSteppe != null) Object.DestroyImmediate(oldSteppe);

            GameObject oldChest = GameObject.Find("StorageChest");
            if (oldChest != null) Object.DestroyImmediate(oldChest);

            // 4. Generate new rolling hill Steppe Terrain, Trees & Ore Pebbles
            GameObject steppeTerrain = SteppeTerrainGenerator.CreateSteppeTerrain("SteppeTerrain", 180f, 90);
            Undo.RegisterCreatedObjectUndo(steppeTerrain, "Generate Steppe Terrain");
            Physics.SyncTransforms();

            // 5. Create Storage Chest (Склад / Скриня) on the Map
            GameObject storageChest = StorageChestNode.CreateStorageChestPrefab("StorageChest");
            Vector3 chestTargetPos = new Vector3(3.0f, 0f, 5.0f);
            chestTargetPos.y = RtsUnit.GetTerrainHeight(chestTargetPos);
            storageChest.transform.position = chestTargetPos;

            // 6. Align Player with Steppe Ground height
            if (player != null)
            {
                float playerGroundY = RtsUnit.GetTerrainHeight(player.transform.position);
                player.transform.position = new Vector3(player.transform.position.x, playerGroundY + 1.0f, player.transform.position.z);
            }

            // 7. Setup NPCs & Assign Resource Gatherer AI to Villagers
            string[] npcNames = { "NPC_Knight_Guard_1", "NPC_Knight_Guard_2", "NPC_Villager_1", "NPC_Villager_2", "NPC_Commander" };
            Vector3[] spawnPositions = {
                new Vector3(12f, 0f, 8f),
                new Vector3(-15f, 0f, 10f),
                new Vector3(6f, 0f, -8f),
                new Vector3(-6f, 0f, -10f),
                new Vector3(5f, 0f, 22f)
            };

            for (int i = 0; i < npcNames.Length; i++)
            {
                GameObject npc = GameObject.Find(npcNames[i]);
                if (npc != null)
                {
                    Vector3 pos = spawnPositions[i];
                    pos.y = RtsUnit.GetTerrainHeight(pos);
                    npc.transform.position = pos;

                    // Attach NpcResourceGatherer AI to Villager NPCs
                    if (npcNames[i].Contains("Villager"))
                    {
                        if (npc.GetComponent<NpcResourceGatherer>() == null)
                        {
                            npc.AddComponent<NpcResourceGatherer>();
                        }
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log("[Battle of Ages] Master Storage Chest & NPC Resource Workers configured successfully!");
        }
    }
}
#endif
