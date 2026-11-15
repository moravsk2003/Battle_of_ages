using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Combat;
using BattleOfAges.Net;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The lobby of a network game (main menu, docs/GAME_DESIGN.md §14.4), the same screen for the host and its guests: the
    /// campaign to be, everyone in it (who is new, who comes back to a kingdom, who is ready), and one's own name and hero.
    /// A new player chooses a class and a starting artifact; a returning one has its hero already. The host starts the game
    /// once everyone is ready (or, pressing twice, without waiting) and may send a guest out; a guest says it is ready.
    /// </summary>
    public sealed class LobbyUI : MonoBehaviour
    {
        private const float RowHeight = 36f, RowStep = 38f, ListTop = -168f;

        public RectTransform Panel { get; private set; }

        private Action<string> closed;
        private Text campaign, address, status, heroNote, classValue, classDescription, artifactValue, artifactDescription, readyText;
        private RectTransform list, choicePane;
        private InputField nameField;
        private Button startButton, readyButton;
        private int shownVersion = -1;
        private bool synced, ready, closing;
        private int classIndex, artifactIndex;
        private float startArmedUntil;
        private string failedWhy;

        public static LobbyUI Build(RectTransform root, Action<string> closed)
        {
            RectTransform panel = UiTheme.Rect(root, "Lobby", new Vector2(0.5f, 0.5f), new Vector2(0f, -60f), new Vector2(1180f, 650f));
            var ui = panel.gameObject.AddComponent<LobbyUI>();
            ui.Panel = panel;
            ui.closed = closed;
            ui.Make();
            return ui;
        }

        /// <summary>Shown once a lobby is opened or joined (its choices taken from the first word of the host).</summary>
        public void Open()
        {
            shownVersion = -1;
            synced = false;
            ready = false;
            closing = false;
            failedWhy = null;
            startArmedUntil = 0f;
            status.text = "";
            Panel.gameObject.SetActive(true);
            Refresh();
        }

        private void OnEnable()
        {
            NetSession.Failed -= OnFailed;
            NetSession.Failed += OnFailed;
            Loc.Changed -= Refresh;
            Loc.Changed += Refresh;
        }

        private void OnDisable()
        {
            NetSession.Failed -= OnFailed;
            Loc.Changed -= Refresh;
        }

        private void OnFailed(string why) => failedWhy = why;

        private void Update()
        {
            NetSession s = NetSession.Current;
            if (s == null)
            {
                if (!closing) Close(!string.IsNullOrEmpty(failedWhy) ? Loc.Tr(failedWhy) : Loc.Pick("Лобі закрито", "Lobby closed"));
                return;
            }
            if (s.LobbyVersion != shownVersion) Refresh();
            if (startArmedUntil > 0f && Time.unscaledTime > startArmedUntil) { startArmedUntil = 0f; RefreshButtons(s.Lobby); }
        }

        private void Close(string why)
        {
            closing = true;
            Panel.gameObject.SetActive(false);
            closed?.Invoke(why);
        }

        private void Leave()
        {
            NetSession.Stop();
            Close("");
        }

        // ------------------------------------------------------------------ what is chosen here

        private static ArtifactType[] OfferedArtifacts(bool host)
        {
            if (host) return Artifacts.All;
            var offered = new List<ArtifactType>();
            foreach (ArtifactType a in Artifacts.All) if (LobbyHost.GuestMayTake(a)) offered.Add(a);
            return offered.ToArray();
        }

        private ArtifactType[] Offered => OfferedArtifacts(NetSession.IsHost);
        private HeroClass Class => LobbyHost.Classes[classIndex];
        private ArtifactType Artifact => Offered[Mathf.Clamp(artifactIndex, 0, Offered.Length - 1)];

        private void Send()
        {
            NetSession.ChooseInLobby(new NetLobbyChoice
            {
                Name = nameField.text, Class = (int)Class, HasArtifact = true, Artifact = (int)Artifact, Ready = NetSession.IsHost || ready
            });
        }

        /// <summary>The choices on screen from one's place in the lobby (the first time, or when the host did not take one).</summary>
        private void SyncFrom(LobbySeat me)
        {
            int c = Array.IndexOf(LobbyHost.Classes, (HeroClass)me.Class);
            classIndex = c >= 0 ? c : 0;
            int a = Array.IndexOf(Offered, (ArtifactType)me.Artifact);
            artifactIndex = a >= 0 ? a : 0;
            ready = me.Ready && !me.IsHost;
            synced = true;
        }

        private void Step(ref int index, int count, int d) => index = ((index + d) % count + count) % count;

        // ------------------------------------------------------------------ view

        private void Refresh()
        {
            LocText.Detach(address);
            LocText.Detach(campaign);
            LocText.Detach(status);
            LocText.Detach(heroNote);
            LocText.Detach(classValue);
            LocText.Detach(classDescription);
            LocText.Detach(artifactValue);
            LocText.Detach(artifactDescription);
            LocText.Detach(readyText);
            if (startButton != null) LocText.Detach(startButton.GetComponentInChildren<Text>());

            NetSession s = NetSession.Current;
            NetLobby lobby = s != null ? s.Lobby : null;
            shownVersion = s != null ? s.LobbyVersion : -1;
            bool host = NetSession.IsHost;
            address.text = host ? Loc.Pick("Скажіть іншим: ", "Share with others: ") + NetSession.AddressLine() : "";
            if (lobby == null)
            {
                campaign.text = host ? "" : s != null && s.Welcome != null ? Loc.Pick("Завантаження світу…", "Loading world…") : Loc.Pick("З'єднання з хостом…", "Connecting to host…");
                for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
                choicePane.gameObject.SetActive(false);
                heroNote.text = "";
                RefreshButtons(null);
                return;
            }
            campaign.text = Loc.Tr(lobby.Campaign);
            LobbySeat me = lobby.SeatOf(lobby.You);
            if (me != null && (!synced || !string.IsNullOrEmpty(lobby.Said))) SyncFrom(me);
            if (!string.IsNullOrEmpty(lobby.Said)) status.text = Loc.Tr(lobby.Said);
            if (lobby.Starting) status.text = host ? Loc.Pick("Світ завантажується…", "Loading world…") : Loc.Pick("Хост завантажує світ…", "Host is loading the world…");

            for (int i = list.childCount - 1; i >= 0; i--) Destroy(list.GetChild(i).gameObject);
            for (int i = 0; i < lobby.Seats.Count; i++) Row(lobby.Seats[i], i, lobby.You, host && !lobby.Starting);

            bool returning = me != null && me.Returning;
            choicePane.gameObject.SetActive(me != null && !returning && !lobby.Starting);
            string meHero = string.IsNullOrEmpty(me?.Hero) ? "" : ": " + me.Hero;
            heroNote.text = me == null ? ""
                          : returning ? Loc.Pick($"Ви повертаєтеся до свого королівства{meHero}.\nКлас і артефакти вже ваші.", $"You are returning to your kingdom{meHero}.\nYour class and artifacts are already yours.")
                          : lobby.Starting ? Loc.Pick("Обрано: ", "Chosen: ") + HeroClassRules.Name(Class) + " · " + Artifacts.Name(Artifact, Class) : "";
            classValue.text = HeroClassRules.Name(Class);
            classDescription.text = HeroClassRules.Description(Class) + "\n" + TipText.Dim(Loc.Pick("Клас обирається один раз.", "Class is chosen once."));
            artifactValue.text = Artifacts.Name(Artifact, Class);
            artifactDescription.text = Artifacts.Description(Artifact) + (host ? "" : "\n" + TipText.Dim(Loc.Pick("Центральну імперію в мережевій грі бере лише хост.", "Only the host may choose Central Empire in multiplayer.")));
            RefreshButtons(lobby);
        }

        private void RefreshButtons(NetLobby lobby)
        {
            bool host = NetSession.IsHost;
            bool starting = lobby != null && lobby.Starting;
            startButton.gameObject.SetActive(host);
            startButton.interactable = lobby != null && !starting;
            startButton.GetComponentInChildren<Text>().text = startArmedUntil > 0f ? Loc.Pick("Почати без них", "Start Anyway") : Loc.Pick("Почати", "Start");
            readyButton.gameObject.SetActive(!host);
            readyButton.interactable = lobby != null && !starting;
            readyText.text = ready ? Loc.Pick("Не готовий", "Not Ready") : Loc.Pick("Готовий", "Ready");
        }

        private void Row(LobbySeat seat, int index, int you, bool mayKick)
        {
            RectTransform row = UiTheme.Rect(list, "Seat" + seat.Number, new Vector2(0.5f, 1f), new Vector2(0f, -index * RowStep), new Vector2(540f, RowHeight));
            UiTheme.PanelImage(row, UiTheme.SlotSprite, seat.Here ? Color.white : new Color(0.7f, 0.7f, 0.7f));
            bool mine = seat.Number == you;
            string name = seat.Name + (mine ? Loc.Pick(" (ви)", " (you)") : "");
            Text n = UiTheme.Label(UiTheme.Rect(row, "Name", new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(170f, RowHeight)), "Text",
                                   name, 17, TextAnchor.MiddleLeft, mine ? UiTheme.Gold : seat.Here ? UiTheme.Ink : UiTheme.InkDim, mine ? FontStyle.Bold : FontStyle.Normal);
            LocText.Detach(n);
            n.horizontalOverflow = HorizontalWrapMode.Overflow;
            string hero = seat.Returning ? (string.IsNullOrEmpty(seat.Hero) ? Loc.Pick("своє королівство", "their kingdom") : seat.Hero)
                        : HeroClassRules.Name((HeroClass)seat.Class) + (seat.HasArtifact ? " · " + Artifacts.Name((ArtifactType)seat.Artifact, (HeroClass)seat.Class) : "");
            Text h = UiTheme.Label(UiTheme.Rect(row, "Hero", new Vector2(0f, 0.5f), new Vector2(190f, 0f), new Vector2(200f, RowHeight)), "Text",
                          hero, 15, TextAnchor.MiddleLeft, UiTheme.InkDim);
            LocText.Detach(h);
            h.horizontalOverflow = HorizontalWrapMode.Overflow;
            string state = seat.IsHost ? TipText.Title(Loc.Pick("хост", "host"), UiTheme.Gold)
                         : !seat.Here ? Loc.Pick("немає в грі", "offline")
                         : seat.Ready ? TipText.Good(Loc.Pick("готовий", "ready")) : Loc.Pick("обирає", "choosing");
            if (seat.Returning && seat.Here && !seat.IsHost) state = (seat.Ready ? TipText.Good(Loc.Pick("готовий", "ready")) : Loc.Pick("повертається", "returning"));
            Text st = UiTheme.Label(UiTheme.Rect(row, "State", new Vector2(1f, 0.5f), new Vector2(mayKick && seat.Here && !seat.IsHost ? -48f : -12f, 0f), new Vector2(100f, RowHeight)), "Text",
                          state, 15, TextAnchor.MiddleRight, UiTheme.Ink);
            LocText.Detach(st);
            if (mayKick && seat.Here && !seat.IsHost)
            {
                int number = seat.Number;
                UiTheme.TextButton(row, "Kick", "×", new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(32f, 30f), () => NetSession.Kick(number), 20)
                       .GetComponent<Image>().color = new Color(1f, 0.65f, 0.6f);
            }
        }

        // ------------------------------------------------------------------ building

        private void Make()
        {
            UiTheme.PanelImage(Panel);
            PopIn.On(Panel);
            UiTheme.Label(UiTheme.Rect(Panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(600f, 44f)), "Text", "Лобі", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            campaign = UiTheme.Label(UiTheme.Rect(Panel, "Campaign", new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(1000f, 26f)), "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Ink);
            address = UiTheme.Label(UiTheme.Rect(Panel, "Address", new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(1000f, 24f)), "Text", "", 16, TextAnchor.MiddleCenter, UiTheme.Gold);
            UiTheme.Divider(Panel, -114f, 560f);

            Heading(-128f, -290f, "Гравці");
            list = UiTheme.Rect(Panel, "Seats", new Vector2(0.5f, 1f), new Vector2(-290f, ListTop), new Vector2(540f, RowStep * LobbyHost.MaxPlayers));

            Heading(-128f, 290f, "Ваш герой");
            Label(Panel, -170f, 140f, "Ім'я");
            nameField = NameField(new Vector2(390f, -170f));
            nameField.text = NetSession.LocalName;
            nameField.onEndEdit.AddListener(v => { NetSession.LocalName = v; Send(); });
            heroNote = UiTheme.Label(UiTheme.Rect(Panel, "HeroNote", new Vector2(0.5f, 1f), new Vector2(290f, -260f), new Vector2(520f, 80f)), "Text", "", 17, TextAnchor.UpperLeft, UiTheme.Ink);
            heroNote.horizontalOverflow = HorizontalWrapMode.Wrap;

            choicePane = UiTheme.Rect(Panel, "Choice", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1f, 1f));
            classValue = Cycler(choicePane, -222f, "Клас", d => { Step(ref classIndex, LobbyHost.Classes.Length, d); });
            classDescription = Description(choicePane, -248f);
            artifactValue = Cycler(choicePane, -380f, "Артефакт", d => { Step(ref artifactIndex, Offered.Length, d); });
            artifactDescription = Description(choicePane, -406f);

            status = UiTheme.Label(UiTheme.Rect(Panel, "Status", new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(1100f, 28f)), "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Bad);
            startButton = UiTheme.TextButton(Panel, "Start", "Почати", new Vector2(0.5f, 0f), new Vector2(130f, 26f), new Vector2(230f, 56f), PressStart, 22);
            startButton.GetComponent<Image>().color = new Color(1f, 0.92f, 0.7f);
            readyButton = UiTheme.TextButton(Panel, "Ready", "Готовий", new Vector2(0.5f, 0f), new Vector2(130f, 26f), new Vector2(230f, 56f), () => { ready = !ready; Send(); RefreshButtons(NetSession.Current?.Lobby); }, 22);
            readyText = readyButton.GetComponentInChildren<Text>();
            UiTheme.TextButton(Panel, "Leave", "Вийти з лобі", new Vector2(0.5f, 0f), new Vector2(-130f, 26f), new Vector2(230f, 56f), Leave, 20);
            Panel.gameObject.SetActive(false);
        }

        private void PressStart()
        {
            string why = NetSession.StartLobbyGame(force: startArmedUntil > 0f);
            startArmedUntil = 0f;
            if (why != null)
            {
                status.text = Loc.Tr(why) + Loc.Pick(". Натисніть ще раз, щоб почати без очікування.", ". Press again to start without waiting.");
                startArmedUntil = Time.unscaledTime + 5f;
            }
            RefreshButtons(NetSession.Current?.Lobby);
        }

        private void Heading(float y, float x, string text)
        {
            RectTransform h = UiTheme.Rect(Panel, text, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(520f, 34f));
            UiTheme.Label(h, "Text", text, 22, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
        }

        private static void Label(RectTransform parent, float y, float x, string text)
        {
            RectTransform l = UiTheme.Rect(parent, text, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(220f, 40f));
            UiTheme.Label(l, "Text", text, 18, TextAnchor.MiddleLeft, UiTheme.Ink);
        }

        /// <summary>A row of the hero: its name, ‹ value ›; a step is sent to the host at once.</summary>
        private Text Cycler(RectTransform parent, float y, string label, Action<int> step)
        {
            Label(parent, y, 140f, label);
            RectTransform v = UiTheme.Rect(parent, label + "_Value", new Vector2(0.5f, 1f), new Vector2(410f, y), new Vector2(200f, 40f));
            UiTheme.PanelImage(v, UiTheme.SlotSprite);
            Text t = UiTheme.Label(v, "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.TextButton(parent, label + "_Prev", "‹", new Vector2(0.5f, 1f), new Vector2(284f, y), new Vector2(40f, 40f), () => { step(-1); Send(); Refresh(); }, 22);
            UiTheme.TextButton(parent, label + "_Next", "›", new Vector2(0.5f, 1f), new Vector2(536f, y), new Vector2(40f, 40f), () => { step(1); Send(); Refresh(); }, 22);
            return t;
        }

        private static Text Description(RectTransform parent, float y)
        {
            RectTransform d = UiTheme.Rect(parent, "Description", new Vector2(0.5f, 1f), new Vector2(290f, y - 50f), new Vector2(520f, 100f));
            Text t = UiTheme.Label(d, "Text", "", 15, TextAnchor.UpperLeft, UiTheme.Ink);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            return t;
        }

        private InputField NameField(Vector2 at)
        {
            RectTransform f = UiTheme.Rect(Panel, "NameField", new Vector2(0.5f, 1f), at, new Vector2(300f, 40f));
            UiTheme.PanelImage(f, UiTheme.SlotSprite);
            Text text = UiTheme.Label(UiTheme.Rect(f, "Input", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 34f)), "Text", "", 18, TextAnchor.MiddleCenter, UiTheme.Ink);
            text.supportRichText = false;
            Text placeholder = UiTheme.Label(UiTheme.Rect(f, "Placeholder", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(280f, 34f)), "Text", "ваше ім'я", 16, TextAnchor.MiddleCenter, UiTheme.InkDim, FontStyle.Italic);
            InputField input = f.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = LobbyHost.MaxNameLength;
            return input;
        }
    }
}
