using UnityEngine;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Resources;
using BattleOfAges.UI;

namespace BattleOfAges.Building
{
    /// <summary>
    /// The level (I..V) of the settlement's walls, apart from the ore each segment is built of (that is its strength):
    /// it raises the extra damage of those fighting from the walls (§10.5). Raised for all the walls at once, like the
    /// town hall; each step costs ore of the next tier (II needs tier I, ... V needs tier IV).
    /// </summary>
    public static class Fortifications
    {
        public const int MaxLevel = 5;

        private static int level = 1;
        public static int Level { get => level; set => level = Mathf.Clamp(value, 1, MaxLevel); }

        /// <summary>Extra damage of a unit on the walls at their level (on top of the height bonus).</summary>
        public static float DamageBonus => GameBalance.WallLevelDamageBonus[level];

        public static void UpgradeCost(int toLevel, out int wood, out int ore, out int tier)
        {
            int l = Mathf.Clamp(toLevel, 2, MaxLevel);
            wood = GameBalance.WallLevelWood[l];
            ore = GameBalance.WallLevelOre[l];
            tier = l - 1;
            if (CentralEmpire.Active) wood = ore = 0;
        }

        public static bool CanUpgrade(out string reason)
        {
            reason = null;
            if (level >= MaxLevel) { reason = "Найвищий рівень стін"; return false; }
            if (WallSegment.Active.Count == 0) { reason = "Спершу збудуйте стіну"; return false; }
            UpgradeCost(level + 1, out int wood, out int ore, out int tier);
            ResourceManager rm = ResourceManager.Instance;
            if (rm == null) return true;
            if (rm.WoodCount < wood) { reason = $"Потрібно {wood} деревини"; return false; }
            if (rm.GetOreCountAtLeast(tier) < ore) { reason = $"Потрібно {ore} руди рів. {tier}+"; return false; }
            return true;
        }

        public static bool TryUpgrade()
        {
            if (!CanUpgrade(out string reason))
            {
                StrategyHud.Notify(reason);
                return false;
            }
            UpgradeCost(level + 1, out int wood, out int ore, out int tier);
            ResourceManager rm = ResourceManager.Instance;
            if (rm != null)
            {
                rm.ConsumeResource(ResourceType.Wood, wood);
                rm.ConsumeOreAtLeast(tier, ore);
            }
            level++;
            StrategyHud.Notify($"Стіни {ItemTypeNames.Roman(level)}: +{Mathf.RoundToInt(DamageBonus * 100f)}% урону зі стін");
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => level = 1;
    }
}
