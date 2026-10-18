using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxParticleControls
    {
        private sealed class Control
        {
            internal ConfigEntry<float> Entry;
            internal float Minimum;
            internal float Maximum;
            internal Button Less;
            internal Button More;
            internal TMP_Text Value;

            internal float Clamped => Mathf.Clamp(Entry.Value, Minimum, Maximum);
        }

        private const float Top = 626f;
        private const float BobTop = 694f;
        private const float ButtonSize = 28f;
        private const float ValueWidth = 136f;
        private const float Gap = 4f;
        private const float TextSize = 14f;
        private readonly Control brightness;
        private readonly Control amount;
        private readonly Control bob;


        internal BoomboxParticleControls(BoomboxSettings settings, RectTransform window, Action changed)
        {
            brightness = Create(
                "ParticleBrightness",
                settings.ParticleBrightness,
                BoomboxParticleLook.Minimum,
                window,
                24f,
                Top,
                changed
            );
            amount = Create(
                "ParticleAmount",
                settings.ParticleAmount,
                BoomboxParticleLook.Minimum,
                window,
                224f,
                Top,
                changed
            );
            bob = Create("HeadBeatBob", settings.HeadBeatBob, 0f, window, 24f, BobTop, changed);
        }

        internal string Signature => brightness.Entry.Value + ":" + amount.Entry.Value + ":" + bob.Entry.Value;

        internal void Refresh()
        {
            Show(brightness, BoomboxText.Get("text.brightness_2"));
            Show(amount, BoomboxText.Get("text.amount_2"));
            Show(bob, BoomboxText.Get("text.head_bob"));
        }

        private static void Show(Control control, string label)
        {
            float value = control.Clamped;
            control.Value.text = label + Mathf.RoundToInt(value * 100f) + " %";
            control.Less.interactable = value > control.Minimum;
            control.More.interactable = value < control.Maximum;
        }

        private static Control Create(
            string name,
            ConfigEntry<float> entry,
            float minimum,
            RectTransform window,
            float x,
            float y,
            Action changed
        )
        {
            var control = new Control { Entry = entry, Minimum = minimum, Maximum = BoomboxParticleLook.Maximum };
            control.Less = BoomboxNativeUI.CleanButton(
            name + "Less",
            window,
            "−",
            () =>
                {
                    Change(control, -BoomboxParticleLook.Step);
                    changed();
                }
            );
            control.More = BoomboxNativeUI.CleanButton(
            name + "More",
            window,
            "+",
            () =>
                {
                    Change(control, BoomboxParticleLook.Step);
                    changed();
                }
            );
            control.Value = BoomboxNativeUI.FixedText(name + "Value", window, TextSize);
            control.Value.alignment = TextAlignmentOptions.Center;
            BoomboxNativeUI.Place((RectTransform)control.Less.transform, x, y, ButtonSize, ButtonSize);
            BoomboxNativeUI.Place(control.Value.rectTransform, x + ButtonSize + Gap, y, ValueWidth, ButtonSize);
            BoomboxNativeUI.Place(
                (RectTransform)control.More.transform,
                x + ButtonSize + Gap + ValueWidth + Gap,
                y,
                ButtonSize,
                ButtonSize
            );
            return control;
        }

        private static void Change(Control control, float step)
        => control.Entry.Value = Mathf.Clamp(
            Mathf.Round((control.Clamped + step) / BoomboxParticleLook.Step) * BoomboxParticleLook.Step,
            control.Minimum,
            control.Maximum
        );
    }
}
