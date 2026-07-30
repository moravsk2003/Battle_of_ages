using UnityEngine;
using UnityEngine.UI;

namespace BattleOfAges.UI
{
    /// <summary>
    /// Modern uGUI Canvas-based RTS selection box.
    /// Replaces legacy OnGUI rendering with zero CPU overhead and smooth RectTransform scaling.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionBoxUI : MonoBehaviour
    {
        [Header("Selection Box Styling")]
        [SerializeField] private Canvas targetCanvas;
        [SerializeField] private RectTransform selectionBoxRect;
        [SerializeField] private Color fillColor = new Color(0.2f, 0.6f, 1.0f, 0.25f);
        [SerializeField] private Color borderColor = new Color(0.3f, 0.8f, 1.0f, 0.9f);

        private Image boxImage;
        private Outline boxOutline;

        private void Awake()
        {
            EnsureUIComponents();
            HideBox();
        }

        private void EnsureUIComponents()
        {
            if (targetCanvas == null)
            {
                targetCanvas = GetComponentInParent<Canvas>();
                if (targetCanvas == null)
                {
                    GameObject canvasObj = new GameObject("SelectionBoxCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                    targetCanvas = canvasObj.GetComponent<Canvas>();
                    targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    targetCanvas.sortingOrder = 99;
                }
            }

            if (selectionBoxRect == null)
            {
                GameObject boxObj = new GameObject("SelectionBoxRect", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
                boxObj.transform.SetParent(targetCanvas.transform, false);

                selectionBoxRect = boxObj.GetComponent<RectTransform>();
                selectionBoxRect.anchorMin = Vector2.zero;
                selectionBoxRect.anchorMax = Vector2.zero;
                selectionBoxRect.pivot = Vector2.zero;

                boxImage = boxObj.GetComponent<Image>();
                boxImage.color = fillColor;
                boxImage.raycastTarget = false;

                boxOutline = boxObj.GetComponent<Outline>();
                boxOutline.effectColor = borderColor;
                boxOutline.effectDistance = new Vector2(2f, -2f);
            }
            else
            {
                boxImage = selectionBoxRect.GetComponent<Image>();
                if (boxImage == null) boxImage = selectionBoxRect.gameObject.AddComponent<Image>();
                boxImage.color = fillColor;
                boxImage.raycastTarget = false;
            }
        }

        public void ShowBox(Vector2 screenStart, Vector2 screenEnd)
        {
            if (selectionBoxRect == null) EnsureUIComponents();

            if (!selectionBoxRect.gameObject.activeSelf)
            {
                selectionBoxRect.gameObject.SetActive(true);
            }

            Vector2 min = Vector2.Min(screenStart, screenEnd);
            Vector2 max = Vector2.Max(screenStart, screenEnd);

            Vector2 size = max - min;

            selectionBoxRect.position = min;
            selectionBoxRect.sizeDelta = size;
        }

        public void HideBox()
        {
            if (selectionBoxRect != null && selectionBoxRect.gameObject.activeSelf)
            {
                selectionBoxRect.gameObject.SetActive(false);
            }
        }
    }
}
