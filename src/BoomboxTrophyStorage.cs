using System;

namespace VikingBoombox
{
    public static class BoomboxTrophyStorage
    {
        public static int SlotPrice(AudioDeck deck)
        {
            var settings = BoomboxPlugin.Instance.Settings;
            return SlotPrice(
                settings.DecorationSlotPriceStart.Value,
                settings.DecorationSlotPriceStep.Value,
                settings.DecorationSlotPriceCap.Value,
                deck.State.unlockedDecorationSlots.Count
            );
        }
        public static int SlotPrice(int start, int step, int cap, int countBefore)
        {
            int raw = start + step * (countBefore - 1);
            if (raw < 0)
                raw = 0;
            return cap > 0 ? Math.Min(raw, cap) : raw;
        }
        public static bool UnlockSlot(AudioDeck deck, int slot)
        {
            if (!Valid(deck, slot) || deck.State.unlockedDecorationSlots.Contains(slot))
                return Valid(deck, slot);
            int price = SlotPrice(deck);
            var recipe = BoomboxDecorationCatalog.SlotRecipe(price);
            if (!AtStation(recipe))
                return StationFail(recipe);
            if (!BoomboxCraftingRequirements.Spend(Player.m_localPlayer, recipe, "BoomboxDecorationSlot"))
                return Fail();
            deck.State.unlockedDecorationSlots.Add(slot);
            Save(deck, BoomboxText.Get("text.slot_unlocked"));
            return true;
        }
        public static bool UnlockDesign(AudioDeck deck, int slot, string prefab)
        {
            if (!Valid(deck, slot)
            || !BoomboxDecorationCatalog.Eligible(
                BoomboxDecorationCatalog.Prefab(prefab),
                BoomboxDecorationCatalog.Slots[slot].Kind
            ))
                return false;
            if (deck.State.unlockedDecorationPrefabs.Contains(prefab))
                return true;
            if (BoomboxDecorationCatalog.IsPiece(prefab))
            {
                if (!BoomboxDecorationCatalog.Found(BoomboxDecorationCatalog.Prefab(prefab), deck.State))
                {
                    BoomboxPlugin.Instance.Notify("text.the_building_recipe_must_be_known");
                    return false;
                }
                deck.State.unlockedDecorationPrefabs.Add(prefab);
                Save(deck, BoomboxText.Get("text.decoration_design_unlocked"));
                return true;
            }
            var recipe = BoomboxDecorationCatalog.DesignRecipe(prefab);
            if (!AtStation(recipe))
                return StationFail(recipe);
            if (!BoomboxCraftingRequirements.Spend(Player.m_localPlayer, recipe, "BoomboxDecorationDesign"))
                return Fail();
            deck.State.unlockedDecorationPrefabs.Add(prefab);
            Save(deck, BoomboxText.Get("text.decoration_design_unlocked"));
            return true;
        }
        public static bool Install(AudioDeck deck, int slot, string prefab)
        {
            if (!Valid(deck, slot) || !deck.State.unlockedDecorationSlots.Contains(slot)
            || !deck.State.unlockedDecorationPrefabs.Contains(prefab)
            || !BoomboxDecorationCatalog.Eligible(
                BoomboxDecorationCatalog.Prefab(prefab),
                BoomboxDecorationCatalog.Slots[slot].Kind
            ))
                return false;
            var previous = deck.State.decorationPlacements.Find(p => p.slot == slot);
            deck.State.decorationPlacements.RemoveAll(p => p.slot == slot);
            var placement = new BoomboxDecorationPlacement
            {
                slot = slot,
                prefab = prefab,
                effect = (previous == null
                    ? BoomboxDecorationCatalog.Bonus(prefab)
                    : BoomboxDecorationCatalog.Effect(previous)).ToString()
            };
            if (previous != null)
            {
                placement.effectParticles = previous.effectParticles;
                placement.extraParticles = previous.extraParticles;
            }
            deck.State.decorationPlacements.Add(placement);
            Save(deck, BoomboxText.Get("text.decoration_installed"));
            return true;
        }
        public static bool SetEffect(AudioDeck deck, int slot, BoomboxDecorationBonus effect)
        {
            var placement = Valid(deck, slot) ? deck.State.decorationPlacements.Find(p => p.slot == slot) : null;
            if (placement == null)
                return false;
            placement.effect = effect.ToString();
            Save(deck, BoomboxText.Get("text.effect") + BoomboxDecorationCatalog.EffectName(effect));
            return true;
        }
        public static bool ToggleEffectParticles(AudioDeck deck, int slot)
        {
            var placement = Placed(deck, slot);
            if (placement == null)
                return false;
            placement.effectParticles = !placement.effectParticles;
            SaveParticles(
                deck,
                BoomboxText.Get("text.effect_particles") + (placement.effectParticles
                    ? BoomboxText.Get("text.on")
                    : BoomboxText.Get("text.off"))
            );
            return true;
        }
        public static bool NextExtraParticles(AudioDeck deck, int slot)
        {
            var placement = Placed(deck, slot);
            if (placement == null)
                return false;
            var next = (BoomboxExtraParticles)(((int)BoomboxParticleCatalog.Extra(placement) + 1) % Enum.GetValues(
                typeof(BoomboxExtraParticles)
            )
                .Length);
            placement.extraParticles = next.ToString();
            SaveParticles(
                deck,
                BoomboxText.Get("text.extra_particles") + BoomboxParticleCatalog.ExtraName(next)
            );
            return true;
        }
        public static bool Clear(AudioDeck deck, int slot)
        {
            if (!Valid(deck, slot) || deck.State.decorationPlacements.RemoveAll(p => p.slot == slot) == 0)
                return false;
            Save(deck, BoomboxText.Get("text.slot_cleared"));
            return true;
        }
        public static BoomboxDecorationSlot[] SlotData() => BoomboxDecorationCatalog.Slots;
        public static string[] Catalog(
            BoomboxDecorationKind kind
        ) => System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.Select(BoomboxDecorationCatalog.Items(kind), item => item.name)
        );
        public static bool TryVisual(
            string prefab,
            out BoomboxDecorationVisualRecord visual
        ) => BoomboxDecorationCatalog.TryVisual(BoomboxDecorationCatalog.Prefab(prefab), out visual);
        private static bool Valid(
            AudioDeck deck,
            int slot
        ) => deck != null
            && slot >= 0
            && slot < BoomboxDecorationCatalog.Slots.Length
            && BoomboxPlugin.Instance.CanControl(deck);
        internal static bool AtStation(UpgradeRecipe recipe)
        {
            var player = Player.m_localPlayer;
            var station = player == null ? null : BoomboxAmplifier.FindStation(player, recipe);
            return station != null && station.GetLevel(true) >= recipe.StationLevel;
        }
        internal static string StationRequirement(
            UpgradeRecipe recipe
        ) => BoomboxText.Get("text.requires_an_accessible") + BoomboxAmplifier.StationName(recipe.StationPrefab) + " "
            + recipe.StationLevel
            + ".";
        private static bool StationFail(UpgradeRecipe recipe)
        {
            BoomboxPlugin.Instance.Notice = StationRequirement(recipe);
            return false;
        }
        private static bool Fail()
        {
            BoomboxPlugin.Instance.Notify("text.not_enough_materials");
            return false;
        }
        private static void Save(AudioDeck deck, string notice)
        {
            BoomboxItems.Persist(deck);
            deck.TrophyVisuals?.Refresh();
            BoomboxPlugin.Instance.Notice = notice;
            BoomboxPlugin.Instance.Save();
        }
        private static BoomboxDecorationPlacement Placed(AudioDeck deck, int slot) => Valid(deck, slot)
            ? deck.State.decorationPlacements.Find(p => p.slot == slot)
            : null;
        private static void SaveParticles(AudioDeck deck, string notice)
        {
            BoomboxItems.Persist(deck);
            BoomboxPlugin.Instance.Notice = notice;
            BoomboxPlugin.Instance.Save();
        }
    }
}
