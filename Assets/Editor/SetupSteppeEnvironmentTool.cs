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
using BattleOfAges.Crafting;
using BattleOfAges.World;

namespace BattleOfAges.EditorTools
{
    public class SetupSteppeEnvironmentTool
    {
        [MenuItem("Battle of Ages/Setup Steppe Terrain & Trees")]
        public static void SetupSteppeEnvironment()
        {
            string scenePath = "Assets/Scenes/SampleScene.unity";
            if (System.IO.File.Exists(scenePath) && EditorSceneManager.GetActiveScene().path != scenePath)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }

            ProceduralVisualMaterials.ClearCache();

            // 1. Setup ResourceManager, UI Components
            GameObject mgrObj = GameObject.Find("GameManager");
            if (mgrObj == null) mgrObj = new GameObject("GameManager");
            if (mgrObj.GetComponent<ResourceManager>() == null) mgrObj.AddComponent<ResourceManager>();

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

            GameObject buildUI = GameObject.Find("BuildMenuUI");
            if (buildUI != null) Object.DestroyImmediate(buildUI);
            buildUI = new GameObject("BuildMenuUI");
            buildUI.AddComponent<BuildMenuUI>();

            GameObject wbUI = GameObject.Find("WorkbenchUI");
            if (wbUI != null) Object.DestroyImmediate(wbUI);
            wbUI = new GameObject("WorkbenchUI");
            wbUI.AddComponent<WorkbenchUI>();

            if (mgrObj.GetComponent<BuildingPlacementSystem>() == null) mgrObj.AddComponent<BuildingPlacementSystem>();

            // 2. Clean old terrain & objects
            GameObject oldGround = GameObject.Find("Ground");
            if (oldGround != null) Object.DestroyImmediate(oldGround);

            GameObject oldSteppe = GameObject.Find("SteppeTerrain");
            if (oldSteppe != null) Object.DestroyImmediate(oldSteppe);

            GameObject oldChest = GameObject.Find("StorageChest");
            if (oldChest != null) Object.DestroyImmediate(oldChest);

            GameObject oldCampfire = GameObject.Find("Campfire");
            if (oldCampfire != null) Object.DestroyImmediate(oldCampfire);

            GameObject oldWall = GameObject.Find("FortressWall_1");
            if (oldWall != null) Object.DestroyImmediate(oldWall);

            GameObject oldWb = GameObject.Find("BaseWorkbench");
            if (oldWb != null) Object.DestroyImmediate(oldWb);

            GameObject oldSample = GameObject.Find("SampleRtsUnit");
            if (oldSample != null) Object.DestroyImmediate(oldSample);

            // 3. Generate new Rolling Hill Steppe Terrain with 3D Grass Tufts, Wildflowers, Barrels & Trees
            GameObject steppeTerrain = SteppeTerrainGenerator.CreateSteppeTerrain("SteppeTerrain", 192f, 96);
            Undo.RegisterCreatedObjectUndo(steppeTerrain, "Generate Steppe Terrain");
            Physics.SyncTransforms();

            // 4. Create Ornate Storage Chest
            GameObject storageChest = StorageChestNode.CreateStorageChestPrefab("StorageChest");
            storageChest.transform.position = new Vector3(3.5f, 0f, 3.5f);
            storageChest.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
            storageChest.GetComponent<StorageChestNode>().SnapToTerrain();

            // 5. Create Cozy Medieval Campfire with Fire VFX
            CreateCampfire(new Vector3(0.5f, 0f, 3.2f));

            // 6. Create Stylized Castle Wall Fortification
            GameObject fortressWall = BuildingNode.CreateCastleWallPrefab("FortressWall_1");
            Vector3 wallPos = new Vector3(-6.5f, 0f, 5.0f);
            wallPos.y = RtsUnit.GetTerrainHeight(wallPos);
            fortressWall.transform.position = wallPos;
            fortressWall.transform.rotation = Quaternion.Euler(0f, 30f, 0f);

            // 7. Create Crafting Workbench
            GameObject workbench = WorkbenchNode.CreateWorkbenchPrefab("BaseWorkbench");
            Vector3 wbPos = new Vector3(4.8f, 0f, -3.2f);
            wbPos.y = RtsUnit.GetTerrainHeight(wbPos);
            workbench.transform.position = wbPos;
            workbench.transform.rotation = Quaternion.Euler(0f, -25f, 0f);

            // 8. Setup Player Character & 3D Hero Model
            GameObject player = GameObject.Find("Player");
            if (player != null)
            {
                float playerGroundY = RtsUnit.GetTerrainHeight(player.transform.position);
                player.transform.position = new Vector3(player.transform.position.x, playerGroundY + 1.0f, player.transform.position.z);

                MeshRenderer pMr = player.GetComponent<MeshRenderer>();
                if (pMr != null) Object.DestroyImmediate(pMr);
                MeshFilter pMf = player.GetComponent<MeshFilter>();
                if (pMf != null) Object.DestroyImmediate(pMf);

                Transform oldHero = player.transform.Find("Hero3DModel");
                if (oldHero != null) Object.DestroyImmediate(oldHero.gameObject);

                GameObject heroModel = HumanoidModelBuilder.CreateHumanoidModel(HumanoidModelBuilder.HumanType.HeroPlayer, "Hero3DModel");
                heroModel.transform.SetParent(player.transform, false);
                heroModel.transform.localPosition = new Vector3(0, -1.0f, 0);

                if (player.GetComponent<PlayerResourceHarvester>() == null) player.AddComponent<PlayerResourceHarvester>();
                if (player.GetComponent<BattleOfAges.Combat.HealthComponent>() == null) player.AddComponent<BattleOfAges.Combat.HealthComponent>();
                if (player.GetComponent<BattleOfAges.Combat.CombatUnit>() == null) player.AddComponent<BattleOfAges.Combat.CombatUnit>();
                if (player.GetComponent<PlayerCombatController>() == null) player.AddComponent<PlayerCombatController>();

                player.GetComponent<BattleOfAges.Combat.HealthComponent>().InitializeHealth(500);
                player.GetComponent<BattleOfAges.Combat.CombatUnit>().InitializeCombatStats(50, 2.0f, 3.5f);
            }

            // 9. Setup Stylized NPCs (Guards & Villagers)
            string[] npcNames = { "NPC_Knight_Guard_1", "NPC_Knight_Guard_2", "NPC_Villager_1", "NPC_Villager_2", "NPC_Commander" };
            Vector3[] spawnPositions = {
                new Vector3(10f, 0f, 8f),
                new Vector3(-12f, 0f, 9f),
                new Vector3(6f, 0f, -7f),
                new Vector3(-6f, 0f, -9f),
                new Vector3(4f, 0f, 18f)
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
                GameObject npc = GameObject.Find(npcNames[i]);
                if (npc == null) npc = new GameObject(npcNames[i]);

                Vector3 pos = spawnPositions[i];
                pos.y = RtsUnit.GetTerrainHeight(pos);
                npc.transform.position = pos;

                for (int c = npc.transform.childCount - 1; c >= 0; c--)
                {
                    Transform child = npc.transform.GetChild(c);
                    if (child.name != "SelectionIndicator") Object.DestroyImmediate(child.gameObject);
                }

                CharacterController cc = npc.GetComponent<CharacterController>();
                if (cc == null) cc = npc.AddComponent<CharacterController>();
                cc.height = 2.0f;
                cc.center = new Vector3(0, 1.0f, 0);
                cc.radius = 0.45f;

                RtsUnit rtsUnit = npc.GetComponent<RtsUnit>();
                if (rtsUnit == null) rtsUnit = npc.AddComponent<RtsUnit>();

                if (npc.GetComponent<NpcWander>() == null) npc.AddComponent<NpcWander>();

                GameObject model = HumanoidModelBuilder.CreateHumanoidModel(npcTypes[i], "VisualModel");
                model.transform.SetParent(npc.transform, false);
                model.transform.localPosition = Vector3.zero;

                Transform selInd = npc.transform.Find("SelectionIndicator");
                if (selInd == null)
                {
                    GameObject ind = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    ind.name = "SelectionIndicator";
                    ind.transform.SetParent(npc.transform, false);
                    ind.transform.localPosition = new Vector3(0f, 0.1f, 0f);
                    ind.transform.localScale = new Vector3(1.6f, 0.02f, 1.6f);
                    Collider c = ind.GetComponent<Collider>();
                    if (c != null) Object.DestroyImmediate(c);
                    ind.SetActive(false);
                }

                BattleOfAges.Combat.HealthComponent hp = npc.GetComponent<BattleOfAges.Combat.HealthComponent>();
                if (hp == null) hp = npc.AddComponent<BattleOfAges.Combat.HealthComponent>();

                BattleOfAges.Combat.CombatUnit cu = npc.GetComponent<BattleOfAges.Combat.CombatUnit>();
                if (cu == null) cu = npc.AddComponent<BattleOfAges.Combat.CombatUnit>();

                if (npc.GetComponent<NpcAnimationController>() == null) npc.AddComponent<NpcAnimationController>();

                if (npcTypes[i] == HumanoidModelBuilder.HumanType.KnightGuard)
                {
                    hp.InitializeHealth(100);
                    cu.InitializeCombatStats(10, 2.0f, 2.8f);
                }
                else
                {
                    hp.InitializeHealth(20);
                    cu.InitializeCombatStats(2, 2.0f, 2.5f);
                    if (npc.GetComponent<NpcResourceGatherer>() == null) npc.AddComponent<NpcResourceGatherer>();
                }
            }

            // 10. Create Stylized Martial Training Dummy Target Mobs
            GameObject oldDummy1 = GameObject.Find("DummyTarget_1");
            if (oldDummy1 != null) Object.DestroyImmediate(oldDummy1);
            GameObject oldDummy2 = GameObject.Find("DummyTarget_2");
            if (oldDummy2 != null) Object.DestroyImmediate(oldDummy2);

            CreateStylizedDummyTarget("DummyTarget_1", new Vector3(14f, 0f, 14f));
            CreateStylizedDummyTarget("DummyTarget_2", new Vector3(-12f, 0f, 16f));

            // 11. Apply AAA Lighting & Post Processing
            SetupLightingAndPostProcessingTool.SetupLightingAndPostProcessing();

            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            }

            Debug.Log("[Battle of Ages] Full AAA Environment, Castle Wall, Workbench, Characters & Lighting refreshed!");
        }

        private static void CreateCampfire(Vector3 pos)
        {
            GameObject campfire = new GameObject("Campfire");
            pos.y = RtsUnit.GetTerrainHeight(pos);
            campfire.transform.position = pos;

            Material stoneMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.40f, 0.38f, 0.36f), 0.05f, 0.25f);
            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.22f, 0.12f, 0.05f), 0.0f, 0.22f);
            Material coalMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.12f, 0.10f, 0.08f), 0.1f, 0.15f);

            int stoneCount = 8;
            float ringRadius = 0.75f;
            for (int i = 0; i < stoneCount; i++)
            {
                float angle = (float)i / stoneCount * Mathf.PI * 2f;
                Vector3 stonePos = new Vector3(Mathf.Cos(angle) * ringRadius, 0.12f, Mathf.Sin(angle) * ringRadius);
                GameObject stone = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                stone.name = $"FireStone_{i}";
                stone.transform.SetParent(campfire.transform, false);
                stone.transform.localPosition = stonePos;
                stone.transform.localScale = new Vector3(0.32f, 0.22f, 0.30f);
                stone.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;
                Collider c = stone.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }

            GameObject coal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            coal.name = "CharcoalBed";
            coal.transform.SetParent(campfire.transform, false);
            coal.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            coal.transform.localScale = new Vector3(1.1f, 0.08f, 1.1f);
            coal.GetComponent<MeshRenderer>().sharedMaterial = coalMat;
            Collider cc = coal.GetComponent<Collider>();
            if (cc != null) Object.DestroyImmediate(cc);

            for (int l = 0; l < 4; l++)
            {
                float logAngle = l * 45f;
                GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                log.name = $"FireLog_{l}";
                log.transform.SetParent(campfire.transform, false);
                log.transform.localPosition = new Vector3(0f, 0.16f + l * 0.04f, 0f);
                log.transform.localRotation = Quaternion.Euler(15f, logAngle, 0f);
                log.transform.localScale = new Vector3(0.12f, 0.45f, 0.12f);
                log.GetComponent<MeshRenderer>().sharedMaterial = woodMat;
                Collider lc = log.GetComponent<Collider>();
                if (lc != null) Object.DestroyImmediate(lc);
            }

            GameObject lightObj = new GameObject("FireLight");
            lightObj.transform.SetParent(campfire.transform, false);
            lightObj.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            Light pLight = lightObj.AddComponent<Light>();
            pLight.type = LightType.Point;
            pLight.color = new Color(1.0f, 0.65f, 0.22f);
            pLight.intensity = 2.4f;
            pLight.range = 8.0f;
            pLight.shadows = LightShadows.Soft;

            GameObject fireParticlesObj = new GameObject("FireParticles");
            fireParticlesObj.transform.SetParent(campfire.transform, false);
            fireParticlesObj.transform.localPosition = new Vector3(0f, 0.25f, 0f);

            ParticleSystem ps = fireParticlesObj.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.2f;
            main.startSpeed = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startColor = new Color(1.0f, 0.55f, 0.10f, 0.85f);
            main.maxParticles = 60;

            var emission = ps.emission;
            emission.rateOverTime = 25f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.22f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.4f);
            sizeCurve.AddKey(0.4f, 1.0f);
            sizeCurve.AddKey(1.0f, 0f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystemRenderer psr = fireParticlesObj.GetComponent<ParticleSystemRenderer>();
            Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            Material fireMat = new Material(pShader);
            fireMat.SetColor("_BaseColor", new Color(1.0f, 0.6f, 0.1f, 0.9f));
            psr.sharedMaterial = fireMat;
        }

        private static void CreateStylizedDummyTarget(string name, Vector3 pos)
        {
            GameObject dummy = new GameObject(name);
            pos.y = RtsUnit.GetTerrainHeight(pos);
            dummy.transform.position = pos;

            CapsuleCollider collider = dummy.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 1.1f, 0f);
            collider.height = 2.2f;
            collider.radius = 0.55f;

            GameObject visual = new GameObject("VisualModel");
            visual.transform.SetParent(dummy.transform, false);

            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.35f, 0.22f, 0.10f), 0f, 0.25f);
            Material targetRed = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, new Color(0.85f, 0.15f, 0.15f), 0f, 0.28f);
            Material targetWhite = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Cloth, new Color(0.92f, 0.90f, 0.82f), 0f, 0.25f);
            Material strawMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Grass, new Color(0.82f, 0.72f, 0.32f), 0f, 0.22f);
            Material ropeMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Rope, new Color(0.65f, 0.52f, 0.32f), 0f, 0.20f);

            CreatePart(PrimitiveType.Cylinder, "CentralPost", visual.transform, new Vector3(0f, 1.05f, 0f), new Vector3(0.32f, 1.05f, 0.32f), Quaternion.identity, woodMat);
            CreatePart(PrimitiveType.Cube, "CrossBeam", visual.transform, new Vector3(0f, 1.45f, 0f), new Vector3(1.35f, 0.18f, 0.22f), Quaternion.identity, woodMat);
            CreatePart(PrimitiveType.Cube, "RopeBinding", visual.transform, new Vector3(0f, 1.45f, 0f), new Vector3(0.38f, 0.28f, 0.36f), Quaternion.identity, ropeMat);
            CreatePart(PrimitiveType.Cylinder, "TargetOuterRing", visual.transform, new Vector3(0f, 1.25f, 0.16f), new Vector3(0.85f, 0.06f, 0.85f), Quaternion.Euler(90f, 0f, 0f), targetRed);
            CreatePart(PrimitiveType.Cylinder, "TargetMidRing", visual.transform, new Vector3(0f, 1.25f, 0.19f), new Vector3(0.55f, 0.07f, 0.55f), Quaternion.Euler(90f, 0f, 0f), targetWhite);
            CreatePart(PrimitiveType.Cylinder, "TargetBullsEye", visual.transform, new Vector3(0f, 1.25f, 0.22f), new Vector3(0.25f, 0.08f, 0.25f), Quaternion.Euler(90f, 0f, 0f), targetRed);
            CreatePart(PrimitiveType.Sphere, "DummyHead", visual.transform, new Vector3(0f, 2.15f, 0f), new Vector3(0.48f, 0.52f, 0.48f), Quaternion.identity, strawMat);
            CreatePart(PrimitiveType.Cylinder, "NeckRope", visual.transform, new Vector3(0f, 1.88f, 0f), new Vector3(0.36f, 0.08f, 0.36f), Quaternion.identity, ropeMat);

            dummy.AddComponent<BattleOfAges.Combat.HealthComponent>().InitializeHealth(100);
            dummy.AddComponent<BattleOfAges.Combat.DummyTargetNode>();
            dummy.AddComponent<BattleOfAges.Combat.DummyWiggleAnimation>();
        }

        private static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Quaternion localRot, Material mat)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRot;

            MeshRenderer mr = part.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;

            Collider c = part.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);

            return part;
        }
    }
}
#endif
