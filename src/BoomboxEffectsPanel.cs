using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxEffectsPanel : IDisposable
    {
        private sealed class Stepper
        {
            internal Func<float> Read;
            internal Action<float> Write;
            internal float Minimum;
            internal float Maximum;
            internal float Step;
            internal Func<float, string> Format;
            internal Button Less;
            internal Button More;
            internal TMP_Text Value;
        }

        private const float Left = 24f;
        private const float RowHeight = 34f;
        private const float ButtonSize = 34f;
        private const float ValueWidth = 300f;
        private const float Gap = 6f;
        private readonly BoomboxPlugin plugin;
        private readonly RectTransform window;
        private readonly TMP_Text heading;
        private readonly Button effects;
        private readonly Button others;
        private readonly Button variantBack;
        private readonly Button variantNext;
        private readonly TMP_Text variant;
        private readonly Button colorMode;
        private readonly Button reset;
        private readonly BoomboxColorPicker picker;
        private readonly Stepper threshold;
        private readonly Stepper brightness;
        private readonly Stepper amount;
        private (string Language, bool Effects, bool Others, bool Damage, BoomboxMusicEffect Variant,
            float Threshold, float Brightness, Color Color, BoomboxSparkColor? ColorMode, float Amount)? signature;
        private BoomboxEffectPreview preview;
        private readonly RectTransform content;
        private readonly BoomboxVisualVisibility visibility;
        private int page;
        internal bool Alive => window != null;
        private MusicEffectSettings Settings => plugin.Settings.Effect;


        internal BoomboxEffectsPanel(BoomboxPlugin owner, RectTransform parent)
        {
            plugin = owner;
            this.window = BoomboxNativeUI.Rect("BoomboxEffectsWindow", parent);
            BoomboxNativeUI.Place(this.window, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            var window = content = BoomboxNativeUI.Rect("EffectSettingsContent", this.window);
            BoomboxNativeUI.Place(window, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            visibility = new BoomboxVisualVisibility(plugin.Settings, this.window);
            var edit = BoomboxNativeUI.CleanButton(
                "EffectPageEdit",
                this.window,
                BoomboxText.Get("text.effect_settings"),
                () => page = 0
            );
            var show = BoomboxNativeUI.CleanButton(
                "EffectPageVisibility",
                this.window,
                BoomboxText.Get("text.visibility"),
                () => page = 1
            );
            BoomboxNativeUI.Place((RectTransform)edit.transform, 24, 118, 488, 32);
            BoomboxNativeUI.Place((RectTransform)show.transform, 544, 118, 488, 32);
            heading = BoomboxNativeUI.FixedText("EffectsHeading", window, 21, true);
            heading.gameObject.SetActive(false);
            BoomboxNativeUI.Place(heading.rectTransform, Left, 118, 1000, 30);
            effects = BoomboxNativeUI.CleanButton(
            "ShowEffects",
            window,
            "",
            () =>
                {
                    plugin.Settings.ShowVisualEffects.Value = !plugin.Settings.ShowVisualEffects.Value;
                    signature = null;
                }
            );
            BoomboxNativeUI.Place((RectTransform)effects.transform, Left, 160, 430, RowHeight);
            others = BoomboxNativeUI.CleanButton(
            "ShowOthersWaves",
            window,
            "",
            () =>
                {
                    plugin.Settings.ShowOthersWaves.Value = !plugin.Settings.ShowOthersWaves.Value;
                    signature = null;
                }
            );
            BoomboxNativeUI.Place((RectTransform)others.transform, 510, 160, 526, RowHeight);
            variantBack = BoomboxNativeUI.CleanButton("VariantBack", window, "◀", () => Cycle(-1));
            variant = BoomboxNativeUI.FixedText("Variant", window, 16);
            variant.alignment = TextAlignmentOptions.Center;
            variantNext = BoomboxNativeUI.CleanButton("VariantNext", window, "▶", () => Cycle(1));
            Row(variantBack, variant, variantNext, 212);
            threshold = Create(
                "StrikeThreshold",
                262,
                .3f,
                .9f,
                .05f,
                value => Number(value),
                () => Settings.Current.Threshold.Value,
                value => Settings.Current.Threshold.Value = value
            );
            brightness = Create(
                "Brightness",
                304,
                0f,
                3f,
                .05f,
                value => Number(value),
                () => Settings.Current.Brightness.Value,
                value => Settings.Current.Brightness.Value = value
            );
            picker = new BoomboxColorPicker(
            window,
            () => Settings.Current.Color.Value,
            value =>
                {
                    BoomboxConfiguration.UpdatePreview(plugin.Config, () =>
                    {
                        Settings.Current.Color.Value = value;
                        if (Settings.Current.ColorMode != null)
                            Settings.Current.ColorMode.Value = BoomboxSparkColor.Own;
                    });
                    signature = null;
                },
            () =>
                {
                    if (plugin.Config.SaveOnConfigSet)
                        plugin.Config.Save();
                }
            );
            colorMode = BoomboxNativeUI.CleanButton(
            "SparkColor",
            window,
            "",
            () =>
            {
                var entry = Settings.Current.ColorMode;
                if (entry == null)
                    return;
                entry.Value = (BoomboxSparkColor)(((int)entry.Value + 1) % Enum.GetValues(typeof(BoomboxSparkColor))
                    .Length);
                signature = null;
            }
            );
            BoomboxNativeUI.Place((RectTransform)colorMode.transform, Left, 570, 384, RowHeight);
            amount = Create(
                "Amount",
                614,
                .25f,
                2f,
                .05f,
                value => Percent(value),
                () => Settings.Amount.Value,
                value => Settings.Amount.Value = value
            );
            reset = BoomboxNativeUI.CleanButton("ResetEffect", window, "", Reset);
            BoomboxNativeUI.Place((RectTransform)reset.transform, Left, 664, 384, RowHeight);
        }

        internal void Show()
        {
            if (!window.gameObject.activeSelf)
                window.gameObject.SetActive(true);
            content.gameObject.SetActive(page == 0);
            visibility.Show(page == 1);
            if (page != 0)
            {
                preview?.Hide();
                return;
            }
            if (preview == null)
            {
                var player = Player.m_localPlayer;
                var terrain = player != null ? Heightmap.FindHeightmap(player.transform.position) : null;
                if (terrain != null && terrain.m_material != null)
                    preview = new BoomboxEffectPreview(content, terrain.m_material);
            }
            preview?.Present(Settings);
            picker.Sync();
            var current = Settings.Current;
            var next = (Localization.instance.GetSelectedLanguage(), plugin.Settings.ShowVisualEffects.Value,
            plugin.Settings.ShowOthersWaves.Value, Settings.CreatureDamage.Value, Settings.Variant.Value,
            current.Threshold.Value, current.Brightness.Value, current.Color.Value, current.ColorMode?.Value,
            Settings.Amount.Value);
            if (signature.HasValue && signature.Value.Equals(next))
                return;
            signature = next;
            heading.text = BoomboxText.Get("config.Music effect");
            BoomboxNativeUI.Label(reset, BoomboxText.Get("text.reset_effect"));
            BoomboxNativeUI.Label(
                effects,
                BoomboxText.Get("text.in_game_effects") + OnOff(plugin.Settings.ShowVisualEffects.Value)
            );
            BoomboxNativeUI.Label(
                others,
                BoomboxText.Get("text.other_players_effects") + OnOff(plugin.Settings.ShowOthersWaves.Value)
            );
            variant.text = BoomboxText.Get("text.variant_2") + VariantName(Settings.Variant.Value);
            int variants = Enum.GetValues(typeof(BoomboxMusicEffect)).Length;
            variantBack.interactable = variantNext.interactable = variants > 1;
            Show(threshold, Settings.Variant.Value != BoomboxMusicEffect.Pulse);
            if (Settings.Variant.Value != BoomboxMusicEffect.Pulse)
                Refresh(threshold, BoomboxText.Get("text.strike_threshold_2"));
            Refresh(brightness, BoomboxText.Get("text.brightness_2"));
            colorMode.gameObject.SetActive(current.ColorMode != null);
            if (current.ColorMode != null)
                BoomboxNativeUI.Label(
                    colorMode,
                    BoomboxText.Get("text.color_mode") + ColorModeName(current.ColorMode.Value)
                );
            Show(amount, current.UsesAmount);
            if (current.UsesAmount)
                Refresh(amount, BoomboxText.Get("text.amount_2"));
        }

        internal void Hide()
        {
            preview?.Hide();
            if (window != null && window.gameObject.activeSelf)
                window.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            preview?.Dispose();
            picker.Dispose();
            if (window != null)
                UnityEngine.Object.Destroy(window.gameObject);
        }

        private void Cycle(int step)
        {
            int count = Enum.GetValues(typeof(BoomboxMusicEffect)).Length;
            Settings.Variant.Value = (BoomboxMusicEffect)((((int)Settings.Variant.Value + step) % count + count) % count);
            signature = null;
        }

        private void Reset()
        {
            var current = Settings.Current;
            current.Threshold.Value = (float)current.Threshold.DefaultValue;
            current.Brightness.Value = (float)current.Brightness.DefaultValue;
            current.Color.Value = (Color)current.Color.DefaultValue;
            if (current.ColorMode != null)
                current.ColorMode.Value = (BoomboxSparkColor)current.ColorMode.DefaultValue;
            if (current.UsesAmount)
                Settings.Amount.Value = (float)Settings.Amount.DefaultValue;
            signature = null;
        }

        private Stepper Create(
            string name,
            float y,
            float minimum,
            float maximum,
            float step,
            Func<float, string> format,
            Func<float> read,
            Action<float> write,
            float left = Left
        )
        {
            var stepper = new Stepper
            {
                Read = read,
                Write = write,
                Minimum = minimum,
                Maximum = maximum,
                Step = step,
                Format = format
            };
            stepper.Less = BoomboxNativeUI.CleanButton(name + "Less", content, "−", () => Change(stepper, -1));
            stepper.More = BoomboxNativeUI.CleanButton(name + "More", content, "+", () => Change(stepper, 1));
            stepper.Value = BoomboxNativeUI.FixedText(name + "Value", content, 16);
            stepper.Value.alignment = TextAlignmentOptions.Center;
            Row(stepper.Less, stepper.Value, stepper.More, y, left);
            return stepper;
        }

        private static void Row(Button less, TMP_Text value, Button more, float y, float left = Left)
        {
            BoomboxNativeUI.Place((RectTransform)less.transform, left, y, ButtonSize, RowHeight);
            BoomboxNativeUI.Place(value.rectTransform, left + ButtonSize + Gap, y, ValueWidth, RowHeight);
            BoomboxNativeUI.Place(
                (RectTransform)more.transform,
                left + ButtonSize + Gap * 2 + ValueWidth,
                y,
                ButtonSize,
                RowHeight
            );
        }

        private void Change(Stepper stepper, int direction)
        {
            float value = Mathf.Clamp(stepper.Read(), stepper.Minimum, stepper.Maximum);
            stepper.Write(
                Mathf.Clamp(
                    Mathf.Round((value + direction * stepper.Step) / stepper.Step) * stepper.Step,
                    stepper.Minimum,
                    stepper.Maximum
                )
            );
            signature = null;
        }

        private static void Show(Stepper stepper, bool shown)
        {
            stepper.Less.gameObject.SetActive(shown);
            stepper.Value.gameObject.SetActive(shown);
            stepper.More.gameObject.SetActive(shown);
        }

        private static void Refresh(Stepper stepper, string label)
        {
            float value = Mathf.Clamp(stepper.Read(), stepper.Minimum, stepper.Maximum);
            stepper.Value.text = label + stepper.Format(value);
            stepper.Less.interactable = value > stepper.Minimum + 1e-4f;
            stepper.More.interactable = value < stepper.Maximum - 1e-4f;
        }

        private static string Number(float value) => value.ToString(
            "0.00",
            BoomboxText.Culture
        );

        private static string Percent(float value) => Mathf.RoundToInt(value * 100f) + " %";

        private static string OnOff(bool value) => value ? BoomboxText.Get("text.on") : BoomboxText.Get("text.off");

        private static string VariantName(BoomboxMusicEffect value)
        {
            switch (value)
            {
                case BoomboxMusicEffect.Sparks:
                    return BoomboxText.Get("text.sparks");
                case BoomboxMusicEffect.RunesAround:
                    return BoomboxText.Get("text.runes_around");
                case BoomboxMusicEffect.Strike:
                    return BoomboxText.Get("text.strike");
                case BoomboxMusicEffect.FixedCracks:
                    return BoomboxText.Get("text.fixed_cracks");
                case BoomboxMusicEffect.Pulse:
                    return BoomboxText.Get("text.pulse");
                default:
                    throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        private static string ColorModeName(BoomboxSparkColor value)
        {
            switch (value)
            {
                case BoomboxSparkColor.Own:
                    return BoomboxText.Get("text.palette");
                case BoomboxSparkColor.Heat:
                    return BoomboxText.Get("text.loudness");
                default:
                    return BoomboxText.Get("text.frequency_bands");
            }
        }
    }
}
