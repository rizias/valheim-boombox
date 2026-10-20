using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VikingBoombox
{
    internal sealed class BoomboxTrophyPanel : IDisposable
    {
        private readonly BoomboxPlugin plugin;
        private readonly RectTransform window;
        private readonly BoomboxTrophyPreview preview;
        private readonly BoomboxTrophyCatalog catalog;
        private readonly TMP_Text totals;
        private readonly Button movementToggle;
        private readonly Button musicToggle;
        private readonly Image itemIcon;
        private readonly TMP_Text itemName;
        private readonly TMP_Text stateText;
        private readonly Button[] effectButtons;
        private readonly BoomboxPendantParticleButtons particleButtons;
        private readonly BoomboxParticleControls particleControls;
        private readonly GameObject[] costs;
        private readonly TMP_Text station;
        private readonly Button action;
        private readonly Button clear;
        private readonly TMP_Text notice;
        private readonly TMP_Text legend;
        private readonly Button resetView;
        private int frame = -1;
        private float nextRequirements;
        private int selectedSlot;
        private string selectedPrefab;
        private (int Slot, string Prefab, string Language, bool Movement, bool Music, string Particles)? signature;
        private BoomboxVisualSnapshot visualSnapshot;
        private BoomboxAuraBalance presentedBalance;
        private string selectionOwner;
        internal bool Alive => window != null;
        internal BoomboxTrophyPreview Preview => preview;


        internal BoomboxTrophyPanel(BoomboxPlugin owner, RectTransform parent)
        {
            plugin = owner;
            window = BoomboxNativeUI.Rect("BoomboxDecorationsWindow", parent);
            BoomboxNativeUI.Place(window, 0, 0, BoomboxMenuFrame.Width, BoomboxMenuFrame.ContentHeight);
            preview = new BoomboxTrophyPreview(window, 24, 116, 400, 420);
            preview.SlotPicked = slot =>
            {
                catalog.SelectGroupFor(slot);
                SelectSlot(slot);
            };
            resetView = BoomboxNativeControls.TextButton("ResetPreview", window, "", preview.ResetZoom);
            BoomboxNativeUI.Place((RectTransform)resetView.transform, 24, 660, 200, 28);
            legend = BoomboxNativeUI.FixedText("MarkerLegend", window, 14);
            BoomboxNativeUI.Place(legend.rectTransform, 24, 514, 400, 22);
            legend.alignment = TextAlignmentOptions.Center;
            legend.richText = true;
            totals = BoomboxNativeUI.FixedText("Totals", window, 15);
            BoomboxNativeUI.Place(totals.rectTransform, 24, 542, 400, 40);
            movementToggle = BoomboxNativeUI.CleanButton(
            "DecorationMovement",
            window,
            "",
            () =>
                {
                    plugin.Settings.DecorationMovement.Value = !plugin.Settings.DecorationMovement.Value;
                    signature = null;
                }
            );
            BoomboxNativeUI.Place((RectTransform)movementToggle.transform, 24, 590, 200, 32);
            musicToggle = BoomboxNativeUI.CleanButton(
            "DecorationMusicMovement",
            window,
            "",
            () =>
                {
                    plugin.Settings.DecorationMusicMovement.Value = !plugin.Settings.DecorationMusicMovement.Value;
                    signature = null;
                }
            );
            BoomboxNativeUI.Place((RectTransform)musicToggle.transform, 224, 590, 200, 32);
            catalog = new BoomboxTrophyCatalog(window, 436, 116);
            catalog.SlotSelected = SelectSlot;
            catalog.PrefabSelected = prefab =>
            {
                selectedPrefab = prefab;
                signature = null;
            };
            itemIcon = BoomboxNativeUI.Image("SelectedIcon", window, null, Color.white);
            itemIcon.type = Image.Type.Simple;
            itemIcon.preserveAspect = true;
            BoomboxNativeUI.Place(itemIcon.rectTransform, 436, 500, 48, 48);
            itemName = BoomboxNativeUI.FixedText("SelectedName", window, 21, true);
            BoomboxNativeUI.Place(itemName.rectTransform, 492, 498, 544, 26);
            stateText = BoomboxNativeUI.FixedText("SelectedState", window, 15);
            BoomboxNativeUI.Place(stateText.rectTransform, 492, 526, 544, 22);
            effectButtons = new Button[BoomboxDecorationCatalog.Effects.Length];
            for (int i = 0; i < effectButtons.Length; i++)
            {
                var effect = BoomboxDecorationCatalog.Effects[i];
                effectButtons[i] = BoomboxNativeUI.CleanButton(
                "Effect" + effect,
                window,
                BoomboxDecorationCatalog.EffectName(effect),
                () =>
                    {
                        BoomboxTrophyStorage.SetEffect(plugin.Current, selectedSlot, effect);
                        signature = null;
                    }
                );
                effectButtons[i].GetComponentInChildren<TMP_Text>().fontSize = 14;
                BoomboxNativeUI.Place((RectTransform)effectButtons[i].transform, 436 + i * 120, 556, 120, 30);
            }
            particleButtons = new BoomboxPendantParticleButtons(
                plugin,
                window,
                () => selectedSlot,
                () => signature = null
            );
            particleControls = new BoomboxParticleControls(plugin.Settings, window, () => signature = null);
            costs = new GameObject[4];
            for (int i = 0; i < costs.Length; i++)
            {
                costs[i] = UnityEngine.Object.Instantiate(
                    InventoryGui.instance.m_recipeRequirementList[i
                        % InventoryGui.instance.m_recipeRequirementList.Length],
                    window,
                    false
                );
                costs[i].name = "Cost" + i;
                costs[i].SetActive(false);
                BoomboxNativeUI.Place((RectTransform)costs[i].transform, 436 + i * 115, 594, 110, 75);
            }
            station = BoomboxNativeUI.FixedText("StationRequirement", window, 15);
            BoomboxNativeUI.Place(station.rectTransform, 436, 672, 600, 22);
            action = BoomboxNativeUI.CleanButton("DecorationAction", window, "", Act);
            BoomboxNativeUI.Place((RectTransform)action.transform, 436, 698, 228, 44);
            clear = BoomboxNativeUI.CleanButton(
            "ClearSlot",
            window,
            BoomboxText.Get("text.clear"),
            () =>
                {
                    BoomboxTrophyStorage.Clear(plugin.Current, selectedSlot);
                    signature = null;
                }
            );
            BoomboxNativeUI.Place((RectTransform)clear.transform, 672, 702, 228, 36);
            notice = BoomboxNativeUI.FixedText("Notice", window, 14);
            BoomboxNativeUI.Place(notice.rectTransform, 228, 694, 196, 60);
        }

        private void SelectSlot(int slot)
        {
            selectedSlot = slot;
            var deck = plugin.Current;
            var placement = deck?.State.decorationPlacements.FirstOrDefault(p => p.slot == slot);
            if (placement != null)
                selectedPrefab = placement.prefab;
            else if (selectedPrefab != null
            && !BoomboxDecorationCatalog.Eligible(
                BoomboxDecorationCatalog.Prefab(selectedPrefab),
                BoomboxDecorationCatalog.Slots[slot].Kind
            ))
                selectedPrefab = null;
            signature = null;
        }

        internal void Show()
        {
            bool wasHidden = !window.gameObject.activeInHierarchy;
            if (!window.gameObject.activeSelf)
                window.gameObject.SetActive(true);
            if (wasHidden)
                preview.ResetZoom();
            if (frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            Refresh();
        }

        private void Refresh()
        {
            var deck = plugin.Current;
            if (deck == null)
                return;
            if (selectionOwner != deck.State.id)
            {
                selectionOwner = deck.State.id;
                selectedSlot = 0;
                selectedPrefab = deck.State.decorationPlacements.FirstOrDefault(p => p.slot == 0)?.prefab;
                catalog.SelectGroupFor(0);
                signature = null;
            }
            var slot = BoomboxDecorationCatalog.Slots[selectedSlot];
            if (Time.unscaledTime >= nextRequirements)
            {
                nextRequirements = Time.unscaledTime + .15f;
                ConfigureAction(deck, slot);
            }
            preview.Present(deck, selectedSlot, catalog.GroupSlots);
            catalog.Refresh(deck.State, selectedSlot, selectedPrefab);
            notice.text = (plugin.Notice);
            var next = (selectedSlot, selectedPrefab, Localization.instance.GetSelectedLanguage(),
            plugin.Settings.DecorationMovement.Value, plugin.Settings.DecorationMusicMovement.Value,
            particleControls.Signature);
            var balance = BoomboxAuraNetwork.Balance;
            if (signature.HasValue && signature.Value.Equals(next) && ReferenceEquals(balance, presentedBalance)
                && visualSnapshot != null && visualSnapshot.Matches(deck.State))
                return;
            presentedBalance = balance;
            signature = next;
            visualSnapshot = new BoomboxVisualSnapshot(deck.State);
            BoomboxNativeUI.Label(resetView, BoomboxText.Get("text.reset_view"));
            BoomboxNativeUI.Label(clear, BoomboxText.Get("text.clear"));
            legend.text = Dot(BoomboxTrophyPreview.LockedColor) + BoomboxText.Get("text.locked") + "   "
                + Dot(BoomboxTrophyPreview.OpenColor)
                + BoomboxText.Get("text.empty")
                + "   "
                + Dot(BoomboxTrophyPreview.FilledColor)
                + BoomboxText.Get("text.occupied");
            BoomboxNativeUI.Label(
                movementToggle,
                BoomboxText.Get("text.movement") + (plugin.Settings.DecorationMovement.Value
                    ? BoomboxText.Get("text.on")
                    : BoomboxText.Get("text.off"))
            );
            BoomboxNativeUI.Label(
                musicToggle,
                BoomboxText.Get("text.from_music") + (plugin.Settings.DecorationMusicMovement.Value
                    ? BoomboxText.Get("text.on")
                    : BoomboxText.Get("text.off"))
            );
            var placement = deck.State.decorationPlacements.FirstOrDefault(p => p.slot == selectedSlot);
            bool unlockedSlot = deck.State.unlockedDecorationSlots.Contains(selectedSlot);
            string shownPrefab = selectedPrefab ?? placement?.prefab;
            itemIcon.sprite = shownPrefab == null ? null : BoomboxDecorationCatalog.Icon(shownPrefab);
            itemIcon.enabled = itemIcon.sprite != null;
            itemName.text = BoomboxText.Get("text.slot") + slot.Number + " · "
                + (slot.Kind == BoomboxDecorationKind.Figurine
                    ? BoomboxText.Get("text.stand")
                    : slot.Upper
                        ? BoomboxText.Get("text.upper_pendant")
                        : BoomboxText.Get("text.lower_pendant"))
                + (shownPrefab == null
                    ? ""
                    : " · " + BoomboxDecorationCatalog.Name(BoomboxDecorationCatalog.Prefab(shownPrefab)));
            string slotState = !unlockedSlot
                ? BoomboxText.Get("text.slot_locked")
                : placement == null
                    ? BoomboxText.Get("text.slot_empty")
                    : BoomboxText.Get("text.installed")
                        + BoomboxDecorationCatalog.Name(BoomboxDecorationCatalog.Prefab(placement.prefab));
            string viewState = shownPrefab == null || placement?.prefab == shownPrefab
                ? ""
                : deck.State.unlockedDecorationPrefabs.Contains(shownPrefab)
                    ? BoomboxText.Get("text.design_unlocked")
                    : BoomboxText.Get("text.design_locked");
            stateText.text = slotState + viewState;
            for (int i = 0; i < effectButtons.Length; i++)
            {
                effectButtons[i].gameObject.SetActive(placement != null);
                if (placement != null)
                    BoomboxNativeUI.Select(
                        effectButtons[i],
                        BoomboxDecorationCatalog.Effect(placement) == BoomboxDecorationCatalog.Effects[i]
                    );
            }
            particleButtons.Refresh(placement);
            particleControls.Refresh();
            var sum = BoomboxDecorationCatalog.Totals(deck.State);
            totals.text = balance == null
                ? BoomboxText.Get("text.waiting_for_server_settings")
                : BoomboxText.Get("text.wave_damage_2")
                    + Percent(BoomboxAuraMath.DecorationAmount(0, sum.Damage, balance) * balance.Strength[0])
                    + " · "
                    + BoomboxText.Get("text.speed_2")
                    + Percent(BoomboxAuraMath.DecorationAmount(4, sum.Speed, balance) * balance.Strength[4])
                    + " · "
                    + RecoveryText(BoomboxDecorationBonus.Health, sum.Health, balance)
                    + "\n"
                    + RecoveryText(BoomboxDecorationBonus.Stamina, sum.Stamina, balance)
                    + " · "
                    + RecoveryText(BoomboxDecorationBonus.Eitr, sum.Eitr, balance);
            ConfigureAction(deck, slot);
            clear.interactable = placement != null;
        }

        private void ConfigureAction(AudioDeck deck, BoomboxDecorationSlot slot)
        {
            UpgradeRecipe recipe = null;
            string name = null;
            string label;
            var placement = deck.State.decorationPlacements.FirstOrDefault(p => p.slot == slot.Index);
            if (!deck.State.unlockedDecorationSlots.Contains(slot.Index))
            {
                recipe = BoomboxDecorationCatalog.SlotRecipe(BoomboxTrophyStorage.SlotPrice(deck));
                name = "BoomboxDecorationSlot";
                label = BoomboxText.Get("text.unlock_slot");
            }
            else if (selectedPrefab == null)
                label = BoomboxText.Get("text.select_a_design");
            else if (!deck.State.unlockedDecorationPrefabs.Contains(selectedPrefab)
                && BoomboxDecorationCatalog.IsPiece(selectedPrefab))
                label = BoomboxText.Get("text.unlock_design");
            else if (!deck.State.unlockedDecorationPrefabs.Contains(selectedPrefab))
            {
                recipe = BoomboxDecorationCatalog.DesignRecipe(selectedPrefab);
                name = "BoomboxDecorationDesign";
                label = BoomboxText.Get("text.unlock_design");
            }
            else if (placement != null && placement.prefab == selectedPrefab)
                label = BoomboxText.Get("text.replace");
            else
                label = placement == null ? BoomboxText.Get("text.install") : BoomboxText.Get("text.replace");
            bool atStation = recipe == null || BoomboxTrophyStorage.AtStation(recipe);
            bool pieceDesign = recipe == null
                && selectedPrefab != null
                && deck.State.unlockedDecorationSlots.Contains(slot.Index)
                && !deck.State.unlockedDecorationPrefabs.Contains(selectedPrefab);
            station.text = pieceDesign
                ? BoomboxText.Get("text.the_building_recipe_is_known_the_design")
                : recipe == null
                    ? ""
                    : atStation
                        ? ""
                        : BoomboxTrophyStorage.StationRequirement(recipe);
            BoomboxNativeUI.Label(action, label);
            action.interactable = recipe == null
                ? selectedPrefab != null && (placement == null || placement.prefab != selectedPrefab)
                : atStation && BoomboxCraftingRequirements.CanAfford(Player.m_localPlayer, recipe, name);
            foreach (var cost in costs)
                cost.SetActive(recipe != null);
            if (recipe != null)
                BoomboxCraftingRequirements.Draw(costs, recipe, name);
        }

        private void Act()
        {
            var deck = plugin.Current;
            if (deck == null)
                return;
            if (!deck.State.unlockedDecorationSlots.Contains(selectedSlot))
                BoomboxTrophyStorage.UnlockSlot(deck, selectedSlot);
            else if (selectedPrefab != null && !deck.State.unlockedDecorationPrefabs.Contains(selectedPrefab))
                BoomboxTrophyStorage.UnlockDesign(deck, selectedSlot, selectedPrefab);
            else if (selectedPrefab != null)
                BoomboxTrophyStorage.Install(deck, selectedSlot, selectedPrefab);
            signature = null;
        }

        private string RecoveryText(BoomboxDecorationBonus bonus, int count, BoomboxAuraBalance balance)
        {
            string label = bonus == BoomboxDecorationBonus.Health
                ? BoomboxText.Get("text.health_regen")
                : bonus == BoomboxDecorationBonus.Stamina
                    ? BoomboxText.Get("text.stamina_regen")
                    : BoomboxText.Get("text.eitr_regen");
            int index = (int)bonus;
            float amount = BoomboxAuraMath.DecorationAmount(index, count, balance) * balance.Strength[index];
            return label + " +" + Number(amount) + " / " + Number(balance.Period[index - 1]) + BoomboxText.Get("text.s");
        }

        private static string Dot(Color color) => "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">●</color> ";
        private static string Percent(float value) => (value * 100f).ToString("0.##", BoomboxText.Culture) + "%";
        private static string Number(float value) => value.ToString("0.##", BoomboxText.Culture);
        internal void Hide()
        {
            preview.Hide();
            if (window != null && window.gameObject.activeSelf)
                window.gameObject.SetActive(false);
        }
        public void Dispose()
        {
            preview.Dispose();
            if (window != null)
                UnityEngine.Object.Destroy(window.gameObject);
        }
    }
}
