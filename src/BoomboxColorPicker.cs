using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxColorSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        internal Action<Vector2> Pick;
        internal Action Commit;
        public void OnPointerDown(PointerEventData data) => Apply(data);
        public void OnDrag(PointerEventData data) => Apply(data);
        public void OnPointerUp(PointerEventData data) => Commit?.Invoke();
        private void OnDisable() => Commit?.Invoke();
        private void Apply(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left)
                return;
            var rect = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect,
                data.position,
                data.pressEventCamera,
                out var point
            ))
                Pick?.Invoke(new Vector2(
                        Mathf.Clamp01((point.x - rect.rect.xMin) / rect.rect.width),
                        Mathf.Clamp01((point.y - rect.rect.yMin) / rect.rect.height)
                    ));
        }
    }

    internal sealed class BoomboxColorPicker : IDisposable
    {
        private readonly Texture2D square, spectrum;
        private readonly RectTransform marker, hueMarker;
        private readonly Func<Color> read;
        private readonly Action<Color> write;
        private readonly Action commit;
        private bool dirty;
        private Color last;
        private float hue;
        private bool known;
        private readonly TMPro.TMP_Text label;
        private readonly Image swatch;
        internal BoomboxColorPicker(RectTransform parent, Func<Color> read, Action<Color> write, Action commit)
        {
            this.read = read;
            this.write = write;
            this.commit = commit;
            var title = BoomboxNativeUI.FixedText("ColorPickerTitle", parent, 17, true);
            title.text = BoomboxText.Get("text.effect_color");
            BoomboxNativeUI.Place(title.rectTransform, 24, 348, 400, 26);
            square = new Texture2D(
                128,
                128,
                TextureFormat.RGB24,
                false
            )
            {
                name = "Boombox color palette",
                wrapMode = TextureWrapMode.Clamp
            };
            spectrum = new Texture2D(
                1,
                128,
                TextureFormat.RGB24,
                false
            )
            {
                name = "Boombox hue spectrum",
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < 128; y++)
                spectrum.SetPixel(0, y, Color.HSVToRGB(y / 127f, 1, 1));
            spectrum.Apply(false, true);
            var sv = Surface(
            "EffectColorPalette",
            parent,
            24,
            380,
            334,
            144,
            square,
            point =>
                {
                    Write(Color.HSVToRGB(hue, point.x, point.y));
                },
            Commit
            );
            var h = Surface(
            "EffectColorHue",
            parent,
            378,
            380,
            30,
            144,
            spectrum,
            point =>
                {
                    hue = point.y;
                    Color.RGBToHSV(read(), out _, out float s, out float v);
                    Write(Color.HSVToRGB(hue, Mathf.Max(s, .01f), Mathf.Max(v, .01f)));
                    Paint();
                },
            Commit
            );
            marker = BoomboxNativeUI.FixedText("ColorSelection", sv, 22, true).rectTransform;
            marker.GetComponent<TMPro.TMP_Text>().text = "+";
            hueMarker = BoomboxNativeUI.FixedText("HueSelection", h, 20, true).rectTransform;
            hueMarker.GetComponent<TMPro.TMP_Text>().text = "—";
            swatch = BoomboxNativeUI.Image("ColorSwatch", parent, null, Color.white);
            BoomboxNativeUI.Place(swatch.rectTransform, 24, 534, 48, 26);
            label = BoomboxNativeUI.FixedText("ColorHex", parent, 16);
            BoomboxNativeUI.Place(label.rectTransform, 84, 534, 324, 26);
        }
        private static RectTransform Surface(
            string name,
            RectTransform parent,
            float x,
            float y,
            float w,
            float h,
            Texture texture,
            Action<Vector2> pick,
            Action commit
        )
        {
            var rect = BoomboxNativeUI.Rect(name, parent);
            BoomboxNativeUI.Place(rect, x, y, w, h);
            var image = rect.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            var surface = rect.gameObject.AddComponent<BoomboxColorSurface>();
            surface.Pick = pick;
            surface.Commit = commit;
            return rect;
        }
        private void Write(Color value)
        {
            value.a = read().a;
            write(value);
            dirty = true;
            Sync();
        }
        private void Commit()
        {
            if (!dirty)
                return;
            commit();
            dirty = false;
        }
        private void Paint()
        {
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    square.SetPixel(x, y, Color.HSVToRGB(hue, x / 127f, y / 127f));
            square.Apply(false);
        }
        internal void Sync()
        {
            Color color = read();
            if (known && color == last)
                return;
            Color.RGBToHSV(color, out float h, out float s, out float v);
            if (!known || s > .001f && v > .001f)
            {
                if (!known || Mathf.Abs(hue - h) > .0001f)
                {
                    hue = h;
                    Paint();
                }
            }
            known = true;
            last = color;
            swatch.color = color;
            label.text = BoomboxText.Get("text.selected_color");
            BoomboxNativeUI.Place(marker, s * 334 - 7, (1 - v) * 144 - 12, 24, 24);
            BoomboxNativeUI.Place(hueMarker, 0, (1 - hue) * 144 - 11, 30, 24);
        }
        public void Dispose()
        {
            Commit();
            UnityEngine.Object.Destroy(square);
            UnityEngine.Object.Destroy(spectrum);
        }
    }
}
