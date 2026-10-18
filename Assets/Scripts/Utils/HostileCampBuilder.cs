using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.AI;
using BattleOfAges.Combat;
using BattleOfAges.RTS;

namespace BattleOfAges.Utils
{
    /// <summary>
    /// Procedural 3D Builder for Hostile Raider Camps, Outposts, and Mob Visuals.
    /// Generates camp structures (Campfire, Tents, Spiked Barricades, War Standards) and spawns hostile squads.
    /// </summary>
    public static class HostileCampBuilder
    {
        public static GameObject CreateHostileCamp(Vector3 campCenter, string name = "HostileRaiderCamp")
        {
            GameObject root = new GameObject(name);
            IEnumerator steps = BuildHostileCamp(root, campCenter);
            while (steps.MoveNext()) { }
            return root;
        }

        /// <summary>
        /// Builds the camp under root one piece per step (structures, the warlord, then each raider), so streamed chunks
        /// can spread it over frames. The root stays inactive until the last step has given the warlord his squad.
        /// </summary>
        public static IEnumerator BuildHostileCamp(GameObject root, Vector3 campCenter)
        {
            root.SetActive(false);
            root.transform.position = campCenter;

            Material woodMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Wood, new Color(0.35f, 0.22f, 0.12f), 0f, 0.25f);
            Material clothMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Dark, new Color(0.55f, 0.18f, 0.14f), 0f, 0.20f); // Crimson cloth
            Material stoneMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Stone, new Color(0.35f, 0.35f, 0.38f), 0f, 0.20f);
            Material fireMat = ProceduralVisualMaterials.Get(ProceduralVisualMaterials.Surface.Gold, new Color(1.0f, 0.45f, 0.10f), 0.8f, 0.8f);

            // 1. Central Campfire
            GameObject fireRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fireRing.name = "CampfireRing";
            fireRing.transform.SetParent(root.transform, false);
            fireRing.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            fireRing.transform.localScale = new Vector3(1.8f, 0.2f, 1.8f);
            fireRing.GetComponent<MeshRenderer>().sharedMaterial = stoneMat;

            GameObject fireEmber = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fireEmber.name = "FireEmbers";
            fireEmber.transform.SetParent(root.transform, false);
            fireEmber.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            fireEmber.transform.localScale = new Vector3(0.9f, 0.6f, 0.9f);
            fireEmber.GetComponent<MeshRenderer>().sharedMaterial = fireMat;

            // 2. Three Raider Tents
            Vector3[] tentPositions = new Vector3[]
            {
                new Vector3(-4.5f, 0f, 2.5f),
                new Vector3(4.5f, 0f, 3.0f),
                new Vector3(0f, 0f, -5.0f)
            };
            float[] tentYaws = new float[] { 45f, -50f, 180f };

            for (int i = 0; i < tentPositions.Length; i++)
            {
                BuildTent(root.transform, tentPositions[i], tentYaws[i], woodMat, clothMat);
            }

            // 3. Spiked Barricades
            Vector3[] barricadePositions = new Vector3[]
            {
                new Vector3(-6.5f, 0f, -2.5f),
                new Vector3(6.5f, 0f, -2.0f),
                new Vector3(-2.5f, 0f, 6.0f),
                new Vector3(2.5f, 0f, 6.0f)
            };

            for (int i = 0; i < barricadePositions.Length; i++)
            {
                BuildSpikeBarricade(root.transform, barricadePositions[i], woodMat);
            }

            // 4. War Standard / Skull Totem
            BuildWarBanner(root.transform, new Vector3(1.8f, 0f, 1.8f), woodMat, clothMat);
            yield return null;

            // 5. Spawn Warlord (Leader) & Raider Squad
            GameObject leaderObj = CreateHostileMob(HostileMobType.WarlordGuard, "Warlord_Leader");
            leaderObj.transform.position = campCenter + new Vector3(0f, 0f, 1.5f);
            leaderObj.transform.position = new Vector3(leaderObj.transform.position.x, RtsUnit.GetTerrainHeight(leaderObj.transform.position), leaderObj.transform.position.z);
            leaderObj.transform.SetParent(root.transform);
            leaderObj.GetComponent<HostileMobAI>().SetGuardOrigin(leaderObj.transform.position);

            HostileSettlementLeader leaderComp = leaderObj.GetComponent<HostileSettlementLeader>();
            yield return null;

            List<HostileMobAI> squad = new List<HostileMobAI>();
            Vector3[] squadOffsets = new Vector3[]
            {
                new Vector3(-2.5f, 0f, -1.0f),
                new Vector3(2.5f, 0f, -1.0f),
                new Vector3(-1.5f, 0f, 3.5f),
                new Vector3(1.5f, 0f, 3.5f)
            };

            for (int i = 0; i < squadOffsets.Length; i++)
            {
                HostileMobType type = (i % 2 == 0) ? HostileMobType.Bandit : HostileMobType.Goblin;
                GameObject mobObj = CreateHostileMob(type, $"Raider_{i + 1}");
                Vector3 spawnPos = campCenter + squadOffsets[i];
                spawnPos.y = RtsUnit.GetTerrainHeight(spawnPos);
                mobObj.transform.position = spawnPos;
                mobObj.transform.SetParent(root.transform);

                HostileMobAI mobAI = mobObj.GetComponent<HostileMobAI>();
                if (mobAI != null)
                {
                    mobAI.SetGuardOrigin(spawnPos);
                    squad.Add(mobAI);
                }
                yield return null;
            }

            if (leaderComp != null)
            {
                leaderComp.InitializeSquad(squad);
            }
            root.SetActive(true);
        }

        private static void BuildTent(Transform parent, Vector3 localPos, float yaw, Material woodMat, Material clothMat)
        {
            GameObject tentRoot = new GameObject("RaiderTent");
            tentRoot.transform.SetParent(parent, false);
            tentRoot.transform.localPosition = localPos;
            tentRoot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // One solid volume for the whole tent: the A-frame roof plates alone leave an open tunnel that
            // units can walk into and get wedged in (AI movement has no pathfinding).
            BoxCollider tentVolume = tentRoot.AddComponent<BoxCollider>();
            tentVolume.center = new Vector3(0f, 1.15f, 0f);
            tentVolume.size = new Vector3(3.6f, 2.3f, 3.4f);

            // Tent Triangular Roof
            GameObject leftRoof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftRoof.transform.SetParent(tentRoot.transform, false);
            leftRoof.transform.localPosition = new Vector3(-1.0f, 1.3f, 0f);
            leftRoof.transform.localScale = new Vector3(0.12f, 2.5f, 3.2f);
            leftRoof.transform.localRotation = Quaternion.Euler(0f, 0f, -40f);
            leftRoof.GetComponent<MeshRenderer>().sharedMaterial = clothMat;

            GameObject rightRoof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightRoof.transform.SetParent(tentRoot.transform, false);
            rightRoof.transform.localPosition = new Vector3(1.0f, 1.3f, 0f);
            rightRoof.transform.localScale = new Vector3(0.12f, 2.5f, 3.2f);
            rightRoof.transform.localRotation = Quaternion.Euler(0f, 0f, 40f);
            rightRoof.GetComponent<MeshRenderer>().sharedMaterial = clothMat;

            // Ridge Pole
            GameObject ridge = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ridge.transform.SetParent(tentRoot.transform, false);
            ridge.transform.localPosition = new Vector3(0f, 2.15f, 0f);
            ridge.transform.localScale = new Vector3(0.12f, 1.8f, 0.12f);
            ridge.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ridge.GetComponent<MeshRenderer>().sharedMaterial = woodMat;
        }

        private static void BuildSpikeBarricade(Transform parent, Vector3 localPos, Material woodMat)
        {
            GameObject barricade = new GameObject("SpikeBarricade");
            barricade.transform.SetParent(parent, false);
            barricade.transform.localPosition = localPos;

            for (int i = -1; i <= 1; i++)
            {
                GameObject spike = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                spike.transform.SetParent(barricade.transform, false);
                spike.transform.localPosition = new Vector3(i * 0.7f, 0.85f, 0f);
                spike.transform.localScale = new Vector3(0.18f, 1.1f, 0.18f);
                spike.transform.localRotation = Quaternion.Euler(30f, i * 15f, 0f);
                spike.GetComponent<MeshRenderer>().sharedMaterial = woodMat;
            }
        }

        private static void BuildWarBanner(Transform parent, Vector3 localPos, Material woodMat, Material clothMat)
        {
            GameObject bannerObj = new GameObject("WarStandard");
            bannerObj.transform.SetParent(parent, false);
            bannerObj.transform.localPosition = localPos;

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.transform.SetParent(bannerObj.transform, false);
            pole.transform.localPosition = new Vector3(0f, 2.0f, 0f);
            pole.transform.localScale = new Vector3(0.1f, 2.0f, 0.1f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = woodMat;

            GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flag.transform.SetParent(bannerObj.transform, false);
            flag.transform.localPosition = new Vector3(0.6f, 3.2f, 0f);
            flag.transform.localScale = new Vector3(1.2f, 1.0f, 0.05f);
            flag.GetComponent<MeshRenderer>().sharedMaterial = clothMat;
        }

        public static GameObject CreateHostileMob(HostileMobType type, string name = "HostileMob")
        {
            GameObject root = new GameObject(name);

            CharacterController charCtrl = root.AddComponent<CharacterController>();
            charCtrl.height = (type == HostileMobType.Goblin) ? 1.4f : 2.0f;
            charCtrl.radius = 0.5f;
            charCtrl.center = new Vector3(0f, charCtrl.height * 0.5f, 0f);

            root.AddComponent<HealthComponent>();
            root.AddComponent<CombatUnit>();

            // HP / damage / speed come from HostileMobAI.InitializeMob (single source of truth).
            // The leader's HostileMobAI is auto-added by [RequireComponent] on HostileSettlementLeader.
            if (type == HostileMobType.WarlordGuard)
            {
                root.AddComponent<HostileSettlementLeader>();
            }

            HostileMobAI mobAI = root.GetComponent<HostileMobAI>();
            if (mobAI == null) mobAI = root.AddComponent<HostileMobAI>();
            mobAI.InitializeMob(type);

            HumanoidModelBuilder.HumanType humanType = (type == HostileMobType.WarlordGuard) ? HumanoidModelBuilder.HumanType.Warlord
                : (type == HostileMobType.Goblin ? HumanoidModelBuilder.HumanType.Goblin : HumanoidModelBuilder.HumanType.Bandit);
            GameObject visual = HumanoidModelBuilder.CreateHumanoidModel(humanType, "VisualModel");
            visual.transform.SetParent(root.transform, false);

            if (type == HostileMobType.Goblin)
            {
                visual.transform.localScale = new Vector3(0.75f, 0.75f, 0.75f);
            }
            else if (type == HostileMobType.WarlordGuard)
            {
                visual.transform.localScale = new Vector3(1.25f, 1.25f, 1.25f);
            }

            return root;
        }
    }
}
