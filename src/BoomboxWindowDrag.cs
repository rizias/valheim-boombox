using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace VikingBoombox
{
    internal sealed class BoomboxWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal Action<Vector2> Moved;
        internal Action Finished;
        public void OnBeginDrag(PointerEventData data)
        {
        }
        public void OnDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                Moved?.Invoke(data.delta);
        }
        public void OnEndDrag(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left)
                Finished?.Invoke();
        }
    }
}
