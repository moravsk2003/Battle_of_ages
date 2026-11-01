using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.Resources;
using BattleOfAges.RTS;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The toast column at the bottom right: gathered wood and ore, new workers, finished buildings, attacks, and any
    /// message (StrategyHud.Notify). A toast of the same kind within a moment merges into the last one ("+5 руди").
    /// </summary>
    public sealed class HudToasts : MonoBehaviour
    {
        public static HudToasts Active { get; private set; }

        private const float Life = 4f, MergeWithin = 2.5f, Spacing = 48f;
        private const int MaxShown = 5;

        private RectTransform column;
        private readonly List<(RectTransform rt, Text text, CanvasGroup group, float born, string key, int amount)> toasts = new List<(RectTransform, Text, CanvasGroup, float, string, int)>();
        private int lastWood = -1, lastOre = -1, lastWorkers = -1, lastBuildings = -1;
        private float pollTimer;

        public static HudToasts Create(RectTransform root)
        {
            RectTransform column = UiTheme.Rect(root, "Toasts", new Vector2(1f, 0f), new Vector2(-24f, 300f), new Vector2(360f, 260f));
            HudToasts t = column.gameObject.AddComponent<HudToasts>();
            t.column = column;
            return t;
        }

        private void OnEnable()
        {
            Active = this;
            AttackAlerts.Raised += OnAttackAlert;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            AttackAlerts.Raised -= OnAttackAlert;
        }

        private void OnAttackAlert(string what, Vector3 where)
        {
            Show("alert:" + what, 1, n => (n > 1 ? $"Напад: {what} ×{n}" : $"Напад: {what}") + $"  ·  {HudTips.Key(InputSystem.KeyAction.AlertJump)} — туди", UiTheme.Bad);
            UiSound.Play(UiSound.Kind.Alert);
        }

        /// <summary>Shows a toast, or adds amount to a recent one of the same key; color: an alert (red text on a reddish plate).</summary>
        public void Show(string key, int amount, System.Func<int, string> text, Color? color = null)
        {
            for (int i = toasts.Count - 1; i >= 0; i--)
                if (toasts[i].key == key && Time.unscaledTime - toasts[i].born < MergeWithin)
                {
                    var t = toasts[i];
                    t.amount += amount; t.born = Time.unscaledTime;
                    t.text.text = text(t.amount);
                    toasts[i] = t;
                    return;
                }
            RectTransform rt = UiTheme.Rect(column, "Toast", new Vector2(1f, 0f), Vector2.zero, new Vector2(340f, 42f));
            UiTheme.PanelImage(rt);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            Text label = UiTheme.Label(rt, "Text", text(amount), 19, TextAnchor.MiddleCenter, color ?? UiTheme.Gold);
            if (color.HasValue) rt.GetComponent<Image>().color = new Color(1f, 0.75f, 0.7f);
            toasts.Add((rt, label, group, Time.unscaledTime, key, amount));
            if (toasts.Count > MaxShown) { Destroy(toasts[0].rt.gameObject); toasts.RemoveAt(0); }
        }

        private void Update()
        {
            WatchTheSettlement();
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                float age = Time.unscaledTime - toasts[i].born;
                if (age > Life) { Destroy(toasts[i].rt.gameObject); toasts.RemoveAt(i); continue; }
                toasts[i].group.alpha = Mathf.Clamp01((Life - age) / 0.6f) * Mathf.Clamp01(age / 0.15f + 0.3f);
            }
            for (int i = 0; i < toasts.Count; i++)
                toasts[i].rt.anchoredPosition = Vector2.Lerp(toasts[i].rt.anchoredPosition, new Vector2(0f, (toasts.Count - 1 - i) * Spacing), 0.25f);
        }

        /// <summary>A few times a second: what grew since last time (wood, ore, workers, buildings) becomes a toast.</summary>
        private void WatchTheSettlement()
        {
            pollTimer -= Time.unscaledDeltaTime;
            if (pollTimer > 0f) return;
            pollTimer = 0.3f;
            ResourceManager res = ResourceManager.Instance;
            int wood = res != null ? res.WoodCount : 0, ore = res != null ? res.OreCount : 0, workers = WorkerRegistry.WorkerCount;
            if (lastWood >= 0 && wood > lastWood) Show("wood", wood - lastWood, n => $"+{n} деревини");
            if (lastOre >= 0 && ore > lastOre) Show("ore", ore - lastOre, n => $"+{n} руди");
            if (lastWorkers >= 0 && workers > lastWorkers) Show("worker", workers - lastWorkers, n => n > 1 ? $"Нові робітники: {n}" : "Новий робітник");
            int buildings = BuildingNode.Active.Count;
            if (lastBuildings >= 0 && buildings > lastBuildings && !World.WorldSettings.Bounded) // (a campaign's sites say so when done)
            {
                BuildingNode b = BuildingNode.Active[buildings - 1];
                Show("build" + buildings, 1, n => "Збудовано: " + BuildingData.GetDefinition(b.Type).DisplayName);
            }
            lastWood = wood; lastOre = ore; lastWorkers = workers; lastBuildings = buildings;
        }
    }
}
