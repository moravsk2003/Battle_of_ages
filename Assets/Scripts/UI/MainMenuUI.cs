using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using BattleOfAges.Managers;
using BattleOfAges.SaveLoad;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Start screen (MainMenu scene): Continue (the latest save), New game, Load (slot list), Settings (sensitivity,
    /// volume) and Quit, on a painted dusk sky with the title. Built in code with UiTheme.
    /// </summary>
    public sealed class MainMenuUI : MonoBehaviour
    {
        private RectTransform menu, settings;
        private CampaignSetupUI campaign;
        private Button continueButton;
        private Text continueInfo;

        private void Start()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (FindFirstObjectByType<EventSystem>() == null)
#if ENABLE_INPUT_SYSTEM
                new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            if (SettingsManager.Instance == null) new GameObject("SettingsManager").AddComponent<SettingsManager>();
            Build();
            RefreshContinue();
            ResumeNetworkLobby();
        }

        /// <summary>Back from a game to its lobby (NetSession.HostBackToLobby): the host opens it with the autosave, a guest joins its host again.</summary>
        private void ResumeNetworkLobby()
        {
            if (Net.NetSession.PendingLobbySave != null)
            {
                SaveData save = Net.NetSession.PendingLobbySave;
                string name = Net.NetSession.PendingLobbyName ?? "";
                Net.NetSession.PendingLobbySave = null;
                HostLobby(null, save, name);
                return;
            }
            string address = Net.NetSession.PendingRejoin;
            if (string.IsNullOrEmpty(address)) return;
            Net.NetSession.PendingRejoin = null;
            StartCoroutine(Rejoin(address));
        }

        private System.Collections.IEnumerator Rejoin(string address)
        {
            ShowJoin(true);
            joinAddress.text = address;
            joinStatus.text = "Хост повертає всіх у лобі…";
            // The host saves and opens its lobby a few seconds after; till then its old game still answers
            yield return new WaitForSecondsRealtime(6f);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                JoinGame();
                float until = Time.realtimeSinceStartup + 16f;
                while (joining && Time.realtimeSinceStartup < until) yield return null;
                if (Net.NetSession.Current != null && Net.NetSession.Current.Seated) yield break;
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        private void RefreshContinue()
        {
            int slot = SaveSystem.LatestSlot();
            continueButton.interactable = slot >= 0;
            if (slot >= 0)
            {
                SaveMeta m = SaveSystem.Meta(slot);
                continueInfo.text = m.Exists ? $"{SaveSystem.SlotName(slot)}  ·  {m.SavedAt:dd.MM HH:mm}" : "";
            }
            else continueInfo.text = "Збережень ще немає";
        }

        private void Continue()
        {
            int slot = SaveSystem.LatestSlot();
            if (slot >= 0) SaveSystem.Load(slot);
        }

        /// <summary>The new campaign screen (M1): its world and hero are chosen before it starts.</summary>
        private void ShowCampaign(bool show)
        {
            menu.gameObject.SetActive(!show);
            campaign.Panel.gameObject.SetActive(show);
        }

        private RectTransform joinPanel;
        private InputField joinAddress;
        private Text joinStatus;
        private CampaignSetupUI networkCampaign;
        private LobbyUI lobby;
        private bool joining;

        private void ShowJoin(bool show)
        {
            menu.gameObject.SetActive(!show);
            joinPanel.gameObject.SetActive(show);
            if (show) joinStatus.text = "";
        }

        /// <summary>To a host by its address: its lobby comes once it answers (or the world, to one coming back to a game on).</summary>
        private void JoinGame()
        {
            string address = joinAddress.text.Trim();
            if (string.IsNullOrEmpty(address)) { joinStatus.text = "Вкажіть адресу хоста"; return; }
            Net.NetSession.Failed -= OnJoinFailed;
            Net.NetSession.Failed += OnJoinFailed;
            joining = Net.NetSession.Join(address);
            joinStatus.text = joining ? "З'єднання…" : "Адреса не схожа на IP, наприклад 192.168.0.5";
        }

        private void OnJoinFailed(string why)
        {
            joining = false;
            if (joinStatus != null) joinStatus.text = why;
        }

        private void OnDestroy() => Net.NetSession.Failed -= OnJoinFailed;

        private void Update()
        {
            // A guest in: to the lobby (one coming back to a game on goes straight into the world instead)
            Net.NetSession s = Net.NetSession.Current;
            if (joining && s != null && s.Seated) { joining = false; ShowLobby(); }
        }

        /// <summary>A lobby of one's own: a new network campaign's choices, or a save (§14.4).</summary>
        private void HostLobby(CampaignSetup setup, SaveData save, string saveName)
        {
            networkCampaign.Panel.gameObject.SetActive(false);
            if (!Net.NetSession.HostLobby(setup, save, saveName)) { ShowJoin(true); joinStatus.text = $"Порт {Net.NetLink.DefaultPort} зайнятий"; return; }
            ShowLobby();
        }

        private void HostFromSave()
        {
            SaveSlotsUI.OpenToPick(slot =>
            {
                SaveData d = SaveSystem.ReadSlot(slot);
                if (d == null) { joinStatus.text = "Збереження не читається"; return; }
                HostLobby(null, d, SaveSystem.SlotName(slot));
            });
        }

        private void ShowLobby()
        {
            menu.gameObject.SetActive(false);
            joinPanel.gameObject.SetActive(false);
            lobby.Open();
        }

        private void LobbyClosed(string why)
        {
            ShowJoin(true);
            joinStatus.text = why ?? "";
        }

        private void BuildJoinPanel(RectTransform root)
        {
            joinPanel = UiTheme.Rect(root, "Join", new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(640f, 490f));
            UiTheme.PanelImage(joinPanel);
            PopIn.On(joinPanel);
            UiTheme.Label(UiTheme.Rect(joinPanel, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(560f, 44f)), "Text", "Гра в мережі", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.Divider(joinPanel, -66f, 400f);
            Text hint = UiTheme.Label(UiTheme.Rect(joinPanel, "Hint", new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(580f, 56f)), "Text",
                "Адреса хоста: він бачить її у своєму лобі. Ви потрапите в лобі, оберете героя, а гра почнеться, коли хост натисне «Почати».", 16, TextAnchor.UpperCenter, UiTheme.InkDim);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform f = UiTheme.Rect(joinPanel, "Address", new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(380f, 46f));
            UiTheme.PanelImage(f, UiTheme.SlotSprite);
            Text text = UiTheme.Label(UiTheme.Rect(f, "Input", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(350f, 38f)), "Text", "", 20, TextAnchor.MiddleCenter, UiTheme.Ink);
            text.supportRichText = false;
            Text placeholder = UiTheme.Label(UiTheme.Rect(f, "Placeholder", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(350f, 38f)), "Text", "192.168.0.5", 18, TextAnchor.MiddleCenter, UiTheme.InkDim, FontStyle.Italic);
            joinAddress = f.gameObject.AddComponent<InputField>();
            joinAddress.textComponent = text;
            joinAddress.placeholder = placeholder;
            joinAddress.characterLimit = 45;

            joinStatus = UiTheme.Label(UiTheme.Rect(joinPanel, "Status", new Vector2(0.5f, 1f), new Vector2(0f, -218f), new Vector2(580f, 30f)), "Text", "", 17, TextAnchor.MiddleCenter, UiTheme.Bad);
            UiTheme.TextButton(joinPanel, "JoinNow", "Приєднатися", new Vector2(0.5f, 1f), new Vector2(0f, -270f), new Vector2(300f, 52f), JoinGame, 20);

            UiTheme.Divider(joinPanel, -312f, 400f);
            UiTheme.Label(UiTheme.Rect(joinPanel, "HostHint", new Vector2(0.5f, 1f), new Vector2(0f, -334f), new Vector2(580f, 28f)), "Text",
                "Або створіть гру самі, і інші зайдуть до вас:", 16, TextAnchor.MiddleCenter, UiTheme.InkDim);
            UiTheme.TextButton(joinPanel, "HostNew", "Нова кампанія", new Vector2(0.5f, 1f), new Vector2(-150f, -378f), new Vector2(280f, 52f),
                               () => { joinPanel.gameObject.SetActive(false); networkCampaign.Panel.gameObject.SetActive(true); }, 19);
            UiTheme.TextButton(joinPanel, "HostSave", "Зі збереження", new Vector2(0.5f, 1f), new Vector2(150f, -378f), new Vector2(280f, 52f), HostFromSave, 19);
            UiTheme.TextButton(joinPanel, "Back", "Назад", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(230f, 46f), () => { joining = false; Net.NetSession.Stop(); ShowJoin(false); }, 19);
            joinPanel.gameObject.SetActive(false);

            networkCampaign = CampaignSetupUI.Build(root, () => { networkCampaign.Panel.gameObject.SetActive(false); ShowJoin(true); },
                                                    setup => HostLobby(setup, null, ""));
            networkCampaign.Panel.gameObject.SetActive(false);
            lobby = LobbyUI.Build(root, LobbyClosed);
        }

        private void ShowSettings(bool show)
        {
            menu.gameObject.SetActive(!show);
            settings.gameObject.SetActive(show);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            Canvas canvas = UiTheme.CreateCanvas("MainMenuCanvas", 10);
            canvas.transform.SetParent(transform, false);
            var root = (RectTransform)canvas.transform;

            RectTransform bg = UiTheme.Rect(root, "Background", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.sizeDelta = Vector2.zero;
            var raw = bg.gameObject.AddComponent<RawImage>();
            raw.texture = Backdrop();
            raw.raycastTarget = false;

            RectTransform titleRt = UiTheme.Rect(root, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1200f, 120f));
            UiTheme.Label(titleRt, "Text", "Battle of Ages", 92, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            RectTransform sub = UiTheme.Rect(root, "Subtitle", new Vector2(0.5f, 1f), new Vector2(0f, -226f), new Vector2(1200f, 40f));
            UiTheme.Label(sub, "Text", "Збудуй поселення  ·  спорядь військо  ·  вистоїш проти набігів", 24, TextAnchor.MiddleCenter, UiTheme.Ink);

            menu = UiTheme.Rect(root, "Menu", new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(460f, 604f));
            UiTheme.PanelImage(menu);
            float y = -36f;
            continueButton = Big(menu, "Продовжити", ref y, Continue);
            RectTransform ci = UiTheme.Rect(menu, "ContinueInfo", new Vector2(0.5f, 1f), new Vector2(0f, y + 14f), new Vector2(400f, 24f));
            continueInfo = UiTheme.Label(ci, "Text", "", 15, TextAnchor.MiddleCenter, UiTheme.InkDim);
            y -= 18f;
            Big(menu, "Нова гра", ref y, () => ShowCampaign(true));
            Big(menu, "Завантажити", ref y, () => SaveSlotsUI.Open(false, RefreshContinue));
            Big(menu, "Гра в мережі", ref y, () => ShowJoin(true));
            Big(menu, "Налаштування", ref y, () => ShowSettings(true));
            Big(menu, "Вихід", ref y, Quit);

            settings = UiTheme.Rect(root, "Settings", new Vector2(0.5f, 0.5f), new Vector2(0f, -90f), new Vector2(1100f, 640f));
            UiTheme.PanelImage(settings);
            RectTransform st = UiTheme.Rect(settings, "Title", new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(500f, 44f));
            UiTheme.Label(st, "Text", "Налаштування", 30, TextAnchor.MiddleCenter, UiTheme.Gold, FontStyle.Bold);
            UiTheme.Divider(settings, -66f, 520f);
            PopIn.On(settings);
            SettingsManager sm = SettingsManager.Instance;
            SliderRow(settings, "Чутливість миші (від першої особи)", -82f, SettingsManager.MIN_SENSITIVITY, SettingsManager.MAX_SENSITIVITY, sm != null ? sm.FpsSensitivity : 1f, v => SettingsManager.Instance?.SetFpsSensitivity(v));
            SliderRow(settings, "Чутливість камери мапи", -148f, SettingsManager.MIN_SENSITIVITY, SettingsManager.MAX_MAP_SENSITIVITY, sm != null ? sm.MapSensitivity : 1f, v => SettingsManager.Instance?.SetMapSensitivity(v));
            SliderRow(settings, "Колесо миші на мапі (швидкість зуму)", -214f, SettingsManager.MIN_ZOOM_SENSITIVITY, SettingsManager.MAX_ZOOM_SENSITIVITY, sm != null ? sm.ZoomSensitivity : 1f, v => SettingsManager.Instance?.SetZoomSensitivity(v));
            SliderRow(settings, "Гучність", -280f, SettingsManager.MIN_VOLUME, SettingsManager.MAX_VOLUME, sm != null ? sm.MasterVolume : 1f, v => SettingsManager.Instance?.SetMasterVolume(v));
            SliderRow(settings, "Музика", -346f, SettingsManager.MIN_VOLUME, SettingsManager.MAX_VOLUME, sm != null ? sm.MusicVolume : 0.85f, v => SettingsManager.Instance?.SetMusicVolume(v));
            SliderRow(settings, "Ефекти світу", -412f, SettingsManager.MIN_VOLUME, SettingsManager.MAX_VOLUME, sm != null ? sm.SfxVolume : 1f, v => SettingsManager.Instance?.SetSfxVolume(v));
            SliderRow(settings, "Оточення та інтерфейс", -478f, SettingsManager.MIN_VOLUME, SettingsManager.MAX_VOLUME, sm != null ? sm.AmbienceVolume : 0.9f, v => SettingsManager.Instance?.SetAmbienceVolume(v));
            OptionRow(settings, "Графіка", -86f, () => GraphicsQuality.Names[(int)GraphicsQuality.Current], GraphicsQuality.Cycle);
            OptionRow(settings, "Масштаб інтерфейсу", -136f, () => DisplaySettings.ScaleName, DisplaySettings.CycleScale);
            OptionRow(settings, "Повний екран", -186f, () => DisplaySettings.FullScreen ? "так" : "вікно", DisplaySettings.ToggleFullScreen);
            OptionRow(settings, "Роздільність", -236f, () => DisplaySettings.ResolutionName, DisplaySettings.CycleResolution);
            OptionRow(settings, "VSync", -286f, () => DisplaySettings.VSync ? "увімк." : "вимк.", DisplaySettings.ToggleVSync);
            OptionRow(settings, "Ліміт кадрів", -336f, () => DisplaySettings.FrameCapName, DisplaySettings.CycleFrameCap);
            RectTransform kb = UiTheme.Rect(settings, "Keys", new Vector2(0.5f, 0f), new Vector2(0f, 88f), new Vector2(560f, 26f));
            UiTheme.Label(kb, "Text", "Клавіші можна перепризначити в грі: Esc → Налаштування", 15, TextAnchor.MiddleCenter, UiTheme.InkDim);
            UiTheme.TextButton(settings, "Back", "Назад", new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(220f, 52f), () => ShowSettings(false), 20);
            settings.gameObject.SetActive(false);

            campaign = CampaignSetupUI.Build(root, () => ShowCampaign(false));
            campaign.Panel.gameObject.SetActive(false);
            BuildJoinPanel(root);

            RectTransform ver = UiTheme.Rect(root, "Version", new Vector2(1f, 0f), new Vector2(-16f, 12f), new Vector2(400f, 24f));
            UiTheme.Label(ver, "Text", "v" + Application.version, 14, TextAnchor.LowerRight, UiTheme.InkDim);
        }

        private static Button Big(RectTransform parent, string text, ref float y, UnityEngine.Events.UnityAction onClick)
        {
            Button b = UiTheme.TextButton(parent, text, text, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(380f, 64f), onClick, 26);
            y -= 84f;
            return b;
        }

        private const float SliderX = -265f;

        /// <summary>A screen option on the right half: its name and a button that steps to the next value.</summary>
        private static void OptionRow(RectTransform parent, string label, float y, System.Func<string> value, System.Action next)
        {
            RectTransform lab = UiTheme.Rect(parent, label, new Vector2(0.5f, 1f), new Vector2(150f, y), new Vector2(240f, 40f));
            UiTheme.Label(lab, "Text", label, 19, TextAnchor.MiddleLeft, UiTheme.Ink);
            Text text = null;
            Button b = UiTheme.TextButton(parent, label + "_Value", "", new Vector2(0.5f, 1f), new Vector2(390f, y), new Vector2(220f, 40f), () => { next(); text.text = value(); }, 18);
            text = b.GetComponentInChildren<Text>();
            text.text = value();
        }

        private static void SliderRow(RectTransform parent, string label, float y, float min, float max, float value, System.Action<float> changed)
        {
            RectTransform lab = UiTheme.Rect(parent, label, new Vector2(0.5f, 1f), new Vector2(SliderX, y), new Vector2(480f, 28f));
            Text valueText = null;
            UiTheme.Label(lab, "Text", label, 19, TextAnchor.MiddleLeft, UiTheme.Ink);
            RectTransform valRt = UiTheme.Rect(lab, "Value", new Vector2(1f, 0.5f), Vector2.zero, new Vector2(80f, 28f));
            valueText = UiTheme.Label(valRt, "Text", "", 19, TextAnchor.MiddleRight, UiTheme.Gold, FontStyle.Bold);

            RectTransform s = UiTheme.Rect(parent, label + "_Slider", new Vector2(0.5f, 1f), new Vector2(SliderX, y - 38f), new Vector2(480f, 26f));
            UiTheme.PanelImage(s, UiTheme.SlotSprite);
            Slider slider = s.gameObject.AddComponent<Slider>();
            RectTransform fillArea = UiTheme.Rect(s, "FillArea", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fillArea.anchorMin = new Vector2(0f, 0f); fillArea.anchorMax = new Vector2(1f, 1f); fillArea.offsetMin = new Vector2(6f, 6f); fillArea.offsetMax = new Vector2(-6f, -6f);
            RectTransform fill = UiTheme.Rect(fillArea, "Fill", new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(0f, 1f); fill.sizeDelta = Vector2.zero;
            UiTheme.PanelImage(fill, UiTheme.BarSprite, UiTheme.Gold * 0.85f);
            RectTransform handleArea = UiTheme.Rect(s, "HandleArea", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            handleArea.anchorMin = Vector2.zero; handleArea.anchorMax = Vector2.one; handleArea.offsetMin = new Vector2(10f, 0f); handleArea.offsetMax = new Vector2(-10f, 0f);
            RectTransform handle = UiTheme.Rect(handleArea, "Handle", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(24f, 34f));
            handle.anchorMin = new Vector2(0f, 0f); handle.anchorMax = new Vector2(0f, 1f); handle.sizeDelta = new Vector2(24f, 8f);
            Image hImg = UiTheme.PanelImage(handle, UiTheme.ButtonSprite);
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = hImg;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = Mathf.Clamp(value, min, max);
            valueText.text = slider.value.ToString("0.##");
            slider.onValueChanged.AddListener(v => { valueText.text = v.ToString("0.##"); changed(v); });
        }

        /// <summary>Dusk sky over dark hills, painted into a small texture.</summary>
        private static Texture2D Backdrop()
        {
            const int W = 960, H = 540;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "MenuBackdrop" };
            var px = new Color[W * H];
            Color top = new Color(0.10f, 0.12f, 0.22f), horizon = new Color(0.85f, 0.52f, 0.30f), hillsFar = new Color(0.20f, 0.17f, 0.20f), hillsNear = new Color(0.08f, 0.07f, 0.07f);
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (float)H, u = x / (float)W;
                Color c = Color.Lerp(horizon, top, Mathf.Pow(Mathf.Clamp01((v - 0.25f) / 0.75f), 0.7f));
                float sun = Mathf.Exp(-((u - 0.62f) * (u - 0.62f) * 40f + (v - 0.33f) * (v - 0.33f) * 120f));
                c = Color.Lerp(c, new Color(1f, 0.85f, 0.55f), sun * 0.8f);
                float far = 0.30f + 0.05f * Mathf.Sin(u * 9f + 1.3f) + 0.03f * Mathf.Sin(u * 23f);
                float near = 0.20f + 0.06f * Mathf.Sin(u * 5f + 0.4f) + 0.02f * Mathf.Sin(u * 31f + 2f);
                c = Color.Lerp(c, Color.Lerp(hillsFar, c, 0.25f), Mathf.Clamp01((far - v) * H / 1.5f + 0.5f));
                c = Color.Lerp(c, hillsNear, Mathf.Clamp01((near - v) * H / 1.5f + 0.5f));
                px[x + y * W] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }
    }
}
