using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.RTS;
using BattleOfAges.InputSystem;

namespace BattleOfAges.UI
{
    /// <summary>
    /// HUD panel for displaying and issuing directives to individually selected NPC units.
    /// Appears at the bottom of the screen whenever one or more RTS units are selected.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectedUnitUI : MonoBehaviour
    {
        public static SelectedUnitUI Instance { get; private set; }

        private GameObject panelObj;
        private Text titleText;
        private Text statusText;
        private RtsUnitCommander commander;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildUI();
        }

        private void Start()
        {
            commander = FindFirstObjectByType<RtsUnitCommander>();
            if (panelObj != null) panelObj.SetActive(false);
        }

        private void Update()
        {
            if (commander == null)
            {
                commander = FindFirstObjectByType<RtsUnitCommander>();
                if (commander == null) return;
            }

            var selected = commander.SelectedUnits;
            if (selected == null || selected.Count == 0)
            {
                if (panelObj != null && panelObj.activeSelf)
                {
                    panelObj.SetActive(false);
                }
                return;
            }

            if (panelObj != null && !panelObj.activeSelf)
            {
                panelObj.SetActive(true);
            }

            UpdateDisplay(selected);
        }

        private void UpdateDisplay(System.Collections.Generic.IReadOnlyList<RtsUnit> selected)
        {
            int count = selected.Count;
            if (count == 1 && selected[0] != null)
            {
                RtsUnit unit = selected[0];
                titleText.text = $"👤 ОБРАНИЙ NPC: {unit.gameObject.name}";

                NpcResourceGatherer gatherer = unit.GetComponent<NpcResourceGatherer>();
                if (gatherer != null)
                {
                    statusText.text = $"Наказ: [{GetDirectiveName(gatherer.CurrentDirective)}] | Стан: {GetStateName(gatherer.CurrentState)}";
                }
                else
                {
                    statusText.text = "Боєць / Охоронець селища";
                }
            }
            else
            {
                titleText.text = $"👥 ОБРАНО ГРУПУ: {count} NPC";
                statusText.text = "Оберіть індивідуальний наказ для виділеної групи:";
            }
        }

        private void IssueDirectiveToSelected(GatherDirective directive)
        {
            if (commander == null) return;

            var selected = commander.SelectedUnits;
            int count = selected.Count;
            int updated = 0;

            for (int i = 0; i < count; i++)
            {
                if (selected[i] == null) continue;
                NpcResourceGatherer gatherer = selected[i].GetComponent<NpcResourceGatherer>();
                if (gatherer != null)
                {
                    gatherer.SetGatherDirective(directive);
                    updated++;
                }
            }

            Debug.Log($"[SelectedUnitUI] Set directive '{directive}' for {updated} selected NPC(s).");
        }

        private void DeselectAll()
        {
            if (commander != null)
            {
                commander.DeselectAll();
            }
        }

        private string GetDirectiveName(GatherDirective directive)
        {
            switch (directive)
            {
                case GatherDirective.WoodGatherBranchesOnly: return "🪵 Збір гілок";
                case GatherDirective.WoodChopTreesOnly: return "🪓 Рубання дерев";
                case GatherDirective.OrePebblesOnly: return "🪨 Збір камінчиків";
                case GatherDirective.HaulOreFromMineToChest: return "⛏️ Шахта";
                default: return "🔄 Автономний";
            }
        }

        private string GetStateName(GathererState state)
        {
            switch (state)
            {
                case GathererState.FindingResource: return "Шукає ресурс";
                case GathererState.MovingToResource: return "Іде до ресурсу";
                case GathererState.Harvesting: return "Добуває";
                case GathererState.DeliveringToChest: return "Несе в скриню";
                case GathererState.MovingToMine: return "Іде до шахти";
                case GathererState.WorkingInsideMine: return "Працює в шахті";
                default: return "Рухається";
            }
        }

        private void BuildUI()
        {
            Canvas canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Font font = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            panelObj = new GameObject("SelectedUnitPanel", typeof(RectTransform));
            panelObj.transform.SetParent(transform, false);

            RectTransform rect = panelObj.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 25f);
            rect.sizeDelta = new Vector2(920f, 130f);

            Image bg = panelObj.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.10f, 0.18f, 0.94f);

            // Title
            GameObject titleObj = new GameObject("TitleText", typeof(RectTransform));
            titleObj.transform.SetParent(panelObj.transform, false);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.68f);
            titleRect.anchorMax = new Vector2(0.75f, 0.95f);
            titleRect.offsetMin = new Vector2(20f, 0f);

            titleText = titleObj.AddComponent<Text>();
            titleText.font = font;
            titleText.fontSize = 20;
            titleText.fontStyle = FontStyle.Bold;
            titleText.color = new Color(0.3f, 0.95f, 1.0f);
            titleText.text = "👤 ОБРАНИЙ NPC";

            // Status
            GameObject statusObj = new GameObject("StatusText", typeof(RectTransform));
            statusObj.transform.SetParent(panelObj.transform, false);
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 0.45f);
            statusRect.anchorMax = new Vector2(0.75f, 0.68f);
            statusRect.offsetMin = new Vector2(20f, 0f);

            statusText = statusObj.AddComponent<Text>();
            statusText.font = font;
            statusText.fontSize = 16;
            statusText.color = new Color(0.85f, 0.92f, 0.98f);
            statusText.text = "Стан робітника";

            // Deselect button
            GameObject deselectBtnObj = new GameObject("DeselectBtn", typeof(RectTransform));
            deselectBtnObj.transform.SetParent(panelObj.transform, false);
            RectTransform deselectRect = deselectBtnObj.GetComponent<RectTransform>();
            deselectRect.anchorMin = new Vector2(0.82f, 0.52f);
            deselectRect.anchorMax = new Vector2(0.98f, 0.92f);

            Image deselectBg = deselectBtnObj.AddComponent<Image>();
            deselectBg.color = new Color(0.40f, 0.12f, 0.12f, 0.9f);
            Button deselectBtn = deselectBtnObj.AddComponent<Button>();
            deselectBtn.onClick.AddListener(DeselectAll);

            GameObject desTxtObj = new GameObject("Text", typeof(RectTransform));
            desTxtObj.transform.SetParent(deselectBtnObj.transform, false);
            RectTransform desTxtRect = desTxtObj.GetComponent<RectTransform>();
            desTxtRect.anchorMin = Vector2.zero;
            desTxtRect.anchorMax = Vector2.one;

            Text desTxt = desTxtObj.AddComponent<Text>();
            desTxt.font = font;
            desTxt.fontSize = 15;
            desTxt.fontStyle = FontStyle.Bold;
            desTxt.alignment = TextAnchor.MiddleCenter;
            desTxt.color = Color.white;
            desTxt.text = "✖ Скинути";

            // Directives Buttons Bar
            GameObject barObj = new GameObject("DirectivesBar", typeof(RectTransform));
            barObj.transform.SetParent(panelObj.transform, false);
            RectTransform barRect = barObj.GetComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0.05f);
            barRect.anchorMax = new Vector2(1f, 0.44f);
            barRect.offsetMin = new Vector2(15f, 5f);
            barRect.offsetMax = new Vector2(-15f, -2f);

            HorizontalLayoutGroup layout = barObj.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            CreateDirectiveButton(barObj.transform, font, "🪵 Гілки", () => IssueDirectiveToSelected(GatherDirective.WoodGatherBranchesOnly));
            CreateDirectiveButton(barObj.transform, font, "🪓 Рубати", () => IssueDirectiveToSelected(GatherDirective.WoodChopTreesOnly));
            CreateDirectiveButton(barObj.transform, font, "🪨 Руда", () => IssueDirectiveToSelected(GatherDirective.OrePebblesOnly));
            CreateDirectiveButton(barObj.transform, font, "⛏️ Шахта", () => IssueDirectiveToSelected(GatherDirective.HaulOreFromMineToChest));
            CreateDirectiveButton(barObj.transform, font, "🔄 Авто", () => IssueDirectiveToSelected(GatherDirective.Auto));
        }

        private void CreateDirectiveButton(Transform parent, Font font, string label, UnityEngine.Events.UnityAction onClick)
        {
            GameObject btnObj = new GameObject("DirectiveBtn", typeof(RectTransform));
            btnObj.transform.SetParent(parent, false);

            Image bg = btnObj.AddComponent<Image>();
            bg.color = new Color(0.14f, 0.22f, 0.35f, 0.95f);

            Button btn = btnObj.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.14f, 0.22f, 0.35f, 0.95f);
            cb.highlightedColor = new Color(0.25f, 0.42f, 0.65f, 1.0f);
            cb.pressedColor = new Color(0.12f, 0.55f, 0.85f, 1.0f);
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
        }
    }
}
