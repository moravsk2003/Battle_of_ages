using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.RTS;
using BattleOfAges.InputSystem;
using BattleOfAges.Managers;

namespace BattleOfAges.UI
{
    /// <summary>
    /// UI modal for configuring and balancing worker allocation across different village jobs.
    /// Opened with the 'K' key. Uses WorkerRegistry for instant zero-GC distribution.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcGatherCommandUI : MonoBehaviour
    {
        public static NpcGatherCommandUI Instance { get; private set; }

        private GameObject commandPanelObj;
        private Text statusText;
        private Text titleTxt;

        // Allocation counts
        private int branchesCount = 0;
        private int chopCount = 0;
        private int pebblesCount = 0;
        private int haulCount = 0;
        private int autoCount = 0;

        // Counter UI Texts
        private Text branchesText;
        private Text chopText;
        private Text pebblesText;
        private Text haulText;
        private Text autoText;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            IsOpen = false;

            BuildCommandUI();
            KeybindsManager.OnKeybindsChanged += UpdateHeaderText;
        }

        private void OnDestroy()
        {
            KeybindsManager.OnKeybindsChanged -= UpdateHeaderText;
        }

        private void Start()
        {
            CloseMenu();
        }

        private void Update()
        {
            if (Time.frameCount < 2 || Time.time < 0.5f) return;
            if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused()) return;

            if (IsOpen)
            {
                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }

                if (UniversalInput.GetKeyDownEscape() || IsToggleKeyPressed())
                {
                    CloseMenu();
                    return;
                }
            }
            else
            {
                if ((InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) ||
                    (BuildMenuUI.Instance != null && BuildMenuUI.Instance.IsOpen))
                {
                    return;
                }

                if (IsToggleKeyPressed())
                {
                    OpenMenu();
                }
            }
        }

        private bool IsToggleKeyPressed()
        {
            return KeybindsManager.GetKeyDown(KeyAction.WorkerCommandUI);
        }

        public void ToggleMenu()
        {
            if (IsOpen) CloseMenu();
            else OpenMenu();
        }

        public void OpenMenu()
        {
            IsOpen = true;
            if (commandPanelObj == null) BuildCommandUI();
            if (commandPanelObj != null) commandPanelObj.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            SyncWithCurrentWorkerState();
            UpdateHeaderText();
        }

        public void CloseMenu()
        {
            IsOpen = false;
            if (commandPanelObj != null) commandPanelObj.SetActive(false);

            foreach (Transform child in transform)
            {
                if (child != null) child.gameObject.SetActive(false);
            }

            if ((InventoryUI.Instance == null || !InventoryUI.Instance.IsOpen) &&
                (BuildMenuUI.Instance == null || !BuildMenuUI.Instance.IsOpen) &&
                (PauseMenuController.Instance == null || !PauseMenuController.Instance.IsPaused()))
            {
                GameModeManager gmm = FindFirstObjectByType<GameModeManager>();
                if (gmm == null || gmm.CurrentMode == GameMode.FirstPerson)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        private void SyncWithCurrentWorkerState()
        {
            branchesCount = WorkerRegistry.GetDirectiveWorkerCount(GatherDirective.WoodGatherBranchesOnly);
            chopCount = WorkerRegistry.GetDirectiveWorkerCount(GatherDirective.WoodChopTreesOnly);
            pebblesCount = WorkerRegistry.GetDirectiveWorkerCount(GatherDirective.OrePebblesOnly);
            haulCount = WorkerRegistry.GetDirectiveWorkerCount(GatherDirective.HaulOreFromMineToChest);
            autoCount = WorkerRegistry.GetDirectiveWorkerCount(GatherDirective.Auto);

            UpdateUIValues();
        }

        private void UpdateUIValues()
        {
            if (branchesText != null) branchesText.text = branchesCount.ToString();
            if (chopText != null) chopText.text = chopCount.ToString();
            if (pebblesText != null) pebblesText.text = pebblesCount.ToString();
            if (haulText != null) haulText.text = haulCount.ToString();
            if (autoText != null) autoText.text = autoCount.ToString();

            int totalWorkers = WorkerRegistry.WorkerCount;
            int totalAssigned = branchesCount + chopCount + pebblesCount + haulCount + autoCount;

            if (statusText != null)
            {
                statusText.text = $"Усього робітників: {totalWorkers}  |  Налаштовано: {totalAssigned}";
            }
        }

        private int GetTotalAssignedExcept(GatherDirective exceptDirective)
        {
            int sum = 0;
            if (exceptDirective != GatherDirective.WoodGatherBranchesOnly) sum += branchesCount;
            if (exceptDirective != GatherDirective.WoodChopTreesOnly) sum += chopCount;
            if (exceptDirective != GatherDirective.OrePebblesOnly) sum += pebblesCount;
            if (exceptDirective != GatherDirective.HaulOreFromMineToChest) sum += haulCount;
            if (exceptDirective != GatherDirective.Auto) sum += autoCount;
            return sum;
        }

        private void ChangeJobCount(GatherDirective directive, int delta)
        {
            int total = WorkerRegistry.WorkerCount;
            int currentlyAssignedOther = GetTotalAssignedExcept(directive);
            int maxAllowed = Mathf.Max(0, total - currentlyAssignedOther);

            switch (directive)
            {
                case GatherDirective.WoodGatherBranchesOnly:
                    branchesCount = Mathf.Clamp(branchesCount + delta, 0, maxAllowed);
                    break;
                case GatherDirective.WoodChopTreesOnly:
                    chopCount = Mathf.Clamp(chopCount + delta, 0, maxAllowed);
                    break;
                case GatherDirective.OrePebblesOnly:
                    pebblesCount = Mathf.Clamp(pebblesCount + delta, 0, maxAllowed);
                    break;
                case GatherDirective.HaulOreFromMineToChest:
                    haulCount = Mathf.Clamp(haulCount + delta, 0, maxAllowed);
                    break;
                case GatherDirective.Auto:
                    autoCount = Mathf.Clamp(autoCount + delta, 0, maxAllowed);
                    break;
            }

            UpdateUIValues();
        }

        public void ApplyCurrentAllocation()
        {
            WorkerRegistry.ApplyJobDistribution(branchesCount, chopCount, pebblesCount, haulCount, autoCount);
            int total = WorkerRegistry.WorkerCount;

            if (statusText != null)
            {
                statusText.text = $"✅ Розподіл застосовано для {total} робітників!";
            }
        }

        public void SetAllToDirective(GatherDirective directive)
        {
            WorkerRegistry.IssueDirectiveToAll(directive);
            SyncWithCurrentWorkerState();

            if (statusText != null)
            {
                statusText.text = $"🔄 Усім робітникам призначено режим [{directive}]";
            }
        }

        private void BuildCommandUI()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Font font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Main Panel Window
            commandPanelObj = new GameObject("NpcCommandPanel", typeof(RectTransform));
            commandPanelObj.transform.SetParent(transform, false);

            RectTransform panelRect = commandPanelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 620f);

            Image panelBg = commandPanelObj.AddComponent<Image>();
            panelBg.color = new Color(0.05f, 0.08f, 0.15f, 0.97f);

            // Header Bar
            GameObject headerObj = new GameObject("Header", typeof(RectTransform));
            headerObj.transform.SetParent(commandPanelObj.transform, false);
            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.anchoredPosition = Vector2.zero;
            headerRect.sizeDelta = new Vector2(0f, 60f);

            Image headerBg = headerObj.AddComponent<Image>();
            headerBg.color = new Color(0.10f, 0.16f, 0.28f, 0.98f);

            GameObject titleObj = new GameObject("TitleText", typeof(RectTransform));
            titleObj.transform.SetParent(headerObj.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = new Vector2(50f, 0f);
            titleRect.offsetMax = new Vector2(-50f, 0f);

            titleTxt = titleObj.AddComponent<Text>();
            titleTxt.font = font;
            titleTxt.fontSize = 22;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = new Color(0.3f, 0.95f, 1.0f);
            titleTxt.raycastTarget = false;
            UpdateHeaderText();

            // Header Close Button [ X ]
            GameObject closeBtnObj = new GameObject("CloseBtn", typeof(RectTransform));
            closeBtnObj.transform.SetParent(headerObj.transform, false);
            RectTransform closeRect = closeBtnObj.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.anchoredPosition = new Vector2(-12f, 0f);
            closeRect.sizeDelta = new Vector2(40f, 40f);

            Image closeBg = closeBtnObj.AddComponent<Image>();
            closeBg.color = new Color(0.85f, 0.20f, 0.20f, 0.95f);
            Button closeBtn = closeBtnObj.AddComponent<Button>();
            closeBtn.onClick.AddListener(CloseMenu);

            GameObject closeTxtObj = new GameObject("Text", typeof(RectTransform));
            closeTxtObj.transform.SetParent(closeBtnObj.transform, false);
            RectTransform closeTxtRect = closeTxtObj.GetComponent<RectTransform>();
            closeTxtRect.anchorMin = Vector2.zero;
            closeTxtRect.anchorMax = Vector2.one;
            Text closeTxt = closeTxtObj.AddComponent<Text>();
            closeTxt.font = font;
            closeTxt.fontSize = 20;
            closeTxt.fontStyle = FontStyle.Bold;
            closeTxt.alignment = TextAnchor.MiddleCenter;
            closeTxt.color = Color.white;
            closeTxt.text = "✖";
            closeTxt.raycastTarget = false;

            // Status Subheader Text
            GameObject statusObj = new GameObject("StatusText", typeof(RectTransform));
            statusObj.transform.SetParent(commandPanelObj.transform, false);
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0.81f);
            statusRect.anchorMax = new Vector2(1f, 0.89f);
            statusRect.offsetMin = new Vector2(25f, 0f);
            statusRect.offsetMax = new Vector2(-25f, 0f);

            statusText = statusObj.AddComponent<Text>();
            statusText.font = font;
            statusText.fontSize = 18;
            statusText.fontStyle = FontStyle.Bold;
            statusText.alignment = TextAnchor.MiddleCenter;
            statusText.color = new Color(0.85f, 0.94f, 1.0f);
            statusText.text = "Налаштуйте кількість робітників на кожну задачу:";
            statusText.raycastTarget = false;

            // Rows Container with VerticalLayoutGroup
            GameObject rowsContainer = new GameObject("RowsContainer", typeof(RectTransform));
            rowsContainer.transform.SetParent(commandPanelObj.transform, false);
            RectTransform containerRect = rowsContainer.GetComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(0.03f, 0.22f);
            containerRect.anchorMax = new Vector2(0.97f, 0.79f);
            containerRect.offsetMin = Vector2.zero;
            containerRect.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = rowsContainer.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // 5 Job Allocation Rows
            branchesText = CreateJobCounterRow(rowsContainer.transform, font, "🪵 Збирання гілок (без нищення дерев)", GatherDirective.WoodGatherBranchesOnly);
            chopText = CreateJobCounterRow(rowsContainer.transform, font, "🪓 Рубання дерев на дрова", GatherDirective.WoodChopTreesOnly);
            pebblesText = CreateJobCounterRow(rowsContainer.transform, font, "🪨 Збір камінчиків руди (Pebbles)", GatherDirective.OrePebblesOnly);
            haulText = CreateJobCounterRow(rowsContainer.transform, font, "⛏️ Перевезення руди з шахти в скриню", GatherDirective.HaulOreFromMineToChest);
            autoText = CreateJobCounterRow(rowsContainer.transform, font, "🔄 Автономні (Шукають найближчі)", GatherDirective.Auto);

            // Bottom Action Buttons Bar
            GameObject bottomBar = new GameObject("BottomBar", typeof(RectTransform));
            bottomBar.transform.SetParent(commandPanelObj.transform, false);
            RectTransform barRect = bottomBar.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0.03f, 0.04f);
            barRect.anchorMax = new Vector2(0.97f, 0.18f);
            barRect.offsetMin = Vector2.zero;
            barRect.offsetMax = Vector2.zero;

            HorizontalLayoutGroup barLayout = bottomBar.AddComponent<HorizontalLayoutGroup>();
            barLayout.spacing = 10f;
            barLayout.childControlWidth = true;
            barLayout.childControlHeight = true;
            barLayout.childForceExpandWidth = true;
            barLayout.childForceExpandHeight = true;

            CreateActionButton(bottomBar.transform, font, "✅ Застосувати", new Color(0.12f, 0.52f, 0.22f), ApplyCurrentAllocation);
            CreateActionButton(bottomBar.transform, font, "🔄 Усі в Авто", new Color(0.18f, 0.32f, 0.55f), () => SetAllToDirective(GatherDirective.Auto));
            CreateActionButton(bottomBar.transform, font, "🪵 Усі Рубачі", new Color(0.55f, 0.35f, 0.12f), () => SetAllToDirective(GatherDirective.WoodChopTreesOnly));
            CreateActionButton(bottomBar.transform, font, "🪨 Усі Рудокопи", new Color(0.35f, 0.40f, 0.45f), () => SetAllToDirective(GatherDirective.OrePebblesOnly));

            if (commandPanelObj != null) commandPanelObj.SetActive(false);
        }

        private void UpdateHeaderText()
        {
            if (titleTxt != null)
            {
                KeyCode key = KeybindsManager.GetKey(KeyAction.WorkerCommandUI);
                string keyName = KeybindsManager.GetKeyDisplayString(key);
                titleTxt.text = $"📜 НАЛАШТУВАННЯ ТА РОЗПОДІЛ РОБІТ (Клавіша '{keyName}')";
            }
        }

        private Text CreateJobCounterRow(Transform parent, Font font, string label, GatherDirective directive)
        {
            GameObject rowObj = new GameObject("JobRow", typeof(RectTransform));
            rowObj.transform.SetParent(parent, false);

            LayoutElement rowElement = rowObj.AddComponent<LayoutElement>();
            rowElement.minHeight = 52f;
            rowElement.preferredHeight = 52f;
            rowElement.flexibleHeight = 0f;

            Image rowBg = rowObj.AddComponent<Image>();
            rowBg.color = new Color(0.10f, 0.16f, 0.26f, 0.95f);

            // Job Title Label
            GameObject labelObj = new GameObject("Label", typeof(RectTransform));
            labelObj.transform.SetParent(rowObj.transform, false);
            RectTransform labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.02f, 0f);
            labelRect.anchorMax = new Vector2(0.60f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text labelTxt = labelObj.AddComponent<Text>();
            labelTxt.font = font;
            labelTxt.fontSize = 16;
            labelTxt.fontStyle = FontStyle.Bold;
            labelTxt.alignment = TextAnchor.MiddleLeft;
            labelTxt.color = Color.white;
            labelTxt.text = label;
            labelTxt.raycastTarget = false;

            // Minus Button [-]
            GameObject minusBtnObj = new GameObject("MinusBtn", typeof(RectTransform));
            minusBtnObj.transform.SetParent(rowObj.transform, false);
            RectTransform minusRect = minusBtnObj.GetComponent<RectTransform>();
            minusRect.anchorMin = new Vector2(0.62f, 0.12f);
            minusRect.anchorMax = new Vector2(0.72f, 0.88f);
            minusRect.offsetMin = Vector2.zero;
            minusRect.offsetMax = Vector2.zero;

            Image minusBg = minusBtnObj.AddComponent<Image>();
            minusBg.color = new Color(0.45f, 0.15f, 0.15f, 0.95f);
            Button minusBtn = minusBtnObj.AddComponent<Button>();
            minusBtn.onClick.AddListener(() => ChangeJobCount(directive, -1));

            GameObject minusTxtObj = new GameObject("Text", typeof(RectTransform));
            minusTxtObj.transform.SetParent(minusBtnObj.transform, false);
            RectTransform minusTxtRect = minusTxtObj.GetComponent<RectTransform>();
            minusTxtRect.anchorMin = Vector2.zero;
            minusTxtRect.anchorMax = Vector2.one;
            Text minusTxt = minusTxtObj.AddComponent<Text>();
            minusTxt.font = font;
            minusTxt.fontSize = 22;
            minusTxt.fontStyle = FontStyle.Bold;
            minusTxt.alignment = TextAnchor.MiddleCenter;
            minusTxt.color = Color.white;
            minusTxt.text = "-";
            minusTxt.raycastTarget = false;

            // Counter Display [ 0 ]
            GameObject countObj = new GameObject("CountBox", typeof(RectTransform));
            countObj.transform.SetParent(rowObj.transform, false);
            RectTransform countRect = countObj.GetComponent<RectTransform>();
            countRect.anchorMin = new Vector2(0.73f, 0.12f);
            countRect.anchorMax = new Vector2(0.86f, 0.88f);
            countRect.offsetMin = Vector2.zero;
            countRect.offsetMax = Vector2.zero;

            Image countBg = countObj.AddComponent<Image>();
            countBg.color = new Color(0.04f, 0.08f, 0.14f, 0.95f);

            GameObject countTxtObj = new GameObject("Text", typeof(RectTransform));
            countTxtObj.transform.SetParent(countObj.transform, false);
            RectTransform countTxtRect = countTxtObj.GetComponent<RectTransform>();
            countTxtRect.anchorMin = Vector2.zero;
            countTxtRect.anchorMax = Vector2.one;

            Text countTxt = countTxtObj.AddComponent<Text>();
            countTxt.font = font;
            countTxt.fontSize = 20;
            countTxt.fontStyle = FontStyle.Bold;
            countTxt.alignment = TextAnchor.MiddleCenter;
            countTxt.color = new Color(0.3f, 0.95f, 1.0f);
            countTxt.text = "0";
            countTxt.raycastTarget = false;

            // Plus Button [+]
            GameObject plusBtnObj = new GameObject("PlusBtn", typeof(RectTransform));
            plusBtnObj.transform.SetParent(rowObj.transform, false);
            RectTransform plusRect = plusBtnObj.GetComponent<RectTransform>();
            plusRect.anchorMin = new Vector2(0.87f, 0.12f);
            plusRect.anchorMax = new Vector2(0.97f, 0.88f);
            plusRect.offsetMin = Vector2.zero;
            plusRect.offsetMax = Vector2.zero;

            Image plusBg = plusBtnObj.AddComponent<Image>();
            plusBg.color = new Color(0.15f, 0.45f, 0.20f, 0.95f);
            Button plusBtn = plusBtnObj.AddComponent<Button>();
            plusBtn.onClick.AddListener(() => ChangeJobCount(directive, 1));

            GameObject plusTxtObj = new GameObject("Text", typeof(RectTransform));
            plusTxtObj.transform.SetParent(plusBtnObj.transform, false);
            RectTransform plusTxtRect = plusTxtObj.GetComponent<RectTransform>();
            plusTxtRect.anchorMin = Vector2.zero;
            plusTxtRect.anchorMax = Vector2.one;
            Text plusTxt = plusTxtObj.AddComponent<Text>();
            plusTxt.font = font;
            plusTxt.fontSize = 22;
            plusTxt.fontStyle = FontStyle.Bold;
            plusTxt.alignment = TextAnchor.MiddleCenter;
            plusTxt.color = Color.white;
            plusTxt.text = "+";
            plusTxt.raycastTarget = false;

            return countTxt;
        }

        private void CreateActionButton(Transform parent, Font font, string label, Color color, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnObj = new GameObject("ActionBtn", typeof(RectTransform));
            btnObj.transform.SetParent(parent, false);

            Image bg = btnObj.AddComponent<Image>();
            bg.color = color;

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = color;
            cb.highlightedColor = color * 1.25f;
            cb.pressedColor = color * 0.85f;
            btn.colors = cb;
            btn.onClick.AddListener(onClick);

            GameObject txtObj = new GameObject("Text", typeof(RectTransform));
            txtObj.transform.SetParent(btnObj.transform, false);
            RectTransform txtRect = txtObj.GetComponent<RectTransform>();
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;

            Text txt = txtObj.AddComponent<Text>();
            txt.font = font;
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.text = label;
            txt.raycastTarget = false;
        }
    }
}
