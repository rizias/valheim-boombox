using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxPlaylistDrag
    {
        internal const float RowStep = 36f;
        private const float Edge = 40f;
        private const float Speed = 480f;
        private readonly ScrollRect scroll;
        private readonly RectTransform content;
        private readonly Image indicator;
        private readonly Action<int, int> drop;
        private Vector2 pointer;
        private int from = -1;
        private int slot;
        internal int Count;
        internal bool Dragging => from >= 0;

        internal BoomboxPlaylistDrag(ScrollRect list, RectTransform listContent, Action<int, int> dropped)
        {
            scroll = list;
            content = listContent;
            drop = dropped;
            indicator = BoomboxNativeUI.Image("DropIndicator", content, null, BoomboxNativeUI.Gold);
            indicator.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            indicator.gameObject.SetActive(false);
        }

        internal void Begin(int index, Vector2 position)
        {
            from = index;
            pointer = position;
            indicator.gameObject.SetActive(true);
            indicator.transform.SetAsLastSibling();
            Target();
        }

        internal void Move(Vector2 position)
        {
            if (!Dragging)
                return;
            pointer = position;
            Target();
        }

        internal void End(Vector2 position)
        {
            if (!Dragging)
                return;
            pointer = position;
            Target();
            int source = from, target = slot > from ? slot - 1 : slot;
            Cancel();
            if (target != source)
                drop(source, target);
        }

        internal void Cancel()
        {
            from = -1;
            if (indicator != null)
                indicator.gameObject.SetActive(false);
        }

        internal void Tick()
        {
            if (!Dragging)
                return;
            var viewport = scroll.viewport;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, pointer, null, out var local))
                return;
            var area = viewport.rect;
            float direction = local.y > area.yMax - Edge
                ? -1f
                : local.y < area.yMin + Edge
                    ? 1f
                    : 0f;
            if (direction != 0f)
            {
                float limit = Mathf.Max(0f, content.rect.height - area.height);
                scroll.velocity = Vector2.zero;
                content.anchoredPosition = new Vector2(
                    content.anchoredPosition.x,
                    Mathf.Clamp(content.anchoredPosition.y + direction * Speed * Time.unscaledDeltaTime, 0f, limit)
                );
            }
            Target();
        }

        private void Target()
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(content, pointer, null, out var local))
                return;
            float y = content.rect.yMax - local.y;
            slot = Mathf.Clamp(Mathf.FloorToInt((y + 20f) / RowStep), 0, Count);
            BoomboxNativeControls.Stretch(indicator.rectTransform, 0, 0, Mathf.Max(0f, slot * RowStep - 3.5f), 3);
        }
    }

    internal sealed class BoomboxDragRow : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        internal BoomboxPlaylistDrag Owner;
        internal int Index;
        public void OnBeginDrag(PointerEventData data)
        {
            Owner?.Begin(Index, data.position);
        }
        public void OnDrag(PointerEventData data)
        {
            Owner?.Move(data.position);
        }
        public void OnEndDrag(PointerEventData data)
        {
            Owner?.End(data.position);
        }
    }
}
