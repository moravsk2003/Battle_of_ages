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
                case ArtifactType.Armor90: return "Броня 80%";
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
                case ArtifactType.Vampiric: return "10% завданої шкоди повертається як HP";
                case ArtifactType.SwordX5: return "шкода ×5 (займає слот зброї, не знімається)";
                case ArtifactType.Armor90: return "комплект броні: вхідна шкода −80% (шолом, нагрудник, рукавиці, чоботи; не знімається; щит — окремо; магія крізь неї)";
                case ArtifactType.FullHeal: return "одноразовий: миттєво все HP і 1 с невразливості";
                case ArtifactType.Lightning: return "одноразовий: 250 магічної шкоди найближчому ворогу (крізь броню)";
                case ArtifactType.Wisdom: return "+20% досвіду";
                default: return "спорядження 5/4/3, без роботи, безкоштовне будівництво";
            }
        }
    }
}
