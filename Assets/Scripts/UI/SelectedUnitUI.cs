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
                titleText.text = unit.gameObject.name.Replace("NPC_Villager_", "Робітник ").Replace("_", " ");

                NpcResourceGatherer gatherer = unit.GetComponent<NpcResourceGatherer>();
                if (gatherer != null)
                {
                    statusText.text = $"{GetDirectiveName(gatherer.CurrentDirective)}  ·  {GetStateName(gatherer.CurrentState)}";
                }
                else
                {
                    statusText.text = "Боєць / Охоронець селища";
                }
            }
            else
            {
                titleText.text = $"Обрано: {count}";
                statusText.text = "Наказ для всієї групи";
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
                case GatherDirective.WoodGatherBranchesOnly: return "Збір гілок";
                case GatherDirective.WoodChopTreesOnly: return "Рубання дерев";
                case GatherDirective.OrePebblesOnly: return "Збір камінчиків";
                case GatherDirective.HaulOreFromMineToChest: return "Шахта";
                default: return "Самостійно";
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
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null) gameObject.AddComponent<GraphicRaycaster>();
            if (gameObject.GetComponent<UiThemed>() == null) gameObject.AddComponent<UiThemed>();

            // Command panel: selection summary on the left, the order buttons on the right
            RectTransform rect = UiTheme.Rect(transform, "SelectedUnitPanel", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(1040f, 128f));
            panelObj = rect.gameObject;
            UiTheme.PanelImage(rect);

            RectTransform info = UiTheme.Rect(rect, "Info", new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(330f, 100f));
            UiTheme.IconImage(info, UiTheme.Icon.Worker, new Vector2(0f, 0.5f), new Vector2(0f, 0f), 64f);
            RectTransform titleRect = UiTheme.Rect(info, "Title", new Vector2(0f, 1f), new Vector2(76f, -8f), new Vector2(254f, 36f));
            titleText = UiTheme.Label(titleRect, "Text", "", 23, TextAnchor.MiddleLeft, UiTheme.Gold, FontStyle.Bold);
            RectTransform statusRect = UiTheme.Rect(info, "Status", new Vector2(0f, 0f), new Vector2(76f, 8f), new Vector2(254f, 50f));
            statusText = UiTheme.Label(statusRect, "Text", "", 17, TextAnchor.UpperLeft, UiTheme.InkDim);
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;

            RectTransform bar = UiTheme.Rect(rect, "DirectivesBar", new Vector2(1f, 0.5f), new Vector2(-24f, -6f), new Vector2(640f, 84f));
            HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            CreateDirectiveButton(bar, UiTheme.Icon.Wood, "Гілки", () => IssueDirectiveToSelected(GatherDirective.WoodGatherBranchesOnly));
            CreateDirectiveButton(bar, UiTheme.Icon.Hammer, "Рубати", () => IssueDirectiveToSelected(GatherDirective.WoodChopTreesOnly));
            CreateDirectiveButton(bar, UiTheme.Icon.Ore, "Руда", () => IssueDirectiveToSelected(GatherDirective.OrePebblesOnly));
            CreateDirectiveButton(bar, UiTheme.Icon.House, "Шахта", () => IssueDirectiveToSelected(GatherDirective.HaulOreFromMineToChest));
            CreateDirectiveButton(bar, UiTheme.Icon.Worker, "Авто", () => IssueDirectiveToSelected(GatherDirective.Auto));

            RectTransform close = UiTheme.Rect(rect, "DeselectBtn", new Vector2(1f, 1f), new Vector2(14f, 14f), new Vector2(38f, 38f));
            UiTheme.PanelImage(close, UiTheme.ButtonSprite, new Color(1f, 0.72f, 0.68f));
            close.gameObject.AddComponent<Button>().onClick.AddListener(DeselectAll);
            UiTheme.Label(close, "Text", "×", 26, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
        }

        private void CreateDirectiveButton(Transform parent, UiTheme.Icon icon, string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("DirectiveBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiTheme.PanelImage((RectTransform)go.transform, UiTheme.ButtonSprite);
            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.2f, 1.12f, 0.95f); cb.pressedColor = new Color(0.78f, 0.72f, 0.64f);
            cb.colorMultiplier = 1.25f;
            btn.colors = cb;
            btn.onClick.AddListener(onClick);
            UiTheme.IconImage(go.transform, icon, new Vector2(0.5f, 1f), new Vector2(0f, -8f), 38f);
            RectTransform txt = UiTheme.Rect(go.transform, "Label", new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(120f, 26f));
            UiTheme.Label(txt, "Text", label, 18, TextAnchor.MiddleCenter, UiTheme.Ink, FontStyle.Bold);
        }
    }
}
