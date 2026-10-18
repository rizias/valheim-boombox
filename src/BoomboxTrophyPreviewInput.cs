using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxTrophyPreviewInput :
        MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerClickHandler,
        IPointerMoveHandler,
        IPointerExitHandler,
        IScrollHandler
    {
        internal Action<Vector2> Dragged;
        internal Action<Vector2> Clicked;
        internal Action<Vector2?> Hovered;
        internal Action<float, Vector2?> Scrolled;
        private bool dragging;
        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = true;
        }
        public void OnDrag(PointerEventData eventData)
        {
            Dragged?.Invoke(eventData.delta);
        }
        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (dragging || eventData.dragging)
                return;
            var point = Normalized(eventData);
            if (point.HasValue)
                Clicked?.Invoke(point.Value);
        }
        public void OnPointerMove(PointerEventData eventData)
        {
            Hovered?.Invoke(dragging ? null : Normalized(eventData));
        }
        public void OnPointerExit(PointerEventData eventData)
        {
            Hovered?.Invoke(null);
        }
        public void OnScroll(PointerEventData eventData)
        {
            Scrolled?.Invoke(ScrollTicks(eventData), Normalized(eventData));
        }
        private static float ScrollTicks(PointerEventData eventData)
        {
            var module = EventSystem.current != null
                ? EventSystem.current.currentInputModule as InputSystemUIInputModule
                : null;
            return module != null
                ? module.ConvertPointerEventScrollDeltaToTicks(eventData.scrollDelta).y
                : eventData.scrollDelta.y / 6f;
        }
        private Vector2? Normalized(PointerEventData eventData)
        {
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect,
                eventData.position,
                eventData.pressEventCamera,
                out var local
            ))
                return null;
            return new Vector2(
                (local.x - rect.rect.xMin) / rect.rect.width,
                (local.y - rect.rect.yMin) / rect.rect.height
            );
        }
    }
}
