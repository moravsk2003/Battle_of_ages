using UnityEngine;
using UnityEngine.UI;

namespace BattleOfAges.UI
{
    [DisallowMultipleComponent]
    public class ResourceUI : MonoBehaviour
    {
        private Text promptText;
        private GameObject promptPanelObj;

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
            if (promptText != null)
            {
                promptText.text = message;
                if (promptPanelObj != null) promptPanelObj.SetActive(true);
            }
        }

        public void HideTreePrompt()
        {
            if (promptPanelObj != null)
            {
                promptPanelObj.SetActive(false);
            }
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

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            Font defaultFont = UnityEngine.Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? UnityEngine.Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Center Interaction Prompt Banner ONLY
            promptPanelObj = new GameObject("PromptPanel");
            promptPanelObj.transform.SetParent(transform, false);
            RectTransform promptPanelRect = promptPanelObj.AddComponent<RectTransform>();
            promptPanelRect.anchorMin = new Vector2(0.5f, 0.22f);
            promptPanelRect.anchorMax = new Vector2(0.5f, 0.22f);
            promptPanelRect.sizeDelta = new Vector2(780f, 90f);

            Image promptBg = promptPanelObj.AddComponent<Image>();
            promptBg.color = new Color(0.04f, 0.08f, 0.14f, 0.88f);

            GameObject promptTxtObj = new GameObject("PromptText");
            promptTxtObj.transform.SetParent(promptPanelObj.transform, false);
            RectTransform promptTxtRect = promptTxtObj.AddComponent<RectTransform>();
            promptTxtRect.anchorMin = Vector2.zero;
            promptTxtRect.anchorMax = Vector2.one;
            promptTxtRect.offsetMin = new Vector2(15f, 10f);
            promptTxtRect.offsetMax = new Vector2(-15f, -10f);

            promptText = promptTxtObj.AddComponent<Text>();
            promptText.font = defaultFont;
            promptText.fontSize = 22;
            promptText.alignment = TextAnchor.MiddleCenter;
            promptText.color = new Color(0.3f, 0.9f, 1.0f);
            promptText.text = "[Hold E] Gather Branches  |  [Hold F] Chop Down Tree";
            promptPanelObj.SetActive(false);
        }
    }
}
