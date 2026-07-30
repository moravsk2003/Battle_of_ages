#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using BattleOfAges.Utils;
using BattleOfAges.RTS;
using BattleOfAges.Player;

namespace BattleOfAges.EditorTools
{
    public class SetupCharacterModelsTool
    {
        [MenuItem("Battle of Ages/Setup Player & NPC Character Models")]
        public static void SetupCharacterModels()
        {
            // 1. Setup Hero Player 3D Model
            GameObject playerObj = GameObject.Find("Player");
            if (playerObj != null)
            {
                float groundY = RtsUnit.GetTerrainHeight(playerObj.transform.position);
                playerObj.transform.position = new Vector3(playerObj.transform.position.x, groundY + 1.0f, playerObj.transform.position.z);

                MeshRenderer playerCapsuleRenderer = playerObj.GetComponent<MeshRenderer>();
                if (playerCapsuleRenderer != null) Object.DestroyImmediate(playerCapsuleRenderer);

                MeshFilter playerCapsuleFilter = playerObj.GetComponent<MeshFilter>();
                if (playerCapsuleFilter != null) Object.DestroyImmediate(playerCapsuleFilter);

                Transform existingModel = playerObj.transform.Find("Hero3DModel");
                if (existingModel != null) Object.DestroyImmediate(existingModel.gameObject);

                GameObject heroModel = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.HeroPlayer, "Hero3DModel");
                heroModel.transform.SetParent(playerObj.transform, false);
                heroModel.transform.localPosition = new Vector3(0, -1.0f, 0);

                FpsViewmodelSetup viewmodelSetup = playerObj.GetComponent<FpsViewmodelSetup>();
                if (viewmodelSetup != null)
                {
                    viewmodelSetup.SetWorldBodyRoot(heroModel.transform);
                }

                // Remove MapMarker Quad from Player
                Transform playerMarker = playerObj.transform.Find("MapMarker");
                if (playerMarker != null) Object.DestroyImmediate(playerMarker.gameObject);

                Undo.RegisterCreatedObjectUndo(heroModel, "Attach Hero 3D Model");
            }

            // Remove old SampleRtsUnit if present
            GameObject oldUnit = GameObject.Find("SampleRtsUnit");
            if (oldUnit != null) Object.DestroyImmediate(oldUnit);

            // 2. Setup NPC Humans on Map
            string[] npcNames = { "NPC_Knight_Guard_1", "NPC_Knight_Guard_2", "NPC_Villager_1", "NPC_Villager_2", "NPC_Commander" };
            Vector3[] spawnPositions = {
                new Vector3(12f, 0f, 8f),
                new Vector3(-15f, 0f, 10f),
                new Vector3(20f, 0f, -12f),
                new Vector3(-10f, 0f, -18f),
                new Vector3(5f, 0f, 22f)
            };
            HumanoidModelBuilder.HumanType[] npcTypes = {
                HumanoidModelBuilder.HumanType.KnightGuard,
                HumanoidModelBuilder.HumanType.KnightGuard,
                HumanoidModelBuilder.HumanType.Villager,
                HumanoidModelBuilder.HumanType.Villager,
                HumanoidModelBuilder.HumanType.KnightGuard
            };

            for (int i = 0; i < npcNames.Length; i++)
            {
                GameObject existingNpc = GameObject.Find(npcNames[i]);
                if (existingNpc != null) Object.DestroyImmediate(existingNpc);

                GameObject npcObj = new GameObject(npcNames[i]);
                Vector3 pos = spawnPositions[i];
                pos.y = RtsUnit.GetTerrainHeight(pos);
                npcObj.transform.position = pos;

                // Remove NavMeshAgent if present to avoid console warnings
                NavMeshAgent oldAgent = npcObj.GetComponent<NavMeshAgent>();
                if (oldAgent != null) Object.DestroyImmediate(oldAgent);

                // Add CharacterController for physical collision with tree trunks and objects
                CharacterController charController = npcObj.AddComponent<CharacterController>();
                charController.height = 2.0f;
                charController.center = new Vector3(0, 1.0f, 0);
                charController.radius = 0.45f;
                charController.stepOffset = 0.4f;
                charController.slopeLimit = 50.0f;

                RtsUnit unitComp = npcObj.AddComponent<RtsUnit>();
                npcObj.AddComponent<NpcWander>();

                // Build 3D Human Model
                GameObject npcModel = HumanoidModelBuilder.CreateHumanoidModel(npcTypes[i], "3DModel");
                npcModel.transform.SetParent(npcObj.transform, false);
                npcModel.transform.localPosition = Vector3.zero;

                // Create Selection Ring Indicator
                GameObject indicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                indicator.name = "SelectionIndicator";
                indicator.transform.SetParent(npcObj.transform, false);
                indicator.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                indicator.transform.localScale = new Vector3(1.8f, 0.02f, 1.8f);

                MeshCollider indCollider = indicator.GetComponent<MeshCollider>();
                if (indCollider != null) Object.DestroyImmediate(indCollider);

                MeshRenderer indRenderer = indicator.GetComponent<MeshRenderer>();
                if (indRenderer != null)
                {
                    indRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    indRenderer.receiveShadows = false;
                    
                    Shader unlitShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
                    Material indMat = new Material(unlitShader);
                    indMat.color = new Color(0.2f, 0.85f, 1.0f, 0.85f);
                    indMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    indMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;

                    indRenderer.material = indMat;
                }

                SerializedObject unitSerialized = new SerializedObject(unitComp);
                unitSerialized.FindProperty("selectionIndicator").objectReferenceValue = indicator;
                unitSerialized.ApplyModifiedProperties();

                indicator.SetActive(false);

                // Remove MapMarker Quad
                Transform npcMarker = npcObj.transform.Find("MapMarker");
                if (npcMarker != null) Object.DestroyImmediate(npcMarker.gameObject);

                Undo.RegisterCreatedObjectUndo(npcObj, "Create NPC Human");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

            Debug.Log("[Battle of Ages] Player & NPC Character Models configured with CharacterController physics!");
        }
    }
}
#endif
