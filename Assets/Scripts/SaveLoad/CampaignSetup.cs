using BattleOfAges.Combat;
using BattleOfAges.World;

namespace BattleOfAges.SaveLoad
{
    /// <summary>
    /// What a new campaign is chosen with before it starts (docs/GAME_DESIGN.md §18.2, M1 step 6): the world (seed and
    /// size), how many heroes and how many of them kings, the player's hero class and starting artifact.
    /// </summary>
    public sealed class CampaignSetup
    {
        public static readonly float[] Sizes = { 8000f, 12000f, 16000f, 24000f };
        public static readonly int[] HeroCounts = { 10, 13, 16, 20, 25, 30 };
        public static readonly float[] KingShares = { 0.5f, 0.75f, 1f };

        /// <summary>0: a random world.</summary>
        public int Seed;
        public float Size = WorldSettings.DefaultSize;
        /// <summary>Heroes in the world, the player's included.</summary>
        public int Heroes = 16;
        public float KingShare = 0.75f;
        /// <summary>None: chosen later in the hero window (as before 0.2.0).</summary>
        public HeroClass Class = HeroClass.None;
        public bool HasArtifact;
        public ArtifactType Artifact;

        public int SeedOrRandom() => Seed != 0 ? Seed : WorldSettings.NewSeed();
    }
}
