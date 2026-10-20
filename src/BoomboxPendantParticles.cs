using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace VikingBoombox
{
    public sealed class BoomboxPendantParticles : MonoBehaviour
    {
        private sealed class Pendant
        {
            internal string Signature;
            internal BoomboxParticleEmitter Effect;
            internal BoomboxParticleEmitter Extra;
            internal BoomboxProductDrops Product;

            internal void Destroy()
            {
                Effect?.Destroy();
                Extra?.Destroy();
                Product?.Destroy();
                Effect = Extra = null;
                Product = null;
            }
        }

        private const float CheckInterval = .2f;
        private const float DefaultCoinInterval = 2f;
        private const float DefaultCoinLifetime = 4f;
        private readonly Dictionary<int, Pendant> pendants = new Dictionary<int, Pendant>();
        private readonly List<BoomboxPendantParticleAnchor> anchors = new List<BoomboxPendantParticleAnchor>();
        private readonly HashSet<int> present = new HashSet<int>();
        private DeckState state;
        private DeckState source;
        private GameObject model;
        private float nextCheck;

        internal void Initialize(DeckState deckState, GameObject visual)
        {
            state = deckState;
            model = visual;
        }

        internal void SetState(DeckState deckState) => state = deckState;

        internal void SetSource(DeckState deckState) => source = deckState;

        private void LateUpdate()
        {
            BoomboxParticleAssets.Refresh();
            foreach (var pendant in pendants.Values)
            {
                pendant.Effect?.Update();
                pendant.Extra?.Update();
                pendant.Product?.Update();
            }
            if (model == null || Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + CheckInterval;
            Synchronize();
        }

        private void Synchronize()
        {
            var settings = BoomboxPlugin.Instance?.Settings;
            var placements = (source ?? state)?.decorationPlacements;
            anchors.Clear();
            present.Clear();
            if (settings != null
                && settings.ShowVisualEffects.Value
                && settings.DecorationParticles.Value
                && placements != null)
                model.GetComponentsInChildren(false, anchors);
            BoomboxParticleSet set = null;
            try
            {
                if (anchors.Count > 0)
                    set = BoomboxParticleCatalog.Set;
            }
            catch (Exception error)
            {
                BoomboxPlugin.Instance.LogError(BoomboxText.Get("text.pendant_particles_are_disabled") + error.Message);
                enabled = false;
                return;
            }
            foreach (var anchor in anchors)
            {
                var placement = placements.FirstOrDefault(p => p != null && p.slot == anchor.Slot);
                if (placement == null)
                    continue;
                present.Add(anchor.Slot);
                Apply(set, anchor, placement, settings);
            }
            foreach (int slot in pendants.Keys.Where(slot => !present.Contains(slot)).ToArray())
            {
                pendants[slot].Destroy();
                pendants.Remove(slot);
            }
        }

        private void Apply(
            BoomboxParticleSet set,
            BoomboxPendantParticleAnchor anchor,
            BoomboxDecorationPlacement placement,
            BoomboxSettings settings
        )
        {
            if (!pendants.TryGetValue(anchor.Slot, out var pendant))
                pendants[anchor.Slot] = pendant = new Pendant();
            var extra = BoomboxParticleCatalog.Extra(placement);
            string effectVariant = placement.effectParticles
                ? set.effects[BoomboxDecorationCatalog.Effect(placement)]
                : null;
            string extraVariant = extra == BoomboxExtraParticles.None ? null : set.extras[extra];
            float scale = anchor.BodySize / set.referenceBodySize * anchor.transform.lossyScale.x;
            var products = BoomboxDecorationCatalog.Products(BoomboxDecorationCatalog.Prefab(placement.prefab));
            float coinInterval = BoomboxSettings.PositiveOr(settings.CoinInterval.Value, DefaultCoinInterval),
                coinLifetime = BoomboxSettings.PositiveOr(settings.CoinLifetime.Value, DefaultCoinLifetime);
            float? interval = extra == BoomboxExtraParticles.Coins || products != null ? coinInterval : (float?)null;
            float? lifetime = extra == BoomboxExtraParticles.Coins || products != null ? coinLifetime : (float?)null;
            float amount = BoomboxParticleLook.Amount;
            string signature = string.Join(
                "|",
                anchor.GetInstanceID(),
                effectVariant,
                extraVariant,
                scale.ToString("0.###", CultureInfo.InvariantCulture),
                interval?.ToString("R", CultureInfo.InvariantCulture),
                lifetime?.ToString("R", CultureInfo.InvariantCulture),
                amount.ToString("R", CultureInfo.InvariantCulture),
                products == null ? null : string.Join(",", products)
            );
            if (pendant.Signature == signature)
                return;
            pendant.Destroy();
            pendant.Signature = signature;
            try
            {
                if (effectVariant != null)
                    pendant.Effect = BoomboxParticleBuilder.Build(
                        BoomboxParticleCatalog.Variant(effectVariant),
                        "EffectParticles",
                        anchor.transform,
                        new BoomboxParticleTuning { Scale = scale, Boombox = model.transform, Amount = amount }
                    );
                if (extraVariant != null)
                    pendant.Extra = BoomboxParticleBuilder.Build(
                        BoomboxParticleCatalog.Variant(extraVariant),
                        "ExtraParticles",
                        anchor.transform,
                        new BoomboxParticleTuning
                        {
                            Scale = scale,
                            Boombox = model.transform,
                            BurstInterval = interval,
                            CoinLifetime = lifetime,
                            Amount = amount
                        }
                    );
                if (products != null)
                    pendant.Product = BoomboxProductDrops.Build(
                        BoomboxParticleCatalog.Variant(set.extras[BoomboxExtraParticles.Coins]),
                        anchor.transform,
                        new BoomboxParticleTuning { Scale = scale, Boombox = model.transform, Amount = amount },
                        products,
                        coinInterval,
                        coinLifetime
                    );
            }
            catch (Exception error)
            {
                pendant.Destroy();
                BoomboxPlugin.Instance.LogError(
                    BoomboxText.Get("text.pendant_particles_in_slot")
                        + BoomboxDecorationCatalog.Slots[anchor.Slot].Number
                        + BoomboxText.Get("text.were_not_created") + error.Message
                );
            }
        }

        private void OnDestroy()
        {
            foreach (var pendant in pendants.Values)
                pendant.Destroy();
            pendants.Clear();
        }
    }
}
