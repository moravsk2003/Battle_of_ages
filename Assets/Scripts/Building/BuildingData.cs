using UnityEngine;

namespace BattleOfAges.Building
{
    public enum BuildingType
    {
        WorkerHouse,
        StorageChest,
        Mine,
        Workbench,
        CastleWall,
        TownHall
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
        public int MaxHealth;       // structure durability
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
                Size = new Vector3(3.0f, 2.5f, 3.0f),
                MaxHealth = 250
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
                Size = new Vector3(2.0f, 1.8f, 2.0f),
                MaxHealth = 200
            },
            new BuildingDefinition
            {
                Type = BuildingType.Mine,
                DisplayName = "Шахта",
                Description = "Видобуває руду (до 10 робітників, 1 руда/с, з кайлом більше)",
                WoodCost = 4,
                OreCost = 2,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(4.0f, 3.2f, 4.0f),
                MaxHealth = 500
            },
            new BuildingDefinition
            {
                Type = BuildingType.Workbench,
                DisplayName = "Верстак (Крафт)",
                Description = "Виготовлення зброї та броні (до 4 NPC робітників)",
                WoodCost = 3,
                OreCost = 1,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(3.2f, 2.2f, 2.2f),
                MaxHealth = 350
            },
            new BuildingDefinition
            {
                Type = BuildingType.CastleWall,
                DisplayName = "Замкова Стіна",
                Description = "Оборонний кам'яний мур для захисту від набігів",
                WoodCost = 2,
                OreCost = 3,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(4.0f, 3.5f, 1.2f),
                MaxHealth = 600
            },
            new BuildingDefinition
            {
                Type = BuildingType.TownHall,
                DisplayName = "Ратуша",
                Description = "Головна будівля: ліміт населення (I-V)",
                WoodCost = 15,
                OreCost = 5,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(5.6f, 5.0f, 5.6f),
                MaxHealth = 1500
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
