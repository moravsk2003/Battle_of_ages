using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.InputSystem;
using BattleOfAges.Net;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The network pause on screen (docs/GAME_DESIGN.md §14.1, §14.4): why the game stands and for how long; to the host, when
    /// a player has left, its choice (a bot, the lobby, waiting), with the session's "always a bot"; to the others, that the
    /// host decides; a button to lift a pause whoever may. F6 pauses the game for everyone (a player: PausesPerPlayer a game).
    /// </summary>
    public sealed class NetPauseUI : MonoBehaviour
    {
        private static NetPauseUI instance;
        private GameObject window, hostChoice, liftButton;
        private Text title, line, alwaysBotLabel;
        private bool shown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance == null) instance = new GameObject("NetPauseUI").AddComponent<NetPauseUI>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            Build();
            window.SetActive(false);
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy() { if (instance == this) instance = null; }

        private void Update()
        {
            NetPauseState p = NetSession.PauseState;
            if (p != null && UniversalInput.IsKeyDown(KeyCode.F6) && !GameWindows.Paused)
            {
                if (p.On)
                {
                    if (NetSession.CanUnpause) NetSession.RequestPause(false);
                    else StrategyHud.Notify(p.AwaitingHost ? "Гра чекає рішення хоста" : $"Зняти паузу може той, хто поставив, або будь-хто за {NetSession.AnyoneUnpausesAfter:0} с");
                }
                else if (NetSession.CanPause) NetSession.RequestPause(true);
                else StrategyHud.Notify("Ваші паузи на цю гру скінчились");
            }
            bool on = p != null && p.On;
            if (on != shown)
            {
                shown = on;
                window.SetActive(on);
                if (on) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
                else if (!GameWindows.AnyModalOpen && !GameWindows.Paused && Managers.GameContext.Modes != null && Managers.GameContext.Modes.CurrentMode == Managers.GameMode.FirstPerson)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            if (!on) return;
            int seconds = Mathf.FloorToInt(NetSession.PauseSeconds);
            title.text = p.AwaitingHost ? "Гравець вийшов" : "Пауза";
            string said = p.AwaitingHost
                ? (NetSession.IsHost ? (p.HostWaits ? $"Чекаємо на: {NetSession.WaitingNames}. Їх повернення зніме паузу." : $"{p.Why}. Що далі?")
                                     : (p.HostWaits ? "Хост чекає, поки гравець повернеться" : $"{p.Why}. Чекаємо рішення хоста"))
                : p.Why + (NetSession.CanUnpause ? "" : $". Зняти її може той, хто поставив, або будь-хто за {Mathf.Max(0, Mathf.CeilToInt(NetSession.AnyoneUnpausesAfter - NetSession.PauseSeconds))} с");
            line.text = $"{said}\n<color=#a89c86>{seconds / 60}:{seconds % 60:00}</color>";
            hostChoice.SetActive(NetSession.IsHost && p.AwaitingHost);
            liftButton.SetActive(NetSession.CanUnpause);
            alwaysBotLabel.text = "Завжди ставити бота: " + (NetSession.AlwaysBot ? "так" : "ні");
        }

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("NetPauseCanvas", 130);
            canvas.transform.SetParent(transform, false);
            window = canvas.gameObject;
            RectTransform panel = UiTheme.Rect(canvas.transform, "Window", new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(640f, 330f));
            UiTheme.PanelImage(panel);
            title = UiTheme.Label(UiTheme.Rect(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(560f, 44f)), "Text", "Пауза", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            line = UiTheme.Label(UiTheme.Rect(panel, "Line", new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(580f, 86f)), "Text", "", 19, TextAnchor.UpperCenter, UiTheme.Ink);
            line.supportRichText = true;

            var choice = new GameObject("HostChoice", typeof(RectTransform));
            choice.transform.SetParent(panel, false);
            var cr = (RectTransform)choice.transform;
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
            hostChoice = choice;
            UiTheme.TextButton(cr, "Bot", "Поставити бота", new Vector2(0.5f, 0f), new Vector2(-200f, 96f), new Vector2(190f, 50f), NetSession.HostPutBots, 18);
            UiTheme.TextButton(cr, "Lobby", "Перейти в лобі", new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(190f, 50f), NetSession.HostBackToLobby, 18);
            UiTheme.TextButton(cr, "Wait", "Чекати", new Vector2(0.5f, 0f), new Vector2(200f, 96f), new Vector2(190f, 50f), NetSession.HostWait, 18);
            Button always = UiTheme.TextButton(cr, "AlwaysBot", "", new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(380f, 42f), () => NetSession.AlwaysBot = !NetSession.AlwaysBot, 16);
            alwaysBotLabel = always.GetComponentInChildren<Text>();
            Tooltip.Set(always.gameObject, () => TipText.Title("Завжди ставити бота", UiTheme.Gold) + "\nДо кінця цієї гри: хто вийде, того королівство одразу стереже бот, без паузи.");

            liftButton = UiTheme.TextButton(panel, "Lift", "Зняти паузу (F6)", new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(260f, 46f), () => NetSession.RequestPause(false), 18).gameObject;
        }
    }
}
