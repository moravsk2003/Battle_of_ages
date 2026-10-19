using UnityEngine;

namespace BattleOfAges.Combat
{
    /// <summary>
    /// How a hero not led by a player grows and fights (NPC kings, the balance sims; docs/GAME_DESIGN.md §8.1, §12).
    /// Attributes: the tank all into HP, the classless hero by turns, the others all into strength. Skills: the tank
    /// Regeneration (start and a point), Instant heal to the top, then Regeneration; the archer Long shot, then Piercing
    /// arrow; the mage Fire fan, then Meteor; the DD Dash, then Critical hit.
    /// </summary>
    public static class HeroAutoPilot
    {
        /// <summary>Spends every free attribute and skill point the usual way of its class.</summary>
        public static void SpendPoints(GameObject go)
        {
            HeroProgress hero = go.GetComponent<HeroProgress>();
            HeroSkills skills = go.GetComponent<HeroSkills>();
            if (hero == null) return;
            HeroClass cls = hero.Class;
            for (int i = hero.StrengthPoints + hero.HealthPoints; hero.AttributePoints > 0; i++)
            {
                bool health = cls == HeroClass.Tank || (cls == HeroClass.None && i % 2 == 1);
                if (!(health ? hero.SpendOnHealth() : hero.SpendOnStrength())) break;
            }
            if (skills == null) return;
            for (int guard = 0; hero.SkillPoints > 0 && guard < 50; guard++)
            {
                bool learnt = cls switch
                {
                    HeroClass.Tank => skills.LearnRegenStart() || (!skills.CanLearnHeal && skills.LearnRegenMax()) || skills.LearnHeal() || skills.LearnRegenMax(),
                    HeroClass.Archer => skills.LearnLongShot() || skills.LearnPierce(),
                    HeroClass.Mage => skills.LearnFireFan() || skills.LearnMeteor(),
                    HeroClass.Dd => skills.LearnDash() || skills.LearnCriticalHit(),
                    _ => false
                };
                if (!learnt) break;
            }
        }

        /// <summary>
        /// The class's active skill when it pays, against this target: the tank heals below half HP, the mage casts at a
        /// target in range (the meteor, else the fan), the archer makes its next shot a piercing one, the DD dashes at a
        /// target still far away and makes its next swing critical.
        /// </summary>
        public static void UseSkills(CombatUnit me, HealthComponent self, HealthComponent target)
        {
            if (me == null || target == null || !target.IsAlive) return;
            HeroSkills skills = me.GetComponent<HeroSkills>();
            HeroProgress hero = me.GetComponent<HeroProgress>();
            if (skills == null || hero == null || !skills.HasActive) return;
            Vector3 to = target.transform.position - me.transform.position;
            float d = to.magnitude, reach = me.AttackRange;
            switch (hero.Class)
            {
                case HeroClass.Tank: if (self != null && self.CurrentHealth < self.MaxHealth / 2) skills.UseActive(0, to); break;
                case HeroClass.Mage: if (d <= reach && !skills.UseActive(1, to)) skills.UseActive(0, to); break;
                case HeroClass.Archer: if (d <= reach + 0.5f && me.CanAttack) skills.UseActive(0, to); break;
                case HeroClass.Dd:
                    if (d > reach + 3f) skills.UseActive(0, to);
                    else if (d <= reach + 0.5f && me.CanAttack) skills.UseActive(1, to);
                    break;
            }
        }
    }
}
