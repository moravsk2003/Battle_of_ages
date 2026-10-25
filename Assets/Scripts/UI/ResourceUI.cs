using UnityEngine;
using UnityEngine.UI;
using BattleOfAges.Managers;

namespace BattleOfAges.UI
{
    /// <summary>
    /// The interaction prompt under the crosshair ("[E] Open the chest", tree progress): a themed plate sized to its
    /// text. Shown only in first person with no window open, so it never sits over the map or a menu.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResourceUI : MonoBehaviour
    {
        private Text promptText;
        private RectTransform promptPanel;
        private bool wanted;
        private GameModeManager modes;

        private void Awake()
        {
            // Clear any old child UI objects attached to this GameObject
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Destroy(transform.GetChild(i).gameObject);
            }

            BuildPromptUI();
        }

        public void ShowTreePrompt(string message)
        {
            if (promptText == null) return;
            message = UiTheme.Sanitize(message).Trim();
            if (promptText.text != message)
            {
                promptText.text = message;
                promptPanel.sizeDelta = new Vector2(Mathf.Min(promptText.preferredWidth + 56f, 900f), promptText.preferredHeight + 22f);
            }
            wanted = true;
            Refresh();
        }

        public void HideTreePrompt()
        {
            wanted = false;
            Refresh();
        }

        private void LateUpdate() => Refresh();

        private void Refresh()
        {
            if (promptPanel == null) return;
            if (modes == null) modes = FindFirstObjectByType<GameModeManager>();
            bool firstPerson = modes == null || modes.CurrentMode == GameMode.FirstPerson;
            bool show = wanted && firstPerson && !GameWindows.BlockGameplay;
            if (promptPanel.gameObject.activeSelf != show) promptPanel.gameObject.SetActive(show);
        }

        private void BuildPromptUI()
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
            if (gameObject.GetComponent<UiThemed>() == null) gameObject.AddComponent<UiThemed>();

            promptPanel = UiTheme.Rect(transform, "PromptPanel", new Vector2(0.5f, 0.3f), Vector2.zero, new Vector2(400f, 50f));
            UiTheme.PanelImage(promptPanel).raycastTarget = false;
            promptText = UiTheme.Label(promptPanel, "PromptText", "", 21, TextAnchor.MiddleCenter, UiTheme.Ink);
            promptText.lineSpacing = 1.15f;
            promptPanel.gameObject.SetActive(false);
        }
    }
}
