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
        /// <summary>Seconds between a unit's blows before the weapon's speed.</summary>
        public static float UnitBaseCooldown = 2f;
        /// <summary>
        /// The abstract battle (§6.2), fitted to real ones (AbstractBattleCalibration, AbstractBattleTests): how many can strike
        /// at once (Base + PerRoot x root of the larger side) and the share of their damage per second that lands.
        /// </summary>
        public static float AbstractFrontBase = 1f, AbstractFrontPerRoot = 6f, AbstractDamageShare = 0.7f;
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

        public static float VampiricShare = 0.1f, SwordArtifactMultiplier = 5f, ArmorArtifactReduction = 0.8f; // the armour: a whole set (helmet, chest, gloves, boots)
        public static float WisdomXpBonus = 0.2f;
        /// <summary>Full heal: all HP back and nothing hurts the hero for this long.</summary>
        public static float FullHealInvulnerableSeconds = 1f;
        /// <summary>Lightning: magic (through any armour) on the nearest enemy within LightningRange.</summary>
        public static int LightningDamage = 250;
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
        /// <summary>
        /// The mage runs like this share of its rank's strength and of its HP; strength points add only to its spells. Its
        /// attack (the bolts) needs a staff in hand; without one it has only its skills.
        /// </summary>
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

        // ------------------------------------------------------------------ weapon prices (§9)

        /// <summary>A bow and a staff cost this many times a sword of the same level (wood and ore alike, rounded up).</summary>
        public static float BowCostFactor = 1.5f, StaffCostFactor = 2f;

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
        /// <summary>
        /// The player's people (one kind of unit: they work and fight): a hit worker fights back unless its HP is below
        /// WorkerFleeShare (then it runs to the chest); a worker goes for an enemy within WorkerEngageRadius of it. The alarm
        /// (key T, or by itself when an enemy comes within AlarmTriggerRadius of the hall) sends everyone at the enemies
        /// within AlarmRadius of the hall, no further than AlarmChaseRadius from it, and stands down AlarmStandDownSeconds
        /// after the last one is gone.
        /// </summary>
        public static float WorkerFleeShare = 0.25f, WorkerEngageRadius = 8f;
        /// <summary>
        /// A fight a unit picked itself (not ordered by the player) ends this far from where it began: the
        /// unit lets the enemy run and goes back (a soldier to its place, a worker to its job).
        /// </summary>
        public static float UnitLeashRadius = 25f, WorkerLeashRadius = 15f;
        /// <summary>Workers at one tree at most: the rest take the next tree instead of crowding round one.</summary>
        public static int WorkersPerTree = 2;
        /// <summary>
        /// Units in each other's way: one walking gives little ground, one standing gives much, so the walker shoulders through;
        /// and held up with nothing solid straight ahead it presses on this many seconds before it tries going round.
        /// </summary>
        public static float WalkerSeparation = 3f, StandingSeparation = 8f, PressBeforeBypass = 1.5f;
        /// <summary>
        /// Workers on their own (Auto) split by need: while the wood in stock is short of the next town hall step (at least
        /// AutoWoodReserve) this share of them hauls mine ore and the rest fetch wood; with wood enough, AutoHaulShare
        /// haul (the rest wood and pebbles). Before this they all hauled while a mine had ore, and a mine always had.
        /// </summary>
        public static int AutoWoodReserve = 40;
        public static float AutoHaulShareWoodShort = 0.25f, AutoHaulShare = 0.6f;
        /// <summary>
        /// In a campaign, under this much ore (stock and mines' buffers) this share of Auto workers, at least one, goes for
        /// pebbles even with trees nearer: a tree is nearly always nearer, and without ore there is neither a hall nor a mine.
        /// </summary>
        public static int AutoPebbleOre = 15;
        public static float AutoPebbleShare = 0.4f;
        /// <summary>Ore a hauler carries from the mine to the chest per trip (3 left a mine of 10 full).</summary>
        public static int HaulerCarry = 6;
        /// <summary>
        /// The workbench melts SmeltRatio ore of a tier into one of the next (a tier is ten times rarer from the mine, so
        /// the same worth), SmeltSecondsPerPiece per piece with one worker: the common ore is worth something, and the rare
        /// a known amount of digging (town hall V from ~100 000 mined ore to ~24 000; docs/GAME_DESIGN.md §13).
        /// </summary>
        public static int SmeltRatio = 10;
        public static float SmeltSecondsPerPiece = 1f;
        public static float AlarmRadius = 60f, AlarmTriggerRadius = 40f, AlarmStandDownSeconds = 10f, AlarmGatherRadius = 25f, AlarmChaseRadius = 90f, AlarmGatherNear = 9f, AlarmGatherFar = 16f;
        public static bool AutoAlarm = true;

        // ------------------------------------------------------------------ promotions (§11.1)

        /// <summary>Battle XP to leave the rank: person -> warrior, warrior -> knight, knight -> general.</summary>
        public static int[] PromotionXp = { 30, 100, 500 };
        public static int XpPerHit = 1, XpPerKill = 10, XpPerSurvival = 5;
        /// <summary>Survival XP is given at most once per this many seconds (one fight, not every target).</summary>
        public static float SurvivalAwardWindow = 30f;
        /// <summary>An NPC kingdom's town: one house to so many people, one field to so many, walls from this hall.</summary>
        public static int TownPeoplePerHouse = 3, TownPeoplePerField = 10, TownWallHall = 3;
        /// <summary>
        /// Chance per game hour, by hall level, that an NPC kingdom's warrior trains into a knight, and from hall IV a knight
        /// into a general (they would otherwise rise only in battle, and a far kingdom fights none).
        /// </summary>
        public static float[] KingdomKnightTraining = { 0f, 0.002f, 0.004f, 0.006f, 0.008f, 0.01f };
        public static float[] KingdomGeneralTraining = { 0f, 0f, 0f, 0f, 0.001f, 0.002f };
        /// <summary>Chance per worked game hour that a working person becomes a warrior.</summary>
        public static float WorkPromotionPerHour = 0.005f;

        /// <summary>Seconds between attacks for every unit and hero: only the weapon, gloves and class change it (§8).</summary>
        public static float BaseAttackCooldown = 2f;

        // ------------------------------------------------------------------ speed (§11.1)

        /// <summary>
        /// speed = SpeedGlobal x modifier x sqrt(body / SpeedStrengthDivisor + SpeedBodyFlat) + SpeedFlat, metres per second, where
        /// body = (strength + HP / SpeedHealthPerStrength) / SpeedBodyDivisor (a rank, 10 HP per strength point: its
        /// strength x 0.75; +5 strength adds 2.5 to the body, +50 HP 1.25).
        /// </summary>
        public static float SpeedGlobal = 0.25f, SpeedStrengthDivisor = 2f, SpeedFlat = 2.5f, SpeedHealthPerStrength = 20f, SpeedBodyDivisor = 2f, SpeedBodyFlat = 0.5f;
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
        /// <summary>HP of the hall of level I..V, the player's and an NPC king's alike (index 0: no hall; an NPC camp cannot be destroyed).</summary>
        public static int[] TownHallHealth = { 0, 1500, 2250, 3000, 3750, 4500 };

        // ------------------------------------------------------------------ walls (§10.5)

        /// <summary>A wall's HP is its type's MaxHealth x (1 + this x the tier of the ore it is built of).</summary>
        public static float WallHealthPerOreTier = 0.5f;
        /// <summary>Walls may stand on ground rising this much across them (their foundation goes deeper than a house's).</summary>
        public static float WallMaxGroundRise = 1.6f;
        /// <summary>A new segment snaps to a free end of a wall within this of the cursor; turns go in these steps.</summary>
        public static float WallSnapRadius = 4.2f, WallTurnStep = 15f;
        /// <summary>Wood and ore pieces to raise the settlement's walls to levels II..V; the minimum ore tier is level - 1.</summary>
        public static int[] WallLevelWood = { 0, 0, 20, 45, 80, 130 };
        public static int[] WallLevelOre = { 0, 0, 8, 12, 16, 24 };
        /// <summary>Extra damage of a unit fighting from a wall of level I..V (on top of the height bonus).</summary>
        public static float[] WallLevelDamageBonus = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        /// <summary>The gate opens for own units and the player within this.</summary>
        public static float GateOpenRadius = 5f;
        /// <summary>A blow harms a wall only from this strength up (a knight); siege engines will not need it (§11.3).</summary>
        public static float WallBreachStrength = 10f;
        /// <summary>A bow (or the mage's bolts) on the walls reaches this much further.</summary>
        public static float WallRangeBonus = 0.3f;
        /// <summary>
        /// Ladders: the first attacker at a spot sets one up in LadderSetupSeconds; the attackers then go up one at a time
        /// at LadderClimbSpeed m/s, over the breastwork onto the walkway and down inside. A ladder nobody used for
        /// LadderLingerSeconds is taken away.
        /// </summary>
        public static float LadderSetupSeconds = 4f, LadderClimbSpeed = 1.1f, LadderLingerSeconds = 60f;

        // ------------------------------------------------------------------ NPC kingdoms (§12, M0)

        /// <summary>NPC kings around the player at a new game, this far from the player's start.</summary>
        /// <summary>
        /// Bandit raids on a campaign's settlement (BanditRaids): the game hour of the first, the hours between them (from
        /// when the last one's over at the earliest), the band's size, how far off it appears. Camps: one chunk in so many
        /// (M0's world: one in five), none this near the base.
        /// </summary>
        public static float BanditFirstRaidHours = 6f, BanditRaidEveryHours = 6f, BanditSpawnDistance = 220f;
        public static int BanditRaidFirst = 3, BanditRaidGrowth = 2, BanditRaidMax = 20;
        public static int CampaignCampEvery = 20;
        public static float CampaignCampClearance = 300f;
        /// <summary>Food (§10.3, a campaign): eaten per person per game day; the stock of a new campaign; "running low" under this many days.</summary>
        public static float FoodPerPersonPerDay = 1f, FoodStart = 40f, FoodLowDays = 1f;
        /// <summary>Food an animal killed by the player's side brings in (to the nearest settlement's stock).</summary>
        public static float FoodPerAnimal = 1f;
        /// <summary>Building by workers (ConstructionSite): one worker's seconds for a building, and more per wood or ore it costs; how far Auto workers come from.</summary>
        public static float BuildBaseSeconds = 8f, BuildSecondsPerResource = 2f, BuildersComeFrom = 400f;
        /// <summary>How far from its hall a settlement marks zones at hall I (§5.3; +60% each hall level).</summary>
        public static float ZoneReachBase = 300f;
        /// <summary>
        /// Zones (ZoneGrower, WorkPlace): marked cells per building; sites at once; farmers and food (a farmer feeds a dozen);
        /// the food days under which Auto workers go to the fields, and over which they leave; sawyers and wood; births per
        /// game hour a house adds (§10.3).
        /// </summary>
        public static int ZoneCellsHouse = 3, ZoneCellsField = 6, ZoneCellsSawmill = 12, ZoneCellsBench = 10, ZoneCellsMine = 10, ZoneCellsMarket = 12, ZoneSitesAtOnce = 2;
        public static int FieldFarmers = 2, SawmillWorkers = 2, SawmillWantWood = 150, SawmillEnoughWood = 300;
        /// <summary>In a campaign workers on their own go down a mine, this many to each, while the ore (stock and mines' buffers) is under Want, and come out at Enough.</summary>
        public static int MineAutoMiners = 2, MineWantOre = 60, MineEnoughOre = 200;

        /// <summary>
        /// A new settlement (M5 step 7): a hall of this level at home, this far from the player's halls and from any
        /// king's; it costs FoundingWood and FoundingOre at the settlement that pays, and starts with FoundingStart* (settlers' load).
        /// </summary>
        public static int FoundingHallLevel = 2, FoundingWood = 200, FoundingOre = 30, FoundingStartWood = 60, FoundingStartFood = 20;
        public static float FoundingFromOwn = 500f, FoundingFromKing = 300f;
        /// <summary>...and this many chunks (64 m) clear of a raiders' camp.</summary>
        public static int FoundingFromCampChunks = 3;

        /// <summary>Caravans: m/s; at most one a route, each CaravanEveryHours; sent when the other runs under Short and this one stays over Spare.</summary>
        public static float CaravanSpeed = 5f, CaravanEveryHours = 1f, CaravanFoodShortDays = 2f, CaravanFoodSpareDays = 4f;
        public static int CaravanWoodShort = 100, CaravanWoodSpare = 300, CaravanOreShort = 30, CaravanOreSpare = 100;
        public static int CaravanLoadWood = 100, CaravanLoadFood = 40, CaravanLoadOre = 40;
        public static float FarmerFoodPerHour = 0.5f, FoodWantDays = 3f, FoodEnoughDays = 6f, SawyerWoodPerHour = 25f, HouseBirthsPerHour = 0.1f;
        public static int KingdomCount = 3;
        /// <summary>
        /// A campaign (M1, a bounded world): heroes in the world, the player's included, and the share of the others that
        /// are kings (the rest free heroes); the kingdoms nearest the player's home that are always real (their raids);
        /// any other is real while the hero is within MaterializeRadius of it, and folds back into its state past
        /// DematerializeRadius (KingdomRealm).
        /// </summary>
        public static int CampaignHeroes = 16;
        public static float CampaignKingShare = 0.75f;
        public static int RealKingdomsNearHome = 3;
        public static float MaterializeRadius = 1200f, DematerializeRadius = 1700f;
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
        public static int KingdomHallRoom = 2;
        /// <summary>
        /// Where the world puts settlements (BuildSite): a kingdom looks this far round its spot for ground rising at most
        /// SiteMaxRise across its 7 m hall; a raider camp looks within its chunk across 14 m, and is not made if none is level.
        /// </summary>
        public static float KingdomSiteSearchRadius = 150f, KingdomSiteMaxRise = 1.2f, CampSiteSearchRadius = 24f, CampSiteMaxRise = 1.6f;
    }
}
