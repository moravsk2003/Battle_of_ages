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
            IEnumerator structures = BuildCampStructures(root, campCenter);
            while (structures.MoveNext()) yield return null;
            IEnumerator raiders = PopulateCamp(root, campCenter);
            while (raiders.MoveNext()) yield return null;
            root.SetActive(true);
        }

        /// <summary>
        /// The camp's fire, tents, barricades and standard under root, in one step (a far chunk shows them; its raiders
        /// come with PopulateCamp when the chunk is near).
        /// </summary>
        public static IEnumerator BuildCampStructures(GameObject root, Vector3 campCenter)
        {
            root.transform.position = campCenter;

            // The camp's pieces (CampBuilder meshes), each set on the ground where it stands
            Transform Piece(string pieceName, Mesh mesh, Vector3 local, float yaw, Mesh far = null)
            {
                Vector3 world = campCenter + local;
                local.y = RtsUnit.GetTerrainHeight(world) - campCenter.y;
                Transform t = CampBuilder.Attach(root.transform, pieceName, mesh, far).transform;
                t.localPosition = local;
                t.localRotation = Quaternion.Euler(0f, yaw, 0f);
                return t;
            }

            // 1. Central campfire
            Piece("Campfire", CampBuilder.Fire, Vector3.zero, 0f);

            // 2. Three raider tents, their doors to the fire; one solid volume each: the open A-frame let units walk in
            // and get wedged (AI movement has no pathfinding)
            Vector3[] tentPositions = { new Vector3(-4.5f, 0f, 2.5f), new Vector3(4.5f, 0f, 3.0f), new Vector3(0f, 0f, -5.0f) };
            foreach (Vector3 tp in tentPositions)
            {
                float yaw = Mathf.Atan2(-tp.x, -tp.z) * Mathf.Rad2Deg;
                Transform tent = Piece("RaiderTent", CampBuilder.Tent, tp, yaw, CampBuilder.TentFar);
                BoxCollider volume = tent.gameObject.AddComponent<BoxCollider>();
                volume.center = new Vector3(0f, 1.1f, 0f);
                volume.size = new Vector3(3.4f, 2.2f, 3.3f);
            }

            // 3. Spiked barricades on the outskirts, facing out
            Vector3[] barricadePositions = { new Vector3(-6.5f, 0f, -2.5f), new Vector3(6.5f, 0f, -2.0f), new Vector3(-2.5f, 0f, 6.0f), new Vector3(2.5f, 0f, 6.0f) };
            foreach (Vector3 bp in barricadePositions) Piece("SpikeBarricade", CampBuilder.Barricade, bp, Mathf.Atan2(bp.x, bp.z) * Mathf.Rad2Deg + 90f);

            // 4. War standard by the fire, and the camp's clutter by a tent
            Piece("WarStandard", CampBuilder.Standard, new Vector3(1.8f, 0f, 1.8f), 30f);
            Piece("CampClutter", CampBuilder.Clutter, new Vector3(-2.2f, 0f, -3.6f), 160f);
            yield return null;
        }

        /// <summary>The warlord and his squad, one per step, as children of the camp's root (WorldPersistence finds them there).</summary>
        public static IEnumerator PopulateCamp(GameObject root, Vector3 campCenter)
        {
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
