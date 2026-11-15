using UnityEngine;
using BattleOfAges.Combat;

namespace BattleOfAges.Building
{
    public enum BuildingType
    {
        WorkerHouse,
        StorageChest,
        Mine,
        Workbench,
        CastleWall,
        TownHall,
        WallStairs,
        WallGate,
        // A campaign's zones (M5; added last so saved numbers hold)
        Field,
        Sawmill,
        Market
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
        public Vector3 FootprintCenter; // where the footprint's middle is in the building's frame (stairs stand inside a wall)
        public int MaxHealth;       // structure durability (a wall's: at ore tier 0, see WallSegment)
    }

    public static class BuildingData
    {
        private static readonly BuildingDefinition[] definitions =
        {
            new BuildingDefinition
            {
                Type = BuildingType.WorkerHouse,
                DisplayName = "Будинок",
                Description = "Ще один робітник (і місце для нього в населенні)",
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
                DisplayName = "Скриня-склад",
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
                Description = "До 10 робітників копають 1 руду/с (з кайлом більше); носії несуть її в скриню",
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
                DisplayName = "Верстак",
                Description = "Зброя, броня й інструменти; переплавка руди (до 4 робітників)",
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
                DisplayName = "Стіна",
                Description = "Сегмент муру з хідником; міцність від руди",
                WoodCost = 2,
                OreCost = 4,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(4.0f, 4.9f, 1.8f),
                MaxHealth = 1500
            },
            new BuildingDefinition
            {
                Type = BuildingType.WallStairs,
                DisplayName = "Стіна зі сходами",
                Description = "Сегмент муру; сходи зсередини на хідник",
                WoodCost = 3,
                OreCost = 6,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(4.0f, 4.9f, 3.0f),
                FootprintCenter = new Vector3(0f, 0f, -0.6f),
                MaxHealth = 1500
            },
            new BuildingDefinition
            {
                Type = BuildingType.WallGate,
                DisplayName = "Ворота",
                Description = "Прохід у мурі: відчиняються лише своїм",
                WoodCost = 8,
                OreCost = 6,
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(6.0f, 4.9f, 1.8f),
                MaxHealth = 1800
            },
            new BuildingDefinition
            {
                Type = BuildingType.TownHall,
                DisplayName = "Ратуша",
                Description = "Головна будівля: ліміт населення (I-V)",
                WoodCost = GameBalance.TownHallWood[1],
                OreCost = GameBalance.TownHallOre[1],
                OreTierRequired = 0,
                NpcSpawnCount = 0,
                Size = new Vector3(5.6f, 5.0f, 5.6f),
                MaxHealth = GameBalance.TownHallHealth[1] // grows with the level (TownHall)
            },
            new BuildingDefinition { Type = BuildingType.Field, DisplayName = "Поле", Description = "Селяни на ньому дають їжу", WoodCost = 2, OreCost = 0, OreTierRequired = 0, NpcSpawnCount = 0, Size = new Vector3(13f, 0.6f, 13f), MaxHealth = 100 },
            new BuildingDefinition { Type = BuildingType.Sawmill, DisplayName = "Лісопилка", Description = "Пилярі дають дерево, більше в густому лісі", WoodCost = 6, OreCost = 1, OreTierRequired = 0, NpcSpawnCount = 0, Size = new Vector3(7f, 3.4f, 6f), MaxHealth = 400 },
            new BuildingDefinition { Type = BuildingType.Market, DisplayName = "Ринок", Description = "Обмін дерева, їжі й руди", WoodCost = 12, OreCost = 4, OreTierRequired = 0, NpcSpawnCount = 0, Size = new Vector3(9f, 3f, 9f), MaxHealth = 500 }
        };

        private static BuildingDefinition Localize(BuildingDefinition def)
        {
            if (!UI.Loc.En) return def;
            switch (def.Type)
            {
                case BuildingType.WorkerHouse:
                    def.DisplayName = "House";
                    def.Description = "Another worker (and a home in your population limit)";
                    break;
                case BuildingType.StorageChest:
                    def.DisplayName = "Storage Chest";
                    def.Description = "Drop-off point and resource stockpile";
                    break;
                case BuildingType.Mine:
                    def.DisplayName = "Mine";
                    def.Description = "Up to 10 workers mine 1 ore/s (more with a pickaxe); haulers carry it to a chest";
                    break;
                case BuildingType.Workbench:
                    def.DisplayName = "Workbench";
                    def.Description = "Weapons, armor, and tools; ore smelting (up to 4 workers)";
                    break;
                case BuildingType.CastleWall:
                    def.DisplayName = "Wall";
                    def.Description = "Wall segment with a walkway; strength scales with ore tier";
                    break;
                case BuildingType.WallStairs:
                    def.DisplayName = "Wall with Stairs";
                    def.Description = "Wall segment; stairs on the inside up to the walkway";
                    break;
                case BuildingType.WallGate:
                    def.DisplayName = "Gate";
                    def.Description = "Passage in the wall: opens only for allies";
                    break;
                case BuildingType.TownHall:
                    def.DisplayName = "Town Hall";
                    def.Description = "Main building: population cap (I–V)";
                    break;
                case BuildingType.Field:
                    def.DisplayName = "Field";
                    def.Description = "Villagers working here produce food";
                    break;
                case BuildingType.Sawmill:
                    def.DisplayName = "Sawmill";
                    def.Description = "Sawyers produce wood, more in dense forest";
                    break;
                case BuildingType.Market:
                    def.DisplayName = "Market";
                    def.Description = "Trade wood, food, and ore";
                    break;
            }
            return def;
        }

        public static BuildingDefinition GetDefinition(BuildingType type)
        {
            for (int i = 0; i < definitions.Length; i++)
            {
                if (definitions[i].Type == type)
                {
                    BuildingDefinition def = definitions[i];
                    if (type == BuildingType.TownHall && World.WorldSettings.Bounded && TownHall.Current != null)
                    {
                        // Another hall is a new settlement: settlers and their load
                        def.WoodCost = GameBalance.FoundingWood;
                        def.OreCost = GameBalance.FoundingOre;
                    }
                    return Localize(def);
                }
            }
            return Localize(definitions[0]);
        }

        public static BuildingDefinition[] GetAllDefinitions()
        {
            if (!UI.Loc.En) return definitions;
            var copy = new BuildingDefinition[definitions.Length];
            for (int i = 0; i < definitions.Length; i++) copy[i] = Localize(definitions[i]);
            return copy;
        }

        /// <summary>Does this building grow from a zone in a campaign (not placed by hand there; §10.1)?</summary>
        public static bool GrowsFromZone(BuildingType t) =>
            t == BuildingType.WorkerHouse || t == BuildingType.Mine || t == BuildingType.Workbench || t == BuildingType.Field || t == BuildingType.Sawmill || t == BuildingType.Market;

        /// <summary>What the build menu offers: in a campaign the key buildings; in the fixed world M0's list.</summary>
        public static BuildingDefinition[] MenuDefinitions()
        {
            var list = new System.Collections.Generic.List<BuildingDefinition>();
            foreach (BuildingDefinition d in definitions)
            {
                bool zoneOnly = d.Type == BuildingType.Field || d.Type == BuildingType.Sawmill || d.Type == BuildingType.Market;
                if (World.WorldSettings.Bounded ? !GrowsFromZone(d.Type) : !zoneOnly) list.Add(Localize(d));
            }
            return list.ToArray();
        }
    }
}
