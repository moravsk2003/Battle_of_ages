using UnityEngine;

namespace BattleOfAges.Building
{
    public enum BuildingType
    {
        WorkerHouse,
        StorageChest,
        Mine
    }

    /// <summary>
    /// Immutable definition of a building: cost, display info, and spawn rules.
    /// </summary>
    public struct BuildingDefinition
    {
        public BuildingType Type;
        public string DisplayName;
        public string Description;
        public int WoodCost;
        public int OreCost;
        public int OreTierRequired; // which ore tier level is consumed
        public int NpcSpawnCount;   // how many NPC workers spawn on build
        public Vector3 Size;        // approximate footprint for ghost preview
    }

    public static class BuildingData
    {
        private static readonly BuildingDefinition[] definitions =
        {
            new BuildingDefinition
            {
                Type = BuildingType.WorkerHouse,
                DisplayName = "Будинок Робітника",
                Description = "Створює 1 робітника-селянина",
                WoodCost = 1,
                OreCost = 1,
                OreTierRequired = 0,
                NpcSpawnCount = 1,
                Size = new Vector3(3.0f, 2.5f, 3.0f)
            },
            new BuildingDefinition
            {
                Type = BuildingType.StorageChest,
                DisplayName = "Скриня (Склад)",
                Description = "Точка збору та сховище ресурсів",
                WoodCost = 2,
                OreCost = 1,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(2.0f, 1.8f, 2.0f)
            },
            new BuildingDefinition
            {
                Type = BuildingType.Mine,
                DisplayName = "Шахта",
                Description = "Видобуває руду (до 10 робітників, 1 руда/сек)",
                WoodCost = 4,
                OreCost = 2,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(4.0f, 3.2f, 4.0f)
            }
        };

        public static BuildingDefinition GetDefinition(BuildingType type)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                if (definitions[i].Type == type)
                    return definitions[i];
            }
            return definitions[0];
        }

        public static BuildingDefinition[] GetAllDefinitions()
        {
            return definitions;
        }
    }
}
