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

        /// <summary>The name for a bearer of this class: the ×3,5 weapon is its class's own.</summary>
        public static string Name(ArtifactType a, HeroClass cls)
        {
            if (a == ArtifactType.SwordX5 && cls == HeroClass.Archer) return UI.Loc.Pick("Лук ×3,5", "Bow ×3.5");
            if (a == ArtifactType.SwordX5 && cls == HeroClass.Mage) return UI.Loc.Pick("Посох ×3,5", "Staff ×3.5");
            return Name(a);
        }

        public static string Name(ArtifactType a)
        {
            switch (a)
            {
                case ArtifactType.Vampiric: return UI.Loc.Pick("Вампірський", "Vampiric");
                case ArtifactType.SwordX5: return UI.Loc.Pick("Меч ×3,5", "Sword ×3.5");
                case ArtifactType.Armor90: return UI.Loc.Pick("Броня 70%", "Armor 70%");
                case ArtifactType.FullHeal: return UI.Loc.Pick("Повне зцілення", "Full Heal");
                case ArtifactType.Lightning: return UI.Loc.Pick("Блискавка", "Lightning");
                case ArtifactType.Wisdom: return UI.Loc.Pick("Мудрості", "Wisdom");
                default: return UI.Loc.Pick("Центральна імперія", "Central Empire");
            }
        }

        public static string Description(ArtifactType a)
        {
            if (UI.Loc.En)
            {
                switch (a)
                {
                    case ArtifactType.Vampiric: return "10% of damage dealt is restored as HP";
                    case ArtifactType.SwordX5: return "×3.5 damage (occupies weapon slot, cannot be removed)";
                    case ArtifactType.Armor90: return "armor set: −70% incoming damage (helmet, chest, gloves, boots; locked; shield separate; magic pierces it)";
                    case ArtifactType.FullHeal: return "consumable: restores full HP + 1 s invulnerability";
                    case ArtifactType.Lightning: return "consumable: 250 magic damage to nearest enemy (ignores armor)";
                    case ArtifactType.Wisdom: return "+20% experience";
                    default: return "5/4/3 gear, no labor needed, free construction";
                }
            }
            switch (a)
            {
                case ArtifactType.Vampiric: return "10% завданої шкоди повертається як HP";
                case ArtifactType.SwordX5: return "шкода ×3,5 (займає слот зброї, не знімається)";
                case ArtifactType.Armor90: return "комплект броні: вхідна шкода −70% (шолом, нагрудник, рукавиці, чоботи; не знімається; щит — окремо; магія крізь неї)";
                case ArtifactType.FullHeal: return "одноразовий: миттєво все HP і 1 с невразливості";
                case ArtifactType.Lightning: return "одноразовий: 250 магічної шкоди найближчому ворогу (крізь броню)";
                case ArtifactType.Wisdom: return "+20% досвіду";
                default: return "спорядження 5/4/3, без роботи, безкоштовне будівництво";
            }
        }
    }
}
