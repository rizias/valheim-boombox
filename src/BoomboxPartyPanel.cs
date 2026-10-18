using System;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;

namespace VikingBoombox
{
    internal sealed class BoomboxPartyPanel : IDisposable
    {
        private readonly BoomboxPlugin plugin;
        private readonly RectTransform window;
        private readonly TMP_Text heading, status, summary;
        private readonly UnityEngine.UI.Button worldMode;
        private readonly TMP_Text[] labels = new TMP_Text[6];
        private readonly TMP_InputField[] inputs = new TMP_InputField[6];
        private readonly TMP_InputField[] periods = new TMP_InputField[3];
        private readonly TMP_Text[] periodLabels = new TMP_Text[3];
        internal bool Alive => window != null;

        internal BoomboxPartyPanel(BoomboxPlugin owner, RectTransform parent, bool embedded = false)
        {
            plugin = owner;
            window = BoomboxNativeUI.Rect("BoomboxPartyWindow", parent);
            BoomboxNativeUI.Place(window, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            heading = Line("PartyHeading", 118, 21, true);
            status = Line("PartyControl", 154, 17);
            for (int row = 0; row < 6; row++)
            {
                int index = row;
                labels[row] = BoomboxNativeControls.Line("PartyLabel" + row, window, 17);
                BoomboxNativeUI.Place(labels[row].rectTransform, 24, 204 + row * 54, 470, 36);
                inputs[row] = BoomboxNativeControls.Input("PartyValue" + row, window, 24);
                BoomboxNativeUI.Place((RectTransform)inputs[row].transform, 510, 204 + row * 54, 180, 36);
                inputs[row].onEndEdit.AddListener(value => Change(index, value));
            }
            summary = Line("PartyTotals", 564, 17);
            summary.textWrappingMode = TextWrappingModes.Normal;
            summary.overflowMode = TextOverflowModes.Truncate;
            BoomboxNativeUI.Place(summary.rectTransform, 24, 564, 1000, 180);
            worldMode = BoomboxNativeUI.CleanButton(
            "WorldMusicOnly",
            window,
            "",
            () =>
                {
                    if (!BoomboxAuraNetwork.CanEditBalance)
                        return;
                    plugin.Settings.MusicOnly.Value = !plugin.Settings.MusicOnly.Value;
                    BoomboxAuraNetwork.PublishImmediate();
                }
            );
            BoomboxNativeUI.Place((RectTransform)worldMode.transform, 720, 204, 312, 42);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                periodLabels[i] = BoomboxNativeUI.FixedText("RecoveryPeriodLabel" + i, window, 16);
                BoomboxNativeUI.Place(periodLabels[i].rectTransform, 720, 270 + i * 70, 310, 26);
                periods[i] = BoomboxNativeControls.Input("RecoveryPeriod" + i, window, 18);
                BoomboxNativeUI.Place((RectTransform)periods[i].transform, 720, 298 + i * 70, 180, 34);
                periods[i].onEndEdit.AddListener(
                value =>
                    {
                        if (!BoomboxAuraNetwork.CanEditBalance
                        || !float.TryParse(
                        value.Replace(',', '.'),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out float parsed
                    )
                        || !BoomboxAuraMath.Finite(parsed)
                        || parsed <= 0)
                        {
                            plugin.Notify("text.the_interval_must_be_positive");
                            return;
                        }
                        new[] {
                        plugin.Settings.Health,
                        plugin.Settings.Stamina,
                        plugin.Settings.Eitr
                        }[index].Interval.Value = parsed;
                        BoomboxAuraNetwork.PublishImmediate();
                    }
                );
            }
            if (embedded)
            {
                heading.gameObject.SetActive(false);
                summary.rectTransform.sizeDelta = new Vector2(1000, 116);
                foreach (RectTransform child in window)
                    child.anchoredPosition += new Vector2(0, -54);
            }
        }

        internal void Show()
        {
            if (!window.gameObject.activeSelf)
                window.gameObject.SetActive(true);
            var balance = BoomboxAuraNetwork.Balance;
            bool editable = BoomboxAuraNetwork.CanEditBalance;
            worldMode.interactable = editable && balance != null;
            BoomboxNativeUI.Label(
                worldMode,
                BoomboxText.Get("text.world_gameplay") + (balance?.MusicOnly == true
                    ? BoomboxText.Get("text.off")
                    : BoomboxText.Get("text.on"))
            );
            for (int i = 0; i < 3; i++)
            {
                periodLabels[i].text = BoomboxAuraHud.Name(i + 1) + BoomboxText.Get("text.interval_s");
                periods[i].interactable = editable && balance != null;
                if (!periods[i].isFocused)
                    periods[i].SetTextWithoutNotify(balance == null
                        ? ""
                        : balance.Period[i].ToString("0.##", BoomboxText.Culture));
            }
            heading.text = BoomboxText.Get("text.party_shared_bonuses");
            status.text = editable
                ? BoomboxText.Get("text.world_host_settings_changes_are_saved_immediately")
                : balance == null
                    ? BoomboxText.Get("text.waiting_for_server_settings_2")
                    : BoomboxText.Get("text.controlled_by_the_server_your_personal_audio");
            for (int row = 0; row < 6; row++)
            {
                labels[row].text = row == 0
                    ? BoomboxText.Get("text.radius_from_each_boombox_m")
                    : BoomboxAuraHud.Name(row - 1) + BoomboxText.Get("text.strength_multiplier");
                inputs[row].interactable = editable && balance != null;
                if (!inputs[row].isFocused)
                    inputs[row].SetTextWithoutNotify(balance == null
                        ? ""
                        : (row == 0
                            ? balance.Radius
                            : balance.Strength[row - 1]).ToString(
                            "0.######",
                            BoomboxText.Culture
                        ));
            }
            var aggregate = BoomboxAuraNetwork.For(Player.m_localPlayer);
            summary.text = BoomboxText.Get("text.multiplier_0_disables_that_shared_bonus_for");
            summary.text += "\n" + string.Join(
                " · ",
                Enumerable.Range(0, 5).Select(
                    i => BoomboxAuraHud.Name(i) + ": +" + BoomboxAuraHud.Number(aggregate.Values[i], i)
                )
            );
            summary.text += "\n" + BoomboxText.Get("text.nearby_sources") + aggregate.Contributions.Count;
            if (balance != null)
                summary.text += "\n" + BoomboxText.Get("text.health_stamina_eitr_recovery_intervals")
                    + string.Join(" / ", balance.Period.Select(p => p.ToString("0.##", BoomboxText.Culture)))
                    + BoomboxText.Get("text.s");
        }

        private void Change(int row, string value)
        {
            if (!float.TryParse(
                value.Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsed
            )
                || !BoomboxAuraMath.Finite(parsed)
                || parsed < 0f)
            {
                plugin.Notify("text.enter_a_nonnegative_number");
                return;
            }
            plugin.Settings.Party.Set(row - 1, parsed);
        }

        private TMP_Text Line(string name, float y, float size, bool title = false)
        {
            var line = BoomboxNativeControls.Line(name, window, size, title);
            BoomboxNativeUI.Place(line.rectTransform, 24, y, 1000, 34);
            return line;
        }


        internal void Hide()
        {
            if (window != null && window.gameObject.activeSelf)
                window.gameObject.SetActive(false);
        }
        public void Dispose()
        {
            if (window != null)
                UnityEngine.Object.Destroy(window.gameObject);
        }
    }
}
