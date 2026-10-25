using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BattleOfAges.UI
{
    /// <summary>Presses and drags on the minimap: the point in the minimap's own space, the button (right?), and whether it is a drag.</summary>
    public sealed class MinimapInput : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public Action<Vector2, bool, bool> Pressed;

        public void OnPointerDown(PointerEventData e) => Send(e, false);
        public void OnDrag(PointerEventData e) => Send(e, true);

        private void Send(PointerEventData e, bool drag)
        {
            if (Pressed == null || e.button == PointerEventData.InputButton.Middle) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out Vector2 local))
                Pressed(local, e.button == PointerEventData.InputButton.Right, drag);
        }
    }
}
