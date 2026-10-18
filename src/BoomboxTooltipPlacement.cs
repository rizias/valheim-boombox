using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxTooltipBelow : MonoBehaviour
    {
        private const float Gap = 6f;
        private static readonly FieldInfo current = AccessTools.Field(typeof(UITooltip), "m_current");
        private static readonly FieldInfo shown = AccessTools.Field(typeof(UITooltip), "m_tooltip");
        private readonly Vector3[] cell = new Vector3[4];
        private readonly Vector3[] panel = new Vector3[4];
        private UITooltip tooltip;

        private void OnEnable()
        {
            tooltip = GetComponent<UITooltip>();
            Canvas.willRenderCanvases += Place;
        }

        private void OnDisable() => Canvas.willRenderCanvases -= Place;

        private void Place()
        {
            if (tooltip == null || !ReferenceEquals(current.GetValue(null), tooltip))
                return;
            var box = shown.GetValue(null) as GameObject;
            if (box == null || box.transform.childCount == 0)
                return;
            ((RectTransform)transform).GetWorldCorners(cell);
            ((RectTransform)box.transform.GetChild(0)).GetWorldCorners(panel);
            box.transform.position += new Vector3(
                cell[0].x - panel[1].x,
                cell[0].y - Gap * transform.lossyScale.y - panel[1].y,
                0
            );
        }
    }
}
