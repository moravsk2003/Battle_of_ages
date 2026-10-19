namespace BattleOfAges.Combat
{
    /// <summary>The first artifacts (docs/GAME_DESIGN.md §9.1.1).</summary>
    public enum ArtifactType { Vampiric, SwordX5, Armor90, FullHeal, Lightning, Wisdom, CentralEmpire }

    public enum ArtifactTier { Common, Legendary, WorldChanging }

    public static class Artifacts
    {
        public static readonly ArtifactType[] All =
        {
            ArtifactType.Vampiric, ArtifactType.SwordX5, ArtifactType.Armor90, ArtifactType.FullHeal,
            ArtifactType.Lightning, ArtifactType.Wisdom, ArtifactType.CentralEmpire
        };

        public static ArtifactTier Tier(ArtifactType a)
        {
            switch (a)
            {
                case ArtifactType.Vampiric: case ArtifactType.SwordX5: case ArtifactType.Armor90: return ArtifactTier.Legendary;
                case ArtifactType.CentralEmpire: return ArtifactTier.WorldChanging;
                default: return ArtifactTier.Common;
            }
        }

        /// <summary>Used once, then gone (and never dropped).</summary>
        public static bool IsConsumable(ArtifactType a) => a == ArtifactType.FullHeal || a == ArtifactType.Lightning;

        public static string Name(ArtifactType a)
        {
            switch (a)
            {
                case ArtifactType.Vampiric: return "Вампірський";
                case ArtifactType.SwordX5: return "Меч ×5";
                case ArtifactType.Armor90: return "Броня 90%";
                case ArtifactType.FullHeal: return "Повне зцілення";
                case ArtifactType.Lightning: return "Блискавка";
                case ArtifactType.Wisdom: return "Мудрості";
                default: return "Центральна імперія";
            }
        }

        public static string Description(ArtifactType a)
        {
            switch (a)
            {
                case ArtifactType.Vampiric: return "10% завданого урону повертається як HP";
                case ArtifactType.SwordX5: return "урон ×5 (займає слот зброї)";
                case ArtifactType.Armor90: return "вхідний урон −90% (займає слот броні; магія крізь неї)";
                case ArtifactType.FullHeal: return "одноразовий: миттєво все HP";
                case ArtifactType.Lightning: return "одноразовий: 200 урону найближчому ворогу";
                case ArtifactType.Wisdom: return "+20% досвіду";
                default: return "спорядження 5/4/3, без роботи, безкоштовне будівництво";
            }
        }
    }
}
