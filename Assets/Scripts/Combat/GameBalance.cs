namespace BattleOfAges.Combat
{
    /// <summary>
    /// Every tuning number of the game rules in one place (docs/GAME_DESIGN.md §16), grouped by topic. Static fields,
    /// not constants, so a balance pass or a test can change them at runtime without touching the systems.
    /// </summary>
    public static class GameBalance
    {
        // ------------------------------------------------------------------ time (§4)

        /// <summary>Real seconds in one game hour (a game day is 24 of them).</summary>
        public static float GameHourSeconds = 60f;

        // ------------------------------------------------------------------ unit ranks (§11.1)

        /// <summary>Base HP of person, warrior, knight, general (without gear).</summary>
        public static int[] RankHealth = { 20, 40, 100, 250 };
        /// <summary>Base strength (damage without a weapon) of person, warrior, knight, general.</summary>
        public static int[] RankStrength = { 2, 4, 10, 25 };
        /// <summary>Hero XP for killing a person, warrior, knight, general (§8).</summary>
        public static int[] RankKillXp = { 2, 4, 10, 50 };
        /// <summary>A hero has this many times the HP and strength of its rank (§7.1).</summary>
        public static float HeroStatMultiplier = 2f;

        // ------------------------------------------------------------------ hero levels (§8)

        /// <summary>Going from level n to n + 1 costs this x n XP.</summary>
        public static int HeroXpPerLevel = 100;
        /// <summary>What one attribute point buys: strength or HP (10 HP : 1 strength, like every rank's base).</summary>
        public static int StrengthPerPoint = 5, HealthPerPoint = 50;
        /// <summary>Unranked beasts give 1 XP per this much of their max HP.</summary>
        public static int WildlifeHpPerXp = 10;

        // ------------------------------------------------------------------ hero classes (§8.1)

        public static float TankHealthBonus = 0.2f, ArcherHealthPenalty = 0.2f;
        public static int TankIgnoreEveryNthHit = 10;
        /// <summary>Archer and mage shoot as far as they run in this many seconds, but never less than MinClassRange.</summary>
        public static float ClassRangeRunSeconds = 3f, MinClassRange = 12f;
        /// <summary>The mage's attack: this many homing bolts, each this share of the damage, through armour.</summary>
        public static int MageBolts = 5;
        public static float MageBoltShare = 0.2f;

        // ------------------------------------------------------------------ promotions (§11.1)

        /// <summary>Battle XP to leave the rank: person -> warrior, warrior -> knight, knight -> general.</summary>
        public static int[] PromotionXp = { 30, 100, 500 };
        public static int XpPerHit = 1, XpPerKill = 10, XpPerSurvival = 5;
        /// <summary>Survival XP is given at most once per this many seconds (one fight, not every target).</summary>
        public static float SurvivalAwardWindow = 30f;
        /// <summary>Chance per worked game hour that a working person becomes a warrior.</summary>
        public static float WorkPromotionPerHour = 0.005f;

        // ------------------------------------------------------------------ speed (§11.1)

        /// <summary>speed = SpeedGlobal x modifier x sqrt(strength), metres per second.</summary>
        public static float SpeedGlobal = 0.25f;
        public static float SpeedModifierPerson = 7f;
        public static float SpeedModifierSoldier = 6f; // warrior, knight, general
        public static float SpeedModifierHero = 5f;

        // ------------------------------------------------------------------ healing (§11.2)

        /// <summary>Natural regeneration of every unit and hero: share of the current HP per game hour.</summary>
        public static float NaturalRegenCurrentPerHour = 0.01f;

        // ------------------------------------------------------------------ population (§10.3)

        /// <summary>Births per settlement per game hour: base + per-capita share of the population.</summary>
        public static float GrowthBasePerHour = 0.5f;
        public static float GrowthPerCapitaPerHour = 0.0075f;

        // ------------------------------------------------------------------ town hall (§10.4)

        /// <summary>Population cap without a hall (index 0) and for hall levels I..V.</summary>
        public static int[] TownHallCaps = { 6, 15, 25, 40, 60, 85 };
        /// <summary>Wood to build the hall (level I) and to upgrade to levels II..V.</summary>
        public static int[] TownHallWood = { 0, 10, 30, 60, 100, 160 };
        /// <summary>Ore pieces for the same steps; the minimum ore tier is level - 1.</summary>
        public static int[] TownHallOre = { 0, 5, 10, 12, 15, 20 };
    }
}
