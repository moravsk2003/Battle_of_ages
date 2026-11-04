using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Building;
using BattleOfAges.InputSystem;
using BattleOfAges.Net;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Held down (Tab by default): who is in this game, and if nobody else is, how to let them in. While a wall is being placed
    /// Tab picks its ore, so the list stays away then.
    /// </summary>
    public sealed class HudPlayerList : MonoBehaviour
    {
        private const float Width = 460f, Margin = 18f;

        private RectTransform panel;
        private Text text;
        private string shown;

        /// <summary>With no key held (tests).</summary>
        public bool ShownAnyway { get; set; }

        public static HudPlayerList Create(RectTransform root)
        {
            RectTransform holder = UiTheme.Rect(root, "PlayerList", new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(Width, 0f));
            HudPlayerList list = holder.gameObject.AddComponent<HudPlayerList>();
            list.panel = UiTheme.Rect(holder, "Panel", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width, 100f));
            UiTheme.PanelImage(list.panel);
            list.text = UiTheme.Label(list.panel, "Text", "", 20, TextAnchor.UpperLeft, UiTheme.Ink);
            list.text.horizontalOverflow = HorizontalWrapMode.Wrap;
            list.text.verticalOverflow = VerticalWrapMode.Overflow;
            list.text.supportRichText = true;
            list.text.rectTransform.offsetMin = new Vector2(Margin, Margin);
            list.text.rectTransform.offsetMax = new Vector2(-Margin, -Margin);
            list.panel.gameObject.SetActive(false);
            return list;
        }

        private void Update()
        {
            BuildingPlacementSystem placer = BuildingPlacementSystem.Instance;
            bool show = ShownAnyway || (KeybindsManager.GetKeyHeld(KeyAction.PlayerList) && !GameWindows.BlockGameplay && (placer == null || !placer.IsPlacing));
            if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
            if (!show) return;
            NetSession s = NetSession.Current;
            string now = s == null ? Describe(false, false, 0, null, null)
                                   : Describe(true, NetSession.IsHost, s.Number, s.Players, NetSession.IsHost ? NetSession.AddressLine() : null);
            if (now == shown) return;
            shown = now;
            text.text = now;
            panel.sizeDelta = new Vector2(Width, text.preferredHeight + Margin * 2f);
        }

        /// <summary>The list's words: alone with the game closed, alone with it open, or everyone by name.</summary>
        public static string Describe(bool inSession, bool host, int self, IReadOnlyList<int> players, string address)
        {
            var b = new StringBuilder("<b>Гравці</b>\n");
            if (!inSession)
                return b.Append("Ви граєте самі.\nЩоб запросити інших: Esc → «Відкрити гру в мережі».").ToString();
            foreach (int who in players)
            {
                b.Append(who == 0 ? "Хост" : "Гравець " + who);
                if (who == self) b.Append(" (ви)");
                b.Append('\n');
            }
            if (players.Count <= 1) b.Append("Поки більше нікого немає.\n");
            if (host && !string.IsNullOrEmpty(address)) b.Append("Гру відкрито: ").Append(address);
            return b.ToString().TrimEnd('\n');
        }
    }
}
