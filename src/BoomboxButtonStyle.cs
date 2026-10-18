using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxButtonStyle : MonoBehaviour
    {
        internal Color NormalColor = BoomboxNativeUI.TextColor;
        private Button button;
        private TMP_Text label;
        private void Awake()
        {
            button = GetComponent<Button>();
            label = GetComponentInChildren<TMP_Text>();
        }
        private void LateUpdate()
        {
            if (label == null || button == null)
                return;
            Color color = button.IsInteractable() ? NormalColor : new Color(.55f, .55f, .55f, 1f);
            if (label.color != color)
                label.color = color;
        }
    }
}
