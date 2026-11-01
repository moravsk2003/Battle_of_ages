using System.Collections.Generic;
using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Resources;
using BattleOfAges.UI;
using BattleOfAges.Utils;
using BattleOfAges.World;

namespace BattleOfAges.Building
{
    /// <summary>
    /// Makes the marked zones grow (docs/GAME_DESIGN.md §10.1, §18.3, M5 step 4): as long as a zone holds fewer buildings
    /// than its size calls for and the stock pays, it sets out a building site on a free spot of the zone, nearest the
    /// hall first; workers do the rest (ConstructionSite). A few sites at a time, so the stock and the hands are not
    /// spread thin.
    /// </summary>
    public sealed class ZoneGrower : MonoBehaviour
    {
        private float clock;
        private readonly Dictionary<ZoneType, float> saidShort = new Dictionary<ZoneType, float>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindFirstObjectByType<ZoneGrower>() != null) return;
            var go = new GameObject("ZoneGrower");
            DontDestroyOnLoad(go);
            go.AddComponent<ZoneGrower>();
        }

        public static BuildingType BuildingOf(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Housing: return BuildingType.WorkerHouse;
                case ZoneType.Fields: return BuildingType.Field;
                case ZoneType.Forest: return BuildingType.Sawmill;
                case ZoneType.Crafts: return BuildingType.Workbench;
                case ZoneType.Mining: return BuildingType.Mine;
                default: return BuildingType.Market;
            }
        }

        /// <summary>Marked cells per building of the zone.</summary>
        public static int CellsPerBuilding(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Housing: return GameBalance.ZoneCellsHouse;
                case ZoneType.Fields: return GameBalance.ZoneCellsField;
                case ZoneType.Forest: return GameBalance.ZoneCellsSawmill;
                case ZoneType.Crafts: return GameBalance.ZoneCellsBench;
                case ZoneType.Mining: return GameBalance.ZoneCellsMine;
                default: return GameBalance.ZoneCellsMarket;
            }
        }

        /// <summary>How many buildings this many cells call for (the first as soon as a zone has a few).</summary>
        public static int Wanted(ZoneType zone, int cells) => Mathf.FloorToInt((cells + CellsPerBuilding(zone) - 1) / (float)CellsPerBuilding(zone));

        private void Update()
        {
            if (!WorldSettings.Bounded || !SaveLoad.SaveSystem.InGame) return;
            clock -= Time.deltaTime;
            if (clock > 0f) return;
            clock = 1.5f;
            Tick();
        }

        /// <summary>Sets out what the zones and the stock allow now.</summary>
        public void Tick()
        {
            int sites = 0;
            foreach (ConstructionSite s in ConstructionSite.All) if (s != null && ZoneMap.At(s.transform.position) != ZoneType.None) sites++;
            foreach (ZoneType zone in ZoneMap.Types)
            {
                if (sites >= GameBalance.ZoneSitesAtOnce) return;
                if (ZoneMap.Count(zone) == 0) continue;
                BuildingType type = BuildingOf(zone);
                int have = 0;
                foreach (BuildingNode b in BuildingNode.Active)
                    if (b != null && b.Type == type && ZoneMap.At(b.transform.position) == zone) have++;
                if (have >= Wanted(zone, ZoneMap.Count(zone))) continue;
                BuildingDefinition def = BuildingData.GetDefinition(type);
                if (!Affordable(def)) { SayShort(zone, def); continue; }
                if (!TryFindSpot(zone, def, out Vector3 spot)) continue;
                Pay(def);
                GameObject building = BuildingPlacementSystem.CreateBuilding(type);
                if (building == null) continue;
                building.transform.SetPositionAndRotation(OnHighest(spot, def), Quaternion.Euler(0f, Mathf.Round(spot.x * 7f) % 4 * 90f, 0f));
                ConstructionSite.Begin(building, type);
                sites++;
            }
        }

        /// <summary>A wide flat building (a field) sits on the highest of its corners: its footing goes down into the slope.</summary>
        private static Vector3 OnHighest(Vector3 spot, BuildingDefinition def)
        {
            if (def.Type != BuildingType.Field) return spot;
            float h = Mathf.Max(def.Size.x, def.Size.z) * 0.5f;
            foreach (float x in new[] { -h, h })
                foreach (float z in new[] { -h, h })
                    spot.y = Mathf.Max(spot.y, InfiniteWorldManager.GetSampledTerrainHeight(spot.x + x, spot.z + z));
            return spot;
        }

        private static bool Affordable(BuildingDefinition def)
        {
            ResourceManager rm = ResourceManager.Instance;
            return rm != null && (CentralEmpire.Active || (rm.WoodCount >= def.WoodCost && rm.GetOreCountAtLeast(def.OreTierRequired) >= def.OreCost));
        }

        private static void Pay(BuildingDefinition def)
        {
            if (CentralEmpire.Active) return;
            ResourceManager rm = ResourceManager.Instance;
            if (def.WoodCost > 0) rm.ConsumeResource(ResourceType.Wood, def.WoodCost);
            if (def.OreCost > 0) rm.ConsumeOreAtLeast(def.OreTierRequired, def.OreCost);
        }

        private void SayShort(ZoneType zone, BuildingDefinition def)
        {
            if (saidShort.TryGetValue(zone, out float at) && Time.unscaledTime - at < 60f) return;
            saidShort[zone] = Time.unscaledTime;
            StrategyHud.Notify($"{ZoneMap.Name(zone)}: на «{def.DisplayName}» треба {def.WoodCost} дерева й {def.OreCost} руди");
        }

        /// <summary>The free cell of the zone nearest the hall: no building's footprint there, no tree (a forest zone may stand among trees).</summary>
        public static bool TryFindSpot(ZoneType zone, BuildingDefinition def, out Vector3 spot)
        {
            Vector3 home = TownHall.Current != null ? TownHall.Current.transform.position : Vector3.zero;
            float best = float.MaxValue, reach = Mathf.Max(def.Size.x, def.Size.z) * 0.5f;
            spot = default;
            bool found = false;
            foreach (var pair in ZoneMap.Cells)
            {
                if (pair.Value != zone) continue;
                Vector3 c = new Vector3((pair.Key.x + 0.5f) * ZoneMap.CellSize, 0f, (pair.Key.y + 0.5f) * ZoneMap.CellSize);
                float d = (c - new Vector3(home.x, 0f, home.z)).sqrMagnitude;
                if (d >= best) continue;
                c.y = InfiniteWorldManager.GetSampledTerrainHeight(c.x, c.z);
                if (!Free(c, reach, zone)) continue;
                best = d;
                spot = c;
                found = true;
            }
            return found;
        }

        private static bool Free(Vector3 at, float reach, ZoneType zone)
        {
            foreach (BuildingNode b in BuildingNode.Active)
            {
                if (b == null) continue;
                BuildingDefinition other = BuildingData.GetDefinition(b.Type);
                float apart = reach + Mathf.Max(other.Size.x, other.Size.z) * 0.5f + 1f;
                Vector3 off = b.transform.position - at; off.y = 0f;
                if (off.sqrMagnitude < apart * apart) return false;
            }
            foreach (StorageChestNode c in StorageChestNode.ActiveChests)
            {
                if (c == null) continue;
                Vector3 off = c.transform.position - at; off.y = 0f;
                if (off.sqrMagnitude < (reach + 2f) * (reach + 2f)) return false;
            }
            if (zone == ZoneType.Forest) return true;
            foreach (Collider col in Physics.OverlapSphere(at + Vector3.up, reach))
                if (col != null && col.GetComponentInParent<ResourceNodeTree>() != null) return false;
            return true;
        }
    }
}
