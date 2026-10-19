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

        // ------------------------------------------------------------------ artifacts (§9.1.1)

        public static float VampiricShare = 0.1f, SwordArtifactMultiplier = 5f, ArmorArtifactReduction = 0.9f;
        public static float WisdomXpBonus = 0.2f;
        public static int LightningDamage = 200;
        public static float LightningRange = 40f;
        /// <summary>No armour stops more than this share of a hit.</summary>
        public static float MaxDamageReduction = 0.9f;
        /// <summary>Central Empire: gear levels of the hero, warriors and up, people.</summary>
        public static int EmpireHeroGear = 5, EmpireSoldierGear = 4, EmpirePersonGear = 3;

        // ------------------------------------------------------------------ hero death (§7.2)

        /// <summary>Real seconds between the hero's death and the new hero.</summary>
        public static float InterregnumSeconds = 90f;
        /// <summary>Share of the units that desert when the hero dies.</summary>
        public static float DesertionShare = 0.5f;

        // ------------------------------------------------------------------ hero classes (§8.1)

        public static float TankHealthBonus = 0.2f, ArcherHealthPenalty = 0.2f;
        public static int TankIgnoreEveryNthHit = 10;
        /// <summary>Archer and mage shoot as far as they run in this many seconds, but never less than MinClassRange.</summary>
        public static float ClassRangeRunSeconds = 3f, MinClassRange = 12f;
        /// <summary>The mage's attack: this many homing bolts, each this share of the damage, through armour.</summary>
        public static int MageBolts = 5;
        public static float MageBoltShare = 0.2f;
        /// <summary>The mage runs like this share of its rank's strength, and strength points add only to its spells.</summary>
        public static float MageBodyStrengthShare = 0.7f; // hero general: 35

        // ------------------------------------------------------------------ class skills (§8.1)

        /// <summary>Tank Regeneration: the start gives one step of each; every further point one more step of one.</summary>
        public static float RegenStepMax = 0.01f, RegenStepCurrent = 0.02f;
        /// <summary>Tank Instant heal: share of max HP at level 1, + step per level, up to HealMaxLevel; cooldown in seconds.</summary>
        public static float HealFirst = 0.05f, HealStep = 0.04f, HealCooldown = 12f * 60f;
        public static int HealMaxLevel = 6;
        /// <summary>Archer Long shot (passive).</summary>
        public static int LongShotCost = 2;
        public static float LongShotRangeBonus = 0.05f;
        /// <summary>Mage Fire fan: cone angle, damage share at the edge of the range, cooldown.</summary>
        public static float FireFanAngle = 60f, FireFanEdgeDamage = 0.5f, FireFanCooldown = 10f;
        /// <summary>The fan hits each target for this share of the mage's attack (at its centre).</summary>
        public static float FireFanDamageShare = 0.6f;
        /// <summary>DD Dash: x(1 + level) run speed for DashSeconds, levels 1..DashMaxLevel (x2..x5).</summary>
        public static float DashSeconds = 1f, DashCooldown = 20f;
        public static int DashMaxLevel = 4;
        /// <summary>
        /// Archer Piercing arrow (Q): the next shot hits its target for PierceShotMultiplier and goes on through, hitting the
        /// enemies behind for these shares of a normal shot in turn (100%, then 50%); "behind" is up to PierceReach past the
        /// last one hit, within PierceWidth of the line of the shot.
        /// </summary>
        public static float PierceShotMultiplier = 2f, PierceCooldown = 100f, PierceReach = 10f, PierceWidth = 1.5f;
        public static float[] PierceThrough = { 1f, 0.5f };
        /// <summary>DD Critical hit (R): the next hit deals this many times the damage; cooldown in seconds.</summary>
        public static float CritMultiplier = 2f, CritCooldown = 40f;
        /// <summary>Mage Meteor: fall time, radius, damage (x the mage's), cooldown.</summary>
        public static float MeteorDelay = 1f, MeteorRadius = 2.5f, MeteorDamageFactor = 5f, MeteorCooldown = 20f;

        // ------------------------------------------------------------------ attack recovery (§11.1)

        /// <summary>
        /// After a melee hit the attacker runs at this share of its speed for a while (no hit-and-run kiting); after a
        /// ranged shot (a bow or the mage's bolts) it cannot move at all for RangedRootSeconds. The values are picked by
        /// the AttackRecoveryProbe (PlayMode) that plays hit-and-run with 0..3 s.
        /// </summary>
        public static float MeleeSlowFactor = 0.5f, MeleeSlowSeconds = 1f;
        /// <summary>Wind-up: seconds from the start of a swing to its hit (a started swing lands even if the striker falls).</summary>
        public static float MeleeWindup = 0.35f, BowWindup = 0.5f, MageWindup = 0.2f;
        public static float RangedRootSeconds = 0.5f;
        /// <summary>The pull of a battle (BattleFront): 5% faster towards the enemy, slower away, checked this often within this radius.</summary>
        public static float BattlePull = 0.05f, BattleFrontSeconds = 10f, BattleRadius = 30f;
        /// <summary>A hit enemy goes after its attacker for this long, past its usual leash.</summary>
        public static float ProvokedSeconds = 15f;

        // ------------------------------------------------------------------ promotions (§11.1)

        /// <summary>Battle XP to leave the rank: person -> warrior, warrior -> knight, knight -> general.</summary>
        public static int[] PromotionXp = { 30, 100, 500 };
        public static int XpPerHit = 1, XpPerKill = 10, XpPerSurvival = 5;
        /// <summary>Survival XP is given at most once per this many seconds (one fight, not every target).</summary>
        public static float SurvivalAwardWindow = 30f;
        /// <summary>Chance per worked game hour that a working person becomes a warrior.</summary>
        public static float WorkPromotionPerHour = 0.005f;

        /// <summary>Seconds between attacks for every unit and hero: only the weapon, gloves and class change it (§8).</summary>
        public static float BaseAttackCooldown = 2f;

        // ------------------------------------------------------------------ speed (§11.1)

        /// <summary>speed = SpeedGlobal x modifier x sqrt(strength / SpeedStrengthDivisor) + SpeedFlat, metres per second.</summary>
        public static float SpeedGlobal = 0.25f, SpeedStrengthDivisor = 2f, SpeedFlat = 1.5f;
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

        // ------------------------------------------------------------------ NPC kingdoms (§12, M0)

        /// <summary>NPC kings around the player at a new game, this far from the player's start.</summary>
        public static int KingdomCount = 3;
        public static float KingdomDistance = 600f;
        /// <summary>The start of every NPC king: a hero (a general, like the player) and this many people and wood, no hall.</summary>
        public static int KingdomStartPeople = 5, KingdomStartWood = 10;
        /// <summary>What one person of an NPC kingdom brings in per game hour of work at home (no walking to the trees).</summary>
        public static float KingdomWoodPerWorkerHour = 2f, KingdomOrePerWorkerHour = 0.5f;
        /// <summary>People within this of the hall work; enemies within DefendRadius are fought by everyone at home.</summary>
        public static float KingdomHomeRadius = 30f, KingdomDefendRadius = 40f;
        /// <summary>
        /// A raid goes when the army (warriors and up and the hero) is this many times stronger than what guards the
        /// target (strength = total HP x total damage per second, as in Lanchester's square law), not before
        /// KingdomFirstRaidHours and RaidCooldownHours after the last one; everyone goes (one kind of unit: people fight
        /// too) but this share, the weakest, who stay home; a raid that lost RaidRetreatLosses turns back. The hall is
        /// raised once the people nearly fill it (KingdomHallRoom places left), as a player would.
        /// </summary>
        public static float KingdomRaidPowerRatio = 1.5f, KingdomFirstRaidHours = 8f, KingdomRaidCooldownHours = 4f, KingdomGarrisonShare = 0.3f, KingdomRaidRetreatLosses = 0.5f;
        /// <summary>Raiders look for something to fight this far from where they are; the guards of a target count within GuardRadius.</summary>
        public static float KingdomRaidSearchRadius = 40f, KingdomGuardRadius = 40f;
        /// <summary>HP of an NPC hall of level I..V (level 0 is a camp fire that cannot be destroyed).</summary>
        public static int KingdomHallHealthPerLevel = 1000;
        public static int KingdomHallRoom = 2;
    }
}
