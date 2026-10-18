using TMPro;
using UnityEngine;

namespace VikingBoombox
{
    [DefaultExecutionOrder(20000)]
    public sealed class BoomboxMarquee : MonoBehaviour
    {
        private TextMeshPro label;
        private string shown;
        private float started;
        public float Offset
        {
            get;
            private set;
        }
        public void Sync(BoomboxMarquee source)
        {
            if (source != null)
            {
                started = source.started;
                shown = source.shown;
            }
        }

        private void OnEnable()
        {
            label = GetComponent<TextMeshPro>();
            if (label != null)
                label.OnPreRenderText += Clip;
        }

        private void OnDisable()
        {
            if (label != null)
                label.OnPreRenderText -= Clip;
        }

        private void LateUpdate()
        {
            if (label == null)
                label = GetComponent<TextMeshPro>();
            if (label == null || !label.isActiveAndEnabled)
                return;
            if (shown != label.text)
            {
                shown = label.text;
                started = Time.unscaledTime;
            }
            float width = label.rectTransform.rect.width;
            float distance = Mathf.Max(0, label.preferredWidth - width);
            Offset = 0;
            var settings = BoomboxPlugin.Instance.Settings;
            float speed = width * BoomboxSettings.Nonnegative(settings.DisplayScrollSpeed.Value);
            float pause = BoomboxSettings.Nonnegative(settings.DisplayScrollPause.Value);
            float offset = 0;
            if (speed > 0)
            {
                float travel = distance / speed;
                float cycle = travel * 2 + pause * 2;
                float phase = cycle > 0 ? (Time.unscaledTime - started) % cycle : 0;
                offset = phase <= travel + pause
                    ? Mathf.Clamp(phase - pause, 0, travel) * speed
                    : distance - Mathf.Clamp(phase - travel - pause * 2, 0, travel) * speed;
            }
            Offset = offset;
            label.ForceMeshUpdate();
        }

        private void Clip(TMP_TextInfo info)
        {
            float offset = Offset;
            float left = label.rectTransform.rect.xMin;
            float right = label.rectTransform.rect.xMax;
            for (int index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible)
                    continue;
                var mesh = info.meshInfo[character.materialReferenceIndex];
                int v = character.vertexIndex;
                var a = mesh.vertices[v];
                var b = mesh.vertices[v + 1];
                var c = mesh.vertices[v + 2];
                var d = mesh.vertices[v + 3];
                var ua = mesh.uvs0[v];
                var ub = mesh.uvs0[v + 1];
                var uc = mesh.uvs0[v + 2];
                var ud = mesh.uvs0[v + 3];
                float low = a.x - offset, high = d.x - offset;
                if (high <= left || low >= right || high <= low)
                {
                    for (int corner = 0; corner < 4; corner++)
                        mesh.vertices[v + corner] = Vector3.zero;
                    continue;
                }
                float from = Mathf.Clamp01((left - low) / (high - low));
                float to = Mathf.Clamp01((right - low) / (high - low));
                var shift = Vector3.right * offset;
                mesh.vertices[v] = Vector3.Lerp(a, d, from) - shift;
                mesh.vertices[v + 1] = Vector3.Lerp(b, c, from) - shift;
                mesh.vertices[v + 2] = Vector3.Lerp(b, c, to) - shift;
                mesh.vertices[v + 3] = Vector3.Lerp(a, d, to) - shift;
                mesh.uvs0[v] = Vector4.Lerp(ua, ud, from);
                mesh.uvs0[v + 1] = Vector4.Lerp(ub, uc, from);
                mesh.uvs0[v + 2] = Vector4.Lerp(ub, uc, to);
                mesh.uvs0[v + 3] = Vector4.Lerp(ua, ud, to);
            }
        }
    }
}
