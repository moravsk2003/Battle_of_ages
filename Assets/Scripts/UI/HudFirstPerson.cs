using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.Crafting;
using BattleOfAges.Managers;
using InputKey = BattleOfAges.InputSystem.KeyAction;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The first-person HUD: the crosshair; the hero's health (with a pale trail of what was just lost), level and
    /// experience at the bottom left; the class skills (Q / R, cooldown rings) and the one-shot artifacts (1 / 2) at the
    /// bottom middle. Lives on the first-person group, so it does nothing while the map is open.
    /// </summary>
    public sealed class HudFirstPerson : MonoBehaviour
    {
        private const float BarWidth = 290f;
        private static readonly InputKey[] AbilityKeys = { InputKey.Ability, InputKey.Ability2, InputKey.UseFullHeal, InputKey.UseLightning };

        private RectTransform crosshair, hpFill, hpTrail, xpFill, abilityBar;
        private Text hpText, levelText;
        private float trail = 1f, trailHold;
        private Shown hpShown, levelShown;
        private readonly Shown[] abilitySeconds = new Shown[4], abilityKeys = new Shown[4];
        private readonly List<(RectTransform rt, Image icon, Image cooldown, Text seconds, Text key)> slots = new List<(RectTransform, Image, Image, Text, Text)>();
        private HealthComponent health;
        private HeroSkills skills;
        private HeroArtifacts artifacts;
        private HeroProgress progress;

        public static HudFirstPerson Create(RectTransform group)
        {
            HudFirstPerson h = group.gameObject.AddComponent<HudFirstPerson>();
            h.Build(group);
            return h;
        }

        private void Build(RectTransform group)
        {
            crosshair = UiTheme.Rect(group, "Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f));
            void Tick(Vector2 pos, Vector2 size)
            {
                RectTransform r = UiTheme.Rect(crosshair, "Tick", new Vector2(0.5f, 0.5f), pos, size);
                var img = r.gameObject.AddComponent<Image>();
                img.sprite = UiTheme.BarSprite; img.type = Image.Type.Sliced; img.color = new Color(1f, 0.96f, 0.88f, 0.85f); img.raycastTarget = false;
                r.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.5f);
            }
            Tick(Vector2.zero, new Vector2(4f, 4f));
            Tick(new Vector2(0f, 11f), new Vector2(2.5f, 8f)); Tick(new Vector2(0f, -11f), new Vector2(2.5f, 8f));
            Tick(new Vector2(11f, 0f), new Vector2(8f, 2.5f)); Tick(new Vector2(-11f, 0f), new Vector2(8f, 2.5f));

            RectTransform vitals = UiTheme.Rect(group, "Vitals", new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(380f, 86f));
            UiTheme.PanelImage(vitals);
            UiTheme.IconImage(vitals, UiTheme.Icon.Heart, new Vector2(0f, 0.5f), new Vector2(14f, 6f), 40f);
            RectTransform back = UiTheme.Rect(vitals, "Bar", new Vector2(0f, 1f), new Vector2(64f, -14f), new Vector2(296f, 26f));
            UiTheme.PanelImage(back, UiTheme.SlotSprite);
            hpTrail = UiTheme.Rect(back, "Trail", new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(BarWidth, 20f));
            UiTheme.PanelImage(hpTrail, UiTheme.BarSprite, new Color(1f, 0.86f, 0.6f, 0.85f));
            hpFill = UiTheme.Rect(back, "Fill", new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(BarWidth, 20f));
            UiTheme.PanelImage(hpFill, UiTheme.BarSprite, new Color(0.72f, 0.16f, 0.13f));
            hpText = UiTheme.Label(back, "Value", "", 18, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);

            // Hero level and the experience to the next one
            RectTransform lv = UiTheme.Rect(vitals, "Level", new Vector2(0f, 0f), new Vector2(12f, 10f), new Vector2(48f, 24f));
            levelText = UiTheme.Label(lv, "Text", "", 15, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            RectTransform xpBack = UiTheme.Rect(vitals, "Xp", new Vector2(0f, 0f), new Vector2(64f, 16f), new Vector2(296f, 12f));
            UiTheme.PanelImage(xpBack, UiTheme.SlotSprite);
            xpFill = UiTheme.Rect(xpBack, "Fill", new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 7f));
            UiTheme.PanelImage(xpFill, UiTheme.BarSprite, UiTheme.Gold);

            // Q / R skills and the one-shot artifacts (1 / 2) at the bottom middle
            abilityBar = UiTheme.Rect(group, "Abilities", new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(4 * 86f, 80f));
            foreach (InputKey key in AbilityKeys) slots.Add(Slot(key));
            for (int i = 0; i < slots.Count; i++)
            {
                int index = i;
                Tooltip.Set(slots[i].rt.gameObject, () => AbilityTip(index));
            }
        }

        private (RectTransform, Image, Image, Text, Text) Slot(InputKey key)
        {
            RectTransform rt = UiTheme.Rect(abilityBar, "Slot_" + key, new Vector2(0f, 0f), Vector2.zero, new Vector2(72f, 72f));
            UiTheme.PanelImage(rt, UiTheme.SlotSprite);
            RectTransform ic = UiTheme.Rect(rt, "Icon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(50f, 50f));
            Image icon = ic.gameObject.AddComponent<Image>();
            icon.preserveAspect = true; icon.raycastTarget = false;
            RectTransform cd = UiTheme.Child(rt, "Cooldown", Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            Image cool = cd.gameObject.AddComponent<Image>();
            cool.sprite = UiTheme.BarSprite; cool.color = new Color(0f, 0f, 0f, 0.62f); cool.raycastTarget = false;
            cool.type = Image.Type.Filled; cool.fillMethod = Image.FillMethod.Radial360; cool.fillOrigin = (int)Image.Origin360.Top; cool.fillClockwise = false;
            Text secs = UiTheme.Label(rt, "Seconds", "", 22, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
            RectTransform k = UiTheme.Rect(rt, "Key", new Vector2(0f, 1f), new Vector2(4f, -2f), new Vector2(30f, 22f));
            Text keyText = UiTheme.Label(k, "Text", "", 15, TextAnchor.UpperLeft, UiTheme.Gold, FontStyle.Bold);
            rt.gameObject.SetActive(false);
            return (rt, icon, cool, secs, keyText);
        }

        private void Update()
        {
            if (health == null)
            {
                health = GameContext.PlayerHealth;
                if (health == null) return;
                skills = health.GetComponent<HeroSkills>();
                artifacts = health.GetComponent<HeroArtifacts>();
                progress = health.GetComponent<HeroProgress>();
            }
            crosshair.gameObject.SetActive(!Cursor.visible && Time.timeScale > 0f);
            UpdateHealth();
            UpdateLevel();
            UpdateAbilities();
        }

        private void UpdateHealth()
        {
            float f = health.MaxHealth > 0 ? Mathf.Clamp01(health.CurrentHealth / (float)health.MaxHealth) : 0f;
            hpFill.sizeDelta = new Vector2(BarWidth * f, 20f);
            // the lost part stays pale for a moment, then drains (healing jumps it up at once)
            if (f >= trail) { trail = f; trailHold = 0f; }
            else if ((trailHold += Time.unscaledDeltaTime) > 0.45f) trail = Mathf.MoveTowards(trail, f, Time.unscaledDeltaTime * 0.6f);
            hpTrail.sizeDelta = new Vector2(BarWidth * trail, 20f);
            if (hpShown.Changed(Shown.Key(health.CurrentHealth, health.MaxHealth))) hpText.text = $"{health.CurrentHealth} / {health.MaxHealth}";
        }

        private void UpdateLevel()
        {
            if (progress == null) return;
            if (levelShown.Changed(progress.Level)) levelText.text = "Рів. " + progress.Level;
            float f = Mathf.Clamp01(progress.Xp / (float)Mathf.Max(1, progress.XpToNextLevel));
            xpFill.sizeDelta = new Vector2(292f * f, 7f);
        }

        /// <summary>Which slots show (a skill the class has, an artifact held), their icons, cooldown rings and keys.</summary>
        private void UpdateAbilities()
        {
            int shown = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                bool on;
                float left = 0f, total = 1f;
                if (i < 2)
                {
                    on = skills != null && skills.HasActiveIn(i);
                    if (on) { left = skills.CooldownLeftIn(i); total = Mathf.Max(0.01f, skills.CooldownOf(i)); s.icon.sprite = SkillIcon(skills.Class, i); }
                }
                else
                {
                    ArtifactType a = i == 2 ? ArtifactType.FullHeal : ArtifactType.Lightning;
                    on = artifacts != null && artifacts.Has(a);
                    if (on) s.icon.sprite = UiTheme.IconSprite(a == ArtifactType.FullHeal ? UiTheme.Icon.Heart : UiTheme.Icon.Bolt);
                }
                if (s.rt.gameObject.activeSelf != on) s.rt.gameObject.SetActive(on);
                if (!on) continue;
                s.rt.anchoredPosition = new Vector2(shown++ * 86f, 0f);
                s.cooldown.fillAmount = left > 0f ? left / total : 0f;
                int secondsKey = left <= 0f ? -1 : left >= 60f ? 100000 + Mathf.CeilToInt(left / 60f) : Mathf.CeilToInt(left);
                if (abilitySeconds[i].Changed(secondsKey)) s.seconds.text = secondsKey < 0 ? "" : secondsKey >= 100000 ? (secondsKey - 100000) + " хв" : secondsKey.ToString();
                s.icon.color = left > 0f ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
                if (abilityKeys[i].Changed((long)InputSystem.KeybindsManager.GetKey(AbilityKeys[i]))) s.key.text = HudTips.Key(AbilityKeys[i]);
            }
            abilityBar.sizeDelta = new Vector2(Mathf.Max(1, shown) * 86f - 14f, 72f);
        }

        private static Sprite SkillIcon(HeroClass c, int slot)
        {
            switch (c)
            {
                case HeroClass.Tank: return UiTheme.IconSprite(UiTheme.Icon.Heart);
                case HeroClass.Mage: return UiTheme.IconSprite(UiTheme.Icon.Fire);
                case HeroClass.Archer: return UiTheme.ItemSprite(ItemType.Bow, 4);
                default: return slot == 0 ? UiTheme.ItemSprite(ItemType.Boots, 4) : UiTheme.ItemSprite(ItemType.Sword, 4);
            }
        }

        private string AbilityTip(int i)
        {
            if (i < 2)
            {
                if (skills == null) return null;
                float left = skills.CooldownLeftIn(i);
                return TipText.Title(skills.ActiveName(i), UiTheme.Gold) + "  " + TipText.Key(HudTips.Key(AbilityKeys[i]))
                       + "\n" + (left > 0f ? TipText.Dim($"Перезарядка ще {left:0} с") : TipText.Good("Готове"))
                       + "\n" + TipText.Dim($"Перезарядка {skills.CooldownOf(i):0} с. Опис — у вікні героя ") + TipText.Key(HudTips.Key(InputKey.Hero));
            }
            ArtifactType a = i == 2 ? ArtifactType.FullHeal : ArtifactType.Lightning;
            return TipText.Title(Artifacts.Name(a), UiTheme.Gold) + "  " + TipText.Key(HudTips.Key(AbilityKeys[i])) + "\n" + Artifacts.Description(a) + "\n" + TipText.Dim("Одноразовий: після використання зникає");
        }
    }
}
